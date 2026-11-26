using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PcPanel.Core;

namespace PcPanel.Services.Screen
{
    /// <summary>
    /// Shows and controls the phone on the PC at up to 60 fps, using scrcpy (open source, by Genymobile)
    /// as the engine. Its window is embedded in PC Panel's own phone window (UI/MirrorWindow.cs).
    /// Needs USB debugging on the phone; can switch to Wi-Fi after the first USB connection.
    /// </summary>
    public sealed class PhoneMirror
    {
        public static readonly PhoneMirror I = new PhoneMirror();

        public enum St { Off, Preparing, Running, Error }
        public St State { get; private set; } = St.Off;
        public string StepKey { get; private set; } = "";
        public double StepProgress { get; private set; } = -1;
        public string ErrorKey { get; private set; } = "";
        public string Serial { get; private set; }
        public string Model { get; private set; } = "";
        public bool Wireless { get; private set; }
        public int Fps { get; private set; }
        public int VideoW { get; private set; }
        public int VideoH { get; private set; }

        public event Action Changed;
        /// <summary>Raised on any thread when the video size changes (phone rotated).</summary>
        public event Action<int, int> VideoSize;
        /// <summary>The scrcpy window handle once it exists.</summary>
        public event Action<IntPtr> WindowReady;
        public event Action Exited;

        Process _scrcpy;
        Process _shell;
        int _gen;

        void Raise() { try { Changed?.Invoke(); } catch { } }
        void Set(St s, string step = null, double p = -1, string err = null)
        {
            State = s;
            if (step != null) StepKey = step;
            StepProgress = p;
            ErrorKey = err ?? "";
            if (s != St.Running) Fps = 0;
            Raise();
        }

        static readonly Regex FpsRx = new Regex(@"(\d+)\s*fps", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex TexRx = new Regex(@"Texture:\s*(\d+)x(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public async Task Start()
        {
            int gen = ++_gen;
            KillProcesses();
            try
            {
                if (!ScreenTools.HasScrcpy)
                {
                    Set(St.Preparing, "pm.step.download", 0);
                    void P(string what, double prog, bool done, string err) { if (what == "scrcpy" && gen == _gen) Set(St.Preparing, "pm.step.download", prog); }
                    ScreenTools.Progress += P;
                    bool ok;
                    try { ok = await ScreenTools.EnsureScrcpy(); } finally { ScreenTools.Progress -= P; }
                    if (gen != _gen) return;
                    if (!ok) { Set(St.Error, err: "pm.err.download"); return; }
                }

                Set(St.Preparing, "pm.step.device");
                Adb.Device d = null;
                for (int i = 0; i < 3 && d == null; i++)
                {
                    d = await Adb.Pick();
                    if (d == null) await Task.Delay(700);
                }
                if (gen != _gen) return;
                if (d == null) { Set(St.Error, err: "pm.err.noDevice"); return; }
                if (d.State == "unauthorized") { Set(St.Error, err: "pm.err.unauthorized"); return; }
                if (!d.Ready) { Set(St.Error, err: "pm.err.offline"); return; }
                Serial = d.Serial;
                Model = d.Model;
                Wireless = d.Wireless;

                Set(St.Preparing, "pm.step.start");
                var st = App.Settings;
                string br = st.PmQuality switch { "mid" => "8M", "ultra" => "24M", _ => "16M" };
                string args = $"-s {d.Serial} --window-title \"PC Panel Phone\" --window-borderless --window-x -32000 --window-y -32000 " +
                              $"--max-fps 60 --video-codec h264 --video-bit-rate {br} --print-fps " +
                              (st.PmAudio ? "--audio-codec opus " : "--no-audio ") +
                              (st.PmScreenOff ? "--turn-screen-off " : "") +
                              (st.PmAwake ? "--stay-awake " : "") +
                              "--shortcut-mod lalt,ralt";
                var p = Process.Start(new ProcessStartInfo(ScreenTools.Scrcpy, args)
                {
                    CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = ScreenTools.ScrcpyDir
                });
                _scrcpy = p;
                try { p.PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }
                string lastErr = "";
                DataReceivedEventHandler onLine = (s, e) =>
                {
                    if (e.Data == null) return;
                    var line = e.Data;
                    var t = TexRx.Match(line);
                    if (t.Success)
                    {
                        VideoW = int.Parse(t.Groups[1].Value); VideoH = int.Parse(t.Groups[2].Value);
                        try { VideoSize?.Invoke(VideoW, VideoH); } catch { }
                        return;
                    }
                    var f = FpsRx.Match(line);
                    if (f.Success && line.Contains("INFO", StringComparison.OrdinalIgnoreCase)) { Fps = int.Parse(f.Groups[1].Value); Raise(); return; }
                    if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase)) { lastErr = line; Log.Write("scrcpy: " + line); }
                };
                p.OutputDataReceived += onLine;
                p.ErrorDataReceived += onLine;
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.EnableRaisingEvents = true;
                p.Exited += (s, e) =>
                {
                    if (gen != _gen) return;
                    if (State == St.Running) Set(St.Off);
                    else Set(St.Error, err: lastErr.Contains("INJECT_EVENTS", StringComparison.OrdinalIgnoreCase) ? "pm.err.xiaomi" : "pm.err.start");
                    try { Exited?.Invoke(); } catch { }
                };

                // wait for the SDL window
                IntPtr hwnd = IntPtr.Zero;
                for (int i = 0; i < 100 && hwnd == IntPtr.Zero && !p.HasExited && gen == _gen; i++)
                {
                    await Task.Delay(100);
                    try { p.Refresh(); hwnd = p.MainWindowHandle; } catch { }
                }
                if (gen != _gen) return;
                if (hwnd == IntPtr.Zero) { if (!p.HasExited) { try { p.Kill(true); } catch { } } Set(St.Error, err: "pm.err.start"); return; }

                Set(St.Running, "");
                try { WindowReady?.Invoke(hwnd); } catch { }
                _ = StartShell(d.Serial);
            }
            catch (Exception ex)
            {
                Log.Write("Mirror: " + ex);
                if (gen == _gen) Set(St.Error, err: "pm.err.start");
            }
        }

        public void Stop()
        {
            _gen++;
            KillProcesses();
            Set(St.Off, "");
        }

        void KillProcesses()
        {
            try { if (_scrcpy != null && !_scrcpy.HasExited) _scrcpy.Kill(true); } catch { }
            try { if (_shell != null && !_shell.HasExited) _shell.Kill(true); } catch { }
            _scrcpy = null; _shell = null;
        }

        // ------------------------------------------------------------------ buttons (a persistent adb shell = quick)
        async Task StartShell(string serial)
        {
            await Task.Yield();
            try
            {
                _shell = Process.Start(new ProcessStartInfo(ScreenTools.Adb, $"-s {serial} shell")
                {
                    CreateNoWindow = true, UseShellExecute = false, RedirectStandardInput = true,
                    RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = ScreenTools.ScrcpyDir
                });
                _shell.OutputDataReceived += (s, e) => { };
                _shell.ErrorDataReceived += (s, e) => { };
                _shell.BeginOutputReadLine();
                _shell.BeginErrorReadLine();
            }
            catch (Exception ex) { Log.Write("adb shell: " + ex.Message); }
        }

        void Shell(string cmd)
        {
            try
            {
                if (_shell != null && !_shell.HasExited) { _shell.StandardInput.WriteLine(cmd); _shell.StandardInput.Flush(); return; }
            }
            catch { }
            if (Serial != null) _ = Adb.Exec($"-s {Serial} shell {cmd}", 5000);
        }

        public void Key(string k)
        {
            int code = k switch
            {
                "back" => 4, "home" => 3, "recents" => 187, "power" => 26,
                "volup" => 24, "voldown" => 25, "mute" => 164, "notif" => -1, _ => 0
            };
            if (code == -1) { Shell("cmd statusbar expand-notifications"); return; }
            if (code > 0) Shell("input keyevent " + code);
        }

        public void Rotate()
        {
            // turn auto-rotate off and flip between portrait and landscape
            bool land = VideoW > VideoH;
            Shell("settings put system accelerometer_rotation 0; settings put system user_rotation " + (land ? 0 : 1));
        }

        public void AutoRotate() => Shell("settings put system accelerometer_rotation 1");

        /// <summary>Saves a full-quality screenshot of the phone into Pictures\PC Panel and returns its path.</summary>
        public async Task<string> Screenshot()
        {
            if (Serial == null) return null;
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PC Panel");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "Phone " + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss") + ".png");
                var psi = new ProcessStartInfo(ScreenTools.Adb, $"-s {Serial} exec-out screencap -p")
                {
                    CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, WorkingDirectory = ScreenTools.ScrcpyDir
                };
                using var p = Process.Start(psi);
                using (var fs = File.Create(file)) await p.StandardOutput.BaseStream.CopyToAsync(fs);
                await p.WaitForExitAsync();
                if (new FileInfo(file).Length < 100) { File.Delete(file); return null; }
                return file;
            }
            catch (Exception ex) { Log.Write("Phone screenshot: " + ex.Message); return null; }
        }

        /// <summary>Switch this USB phone to wireless debugging so the cable can be unplugged.</summary>
        public async Task<bool> GoWireless(string phoneIp)
        {
            if (Serial == null || Wireless) return Wireless;
            var target = await Adb.GoWireless(Serial, phoneIp);
            return target != null;
        }
    }
}

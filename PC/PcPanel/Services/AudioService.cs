using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using PcPanel.Core;
using NAudio.CoreAudioApi;

namespace PcPanel.Services
{
    /// <summary>System volume + peak meter. All members must be called on the UI thread.</summary>
    public sealed class AudioService
    {
        public static readonly AudioService I = new AudioService();

        MMDeviceEnumerator _en;
        MMDevice _dev;
        public float Level { get; private set; }
        public bool Muted { get; private set; }

        /// <summary>Raised on the UI thread when the volume changes (from any source).</summary>
        public event Action VolumeChanged;

        public void Init()
        {
            try
            {
                _en = new MMDeviceEnumerator();
                Ensure();
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                t.Tick += (s, e) => Ensure();
                t.Start();
            }
            catch (Exception ex) { Log.Write("Audio init: " + ex.Message); }
        }

        void Ensure()
        {
            try
            {
                var d = _en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (_dev != null && d.ID == _dev.ID) { d.Dispose(); return; }
                if (_dev != null)
                {
                    try { _dev.AudioEndpointVolume.OnVolumeNotification -= OnVol; } catch { }
                    _dev.Dispose();
                }
                _dev = d;
                _dev.AudioEndpointVolume.OnVolumeNotification += OnVol;
                Level = _dev.AudioEndpointVolume.MasterVolumeLevelScalar;
                Muted = _dev.AudioEndpointVolume.Mute;
            }
            catch { _dev = null; }
        }

        void OnVol(AudioVolumeNotificationData data)
        {
            var disp = System.Windows.Application.Current?.Dispatcher;
            disp?.BeginInvoke(new Action(() =>
            {
                bool changed = Math.Abs(data.MasterVolume - Level) > 0.001f || data.Muted != Muted;
                Level = data.MasterVolume;
                Muted = data.Muted;
                if (changed) VolumeChanged?.Invoke();
            }));
        }

        public void SetLevel(float v)
        {
            Ensure();
            if (_dev == null) return;
            v = Math.Clamp(v, 0f, 1f);
            _dev.AudioEndpointVolume.MasterVolumeLevelScalar = v;
            if (_dev.AudioEndpointVolume.Mute && v > 0) _dev.AudioEndpointVolume.Mute = false;
        }

        public void SetMute(bool m)
        {
            Ensure();
            if (_dev == null) return;
            _dev.AudioEndpointVolume.Mute = m;
        }

        public float Peak
        {
            get
            {
                try { return _dev?.AudioMeterInformation.MasterPeakValue ?? 0f; }
                catch { return 0f; }
            }
        }

        // ================================================================ per-app mixer
        static readonly Dictionary<string, string> _iconCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static string ExeIconPng(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return "";
            if (_iconCache.TryGetValue(exe, out var c)) return c;
            string res = "";
            try
            {
                using var ico = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                if (ico != null)
                {
                    using var bmp = ico.ToBitmap();
                    using var ms = new MemoryStream();
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    res = Convert.ToBase64String(ms.ToArray());
                }
            }
            catch { }
            _iconCache[exe] = res;
            return res;
        }

        static (string name, string exe) ProcInfo(uint pid)
        {
            if (pid == 0) return ("", "");
            try
            {
                using var p = Process.GetProcessById((int)pid);
                string exe = "";
                try { exe = p.MainModule?.FileName ?? ""; } catch { }
                string name = "";
                try { if (!string.IsNullOrEmpty(exe)) name = FileVersionInfo.GetVersionInfo(exe).FileDescription; } catch { }
                if (string.IsNullOrWhiteSpace(name)) name = p.ProcessName;
                return (name, exe);
            }
            catch { return ("", ""); }
        }

        /// <summary>Audio sessions of the default output (UI thread).</summary>
        public object Mixer()
        {
            Ensure();
            var list = new List<object>();
            if (_dev == null) return new { type = "mixer", list };
            try
            {
                var mgr = _dev.AudioSessionManager;
                mgr.RefreshSessions();
                var sessions = mgr.Sessions;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < sessions.Count; i++)
                {
                    var sc = sessions[i];
                    try
                    {
                        if (sc.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired) continue;
                        bool system = sc.IsSystemSoundsSession;
                        uint pid = sc.GetProcessID;
                        var (name, exe) = system ? (Loc.T("pc.mixer.system"), "") : ProcInfo(pid);
                        if (string.IsNullOrWhiteSpace(name)) name = sc.DisplayName;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        string id = system ? "system" : "pid:" + pid;
                        string key = system ? "system" : (string.IsNullOrEmpty(exe) ? name : exe);
                        if (!seen.Add(key)) continue;
                        list.Add(new
                        {
                            id,
                            name,
                            vol = Math.Round(sc.SimpleAudioVolume.Volume, 3),
                            mute = sc.SimpleAudioVolume.Mute,
                            active = sc.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive,
                            icon = system ? "" : ExeIconPng(exe)
                        });
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Log.Write("Mixer: " + ex.Message); }
            return new { type = "mixer", list };
        }

        /// <summary>Set the volume of every session that belongs to the same app (UI thread).</summary>
        public void SetSession(string id, float vol, bool? mute)
        {
            Ensure();
            if (_dev == null || string.IsNullOrEmpty(id)) return;
            try
            {
                var mgr = _dev.AudioSessionManager;
                mgr.RefreshSessions();
                var sessions = mgr.Sessions;
                string targetExe = null;
                if (id.StartsWith("pid:") && uint.TryParse(id.Substring(4), out var tp)) targetExe = ProcInfo(tp).exe;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var sc = sessions[i];
                    try
                    {
                        bool match;
                        if (id == "system") match = sc.IsSystemSoundsSession;
                        else
                        {
                            uint pid = sc.GetProcessID;
                            match = "pid:" + pid == id || (!string.IsNullOrEmpty(targetExe) && string.Equals(ProcInfo(pid).exe, targetExe, StringComparison.OrdinalIgnoreCase));
                        }
                        if (!match) continue;
                        if (vol >= 0) sc.SimpleAudioVolume.Volume = Math.Clamp(vol, 0f, 1f);
                        if (mute.HasValue) sc.SimpleAudioVolume.Mute = mute.Value;
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Log.Write("SetSession: " + ex.Message); }
        }

        // ================================================================ output devices
        public object Outputs()
        {
            var list = new List<object>();
            try
            {
                string def = "";
                try { using var d = _en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); def = d.ID; } catch { }
                foreach (var d in _en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    try { list.Add(new { id = d.ID, name = d.FriendlyName, def = d.ID == def }); }
                    finally { d.Dispose(); }
                }
            }
            catch (Exception ex) { Log.Write("Outputs: " + ex.Message); }
            return new { type = "outputs", list };
        }

        public bool SetOutput(string id)
        {
            try
            {
                var pc = (IPolicyConfig)new CPolicyConfigClient();
                for (int role = 0; role < 3; role++) pc.SetDefaultEndpoint(id, role);
                Marshal.FinalReleaseComObject(pc);
                Ensure();
                return true;
            }
            catch (Exception ex) { Log.Write("SetOutput: " + ex.Message); return false; }
        }

        public string OutputName
        {
            get { try { return _dev?.FriendlyName ?? ""; } catch { return ""; } }
        }
    }

    // Undocumented but stable since Windows 7: used by every "switch audio device" tool.
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    class CPolicyConfigClient { }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int def, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int def, IntPtr defPeriod, IntPtr minPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }
}

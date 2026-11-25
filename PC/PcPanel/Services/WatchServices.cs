using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using PcPanel.Core;

namespace PcPanel.Services
{
    // =====================================================================================================
    //  Activity log – "someone opened your PC"
    // =====================================================================================================
    public sealed class ActivityEntry
    {
        public long Id { get; set; }
        public long Time { get; set; }       // unix ms
        public string Kind { get; set; }     // boot | logon | unlock | lock | wake | sleep | failed | remote | shutdown
        public string Detail { get; set; } = "";
    }

    /// <summary>
    /// Records when the PC is turned on, unlocked, woken up, locked… (with the exact time) and tells the
    /// phone about the ones that matter. Wrong-password attempts are read from the Windows security log.
    /// </summary>
    public sealed class ActivityService
    {
        public static readonly ActivityService I = new ActivityService();
        public event Action Changed;

        readonly object _lock = new object();
        List<ActivityEntry> _items = new List<ActivityEntry>();
        static string FilePath => Path.Combine(AppSettings.Dir, "activity.json");
        EventLogWatcher _failWatcher;

        /// <summary>Kinds the phone gets a notification for.</summary>
        public static bool IsAlert(string kind) => kind is "boot" or "logon" or "unlock" or "wake" or "failed" or "remote";

        public IReadOnlyList<ActivityEntry> Items { get { lock (_lock) return _items.ToArray(); } }

        public void Start()
        {
            try
            {
                if (File.Exists(FilePath))
                    _items = JsonSerializer.Deserialize<List<ActivityEntry>>(File.ReadAllText(FilePath)) ?? new List<ActivityEntry>();
            }
            catch { _items = new List<ActivityEntry>(); }

            // PC Panel starts with Windows – if the PC has only been up a few minutes, it was just turned on
            if (Environment.TickCount64 < 8 * 60 * 1000)
            {
                long bootMs = DateTimeOffset.Now.ToUnixTimeMilliseconds() - Environment.TickCount64;
                lock (_lock)
                    if (!_items.Any(i => i.Kind == "boot" && Math.Abs(i.Time - bootMs) < 10 * 60 * 1000))
                        Add("boot", "", bootMs);
            }

            SystemEvents.SessionSwitch += (s, e) =>
            {
                switch (e.Reason)
                {
                    case SessionSwitchReason.SessionUnlock: Add("unlock"); break;
                    case SessionSwitchReason.SessionLock: Add("lock"); break;
                    case SessionSwitchReason.SessionLogon: Add("logon"); break;
                    case SessionSwitchReason.RemoteConnect: Add("remote"); break;
                }
            };
            SystemEvents.PowerModeChanged += (s, e) =>
            {
                if (e.Mode == PowerModes.Resume) Add("wake");
                else if (e.Mode == PowerModes.Suspend) Add("sleep");
            };
            SystemEvents.SessionEnding += (s, e) => Add("shutdown");

            // wrong password / PIN on the lock screen (event 4625 in the Security log – needs admin)
            try
            {
                _failWatcher = new EventLogWatcher(new EventLogQuery("Security", PathType.LogName, "*[System[(EventID=4625)]]"));
                _failWatcher.EventRecordWritten += (s, e) =>
                {
                    try
                    {
                        string user = "";
                        try { user = e.EventRecord?.Properties.Count > 5 ? e.EventRecord.Properties[5].Value?.ToString() ?? "" : ""; } catch { }
                        Add("failed", user);
                    }
                    catch { }
                };
                _failWatcher.Enabled = true;
            }
            catch (Exception ex) { Log.Write("Failed-logon watcher: " + ex.Message); }
        }

        public void Add(string kind, string detail = "", long timeMs = 0)
        {
            ActivityEntry e;
            lock (_lock)
            {
                // the lock screen often raises the same event twice
                var last = _items.LastOrDefault();
                long now = timeMs > 0 ? timeMs : DateTimeOffset.Now.ToUnixTimeMilliseconds();
                if (last != null && last.Kind == kind && now - last.Time < 5000) return;
                e = new ActivityEntry { Id = (_items.Count == 0 ? 1 : _items.Max(i => i.Id) + 1), Time = now, Kind = kind, Detail = detail ?? "" };
                _items.Add(e);
                if (_items.Count > 300) _items.RemoveRange(0, _items.Count - 300);
                Save();
            }
            if (App.Settings.SecurityAlerts || !IsAlert(kind))
                App.Server?.Broadcast(new { type = "activity", entry = Json(e), alert = IsAlert(kind) && App.Settings.SecurityAlerts });
            try { Changed?.Invoke(); } catch { }
        }

        public void Clear()
        {
            lock (_lock) { _items.Clear(); Save(); }
            App.Server?.Broadcast(new { type = "activityList", items = Array.Empty<object>() });
            Changed?.Invoke();
        }

        public object ListMessage()
        {
            lock (_lock) return new { type = "activityList", items = _items.Skip(Math.Max(0, _items.Count - 150)).Select(Json).ToArray() };
        }

        static object Json(ActivityEntry e) => new { id = e.Id, time = e.Time, kind = e.Kind, detail = e.Detail };

        void Save()
        {
            try
            {
                Directory.CreateDirectory(AppSettings.Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(_items));
            }
            catch { }
        }
    }

    // =====================================================================================================
    //  Screenshots → phone gallery
    // =====================================================================================================
    /// <summary>
    /// Win+PrtSc and the Snipping Tool save screenshots to Pictures\Screenshots; each new one is sent to
    /// the phone, where it lands in the gallery.
    /// </summary>
    public sealed class ScreenshotWatcher
    {
        public static readonly ScreenshotWatcher I = new ScreenshotWatcher();
        readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        readonly HashSet<string> _sent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        [DllImport("shell32.dll")]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);

        public static string KnownFolder(Guid id)
        {
            try
            {
                if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var p) == 0)
                {
                    var s = Marshal.PtrToStringUni(p);
                    Marshal.FreeCoTaskMem(p);
                    return s;
                }
            }
            catch { }
            return null;
        }

        static readonly Guid Screenshots = new Guid("b7bede81-df94-4682-a7d8-57a52620b86f");
        static readonly Guid Pictures = new Guid("33E28130-4E1E-4676-835A-98395C3BC3BB");
        public static readonly Guid Downloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");

        public void Start()
        {
            var dirs = new List<string>();
            var shots = KnownFolder(Screenshots);
            if (!string.IsNullOrEmpty(shots)) dirs.Add(shots);
            var pics = KnownFolder(Pictures);
            if (!string.IsNullOrEmpty(pics))
            {
                var alt = Path.Combine(pics, "Screenshots");
                if (!dirs.Contains(alt, StringComparer.OrdinalIgnoreCase)) dirs.Add(alt);
            }
            foreach (var d in dirs)
            {
                try
                {
                    Directory.CreateDirectory(d);
                    var w = new FileSystemWatcher(d) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName };
                    w.Created += (s, e) => OnNew(e.FullPath);
                    w.Renamed += (s, e) => OnNew(e.FullPath);
                    w.EnableRaisingEvents = true;
                    _watchers.Add(w);
                }
                catch (Exception ex) { Log.Write("Screenshot watcher: " + ex.Message); }
            }
        }

        void OnNew(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") return;
            if (!App.Settings.ShotToPhone) return;
            lock (_sent) { if (!_sent.Add(path)) return; }
            _ = Task.Run(async () =>
            {
                // wait until Windows has finished writing the file
                for (int i = 0; i < 20; i++)
                {
                    await Task.Delay(250);
                    try { using var f = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read); if (f.Length > 0) break; } catch { }
                }
                var phone = App.Server?.Sessions.FirstOrDefault(s => s.Authed);
                if (phone == null) return;
                var r = await FileService.SendAsync(phone, path);
                if (r == FileService.SendResult.Ok)
                    App.Ui(() => App.Notch?.ShowInfo(Glyph.Camera, Loc.F("pc.shot.toPhone", phone.DeviceName), 2.4));
            });
        }
    }

    // =====================================================================================================
    //  "Download finished" – browsers, Steam, Epic (no API keys needed)
    // =====================================================================================================
    public sealed class DownloadWatcher
    {
        public static readonly DownloadWatcher I = new DownloadWatcher();
        static readonly string[] PartialExt = { ".crdownload", ".opdownload", ".part", ".partial", ".download", ".!ut" };
        FileSystemWatcher _browser;
        Timer _poll;
        readonly Dictionary<string, HashSet<string>> _steamDownloading = new Dictionary<string, HashSet<string>>();
        readonly Dictionary<string, bool> _epicIncomplete = new Dictionary<string, bool>();
        readonly Dictionary<string, DateTime> _recent = new Dictionary<string, DateTime>();
        bool _firstPoll = true;

        public void Start()
        {
            try
            {
                var dl = ScreenshotWatcher.KnownFolder(ScreenshotWatcher.Downloads);
                if (!string.IsNullOrEmpty(dl) && Directory.Exists(dl))
                {
                    _browser = new FileSystemWatcher(dl) { NotifyFilter = NotifyFilters.FileName, IncludeSubdirectories = false };
                    _browser.Renamed += (s, e) =>
                    {
                        // Chrome/Edge/Opera: "x.crdownload" → "x.zip"
                        if (PartialExt.Contains(Path.GetExtension(e.OldFullPath).ToLowerInvariant()) && !PartialExt.Contains(Path.GetExtension(e.FullPath).ToLowerInvariant()))
                            Finished(Path.GetFileName(e.FullPath), "browser", e.FullPath);
                    };
                    _browser.Deleted += (s, e) =>
                    {
                        // Firefox: "x.zip.part" disappears next to the finished "x.zip"
                        if (e.FullPath.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                        {
                            var final = e.FullPath.Substring(0, e.FullPath.Length - 5);
                            if (File.Exists(final)) Finished(Path.GetFileName(final), "browser", final);
                        }
                    };
                    _browser.EnableRaisingEvents = true;
                }
            }
            catch (Exception ex) { Log.Write("Download watcher: " + ex.Message); }
            _poll = new Timer(_ => Poll(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20));
        }

        void Finished(string name, string source, string path = null)
        {
            if (!App.Settings.DownloadAlerts || string.IsNullOrWhiteSpace(name)) return;
            lock (_recent)
            {
                if (_recent.TryGetValue(name, out var t) && (DateTime.Now - t).TotalSeconds < 30) return;
                _recent[name] = DateTime.Now;
            }
            long size = 0;
            try { if (path != null && File.Exists(path)) size = new FileInfo(path).Length; } catch { }
            if (source == "browser" && size > 0 && size < 50 * 1024) return;   // ignore tiny files (thumbnails, page bits)
            App.Server?.Broadcast(new { type = "download", name, source, size });
            App.Ui(() => App.Notch?.ShowInfo(Glyph.Download, Loc.F("pc.dl.done", name), 3.2));
            SoundService.Play("done");
        }

        void Poll()
        {
            try { PollSteam(); } catch { }
            try { PollEpic(); } catch { }
            _firstPoll = false;
        }

        // ------------------------------------------------------------------ Steam
        void PollSteam()
        {
            string steam = null;
            try { steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string; } catch { }
            if (string.IsNullOrEmpty(steam) || !Directory.Exists(steam)) return;
            var libs = new List<string> { Path.Combine(steam, "steamapps") };
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    var lib = Path.Combine(m.Groups[1].Value.Replace(@"\\", @"\"), "steamapps");
                    if (!libs.Contains(lib, StringComparer.OrdinalIgnoreCase)) libs.Add(lib);
                }

            foreach (var lib in libs)
            {
                var dl = Path.Combine(lib, "downloading");
                var now = Directory.Exists(dl)
                    ? new HashSet<string>(Directory.GetDirectories(dl).Select(Path.GetFileName).Where(n => n.All(char.IsDigit)))
                    : new HashSet<string>();
                if (_steamDownloading.TryGetValue(lib, out var before) && !_firstPoll)
                {
                    foreach (var id in before.Where(id => !now.Contains(id)))
                    {
                        var acf = Path.Combine(lib, $"appmanifest_{id}.acf");
                        if (!File.Exists(acf)) continue;
                        var text = File.ReadAllText(acf);
                        var name = Regex.Match(text, "\"name\"\\s+\"([^\"]+)\"").Groups[1].Value;
                        var flags = Regex.Match(text, "\"StateFlags\"\\s+\"(\\d+)\"").Groups[1].Value;
                        if (flags == "4") Finished(name, "steam");
                    }
                }
                _steamDownloading[lib] = now;
            }
        }

        // ------------------------------------------------------------------ Epic Games
        void PollEpic()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, "*.item"))
            {
                try
                {
                    var j = JsonNode.Parse(File.ReadAllText(f));
                    bool incomplete = false;
                    try { incomplete = j?["bIsIncompleteInstall"]?.GetValue<bool>() ?? false; } catch { }
                    string name = j?["DisplayName"]?.GetValue<string>() ?? "";
                    if (_epicIncomplete.TryGetValue(f, out var was) && was && !incomplete && !_firstPoll) Finished(name, "epic");
                    _epicIncomplete[f] = incomplete;
                }
                catch { }
            }
        }
    }
}

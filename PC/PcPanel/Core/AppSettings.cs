using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PcPanel.Core
{
    public sealed class AppSettings
    {
        // ---- pairing
        public string Token { get; set; }
        public string PcId { get; set; }
        public bool EverPaired { get; set; }
        public string InstalledPath { get; set; }

        // ---- general
        public string Lang { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool Autostart { get; set; } = true;
        public int CpuAlert { get; set; } = 90;
        public int GpuAlert { get; set; } = 85;

        // ---- notch
        public bool NotchEnabled { get; set; } = true;
        public string NotchStyle { get; set; } = "notch";   // notch | island
        public string NotchMode { get; set; } = "auto";     // auto | always
        public bool NotchHideFullscreen { get; set; } = true;
        public bool NotchVolume { get; set; } = true;
        public bool NotchConnectInfo { get; set; } = true;
        public bool NotchNotifications { get; set; } = true;
        public bool NotchCalls { get; set; } = true;
        public bool NotchDrop { get; set; } = true;
        public bool NotchCharging { get; set; } = true;

        // ---- features
        public bool Sounds { get; set; } = true;
        public int SoundVolume { get; set; } = 70;
        public bool ClipboardSync { get; set; } = true;
        public bool AutoLock { get; set; } = false;
        public bool ShotCursor { get; set; } = false;
        public bool GameMode { get; set; } = true;
        public bool TempHistory { get; set; } = true;
        public bool FanControl { get; set; } = true;

        // ---- music
        public string MusicSource { get; set; } = "spotify"; // spotify | youtube
        public string SpotifyClientId { get; set; } = "";
        public string SpotifyRefresh { get; set; } = "";     // DPAPI protected, base64
        public string SpotifyUser { get; set; } = "";
        public string YouTubeKey { get; set; } = "";

        public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PC Panel");
        static string OldDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MobiLink");
        static string FilePath => Path.Combine(Dir, "settings.json");

        public string PcName => string.IsNullOrWhiteSpace(DisplayName) ? Environment.MachineName : DisplayName.Trim();

        public static AppSettings Load()
        {
            AppSettings s = null;
            try
            {
                // migrate from MobiLink 1.x so the phone stays paired
                var old = Path.Combine(OldDir, "settings.json");
                if (!File.Exists(FilePath) && File.Exists(old))
                {
                    Directory.CreateDirectory(Dir);
                    File.Copy(old, FilePath);
                }
                if (File.Exists(FilePath))
                    s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
            }
            catch (Exception ex) { Log.Write("Settings load failed: " + ex.Message); }
            s ??= new AppSettings();
            bool dirty = false;
            if (string.IsNullOrEmpty(s.Token)) { s.Token = NewToken(); dirty = true; }
            if (string.IsNullOrEmpty(s.PcId)) { s.PcId = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant(); dirty = true; }
            if (string.IsNullOrEmpty(s.Lang)) { s.Lang = Loc.GuessSystemLanguage(); dirty = true; }
            if (s.NotchStyle != "island") s.NotchStyle = "notch";
            if (s.NotchMode != "always") s.NotchMode = "auto";
            if (s.MusicSource != "youtube") s.MusicSource = "spotify";
            if (dirty) s.Save();
            return s;
        }

        public static string NewToken()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        readonly object _saveLock = new object();

        public void Save()
        {
            lock (_saveLock)
            {
                try
                {
                    Directory.CreateDirectory(Dir);
                    var tmp = FilePath + ".tmp";
                    File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                    File.Move(tmp, FilePath, true);
                }
                catch (Exception ex) { Log.Write("Settings save failed: " + ex.Message); }
            }
        }

        // ---- secrets (Windows DPAPI, current user only)
        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }

        public static string Unprotect(string b64)
        {
            if (string.IsNullOrEmpty(b64)) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(b64), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }
    }

    public static class Log
    {
        static readonly object _lock = new object();
        public static void Write(string msg)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(AppSettings.Dir);
                    var p = Path.Combine(AppSettings.Dir, "log.txt");
                    var fi = new FileInfo(p);
                    if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();
                    File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}

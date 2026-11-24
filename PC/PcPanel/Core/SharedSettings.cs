using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using PcPanel.Services;

namespace PcPanel.Core
{
    /// <summary>
    /// Settings that exist on both the PC and the phone. The PC is the source of truth:
    /// a change on either side is applied here, saved, and pushed to every phone and to the PC window.
    /// </summary>
    public static class SharedSettings
    {
        /// <summary>Raised on the UI thread after any shared setting changed.</summary>
        public static event Action Changed;

        public static object Snapshot()
        {
            var s = App.Settings;
            return new
            {
                type = "settings",
                lang = s.Lang,
                pcName = s.PcName,
                notchOn = s.NotchEnabled,
                notchStyle = s.NotchStyle,
                notchMode = s.NotchMode,
                notchFs = s.NotchHideFullscreen,
                notchVol = s.NotchVolume,
                notchConn = s.NotchConnectInfo,
                notchNotif = s.NotchNotifications,
                notchCall = s.NotchCalls,
                notchCharge = s.NotchCharging,
                sounds = s.Sounds,
                soundVol = s.SoundVolume,
                clipSync = s.ClipboardSync,
                autoLock = s.AutoLock,
                shotCursor = s.ShotCursor,
                gameMode = s.GameMode,
                tempHistory = s.TempHistory,
                fanCtl = s.FanControl,
                musicSrc = s.MusicSource,
                cpuAlert = s.CpuAlert,
                gpuAlert = s.GpuAlert,
                autostart = s.Autostart,
                spotify = SpotifyService.I.Connected,
                spotifyUser = s.SpotifyUser,
                spotifyClient = !string.IsNullOrWhiteSpace(s.SpotifyClientId),
                ytKey = !string.IsNullOrWhiteSpace(s.YouTubeKey),
                version = App.Version
            };
        }

        static bool B(JsonNode n) { try { return n.GetValue<bool>(); } catch { return false; } }
        static int I(JsonNode n, int d) { try { return (int)Math.Round(n.GetValue<double>()); } catch { try { return n.GetValue<int>(); } catch { return d; } } }
        static string S(JsonNode n) { try { return n.GetValue<string>() ?? ""; } catch { return ""; } }

        /// <summary>Apply one or more key/values (from the phone or the PC window). Call on the UI thread.</summary>
        public static void Apply(IEnumerable<KeyValuePair<string, JsonNode>> values)
        {
            var s = App.Settings;
            bool autostartChanged = false, langChanged = false, nameChanged = false;
            foreach (var kv in values)
            {
                var v = kv.Value;
                if (v == null) continue;
                switch (kv.Key)
                {
                    case "lang": { var l = S(v); if (Loc.IsSupported(l) && l != s.Lang) { s.Lang = l; langChanged = true; } break; }
                    case "pcName": { var n = S(v).Trim(); if (n.Length > 40) n = n.Substring(0, 40); if (n != s.DisplayName) { s.DisplayName = n == Environment.MachineName ? "" : n; nameChanged = true; } break; }
                    case "notchOn": s.NotchEnabled = B(v); break;
                    case "notchStyle": s.NotchStyle = S(v) == "island" ? "island" : "notch"; break;
                    case "notchMode": s.NotchMode = S(v) == "always" ? "always" : "auto"; break;
                    case "notchFs": s.NotchHideFullscreen = B(v); break;
                    case "notchVol": s.NotchVolume = B(v); break;
                    case "notchConn": s.NotchConnectInfo = B(v); break;
                    case "notchNotif": s.NotchNotifications = B(v); break;
                    case "notchCall": s.NotchCalls = B(v); break;
                    case "notchCharge": s.NotchCharging = B(v); break;
                    case "sounds": s.Sounds = B(v); break;
                    case "soundVol": s.SoundVolume = Math.Clamp(I(v, 70), 0, 100); break;
                    case "clipSync": s.ClipboardSync = B(v); break;
                    case "autoLock": s.AutoLock = B(v); break;
                    case "shotCursor": s.ShotCursor = B(v); break;
                    case "gameMode": s.GameMode = B(v); break;
                    case "tempHistory": s.TempHistory = B(v); break;
                    case "fanCtl": s.FanControl = B(v); if (!s.FanControl) FanService.I.RestoreAll(); break;
                    case "musicSrc": s.MusicSource = S(v) == "youtube" ? "youtube" : "spotify"; break;
                    case "cpuAlert": s.CpuAlert = Math.Clamp(I(v, 90), 60, 105); break;
                    case "gpuAlert": s.GpuAlert = Math.Clamp(I(v, 85), 60, 105); break;
                    case "autostart": { bool a = B(v); if (a != s.Autostart) { s.Autostart = a; autostartChanged = true; } break; }
                }
            }
            s.Save();
            if (langChanged) { Loc.Load(s.Lang); App.LanguageChanged(); }
            if (autostartChanged) { bool on = s.Autostart; System.Threading.Tasks.Task.Run(() => SystemSetup.SetAutostart(on)); }
            if (nameChanged) App.Server?.Broadcast(new { type = "pcInfo", name = s.PcName });
            Push();
        }

        public static void Set(string key, JsonNode value) => Apply(new[] { new KeyValuePair<string, JsonNode>(key, value) });
        public static void Set(string key, bool value) => Set(key, JsonValue.Create(value));
        public static void Set(string key, int value) => Set(key, JsonValue.Create(value));
        public static void Set(string key, string value) => Set(key, JsonValue.Create(value));

        /// <summary>Send the full set to every phone and refresh the PC UI.</summary>
        public static void Push()
        {
            App.Server?.Broadcast(Snapshot());
            App.Notch?.SettingsChanged();
            Changed?.Invoke();
        }
    }
}

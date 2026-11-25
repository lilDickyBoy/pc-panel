using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;
using PcPanel.Core;

namespace PcPanel.Services
{
    /// <summary>Things the phone tells the PC about itself: battery, notifications, calls.</summary>
    public static class PhoneService
    {
        static string S(JsonObject m, string k) { try { return m[k]?.GetValue<string>() ?? ""; } catch { return ""; } }
        static int I(JsonObject m, string k, int d) { try { return (int)Math.Round(m[k]?.GetValue<double>() ?? d); } catch { return d; } }
        static bool B(JsonObject m, string k) { try { return m[k]?.GetValue<bool>() ?? false; } catch { return false; } }

        /// <summary>Raised on the UI thread when a phone's battery changed.</summary>
        public static event Action BatteryChanged;

        public static void OnPhone(Session s, JsonObject m)
        {
            int level = Math.Clamp(I(m, "level", -1), -1, 100);
            bool chg = B(m, "charging");
            int oldLevel = s.Battery;
            bool oldChg = s.Charging;
            bool first = oldLevel < 0;
            s.Battery = level;
            s.Charging = chg;
            try { s.BatteryTemp = (float)(m["temp"]?.GetValue<double>() ?? -1); } catch { }

            App.Ui(() =>
            {
                BatteryChanged?.Invoke();
                App.Notch?.PhoneBatteryChanged();
                if (first || level < 0 || !App.Settings.NotchCharging) return;
                if (chg && !oldChg)
                {
                    App.Notch?.ShowBattery(level, true, Loc.F("pc.phone.charging", level));
                    SoundService.Play("charge_in");
                }
                else if (!chg && oldChg)
                {
                    App.Notch?.ShowBattery(level, false, Loc.F("pc.phone.unplugged", level));
                    SoundService.Play("charge_out");
                }
                else if (chg && level >= 100 && oldLevel < 100)
                {
                    App.Notch?.ShowBattery(100, true, Loc.T("pc.phone.full"));
                    SoundService.Play("done");
                }
                else if (!chg && level <= 15 && oldLevel > 15)
                {
                    App.Notch?.ShowBattery(level, false, Loc.F("pc.phone.low", level));
                    SoundService.Play("low");
                }
            });
        }

        static BitmapSource DecodeIcon(string b64)
        {
            if (string.IsNullOrEmpty(b64)) return null;
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = new MemoryStream(Convert.FromBase64String(b64));
                bi.DecodePixelWidth = 96;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        public static void OnNotification(JsonObject m)
        {
            string app = S(m, "app"), title = S(m, "title"), text = S(m, "text"), icon = S(m, "icon");
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(text)) return;
            App.Ui(() =>
            {
                if (!App.Settings.NotchNotifications) return;
                var img = DecodeIcon(icon);
                App.Notch?.ShowNotification(img, app, title, text, S(m, "pkg"));
                SoundService.Play("notify");
            });
        }

        public static void OnCall(JsonObject m)
        {
            string state = S(m, "state"), name = S(m, "name"), app = S(m, "app"), icon = S(m, "icon");
            App.Ui(() =>
            {
                if (!App.Settings.NotchCalls) return;
                if (state == "ringing")
                {
                    App.Notch?.ShowCall(DecodeIcon(icon), string.IsNullOrWhiteSpace(name) ? Loc.T("pc.call.unknown") : name, app);
                    SoundService.Play("notify");
                }
                else App.Notch?.EndCall();
            });
        }
    }
}

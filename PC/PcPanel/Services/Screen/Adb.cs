using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PcPanel.Core;

namespace PcPanel.Services.Screen
{
    /// <summary>Thin wrapper around adb.exe (shipped inside the scrcpy folder).</summary>
    public static class Adb
    {
        public sealed class Device
        {
            public string Serial;
            public string State;      // device | unauthorized | offline
            public string Model = "";
            public bool Wireless => Serial.Contains(':') || Serial.StartsWith("adb-", StringComparison.OrdinalIgnoreCase);
            public bool Ready => State == "device";
        }

        public static bool Available => ScreenTools.HasScrcpy;

        public static async Task<string> Exec(string args, int timeoutMs = 10000)
        {
            if (!Available) return "";
            var r = await ScreenTools.Run(ScreenTools.Adb, args, timeoutMs);
            return (r.Out ?? "") + (r.Err ?? "");
        }

        public static async Task<List<Device>> Devices()
        {
            var list = new List<Device>();
            if (!Available) return list;
            var r = await ScreenTools.Run(ScreenTools.Adb, "devices -l", 10000);
            foreach (var raw in (r.Out ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("List of devices") || line.StartsWith("*")) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                var d = new Device { Serial = parts[0], State = parts[1] };
                var model = parts.FirstOrDefault(p => p.StartsWith("model:"));
                if (model != null) d.Model = model.Substring(6).Replace('_', ' ');
                list.Add(d);
            }
            return list;
        }

        /// <summary>The phone to use: a USB one first, then a wireless one.</summary>
        public static async Task<Device> Pick(bool allowWireless = true)
        {
            var all = await Devices();
            return all.FirstOrDefault(d => d.Ready && !d.Wireless)
                ?? (allowWireless ? all.FirstOrDefault(d => d.Ready) : null)
                ?? all.FirstOrDefault(d => !d.Wireless)
                ?? (allowWireless ? all.FirstOrDefault() : null);
        }

        /// <summary>Makes 127.0.0.1:port on the phone reach port on this PC through the USB cable.</summary>
        public static async Task<bool> Reverse(string serial, int port)
        {
            var o = await Exec($"-s {serial} reverse tcp:{port} tcp:{port}");
            bool ok = !o.Contains("error", StringComparison.OrdinalIgnoreCase);
            if (!ok) Log.Write("adb reverse: " + o.Trim());
            return ok;
        }

        public static Task RemoveReverse(string serial, int port) => Exec($"-s {serial} reverse --remove tcp:{port}");

        /// <summary>Switches a USB phone to Wi-Fi debugging and connects to it (cable can be unplugged afterwards).</summary>
        public static async Task<string> GoWireless(string serial, string phoneIp)
        {
            if (string.IsNullOrEmpty(phoneIp)) return null;
            await Exec($"-s {serial} tcpip 5555");
            await Task.Delay(1500);
            for (int i = 0; i < 4; i++)
            {
                var o = await Exec($"connect {phoneIp}:5555");
                if (o.Contains("connected to", StringComparison.OrdinalIgnoreCase)) return phoneIp + ":5555";
                await Task.Delay(1000);
            }
            return null;
        }

        public static Task Key(string serial, int keycode) => Exec($"-s {serial} shell input keyevent {keycode}", 5000);

        public static void KillServer()
        {
            try { _ = ScreenTools.Run(ScreenTools.Adb, "kill-server", 4000); } catch { }
        }
    }
}

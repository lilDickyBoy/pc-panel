using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Reflection;
using PcPanel.Core;

namespace PcPanel.Services
{
    /// <summary>Short UI sounds (embedded WAVs). Volume is applied by scaling the 16-bit samples.</summary>
    public static class SoundService
    {
        static readonly Dictionary<string, byte[]> _raw = new Dictionary<string, byte[]>();
        static SoundPlayer _player;
        static DateTime _last = DateTime.MinValue;

        static byte[] Load(string name)
        {
            lock (_raw)
            {
                if (_raw.TryGetValue(name, out var b)) return b;
                try
                {
                    using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("snd." + name + ".wav");
                    if (s == null) return null;
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    b = ms.ToArray();
                    _raw[name] = b;
                    return b;
                }
                catch { return null; }
            }
        }

        /// <param name="name">charge_in, charge_out, notify, connect, disconnect, alert, done, low, file</param>
        public static void Play(string name, bool force = false)
        {
            var st = App.Settings;
            if (!force && (st == null || !st.Sounds)) return;
            // don't stack sounds on top of each other
            if (!force && (DateTime.UtcNow - _last).TotalMilliseconds < 250) return;
            _last = DateTime.UtcNow;
            int vol = st?.SoundVolume ?? 70;
            if (vol <= 0) return;
            var raw = Load(name);
            if (raw == null) return;
            try
            {
                var data = (byte[])raw.Clone();
                double k = Math.Pow(vol / 100.0, 1.6); // perceptual curve
                int dataStart = FindData(data);
                if (dataStart > 0)
                {
                    for (int i = dataStart; i + 1 < data.Length; i += 2)
                    {
                        short v = (short)(data[i] | (data[i + 1] << 8));
                        int nv = (int)(v * k);
                        data[i] = (byte)(nv & 0xFF);
                        data[i + 1] = (byte)((nv >> 8) & 0xFF);
                    }
                }
                var p = new SoundPlayer(new MemoryStream(data));
                var old = _player;
                _player = p;
                p.Play();
                old?.Dispose();
            }
            catch (Exception ex) { Log.Write("Sound: " + ex.Message); }
        }

        static int FindData(byte[] b)
        {
            for (int i = 12; i + 8 < b.Length; i++)
                if (b[i] == 'd' && b[i + 1] == 'a' && b[i + 2] == 't' && b[i + 3] == 'a') return i + 8;
            return -1;
        }
    }
}

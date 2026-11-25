using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PcPanel.Core
{
    /// <summary>Brand logos (Spotify, YouTube, Discord, WhatsApp, Bluetooth) embedded as WPF resources.</summary>
    public static class Brand
    {
        static readonly Dictionary<string, ImageSource> _cache = new Dictionary<string, ImageSource>();

        public static ImageSource Get(string name)
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(name, out var img)) return img;
                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri($"pack://application:,,,/Assets/Brand/{name}.png", UriKind.Absolute);
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze();
                    img = bi;
                }
                catch { img = null; }
                _cache[name] = img;
                return img;
            }
        }

        /// <summary>Logo for a notification's app (phone package name or app label), or null.</summary>
        public static ImageSource ForApp(string packageOrName)
        {
            var a = (packageOrName ?? "").ToLowerInvariant();
            if (a.Contains("whatsapp")) return Get("whatsapp");
            if (a.Contains("discord")) return Get("discord");
            if (a.Contains("spotify")) return Get("spotify_white");
            if (a.Contains("youtube")) return Get("youtube");
            return null;
        }

        /// <summary>Background colour behind a white logo, for app tiles.</summary>
        public static Color TileFor(string packageOrName)
        {
            var a = (packageOrName ?? "").ToLowerInvariant();
            if (a.Contains("whatsapp")) return Color.FromRgb(0x25, 0xD3, 0x66);
            if (a.Contains("discord")) return Color.FromRgb(0x58, 0x65, 0xF2);
            if (a.Contains("spotify")) return Color.FromRgb(0x1E, 0xD7, 0x60);
            return Color.FromRgb(0x2C, 0x2C, 0x2E);
        }
    }
}

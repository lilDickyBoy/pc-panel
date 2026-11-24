using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PcPanel.Core;

namespace PcPanel.Services
{
    /// <summary>
    /// Lists everything in the Start menu (desktop apps, Steam games, Store apps) and extracts icons.
    /// Shell COM objects need an STA thread, so all work runs on one dedicated worker thread.
    /// </summary>
    public static class AppsService
    {
        sealed class Entry { public string Id; public string Name; }

        static readonly BlockingCollection<Action> Queue = new BlockingCollection<Action>();
        static List<Entry> _cache;
        static DateTime _cacheTime;

        static readonly string[] Junk = { "uninstall", "readme", "read me", "help", "website", "documentation", "release notes", "license", "manual", "support", "changelog", "kaldır" };

        static AppsService()
        {
            var t = new Thread(() =>
            {
                foreach (var a in Queue.GetConsumingEnumerable())
                {
                    try { a(); } catch (Exception ex) { Log.Write("AppsService: " + ex.Message); }
                }
            })
            { IsBackground = true, Name = "AppsSTA" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        static Task<T> Sta<T>(Func<T> f)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Queue.Add(() => { try { tcs.SetResult(f()); } catch (Exception e) { tcs.SetException(e); } });
            return tcs.Task;
        }

        static List<Entry> Enumerate()
        {
            if (_cache != null && (DateTime.Now - _cacheTime).TotalSeconds < 120) return _cache;
            var list = new List<Entry>();
            var t = Type.GetTypeFromProgID("Shell.Application");
            dynamic shell = Activator.CreateInstance(t);
            try
            {
                dynamic folder = shell.NameSpace("shell:::{4234d49b-0245-4df3-b780-3893943456e1}");
                dynamic items = folder.Items();
                int count = items.Count;
                for (int i = 0; i < count; i++)
                {
                    dynamic item = items.Item(i);
                    if (item == null) continue;
                    string name = item.Name;
                    string path = item.Path;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                    var ln = name.ToLowerInvariant();
                    if (Junk.Any(j => ln.Contains(j))) continue;
                    if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                    if (path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".chm", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new Entry { Id = path, Name = name });
                }
            }
            finally { Marshal.FinalReleaseComObject(shell); }
            _cache = list.GroupBy(e => e.Id).Select(g => g.First()).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            _cacheTime = DateTime.Now;
            return _cache;
        }

        public static async Task<object> ListAsync()
        {
            var items = await Sta(Enumerate);
            return new { type = "apps", list = items.Select(e => (object)new { id = e.Id, name = e.Name }).ToList() };
        }

        public static async Task<object> IconAsync(string id)
        {
            string data = await Sta(() => ExtractIcon(id, 96));
            return new { type = "icon", id, data = data ?? "" };
        }

        public static async Task<bool> LaunchAsync(string id)
        {
            var items = await Sta(Enumerate);
            if (!items.Any(e => e.Id == id)) return false; // only launch things that really are in the Start menu
            Process.Start(new ProcessStartInfo("explorer.exe", "\"shell:AppsFolder\\" + id + "\"") { UseShellExecute = true });
            return true;
        }

        static string ExtractIcon(string id, int size)
        {
            IntPtr hbm = IntPtr.Zero;
            Native.IShellItemImageFactory factory = null;
            try
            {
                var iid = typeof(Native.IShellItemImageFactory).GUID;
                Native.SHCreateItemFromParsingName("shell:AppsFolder\\" + id, IntPtr.Zero, ref iid, out factory);
                // SIIGBF_BIGGERSIZEOK (0x1) | SIIGBF_ICONONLY (0x4)
                int hr = factory.GetImage(new Native.SIZE { cx = size, cy = size }, 0x1 | 0x4, out hbm);
                if (hr != 0 || hbm == IntPtr.Zero) return null;

                Native.GetObject(hbm, Marshal.SizeOf<Native.BITMAP>(), out var bm);
                int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
                if (w <= 0 || h <= 0) return null;
                var bmi = new Native.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
                    biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32, biCompression = 0
                };
                var px = new byte[w * h * 4];
                var dc = Native.GetDC(IntPtr.Zero);
                try { Native.GetDIBits(dc, hbm, 0, (uint)h, px, ref bmi, 0); }
                finally { Native.ReleaseDC(IntPtr.Zero, dc); }

                bool hasAlpha = false;
                for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) { hasAlpha = true; break; }
                var src = BitmapSource.Create(w, h, 96, 96, hasAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32, null, px, w * 4);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(src));
                using var ms = new MemoryStream();
                enc.Save(ms);
                return Convert.ToBase64String(ms.ToArray());
            }
            catch { return null; }
            finally
            {
                if (hbm != IntPtr.Zero) Native.DeleteObject(hbm);
                if (factory != null) Marshal.ReleaseComObject(factory);
            }
        }
    }
}

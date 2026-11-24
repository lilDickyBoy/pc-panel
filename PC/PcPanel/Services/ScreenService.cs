using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PcPanel.Core;

namespace PcPanel.Services
{
    /// <summary>Screenshots and the low-latency live view of the primary screen.</summary>
    public static class ScreenService
    {
        static readonly ImageCodecInfo Jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        static Session _streamTo;
        static int _streamGen;

        static Rectangle Bounds => System.Windows.Forms.Screen.PrimaryScreen.Bounds;

        static void DrawCursor(Graphics g, Rectangle b, double scale)
        {
            try
            {
                var ci = new Native.CURSORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.CURSORINFO>() };
                if (!Native.GetCursorInfo(ref ci) || (ci.flags & Native.CURSOR_SHOWING) == 0) return;
                int hx = 0, hy = 0;
                if (Native.GetIconInfo(ci.hCursor, out var ii))
                {
                    hx = ii.xHotspot; hy = ii.yHotspot;
                    if (ii.hbmMask != IntPtr.Zero) Native.DeleteObject(ii.hbmMask);
                    if (ii.hbmColor != IntPtr.Zero) Native.DeleteObject(ii.hbmColor);
                }
                int x = (int)((ci.ptScreenPos.X - b.Left - hx) * scale);
                int y = (int)((ci.ptScreenPos.Y - b.Top - hy) * scale);
                var hdc = g.GetHdc();
                try
                {
                    int size = Math.Max(16, (int)(32 * Math.Max(scale, 0.6)));
                    Native.DrawIconEx(hdc, x, y, ci.hCursor, size, size, 0, IntPtr.Zero, Native.DI_NORMAL);
                }
                finally { g.ReleaseHdc(hdc); }
            }
            catch { }
        }

        static byte[] Capture(int maxWidth, long quality, bool cursor)
        {
            var b = Bounds;
            using var full = new Bitmap(b.Width, b.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(full))
            {
                g.CopyFromScreen(b.Left, b.Top, 0, 0, b.Size, CopyPixelOperation.SourceCopy);
                if (cursor) DrawCursor(g, b, 1.0);
            }
            Bitmap outBmp = full;
            Bitmap scaled = null;
            if (maxWidth > 0 && b.Width > maxWidth)
            {
                int h = (int)Math.Round(b.Height * (maxWidth / (double)b.Width));
                scaled = new Bitmap(maxWidth, h, PixelFormat.Format24bppRgb);
                using var g2 = Graphics.FromImage(scaled);
                g2.InterpolationMode = InterpolationMode.Bilinear;
                g2.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                g2.DrawImage(full, 0, 0, maxWidth, h);
                outBmp = scaled;
            }
            try
            {
                using var ep = new EncoderParameters(1);
                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                using var ms = new MemoryStream();
                outBmp.Save(ms, Jpeg, ep);
                return ms.ToArray();
            }
            finally { scaled?.Dispose(); }
        }

        public static string ScreenshotBase64()
        {
            return Convert.ToBase64String(Capture(0, 90L, App.Settings.ShotCursor));
        }

        // ------------------------------------------------------------ live view
        public static void Start(Session s, int fps, int quality)
        {
            fps = Math.Clamp(fps, 1, 12);
            quality = Math.Clamp(quality, 25, 85);
            _streamTo = s;
            int gen = Interlocked.Increment(ref _streamGen);
            s.Streaming = true;
            Task.Run(async () =>
            {
                var period = TimeSpan.FromMilliseconds(1000.0 / fps);
                while (gen == _streamGen && s.Authed && s.Streaming)
                {
                    var t0 = DateTime.UtcNow;
                    if (SensorService.I.InGame && App.Settings.GameMode) { await Task.Delay(1000); continue; }
                    try
                    {
                        var jpg = Capture(1280, quality, true);
                        var b = Bounds;
                        await s.SendAsync(new { type = "frame", data = Convert.ToBase64String(jpg), w = b.Width, h = b.Height });
                    }
                    catch (Exception ex) { Log.Write("Frame: " + ex.Message); await Task.Delay(500); }
                    var wait = period - (DateTime.UtcNow - t0);
                    if (wait > TimeSpan.Zero) await Task.Delay(wait);
                }
                s.Streaming = false;
            });
        }

        public static void Stop(Session s)
        {
            s.Streaming = false;
            if (_streamTo == s) { _streamTo = null; Interlocked.Increment(ref _streamGen); }
        }

        /// <summary>Tap on the live view: x/y are 0..1 of the primary screen.</summary>
        public static void ClickAt(double x, double y, string button, bool dbl)
        {
            var b = Bounds;
            int px = b.Left + (int)Math.Round(Math.Clamp(x, 0, 1) * (b.Width - 1));
            int py = b.Top + (int)Math.Round(Math.Clamp(y, 0, 1) * (b.Height - 1));
            Native.SetCursorPos(px, py);
            if (!string.IsNullOrEmpty(button)) InputService.Click(button, dbl);
        }
    }
}

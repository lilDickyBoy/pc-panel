using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PcPanel.Core;
using Windows.Media.Control;

namespace PcPanel.Services
{
    /// <summary>
    /// Reads "Now Playing" from Windows (works with Spotify, YouTube in any browser, Apple Music, VLC, ...).
    /// </summary>
    public sealed class MediaService
    {
        public static readonly MediaService I = new MediaService();

        GlobalSystemMediaTransportControlsSessionManager _mgr;
        GlobalSystemMediaTransportControlsSession _session;
        readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);

        public bool HasMedia { get; private set; }
        public bool Playing { get; private set; }
        public string Title { get; private set; } = "";
        public string Artist { get; private set; } = "";
        public string Album { get; private set; } = "";
        public string AppId { get; private set; } = "";
        public double DurationSec { get; private set; }
        double _posSec; DateTimeOffset _posStamp;

        public BitmapSource Art { get; private set; }      // frozen, square
        public byte[] ArtJpeg { get; private set; }
        public int ArtVersion { get; private set; }
        public Color Accent { get; private set; } = Color.FromRgb(0x0A, 0x84, 0xFF);
        string _artKey = "";
        int _artRetries;

        /// <summary>Raised on a background thread whenever anything changes.</summary>
        public event Action Changed;

        public double PositionSec
        {
            get
            {
                double p = _posSec;
                if (Playing && _posStamp != default)
                    p += (DateTimeOffset.Now - _posStamp).TotalSeconds;
                if (DurationSec > 0) p = Math.Min(p, DurationSec);
                return Math.Max(0, p);
            }
        }

        public async Task InitAsync()
        {
            try
            {
                _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _mgr.CurrentSessionChanged += (s, e) => _ = PickSession();
                _mgr.SessionsChanged += (s, e) => _ = PickSession();
                await PickSession();
            }
            catch (Exception ex) { Log.Write("Media init: " + ex.Message); }
        }

        async Task PickSession()
        {
            try
            {
                var sessions = _mgr.GetSessions();
                GlobalSystemMediaTransportControlsSession pick = null;
                foreach (var s in sessions)
                {
                    try
                    {
                        if (s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) { pick = s; break; }
                    }
                    catch { }
                }
                pick ??= _mgr.GetCurrentSession() ?? sessions.FirstOrDefault();

                if (!ReferenceEquals(pick, _session))
                {
                    if (_session != null)
                    {
                        _session.MediaPropertiesChanged -= OnProps;
                        _session.PlaybackInfoChanged -= OnPlayback;
                        _session.TimelinePropertiesChanged -= OnTimeline;
                    }
                    _session = pick;
                    if (_session != null)
                    {
                        _session.MediaPropertiesChanged += OnProps;
                        _session.PlaybackInfoChanged += OnPlayback;
                        _session.TimelinePropertiesChanged += OnTimeline;
                    }
                }
                await Refresh();
            }
            catch (Exception ex) { Log.Write("PickSession: " + ex.Message); }
        }

        void OnProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => _ = Refresh();
        void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) { ReadTimeline(); Changed?.Invoke(); }
        void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e)
        {
            // Another app may have started playing -> re-pick
            _ = PickSession();
        }

        void ReadTimeline()
        {
            try
            {
                var tl = _session?.GetTimelineProperties();
                if (tl == null) return;
                DurationSec = Math.Max(0, (tl.EndTime - tl.StartTime).TotalSeconds);
                _posSec = (tl.Position - tl.StartTime).TotalSeconds;
                _posStamp = tl.LastUpdatedTime.Year > 2000 ? tl.LastUpdatedTime : DateTimeOffset.Now;
            }
            catch { }
        }

        async Task Refresh()
        {
            await _refreshLock.WaitAsync();
            try
            {
                var s = _session;
                if (s == null)
                {
                    HasMedia = false; Playing = false; Title = Artist = Album = ""; Art = null; ArtJpeg = null; _artKey = "";
                    Changed?.Invoke();
                    return;
                }
                GlobalSystemMediaTransportControlsSessionMediaProperties props = null;
                try { props = await s.TryGetMediaPropertiesAsync(); } catch { }
                var pb = s.GetPlaybackInfo();
                Playing = pb?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                AppId = s.SourceAppUserModelId ?? "";
                Title = props?.Title ?? "";
                Artist = props?.Artist ?? "";
                Album = props?.AlbumTitle ?? "";
                HasMedia = !string.IsNullOrWhiteSpace(Title);
                ReadTimeline();

                string key = AppId + "|" + Title + "|" + Artist + "|" + Album;
                bool hasThumb = props?.Thumbnail != null;
                if (key != _artKey)
                {
                    _artKey = key;
                    _artRetries = 0;
                    await LoadArt(props);
                }
                else if (Art == null && hasThumb)
                {
                    // Browsers / Spotify often send the title first and the cover a moment later
                    await LoadArt(props);
                }
                if (Art == null && HasMedia && _artRetries < 4)
                {
                    int attempt = ++_artRetries;
                    var k = key;
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(attempt * 900);
                        if (_artKey == k && Art == null) await Refresh();
                    });
                }
                Changed?.Invoke();
            }
            catch (Exception ex) { Log.Write("Media refresh: " + ex.Message); }
            finally { _refreshLock.Release(); }
        }

        async Task LoadArt(GlobalSystemMediaTransportControlsSessionMediaProperties props)
        {
            Art = null; ArtJpeg = null;
            Accent = Color.FromRgb(0x0A, 0x84, 0xFF);
            try
            {
                if (props?.Thumbnail != null)
                {
                    using var ras = await props.Thumbnail.OpenReadAsync();
                    using var src = ras.AsStreamForRead();
                    var ms = new MemoryStream();
                    await src.CopyToAsync(ms);
                    ms.Position = 0;
                    if (ms.Length < 64) throw new Exception("empty thumbnail");
                    var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                    BitmapSource frame = dec.Frames[0];
                    if (frame.Format != PixelFormats.Bgra32 && frame.Format != PixelFormats.Bgr32)
                        frame = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                    // YouTube thumbnails often have black letterbox bars – trim them before cropping
                    frame = TrimBars(frame);
                    // center-crop to square (YouTube thumbnails are 16:9)
                    int w = frame.PixelWidth, h = frame.PixelHeight, side = Math.Min(w, h);
                    BitmapSource sq = new CroppedBitmap(frame, new System.Windows.Int32Rect((w - side) / 2, (h - side) / 2, side, side));
                    double scale = 300.0 / side;
                    if (scale < 1) sq = new TransformedBitmap(sq, new ScaleTransform(scale, scale));
                    sq.Freeze();
                    Art = sq;

                    var enc = new JpegBitmapEncoder { QualityLevel = 85 };
                    enc.Frames.Add(BitmapFrame.Create(sq));
                    using var outMs = new MemoryStream();
                    enc.Save(outMs);
                    ArtJpeg = outMs.ToArray();
                    Accent = ComputeAccent(sq);
                }
            }
            catch (Exception ex) { Log.Write("Art: " + ex.Message); }
            ArtVersion++;
        }

        static BitmapSource TrimBars(BitmapSource src)
        {
            try
            {
                int w = src.PixelWidth, h = src.PixelHeight;
                var px = new byte[w * h * 4];
                src.CopyPixels(px, w * 4, 0);
                bool Dark(int y)
                {
                    long sum = 0;
                    for (int x = 0; x < w; x += 4) { int i = (y * w + x) * 4; sum += px[i] + px[i + 1] + px[i + 2]; }
                    return sum / (double)((w + 3) / 4) < 30;
                }
                int top = 0, bottom = h - 1;
                while (top < h / 3 && Dark(top)) top++;
                while (bottom > h * 2 / 3 && Dark(bottom)) bottom--;
                if (top == 0 && bottom == h - 1) return src;
                var c = new CroppedBitmap(src, new System.Windows.Int32Rect(0, top, w, bottom - top + 1));
                c.Freeze();
                return c;
            }
            catch { return src; }
        }

        static Color ComputeAccent(BitmapSource src)
        {
            try
            {
                var small = new TransformedBitmap(src, new ScaleTransform(16.0 / src.PixelWidth, 16.0 / src.PixelHeight));
                var conv = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight;
                var px = new byte[w * h * 4];
                conv.CopyPixels(px, w * 4, 0);
                double r = 0, g = 0, b = 0, tw = 0;
                for (int i = 0; i < px.Length; i += 4)
                {
                    double B = px[i], G = px[i + 1], Rr = px[i + 2];
                    double max = Math.Max(Rr, Math.Max(G, B)), min = Math.Min(Rr, Math.Min(G, B));
                    double sat = max <= 0 ? 0 : (max - min) / max;
                    double wgt = 0.05 + sat * sat * (max / 255.0);
                    r += Rr * wgt; g += G * wgt; b += B * wgt; tw += wgt;
                }
                r /= tw; g /= tw; b /= tw;
                // brighten so it reads well on black
                double mx = Math.Max(r, Math.Max(g, b));
                if (mx < 1) return Color.FromRgb(0x0A, 0x84, 0xFF);
                double k = 235.0 / mx;
                return Color.FromRgb((byte)Math.Min(255, r * k), (byte)Math.Min(255, g * k), (byte)Math.Min(255, b * k));
            }
            catch { return Color.FromRgb(0x0A, 0x84, 0xFF); }
        }

        // ---------- Controls ----------
        public async Task TogglePlayPause() { try { if (_session != null) await _session.TryTogglePlayPauseAsync(); } catch { } }
        public async Task Play() { try { if (_session != null) await _session.TryPlayAsync(); } catch { } }
        public async Task Pause() { try { if (_session != null) await _session.TryPauseAsync(); } catch { } }
        public async Task Next() { try { if (_session != null) await _session.TrySkipNextAsync(); } catch { } }
        public async Task Prev() { try { if (_session != null) await _session.TrySkipPreviousAsync(); } catch { } }
        public async Task Seek(double sec)
        {
            try
            {
                if (_session == null) return;
                var tl = _session.GetTimelineProperties();
                long ticks = (tl.StartTime + TimeSpan.FromSeconds(Math.Max(0, sec))).Ticks;
                await _session.TryChangePlaybackPositionAsync(ticks);
            }
            catch { }
        }

        public string AppDisplayName
        {
            get
            {
                var a = AppId.ToLowerInvariant();
                if (a.Contains("spotify")) return "Spotify";
                if (a.Contains("chrome")) return "Chrome";
                if (a.Contains("msedge")) return "Edge";
                if (a.Contains("firefox")) return "Firefox";
                if (a.Contains("opera")) return "Opera";
                if (a.Contains("brave")) return "Brave";
                if (a.Contains("zune") || a.Contains("media")) return "Media Player";
                if (a.Contains("applemusic") || a.Contains("apple")) return "Apple Music";
                if (a.Contains("vlc")) return "VLC";
                var name = AppId;
                int i = name.LastIndexOf('\\'); if (i >= 0) name = name[(i + 1)..];
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
                return name;
            }
        }
    }
}

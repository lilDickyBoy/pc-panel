using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PcPanel.Core;

namespace PcPanel.Services
{
    /// <summary>
    /// Time-synced lyrics from LRCLIB (free, no API key). Results are cached on disk so a song is only
    /// looked up once. Lines are (seconds, text); an empty text means an instrumental gap.
    /// </summary>
    public sealed class LyricsService
    {
        public static readonly LyricsService I = new LyricsService();
        public event Action Changed;

        public IReadOnlyList<(double t, string text)> Lines { get; private set; } = Array.Empty<(double, string)>();
        public bool HasLyrics => Lines.Count > 0;
        public string TrackKey { get; private set; } = "";

        static readonly HttpClient Http = CreateHttp();
        string _key = "";
        CancellationTokenSource _cts;
        static string CacheDir => Path.Combine(AppSettings.Dir, "lyrics");

        static HttpClient CreateHttp()
        {
            var h = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(10) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("PC-Panel/" + App.Version + " (https://github.com/lilDickyBoy/pc-panel)");
            return h;
        }

        /// <summary>Call whenever the track may have changed. Cheap when it hasn't.</summary>
        public void OnTrack(string title, string artist, string album, double durationSec)
        {
            title ??= ""; artist ??= ""; album ??= "";
            string key = (title + "|" + artist).ToLowerInvariant();
            if (!App.Settings.Lyrics || string.IsNullOrWhiteSpace(title)) key = "";
            if (key == _key) return;
            _key = key;
            _cts?.Cancel();
            Lines = Array.Empty<(double, string)>();
            TrackKey = key;
            Changed?.Invoke();
            if (key.Length == 0) return;

            var cts = _cts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try
                {
                    // the duration often arrives a moment after the title
                    await Task.Delay(400, cts.Token);
                    double dur = durationSec > 0 ? durationSec : MediaService.I.DurationSec;
                    var lines = await Fetch(title, artist, album, dur, cts.Token);
                    if (cts.IsCancellationRequested || _key != key) return;
                    Lines = lines;
                    Changed?.Invoke();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Log.Write("Lyrics: " + ex.Message); }
            });
        }

        /// <summary>Index of the line being sung at this position (with a small lead so it feels on time).</summary>
        public int IndexAt(double pos)
        {
            var l = Lines;
            if (l.Count == 0 || pos + 0.25 < l[0].t) return -1;
            int lo = 0, hi = l.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (l[mid].t <= pos + 0.25) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        // ------------------------------------------------------------------ lookup
        async Task<IReadOnlyList<(double, string)>> Fetch(string title, string artist, string album, double dur, CancellationToken ct)
        {
            var (t, a) = Clean(title, artist);
            string cacheFile = Path.Combine(CacheDir, Hash(t + "|" + a) + ".lrc");
            try
            {
                if (File.Exists(cacheFile))
                {
                    var cached = await File.ReadAllTextAsync(cacheFile, ct);
                    return Parse(cached);   // an empty file means "no synced lyrics"
                }
            }
            catch { }

            string lrc = null;
            try
            {
                var url = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(t) + "&artist_name=" + Uri.EscapeDataString(a)
                          + (dur > 0 ? "&duration=" + Math.Round(dur).ToString(CultureInfo.InvariantCulture) : "");
                using var r = await Http.GetAsync(url, ct);
                if (r.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
                    lrc = Str(doc.RootElement, "syncedLyrics");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Log.Write("Lyrics get: " + ex.Message); }

            if (string.IsNullOrWhiteSpace(lrc))
            {
                try
                {
                    var url = "https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(t) + (a.Length > 0 ? "&artist_name=" + Uri.EscapeDataString(a) : "");
                    using var r = await Http.GetAsync(url, ct);
                    if (r.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
                        var best = doc.RootElement.EnumerateArray()
                            .Where(e => !string.IsNullOrWhiteSpace(Str(e, "syncedLyrics")))
                            .OrderBy(e => dur > 0 && e.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? Math.Abs(d.GetDouble() - dur) : 0)
                            .FirstOrDefault();
                        if (best.ValueKind == JsonValueKind.Object)
                        {
                            bool durOk = dur <= 0 || !best.TryGetProperty("duration", out var d2) || d2.ValueKind != JsonValueKind.Number || Math.Abs(d2.GetDouble() - dur) < 8;
                            if (durOk) lrc = Str(best, "syncedLyrics");
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Log.Write("Lyrics search: " + ex.Message); }
            }

            try
            {
                Directory.CreateDirectory(CacheDir);
                await File.WriteAllTextAsync(cacheFile, lrc ?? "", ct);
                TrimCache();
            }
            catch { }
            return Parse(lrc);
        }

        static string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        static readonly Regex TimeTag = new Regex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);

        public static IReadOnlyList<(double, string)> Parse(string lrc)
        {
            var list = new List<(double, string)>();
            if (string.IsNullOrWhiteSpace(lrc)) return list;
            foreach (var raw in lrc.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var tags = TimeTag.Matches(line);
                if (tags.Count == 0) continue;
                string text = TimeTag.Replace(line, "").Trim();
                foreach (Match m in tags)
                {
                    double t = int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value);
                    if (m.Groups[3].Success)
                    {
                        var f = m.Groups[3].Value;
                        t += int.Parse(f) / Math.Pow(10, f.Length);
                    }
                    list.Add((t, text));
                }
            }
            list.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            return list;
        }

        static readonly Regex Junk = new Regex(@"\s*[\(\[][^\)\]]*(official|video|audio|lyric|lyrics|visualizer|remaster|remastered|hd|4k|mv|m/v|live|clip|explicit)[^\)\]]*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>"Artist - Song (Official Video)" by "ArtistVEVO" → ("Song", "Artist"); strips " - Remastered 2011" etc.</summary>
        public static (string title, string artist) Clean(string title, string artist)
        {
            string t = Junk.Replace(title ?? "", "").Trim();
            string a = (artist ?? "").Replace(" - Topic", "").Trim();
            if (a.EndsWith("VEVO", StringComparison.OrdinalIgnoreCase)) a = a.Substring(0, a.Length - 4).Trim();
            int dash = t.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0)
            {
                string left = t.Substring(0, dash).Trim(), right = t.Substring(dash + 3).Trim();
                if (Regex.IsMatch(right, @"^(\d{4} )?remaster|^remaster|^live|^radio edit|^single version|^mono|^stereo", RegexOptions.IgnoreCase)) t = left;
                else if (a.Length == 0 || left.Equals(a, StringComparison.OrdinalIgnoreCase) || !LooksLikeArtist(a)) { a = left; t = right; }
            }
            t = Regex.Replace(t, @"\s*\(feat\.[^\)]*\)|\s*ft\..*$|\s*feat\..*$", "", RegexOptions.IgnoreCase).Trim();
            int comma = a.IndexOf(',');
            if (comma > 0) a = a.Substring(0, comma).Trim();   // first artist matches best
            return (t, a);
        }

        // a YouTube channel name like "Some Music Channel" is not a real artist
        static bool LooksLikeArtist(string a) => !Regex.IsMatch(a, @"music|records|channel|official|tv|lyrics|songs", RegexOptions.IgnoreCase);

        static string Hash(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s.ToLowerInvariant()))).Substring(0, 16);

        static void TrimCache()
        {
            try
            {
                var files = new DirectoryInfo(CacheDir).GetFiles("*.lrc");
                if (files.Length <= 800) return;
                foreach (var f in files.OrderBy(f => f.LastAccessTimeUtc).Take(files.Length - 600)) f.Delete();
            }
            catch { }
        }
    }
}

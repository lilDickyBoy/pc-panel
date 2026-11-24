using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PcPanel.Core;

namespace PcPanel.Services
{
    /// <summary>
    /// Spotify Web API (needs Premium for playback control).
    /// Login uses Authorization Code + PKCE with a loopback redirect, so no client secret is stored.
    /// The user creates a free app at developer.spotify.com and pastes its Client ID into PC Panel.
    /// </summary>
    public sealed class SpotifyService
    {
        public static readonly SpotifyService I = new SpotifyService();

        public const string RedirectUri = "http://127.0.0.1:47803/callback";
        const string Scopes = "user-read-playback-state user-modify-playback-state user-read-currently-playing " +
                              "playlist-read-private playlist-read-collaborative user-library-read user-read-recently-played user-read-private";

        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        string _access;
        DateTime _accessExp = DateTime.MinValue;
        readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);
        HttpListener _listener;

        public bool Connected => !string.IsNullOrEmpty(App.Settings?.SpotifyRefresh);

        /// <summary>Raised when login state changes.</summary>
        public event Action StateChanged;

        // ============================================================ login
        static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public async Task<bool> LoginAsync()
        {
            var clientId = App.Settings.SpotifyClientId?.Trim();
            if (string.IsNullOrEmpty(clientId)) return false;

            var verifier = B64Url(RandomNumberGenerator.GetBytes(48));
            var challenge = B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var state = B64Url(RandomNumberGenerator.GetBytes(12));

            try { _listener?.Stop(); } catch { }
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://127.0.0.1:47803/");
            try { _listener.Start(); }
            catch (Exception ex) { Log.Write("Spotify listener: " + ex.Message); return false; }

            var url = "https://accounts.spotify.com/authorize?response_type=code" +
                      "&client_id=" + Uri.EscapeDataString(clientId) +
                      "&scope=" + Uri.EscapeDataString(Scopes) +
                      "&redirect_uri=" + Uri.EscapeDataString(RedirectUri) +
                      "&code_challenge_method=S256&code_challenge=" + challenge +
                      "&state=" + state;
            // Open a short local address instead of the long Spotify URL: passing a URL with a query
            // string through explorer.exe can drop the parameters (Spotify then says "redirect_uri: Not present").
            // Our listener answers /login with a redirect to the full authorize URL.
            SystemSetup.ShellOpen("http://127.0.0.1:47803/login");

            try
            {
                var deadline = DateTime.UtcNow.AddMinutes(5);
                HttpListenerContext ctx = null;
                while (ctx == null)
                {
                    var ctxTask = _listener.GetContextAsync();
                    var left = deadline - DateTime.UtcNow;
                    if (left <= TimeSpan.Zero || await Task.WhenAny(ctxTask, Task.Delay(left)) != ctxTask) { _listener.Stop(); return false; }
                    var c = ctxTask.Result;
                    var path = c.Request.Url?.AbsolutePath ?? "";
                    if (path.StartsWith("/callback", StringComparison.OrdinalIgnoreCase)) { ctx = c; break; }
                    if (path.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
                    {
                        c.Response.StatusCode = 302;
                        c.Response.RedirectLocation = url;
                        c.Response.AddHeader("Cache-Control", "no-store");
                    }
                    else c.Response.StatusCode = 404;
                    c.Response.Close();
                }
                var q = System.Web.HttpUtility.ParseQueryString(ctx.Request.Url.Query);
                string code = q["code"], gotState = q["state"], err = q["error"];
                bool ok = code != null && gotState == state;
                string html = "<html><body style='background:#000;color:#fff;font-family:Segoe UI,sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0'>" +
                              "<div style='text-align:center'><div style='font-size:64px'>" + (ok ? "&#10003;" : "&#10007;") + "</div><h2>PC Panel</h2><p>" +
                              WebUtility.HtmlEncode(ok ? Loc.T("pc.spotify.browserOk") : Loc.T("pc.spotify.browserFail") + (err != null ? " (" + err + ")" : "")) +
                              "</p></div></body></html>";
                var bytes = Encoding.UTF8.GetBytes(html);
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
                _listener.Stop();
                if (!ok) return false;

                var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = RedirectUri,
                    ["client_id"] = clientId,
                    ["code_verifier"] = verifier
                });
                var resp = await Http.PostAsync("https://accounts.spotify.com/api/token", form);
                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) { Log.Write("Spotify token: " + body); return false; }
                var j = JsonNode.Parse(body);
                _access = j?["access_token"]?.GetValue<string>();
                _accessExp = DateTime.UtcNow.AddSeconds((j?["expires_in"]?.GetValue<int>() ?? 3600) - 60);
                var refresh = j?["refresh_token"]?.GetValue<string>();
                App.Settings.SpotifyRefresh = AppSettings.Protect(refresh);
                var me = await GetAsync("https://api.spotify.com/v1/me");
                App.Settings.SpotifyUser = me?["display_name"]?.GetValue<string>() ?? me?["id"]?.GetValue<string>() ?? "";
                App.Settings.Save();
                StateChanged?.Invoke();
                return true;
            }
            catch (Exception ex) { Log.Write("Spotify login: " + ex.Message); try { _listener.Stop(); } catch { } return false; }
        }

        public void Logout()
        {
            _access = null;
            App.Settings.SpotifyRefresh = "";
            App.Settings.SpotifyUser = "";
            App.Settings.Save();
            StateChanged?.Invoke();
        }

        async Task<string> TokenAsync()
        {
            if (_access != null && DateTime.UtcNow < _accessExp) return _access;
            await _tokenLock.WaitAsync();
            try
            {
                if (_access != null && DateTime.UtcNow < _accessExp) return _access;
                var refresh = AppSettings.Unprotect(App.Settings.SpotifyRefresh);
                if (string.IsNullOrEmpty(refresh)) return null;
                var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refresh,
                    ["client_id"] = App.Settings.SpotifyClientId?.Trim() ?? ""
                });
                var resp = await Http.PostAsync("https://accounts.spotify.com/api/token", form);
                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    Log.Write("Spotify refresh: " + body);
                    if ((int)resp.StatusCode == 400) Logout(); // revoked
                    return null;
                }
                var j = JsonNode.Parse(body);
                _access = j?["access_token"]?.GetValue<string>();
                _accessExp = DateTime.UtcNow.AddSeconds((j?["expires_in"]?.GetValue<int>() ?? 3600) - 60);
                var newRefresh = j?["refresh_token"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(newRefresh)) { App.Settings.SpotifyRefresh = AppSettings.Protect(newRefresh); App.Settings.Save(); }
                return _access;
            }
            catch (Exception ex) { Log.Write("Spotify token: " + ex.Message); return null; }
            finally { _tokenLock.Release(); }
        }

        async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string json = null)
        {
            var tok = await TokenAsync();
            if (tok == null) return null;
            var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tok);
            if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return await Http.SendAsync(req);
        }

        async Task<JsonNode> GetAsync(string url)
        {
            try
            {
                using var r = await SendAsync(HttpMethod.Get, url);
                if (r == null || !r.IsSuccessStatusCode) return null;
                var s = await r.Content.ReadAsStringAsync();
                return string.IsNullOrWhiteSpace(s) ? null : JsonNode.Parse(s);
            }
            catch (Exception ex) { Log.Write("Spotify GET: " + ex.Message); return null; }
        }

        // ============================================================ browse
        static string Str(JsonNode n, string k) { try { return n?[k]?.GetValue<string>() ?? ""; } catch { return ""; } }

        static string Img(JsonNode n)
        {
            try
            {
                var imgs = n?["images"] as JsonArray ?? n?["album"]?["images"] as JsonArray;
                if (imgs == null || imgs.Count == 0) return "";
                // pick the smallest image that is at least 160 px
                JsonNode best = imgs[0];
                foreach (var i in imgs)
                {
                    int w = 0; try { w = i?["width"]?.GetValue<int>() ?? 0; } catch { }
                    if (w >= 160) best = i;
                }
                return Str(best, "url");
            }
            catch { return ""; }
        }

        static string Artists(JsonNode n)
        {
            try { return string.Join(", ", (n?["artists"] as JsonArray ?? new JsonArray()).Select(a => Str(a, "name"))); }
            catch { return ""; }
        }

        static object Track(JsonNode t, string context = "")
        {
            return new { kind = "track", id = Str(t, "id"), uri = Str(t, "uri"), title = Str(t, "name"), sub = Artists(t), img = Img(t), ctx = context };
        }

        public async Task<object> SearchAsync(string q)
        {
            var j = await GetAsync("https://api.spotify.com/v1/search?type=track,playlist,album,artist&limit=12&q=" + Uri.EscapeDataString(q));
            var items = new List<object>();
            if (j == null) return new { type = "musicResults", src = "spotify", q, items, error = Connected ? "fail" : "login" };
            foreach (var t in (j["tracks"]?["items"] as JsonArray ?? new JsonArray()).Take(12)) if (t != null) items.Add(Track(t));
            foreach (var a in (j["artists"]?["items"] as JsonArray ?? new JsonArray()).Take(4)) if (a != null) items.Add(new { kind = "artist", id = Str(a, "id"), uri = Str(a, "uri"), title = Str(a, "name"), sub = "", img = Img(a), ctx = "" });
            foreach (var a in (j["albums"]?["items"] as JsonArray ?? new JsonArray()).Take(6)) if (a != null) items.Add(new { kind = "album", id = Str(a, "id"), uri = Str(a, "uri"), title = Str(a, "name"), sub = Artists(a), img = Img(a), ctx = "" });
            foreach (var p in (j["playlists"]?["items"] as JsonArray ?? new JsonArray()).Take(6)) if (p != null) items.Add(new { kind = "playlist", id = Str(p, "id"), uri = Str(p, "uri"), title = Str(p, "name"), sub = Str(p["owner"], "display_name"), img = Img(p), ctx = "" });
            return new { type = "musicResults", src = "spotify", q, items };
        }

        public async Task<object> HomeAsync()
        {
            var sections = new List<object>();
            if (!Connected) return new { type = "musicHome", src = "spotify", sections, error = "login" };
            var recent = await GetAsync("https://api.spotify.com/v1/me/player/recently-played?limit=20");
            var recentItems = new List<object>();
            var seen = new HashSet<string>();
            foreach (var it in recent?["items"] as JsonArray ?? new JsonArray())
            {
                var t = it?["track"]; if (t == null) continue;
                if (seen.Add(Str(t, "id"))) recentItems.Add(Track(t));
            }
            if (recentItems.Count > 0) sections.Add(new { title = "recent", items = recentItems });

            var pls = await GetAsync("https://api.spotify.com/v1/me/playlists?limit=30");
            var plItems = new List<object> { new { kind = "liked", id = "liked", uri = "liked", title = "Liked Songs", sub = "", img = "", ctx = "" } };
            foreach (var p in pls?["items"] as JsonArray ?? new JsonArray())
                if (p != null) plItems.Add(new { kind = "playlist", id = Str(p, "id"), uri = Str(p, "uri"), title = Str(p, "name"), sub = Str(p["owner"], "display_name"), img = Img(p), ctx = "" });
            sections.Add(new { title = "playlists", items = plItems });
            return new { type = "musicHome", src = "spotify", sections, user = App.Settings.SpotifyUser };
        }

        public async Task<object> ListAsync(string kind, string id)
        {
            var items = new List<object>();
            string title = "";
            try
            {
                if (kind == "liked")
                {
                    var j = await GetAsync("https://api.spotify.com/v1/me/tracks?limit=50");
                    foreach (var it in j?["items"] as JsonArray ?? new JsonArray()) if (it?["track"] != null) items.Add(Track(it["track"]));
                    title = "Liked Songs";
                }
                else if (kind == "playlist")
                {
                    var p = await GetAsync("https://api.spotify.com/v1/playlists/" + id + "?fields=name,uri,tracks.items(track(id,uri,name,artists(name),album(images)))");
                    title = Str(p, "name");
                    var ctx = Str(p, "uri");
                    foreach (var it in p?["tracks"]?["items"] as JsonArray ?? new JsonArray()) if (it?["track"] != null) items.Add(Track(it["track"], ctx));
                }
                else if (kind == "album")
                {
                    var a = await GetAsync("https://api.spotify.com/v1/albums/" + id);
                    title = Str(a, "name");
                    var ctx = Str(a, "uri");
                    foreach (var t in a?["tracks"]?["items"] as JsonArray ?? new JsonArray())
                        if (t != null) items.Add(new { kind = "track", id = Str(t, "id"), uri = Str(t, "uri"), title = Str(t, "name"), sub = Artists(t), img = Img(a), ctx });
                }
                else if (kind == "artist")
                {
                    var a = await GetAsync("https://api.spotify.com/v1/artists/" + id);
                    title = Str(a, "name");
                    var top = await GetAsync("https://api.spotify.com/v1/artists/" + id + "/top-tracks?market=from_token");
                    foreach (var t in top?["tracks"] as JsonArray ?? new JsonArray()) if (t != null) items.Add(Track(t));
                }
            }
            catch (Exception ex) { Log.Write("Spotify list: " + ex.Message); }
            return new { type = "musicList", src = "spotify", kind, id, title, items };
        }

        // ============================================================ playback
        async Task<string> DeviceIdAsync()
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var j = await GetAsync("https://api.spotify.com/v1/me/player/devices");
                var devs = j?["devices"] as JsonArray ?? new JsonArray();
                JsonNode pick = null;
                foreach (var d in devs) if (d != null && Str(d, "type") == "Computer" && string.Equals(Str(d, "name"), Environment.MachineName, StringComparison.OrdinalIgnoreCase)) pick = d;
                if (pick == null) foreach (var d in devs) if (d != null && Str(d, "type") == "Computer") { pick = d; break; }
                if (pick == null) foreach (var d in devs) { try { if (d?["is_active"]?.GetValue<bool>() == true) { pick = d; break; } } catch { } }
                if (pick != null) return Str(pick, "id");
                if (attempt == 0)
                {
                    // Spotify isn't running on the PC – start it
                    if (Process.GetProcessesByName("Spotify").Length == 0) SystemSetup.ShellOpen("spotify:");
                }
                await Task.Delay(1000);
            }
            return null;
        }

        /// <returns>"" when ok, otherwise an error code: login, nodevice, premium, fail</returns>
        public async Task<string> PlayAsync(string kind, string uri, string ctx)
        {
            if (!Connected) return "login";
            var dev = await DeviceIdAsync();
            if (dev == null) return "nodevice";
            string body;
            if (kind == "track" && !string.IsNullOrEmpty(ctx)) body = new JsonObject { ["context_uri"] = ctx, ["offset"] = new JsonObject { ["uri"] = uri } }.ToJsonString();
            else if (kind == "track") { var one = new JsonArray(); one.Add(uri); body = new JsonObject { ["uris"] = one }.ToJsonString(); }
            else if (kind == "liked")
            {
                var j = await GetAsync("https://api.spotify.com/v1/me/tracks?limit=50");
                var uris = new JsonArray();
                foreach (var it in j?["items"] as JsonArray ?? new JsonArray()) { var u = Str(it?["track"], "uri"); if (u != "") uris.Add(u); }
                body = new JsonObject { ["uris"] = uris }.ToJsonString();
            }
            else body = new JsonObject { ["context_uri"] = uri }.ToJsonString();

            try
            {
                using var r = await SendAsync(HttpMethod.Put, "https://api.spotify.com/v1/me/player/play?device_id=" + Uri.EscapeDataString(dev), body);
                if (r == null) return "login";
                if (r.IsSuccessStatusCode) return "";
                var err = await r.Content.ReadAsStringAsync();
                Log.Write("Spotify play: " + (int)r.StatusCode + " " + err);
                if (err.Contains("PREMIUM")) return "premium";
                return "fail";
            }
            catch (Exception ex) { Log.Write("Spotify play: " + ex.Message); return "fail"; }
        }
    }

    // ================================================================== YouTube
    public static class YouTubeService
    {
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        public static async Task<object> SearchAsync(string q)
        {
            var items = new List<object>();
            var key = App.Settings.YouTubeKey?.Trim();
            if (string.IsNullOrEmpty(key)) return new { type = "musicResults", src = "youtube", q, items, error = "nokey" };
            try
            {
                var url = "https://www.googleapis.com/youtube/v3/search?part=snippet&type=video&maxResults=25&q=" + Uri.EscapeDataString(q) + "&key=" + Uri.EscapeDataString(key);
                var s = await Http.GetStringAsync(url);
                var j = JsonNode.Parse(s);
                foreach (var it in j?["items"] as JsonArray ?? new JsonArray())
                {
                    var id = it?["id"]?["videoId"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(id)) continue;
                    var sn = it["snippet"];
                    items.Add(new
                    {
                        kind = "video",
                        id,
                        uri = id,
                        title = WebUtility.HtmlDecode(sn?["title"]?.GetValue<string>() ?? ""),
                        sub = WebUtility.HtmlDecode(sn?["channelTitle"]?.GetValue<string>() ?? ""),
                        img = sn?["thumbnails"]?["medium"]?["url"]?.GetValue<string>() ?? "",
                        ctx = ""
                    });
                }
                return new { type = "musicResults", src = "youtube", q, items };
            }
            catch (Exception ex)
            {
                Log.Write("YouTube: " + ex.Message);
                return new { type = "musicResults", src = "youtube", q, items, error = "fail" };
            }
        }

        public static void Play(string id) => SystemSetup.ShellOpen("https://www.youtube.com/watch?v=" + Uri.EscapeDataString(id));
        public static void SearchInBrowser(string q) => SystemSetup.ShellOpen("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(q));
    }
}

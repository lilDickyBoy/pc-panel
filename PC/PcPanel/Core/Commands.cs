using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using PcPanel.Services;

namespace PcPanel.Core
{
    /// <summary>Handles every message coming from the phone.</summary>
    public static class Commands
    {
        static string S(JsonObject m, string k, string d = "") { try { return m[k]?.GetValue<string>() ?? d; } catch { return d; } }
        static double D(JsonObject m, string k, double d = 0) { try { return m[k]?.GetValue<double>() ?? d; } catch { return d; } }
        static bool B(JsonObject m, string k, bool d = false) { try { return m[k]?.GetValue<bool>() ?? d; } catch { return d; } }

        public static object BuildWelcome()
        {
            var net = App.Net;
            return new
            {
                type = "welcome",
                pcName = App.Settings.PcName,
                pcId = App.Settings.PcId,
                mac = net.Mac,
                bcast = net.Broadcast,
                os = "Windows",
                version = App.Version,
                cpuAlert = App.Settings.CpuAlert,
                gpuAlert = App.Settings.GpuAlert
            };
        }

        public static object MediaState()
        {
            var m = MediaService.I;
            return new
            {
                type = "media",
                has = m.HasMedia,
                title = m.Title,
                artist = m.Artist,
                album = m.Album,
                app = m.AppDisplayName,
                playing = m.Playing,
                pos = Math.Round(m.PositionSec, 1),
                dur = Math.Round(m.DurationSec, 1),
                artId = m.ArtJpeg != null ? m.ArtVersion : 0,
                color = $"#{m.Accent.R:X2}{m.Accent.G:X2}{m.Accent.B:X2}"
            };
        }

        public static object ArtState()
        {
            var m = MediaService.I;
            return new { type = "art", artId = m.ArtVersion, data = m.ArtJpeg != null ? Convert.ToBase64String(m.ArtJpeg) : "" };
        }

        public static object VolumeState() => new { type = "vol", level = Math.Round(AudioService.I.Level, 3), muted = AudioService.I.Muted, output = AudioService.I.OutputName };

        public static void PushInitialState(Session s)
        {
            _ = s.SendAsync(SharedSettings.Snapshot());
            _ = s.SendAsync(MediaState());
            if (MediaService.I.ArtJpeg != null) _ = s.SendAsync(ArtState());
            _ = s.SendAsync(VolumeState());
            _ = s.SendAsync(PowerService.I.State());
            _ = s.SendAsync(CountdownService.I.State());
            SensorService.I.Wake();
        }

        static Task<T> OnUi<T>(Func<T> f) => App.UiGet(f);

        public static async Task Handle(Session s, string type, JsonObject m)
        {
            switch (type)
            {
                // ------------------------------------------------ media
                case "media":
                    switch (S(m, "action"))
                    {
                        case "toggle": await MediaService.I.TogglePlayPause(); break;
                        case "play": await MediaService.I.Play(); break;
                        case "pause": await MediaService.I.Pause(); break;
                        case "next": await MediaService.I.Next(); break;
                        case "prev": await MediaService.I.Prev(); break;
                        case "seek": await MediaService.I.Seek(D(m, "pos")); break;
                    }
                    break;

                case "volume":
                    {
                        float lvl = (float)D(m, "level", -1);
                        App.Ui(() => { if (lvl >= 0) AudioService.I.SetLevel(lvl); });
                        break;
                    }
                case "mute":
                    {
                        bool v = B(m, "value");
                        App.Ui(() => AudioService.I.SetMute(v));
                        break;
                    }

                // ------------------------------------------------ mixer & outputs
                case "mixer":
                    await s.SendAsync(await OnUi(() => AudioService.I.Mixer()));
                    break;
                case "mixerSet":
                    {
                        string id = S(m, "id");
                        float vol = (float)D(m, "vol", -1);
                        bool? mute = m["mute"] != null ? B(m, "mute") : (bool?)null;
                        await OnUi(() => { AudioService.I.SetSession(id, vol, mute); return true; });
                        break;
                    }
                case "outputs":
                    await s.SendAsync(await OnUi(() => AudioService.I.Outputs()));
                    break;
                case "outputSet":
                    {
                        string id = S(m, "id");
                        bool ok = await OnUi(() => AudioService.I.SetOutput(id));
                        if (!ok) await s.SendAsync(new { type = "result", what = "output", ok });
                        await s.SendAsync(await OnUi(() => AudioService.I.Outputs()));
                        await s.SendAsync(VolumeState());
                        App.Ui(() => App.Notch?.ShowInfo(Glyph.Headphone, AudioService.I.OutputName, 2.5));
                        break;
                    }

                // ------------------------------------------------ power
                case "power":
                    PowerService.I.Do(S(m, "action"));
                    break;
                case "timer":
                    PowerService.I.SetTimer(S(m, "action", "sleep"), (int)D(m, "minutes"));
                    break;
                case "timerCancel":
                    PowerService.I.Cancel();
                    break;

                // ------------------------------------------------ input
                case "mouse": InputService.Move((int)Math.Round(D(m, "dx")), (int)Math.Round(D(m, "dy"))); break;
                case "click": InputService.Click(S(m, "button", "left"), B(m, "double")); break;
                case "button": InputService.Button(S(m, "button", "left"), B(m, "down")); break;
                case "scroll": InputService.Scroll((int)D(m, "dy"), (int)D(m, "dx")); break;
                case "text": InputService.Text(S(m, "text")); break;
                case "keys":
                    {
                        var arr = m["keys"] as JsonArray;
                        if (arr != null) InputService.Combo(arr.Select(x => x?.GetValue<string>() ?? "").ToList());
                        break;
                    }
                case "open":
                    {
                        var target = S(m, "target").Trim();
                        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            SystemSetup.ShellOpen(target);
                            App.Ui(() => App.Notch?.ShowInfo(Glyph.Globe, Loc.T("pc.open.link"), 2.2));
                        }
                        break;
                    }

                // ------------------------------------------------ screen
                case "screenshot":
                    {
                        var data = await Task.Run(ScreenService.ScreenshotBase64);
                        await s.SendAsync(new { type = "shot", data });
                        break;
                    }
                case "screenStart":
                    ScreenService.Start(s, (int)D(m, "fps", 6), (int)D(m, "quality", 55));
                    break;
                case "screenStop":
                    ScreenService.Stop(s);
                    break;
                case "clickAt":
                    ScreenService.ClickAt(D(m, "x"), D(m, "y"), S(m, "button", "left"), B(m, "double"));
                    break;

                // ------------------------------------------------ processes & apps
                case "procs":
                    await s.SendAsync(await Task.Run(ProcessService.List));
                    break;
                case "kill":
                    {
                        bool ok = await Task.Run(() => ProcessService.Kill(S(m, "name")));
                        await s.SendAsync(new { type = "result", what = "kill", ok });
                        await Task.Delay(400);
                        await s.SendAsync(await Task.Run(ProcessService.List));
                        break;
                    }
                case "apps":
                    await s.SendAsync(await AppsService.ListAsync());
                    break;
                case "icon":
                    await s.SendAsync(await AppsService.IconAsync(S(m, "id")));
                    break;
                case "launch":
                    {
                        bool ok = await AppsService.LaunchAsync(S(m, "id"));
                        await s.SendAsync(new { type = "result", what = "launch", ok });
                        break;
                    }

                // ------------------------------------------------ display
                case "displayGet":
                    {
                        int b = await Task.Run(DisplayService.GetBrightness);
                        await s.SendAsync(new { type = "display", brightness = b, night = DisplayService.NightLevel });
                        break;
                    }
                case "brightness":
                    {
                        int v = (int)D(m, "value", 50);
                        bool ok = await Task.Run(() => DisplayService.SetBrightness(v));
                        if (!ok) await s.SendAsync(new { type = "result", what = "brightness", ok });
                        break;
                    }
                case "nightlight":
                    {
                        int v = (int)D(m, "value");
                        bool ok = DisplayService.SetNight(v);
                        if (!ok) await s.SendAsync(new { type = "result", what = "nightlight", ok });
                        break;
                    }

                // ------------------------------------------------ settings sync
                case "set":
                    {
                        var values = m["values"] as JsonObject;
                        if (values == null) break;
                        var list = values.Select(kv => new KeyValuePair<string, JsonNode>(kv.Key, kv.Value?.DeepClone())).ToList();
                        App.Ui(() => SharedSettings.Apply(list));
                        break;
                    }
                case "alerts": // v1 phones
                    {
                        var list = new List<KeyValuePair<string, JsonNode>>
                        {
                            new KeyValuePair<string, JsonNode>("cpuAlert", JsonValue.Create(D(m, "cpu", 90))),
                            new KeyValuePair<string, JsonNode>("gpuAlert", JsonValue.Create(D(m, "gpu", 85)))
                        };
                        App.Ui(() => SharedSettings.Apply(list));
                        break;
                    }

                // ------------------------------------------------ phone → PC info
                case "phone": PhoneService.OnPhone(s, m); break;
                case "notif": PhoneService.OnNotification(m); break;
                case "call": PhoneService.OnCall(m); break;
                case "clip": ClipboardService.Receive(S(m, "text"), B(m, "explicit")); break;

                // ------------------------------------------------ files
                case "fileStart": await FileService.OnStart(s, m); break;
                case "fileChunk": await FileService.OnChunk(s, m); break;
                case "fileEnd": await FileService.OnEnd(s, m); break;

                // ------------------------------------------------ countdown in the notch
                case "countdown":
                    CountdownService.I.Start((int)D(m, "seconds", 60), S(m, "label"));
                    break;
                case "countdownCancel":
                    CountdownService.I.Cancel();
                    break;

                // ------------------------------------------------ sensors extras
                case "history":
                    await s.SendAsync(SensorService.I.History());
                    break;
                case "fanSet":
                    FanService.I.Set(S(m, "group"), S(m, "mode", "auto"), (float)D(m, "pct", 60));
                    break;

                // ------------------------------------------------ music
                case "musicHome":
                    if (S(m, "src") == "youtube") await s.SendAsync(new { type = "musicHome", src = "youtube", sections = new object[0], hasKey = !string.IsNullOrWhiteSpace(App.Settings.YouTubeKey) });
                    else await s.SendAsync(await SpotifyService.I.HomeAsync());
                    break;
                case "musicSearch":
                    {
                        var q = S(m, "q").Trim();
                        if (q.Length == 0) break;
                        if (S(m, "src") == "youtube") await s.SendAsync(await YouTubeService.SearchAsync(q));
                        else await s.SendAsync(await SpotifyService.I.SearchAsync(q));
                        break;
                    }
                case "musicList":
                    await s.SendAsync(await SpotifyService.I.ListAsync(S(m, "kind"), S(m, "id")));
                    break;
                case "musicPlay":
                    {
                        string src = S(m, "src"), kind = S(m, "kind"), uri = S(m, "uri"), ctx = S(m, "ctx");
                        if (src == "youtube")
                        {
                            if (kind == "search") YouTubeService.SearchInBrowser(uri);
                            else YouTubeService.Play(uri);
                            await s.SendAsync(new { type = "musicPlayed", ok = true, error = "" });
                        }
                        else
                        {
                            var err = await SpotifyService.I.PlayAsync(kind, uri, ctx);
                            await s.SendAsync(new { type = "musicPlayed", ok = err == "", error = err });
                        }
                        break;
                    }
                case "spotifyLogin":
                    {
                        if (string.IsNullOrWhiteSpace(App.Settings.SpotifyClientId))
                        {
                            await s.SendAsync(new { type = "spotifyState", connected = false, error = "noclient" });
                            App.Ui(() => App.ShowMainPage("music"));
                            break;
                        }
                        bool ok = await SpotifyService.I.LoginAsync();
                        await s.SendAsync(new { type = "spotifyState", connected = ok, error = ok ? "" : "fail" });
                        App.Ui(SharedSettings.Push);
                        break;
                    }
                case "spotifyLogout":
                    SpotifyService.I.Logout();
                    App.Ui(SharedSettings.Push);
                    break;

                case "rate":
                    s.StatsInterval = Math.Clamp((int)D(m, "ms", 1000), 500, 10000);
                    SensorService.I.Wake();
                    break;

                case "ping":
                    await s.SendAsync(new { type = "pong", t = D(m, "t") });
                    break;
            }
        }
    }
}

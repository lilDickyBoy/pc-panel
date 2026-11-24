using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using PcPanel.Core;

namespace PcPanel.Services
{
    /// <summary>
    /// File transfer in both directions, in 256 KB chunks.
    /// Phone → PC: fileStart / fileChunk (acked) / fileEnd. Files land in Downloads\PC Panel.
    /// PC → phone: the same messages the other way round.
    /// </summary>
    public static class FileService
    {
        public const int ChunkSize = 256 * 1024;
        const long MaxSize = 4L * 1024 * 1024 * 1024;

        sealed class Incoming { public FileStream Stream; public string Path; public string Name; public long Size; public long Got; }
        static readonly ConcurrentDictionary<string, Incoming> _in = new ConcurrentDictionary<string, Incoming>();

        /// <summary>(fileName, 0..1, done) – raised on any thread for sends started from the PC.</summary>
        public static event Action<string, double, bool> SendProgress;

        public static string Folder
        {
            get
            {
                var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                var dir = Path.Combine(downloads, "PC Panel");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        static string Safe(string name)
        {
            name = Path.GetFileName(name ?? "file");
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (string.IsNullOrWhiteSpace(name) || name == "." || name == "..") name = "file";
            return name.Length > 150 ? name.Substring(name.Length - 150) : name;
        }

        static string Unique(string dir, string name)
        {
            var p = Path.Combine(dir, name);
            if (!File.Exists(p)) return p;
            var bare = Path.GetFileNameWithoutExtension(name);
            var ext = Path.GetExtension(name);
            for (int i = 2; ; i++)
            {
                p = Path.Combine(dir, bare + " (" + i + ")" + ext);
                if (!File.Exists(p)) return p;
            }
        }

        static string S(JsonObject m, string k) { try { return m[k]?.GetValue<string>() ?? ""; } catch { return ""; } }
        static long L(JsonObject m, string k) { try { return (long)(m[k]?.GetValue<double>() ?? 0); } catch { return 0; } }

        public static async Task OnStart(Session s, JsonObject m)
        {
            var id = S(m, "id");
            long size = L(m, "size");
            if (string.IsNullOrEmpty(id) || size < 0 || size > MaxSize) { await s.SendAsync(new { type = "fileAck", id, idx = -1, ok = false }); return; }
            var name = Safe(S(m, "name"));
            var path = Unique(Folder, name);
            var inc = new Incoming { Path = path, Name = Path.GetFileName(path), Size = size, Stream = new FileStream(path + ".part", FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16) };
            _in[id] = inc;
            await s.SendAsync(new { type = "fileAck", id, idx = -1, ok = true });
        }

        public static async Task OnChunk(Session s, JsonObject m)
        {
            var id = S(m, "id");
            int idx = (int)L(m, "idx");
            if (!_in.TryGetValue(id, out var inc)) { await s.SendAsync(new { type = "fileAck", id, idx, ok = false }); return; }
            var bytes = Convert.FromBase64String(S(m, "data"));
            inc.Got += bytes.Length;
            if (inc.Got > inc.Size + ChunkSize) { Abort(id); await s.SendAsync(new { type = "fileAck", id, idx, ok = false }); return; }
            await inc.Stream.WriteAsync(bytes);
            await s.SendAsync(new { type = "fileAck", id, idx, ok = true });
        }

        public static Task OnEnd(Session s, JsonObject m)
        {
            var id = S(m, "id");
            if (!_in.TryRemove(id, out var inc)) return Task.CompletedTask;
            bool cancelled = m["cancel"] != null;
            try { inc.Stream.Dispose(); } catch { }
            if (cancelled) { try { File.Delete(inc.Path + ".part"); } catch { } return Task.CompletedTask; }
            try { File.Move(inc.Path + ".part", inc.Path, true); } catch (Exception ex) { Log.Write("File finish: " + ex.Message); }
            var name = inc.Name;
            App.Ui(() =>
            {
                App.Notch?.ShowInfo(Glyph.Download, Loc.F("pc.file.received", name), 3.5);
                SoundService.Play("file");
                App.ShowFileToast(name, inc.Path);
            });
            return Task.CompletedTask;
        }

        static void Abort(string id)
        {
            if (_in.TryRemove(id, out var inc))
            {
                try { inc.Stream.Dispose(); } catch { }
                try { File.Delete(inc.Path + ".part"); } catch { }
            }
        }

        public static void AbortAll()
        {
            foreach (var k in _in.Keys.ToArray()) Abort(k);
        }

        // ---- acknowledgements from the phone for PC → phone transfers (newer phone apps send them) ----
        static readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _acks = new ConcurrentDictionary<string, TaskCompletionSource<bool>>();

        static TaskCompletionSource<bool> Expect(string id, int idx)
        {
            var t = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _acks[id + ":" + idx] = t;
            return t;
        }

        public static void OnAck(JsonObject m)
        {
            var key = S(m, "id") + ":" + (int)L(m, "idx");
            bool ok = true;
            try { ok = m["ok"]?.GetValue<bool>() ?? true; } catch { }
            if (_acks.TryRemove(key, out var t)) t.TrySetResult(ok);
        }

        static async Task<bool?> Wait(TaskCompletionSource<bool> t, int ms)
        {
            var done = await Task.WhenAny(t.Task, Task.Delay(ms));
            return done == t.Task ? t.Task.Result : (bool?)null;
        }

        public enum SendResult { Ok, Failed, Cancelled }

        /// <summary>
        /// Send a file from the PC to a phone. progress gets 0..1. When the phone app acknowledges chunks,
        /// progress follows what the phone actually wrote and Ok means the file is saved on the phone.
        /// </summary>
        public static async Task<SendResult> SendAsync(Session s, string path, Action<double> progress = null, Func<bool> cancelled = null, string sendName = null)
        {
            var name = sendName ?? Path.GetFileName(path);
            var id = Guid.NewGuid().ToString("N");
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length > MaxSize) return SendResult.Failed;
                long total = Math.Max(1, fi.Length);
                await s.SendAsync(new { type = "fileStart", id, name, size = fi.Length });
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
                var buf = new byte[ChunkSize];
                long sent = 0; int idx = 0; int n;
                bool? acking = null;                       // null = not known yet, false = old phone app
                var pending = new System.Collections.Generic.Queue<(TaskCompletionSource<bool> t, long upTo)>();
                long confirmed = 0;
                while ((n = await fs.ReadAsync(buf, 0, buf.Length)) > 0)
                {
                    if (!s.Authed) return SendResult.Failed;
                    if (cancelled?.Invoke() == true) { await s.SendAsync(new { type = "fileEnd", id, cancel = true }); return SendResult.Cancelled; }
                    var t = acking == false ? null : Expect(id, idx);
                    await s.SendAsync(new { type = "fileChunk", id, idx = idx++, data = Convert.ToBase64String(buf, 0, n) });
                    sent += n;
                    if (t != null) pending.Enqueue((t, sent));
                    // keep up to 4 chunks in flight
                    while (pending.Count > (acking == null ? 0 : 3))
                    {
                        var (pt, upTo) = pending.Dequeue();
                        var r = await Wait(pt, acking == null ? 4000 : 20000);
                        if (r == null)
                        {
                            if (acking == null) { acking = false; pending.Clear(); break; }   // old phone app: no acks
                            return SendResult.Failed;
                        }
                        if (r == false) return SendResult.Failed;
                        acking = true;
                        confirmed = upTo;
                    }
                    double frac = acking == true ? (double)confirmed / total : (double)sent / total;
                    progress?.Invoke(Math.Min(frac, 0.99));
                    SendProgress?.Invoke(name, Math.Min(frac, 0.99), false);
                }
                while (pending.Count > 0)
                {
                    var (pt, upTo) = pending.Dequeue();
                    var r = await Wait(pt, 20000);
                    if (r != true) { if (acking == null && r == null) { acking = false; break; } return SendResult.Failed; }
                    confirmed = upTo;
                    progress?.Invoke(Math.Min((double)confirmed / total, 0.99));
                }
                var saved = acking == true ? Expect(id, -2) : null;
                await s.SendAsync(new { type = "fileEnd", id });
                if (saved != null && await Wait(saved, 15000) == false) return SendResult.Failed;
                progress?.Invoke(1);
                SendProgress?.Invoke(name, 1, true);
                return SendResult.Ok;
            }
            catch (Exception ex)
            {
                Log.Write("Send file: " + ex.Message);
                SendProgress?.Invoke(name, 0, true);
                return SendResult.Failed;
            }
            finally
            {
                foreach (var k in _acks.Keys.Where(k => k.StartsWith(id + ":")).ToArray()) _acks.TryRemove(k, out _);
            }
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PcPanel.Core;

namespace PcPanel.Services.Screen
{
    /// <summary>
    /// The helper programs the screen features need, downloaded once on demand into %LOCALAPPDATA%\PC Panel\tools:
    ///  • FFmpeg  – captures the virtual monitor on the GPU and encodes it with NVENC / AMF / QSV (second screen)
    ///  • scrcpy  – shows and controls the phone on the PC; its folder also brings adb.exe (USB link)
    ///  • Virtual Display Driver – the extra monitor Windows sees (installed with winget)
    /// </summary>
    public static class ScreenTools
    {
        public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PC Panel", "tools");
        public static string FfmpegDir => Path.Combine(Root, "ffmpeg");
        public static string ScrcpyDir => Path.Combine(Root, "scrcpy");
        public static string Ffmpeg => Path.Combine(FfmpegDir, "ffmpeg.exe");
        public static string Scrcpy => Path.Combine(ScrcpyDir, "scrcpy.exe");
        public static string Adb => Path.Combine(ScrcpyDir, "adb.exe");

        public static bool HasFfmpeg => File.Exists(Ffmpeg);
        public static bool HasScrcpy => File.Exists(Scrcpy) && File.Exists(Adb);

        const string FfmpegUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip";
        const string ScrcpyApi = "https://api.github.com/repos/Genymobile/scrcpy/releases/latest";

        /// <summary>(what, 0..1 or -1 for "working…", done, error). Raised on any thread.</summary>
        public static event Action<string, double, bool, string> Progress;

        static readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        public static volatile string Busy;   // "ffmpeg" | "scrcpy" | "driver" | null

        static HttpClient NewHttp()
        {
            var h = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromMinutes(20) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("PCPanel/" + App.Version);
            return h;
        }

        static void Report(string what, double p, bool done = false, string err = null)
        {
            try { Progress?.Invoke(what, p, done, err); } catch { }
        }

        public static async Task<bool> EnsureFfmpeg()
        {
            if (HasFfmpeg) return true;
            await _lock.WaitAsync();
            try
            {
                if (HasFfmpeg) return true;
                Busy = "ffmpeg";
                var zip = await Download("ffmpeg", FfmpegUrl);
                Report("ffmpeg", -1);
                await Task.Run(() => ExtractFlat(zip, FfmpegDir, e =>
                {
                    // only bin\ffmpeg.exe and its DLLs
                    var p = e.FullName.Replace('\\', '/');
                    if (!p.Contains("/bin/")) return false;
                    var n = Path.GetFileName(p).ToLowerInvariant();
                    return n == "ffmpeg.exe" || n.EndsWith(".dll");
                }));
                try { File.Delete(zip); } catch { }
                Report("ffmpeg", 1, true);
                return HasFfmpeg;
            }
            catch (Exception ex) { Log.Write("ffmpeg download: " + ex.Message); Report("ffmpeg", 0, true, ex.Message); return false; }
            finally { Busy = null; _lock.Release(); }
        }

        public static async Task<bool> EnsureScrcpy()
        {
            if (HasScrcpy) return true;
            await _lock.WaitAsync();
            try
            {
                if (HasScrcpy) return true;
                Busy = "scrcpy";
                Report("scrcpy", -1);
                string url;
                using (var h = NewHttp())
                {
                    var json = JsonNode.Parse(await h.GetStringAsync(ScrcpyApi));
                    url = json?["assets"]?.AsArray()
                        .Select(a => a?["browser_download_url"]?.GetValue<string>())
                        .FirstOrDefault(u => u != null && Path.GetFileName(u).StartsWith("scrcpy-win64-", StringComparison.OrdinalIgnoreCase) && u.EndsWith(".zip"));
                }
                if (url == null) throw new Exception("scrcpy release not found");
                var zip = await Download("scrcpy", url);
                Report("scrcpy", -1);
                await Task.Run(() => ExtractFlat(zip, ScrcpyDir, e => !string.IsNullOrEmpty(e.Name)));
                try { File.Delete(zip); } catch { }
                Report("scrcpy", 1, true);
                return HasScrcpy;
            }
            catch (Exception ex) { Log.Write("scrcpy download: " + ex.Message); Report("scrcpy", 0, true, ex.Message); return false; }
            finally { Busy = null; _lock.Release(); }
        }

        static async Task<string> Download(string what, string url)
        {
            Directory.CreateDirectory(Root);
            var tmp = Path.Combine(Root, what + ".download");
            using var h = NewHttp();
            using var resp = await h.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? -1;
            using (var src = await resp.Content.ReadAsStreamAsync())
            using (var dst = File.Create(tmp))
            {
                var buf = new byte[1 << 16];
                long got = 0;
                var last = DateTime.MinValue;
                int n;
                while ((n = await src.ReadAsync(buf, 0, buf.Length)) > 0)
                {
                    await dst.WriteAsync(buf, 0, n);
                    got += n;
                    if ((DateTime.UtcNow - last).TotalMilliseconds > 120)
                    {
                        last = DateTime.UtcNow;
                        Report(what, total > 0 ? got / (double)total : -1);
                    }
                }
            }
            return tmp;
        }

        /// <summary>Extracts the matching entries of a zip into one folder (sub folders flattened).</summary>
        static void ExtractFlat(string zip, string dir, Func<ZipArchiveEntry, bool> take)
        {
            if (Directory.Exists(dir)) try { Directory.Delete(dir, true); } catch { }
            Directory.CreateDirectory(dir);
            using var z = ZipFile.OpenRead(zip);
            foreach (var e in z.Entries)
            {
                if (string.IsNullOrEmpty(e.Name) || !take(e)) continue;
                e.ExtractToFile(Path.Combine(dir, e.Name), true);
            }
        }

        // ------------------------------------------------------------------ process helpers
        public sealed class RunResult { public int Code = -1; public string Out = ""; public string Err = ""; }

        public static async Task<RunResult> Run(string exe, string args, int timeoutMs = 15000, string workDir = null)
        {
            var r = new RunResult();
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                    WorkingDirectory = workDir ?? Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory
                };
                if (!Path.IsPathRooted(exe)) psi.WorkingDirectory = Environment.CurrentDirectory;
                using var p = Process.Start(psi);
                var o = p.StandardOutput.ReadToEndAsync();
                var e = p.StandardError.ReadToEndAsync();
                using var cts = new CancellationTokenSource(timeoutMs);
                try { await p.WaitForExitAsync(cts.Token); }
                catch (OperationCanceledException) { try { p.Kill(true); } catch { } }
                r.Out = await o; r.Err = await e;
                r.Code = p.HasExited ? p.ExitCode : -1;
            }
            catch (Exception ex) { r.Err = ex.Message; }
            return r;
        }

        public static Task<RunResult> PowerShell(string script, int timeoutMs = 30000)
        {
            var b64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            return Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + b64, timeoutMs);
        }
    }
}

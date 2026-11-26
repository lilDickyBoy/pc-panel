using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PcPanel.Core;

namespace PcPanel.Services.Screen
{
    /// <summary>
    /// Phone as a second monitor.
    ///
    /// Pipeline:  virtual monitor → FFmpeg ddagrab (GPU capture, 60 fps) → NVENC/AMF H.264 (ultra low latency)
    ///            → FLV on stdout (exact frame boundaries) → AES-GCM → TCP (USB via "adb reverse", or Wi-Fi)
    ///            → phone MediaCodec (hardware decoder, low-latency mode) → SurfaceView.
    /// Touches come back on the same socket and are injected as real Windows touch input.
    ///
    /// Wire format (both directions): [u32 length][AES-GCM(ciphertext | tag16)], nonce = dir(4) | counter(8).
    /// Plain message = [u8 type][payload]. The phone opens with a 16-byte one-time ticket it got over the paired channel.
    /// </summary>
    public sealed class SecondScreenService
    {
        public static readonly SecondScreenService I = new SecondScreenService();
        public const int StreamPort = 47810;

        public enum St { Off, Preparing, Waiting, Live, Error }

        // ------------------------------------------------------------------ observable state
        public St State { get; private set; } = St.Off;
        public string StepKey { get; private set; } = "";
        public double StepProgress { get; private set; } = -1;
        public string ErrorKey { get; private set; } = "";
        public string Transport { get; private set; } = "";   // "usb" | "wifi"
        public string Encoder { get; private set; } = "";
        public int Fps { get; private set; }
        public double Mbps { get; private set; }
        public int LatencyMs { get; private set; } = -1;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public string PhoneName { get; private set; } = "";
        public bool Busy => State == St.Preparing || State == St.Waiting || State == St.Live;

        /// <summary>Raised on any thread whenever something above changed.</summary>
        public event Action Changed;

        // ------------------------------------------------------------------ internals
        readonly object _lock = new object();
        TcpListener _listener;
        Session _session;
        string _gdi;
        string _adbSerial;
        byte[] _ticket, _key;
        int _gen;
        TcpClient _client;
        Process _ffmpeg;
        TouchInjector _touch;
        BlockingCollection<byte[]> _out;
        string _workingEncoder;   // cached after the first success
        DateTime _lastClientSeen;

        void Raise() { try { Changed?.Invoke(); } catch { } }

        void Set(St st, string step = null, double progress = -1, string error = null)
        {
            State = st;
            if (step != null) StepKey = step;
            StepProgress = progress;
            ErrorKey = error ?? (st == St.Error ? ErrorKey : "");
            if (st != St.Live) { Fps = 0; Mbps = 0; LatencyMs = -1; }
            Raise();
            PushState();
        }

        public object StateMessage() => new
        {
            type = "ssState",
            state = State.ToString().ToLowerInvariant(),
            step = StepKey,
            progress = Math.Round(StepProgress, 3),
            error = ErrorKey,
            transport = Transport,
            encoder = Encoder,
            fps = Fps,
            latency = LatencyMs,
            w = Width,
            h = Height
        };

        void PushState()
        {
            var s = _session;
            if (s != null) _ = s.SendAsync(StateMessage());
        }

        // ================================================================== settings changed while running
        string _lastQuality, _lastOrient;
        int _lastScale;

        void RememberSettings()
        {
            var s = App.Settings;
            _lastQuality = s.SsQuality; _lastOrient = s.SsOrient; _lastScale = s.SsScale;
        }

        /// <summary>Called (UI thread) after any shared setting changed.</summary>
        public void OnSettingsChanged()
        {
            if (State == St.Off || State == St.Error || _lastQuality == null) return;
            var s = App.Settings;
            if (!s.SsTouch) _touch?.ReleaseAll();
            if (s.SsQuality != _lastQuality)
            {
                // the encode loop restarts FFmpeg with the new bitrate (new key frame → the phone just continues)
                Process f; lock (_lock) f = _ffmpeg;
                try { if (f != null && !f.HasExited) f.Kill(true); } catch { }
            }
            if (s.SsScale != _lastScale && _gdi != null) VirtualDisplay.SetScale(_gdi, s.SsScale);
            bool reorient = s.SsOrient != _lastOrient;
            RememberSettings();
            if (reorient && _session != null) AskPhone(_session);
        }

        // ================================================================== start / stop
        /// <summary>Asks the phone to open its second-screen view (it answers with "ssRequest").</summary>
        public void AskPhone(Session s)
        {
            if (s == null) return;
            _ = s.SendAsync(new { type = "ssPrepare" });
        }

        /// <summary>The phone's second-screen view is open: prepare everything and tell it where to connect.</summary>
        public async Task OnRequest(Session s, int phoneW, int phoneH)
        {
            int gen;
            lock (_lock)
            {
                // a new request replaces whatever was running
                gen = ++_gen;
                _session = s;
                PhoneName = s?.DeviceName ?? "";
            }
            CloseStream(false);

            try
            {
                var st = App.Settings;
                RememberSettings();
                int longSide = Math.Max(phoneW, phoneH), shortSide = Math.Min(phoneW, phoneH);
                if (longSide < 320 || shortSide < 200) { longSide = 2340; shortSide = 1080; }
                bool portrait = st.SsOrient == "portrait";
                Width = portrait ? shortSide : longSide;
                Height = portrait ? longSide : shortSide;
                // even sizes for the encoder
                Width &= ~1; Height &= ~1;

                // 1. tools
                if (!ScreenTools.HasFfmpeg)
                {
                    Set(St.Preparing, "ss.step.download", 0);
                    void P(string what, double prog, bool done, string err) { if (what == "ffmpeg" && gen == _gen) Set(St.Preparing, "ss.step.download", prog); }
                    ScreenTools.Progress += P;
                    bool ok;
                    try { ok = await ScreenTools.EnsureFfmpeg(); } finally { ScreenTools.Progress -= P; }
                    if (gen != _gen) return;
                    if (!ok) { Set(St.Error, error: "ss.err.download"); return; }
                }
                if (st.SsConn != "wifi" && !ScreenTools.HasScrcpy)
                {
                    Set(St.Preparing, "ss.step.download", -1);
                    await ScreenTools.EnsureScrcpy();   // only for adb (USB); Wi-Fi still works without it
                    if (gen != _gen) return;
                }

                // 2. driver
                Set(St.Preparing, "ss.step.driver");
                if (VirtualDisplay.Installed != true && !await VirtualDisplay.CheckInstalled())
                {
                    Set(St.Preparing, "ss.step.install");
                    if (!await VirtualDisplay.Install()) { if (gen == _gen) Set(St.Error, error: "ss.err.driver"); return; }
                }
                if (gen != _gen) return;

                // 3. monitor
                Set(St.Preparing, "ss.step.monitor");
                bool changed = VirtualDisplay.Configure(Width, Height);
                if (changed) await VirtualDisplay.Restart(); else await VirtualDisplay.SetEnabled(true);
                _gdi = await VirtualDisplay.WaitForMonitor();
                if (gen != _gen) return;
                if (_gdi == null) { await Fail("ss.err.monitor"); return; }
                VirtualDisplay.SetMode(_gdi, Width, Height);
                await Task.Delay(400);
                VirtualDisplay.SetScale(_gdi, st.SsScale);
                var b = VirtualDisplay.Bounds(_gdi);
                if (b.Width > 0) { Width = b.Width & ~1; Height = b.Height & ~1; }

                // 4. link
                Set(St.Preparing, "ss.step.link");
                EnsureListener();
                string host = "";
                Transport = "wifi";
                _adbSerial = null;
                if (st.SsConn != "wifi" && Adb.Available)
                {
                    var d = await Adb.Pick(allowWireless: false);
                    if (d != null && d.Ready && await Adb.Reverse(d.Serial, StreamPort))
                    {
                        _adbSerial = d.Serial;
                        host = "127.0.0.1";
                        Transport = "usb";
                    }
                }
                if (Transport != "usb" && st.SsConn == "usb") { await Fail("ss.err.usb"); return; }
                if (gen != _gen) return;

                _ticket = RandomNumberGenerator.GetBytes(16);
                _key = RandomNumberGenerator.GetBytes(32);
                _lastClientSeen = DateTime.UtcNow;
                Set(St.Waiting, "ss.step.waiting");
                await s.SendAsync(new
                {
                    type = "ssOpen",
                    host,
                    port = StreamPort,
                    ticket = Convert.ToBase64String(_ticket),
                    key = Convert.ToBase64String(_key),
                    w = Width,
                    h = Height,
                    usb = Transport == "usb",
                    touch = st.SsTouch
                });
                _ = Watchdog(gen);
            }
            catch (Exception ex)
            {
                Log.Write("Second screen: " + ex);
                if (gen == _gen) await Fail("ss.err.generic");
            }
        }

        /// <summary>Turns everything off again (the extra monitor too) and shows the error on both devices.</summary>
        async Task Fail(string key)
        {
            await Stop(false);
            Set(St.Error, error: key);
        }

        /// <summary>Gives up when the phone doesn't (re)connect for a while.</summary>
        async Task Watchdog(int gen)
        {
            while (gen == _gen && (State == St.Waiting || State == St.Live))
            {
                await Task.Delay(1000);
                if (State == St.Waiting && (DateTime.UtcNow - _lastClientSeen).TotalSeconds > 25)
                {
                    Log.Write("Second screen: phone didn't connect");
                    if (gen == _gen) await Fail("ss.err.timeout");
                    return;
                }
            }
        }

        public async Task Stop(bool tellPhone = true)
        {
            Session s;
            string serial, gdi;
            lock (_lock)
            {
                _gen++;
                s = _session;
                serial = _adbSerial;
                gdi = _gdi;
                _adbSerial = null;
                _gdi = null;
            }
            CloseStream(true);
            bool wasOn = State != St.Off;
            Set(St.Off, "");
            if (tellPhone && s != null) _ = s.SendAsync(new { type = "ssClose" });
            if (serial != null) _ = Adb.RemoveReverse(serial, StreamPort);
            // switch the virtual monitor off again so no window can get stuck on it
            if (wasOn && (gdi != null || VirtualDisplay.Installed == true)) await VirtualDisplay.SetEnabled(false);
        }

        /// <summary>Synchronous best-effort stop when PC Panel quits.</summary>
        public void Shutdown()
        {
            if (State == St.Off) return;
            try { Task.Run(() => Stop(true)).Wait(8000); } catch { }
        }

        void CloseStream(bool releaseTouch)
        {
            TcpClient c; Process f; BlockingCollection<byte[]> q;
            lock (_lock) { c = _client; f = _ffmpeg; q = _out; _client = null; _ffmpeg = null; _out = null; }
            try { q?.CompleteAdding(); } catch { }
            try { if (f != null && !f.HasExited) f.Kill(true); } catch { }
            try { c?.Close(); } catch { }
            if (releaseTouch) { try { _touch?.Dispose(); } catch { } _touch = null; }
            else _touch?.ReleaseAll();
        }

        // ================================================================== socket
        void EnsureListener()
        {
            if (_listener != null) return;
            _listener = new TcpListener(IPAddress.Any, StreamPort);
            _listener.Start();
            _ = AcceptLoop();
        }

        async Task AcceptLoop()
        {
            while (true)
            {
                TcpClient c;
                try { c = await _listener.AcceptTcpClientAsync(); }
                catch { await Task.Delay(500); continue; }
                _ = Task.Run(() => HandleClient(c));
            }
        }

        async Task HandleClient(TcpClient c)
        {
            int gen = _gen;
            try
            {
                c.NoDelay = true;
                c.SendBufferSize = 1 << 20;
                var ns = c.GetStream();
                var hello = new byte[17];
                using (var cts = new CancellationTokenSource(5000))
                    await ns.ReadExactlyAsync(hello, cts.Token);
                var ticket = _ticket;
                if (ticket == null || hello[0] != 1 || !CryptographicOperations.FixedTimeEquals(hello.AsSpan(1, 16), ticket)
                    || (State != St.Waiting && State != St.Live))
                {
                    c.Close();
                    return;
                }

                // only one phone at a time; a reconnect replaces the old socket
                CloseStream(false);
                var q = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>(), 6);
                lock (_lock) { _client = c; _out = q; }
                _touch ??= new TouchInjector { Area = () => VirtualDisplay.Bounds(_gdi) };
                _lastClientSeen = DateTime.UtcNow;

                var sender = new Thread(() => SendLoop(c, q, gen)) { IsBackground = true, Name = "SS send", Priority = ThreadPriority.AboveNormal };
                sender.Start();
                var encoderThread = new Thread(() => EncodeLoop(q, gen)) { IsBackground = true, Name = "SS encode", Priority = ThreadPriority.AboveNormal };
                encoderThread.Start();

                await ReadLoop(ns, gen);
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is OperationCanceledException || ex is ObjectDisposedException) { }
            catch (Exception ex) { Log.Write("SS client: " + ex.Message); }
            finally
            {
                bool mine;
                lock (_lock) mine = _client == c;
                try { c.Close(); } catch { }
                if (mine && gen == _gen)
                {
                    CloseStream(false);
                    _lastClientSeen = DateTime.UtcNow;
                    Set(St.Waiting, "ss.step.waiting");
                }
            }
        }

        // ------------------------------------------------------------------ phone → PC
        async Task ReadLoop(NetworkStream ns, int gen)
        {
            var lenBuf = new byte[4];
            var nonce = new byte[12];
            BinaryPrimitives.WriteUInt32BigEndian(nonce, 2);
            ulong counter = 0;
            using var gcm = new AesGcm(_key, 16);
            var contacts = new List<TouchInjector.Contact>(10);
            while (gen == _gen)
            {
                await ns.ReadExactlyAsync(lenBuf);
                int len = (int)BinaryPrimitives.ReadUInt32BigEndian(lenBuf);
                if (len < 17 || len > 64 * 1024) throw new IOException("bad frame");
                var data = new byte[len];
                await ns.ReadExactlyAsync(data);
                BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), counter++);
                var plain = new byte[len - 16];
                gcm.Decrypt(nonce, data.AsSpan(0, len - 16), data.AsSpan(len - 16), plain);
                _lastClientSeen = DateTime.UtcNow;

                switch (plain[0])
                {
                    case 10: // touch frame
                        if (!App.Settings.SsTouch) break;
                        contacts.Clear();
                        int n = plain[1];
                        for (int i = 0; i < n && 2 + i * 6 + 6 <= plain.Length; i++)
                        {
                            int o = 2 + i * 6;
                            contacts.Add(new TouchInjector.Contact
                            {
                                Id = plain[o],
                                Phase = (TouchInjector.Phase)Math.Min((int)plain[o + 1], 2),
                                X = BinaryPrimitives.ReadUInt16BigEndian(plain.AsSpan(o + 2)) / 65535.0,
                                Y = BinaryPrimitives.ReadUInt16BigEndian(plain.AsSpan(o + 4)) / 65535.0
                            });
                        }
                        _touch?.Handle(contacts);
                        break;
                    case 11: // pong
                        if (plain.Length >= 9)
                        {
                            long sent = (long)BinaryPrimitives.ReadUInt64BigEndian(plain.AsSpan(1));
                            LatencyMs = (int)Math.Max(1, (Stopwatch.GetTimestamp() - sent) * 1000 / Stopwatch.Frequency / 2);
                        }
                        break;
                    case 13: // phone closed the view
                        _ = Stop(false);
                        return;
                }
            }
        }

        // ------------------------------------------------------------------ PC → phone
        void SendLoop(TcpClient c, BlockingCollection<byte[]> q, int gen)
        {
            try
            {
                var ns = c.GetStream();
                var nonce = new byte[12];
                BinaryPrimitives.WriteUInt32BigEndian(nonce, 1);
                ulong counter = 0;
                using var gcm = new AesGcm(_key, 16);
                byte[] wire = new byte[1 << 16];
                long bytes = 0; int frames = 0;
                var sw = Stopwatch.StartNew();
                long lastPing = 0;
                while (gen == _gen)
                {
                    if (!q.TryTake(out var plain, 250))
                    {
                        if (q.IsAddingCompleted) break;
                        plain = null;
                    }
                    if (sw.ElapsedMilliseconds - lastPing > 1000)
                    {
                        lastPing = sw.ElapsedMilliseconds;
                        var ping = new byte[9];
                        ping[0] = 3;
                        BinaryPrimitives.WriteUInt64BigEndian(ping.AsSpan(1), (ulong)Stopwatch.GetTimestamp());
                        Write(ping);
                        Fps = frames; Mbps = Math.Round(bytes * 8 / 1_000_000.0, 1);
                        frames = 0; bytes = 0;
                        if (State == St.Live) Raise();
                    }
                    if (plain == null) continue;
                    Write(plain);
                    if (plain[0] == 2) { frames++; bytes += plain.Length; }
                }

                void Write(byte[] p)
                {
                    int total = 4 + p.Length + 16;
                    if (wire.Length < total) wire = new byte[Math.Max(total, wire.Length * 2)];
                    BinaryPrimitives.WriteUInt32BigEndian(wire, (uint)(p.Length + 16));
                    BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), counter++);
                    gcm.Encrypt(nonce, p, wire.AsSpan(4, p.Length), wire.AsSpan(4 + p.Length, 16));
                    ns.Write(wire, 0, total);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException || ex is InvalidOperationException) { }
            catch (Exception ex) { Log.Write("SS send: " + ex.Message); }
            finally { try { c.Close(); } catch { } }
        }

        // ------------------------------------------------------------------ FFmpeg
        static string Bitrate(string q) => q switch { "low" => "6M", "mid" => "12M", "ultra" => "35M", _ => "20M" };
        static string BufSize(string q) => q switch { "low" => "400k", "mid" => "800k", "ultra" => "2M", _ => "1200k" };

        static string EncoderArgs(string enc, string q)
        {
            string br = Bitrate(q), buf = BufSize(q);
            return enc switch
            {
                "h264_nvenc" => $"-c:v h264_nvenc -preset p1 -tune ull -zerolatency 1 -delay 0 -rc cbr -b:v {br} -maxrate {br} -bufsize {buf} -g 60 -bf 0 -forced-idr 1 -profile:v high",
                "h264_amf" => $"-c:v h264_amf -usage ultralowlatency -quality speed -rc cbr -b:v {br} -maxrate {br} -bufsize {buf} -g 60 -bf 0",
                _ => $"-c:v libx264 -preset ultrafast -tune zerolatency -b:v {br} -maxrate {br} -bufsize {buf} -g 60 -bf 0 -pix_fmt yuv420p"
            };
        }

        string FfmpegArgs(string enc, int adapter, int output)
        {
            var hw = adapter > 0 ? $"-init_hw_device d3d11va=dd:{adapter} -filter_hw_device dd " : "";
            var graph = $"ddagrab=output_idx={Math.Max(0, output)}:framerate=60:draw_mouse=1";
            if (enc == "libx264") graph += ",hwdownload,format=bgra,format=yuv420p";
            return "-hide_banner -loglevel error -nostdin " + hw +
                   $"-filter_complex \"{graph}[v]\" -map \"[v]\" " + EncoderArgs(enc, App.Settings.SsQuality) +
                   " -an -flush_packets 1 -f flv -flvflags no_duration_filesize pipe:1";
        }

        void EncodeLoop(BlockingCollection<byte[]> q, int gen)
        {
            var encoders = new List<string>();
            if (_workingEncoder != null) encoders.Add(_workingEncoder);
            foreach (var e in new[] { "h264_nvenc", "h264_amf", "libx264" }) if (!encoders.Contains(e)) encoders.Add(e);

            int failures = 0;
            int encIdx = 0;
            while (gen == _gen && !q.IsAddingCompleted)
            {
                var (adapter, output) = VirtualDisplay.FindDxgiOutput(_gdi);
                if (output < 0) { Log.Write("SS: DXGI output not found for " + _gdi); adapter = 0; output = 0; }
                string enc = encoders[Math.Min(encIdx, encoders.Count - 1)];
                var started = DateTime.UtcNow;
                int framesOut = RunFfmpeg(enc, adapter, output, q, gen, out string err);
                if (gen != _gen || q.IsAddingCompleted) return;

                if (framesOut > 0)
                {
                    _workingEncoder = enc;
                    failures = 0;
                    Log.Write("SS: ffmpeg stopped after " + framesOut + " frames, restarting. " + err);
                }
                else
                {
                    Log.Write("SS: " + enc + " failed: " + err);
                    if (encIdx < encoders.Count - 1) { encIdx++; continue; }
                    if (++failures >= 3) { _ = Fail("ss.err.encoder"); return; }
                }
                Thread.Sleep((DateTime.UtcNow - started).TotalSeconds < 2 ? 700 : 150);
            }
        }

        /// <summary>Runs one FFmpeg and forwards its frames. Returns how many frames came out.</summary>
        int RunFfmpeg(string enc, int adapter, int output, BlockingCollection<byte[]> q, int gen, out string error)
        {
            error = "";
            Process p;
            try
            {
                p = Process.Start(new ProcessStartInfo(ScreenTools.Ffmpeg, FfmpegArgs(enc, adapter, output))
                {
                    CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = false,
                    WorkingDirectory = ScreenTools.FfmpegDir
                });
                try { p.PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }
            }
            catch (Exception ex) { error = ex.Message; return 0; }
            lock (_lock) _ffmpeg = p;

            var errText = new StringBuilder();
            p.ErrorDataReceived += (s, e) => { if (e.Data != null && errText.Length < 2000) errText.AppendLine(e.Data); };
            p.BeginErrorReadLine();

            int frames = 0;
            bool waitKey = true;       // after a drop, skip to the next key frame
            try
            {
                var flv = new FlvReader(p.StandardOutput.BaseStream);
                while (gen == _gen)
                {
                    var tag = flv.Next();
                    if (tag == null) break;
                    if (tag.Config != null)
                    {
                        Encoder = EncName(enc);
                        var cfg = JsonSerializer.SerializeToUtf8Bytes(new
                        {
                            w = Width, h = Height,
                            sps = Convert.ToBase64String(tag.Config.Value.sps),
                            pps = Convert.ToBase64String(tag.Config.Value.pps)
                        });
                        var msg = new byte[1 + cfg.Length];
                        msg[0] = 1;
                        cfg.CopyTo(msg, 1);
                        q.Add(msg);
                        waitKey = true;
                        continue;
                    }
                    if (tag.Frame == null) continue;
                    if (waitKey && !tag.Key) continue;
                    var m = new byte[1 + 1 + 8 + tag.Frame.Length];
                    m[0] = 2;
                    m[1] = (byte)(tag.Key ? 1 : 0);
                    BinaryPrimitives.WriteUInt64BigEndian(m.AsSpan(2), (ulong)tag.TimeMs * 1000UL);
                    tag.Frame.CopyTo(m, 10);
                    if (!q.TryAdd(m))
                    {
                        // the link can't keep up: drop until the next key frame instead of building up delay
                        waitKey = true;
                        continue;
                    }
                    waitKey = false;
                    if (frames++ == 0 && State != St.Live) Set(St.Live, "ss.step.live");
                }
            }
            catch (InvalidOperationException) { }   // queue completed
            catch (Exception ex) { if (gen == _gen) error = ex.Message; }
            finally
            {
                try { if (!p.HasExited) p.Kill(true); } catch { }
                try { p.WaitForExit(1500); } catch { }
                error = (error + " " + errText.ToString().Trim()).Trim();
                lock (_lock) if (_ffmpeg == p) _ffmpeg = null;
                p.Dispose();
            }
            return frames;
        }

        static string EncName(string enc) => enc switch { "h264_nvenc" => "NVIDIA NVENC", "h264_amf" => "AMD AMF", _ => "CPU (x264)" };

        // ================================================================== FLV demuxer (H.264 only)
        sealed class FlvTag
        {
            public (byte[] sps, byte[] pps)? Config;
            public byte[] Frame;   // Annex-B
            public bool Key;
            public long TimeMs;
        }

        sealed class FlvReader
        {
            readonly Stream _s;
            readonly byte[] _hdr = new byte[15];
            int _nalLen = 4;
            bool _started;

            public FlvReader(Stream s) { _s = new BufferedStream(s, 1 << 16); }

            void Read(byte[] buf, int count) => _s.ReadExactly(buf, 0, count);

            public FlvTag Next()
            {
                try
                {
                    if (!_started)
                    {
                        var h = new byte[13];          // "FLV" v flags size(4) + PreviousTagSize0(4)
                        Read(h, 13);
                        if (h[0] != 'F' || h[1] != 'L' || h[2] != 'V') throw new IOException("not FLV");
                        int skip = (int)BinaryPrimitives.ReadUInt32BigEndian(h.AsSpan(5)) - 9;
                        if (skip > 0) Read(new byte[skip], skip);
                        _started = true;
                    }
                    while (true)
                    {
                        Read(_hdr, 11);
                        int type = _hdr[0] & 0x1F;
                        int size = (_hdr[1] << 16) | (_hdr[2] << 8) | _hdr[3];
                        long ts = (_hdr[4] << 16) | (_hdr[5] << 8) | _hdr[6] | (_hdr[7] << 24);
                        var body = new byte[size + 4];   // + PreviousTagSize
                        Read(body, size + 4);
                        if (type != 9 || size < 5) continue;
                        var t = Parse(body, size, ts);
                        if (t != null) return t;
                    }
                }
                catch (EndOfStreamException) { return null; }
            }

            FlvTag Parse(byte[] b, int size, long ts)
            {
                bool ex = (b[0] & 0x80) != 0;          // enhanced FLV header
                int frameType = (b[0] >> 4) & 0x07;
                int pktType, off;
                if (ex)
                {
                    pktType = b[0] & 0x0F;              // 0 seq start, 1 coded frames, 3 coded frames (no CTS)
                    off = 5;                            // + FourCC
                    if (pktType == 1) off += 3;
                    if (pktType == 3) pktType = 1;
                }
                else
                {
                    if ((b[0] & 0x0F) != 7) return null;   // not AVC
                    pktType = b[1];
                    off = 5;
                }
                if (pktType == 0) return new FlvTag { Config = ParseAvcC(b, off, size), TimeMs = ts };
                if (pktType != 1) return null;

                // AVCC (length-prefixed NAL units) → Annex-B (start codes)
                var ms = new MemoryStream(size + 16);
                int p = off;
                bool idr = false;
                while (p + _nalLen <= size)
                {
                    int n = 0;
                    for (int i = 0; i < _nalLen; i++) n = (n << 8) | b[p + i];
                    p += _nalLen;
                    if (n <= 0 || p + n > size) break;
                    int nalType = b[p] & 0x1F;
                    if (nalType == 5) idr = true;
                    if (nalType != 9)   // drop access unit delimiters
                    {
                        ms.Write(StartCode, 0, 4);
                        ms.Write(b, p, n);
                    }
                    p += n;
                }
                if (ms.Length == 0) return null;
                return new FlvTag { Frame = ms.ToArray(), Key = frameType == 1 || idr, TimeMs = ts };
            }

            static readonly byte[] StartCode = { 0, 0, 0, 1 };

            (byte[] sps, byte[] pps)? ParseAvcC(byte[] b, int off, int size)
            {
                try
                {
                    _nalLen = (b[off + 4] & 0x03) + 1;
                    int nSps = b[off + 5] & 0x1F;
                    int p = off + 6;
                    var sps = new MemoryStream();
                    for (int i = 0; i < nSps; i++)
                    {
                        int l = (b[p] << 8) | b[p + 1]; p += 2;
                        sps.Write(StartCode, 0, 4); sps.Write(b, p, l); p += l;
                    }
                    int nPps = b[p++];
                    var pps = new MemoryStream();
                    for (int i = 0; i < nPps; i++)
                    {
                        int l = (b[p] << 8) | b[p + 1]; p += 2;
                        pps.Write(StartCode, 0, 4); pps.Write(b, p, l); p += l;
                    }
                    if (p > size) return null;
                    return (sps.ToArray(), pps.ToArray());
                }
                catch { return null; }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PcPanel.Core
{
    public sealed class Server
    {
        public const int Port = 47800;
        public const int DiscoveryPort = 47801;

        TcpListener _tcp;
        UdpClient _udp;
        volatile bool _stopped;
        readonly List<Session> _sessions = new List<Session>();

        /// <summary>Raised (on any thread) when a phone connects, disconnects or changes focus.</summary>
        public event Action SessionsChanged;
        public event Action<Session> PhoneConnected;
        public event Action<Session> PhoneDisconnected;

        public Session[] Sessions { get { lock (_sessions) return _sessions.ToArray(); } }
        public bool AnyForeground => Sessions.Any(s => s.Foreground);
        public int FastestInterval { get { var f = Sessions.Where(s => s.Foreground).Select(s => s.StatsInterval).DefaultIfEmpty(1000).Min(); return Math.Clamp(f, 500, 10000); } }
        public bool AnyConnected => Sessions.Length > 0;

        public void Start()
        {
            try
            {
                _tcp = new TcpListener(IPAddress.Any, Port);
                _tcp.Start();
                _ = AcceptLoop();
            }
            catch (Exception ex) { Log.Write("TCP listen failed: " + ex.Message); }

            try
            {
                _udp = new UdpClient(new IPEndPoint(IPAddress.Any, DiscoveryPort)) { EnableBroadcast = true };
                _ = DiscoveryLoop();
            }
            catch (Exception ex) { Log.Write("UDP listen failed: " + ex.Message); }
        }

        public void Stop()
        {
            _stopped = true;
            foreach (var s in Sessions) s.Kick("pc_quit");
            try { _tcp?.Stop(); } catch { }
            try { _udp?.Close(); } catch { }
        }

        async Task DiscoveryLoop()
        {
            while (!_stopped)
            {
                try
                {
                    var r = await _udp.ReceiveAsync();
                    var msg = Encoding.ASCII.GetString(r.Buffer);
                    if (msg == "MOBILINK?" + App.Settings.PcId || msg == "MOBILINK?*")
                    {
                        var reply = Encoding.UTF8.GetBytes("MOBILINK!" + App.Settings.PcId + "|" + Port + "|" + App.Settings.PcName);
                        await _udp.SendAsync(reply, reply.Length, r.RemoteEndPoint);
                    }
                }
                catch (ObjectDisposedException) { return; }
                catch (Exception ex) { if (_stopped) return; Log.Write("Discovery: " + ex.Message); await Task.Delay(500); }
            }
        }

        async Task AcceptLoop()
        {
            while (!_stopped)
            {
                TcpClient c;
                try { c = await _tcp.AcceptTcpClientAsync(); }
                catch { if (_stopped) return; await Task.Delay(300); continue; }
                _ = Task.Run(() => HandleClient(c));
            }
        }

        static readonly Regex KeyRx = new Regex(@"Sec-WebSocket-Key:\s*(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        async Task HandleClient(TcpClient c)
        {
            try
            {
                c.NoDelay = true;
                var stream = c.GetStream();
                var buf = new byte[8192];
                int len = 0;
                using (var cts = new CancellationTokenSource(5000))
                {
                    while (true)
                    {
                        int n = await stream.ReadAsync(buf.AsMemory(len, buf.Length - len), cts.Token);
                        if (n <= 0) { c.Close(); return; }
                        len += n;
                        if (Encoding.ASCII.GetString(buf, 0, len).Contains("\r\n\r\n")) break;
                        if (len >= buf.Length) { c.Close(); return; }
                    }
                }
                var req = Encoding.ASCII.GetString(buf, 0, len);
                var m = KeyRx.Match(req);
                if (!m.Success)
                {
                    var body = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 8\r\nConnection: close\r\n\r\nPC Panel");
                    await stream.WriteAsync(body);
                    c.Close();
                    return;
                }
                var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(m.Groups[1].Value.Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                var resp = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(resp));

                var ws = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions
                {
                    IsServer = true,
                    KeepAliveInterval = TimeSpan.FromSeconds(15)
                });
                var ip = (c.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";
                var session = new Session(this, ws, ip);
                await session.RunAsync();
            }
            catch (Exception ex) { Log.Write("Client error: " + ex.Message); }
            finally { try { c.Close(); } catch { } }
        }

        internal void Add(Session s)
        {
            lock (_sessions) _sessions.Add(s);
            PhoneConnected?.Invoke(s);
            SessionsChanged?.Invoke();
        }

        internal void Remove(Session s)
        {
            bool removed;
            lock (_sessions) removed = _sessions.Remove(s);
            if (removed)
            {
                PhoneDisconnected?.Invoke(s);
                SessionsChanged?.Invoke();
            }
        }

        internal void RaiseChanged() => SessionsChanged?.Invoke();

        public void Broadcast(object msg, bool foregroundOnly = false)
        {
            foreach (var s in Sessions)
            {
                if (foregroundOnly && !s.Foreground) continue;
                _ = s.SendAsync(msg);
            }
        }

        public void KickAll(string reason = "pc_disconnect")
        {
            foreach (var s in Sessions) s.Kick(reason);
        }
    }

    public sealed class Session
    {
        readonly Server _server;
        readonly WebSocket _ws;
        readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        Crypto _crypto;
        long _sendSeq;
        long _recvSeq;
        string _sid;

        public string Ip { get; }
        public string DeviceName { get; private set; } = "Phone";
        public bool Authed { get; private set; }
        public bool Foreground { get; private set; } = true;
        public DateTime ConnectedAt { get; private set; }
        public string ByeReason { get; private set; } = "";

        // phone state reported by the app
        public int Battery { get; set; } = -1;
        public bool Charging { get; set; }
        public float BatteryTemp { get; set; } = -1;
        public volatile bool Streaming;
        public volatile int StatsInterval = 1000;

        public Session(Server server, WebSocket ws, string ip)
        {
            _server = server;
            _ws = ws;
            Ip = ip;
        }

        public async Task RunAsync()
        {
            _crypto = new Crypto(App.Settings.Token);
            _sid = Convert.ToBase64String(RandomNumberGenerator.GetBytes(12));
            await SendRawAsync(JsonSerializer.Serialize(new { type = "hello", sid = _sid, v = 1, pcId = App.Settings.PcId }));

            using var authTimer = new Timer(_ => { if (!Authed) try { _ws.Abort(); } catch { } }, null, 10000, Timeout.Infinite);
            var buffer = new byte[64 * 1024];
            try
            {
                while (_ws.State == WebSocketState.Open)
                {
                    var text = await ReceiveTextAsync(buffer);
                    if (text == null) break;

                    JsonObject msg;
                    try
                    {
                        msg = JsonNode.Parse(_crypto.Decrypt(text)) as JsonObject;
                        if (msg == null) throw new Exception("bad json");
                        long seq = msg["seq"]?.GetValue<long>() ?? 0;
                        if (seq <= _recvSeq) throw new Exception("replay");
                        _recvSeq = seq;
                    }
                    catch
                    {
                        if (!Authed)
                        {
                            Log.Write("Auth failed from " + Ip);
                            try { await _ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "auth", CancellationToken.None); } catch { }
                            return;
                        }
                        continue; // ignore garbage after auth
                    }

                    string type = msg["type"]?.GetValue<string>() ?? "";
                    if (!Authed)
                    {
                        if (type != "auth" || msg["sid"]?.GetValue<string>() != _sid)
                        {
                            try { await _ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "auth", CancellationToken.None); } catch { }
                            return;
                        }
                        Authed = true;
                        DeviceName = msg["device"]?.GetValue<string>() ?? "Phone";
                        Foreground = msg["foreground"]?.GetValue<bool>() ?? true;
                        ConnectedAt = DateTime.Now;
                        if (!App.Settings.EverPaired) { App.Settings.EverPaired = true; App.Settings.Save(); }
                        _server.Add(this);
                        await SendAsync(Commands.BuildWelcome());
                        Commands.PushInitialState(this);
                        continue;
                    }

                    if (type == "focus")
                    {
                        Foreground = msg["active"]?.GetValue<bool>() ?? true;
                        _server.RaiseChanged();
                        if (Foreground) Commands.PushInitialState(this);
                        continue;
                    }
                    if (type == "bye") { ByeReason = msg["reason"]?.GetValue<string>() ?? "bye"; break; }

                    try { await Commands.Handle(this, type, msg); }
                    catch (Exception ex) { Log.Write("Command " + type + " failed: " + ex.Message); }
                }
            }
            catch (WebSocketException) { }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Write("Session error: " + ex.Message); }
            finally
            {
                _server.Remove(this);
                try { _ws.Abort(); } catch { }
                _ws.Dispose();
            }
        }

        async Task<string> ReceiveTextAsync(byte[] buffer)
        {
            using var ms = new MemoryStream();
            while (true)
            {
                var r = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (r.MessageType == WebSocketMessageType.Close) return null;
                ms.Write(buffer, 0, r.Count);
                if (ms.Length > 4 * 1024 * 1024) return null;
                if (r.EndOfMessage) break;
            }
            return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        }

        async Task SendRawAsync(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await _sendLock.WaitAsync();
            try
            {
                if (_ws.State != WebSocketState.Open) return;
                using var cts = new CancellationTokenSource(15000);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
            }
            catch { try { _ws.Abort(); } catch { } }
            finally { _sendLock.Release(); }
        }

        public async Task SendAsync(object msg)
        {
            if (_ws.State != WebSocketState.Open) return;
            try
            {
                var node = JsonSerializer.SerializeToNode(msg) as JsonObject ?? new JsonObject();
                node["seq"] = Interlocked.Increment(ref _sendSeq);
                await SendRawAsync(_crypto.Encrypt(node.ToJsonString()));
            }
            catch (Exception ex) { Log.Write("Send failed: " + ex.Message); }
        }

        public void Kick(string reason)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendAsync(new { type = "bye", reason });
                    using var cts = new CancellationTokenSource(2000);
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, cts.Token);
                }
                catch { }
                finally { try { _ws.Abort(); } catch { } }
            });
        }
    }
}

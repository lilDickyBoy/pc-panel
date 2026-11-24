using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using LibreHardwareMonitor.Hardware;
using PcPanel.Core;

namespace PcPanel.Services
{
    /// <summary>
    /// Reads CPU / GPU / RAM / fans / disks / network. Only polls while a phone is connected,
    /// so it costs nothing when nobody is watching.
    /// </summary>
    public sealed class SensorService
    {
        public static readonly SensorService I = new SensorService();

        Computer _pc;
        bool _lhmOk;
        readonly AutoResetEvent _wake = new AutoResetEvent(false);
        Thread _thread;

        public string CpuName = "";
        public string GpuName = "";
        public bool SensorsLimited; // true when CPU temps could not be read (driver missing)

        // last snapshot
        public float CpuTemp = -1, GpuTemp = -1;

        long _lastIdle, _lastKernel, _lastUser;
        long _lastRx, _lastTx; DateTime _lastNetTime;
        List<object> _disks = new List<object>(); DateTime _lastDisk = DateTime.MinValue;
        DateTime _lastCpuAlert = DateTime.MinValue, _lastGpuAlert = DateTime.MinValue;

        // temperature history: one point every 10 s, 1 hour
        const int HistLen = 360;
        readonly float[] _histCpu = new float[HistLen], _histGpu = new float[HistLen];
        int _histCount, _histHead;
        DateTime _lastHist = DateTime.MinValue;
        readonly object _histLock = new object();

        public bool InGame { get; private set; }
        public event Action GameChanged;

        public void Start()
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "Sensors", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }

        public void Wake() => _wake.Set();

        public void Stop()
        {
            try { _pc?.Close(); } catch { }
        }

        void InitLhm()
        {
            try
            {
                _pc = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMotherboardEnabled = true,
                    IsControllerEnabled = true
                };
                _pc.Open();
                _lhmOk = true;
                foreach (var hw in _pc.Hardware)
                {
                    if (hw.HardwareType == HardwareType.Cpu && CpuName == "") CpuName = hw.Name;
                    if ((hw.HardwareType == HardwareType.GpuNvidia || hw.HardwareType == HardwareType.GpuAmd || hw.HardwareType == HardwareType.GpuIntel) && GpuName == "")
                        GpuName = hw.Name;
                }
            }
            catch (Exception ex)
            {
                Log.Write("LibreHardwareMonitor init failed: " + ex.Message);
                _lhmOk = false;
            }
        }

        void Loop()
        {
            InitLhm();
            while (true)
            {
                var server = App.Server;
                UpdateGame();
                if (server == null || !server.AnyConnected)
                {
                    // nobody watching: only keep the (cheap) temperature history going
                    if (App.Settings != null && App.Settings.TempHistory && _lhmOk)
                    {
                        try { PollTempsOnly(); } catch (Exception ex) { Log.Write("Temp poll: " + ex.Message); }
                        _wake.WaitOne(10000);
                    }
                    else _wake.WaitOne(15000);
                    continue;
                }
                bool fg = server.AnyForeground;
                try
                {
                    var stats = Poll();
                    if (fg) server.Broadcast(stats, foregroundOnly: true);
                    CheckAlerts();
                    FanService.I.Apply(CpuTemp, GpuTemp);
                }
                catch (Exception ex) { Log.Write("Sensor poll: " + ex.Message); }
                int wait = !fg ? 4000 : (InGame && App.Settings.GameMode ? Math.Max(3000, server.FastestInterval) : server.FastestInterval);
                _wake.WaitOne(wait);
            }
        }

        void UpdateGame()
        {
            bool game = false;
            try
            {
                if (Native.SHQueryUserNotificationState(out int st) == 0)
                    game = st == 3 || st == 2; // D3D full screen or busy (full screen app)
            }
            catch { }
            if (game != InGame)
            {
                InGame = game;
                try { GameChanged?.Invoke(); } catch { }
            }
        }

        void AddHistory(float cpu, float gpu)
        {
            if (!App.Settings.TempHistory) return;
            if ((DateTime.UtcNow - _lastHist).TotalSeconds < 9.5) return;
            _lastHist = DateTime.UtcNow;
            lock (_histLock)
            {
                _histCpu[_histHead] = cpu;
                _histGpu[_histHead] = gpu;
                _histHead = (_histHead + 1) % HistLen;
                if (_histCount < HistLen) _histCount++;
            }
        }

        public object History()
        {
            lock (_histLock)
            {
                var cpu = new float[_histCount];
                var gpu = new float[_histCount];
                for (int i = 0; i < _histCount; i++)
                {
                    int idx = (_histHead - _histCount + i + HistLen) % HistLen;
                    cpu[i] = _histCpu[idx];
                    gpu[i] = _histGpu[idx];
                }
                return new { type = "history", step = 10, cpu, gpu };
            }
        }

        void PollTempsOnly()
        {
            float cpuTemp = -1, coreMax = -1, gpuTemp = -1;
            foreach (var hw in _pc.Hardware)
            {
                if (hw.HardwareType == HardwareType.Cpu)
                {
                    hw.Update();
                    foreach (var s in hw.Sensors)
                    {
                        if (s.SensorType != SensorType.Temperature) continue;
                        var v = V(s);
                        if (v < 0) continue;
                        if (s.Name == "CPU Package" || s.Name.StartsWith("Core (Tctl")) cpuTemp = v;
                        else if (s.Name == "Core Max") coreMax = v;
                    }
                }
                else if ((hw.HardwareType == HardwareType.GpuNvidia || hw.HardwareType == HardwareType.GpuAmd || hw.HardwareType == HardwareType.GpuIntel) && gpuTemp < 0)
                {
                    hw.Update();
                    foreach (var s in hw.Sensors)
                        if (s.SensorType == SensorType.Temperature && s.Name == "GPU Core") gpuTemp = V(s);
                }
            }
            if (cpuTemp < 0) cpuTemp = coreMax;
            CpuTemp = cpuTemp; GpuTemp = gpuTemp;
            AddHistory(cpuTemp, gpuTemp);
        }

        static float V(ISensor s) => s?.Value is float f && !float.IsNaN(f) && !float.IsInfinity(f) ? f : -1;
        static float R(float v, int d = 1) => v < 0 ? -1 : (float)Math.Round(v, d);

        object Poll()
        {
            float cpuLoad = -1, cpuTemp = -1, cpuClock = -1, cpuPower = -1;
            float gpuLoad = -1, gpuTemp = -1, gpuClock = -1, gpuPower = -1, gpuMemUsed = -1, gpuMemTotal = -1, gpuFan = -1, gpuFanPct = -1, gpuHot = -1;
            var fans = new List<object>();

            if (_lhmOk)
            {
                foreach (var hw in _pc.Hardware)
                {
                    switch (hw.HardwareType)
                    {
                        case HardwareType.Cpu:
                        {
                            hw.Update();
                            float maxClock = -1, coreMax = -1;
                            foreach (var s in hw.Sensors)
                            {
                                var v = V(s);
                                if (v < 0) continue;
                                if (s.SensorType == SensorType.Load && s.Name == "CPU Total") cpuLoad = v;
                                else if (s.SensorType == SensorType.Temperature)
                                {
                                    if (s.Name == "CPU Package" || s.Name.StartsWith("Core (Tctl")) cpuTemp = v;
                                    else if (s.Name == "Core Max") coreMax = v;
                                }
                                else if (s.SensorType == SensorType.Clock && s.Name.Contains("Core") && v > maxClock) maxClock = v;
                                else if (s.SensorType == SensorType.Power && s.Name == "CPU Package") cpuPower = v;
                            }
                            if (cpuTemp < 0) cpuTemp = coreMax;
                            cpuClock = maxClock;
                            break;
                        }
                        case HardwareType.GpuNvidia:
                        case HardwareType.GpuAmd:
                        case HardwareType.GpuIntel:
                        {
                            if (gpuLoad >= 0) break; // first GPU only
                            hw.Update();
                            foreach (var s in hw.Sensors)
                            {
                                var v = V(s);
                                if (v < 0) continue;
                                switch (s.SensorType)
                                {
                                    case SensorType.Load when s.Name == "GPU Core": gpuLoad = v; break;
                                    case SensorType.Temperature when s.Name == "GPU Core": gpuTemp = v; break;
                                    case SensorType.Temperature when s.Name.Contains("Hot Spot"): gpuHot = v; break;
                                    case SensorType.Clock when s.Name == "GPU Core": gpuClock = v; break;
                                    case SensorType.Power when (s.Name == "GPU Package" || s.Name == "GPU Power" || s.Name == "GPU Core"): if (gpuPower < 0) gpuPower = v; break;
                                    case SensorType.SmallData when s.Name == "GPU Memory Used": gpuMemUsed = v; break;
                                    case SensorType.SmallData when s.Name == "GPU Memory Total": gpuMemTotal = v; break;
                                    case SensorType.Fan: if (v > gpuFan) gpuFan = v; break;
                                    case SensorType.Control when s.Name.Contains("Fan"): if (v > gpuFanPct) gpuFanPct = v; FanService.I.Register(s, "gpu"); break;
                                }
                            }
                            if (gpuLoad < 0) gpuLoad = 0;
                            break;
                        }
                        case HardwareType.Motherboard:
                        {
                            hw.Update();
                            foreach (var sub in hw.SubHardware)
                            {
                                sub.Update();
                                foreach (var s in sub.Sensors)
                                {
                                    if (s.SensorType == SensorType.Control) { FanService.I.Register(s, "case"); continue; }
                                    if (s.SensorType != SensorType.Fan) continue;
                                    var v = V(s);
                                    if (v <= 0) continue;
                                    fans.Add(new { n = s.Name, rpm = (int)v });
                                }
                            }
                            break;
                        }
                    }
                }
            }

            if (cpuLoad < 0) cpuLoad = FallbackCpuLoad();
            SensorsLimited = _lhmOk == false || cpuTemp < 0;
            CpuTemp = cpuTemp; GpuTemp = gpuTemp;
            AddHistory(cpuTemp, gpuTemp);

            // RAM
            var ms = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
            Native.GlobalMemoryStatusEx(ref ms);

            // Network speed
            long rx = 0, tx = 0;
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    var st = ni.GetIPv4Statistics();
                    rx += st.BytesReceived; tx += st.BytesSent;
                }
            }
            catch { }
            var now = DateTime.UtcNow;
            double secs = (now - _lastNetTime).TotalSeconds;
            long down = 0, up = 0;
            if (_lastNetTime != default && secs > 0.2)
            {
                down = Math.Max(0, (long)((rx - _lastRx) / secs));
                up = Math.Max(0, (long)((tx - _lastTx) / secs));
            }
            _lastRx = rx; _lastTx = tx; _lastNetTime = now;

            // Disks (every 15 s)
            if ((now - _lastDisk).TotalSeconds > 15)
            {
                _lastDisk = now;
                var list = new List<object>();
                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                        list.Add(new { n = d.Name.TrimEnd('\\'), label = d.VolumeLabel, used = d.TotalSize - d.TotalFreeSpace, total = d.TotalSize });
                    }
                    catch { }
                }
                _disks = list;
            }

            return new
            {
                type = "stats",
                cpu = new { name = CpuName, load = R(cpuLoad), temp = R(cpuTemp, 0), clock = R(cpuClock, 0), power = R(cpuPower, 0) },
                gpu = new { name = GpuName, load = R(gpuLoad), temp = R(gpuTemp, 0), hot = R(gpuHot, 0), clock = R(gpuClock, 0), power = R(gpuPower, 0), memUsed = R(gpuMemUsed, 0), memTotal = R(gpuMemTotal, 0), fan = R(gpuFan, 0), fanPct = R(gpuFanPct, 0) },
                ram = new { used = (long)(ms.ullTotalPhys - ms.ullAvailPhys), total = (long)ms.ullTotalPhys },
                fans,
                disks = _disks,
                net = new { down, up },
                uptime = Environment.TickCount64 / 1000,
                limited = SensorsLimited,
                game = InGame,
                fanCtl = FanService.I.Snapshot()
            };
        }

        float FallbackCpuLoad()
        {
            if (!Native.GetSystemTimes(out long idle, out long kernel, out long user)) return -1;
            long di = idle - _lastIdle, dk = kernel - _lastKernel, du = user - _lastUser;
            _lastIdle = idle; _lastKernel = kernel; _lastUser = user;
            long total = dk + du;
            if (total <= 0) return 0;
            return Math.Clamp((float)(total - di) * 100f / total, 0, 100);
        }

        void CheckAlerts()
        {
            var s = App.Settings;
            var now = DateTime.UtcNow;
            if (CpuTemp >= s.CpuAlert && (now - _lastCpuAlert).TotalMinutes > 5)
            {
                _lastCpuAlert = now;
                App.Server.Broadcast(new { type = "alert", kind = "cpu", temp = (int)CpuTemp });
                App.UiNotchInfo(Glyph.Warning, Loc.F("pc.alert.hot", "CPU", (int)CpuTemp));
                SoundService.Play("alert");
            }
            if (GpuTemp >= s.GpuAlert && (now - _lastGpuAlert).TotalMinutes > 5)
            {
                _lastGpuAlert = now;
                App.Server.Broadcast(new { type = "alert", kind = "gpu", temp = (int)GpuTemp });
                App.UiNotchInfo(Glyph.Warning, Loc.F("pc.alert.hot", "GPU", (int)GpuTemp));
                SoundService.Play("alert");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;
using PcPanel.Core;

namespace PcPanel.Services
{
    /// <summary>
    /// Fan control through LibreHardwareMonitor "Control" sensors (GPU fan + motherboard fan headers).
    /// Two groups: "gpu" and "case". Modes: auto (BIOS/driver), silent, balanced, performance, manual.
    /// Safety: never below 30 %, 100 % above 85 °C, everything goes back to automatic when the
    /// phone disconnects or the app quits.
    /// </summary>
    public sealed class FanService
    {
        public static readonly FanService I = new FanService();

        const float MinPct = 30f;
        const float Panic = 85f;

        sealed class Group { public string Mode = "auto"; public float Pct = 60; }
        readonly Dictionary<string, Group> _groups = new Dictionary<string, Group> { ["gpu"] = new Group(), ["case"] = new Group() };
        readonly Dictionary<string, (ISensor sensor, string group)> _controls = new Dictionary<string, (ISensor, string)>();
        readonly HashSet<string> _overridden = new HashSet<string>();
        readonly object _lock = new object();

        public string Mode(string group) { lock (_lock) return _groups.TryGetValue(group, out var g) ? g.Mode : "auto"; }
        public float Pct(string group) { lock (_lock) return _groups.TryGetValue(group, out var g) ? g.Pct : 60; }

        /// <summary>Called by the sensor thread for every control sensor it sees.</summary>
        public void Register(ISensor s, string group)
        {
            if (s?.Control == null) return;
            lock (_lock) _controls[s.Identifier.ToString()] = (s, group);
        }

        public bool HasControls(string group)
        {
            lock (_lock) { foreach (var c in _controls.Values) if (c.group == group) return true; }
            return false;
        }

        public void Set(string group, string mode, float pct)
        {
            if (group != "gpu" && group != "case") return;
            if (mode != "silent" && mode != "balanced" && mode != "performance" && mode != "manual") mode = "auto";
            lock (_lock)
            {
                var g = _groups[group];
                g.Mode = mode;
                g.Pct = Math.Clamp(pct, MinPct, 100);
            }
            SensorService.I.Wake();
        }

        static float Curve(string mode, float temp)
        {
            if (temp < 0) temp = 60; // unknown → be careful
            // (temp, pct) points
            (float t, float p)[] pts = mode switch
            {
                "silent" => new[] { (40f, 30f), (60f, 38f), (72f, 55f), (82f, 85f), (88f, 100f) },
                "performance" => new[] { (35f, 55f), (55f, 70f), (68f, 88f), (75f, 100f) },
                _ => new[] { (40f, 35f), (58f, 48f), (70f, 70f), (80f, 92f), (85f, 100f) },
            };
            if (temp <= pts[0].t) return pts[0].p;
            for (int i = 1; i < pts.Length; i++)
            {
                if (temp <= pts[i].t)
                {
                    var a = pts[i - 1]; var b = pts[i];
                    return a.p + (b.p - a.p) * (temp - a.t) / (b.t - a.t);
                }
            }
            return 100;
        }

        /// <summary>Applies the current modes. Called by the sensor thread after every poll.</summary>
        public void Apply(float cpuTemp, float gpuTemp)
        {
            if (!App.Settings.FanControl) { RestoreAll(); return; }
            lock (_lock)
            {
                foreach (var kv in _controls)
                {
                    var (sensor, group) = kv.Value;
                    var g = _groups[group];
                    float temp = group == "gpu" ? gpuTemp : Math.Max(cpuTemp, gpuTemp);
                    try
                    {
                        if (g.Mode == "auto")
                        {
                            if (_overridden.Remove(kv.Key)) sensor.Control.SetDefault();
                            continue;
                        }
                        float target = g.Mode == "manual" ? g.Pct : Curve(g.Mode, temp);
                        if (temp >= Panic) target = 100;
                        target = Math.Clamp(target, MinPct, 100);
                        float min = sensor.Control.MinSoftwareValue, max = sensor.Control.MaxSoftwareValue;
                        if (max > min) target = Math.Clamp(target, Math.Max(min, MinPct), max);
                        sensor.Control.SetSoftware(target);
                        _overridden.Add(kv.Key);
                    }
                    catch (Exception ex) { Log.Write("Fan " + kv.Key + ": " + ex.Message); }
                }
            }
        }

        /// <summary>Gives every fan back to the BIOS / driver.</summary>
        public void RestoreAll()
        {
            lock (_lock)
            {
                foreach (var g in _groups.Values) g.Mode = "auto";
                foreach (var id in _overridden)
                {
                    try { if (_controls.TryGetValue(id, out var c)) c.sensor.Control.SetDefault(); } catch { }
                }
                _overridden.Clear();
            }
        }

        public object Snapshot()
        {
            lock (_lock)
            {
                return new
                {
                    gpu = new { mode = _groups["gpu"].Mode, pct = _groups["gpu"].Pct, available = HasControlsUnlocked("gpu") },
                    @case = new { mode = _groups["case"].Mode, pct = _groups["case"].Pct, available = HasControlsUnlocked("case") },
                    enabled = App.Settings.FanControl
                };
            }
        }

        bool HasControlsUnlocked(string group)
        {
            foreach (var c in _controls.Values) if (c.group == group) return true;
            return false;
        }
    }
}

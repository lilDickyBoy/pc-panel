using System;
using System.Collections.Generic;
using PcPanel.Core;
using Windows.Devices.Enumeration;

namespace PcPanel.Services
{
    /// <summary>
    /// Watches paired Bluetooth devices (classic + Low Energy) and reports when one connects or disconnects,
    /// so the notch can show "AirPods – Connected".
    /// </summary>
    public sealed class BluetoothService
    {
        public static readonly BluetoothService I = new BluetoothService();
        /// <summary>(device name, connected). Raised on a background thread.</summary>
        public event Action<string, bool> DeviceChanged;

        const string ClassicProtocol = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
        const string LeProtocol = "{bb7bb05e-5972-42b5-94fc-76eaa7084d49}";
        const string IsConnected = "System.Devices.Aep.IsConnected";

        readonly object _lock = new object();
        readonly Dictionary<string, (string name, bool connected)> _devices = new Dictionary<string, (string, bool)>();
        readonly Dictionary<string, DateTime> _lastShown = new Dictionary<string, DateTime>();
        DeviceWatcher _classic, _le;
        int _ready;

        public void Start()
        {
            try
            {
                _classic = Create(ClassicProtocol);
                _le = Create(LeProtocol);
                _classic.Start();
                _le.Start();
            }
            catch (Exception ex) { Log.Write("Bluetooth: " + ex.Message); }
        }

        DeviceWatcher Create(string protocol)
        {
            var w = DeviceInformation.CreateWatcher(
                $"System.Devices.Aep.ProtocolId:=\"{protocol}\" AND System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True",
                new[] { IsConnected }, DeviceInformationKind.AssociationEndpoint);
            w.Added += (s, d) =>
            {
                lock (_lock) _devices[d.Id] = (d.Name, Bool(d.Properties, IsConnected));
            };
            w.Updated += (s, u) =>
            {
                if (!u.Properties.TryGetValue(IsConnected, out var v) || v is not bool now) return;
                string name; bool was;
                lock (_lock)
                {
                    if (!_devices.TryGetValue(u.Id, out var st)) return;
                    name = st.name; was = st.connected;
                    _devices[u.Id] = (name, now);
                }
                if (was == now || _ready < 2) return;
                Report(name, now);
            };
            w.Removed += (s, u) => { lock (_lock) _devices.Remove(u.Id); };
            w.EnumerationCompleted += (s, e) => _ready++;
            return w;
        }

        void Report(string name, bool connected)
        {
            // dual-mode devices (classic + LE) report twice – show it once
            string key = (name ?? "") + "|" + connected;
            lock (_lock)
            {
                if (_lastShown.TryGetValue(key, out var t) && (DateTime.Now - t).TotalSeconds < 4) return;
                _lastShown[key] = DateTime.Now;
            }
            try { DeviceChanged?.Invoke(name, connected); } catch { }
        }

        static bool Bool(IReadOnlyDictionary<string, object> p, string key) =>
            p != null && p.TryGetValue(key, out var v) && v is bool b && b;
    }
}

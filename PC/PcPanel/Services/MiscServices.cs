using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using PcPanel.Core;

namespace PcPanel.Services
{
    // ================================================================== countdown in the notch
    public sealed class CountdownService
    {
        public static readonly CountdownService I = new CountdownService();

        Timer _timer;
        public bool Active { get; private set; }
        public DateTime End { get; private set; }
        public int TotalSeconds { get; private set; }
        public string Label { get; private set; } = "";

        /// <summary>Raised on any thread.</summary>
        public event Action Changed;

        public int Remaining => Active ? Math.Max(0, (int)Math.Ceiling((End - DateTime.Now).TotalSeconds)) : 0;

        public void Start(int seconds, string label)
        {
            seconds = Math.Clamp(seconds, 1, 24 * 3600);
            _timer?.Dispose();
            Active = true;
            TotalSeconds = seconds;
            Label = (label ?? "").Trim();
            if (Label.Length > 40) Label = Label.Substring(0, 40);
            End = DateTime.Now.AddSeconds(seconds);
            _timer = new Timer(_ => Finish(), null, TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan);
            Changed?.Invoke();
        }

        void Finish()
        {
            var label = Label;
            Cancel(false);
            App.Ui(() =>
            {
                App.Notch?.ShowInfo(Glyph.Timer, string.IsNullOrEmpty(label) ? Loc.T("pc.countdown.done") : label + " · " + Loc.T("pc.countdown.done"), 6);
                SoundService.Play("done", true);
            });
            App.Server?.Broadcast(new { type = "countdownDone", label });
            Changed?.Invoke();
        }

        public void Cancel(bool notify = true)
        {
            _timer?.Dispose(); _timer = null;
            Active = false;
            Label = "";
            if (notify) Changed?.Invoke();
        }

        public object State() => new { type = "countdown", active = Active, remaining = Remaining, total = TotalSeconds, label = Label };
    }

    // ================================================================== auto lock when the phone leaves
    /// <summary>
    /// When the phone that was connected can no longer be reached on the network for a minute
    /// (you left home / Wi-Fi off), the PC locks itself. Closing the app on the phone does not count.
    /// </summary>
    public static class AutoLockService
    {
        static string _phoneIp;
        static int _misses;
        static Timer _timer;

        public static void Start()
        {
            _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }

        public static void OnConnected(Session s) { _phoneIp = s.Ip; _misses = 0; }

        static void Tick()
        {
            try
            {
                var st = App.Settings;
                if (st == null || !st.AutoLock || string.IsNullOrEmpty(_phoneIp)) return;
                var server = App.Server;
                if (server != null && server.AnyConnected) { _misses = 0; return; }
                bool reachable = false;
                try
                {
                    using var p = new Ping();
                    for (int i = 0; i < 2 && !reachable; i++)
                        reachable = p.Send(_phoneIp, 1200).Status == IPStatus.Success;
                }
                catch { }
                if (reachable) { _misses = 0; return; }
                if (++_misses >= 6)
                {
                    _misses = 0;
                    _phoneIp = null; // only once until the phone comes back
                    Log.Write("Auto lock: phone left the network");
                    Native.LockWorkStation();
                }
            }
            catch (Exception ex) { Log.Write("AutoLock: " + ex.Message); }
        }
    }
}

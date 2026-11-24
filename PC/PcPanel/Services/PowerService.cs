using System;
using System.Diagnostics;
using System.Threading;
using PcPanel.Core;

namespace PcPanel.Services
{
    public sealed class PowerService
    {
        public static readonly PowerService I = new PowerService();

        Timer _timer;
        Timer _warnTimer;
        public string TimerAction { get; private set; } = "";
        public DateTime TimerEnd { get; private set; }
        public bool TimerActive => _timer != null;

        public event Action TimerChanged;

        public void Do(string action)
        {
            switch (action)
            {
                case "lock": Native.LockWorkStation(); break;
                case "displayoff":
                    // Give the phone a moment so the "tap" doesn't wake the screen again
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        Thread.Sleep(600);
                        Native.SendMessage(Native.HWND_BROADCAST, Native.WM_SYSCOMMAND, (IntPtr)Native.SC_MONITORPOWER, (IntPtr)2);
                    });
                    break;
                case "sleep":
                    ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(800); Native.SetSuspendState(false, false, false); });
                    break;
                case "shutdown": Run("shutdown", "/s /t 1"); break;
                case "restart": Run("shutdown", "/r /t 1"); break;
            }
        }

        static void Run(string exe, string args)
        {
            try { Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false }); }
            catch (Exception ex) { Log.Write("Power: " + ex.Message); }
        }

        public void SetTimer(string action, int minutes)
        {
            Cancel(false);
            if (minutes <= 0) return;
            TimerAction = action == "shutdown" ? "shutdown" : "sleep";
            TimerEnd = DateTime.Now.AddMinutes(minutes);
            _timer = new Timer(_ =>
            {
                var a = TimerAction;
                Cancel(true);
                Do(a);
            }, null, TimeSpan.FromMinutes(minutes), Timeout.InfiniteTimeSpan);
            if (minutes > 1)
            {
                _warnTimer = new Timer(_ =>
                {
                    App.UiNotchInfo(TimerAction == "shutdown" ? Glyph.Power : Glyph.Moon,
                        Loc.T(TimerAction == "shutdown" ? "pc.timer.shutdown" : "pc.timer.sleep"), 6);
                    App.UiTrayBalloon(Loc.T(TimerAction == "shutdown" ? "pc.timer.shutdown" : "pc.timer.sleep"));
                }, null, TimeSpan.FromMinutes(minutes - 1), Timeout.InfiniteTimeSpan);
            }
            TimerChanged?.Invoke();
        }

        public void Cancel(bool notify = true)
        {
            _timer?.Dispose(); _timer = null;
            _warnTimer?.Dispose(); _warnTimer = null;
            TimerAction = "";
            if (notify) TimerChanged?.Invoke();
        }

        public object State()
        {
            return new
            {
                type = "timer",
                active = TimerActive,
                action = TimerAction,
                remaining = TimerActive ? Math.Max(0, (int)(TimerEnd - DateTime.Now).TotalSeconds) : 0
            };
        }
    }
}

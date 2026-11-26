using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using PcPanel.Core;
using PcPanel.Services;
using PcPanel.UI;

namespace PcPanel
{
    public partial class App : Application
    {
        public static AppSettings Settings { get; private set; }
        public static Server Server { get; private set; }
        public static NetInfo Net { get; private set; } = new NetInfo();
        public static NotchWindow Notch { get; private set; }
        public static string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0.0";

        static App _self;
        Mutex _mutex;
        System.Windows.Forms.NotifyIcon _tray;
        MainWindow _main;
        DateTime _lastMediaPush;
        bool _mediaPushPending;
        int _lastArtSent = -1;
        string _pendingToastPath;

        protected override void OnStartup(StartupEventArgs e)
        {
            // PC Panel needs admin rights for the hardware sensors and fan control. Instead of a UAC prompt
            // on every launch, a normal launch starts the "PC Panel Launcher" scheduled task (already approved
            // once) and quits. Only the very first launch after installing asks for permission.
            if (!SystemSetup.IsElevated && !e.Args.Contains("--no-elevate"))
            {
                if (SystemSetup.RunLauncherTask()) { Shutdown(); return; }
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath, "--launched") { UseShellExecute = true, Verb = "runas" });
                    Shutdown();
                    return;
                }
                catch { /* UAC declined: keep running without admin rights (fewer sensors) */ }
            }

            _mutex = new Mutex(true, "Local\\PCPanel.SingleInstance", out bool created);
            if (!created)
            {
                try { EventWaitHandle.OpenExisting("Local\\PCPanel.Show").Set(); } catch { }
                Shutdown();
                return;
            }
            _self = this;
            base.OnStartup(e);

            DispatcherUnhandledException += (s, ex) => { Log.Write("UI error: " + ex.Exception); ex.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (s, ex) => Log.Write("Fatal: " + ex.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (s, ex) => { Log.Write("Task: " + ex.Exception?.Message); ex.SetObserved(); };

            Settings = AppSettings.Load();
            Loc.Load(Settings.Lang);
            Net = NetInfo.Get();
            System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += (s, a) =>
            {
                Net = NetInfo.Get();
                Ui(() => _main?.RefreshQr());
            };

            Task.Run(SystemSetup.EnsureInstalled);

            AudioService.I.Init();
            AudioService.I.VolumeChanged += OnVolumeChanged;

            Notch = new NotchWindow();
            ClipboardService.Init();

            MediaService.I.Changed += () => Ui(OnMediaChanged);
            _ = MediaService.I.InitAsync();

            PowerService.I.TimerChanged += () => Server?.Broadcast(PowerService.I.State());
            CountdownService.I.Changed += () =>
            {
                Server?.Broadcast(CountdownService.I.State());
                Ui(() => Notch?.CountdownChanged());
            };
            SensorService.I.GameChanged += () =>
            {
                Server?.Broadcast(new { type = "game", on = SensorService.I.InGame });
                Ui(() => Notch?.SettingsChanged());
            };
            SpotifyService.I.StateChanged += () => Ui(SharedSettings.Push);
            BluetoothService.I.DeviceChanged += (name, on) => Ui(() => Notch?.ShowBluetooth(name, on));
            BluetoothService.I.Start();
            ActivityService.I.Start();
            ActivityService.I.Changed += () => Ui(() => _main?.OnActivityChanged());
            ScreenshotWatcher.I.Start();
            DownloadWatcher.I.Start();

            Server = new Server();
            Server.PhoneConnected += s => Ui(() =>
            {
                AutoLockService.OnConnected(s);
                if (Settings.NotchConnectInfo) Notch.ShowInfo(Glyph.Phone, Loc.F("pc.notch.connected", s.DeviceName));
                SoundService.Play("connect");
            });
            Server.PhoneDisconnected += s => Ui(() =>
            {
                ScreenService.Stop(s);
                // closing the app on the phone ("background") is quiet; a real disconnect is announced
                if (s.ByeReason != "background")
                {
                    if (Settings.NotchConnectInfo) Notch.ShowInfo(Glyph.Unlink, Loc.F("pc.notch.disconnected", s.DeviceName));
                    SoundService.Play("disconnect");
                }
                if (!Server.AnyConnected) { FanService.I.RestoreAll(); FileService.AbortAll(); }
            });
            Server.SessionsChanged += () =>
            {
                SensorService.I.Wake();
                Ui(() => { _main?.RefreshDevices(); UpdateTrayText(); Notch?.PhoneBatteryChanged(); });
            };
            Server.Start();
            SharedSettings.Changed += Services.Screen.SecondScreenService.I.OnSettingsChanged;
            Services.Screen.VirtualDisplay.CleanUpAfterCrash();
            SensorService.I.Start();
            AutoLockService.Start();

            SetupTray();
            new Thread(() =>
            {
                using var ev = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\PCPanel.Show");
                while (true) { ev.WaitOne(); Ui(ShowMain); }
            }) { IsBackground = true, Name = "ShowSignal" }.Start();

            bool trayStart = e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
            if (!trayStart || !Settings.EverPaired) ShowMain();
        }

        // ---------------------------------------------------------------- threading helpers
        public static void Ui(Action a)
        {
            var d = Current?.Dispatcher;
            if (d == null) return;
            if (d.CheckAccess()) a(); else d.BeginInvoke(a);
        }

        public static Task<T> UiGet<T>(Func<T> f)
        {
            var d = Current?.Dispatcher;
            if (d == null) return Task.FromResult(default(T));
            if (d.CheckAccess()) return Task.FromResult(f());
            return d.InvokeAsync(f).Task;
        }

        // ---------------------------------------------------------------- media / volume
        void OnMediaChanged()
        {
            var mm = MediaService.I;
            LyricsService.I.OnTrack(mm.Title, mm.Artist, mm.Album, mm.DurationSec);
            Notch.OnMediaChanged();
            if (_mediaPushPending) return;
            var wait = TimeSpan.FromMilliseconds(250) - (DateTime.Now - _lastMediaPush);
            if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
            _mediaPushPending = true;
            var t = new DispatcherTimer { Interval = wait };
            t.Tick += (s, e) =>
            {
                t.Stop();
                _mediaPushPending = false;
                _lastMediaPush = DateTime.Now;
                var m = MediaService.I;
                Server.Broadcast(Commands.MediaState());
                if (m.ArtVersion != _lastArtSent)
                {
                    _lastArtSent = m.ArtVersion;
                    Server.Broadcast(Commands.ArtState());
                }
            };
            t.Start();
        }

        void OnVolumeChanged()
        {
            if (Settings.NotchVolume) Notch.ShowVolume(AudioService.I.Level, AudioService.I.Muted);
            Server?.Broadcast(Commands.VolumeState());
        }

        // ---------------------------------------------------------------- helpers used by services
        public static void UiNotchInfo(string glyph, string text, double seconds = 3)
        {
            Ui(() => Notch?.ShowInfo(glyph, text, seconds));
        }

        public static void UiTrayBalloon(string text, string title = null)
        {
            Ui(() =>
            {
                try { _self?._tray?.ShowBalloonTip(4000, title ?? "PC Panel", text, System.Windows.Forms.ToolTipIcon.Info); } catch { }
            });
        }

        /// <summary>Windows notification for a received file; clicking it opens the folder.</summary>
        public static void ShowFileToast(string name, string path)
        {
            if (_self?._tray == null) return;
            _self._pendingToastPath = path;
            try { _self._tray.ShowBalloonTip(5000, Loc.T("pc.file.toastTitle"), name, System.Windows.Forms.ToolTipIcon.None); } catch { }
        }

        // ---------------------------------------------------------------- tray
        void SetupTray()
        {
            System.Drawing.Icon icon;
            try
            {
                var sri = GetResourceStream(new Uri("pack://application:,,,/Assets/PcPanel.ico"));
                icon = new System.Drawing.Icon(sri.Stream, new System.Drawing.Size(32, 32));
            }
            catch { icon = System.Drawing.SystemIcons.Application; }

            _tray = new System.Windows.Forms.NotifyIcon { Icon = icon, Visible = true, Text = "PC Panel" };
            _tray.MouseClick += (s, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) ShowMain(); };
            _tray.BalloonTipClicked += (s, e) =>
            {
                var p = _pendingToastPath;
                _pendingToastPath = null;
                if (p != null) SystemSetup.ShellOpen("/select," + p);
                else ShowMain();
            };
            _tray.BalloonTipClosed += (s, e) => _pendingToastPath = null;
            BuildTrayMenu();
            UpdateTrayText();
        }

        public void BuildTrayMenu()
        {
            var menu = new System.Windows.Forms.ContextMenuStrip
            {
                ShowImageMargin = false,
                BackColor = System.Drawing.Color.FromArgb(28, 28, 30),
                ForeColor = System.Drawing.Color.White,
                Renderer = new DarkMenuRenderer(),
                Font = new System.Drawing.Font("Segoe UI", 10.5f),
                Padding = new System.Windows.Forms.Padding(4)
            };
            menu.Items.Add(Loc.T("pc.tray.open"), null, (s, e) => ShowMain());
            menu.Items.Add(Loc.T("pc.tray.findPhone"), null, (s, e) => FindPhone());
            menu.Items.Add(Loc.T("pc.tray.sendFile"), null, (s, e) => { ShowMain(); _main?.PickAndSendFiles(); });
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(Loc.T("pc.tray.disconnect"), null, (s, e) => Server.KickAll());
            menu.Items.Add(Loc.T("pc.tray.quit"), null, (s, e) => Quit());
            foreach (System.Windows.Forms.ToolStripItem it in menu.Items) it.Padding = new System.Windows.Forms.Padding(6, 4, 6, 4);
            var old = _tray.ContextMenuStrip;
            _tray.ContextMenuStrip = menu;
            old?.Dispose();
        }

        void UpdateTrayText()
        {
            if (_tray == null) return;
            var sessions = Server?.Sessions ?? new Session[0];
            var t = "PC Panel - " + (sessions.Length > 0 ? Loc.T("pc.status.connected") + " (" + sessions[0].DeviceName + ")" : Loc.T("pc.status.waiting"));
            _tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
        }

        public static void FindPhone()
        {
            var sessions = Server?.Sessions ?? new Session[0];
            if (sessions.Length == 0) { UiTrayBalloon(Loc.T("pc.devices.none")); return; }
            foreach (var s in sessions) _ = s.SendAsync(new { type = "ring" });
            Notch?.ShowInfo(Glyph.Bell, Loc.T("pc.findPhone.ringing"), 3);
        }

        // ---------------------------------------------------------------- main window
        public void ShowMain()
        {
            if (_main == null)
            {
                _main = new MainWindow();
                _main.Closed += (s, e) =>
                {
                    _main = null;
                    // Give memory back after the window is gone
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Native.SetProcessWorkingSetSize(Native.GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
                };
                _main.Show();
            }
            else
            {
                if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
                _main.Activate();
            }
        }

        public static void ShowMainPage(string page)
        {
            _self?.ShowMain();
            _self?._main?.GoTo(page);
        }

        public static void ShowMainStatic() => _self?.ShowMain();

        public static void LanguageChanged()
        {
            _self?.BuildTrayMenu();
            _self?.UpdateTrayText();
            _self?._main?.ApplyTexts();
            Notch?.LanguageChanged();
        }

        public static void Quit()
        {
            try { Services.Screen.PhoneMirror.I.Stop(); } catch { }
            try { Services.Screen.SecondScreenService.I.Shutdown(); } catch { }
            try { FanService.I.RestoreAll(); } catch { }
            try { Server?.Stop(); } catch { }
            try { if (DisplayService.NightLevel > 0) DisplayService.SetNight(0); } catch { }
            try { SensorService.I.Stop(); } catch { }
            try { ClipboardService.Shutdown(); } catch { }
            if (_self?._tray != null) { _self._tray.Visible = false; _self._tray.Dispose(); }
            Current.Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { FanService.I.RestoreAll(); } catch { }
            try { _tray?.Dispose(); } catch { }
            base.OnExit(e);
        }
    }

    sealed class DarkMenuRenderer : System.Windows.Forms.ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = true; }

        protected override void OnRenderItemText(System.Windows.Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = System.Drawing.Color.White;
            base.OnRenderItemText(e);
        }

        sealed class DarkColors : System.Windows.Forms.ProfessionalColorTable
        {
            static readonly System.Drawing.Color Bg = System.Drawing.Color.FromArgb(28, 28, 30);
            static readonly System.Drawing.Color Hover = System.Drawing.Color.FromArgb(10, 132, 255);
            public override System.Drawing.Color ToolStripDropDownBackground => Bg;
            public override System.Drawing.Color MenuBorder => System.Drawing.Color.FromArgb(56, 56, 58);
            public override System.Drawing.Color MenuItemSelected => Hover;
            public override System.Drawing.Color MenuItemBorder => Hover;
            public override System.Drawing.Color MenuItemSelectedGradientBegin => Hover;
            public override System.Drawing.Color MenuItemSelectedGradientEnd => Hover;
            public override System.Drawing.Color ImageMarginGradientBegin => Bg;
            public override System.Drawing.Color ImageMarginGradientMiddle => Bg;
            public override System.Drawing.Color ImageMarginGradientEnd => Bg;
            public override System.Drawing.Color SeparatorDark => System.Drawing.Color.FromArgb(56, 56, 58);
            public override System.Drawing.Color SeparatorLight => System.Drawing.Color.FromArgb(56, 56, 58);
        }
    }
}

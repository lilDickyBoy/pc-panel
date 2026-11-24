using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using PcPanel.Core;
using PcPanel.Services;

namespace PcPanel.UI
{
    /// <summary>
    /// The notch / Dynamic Island at the top centre of the screen.
    ///
    /// Base modes (what it shows when nothing else is going on):
    ///   Hidden, Idle (always-on: clock + phone battery), Music (compact, expands on hover), Countdown.
    /// Transient modes (shown for a few seconds, then it goes back to the base mode):
    ///   Info (connections, charging, clipboard, files…), Volume, Notif (phone notification), Call.
    /// If the notch is already open, it morphs to the new size and the content cross-fades with a
    /// blur like iOS – it never closes in between.
    /// </summary>
    public partial class NotchWindow : Window
    {
        enum Mode { Hidden, Idle, Music, Countdown, Info, Volume, Notif, Call, DropPeek, DropHover, Sending }

        const double CW = 312, CH = 38, CR = 19;     // compact music
        const double EW = 470, EH = 178, ER = 34;    // expanded music
        const double IW = 196, IH = 32, IR = 16;     // idle
        const double VW = 320;                        // volume
        const double NW = 470, NH = 72, NR = 30;     // notification
        const double KW = 420, KH = 64, KR = 30;     // call
        const int Fps = 144;

        Mode _shown = Mode.Hidden;
        Mode _transient = Mode.Hidden; // Hidden = none
        bool _expanded;
        bool _clickThrough = true;
        IntPtr _hwnd;
        int _inTicks, _outTicks;
        bool _wasPlaying;
        DateTime _pausedAt = DateTime.MinValue;
        FrameworkElement _currentPanel;
        FrameworkElement[] _panels;

        readonly DispatcherTimer _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        readonly DispatcherTimer _barsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        readonly DispatcherTimer _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        readonly DispatcherTimer _transientTimer = new DispatcherTimer();
        readonly DispatcherTimer _hideFinishTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        readonly DispatcherTimer _watchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        readonly DispatcherTimer _cdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };

        AnimationClock _spinClock;
        readonly SolidColorBrush _accent;
        readonly Random _rnd = new Random();
        readonly double[] _bar = new double[4];
        ScaleTransform[] _barScalesC, _barScalesE;
        int _lastArtVersion = -1;
        string _lastTitle;
        readonly BatteryGlyph _infoBattery = new BatteryGlyph(24, 12);
        readonly BatteryGlyph _idleBattery = new BatteryGlyph(22, 11);

        public NotchWindow()
        {
            InitializeComponent();
            _accent = (SolidColorBrush)Resources["AccentBrush"];
            _panels = new FrameworkElement[] { IdlePanel, CompactPanel, ExpandedPanel, InfoPanel, VolumePanel, NotifPanel, CallPanel, CountdownPanel, DropPeekPanel, DropHoverPanel, SendPanel };
            foreach (var p in _panels)
            {
                p.RenderTransformOrigin = new Point(0.5, 0.5);
                p.RenderTransform = new ScaleTransform(1, 1);
            }

            foreach (Rectangle r in BarsC.Children) r.Fill = _accent;
            foreach (Rectangle r in BarsE.Children) r.Fill = _accent;
            _barScalesC = BarsC.Children.Cast<Rectangle>().Select(r => (ScaleTransform)r.RenderTransform).ToArray();
            _barScalesE = BarsE.Children.Cast<Rectangle>().Select(r => (ScaleTransform)r.RenderTransform).ToArray();

            GPrev.Text = Glyph.Prev; GNext.Text = Glyph.Next; GPlay.Text = Glyph.Play;
            NoArtC.Text = Glyph.Music; NoArtE.Text = Glyph.Music;
            CallGlyph.Text = Glyph.Call;
            CdGlyph.Text = Glyph.Timer;
            InfoIconHost.Children.Add(_infoBattery);
            _infoBattery.HorizontalAlignment = HorizontalAlignment.Center;
            _infoBattery.VerticalAlignment = VerticalAlignment.Center;
            _infoBattery.Visibility = Visibility.Collapsed;
            IdleBatteryHost.Children.Add(_idleBattery);
            _idleBattery.VerticalAlignment = VerticalAlignment.Center;

            WireButton(BtnPrev, () => _ = MediaService.I.Prev());
            WireButton(BtnPlay, () => _ = MediaService.I.TogglePlayPause());
            WireButton(BtnNext, () => _ = MediaService.I.Next());

            // vinyl spin: one clock drives both discs, paused while music is paused
            var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(8)) { RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(spin, 60);
            _spinClock = spin.CreateClock();
            RotC.ApplyAnimationClock(RotateTransform.AngleProperty, _spinClock);
            RotE.ApplyAnimationClock(RotateTransform.AngleProperty, _spinClock);
            _spinClock.Controller.Pause();

            _hoverTimer.Tick += HoverTick;
            _barsTimer.Tick += BarsTick;
            _progressTimer.Tick += (s, e) => UpdateProgress();
            _transientTimer.Tick += (s, e) => { _transientTimer.Stop(); _transient = Mode.Hidden; Refresh(); };
            _hideFinishTimer.Tick += (s, e) => { _hideFinishTimer.Stop(); if (_shown == Mode.Hidden) { Hide(); StopLoops(); } };
            _watchTimer.Tick += (s, e) => { if (_shown == Mode.Idle) UpdateIdle(); Refresh(); };
            _watchTimer.Start();
            _cdTimer.Tick += (s, e) => UpdateCountdown();

            SourceInitialized += (s, e) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                var ex = (long)Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE);
                ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT;
                Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, (IntPtr)ex);
                InitDrop();
            };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) => Dispatcher.BeginInvoke(new Action(PositionWindow));
            ApplyStyle();
            PositionWindow();
            Dispatcher.BeginInvoke(new Action(() => Refresh()), DispatcherPriority.ApplicationIdle);
        }

        void PositionWindow()
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = 0;
        }

        bool IsIsland => App.Settings.NotchStyle == "island";

        void ApplyStyle()
        {
            if (IsIsland)
            {
                Island.Margin = new Thickness(0, 9, 0, 0);
                IslandShape.Margin = new Thickness(0);
                EarL.Visibility = EarR.Visibility = Visibility.Collapsed;
                IslandShape.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.45, Direction = 270 };
            }
            else
            {
                Island.Margin = new Thickness(0);
                IslandShape.Margin = new Thickness(0, -40, 0, 0);
                EarL.Visibility = EarR.Visibility = Visibility.Visible;
                IslandShape.Effect = null;
            }
        }

        // ================================================================== public API (UI thread)
        public void SettingsChanged()
        {
            ApplyStyle();
            Refresh(force: true);
        }

        public void LanguageChanged()
        {
            _lastTitle = null;
            UpdateMusicContent();
            UpdateIdle();
            UpdateCountdown();
        }

        public void OnMediaChanged()
        {
            var m = MediaService.I;
            if (_wasPlaying && !m.Playing) _pausedAt = DateTime.Now;
            _wasPlaying = m.Playing;
            UpdateMusicContent();
            if (_shown == Mode.Music)
            {
                if (m.Playing) { _spinClock.Controller.Resume(); _barsTimer.Start(); }
                else _spinClock.Controller.Pause();
            }
            Refresh();
        }

        public void CountdownChanged()
        {
            UpdateCountdown();
            Refresh(force: _shown == Mode.Countdown);
        }

        public void PhoneBatteryChanged()
        {
            UpdateIdle();
        }

        public void ShowInfo(string glyph, string text, double seconds = 3)
        {
            if (!CanShowTransient()) return;
            InfoGlyph.Visibility = Visibility.Visible;
            _infoBattery.Visibility = Visibility.Collapsed;
            InfoGlyph.Text = glyph;
            InfoGlyph.Foreground = _accent;
            InfoGlyph.RenderTransform = null;
            InfoText.Text = text;
            InfoText.Foreground = Brushes.White;
            ShowTransient(Mode.Info, seconds);
        }

        public void ShowBattery(int level, bool charging, string text)
        {
            if (!CanShowTransient()) return;
            InfoGlyph.Visibility = Visibility.Collapsed;
            _infoBattery.Visibility = Visibility.Visible;
            _infoBattery.Set(level, charging, animate: true);
            InfoText.Text = text;
            InfoText.Foreground = charging ? new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)) : level <= 15 ? new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)) : Brushes.White;
            ShowTransient(Mode.Info, 3.2);
        }

        public void ShowVolume(float level, bool muted)
        {
            if (!CanShowTransient() || _transient == Mode.Call) return;
            VolGlyph.Text = muted || level <= 0.001f ? Glyph.Mute : level < 0.34f ? Glyph.Volume1 : level < 0.67f ? Glyph.Volume2 : Glyph.Volume3;
            VolText.Text = muted ? "0%" : ((int)Math.Round(level * 100)) + "%";
            double trackW = VW - 32 - 26 - 44 - 12;
            var a = new DoubleAnimation(muted ? 0 : trackW * level, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            VolFill.BeginAnimation(WidthProperty, a);
            if (_transient == Mode.Volume && _shown == Mode.Volume) { Restart(_transientTimer, 1.6); return; }
            ShowTransient(Mode.Volume, 1.6);
        }

        public void ShowNotification(BitmapSource icon, string app, string title, string text)
        {
            if (!CanShowTransient() || _transient == Mode.Call) return;
            NotifIcon.Source = icon;
            NotifGlyph.Text = icon == null ? Glyph.Bell : "";
            NotifTitle.Text = string.IsNullOrWhiteSpace(title) ? app : title;
            NotifApp.Text = string.IsNullOrWhiteSpace(title) ? "" : app;
            NotifText.Text = (text ?? "").Length > 160 ? text.Substring(0, 160) + "…" : text;
            ShowTransient(Mode.Notif, 5);
        }

        public void ShowCall(BitmapSource icon, string name, string app)
        {
            if (!CanShowTransient()) return;
            CallIcon.Source = icon;
            CallGlyph.Visibility = icon == null ? Visibility.Visible : Visibility.Collapsed;
            CallName.Text = name;
            CallSub.Text = Loc.T("pc.call.incoming") + (string.IsNullOrWhiteSpace(app) ? "" : " · " + app);
            var sc = (ScaleTransform)CallPulse.RenderTransform;
            var pulse = new DoubleAnimation(1, 1.25, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            Timeline.SetDesiredFrameRate(pulse, 40);
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
            ShowTransient(Mode.Call, 30);
        }

        public void EndCall()
        {
            if (_transient != Mode.Call) return;
            var sc = (ScaleTransform)CallPulse.RenderTransform;
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _transientTimer.Stop();
            _transient = Mode.Hidden;
            Refresh();
        }

        // ================================================================== state machine
        bool CanShowTransient() => App.Settings.NotchEnabled && Allowed();

        Mode ComputeBase()
        {
            var s = App.Settings;
            var m = MediaService.I;
            if (!s.NotchEnabled || !Allowed()) return Mode.Hidden;
            if (CountdownService.I.Active) return Mode.Countdown;
            if (m.HasMedia && (m.Playing || _expanded || (DateTime.Now - _pausedAt).TotalSeconds < 4)) return Mode.Music;
            if (s.NotchMode == "always") return Mode.Idle;
            return Mode.Hidden;
        }

        void ShowTransient(Mode m, double seconds)
        {
            _transient = m;
            Restart(_transientTimer, seconds);
            if (_phase != DropPhase.None) return;   // a drag/drop owns the notch right now
            Go(m, sameContentChanged: _shown == m);
        }

        void Refresh(bool force = false)
        {
            if (!Allowed() || !App.Settings.NotchEnabled) _transient = Mode.Hidden;
            var target = _phase != DropPhase.None ? DropMode : _transient != Mode.Hidden ? _transient : ComputeBase();
            if (target == _shown && !force) return;
            Go(target, false);
        }

        void Go(Mode target, bool sameContentChanged)
        {
            _shown = target;
            if (target != Mode.Music && _expanded) { _expanded = false; SetClickThrough(true); _progressTimer.Stop(); }
            if (target != Mode.Countdown) _cdTimer.Stop();
            if (target != Mode.DropPeek && target != Mode.DropHover) StopDropLoops();

            switch (target)
            {
                case Mode.Hidden:
                    GoHidden();
                    return;
                case Mode.Idle:
                    UpdateIdle();
                    Morph(IW, IH, IR);
                    ShowPanel(IdlePanel, sameContentChanged);
                    break;
                case Mode.Music:
                    if (_expanded) { Morph(EW, EH, ER); ShowPanel(ExpandedPanel, false); }
                    else { Morph(CW, CH, CR); ShowPanel(CompactPanel, false); }
                    if (MediaService.I.Playing) _spinClock.Controller.Resume(); else _spinClock.Controller.Pause();
                    _barsTimer.Start();
                    RestartMarquee(TitleHostC, TitleC);
                    break;
                case Mode.Countdown:
                    UpdateCountdown();
                    CountdownPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Morph(Math.Clamp(CountdownPanel.DesiredSize.Width + 36, 230, 420), CH, CR);
                    ShowPanel(CountdownPanel, sameContentChanged);
                    _cdTimer.Start();
                    break;
                case Mode.Info:
                    InfoPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Morph(Math.Clamp(InfoPanel.DesiredSize.Width + 48, 200, 600), CH, CR);
                    ShowPanel(InfoPanel, sameContentChanged);
                    break;
                case Mode.Volume:
                    Morph(VW, CH, CR);
                    ShowPanel(VolumePanel, sameContentChanged);
                    break;
                case Mode.Notif:
                    Morph(NW, NH, NR);
                    ShowPanel(NotifPanel, sameContentChanged);
                    break;
                case Mode.Call:
                    Morph(KW, KH, KR);
                    ShowPanel(CallPanel, sameContentChanged);
                    break;
                case Mode.DropPeek:
                    UpdatePeek();
                    Morph(_peekNear ? PNW : PW, _peekNear ? PNH : PH, _peekNear ? PNH / 2 : PR);
                    ShowPanel(DropPeekPanel, false);
                    StartDropLoops(Mode.DropPeek);
                    break;
                case Mode.DropHover:
                    Morph(HW, HH, HR);
                    ShowPanel(DropHoverPanel, false);
                    StartDropLoops(Mode.DropHover);
                    break;
                case Mode.Sending:
                    Morph(SW, SH, SR);
                    ShowPanel(SendPanel, false);
                    break;
            }
            _hoverTimer.Start();
        }

        void GoHidden()
        {
            _expanded = false;
            SetClickThrough(true);
            ShowPanel(null, false);
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            Animate(Island, WidthProperty, IsIsland ? 90 : 140, 300, ease);
            Animate(Island, HeightProperty, 0, 300, ease);
            Animate(IslandShape, Rectangle.RadiusXProperty, 12, 300, ease);
            Animate(IslandShape, Rectangle.RadiusYProperty, 12, 300, ease);
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)) { BeginTime = TimeSpan.FromMilliseconds(IsIsland ? 120 : 200) };
            Island.BeginAnimation(OpacityProperty, fade);
            _hideFinishTimer.Stop(); _hideFinishTimer.Start();
        }

        void Expand()
        {
            _expanded = true;
            SetClickThrough(false);
            Morph(EW, EH, ER);
            ShowPanel(ExpandedPanel, false);
            UpdateProgress();
            _progressTimer.Start();
            RestartMarquee(TitleHostE, TitleE);
        }

        void Collapse()
        {
            _expanded = false;
            SetClickThrough(true);
            _progressTimer.Stop();
            if (!MediaService.I.Playing) _pausedAt = DateTime.Now;
            var target = ComputeBase();
            if (target == Mode.Music)
            {
                Morph(CW, CH, CR);
                ShowPanel(CompactPanel, false);
                RestartMarquee(TitleHostC, TitleC);
            }
            else Go(target, false);
        }

        void StopLoops()
        {
            _hoverTimer.Stop(); _barsTimer.Stop(); _progressTimer.Stop(); _cdTimer.Stop();
            _spinClock.Controller.Pause();
        }

        // ================================================================== animation helpers
        void Morph(double w, double h, double r)
        {
            _hideFinishTimer.Stop();
            if (!IsVisible) { PositionWindow(); Show(); }
            IEasingFunction ease = new SpringEase { Damping = 0.66, Stiffness = 12.5 };
            const int ms = 640;
            if (IsIsland) r = Math.Min(r, h / 2);
            Animate(Island, WidthProperty, w, ms, ease);
            Animate(Island, HeightProperty, h, ms, ease);
            Animate(IslandShape, Rectangle.RadiusXProperty, r, ms, ease);
            Animate(IslandShape, Rectangle.RadiusYProperty, r, ms, ease);
            Island.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        }

        static void Animate(DependencyObject target, DependencyProperty dp, double to, int ms, IEasingFunction ease)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
            Timeline.SetDesiredFrameRate(a, Fps);
            if (target is UIElement ui) ui.BeginAnimation(dp, a);
            else if (target is Animatable an) an.BeginAnimation(dp, a);
        }

        /// <summary>iOS-like content swap: the old content shrinks + blurs away, the new one grows in from a blur.</summary>
        void ShowPanel(FrameworkElement panel, bool sameContentChanged)
        {
            foreach (var p in _panels)
            {
                if (p == panel)
                {
                    p.IsHitTestVisible = p == ExpandedPanel;
                    AnimateIn(p, p == _currentPanel && !sameContentChanged ? -1 : (p == _currentPanel ? 0 : 110), p == _currentPanel);
                }
                else if (p == _currentPanel || p.Opacity > 0.01)
                {
                    p.IsHitTestVisible = false;
                    AnimateOut(p);
                }
            }
            _currentPanel = panel;
        }

        static void AnimateIn(FrameworkElement p, int delayMs, bool same)
        {
            if (delayMs < 0)
            {
                // already visible and unchanged – just make sure it's fully shown
                p.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
                return;
            }
            var sc = (ScaleTransform)p.RenderTransform;
            var blur = new BlurEffect { Radius = 10, RenderingBias = RenderingBias.Performance };
            p.Effect = blur;
            var begin = TimeSpan.FromMilliseconds(delayMs);
            var spring = new SpringEase { Damping = 0.72, Stiffness = 13 };
            var s = new DoubleAnimation(same ? 0.96 : 0.86, 1, TimeSpan.FromMilliseconds(520)) { BeginTime = begin, EasingFunction = spring };
            Timeline.SetDesiredFrameRate(s, Fps);
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, s);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, s);
            var o = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { BeginTime = begin };
            p.BeginAnimation(OpacityProperty, o);
            var b = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(280)) { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            b.Completed += (x, y) => { if (ReferenceEquals(p.Effect, blur)) p.Effect = null; };
            blur.BeginAnimation(BlurEffect.RadiusProperty, b);
        }

        static void AnimateOut(FrameworkElement p)
        {
            var sc = (ScaleTransform)p.RenderTransform;
            var blur = new BlurEffect { Radius = 0, RenderingBias = RenderingBias.Performance };
            p.Effect = blur;
            var s = new DoubleAnimation(0.9, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, s);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, s);
            p.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(130)));
            var b = new DoubleAnimation(8, TimeSpan.FromMilliseconds(180));
            b.Completed += (x, y) => { if (ReferenceEquals(p.Effect, blur)) p.Effect = null; };
            blur.BeginAnimation(BlurEffect.RadiusProperty, b);
        }

        static void Restart(DispatcherTimer t, double seconds)
        {
            t.Stop();
            t.Interval = TimeSpan.FromSeconds(seconds);
            t.Start();
        }

        void SetClickThrough(bool on)
        {
            if (_hwnd == IntPtr.Zero || _clickThrough == on) return;
            _clickThrough = on;
            var ex = (long)Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE);
            ex = on ? ex | Native.WS_EX_TRANSPARENT : ex & ~(long)Native.WS_EX_TRANSPARENT;
            Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, (IntPtr)ex);
        }

        // ================================================================== content
        void UpdateIdle()
        {
            IdleTime.Text = DateTime.Now.ToString("t", System.Globalization.CultureInfo.CurrentCulture);
            var phone = App.Server?.Sessions.FirstOrDefault(x => x.Battery >= 0);
            if (phone != null)
            {
                IdleBatteryHost.Visibility = Visibility.Visible;
                IdleBatteryText.Text = Loc.F("pc.percent", phone.Battery);
                _idleBattery.Set(phone.Battery, phone.Charging, animate: false);
            }
            else IdleBatteryHost.Visibility = Visibility.Collapsed;
        }

        void UpdateCountdown()
        {
            var c = CountdownService.I;
            int rem = c.Remaining;
            var t = TimeSpan.FromSeconds(rem);
            CdTime.Text = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
            CdLabel.Text = string.IsNullOrWhiteSpace(c.Label) ? Loc.T("pc.countdown.title") : c.Label;
        }

        void UpdateMusicContent()
        {
            var m = MediaService.I;
            string title = string.IsNullOrWhiteSpace(m.Title) ? "—" : m.Title;
            if (title != _lastTitle)
            {
                _lastTitle = title;
                TitleC.Text = title;
                TitleE.Text = title;
                RestartMarquee(TitleHostC, TitleC);
                RestartMarquee(TitleHostE, TitleE);
            }
            ArtistE.Text = string.IsNullOrWhiteSpace(m.Artist) ? Loc.T("pc.notch.unknown") : m.Artist;
            AppE.Text = (m.Playing ? "" : Loc.T("pc.notch.paused") + "  ·  ") + m.AppDisplayName;
            TitleC.Foreground = m.Playing ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9F));
            GPlay.Text = m.Playing ? Glyph.Pause : Glyph.Play;

            if (m.ArtVersion != _lastArtVersion)
            {
                _lastArtVersion = m.ArtVersion;
                if (m.Art != null)
                {
                    var b = new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill };
                    b.Freeze();
                    ArtC.Fill = b; ArtE.Fill = b;
                    NoArtC.Visibility = NoArtE.Visibility = Visibility.Collapsed;
                    GroovesC.Opacity = GroovesE.Opacity = 0.35; // let the real cover show through
                }
                else
                {
                    ArtC.Fill = (Brush)Resources["NoArtBrush"]; ArtE.Fill = ArtC.Fill;
                    NoArtC.Visibility = NoArtE.Visibility = Visibility.Visible;
                    GroovesC.Opacity = GroovesE.Opacity = 1;
                }
                _accent.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(m.Accent, TimeSpan.FromMilliseconds(450)));
            }
            if (_expanded) UpdateProgress();
        }

        void UpdateProgress()
        {
            var m = MediaService.I;
            double pos = m.PositionSec, dur = m.DurationSec;
            PosText.Text = Fmt(pos);
            DurText.Text = dur > 0 ? Fmt(dur) : "--:--";
            double w = Track.ActualWidth;
            double target = dur > 0 ? w * Math.Clamp(pos / dur, 0, 1) : 0;
            TrackFill.BeginAnimation(WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(480)));
        }

        static string Fmt(double s)
        {
            if (s < 0 || double.IsNaN(s)) s = 0;
            var t = TimeSpan.FromSeconds(s);
            return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        }

        void RestartMarquee(Canvas host, TextBlock tb)
        {
            var tt = (TranslateTransform)tb.RenderTransform;
            tt.BeginAnimation(TranslateTransform.XProperty, null);
            tt.X = 0;
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double dist = tb.DesiredSize.Width - host.Width;
            if (dist <= 2) return;
            double scroll = dist / 30.0;
            var k = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            double t0 = 2.2, t1 = t0 + scroll, t2 = t1 + 1.6, t3 = t2 + 0.7;
            k.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            k.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t0))));
            k.KeyFrames.Add(new LinearDoubleKeyFrame(-dist, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t1))));
            k.KeyFrames.Add(new LinearDoubleKeyFrame(-dist, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t2))));
            k.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t3)), new CubicEase { EasingMode = EasingMode.EaseInOut }));
            Timeline.SetDesiredFrameRate(k, 60);
            tt.BeginAnimation(TranslateTransform.XProperty, k);
        }

        // ================================================================== loops
        void BarsTick(object sender, EventArgs e)
        {
            if (_shown != Mode.Music) { _barsTimer.Stop(); return; }
            bool playing = MediaService.I.Playing;
            float peak = playing ? AudioService.I.Peak : 0f;
            bool settled = true;
            for (int i = 0; i < 4; i++)
            {
                double target = playing ? Math.Clamp(peak * (0.55 + _rnd.NextDouble() * 0.75) * 1.7, 0.18, 1.0) : 0.18;
                _bar[i] += (target - _bar[i]) * 0.45;
                if (Math.Abs(target - _bar[i]) > 0.01) settled = false;
                _barScalesC[i].ScaleY = _bar[i];
                _barScalesE[i].ScaleY = _bar[i];
            }
            if (!playing && settled) _barsTimer.Stop();
        }

        void HoverTick(object sender, EventArgs e)
        {
            if (_shown != Mode.Music || !IsVisible)
            {
                if (_expanded) Collapse();
                if (_shown == Mode.Hidden) _hoverTimer.Stop();
                return;
            }
            if (!Native.GetCursorPos(out var p)) return;
            Point tl;
            try { tl = Island.PointToScreen(new Point(0, 0)); } catch { return; }
            var dpi = VisualTreeHelper.GetDpi(this);
            double w = Island.ActualWidth * dpi.DpiScaleX, h = Island.ActualHeight * dpi.DpiScaleY;
            double pad = _expanded ? 14 : 4;
            bool inside = p.X >= tl.X - pad && p.X <= tl.X + w + pad && p.Y >= tl.Y - 10 && p.Y <= tl.Y + h + pad;

            if (!_expanded)
            {
                _inTicks = inside ? _inTicks + 1 : 0;
                if (_inTicks >= 4) { _inTicks = 0; Expand(); }
            }
            else
            {
                _outTicks = inside ? 0 : _outTicks + 1;
                if (_outTicks >= 5) { _outTicks = 0; Collapse(); }
            }
        }

        static bool Allowed()
        {
            var s = App.Settings;
            bool hideFs = s.NotchHideFullscreen || (s.GameMode && SensorService.I.InGame);
            if (!hideFs) return true;
            try
            {
                if (Native.SHQueryUserNotificationState(out int st) == 0)
                    return !(st == 2 || st == 3 || st == 4);
            }
            catch { }
            return true;
        }

        // ================================================================== input
        void WireButton(Border b, Action click)
        {
            var sc = (ScaleTransform)b.RenderTransform;
            b.MouseEnter += (s, e) => b.Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
            b.MouseLeave += (s, e) => { b.Background = Brushes.Transparent; Pop(sc, 1); };
            b.MouseLeftButtonDown += (s, e) => { Pop(sc, 0.86); e.Handled = true; };
            b.MouseLeftButtonUp += (s, e) => { Pop(sc, 1, true); click(); e.Handled = true; };
        }

        static void Pop(ScaleTransform sc, double to, bool spring = false)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(spring ? 420 : 90));
            if (spring) a.EasingFunction = new SpringEase { Damping = 0.45, Stiffness = 14 };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        void Track_Click(object sender, MouseButtonEventArgs e)
        {
            var m = MediaService.I;
            if (m.DurationSec <= 0) return;
            double x = e.GetPosition(Track).X;
            double frac = Math.Clamp(x / Track.ActualWidth, 0, 1);
            _ = m.Seek(frac * m.DurationSec);
        }
    }

    /// <summary>iOS-style battery icon: outline, fill level, cap and a bolt while charging.</summary>
    public sealed class BatteryGlyph : Grid
    {
        readonly Border _fill;
        readonly TextBlock _bolt;
        readonly double _inner;

        public BatteryGlyph(double w, double h)
        {
            Width = w + 3;
            Height = h;
            var body = new Grid { Width = w, Height = h, HorizontalAlignment = HorizontalAlignment.Left };
            body.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(h * 0.3),
                BorderThickness = new Thickness(1.2),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF))
            });
            _fill = new Border { CornerRadius = new CornerRadius(h * 0.17), Margin = new Thickness(2.2), HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.White };
            body.Children.Add(_fill);
            _bolt = new TextBlock
            {
                Text = Glyph.Bolt,
                FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
                FontSize = h * 0.8,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.8, Color = Colors.Black }
            };
            body.Children.Add(_bolt);
            Children.Add(body);
            Children.Add(new Border
            {
                Width = 2, Height = h * 0.42, CornerRadius = new CornerRadius(0, 1, 1, 0),
                Background = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
            });
            _inner = w - 4.4;
        }

        public void Set(int level, bool charging, bool animate)
        {
            level = Math.Clamp(level, 0, 100);
            double target = Math.Max(1.5, _inner * level / 100.0);
            _fill.Background = new SolidColorBrush(charging ? Color.FromRgb(0x30, 0xD1, 0x58)
                : level <= 15 ? Color.FromRgb(0xFF, 0x45, 0x3A)
                : level <= 25 ? Color.FromRgb(0xFF, 0x9F, 0x0A) : Colors.White);
            _bolt.Visibility = charging ? Visibility.Visible : Visibility.Collapsed;
            if (animate)
            {
                var a = new DoubleAnimation(charging ? Math.Max(1.5, target * 0.35) : target, target, TimeSpan.FromMilliseconds(900))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, BeginTime = TimeSpan.FromMilliseconds(200) };
                _fill.BeginAnimation(WidthProperty, a);
            }
            else
            {
                _fill.BeginAnimation(WidthProperty, null);
                _fill.Width = target;
            }
        }
    }
}

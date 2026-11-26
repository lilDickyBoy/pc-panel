using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using PcPanel.Core;
using PcPanel.Services;
using PcPanel.Services.Screen;

namespace PcPanel.UI
{
    /// <summary>"Phone screen" page: phone as a second monitor, and controlling the phone from the PC.</summary>
    public partial class MainWindow
    {
        // live bits of the page, refreshed without rebuilding it
        TextBlock _ssStatus, _ssStats, _pmStatus;
        Ellipse _ssDot, _pmDot;
        Border _ssBar, _ssBarTrack, _ssBeam;
        Border _ssPhoneScreen, _ssMonScreen;
        DispatcherTimer _screenTimer;
        string _ssShape = "", _pmShape = "";
        bool _screenHooks;

        void HookScreenEvents()
        {
            if (_screenHooks) return;
            _screenHooks = true;
            SecondScreenService.I.Changed += OnScreenChanged;
            PhoneMirror.I.Changed += OnScreenChanged;
            ScreenTools.Progress += OnToolProgress;
            _screenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _screenTimer.Tick += (s, e) => { if (_page == "screen") RefreshScreenLive(); };
            _screenTimer.Start();
            Closed += (s, e) =>
            {
                SecondScreenService.I.Changed -= OnScreenChanged;
                PhoneMirror.I.Changed -= OnScreenChanged;
                ScreenTools.Progress -= OnToolProgress;
                _screenTimer?.Stop();
            };
        }

        void OnToolProgress(string what, double p, bool done, string err)
        {
            if (done) Dispatcher.BeginInvoke(new Action(() => { if (_page == "screen") BuildPage(false); }));
        }

        void OnScreenChanged() => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_page != "screen") return;
            // rebuild only when the "shape" of the page changes, otherwise just refresh the numbers
            var ss = SecondScreenService.I; var pm = PhoneMirror.I;
            if (ss.State + "|" + ss.ErrorKey != _ssShape || pm.State + "|" + pm.ErrorKey != _pmShape)
            {
                if (Keyboard.FocusedElement is TextBox tb && tb.IsDescendantOf(PageHost)) return;
                BuildPage(false);
            }
            else RefreshScreenLive();
        }));

        // ================================================================== page
        void BuildScreen()
        {
            HookScreenEvents();
            var st = App.Settings;
            var ss = SecondScreenService.I;
            var pm = PhoneMirror.I;
            _ssShape = ss.State + "|" + ss.ErrorKey;
            _pmShape = pm.State + "|" + pm.ErrorKey;
            var phone = App.Server?.Sessions?.FirstOrDefault();

            // ------------------------------------------------ second screen hero
            Add(Section(Loc.T("ss.section")));
            Add(SecondScreenHero(phone));

            Add(Section(Loc.T("ss.settings")));
            Add(Card(
                SwitchRow(Glyph.Phone, CBlue, Loc.T("ss.touch"), Loc.T("ss.touchSub"), st.SsTouch, "ssTouch"),
                Row(Glyph.Speed, CGreen, Loc.T("ss.quality"), Loc.T("ss.qualitySub." + st.SsQuality),
                    Segmented(new[] { Loc.T("ss.q.low"), Loc.T("ss.q.mid"), Loc.T("ss.q.high"), Loc.T("ss.q.ultra") },
                        Array.IndexOf(new[] { "low", "mid", "high", "ultra" }, st.SsQuality), i => SharedSettings.Set("ssQuality", new[] { "low", "mid", "high", "ultra" }[i]), 300)),
                Row(Glyph.Sync, COrange, Loc.T("ss.orient"), null,
                    Segmented(new[] { Loc.T("ss.landscape"), Loc.T("ss.portrait") }, st.SsOrient == "portrait" ? 1 : 0, i => SharedSettings.Set("ssOrient", i == 1 ? "portrait" : "landscape"), 240)),
                Row(Glyph.Link, CIndigo, Loc.T("ss.conn"), Loc.T("ss.connSub." + st.SsConn),
                    Segmented(new[] { Loc.T("ss.c.auto"), "USB", "Wi-Fi" }, Array.IndexOf(new[] { "auto", "usb", "wifi" }, st.SsConn), i => SharedSettings.Set("ssConn", new[] { "auto", "usb", "wifi" }[i]), 240)),
                Row(Glyph.Search, CTeal, Loc.T("ss.scale"), Loc.T("ss.scaleSub"),
                    Segmented(new[] { "100%", "125%", "150%", "175%", "200%" }, Math.Max(0, Array.IndexOf(new[] { 100, 125, 150, 175, 200 }, st.SsScale)),
                        i => SharedSettings.Set("ssScale", new[] { 100, 125, 150, 175, 200 }[i]), 300)),
                SwitchRow(Glyph.Video, CPink, Loc.T("ss.fps"), Loc.T("ss.fpsSub"), st.SsFps, "ssFps")
            ));
            Add(Hint(Loc.T("ss.hint")));

            // ------------------------------------------------ control the phone
            Add(Section(Loc.T("pm.section")));
            Add(MirrorHero(phone));

            Add(Section(Loc.T("pm.settings")));
            void Save() => App.Settings.Save();
            Add(Card(
                Row(Glyph.Speed, CGreen, Loc.T("ss.quality"), Loc.T("pm.qualitySub"),
                    Segmented(new[] { Loc.T("ss.q.mid"), Loc.T("ss.q.high"), Loc.T("ss.q.ultra") }, Array.IndexOf(new[] { "mid", "high", "ultra" }, st.PmQuality),
                        i => { App.Settings.PmQuality = new[] { "mid", "high", "ultra" }[i]; Save(); }, 240)),
                Row(Glyph.Volume3, COrange, Loc.T("pm.audio"), Loc.T("pm.audioSub"), Switch(st.PmAudio, v => { App.Settings.PmAudio = v; Save(); })),
                Row(Glyph.Moon, CIndigo, Loc.T("pm.screenOff"), Loc.T("pm.screenOffSub"), Switch(st.PmScreenOff, v => { App.Settings.PmScreenOff = v; Save(); })),
                Row(Glyph.Light, CYellow, Loc.T("pm.awake"), Loc.T("pm.awakeSub"), Switch(st.PmAwake, v => { App.Settings.PmAwake = v; Save(); })),
                Row(Glyph.Monitor, CBlue, Loc.T("pm.onTop"), null, Switch(st.PmOnTop, v => { App.Settings.PmOnTop = v; Save(); }))
            ));
            Add(Hint(Loc.T("pm.restartHint")));

            // ------------------------------------------------ setup
            Add(Section(Loc.T("scr.setup")));
            Add(Card(
                ToolRow("ffmpeg", Glyph.Video, CPurple, Loc.T("scr.ffmpeg"), Loc.T("scr.ffmpegSub"), ScreenTools.HasFfmpeg, () => ScreenTools.EnsureFfmpeg()),
                ToolRow("scrcpy", Glyph.Phone, CGreen, Loc.T("scr.scrcpy"), Loc.T("scr.scrcpySub"), ScreenTools.HasScrcpy, () => ScreenTools.EnsureScrcpy()),
                DriverRow()
            ));

            Add(Section(Loc.T("scr.usb")));
            var steps = new StackPanel { Margin = new Thickness(2, 12, 2, 12) };
            string[] keys = { "scr.usb1", "scr.usb2", "scr.usb3", "scr.usb4", "scr.usb5" };
            for (int i = 0; i < keys.Length; i++)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, i == 0 ? 0 : 12, 0, 0) };
                row.Children.Add(new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = B(CGreen), VerticalAlignment = VerticalAlignment.Top, Child = new TextBlock { Text = (i + 1).ToString(), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
                row.Children.Add(new TextBlock { Text = Loc.T(keys[i]), Foreground = B(Color.FromRgb(0xE5, 0xE5, 0xEA)), FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxWidth = 560, Margin = new Thickness(12, 2, 0, 0), LineHeight = 20 });
                steps.Children.Add(row);
            }
            Add(Card(steps));
            RefreshScreenLive();
        }

        static readonly Color CYellow = Color.FromRgb(0xFF, 0xD6, 0x0A);

        // ------------------------------------------------------------------ second screen card
        UIElement SecondScreenHero(Session phone)
        {
            var ss = SecondScreenService.I;
            var g = new Grid { Margin = new Thickness(4, 18, 4, 18) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());

            g.Children.Add(Illustration(ss.State));

            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(26, 0, 0, 0) };
            right.Children.Add(new TextBlock { Text = Loc.T("ss.title"), Foreground = Brushes.White, FontSize = 21, FontWeight = FontWeights.Bold });
            right.Children.Add(new TextBlock { Text = Loc.T("ss.sub"), Foreground = Text2, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), LineHeight = 19 });

            var status = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            _ssDot = new Ellipse { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center };
            _ssStatus = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
            status.Children.Add(_ssDot);
            status.Children.Add(_ssStatus);
            right.Children.Add(status);
            _ssStats = new TextBlock { Foreground = Text2, FontSize = 12.5, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(17, 4, 0, 0), Visibility = Visibility.Collapsed };
            right.Children.Add(_ssStats);

            _ssBar = new Border { CornerRadius = new CornerRadius(3), Background = B(CBlue), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            _ssBarTrack = new Border { Width = 260, Height = 6, CornerRadius = new CornerRadius(3), Background = SepBg, Margin = new Thickness(17, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Child = _ssBar, Visibility = Visibility.Collapsed };
            right.Children.Add(_ssBarTrack);

            Button main;
            if (ss.Busy)
                main = Btn(Loc.T("ss.stop"), () => _ = SecondScreenService.I.Stop(), Color.FromArgb(0x33, 0xFF, 0x45, 0x3A), CRed, Glyph.Close);
            else if (phone == null)
            {
                main = Btn(Loc.T("ss.start"), () => { }, Color.FromRgb(0x2C, 0x2C, 0x2E), CGray, Glyph.Play);
                main.IsEnabled = false;
            }
            else
                main = Btn(Loc.T("ss.start"), () => SecondScreenService.I.AskPhone(App.Server?.Sessions?.FirstOrDefault()), CBlue, null, Glyph.Play);
            main.HorizontalAlignment = HorizontalAlignment.Left;
            main.Margin = new Thickness(0, 16, 0, 0);
            main.Padding = new Thickness(22, 11, 22, 11);
            right.Children.Add(main);
            if (phone == null && !ss.Busy) right.Children.Add(Hint(Loc.T("ss.needPhone"), 8));

            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            return Card(g);
        }

        /// <summary>A little PC monitor and phone; the "beam" between them flows while connecting and both screens light up when live.</summary>
        UIElement Illustration(SecondScreenService.St state)
        {
            bool live = state == SecondScreenService.St.Live;
            bool busy = state == SecondScreenService.St.Preparing || state == SecondScreenService.St.Waiting;
            var canvas = new Canvas { Width = 250, Height = 128 };

            Brush ScreenBrush() => live
                ? new LinearGradientBrush(Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0xBF, 0x5A, 0xF2), 35)
                : (Brush)B(Color.FromRgb(0x16, 0x16, 0x18));

            // monitor
            var mon = new Border { Width = 132, Height = 86, CornerRadius = new CornerRadius(10), Background = B(Color.FromRgb(0x2C, 0x2C, 0x2E)), Padding = new Thickness(5) };
            _ssMonScreen = new Border { CornerRadius = new CornerRadius(6), Background = ScreenBrush() };
            mon.Child = _ssMonScreen;
            Canvas.SetLeft(mon, 0); Canvas.SetTop(mon, 8);
            canvas.Children.Add(mon);
            var neck = new Border { Width = 18, Height = 16, Background = B(Color.FromRgb(0x3A, 0x3A, 0x3C)) };
            Canvas.SetLeft(neck, 57); Canvas.SetTop(neck, 94);
            canvas.Children.Add(neck);
            var foot = new Border { Width = 60, Height = 6, CornerRadius = new CornerRadius(3), Background = B(Color.FromRgb(0x3A, 0x3A, 0x3C)) };
            Canvas.SetLeft(foot, 36); Canvas.SetTop(foot, 110);
            canvas.Children.Add(foot);

            // beam
            _ssBeam = new Border { Width = 30, Height = 4, CornerRadius = new CornerRadius(2), Background = live ? B(CGreen) : busy ? B(CBlue) : SepBg, Opacity = live || busy ? 1 : 0.6 };
            Canvas.SetLeft(_ssBeam, 138); Canvas.SetTop(_ssBeam, 70);
            canvas.Children.Add(_ssBeam);
            if (busy) _ssBeam.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(650)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });

            // phone (landscape)
            bool portrait = App.Settings.SsOrient == "portrait";
            double pw = portrait ? 38 : 72, ph = portrait ? 74 : 38;
            var phoneB = new Border { Width = pw, Height = ph, CornerRadius = new CornerRadius(8), Background = B(Color.FromRgb(0x2C, 0x2C, 0x2E)), Padding = new Thickness(3) };
            _ssPhoneScreen = new Border { CornerRadius = new CornerRadius(5), Background = ScreenBrush() };
            phoneB.Child = _ssPhoneScreen;
            Canvas.SetLeft(phoneB, 172); Canvas.SetTop(phoneB, 72 - ph / 2);
            canvas.Children.Add(phoneB);
            if (live)
            {
                phoneB.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0x0A, 0x84, 0xFF), BlurRadius = 22, ShadowDepth = 0, Opacity = 0.8 };
                var pulse = new DoubleAnimation(0.45, 0.95, TimeSpan.FromMilliseconds(1300)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                phoneB.Effect.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, pulse);
            }
            // pop-in
            var sc = new ScaleTransform(0.9, 0.9);
            canvas.RenderTransformOrigin = new Point(0.5, 0.5);
            canvas.RenderTransform = sc;
            var pop = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(650)) { EasingFunction = new SpringEase { Damping = 0.55, Stiffness = 12 } };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            return canvas;
        }

        // ------------------------------------------------------------------ phone mirror card
        UIElement MirrorHero(Session phone)
        {
            var pm = PhoneMirror.I;
            var g = new Grid { Margin = new Thickness(4, 16, 4, 16) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());

            // phone illustration with a cursor
            var art = new Grid { Width = 96, Height = 128 };
            var body = new Border { Width = 64, Height = 122, CornerRadius = new CornerRadius(13), Background = B(Color.FromRgb(0x2C, 0x2C, 0x2E)), Padding = new Thickness(4) };
            body.Child = new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = pm.State == PhoneMirror.St.Running
                    ? new LinearGradientBrush(Color.FromRgb(0x30, 0xD1, 0x58), Color.FromRgb(0x0A, 0x84, 0xFF), 60)
                    : (Brush)B(Color.FromRgb(0x16, 0x16, 0x18))
            };
            art.Children.Add(body);
            var cursor = new TextBlock { Text = ((char)0xE7C9).ToString(), FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 26, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 2, 8), RenderTransform = new TranslateTransform() };
            art.Children.Add(cursor);
            var tt = (TranslateTransform)cursor.RenderTransform;
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -14, TimeSpan.FromMilliseconds(1600)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -22, TimeSpan.FromMilliseconds(2300)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            g.Children.Add(art);

            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(26, 0, 0, 0) };
            right.Children.Add(new TextBlock { Text = Loc.T("pm.heroTitle"), Foreground = Brushes.White, FontSize = 21, FontWeight = FontWeights.Bold });
            right.Children.Add(new TextBlock { Text = Loc.T("pm.heroSub"), Foreground = Text2, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), LineHeight = 19 });
            var status = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            _pmDot = new Ellipse { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center };
            _pmStatus = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
            status.Children.Add(_pmDot);
            status.Children.Add(_pmStatus);
            right.Children.Add(status);

            var open = Btn(pm.State == PhoneMirror.St.Running ? Loc.T("pm.show") : Loc.T("pm.open"), MirrorWindow.ShowSingle, CGreen, null, Glyph.Phone);
            open.Padding = new Thickness(22, 11, 22, 11);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
            buttons.Children.Add(open);
            if (pm.State == PhoneMirror.St.Running && !pm.Wireless && phone != null)
            {
                var wl = Btn(Loc.T("pm.wireless"), async () =>
                {
                    App.Notch?.ShowInfo(Glyph.Sync, Loc.T("pm.wireless.working"), 3);
                    bool ok = await PhoneMirror.I.GoWireless(App.Server?.Sessions?.FirstOrDefault()?.Ip);
                    App.Notch?.ShowInfo(ok ? Glyph.Check : Glyph.Warning, Loc.T(ok ? "pm.wireless.ok" : "pm.wireless.failed"), 4);
                }, null, null, Glyph.Globe);
                wl.Margin = new Thickness(10, 0, 0, 0);
                buttons.Children.Add(wl);
            }
            right.Children.Add(buttons);
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            return Card(g);
        }

        // ------------------------------------------------------------------ setup rows
        UIElement ToolRow(string id, string glyph, Color color, string title, string sub, bool installed, Func<Task<bool>> install)
        {
            UIElement right;
            if (installed) right = Pill(Loc.T("scr.ready"), CGreen);
            else if (ScreenTools.Busy == id) right = Pill(Loc.T("scr.downloading"), CBlue);
            else right = Btn(Loc.T("scr.download"), () => { _ = install(); BuildPage(false); }, CBlue, null, Glyph.Download);
            return Row(glyph, color, title, sub, right);
        }

        bool _driverBusy;
        UIElement DriverRow()
        {
            UIElement right;
            if (_driverBusy) right = Pill(Loc.T("scr.installing"), CBlue);
            else if (VirtualDisplay.Installed == true) right = Pill(Loc.T("scr.ready"), CGreen);
            else if (VirtualDisplay.Installed == null)
            {
                right = Pill(Loc.T("scr.checking"), CGray);
                _ = Task.Run(async () => { await VirtualDisplay.CheckInstalled(); Dispatcher.BeginInvoke(new Action(() => { if (_page == "screen") BuildPage(false); })); });
            }
            else right = Btn(Loc.T("scr.install"), async () =>
            {
                _driverBusy = true; BuildPage(false);
                bool ok = await VirtualDisplay.Install();
                _driverBusy = false;
                if (!ok) SystemSetup.ShellOpen("https://github.com/VirtualDrivers/Virtual-Display-Driver/releases");
                if (_page == "screen") BuildPage(false);
            }, CBlue, null, Glyph.Download);
            return Row(Glyph.Monitor, CBlue, Loc.T("scr.driver"), Loc.T("scr.driverSub"), right);
        }

        Border Pill(string text, Color c) => new Border
        {
            CornerRadius = new CornerRadius(10), Background = B(Color.FromArgb(0x2E, c.R, c.G, c.B)), Padding = new Thickness(10, 4, 10, 5),
            Child = new TextBlock { Text = text, Foreground = B(c), FontSize = 12.5, FontWeight = FontWeights.Bold }
        };

        // ------------------------------------------------------------------ live refresh
        void RefreshScreenLive()
        {
            var ss = SecondScreenService.I;
            if (_ssStatus != null)
            {
                (string text, Color c) = ss.State switch
                {
                    SecondScreenService.St.Live => (Loc.F("ss.live", ss.PhoneName, ss.Transport == "usb" ? "USB" : "Wi-Fi"), CGreen),
                    SecondScreenService.St.Waiting => (Loc.T("ss.step.waiting"), CBlue),
                    SecondScreenService.St.Preparing => (Loc.T(ss.StepKey), CBlue),
                    SecondScreenService.St.Error => (Loc.T(ss.ErrorKey), CRed),
                    _ => (Loc.T("ss.off"), CGray)
                };
                _ssStatus.Text = text;
                _ssStatus.Foreground = ss.State == SecondScreenService.St.Off ? Text2 : B(c);
                _ssDot.Fill = B(c);
                bool live = ss.State == SecondScreenService.St.Live;
                _ssStats.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
                if (live)
                    _ssStats.Text = $"{ss.Fps} FPS  ·  {(ss.LatencyMs >= 0 ? ss.LatencyMs + " ms" : "– ms")}  ·  {ss.Mbps:0.#} Mbps  ·  {ss.Width}×{ss.Height}  ·  {ss.Encoder}";
                bool bar = ss.State == SecondScreenService.St.Preparing && ss.StepProgress >= 0;
                _ssBarTrack.Visibility = bar ? Visibility.Visible : Visibility.Collapsed;
                if (bar) _ssBar.Width = 260 * Math.Clamp(ss.StepProgress, 0, 1);
            }
            var pm = PhoneMirror.I;
            if (_pmStatus != null)
            {
                (string text, Color c) = pm.State switch
                {
                    PhoneMirror.St.Running => (Loc.F("pm.running", string.IsNullOrEmpty(pm.Model) ? "Phone" : pm.Model, pm.Fps, pm.Wireless ? "Wi-Fi" : "USB"), CGreen),
                    PhoneMirror.St.Preparing => (Loc.T(pm.StepKey), CBlue),
                    PhoneMirror.St.Error => (Loc.T(pm.ErrorKey + ".title"), CRed),
                    _ => (Loc.T("pm.idle"), CGray)
                };
                _pmStatus.Text = text;
                _pmStatus.Foreground = pm.State == PhoneMirror.St.Off ? Text2 : B(c);
                _pmDot.Fill = B(c);
            }
        }
    }
}

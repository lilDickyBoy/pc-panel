using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using PcPanel.Core;
using PcPanel.Services;
using QRCoder;

namespace PcPanel.UI
{
    public partial class MainWindow : Window
    {
        static readonly Color CBlue = Color.FromRgb(0x0A, 0x84, 0xFF), CGreen = Color.FromRgb(0x30, 0xD1, 0x58),
            CRed = Color.FromRgb(0xFF, 0x45, 0x3A), COrange = Color.FromRgb(0xFF, 0x9F, 0x0A), CIndigo = Color.FromRgb(0x5E, 0x5C, 0xE6),
            CPurple = Color.FromRgb(0xBF, 0x5A, 0xF2), CPink = Color.FromRgb(0xFF, 0x37, 0x5F), CTeal = Color.FromRgb(0x40, 0xC8, 0xE0),
            CGray = Color.FromRgb(0x8E, 0x8E, 0x93), CSpotify = Color.FromRgb(0x1D, 0xB9, 0x54), CYouTube = Color.FromRgb(0xFF, 0x00, 0x33);

        static Brush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        static readonly Brush Text2 = B(CGray), CardBg = B(Color.FromRgb(0x1C, 0x1C, 0x1E)), SepBg = B(Color.FromRgb(0x2C, 0x2C, 0x2E));

        readonly (string id, string glyph, string key)[] _pages =
        {
            ("home", Glyph.Home, "pc.nav.home"),
            ("notch", Glyph.Monitor, "pc.nav.notch"),
            ("music", Glyph.Music, "pc.nav.music"),
            ("features", Glyph.Apps, "pc.nav.features"),
            ("settings", Glyph.Settings, "pc.nav.settings"),
        };

        string _page = "home";
        bool _closing;
        readonly DispatcherTimer _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        Image _qrImage;
        TextBlock _addrText, _transferText;

        public MainWindow()
        {
            InitializeComponent();
            MinBtn.Content = Glyph.Minimize;
            CloseBtn.Content = Glyph.ChromeClose;
            QuitGlyph.Text = Glyph.Quit;
            DropGlyph.Text = Glyph.Send;

            BuildNav();
            ApplyTexts();

            SharedSettings.Changed += OnSharedChanged;
            PhoneService.BatteryChanged += OnBattery;
            FileService.SendProgress += OnSendProgress;
            _clock.Tick += (s, e) => { if (_page == "home") RefreshDevices(); };
            _clock.Start();

            DragEnter += OnDragEnter;
            DragOver += OnDragOver;
            DragLeave += (s, e) => DropOverlay.Visibility = Visibility.Collapsed;
            Drop += OnDrop;

            Loaded += (s, e) => AnimateIn();
            Closed += (s, e) =>
            {
                _clock.Stop();
                SharedSettings.Changed -= OnSharedChanged;
                PhoneService.BatteryChanged -= OnBattery;
                FileService.SendProgress -= OnSendProgress;
            };
        }

        // ================================================================== window animation
        ScaleTransform RootScale => (ScaleTransform)((TransformGroup)RootFrame.RenderTransform).Children[0];
        TranslateTransform RootMove => (TranslateTransform)((TransformGroup)RootFrame.RenderTransform).Children[1];

        void AnimateIn()
        {
            var spring = new SpringEase { Damping = 0.72, Stiffness = 11 };
            var a = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(620)) { EasingFunction = spring };
            RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            RootMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(620)) { EasingFunction = spring });
            RootFrame.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)));
        }

        void AnimateOutThen(Action after)
        {
            if (_closing) return;
            _closing = true;
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var a = new DoubleAnimation(0.9, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease };
            RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            RootMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(28, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            var f = new DoubleAnimation(0, TimeSpan.FromMilliseconds(240)) { BeginTime = TimeSpan.FromMilliseconds(40) };
            f.Completed += (s, e) => after();
            RootFrame.BeginAnimation(OpacityProperty, f);
        }

        // ================================================================== navigation
        void BuildNav()
        {
            NavList.Children.Clear();
            foreach (var p in _pages)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var g = new TextBlock { Text = p.glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 18, Width = 26, VerticalAlignment = VerticalAlignment.Center, Tag = "glyph" };
                var t = new TextBlock { FontSize = 15.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Tag = "text" };
                row.Children.Add(g); row.Children.Add(t);
                var b = new Button { Style = (Style)FindResource("NavButton"), Content = row, Tag = p.id, Margin = new Thickness(0, 0, 0, 4) };
                b.Click += (s, e) => GoTo(p.id);
                NavList.Children.Add(b);
            }
        }

        void UpdateNav(bool animate)
        {
            int idx = Array.FindIndex(_pages, x => x.id == _page);
            for (int i = 0; i < NavList.Children.Count; i++)
            {
                var b = (Button)NavList.Children[i];
                var row = (StackPanel)b.Content;
                bool sel = i == idx;
                ((TextBlock)row.Children[0]).Foreground = sel ? B(CBlue) : B(Color.FromRgb(0xC7, 0xC7, 0xCC));
                var t = (TextBlock)row.Children[1];
                t.Text = Loc.T(_pages[i].key);
                t.Foreground = sel ? Brushes.White : B(Color.FromRgb(0xC7, 0xC7, 0xCC));
            }
            var tt = (TranslateTransform)NavHighlight.RenderTransform;
            double y = idx * 52;
            if (!animate) { tt.BeginAnimation(TranslateTransform.YProperty, null); tt.Y = y; return; }
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(460)) { EasingFunction = new SpringEase { Damping = 0.74, Stiffness = 13 } });
        }

        public void GoTo(string page)
        {
            if (!_pages.Any(p => p.id == page)) page = "home";
            bool changed = page != _page;
            _page = page;
            UpdateNav(true);
            BuildPage(changed);
            if (changed) Scroller.ScrollToTop();
        }

        public void ApplyTexts()
        {
            StatusDotUpdate();
            QuitText.Text = Loc.T("pc.btn.quit");
            VersionText.Text = "PC Panel " + App.Version;
            DropText.Text = Loc.T("pc.drop.hint");
            UpdateNav(false);
            BuildPage(false);
        }

        void OnSharedChanged()
        {
            // don't rebuild while the user is typing in a text box on this page
            if (Keyboard.FocusedElement is TextBox tb && tb.IsDescendantOf(PageHost)) return;
            BuildPage(false);
            StatusDotUpdate();
        }

        void OnBattery() { if (_page == "home") BuildPage(false); }

        void OnSendProgress(string name, double frac, bool done)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_transferText == null) return;
                _transferText.Visibility = Visibility.Visible;
                _transferText.Text = done ? (frac >= 1 ? Loc.F("pc.file.sent", name) : Loc.F("pc.file.failed", name)) : Loc.F("pc.file.sending", name, (int)(frac * 100));
            }));
        }

        void BuildPage(bool animate)
        {
            PageTitle.Text = Loc.T(_pages.First(p => p.id == _page).key);
            PageHost.Children.Clear();
            _qrImage = null; _addrText = null; _transferText = null;
            switch (_page)
            {
                case "home": BuildHome(); break;
                case "notch": BuildNotch(); break;
                case "music": BuildMusic(); break;
                case "features": BuildFeatures(); break;
                default: BuildSettings(); break;
            }
            if (animate)
            {
                var tt = (TranslateTransform)PageHost.RenderTransform;
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = new SpringEase { Damping = 0.8, Stiffness = 12 } });
                PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            }
        }

        // ================================================================== UI kit
        UIElement Section(string text) => new TextBlock
        {
            Text = text.ToUpperInvariant(), Foreground = Text2, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(6, 22, 0, 9)
        };

        Border Card(params UIElement[] children)
        {
            var sp = new StackPanel();
            bool first = true;
            foreach (var c in children)
            {
                if (c == null) continue;
                if (!first && !(c is Border bd && Equals(bd.Tag, "nosep"))) sp.Children.Add(new Border { Height = 1, Background = SepBg, Margin = new Thickness(52, 0, 0, 0) });
                sp.Children.Add(c);
                first = false;
            }
            return new Border { Background = CardBg, CornerRadius = new CornerRadius(18), Padding = new Thickness(18, 6, 18, 6), Child = sp };
        }

        Border IconTile(string glyph, Color c, double size = 34)
        {
            return new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.26), Background = B(c),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = size * 0.5, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
        }

        Grid Row(string glyph, Color color, string title, string sub, UIElement right)
        {
            var g = new Grid { Margin = new Thickness(0, 11, 0, 11), MinHeight = 36 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (glyph != null) g.Children.Add(IconTile(glyph, color));
            var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(glyph != null ? 16 : 0, 0, 14, 0) };
            txt.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 15.5, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrEmpty(sub)) txt.Children.Add(new TextBlock { Text = sub, Foreground = Text2, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(txt, 1);
            g.Children.Add(txt);
            if (right != null)
            {
                if (right is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(right, 2);
                g.Children.Add(right);
            }
            return g;
        }

        CheckBox Switch(bool value, Action<bool> onChange)
        {
            var c = new CheckBox { Style = (Style)FindResource("IosSwitch"), IsChecked = value };
            c.Checked += (s, e) => onChange(true);
            c.Unchecked += (s, e) => onChange(false);
            return c;
        }

        Grid SwitchRow(string glyph, Color color, string title, string sub, bool value, string sharedKey)
            => Row(glyph, color, title, sub, Switch(value, v => SharedSettings.Set(sharedKey, v)));

        Button Btn(string text, Action click, Color? bg = null, Color? fg = null, string glyph = null)
        {
            object content = text;
            if (glyph != null)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
                content = sp;
            }
            var b = new Button { Style = (Style)FindResource("PillButton"), Content = content };
            if (bg.HasValue) b.Background = B(bg.Value);
            if (fg.HasValue) b.Foreground = B(fg.Value);
            b.Click += (s, e) => click();
            return b;
        }

        Border Segmented(string[] options, int selected, Action<int> onSelect, double width = 300)
        {
            var outer = new Border { Width = width, Height = 36, CornerRadius = new CornerRadius(10), Background = SepBg, Padding = new Thickness(2) };
            var grid = new Grid();
            double segW = (width - 4) / options.Length;
            var thumb = new Border { Width = segW, CornerRadius = new CornerRadius(8), Background = B(Color.FromRgb(0x63, 0x63, 0x66)), HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = new TranslateTransform(segW * selected, 0) };
            grid.Children.Add(thumb);
            var labels = new Grid();
            for (int i = 0; i < options.Length; i++)
            {
                labels.ColumnDefinitions.Add(new ColumnDefinition());
                var t = new TextBlock { Text = options[i], Foreground = Brushes.White, FontSize = 13.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(t, i);
                labels.Children.Add(t);
                int idx = i;
                var hit = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand };
                hit.MouseLeftButtonUp += (s, e) =>
                {
                    ((TranslateTransform)thumb.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
                        new DoubleAnimation(segW * idx, TimeSpan.FromMilliseconds(380)) { EasingFunction = new SpringEase { Damping = 0.72, Stiffness = 12 } });
                    onSelect(idx);
                };
                Grid.SetColumn(hit, i);
                labels.Children.Add(hit);
            }
            grid.Children.Add(labels);
            outer.Child = grid;
            return outer;
        }

        TextBox Input(string text, double width = 300, string placeholder = null)
        {
            var tb = new TextBox { Style = (Style)FindResource("DarkTextBox"), Text = text ?? "", Width = width };
            if (!string.IsNullOrEmpty(placeholder)) tb.ToolTip = placeholder;
            return tb;
        }

        TextBlock Hint(string text, double top = 8) => new TextBlock { Text = text, Foreground = Text2, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 4), LineHeight = 20 };

        StackPanel HStack(params UIElement[] items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var i in items)
            {
                if (i is FrameworkElement fe && sp.Children.Count > 0) fe.Margin = new Thickness(10, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
                sp.Children.Add(i);
            }
            return sp;
        }

        void Add(UIElement e) => PageHost.Children.Add(e);

        // ================================================================== HOME
        void BuildHome()
        {
            var sessions = App.Server?.Sessions ?? new Session[0];

            if (sessions.Length > 0)
            {
                Add(Section(Loc.T("pc.devices.title")));
                foreach (var s in sessions) Add(PhoneCard(s));
                _transferText = new TextBlock { Foreground = B(CBlue), FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 10, 0, 0), Visibility = Visibility.Collapsed };
                Add(_transferText);
            }

            Add(Section(sessions.Length > 0 ? Loc.T("pc.pair.another") : Loc.T("pc.pair.title")));
            var qrFrame = new Border { Width = 236, Height = 236, CornerRadius = new CornerRadius(20), Background = Brushes.White, Padding = new Thickness(10), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
            _qrImage = new Image();
            RenderOptions.SetBitmapScalingMode(_qrImage, BitmapScalingMode.NearestNeighbor);
            qrFrame.Child = _qrImage;
            _addrText = new TextBlock { Foreground = Text2, FontSize = 13, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 14, 0, 0) };

            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28, 0, 0, 0) };
            right.Children.Add(new TextBlock { Text = Loc.T("pc.pair.howTitle"), Foreground = Brushes.White, FontSize = 19, FontWeight = FontWeights.Bold });
            string[] steps = { Loc.T("pc.pair.step1"), Loc.T("pc.pair.step2"), Loc.T("pc.pair.step3") };
            for (int i = 0; i < steps.Length; i++)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
                row.Children.Add(new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = B(CBlue), Child = new TextBlock { Text = (i + 1).ToString(), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
                row.Children.Add(new TextBlock { Text = steps[i], Foreground = B(Color.FromRgb(0xE5, 0xE5, 0xEA)), FontSize = 14.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 330, Margin = new Thickness(12, 3, 0, 0) });
                right.Children.Add(row);
            }
            right.Children.Add(_addrText);
            var reset = Btn(Loc.T("pc.pair.reset"), ResetPairing, Color.FromArgb(0, 0, 0, 0), CBlue, Glyph.Refresh);
            reset.HorizontalAlignment = HorizontalAlignment.Left;
            reset.Padding = new Thickness(0, 10, 10, 10);
            reset.Margin = new Thickness(0, 8, 0, 0);
            right.Children.Add(reset);

            var pairGrid = new Grid { Margin = new Thickness(4, 14, 4, 14) };
            pairGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pairGrid.ColumnDefinitions.Add(new ColumnDefinition());
            pairGrid.Children.Add(qrFrame);
            Grid.SetColumn(right, 1);
            pairGrid.Children.Add(right);
            Add(Card(pairGrid));
            RefreshQr();
            var qs = (ScaleTransform)qrFrame.RenderTransform;
            var pop = new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(700)) { EasingFunction = new SpringEase { Damping = 0.5, Stiffness = 12 }, BeginTime = TimeSpan.FromMilliseconds(120) };
            qs.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            qs.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }

        UIElement PhoneCard(Session s)
        {
            var g = new Grid { Margin = new Thickness(2, 14, 2, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(IconTile(Glyph.Phone, CBlue, 54));

            var info = new StackPanel { Margin = new Thickness(16, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = s.DeviceName, Foreground = Brushes.White, FontSize = 19, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis });
            var mins = (int)(DateTime.Now - s.ConnectedAt).TotalMinutes;
            info.Children.Add(new TextBlock { Text = s.Ip + "   ·   " + Loc.F("pc.devices.since", mins < 1 ? "<1" : mins.ToString()), Foreground = Text2, FontSize = 13, Margin = new Thickness(0, 3, 0, 0) });
            Grid.SetColumn(info, 1);
            g.Children.Add(info);

            if (s.Battery >= 0)
            {
                var bat = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var glyph = new BatteryGlyph(30, 15) { VerticalAlignment = VerticalAlignment.Center };
                glyph.Set(s.Battery, s.Charging, false);
                bat.Children.Add(new TextBlock
                {
                    Text = Loc.F("pc.percent", s.Battery), FontSize = 20, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
                    Foreground = s.Charging ? B(CGreen) : s.Battery <= 15 ? B(CRed) : Brushes.White
                });
                bat.Children.Add(glyph);
                var batCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                batCol.Children.Add(bat);
                batCol.Children.Add(new TextBlock { Text = s.Charging ? Loc.T("pc.phone.chargingShort") : Loc.T("pc.phone.battery"), Foreground = s.Charging ? B(CGreen) : Text2, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 3, 0, 0) });
                Grid.SetColumn(batCol, 2);
                g.Children.Add(batCol);
            }

            var actions = new WrapPanel { Margin = new Thickness(2, 6, 0, 12) };
            void A(Button b) { b.Margin = new Thickness(0, 6, 10, 0); actions.Children.Add(b); }
            A(Btn(Loc.T("pc.findPhone"), () => { _ = s.SendAsync(new { type = "ring" }); App.Notch?.ShowInfo(Glyph.Bell, Loc.T("pc.findPhone.ringing"), 3); }, CBlue, null, Glyph.Bell));
            A(Btn(Loc.T("pc.file.send"), PickAndSendFiles, null, null, Glyph.Send));
            A(Btn(Loc.T("pc.clip.send"), () =>
            {
                if (ClipboardService.SendNow()) App.Notch?.ShowInfo(Glyph.Clipboard, Loc.T("pc.clip.sent"), 2);
            }, null, null, Glyph.Copy));
            A(Btn(Loc.T("pc.devices.disconnect"), () => s.Kick("pc_disconnect"), Color.FromArgb(0x33, 0xFF, 0x45, 0x3A), CRed, Glyph.Unlink));

            var drop = new Border
            {
                Tag = "nosep",
                BorderBrush = B(Color.FromRgb(0x3A, 0x3A, 0x3C)), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 4, 0, 14),
                Child = HStack(
                    new TextBlock { Text = Glyph.Download, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 20, Foreground = Text2, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = Loc.T("pc.drop.card"), Foreground = Text2, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap })
            };
            return Card(g, actions, drop);
        }

        public void RefreshDevices()
        {
            StatusDotUpdate();
            if (_page == "home") BuildPage(false);
        }

        void StatusDotUpdate()
        {
            bool any = (App.Server?.Sessions.Length ?? 0) > 0;
            StatusText.Text = any ? Loc.T("pc.status.connected") : Loc.T("pc.status.waiting");
            StatusDot.Fill = any ? B(CGreen) : B(CGray);
            var pulse = new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(pulse, 30);
            StatusDot.BeginAnimation(OpacityProperty, any ? null : pulse);
        }

        public void RefreshQr()
        {
            if (_qrImage == null) return;
            var net = App.Net;
            if (string.IsNullOrEmpty(net.Ip))
            {
                _addrText.Text = Loc.T("pc.noNetwork");
                _qrImage.Source = null;
                return;
            }
            var st = App.Settings;
            string payload = "mobilink://pair?h=" + net.Ip + "&p=" + Server.Port + "&k=" + st.Token + "&i=" + st.PcId +
                             "&n=" + Uri.EscapeDataString(st.PcName) + "&m=" + net.Mac + "&b=" + net.Broadcast;
            using var gen = new QRCodeGenerator();
            using var data = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(data).GetGraphic(10);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = new MemoryStream(png);
            bi.EndInit();
            bi.Freeze();
            _qrImage.Source = bi;
            _addrText.Text = net.Ip + " : " + Server.Port + "   ·   " + net.AdapterName;
        }

        void ResetPairing()
        {
            var r = MessageBox.Show(this, Loc.T("pc.pair.resetConfirm"), "PC Panel", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (r != MessageBoxResult.OK) return;
            App.Server.KickAll("unpaired");
            App.Settings.Token = AppSettings.NewToken();
            App.Settings.Save();
            BuildPage(false);
        }

        // ================================================================== files
        public void PickAndSendFiles()
        {
            var sessions = App.Server?.Sessions ?? new Session[0];
            if (sessions.Length == 0) { MessageBox.Show(this, Loc.T("pc.devices.none"), "PC Panel"); return; }
            var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = Loc.T("pc.file.send") };
            if (dlg.ShowDialog(this) != true) return;
            SendFiles(dlg.FileNames);
        }

        void SendFiles(IEnumerable<string> paths)
        {
            var s = App.Server?.Sessions.FirstOrDefault();
            if (s == null) return;
            var list = paths.Where(File.Exists).ToList();
            if (list.Count == 0) return;
            Task.Run(async () =>
            {
                foreach (var p in list) await FileService.SendAsync(s, p);
            });
        }

        void OnDragEnter(object sender, DragEventArgs e)
        {
            bool ok = e.Data.GetDataPresent(DataFormats.FileDrop) && (App.Server?.AnyConnected ?? false);
            e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            DropOverlay.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
            e.Handled = true;
        }

        void OnDragOver(object sender, DragEventArgs e)
        {
            bool ok = e.Data.GetDataPresent(DataFormats.FileDrop) && (App.Server?.AnyConnected ?? false);
            e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        void OnDrop(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Collapsed;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null) SendFiles(files);
        }

        // ================================================================== NOTCH
        void BuildNotch()
        {
            var st = App.Settings;
            Add(Section(Loc.T("pc.notch.style")));
            var styles = new UniformGrid { Columns = 2 };
            styles.Children.Add(StyleCard("notch", Loc.T("pc.notch.styleNotch"), Loc.T("pc.notch.styleNotchSub"), st.NotchStyle == "notch"));
            styles.Children.Add(StyleCard("island", Loc.T("pc.notch.styleIsland"), Loc.T("pc.notch.styleIslandSub"), st.NotchStyle == "island"));
            Add(styles);

            Add(Section(Loc.T("pc.notch.behavior")));
            Add(Card(
                SwitchRow(Glyph.Monitor, CBlue, Loc.T("pc.notch.enabled"), Loc.T("pc.notch.enabledSub"), st.NotchEnabled, "notchOn"),
                Row(Glyph.Timer, CIndigo, Loc.T("pc.notch.visibility"), st.NotchMode == "always" ? Loc.T("pc.notch.alwaysSub") : Loc.T("pc.notch.autoSub"),
                    Segmented(new[] { Loc.T("pc.notch.auto"), Loc.T("pc.notch.always") }, st.NotchMode == "always" ? 1 : 0, i => SharedSettings.Set("notchMode", i == 1 ? "always" : "auto"), 280)),
                SwitchRow(Glyph.Game, CPurple, Loc.T("pc.notch.fullscreen"), Loc.T("pc.notch.fullscreenSub"), st.NotchHideFullscreen, "notchFs")
            ));

            Add(Section(Loc.T("pc.notch.shows")));
            Add(Card(
                SwitchRow(Glyph.Volume, COrange, Loc.T("pc.notch.volume"), null, st.NotchVolume, "notchVol"),
                SwitchRow(Glyph.Phone, CBlue, Loc.T("pc.notch.connections"), null, st.NotchConnectInfo, "notchConn"),
                SwitchRow(Glyph.Bolt, CGreen, Loc.T("pc.notch.charging"), Loc.T("pc.notch.chargingSub"), st.NotchCharging, "notchCharge"),
                SwitchRow(Glyph.Bell, CRed, Loc.T("pc.notch.notifs"), Loc.T("pc.notch.notifsSub"), st.NotchNotifications, "notchNotif"),
                SwitchRow(Glyph.Call, CGreen, Loc.T("pc.notch.calls"), Loc.T("pc.notch.callsSub"), st.NotchCalls, "notchCall"),
                SwitchRow(Glyph.Upload, CIndigo, Loc.T("pc.notch.drop"), Loc.T("pc.notch.dropSub"), st.NotchDrop, "notchDrop")
            ));

            var test = Btn(Loc.T("pc.notch.test"), () =>
            {
                App.Notch?.ShowBattery(80, true, Loc.F("pc.phone.charging", 80));
                SoundService.Play("charge_in");
            }, CBlue, null, Glyph.Play);
            test.HorizontalAlignment = HorizontalAlignment.Left;
            test.Margin = new Thickness(0, 18, 0, 0);
            Add(test);
        }

        UIElement StyleCard(string id, string title, string sub, bool selected)
        {
            // mini preview of the top of a screen
            var screen = new Grid { Height = 92, Margin = new Thickness(0, 0, 0, 14), ClipToBounds = true };
            screen.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = new LinearGradientBrush(Color.FromRgb(0x2B, 0x32, 0x5C), Color.FromRgb(0x5B, 0x2A, 0x5E), 30) });
            if (id == "notch")
            {
                var shape = new Border { Width = 132, Height = 30, Background = Brushes.Black, CornerRadius = new CornerRadius(0, 0, 15, 15), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
                shape.Child = MiniContent();
                screen.Children.Add(shape);
            }
            else
            {
                var shape = new Border { Width = 132, Height = 30, Background = Brushes.Black, CornerRadius = new CornerRadius(15), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 0, 0) };
                shape.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.5, Direction = 270 };
                shape.Child = MiniContent();
                screen.Children.Add(shape);
            }
            var sp = new StackPanel();
            sp.Children.Add(screen);
            var head = new Grid();
            head.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.SemiBold });
            head.Children.Add(new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), HorizontalAlignment = HorizontalAlignment.Right,
                Background = selected ? B(CBlue) : Brushes.Transparent, BorderBrush = selected ? B(CBlue) : B(Color.FromRgb(0x48, 0x48, 0x4A)), BorderThickness = new Thickness(2),
                Child = selected ? new TextBlock { Text = Glyph.Check, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 11, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null
            });
            sp.Children.Add(head);
            sp.Children.Add(new TextBlock { Text = sub, Foreground = Text2, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });

            var card = new Border
            {
                Background = CardBg, CornerRadius = new CornerRadius(18), Padding = new Thickness(16), Margin = new Thickness(id == "notch" ? 0 : 7, 0, id == "notch" ? 7 : 0, 0),
                BorderBrush = selected ? B(CBlue) : Brushes.Transparent, BorderThickness = new Thickness(2), Cursor = Cursors.Hand, Child = sp,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1)
            };
            card.MouseLeftButtonDown += (s, e) => Press(card, 0.97);
            card.MouseLeftButtonUp += (s, e) => { Press(card, 1, true); SharedSettings.Set("notchStyle", id); App.Notch?.ShowInfo(Glyph.Monitor, title, 2.2); };
            card.MouseLeave += (s, e) => Press(card, 1);
            return card;
        }

        UIElement MiniContent()
        {
            var g = new Grid { Margin = new Thickness(8, 0, 10, 0) };
            g.Children.Add(new Ellipse { Width = 18, Height = 18, Fill = new LinearGradientBrush(CIndigo, CBlue, 45), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
            g.Children.Add(new Border { Width = 56, Height = 5, CornerRadius = new CornerRadius(2.5), Background = B(Color.FromRgb(0xE5, 0xE5, 0xEA)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(26, 0, 0, 0) });
            var bars = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            foreach (var h in new[] { 6.0, 11, 8, 12 }) bars.Children.Add(new Border { Width = 2.5, Height = h, Margin = new Thickness(1, 0, 1, 0), CornerRadius = new CornerRadius(1.2), Background = B(CBlue) });
            g.Children.Add(bars);
            return g;
        }

        static void Press(FrameworkElement el, double to, bool spring = false)
        {
            if (!(el.RenderTransform is ScaleTransform sc)) return;
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(spring ? 420 : 90));
            if (spring) a.EasingFunction = new SpringEase { Damping = 0.45, Stiffness = 14 };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        // ================================================================== MUSIC
        void BuildMusic()
        {
            var st = App.Settings;
            Add(Section(Loc.T("pc.music.source")));
            Add(Card(Row(Glyph.Music, CPink, Loc.T("pc.music.default"), Loc.T("pc.music.defaultSub"),
                Segmented(new[] { "Spotify", "YouTube" }, st.MusicSource == "youtube" ? 1 : 0, i => SharedSettings.Set("musicSrc", i == 1 ? "youtube" : "spotify"), 240))));

            // ---------- Spotify
            Add(Section("Spotify"));
            bool connected = SpotifyService.I.Connected;
            var status = Row(Glyph.Music, CSpotify,
                connected ? Loc.F("pc.spotify.connectedAs", string.IsNullOrEmpty(st.SpotifyUser) ? "Spotify" : st.SpotifyUser) : Loc.T("pc.spotify.notConnected"),
                connected ? Loc.T("pc.spotify.connectedSub") : Loc.T("pc.spotify.notConnectedSub"),
                connected
                    ? Btn(Loc.T("pc.spotify.disconnect"), () => SpotifyService.I.Logout(), Color.FromArgb(0x33, 0xFF, 0x45, 0x3A), CRed)
                    : Btn(Loc.T("pc.spotify.connect"), async () =>
                    {
                        if (string.IsNullOrWhiteSpace(App.Settings.SpotifyClientId)) { MessageBox.Show(this, Loc.T("pc.spotify.needClient"), "PC Panel"); return; }
                        await SpotifyService.I.LoginAsync();
                    }, CSpotify));

            var clientBox = Input(st.SpotifyClientId, 330);
            var clientRow = new StackPanel { Margin = new Thickness(0, 12, 0, 14) };
            clientRow.Children.Add(new TextBlock { Text = "Client ID", Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.Medium });
            clientRow.Children.Add(HStack(clientBox, Btn(Loc.T("pc.save"), () =>
            {
                App.Settings.SpotifyClientId = clientBox.Text.Trim();
                App.Settings.Save();
                App.Notch?.ShowInfo(Glyph.Check, Loc.T("pc.saved"), 1.6);
                SharedSettings.Push();
            }, CBlue)));
            ((FrameworkElement)((StackPanel)clientRow.Children[1])).Margin = new Thickness(0, 8, 0, 0);

            var guide = new StackPanel { Margin = new Thickness(0, 12, 0, 16) };
            guide.Children.Add(new TextBlock { Text = Loc.T("pc.spotify.howTitle"), Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.Medium });
            guide.Children.Add(Hint(Loc.T("pc.spotify.how")));
            var redirect = new TextBox { Style = (Style)FindResource("DarkTextBox"), Text = SpotifyService.RedirectUri, IsReadOnly = true, Width = 330, FontFamily = new FontFamily("Consolas"), FontSize = 13.5 };
            var row2 = HStack(redirect, Btn(Loc.T("pc.copy"), () => { try { Clipboard.SetText(SpotifyService.RedirectUri); App.Notch?.ShowInfo(Glyph.Copy, Loc.T("pc.copied"), 1.5); } catch { } }, null, null, Glyph.Copy));
            row2.Margin = new Thickness(0, 10, 0, 0);
            guide.Children.Add(row2);
            var openDash = Btn(Loc.T("pc.spotify.openDashboard"), () => SystemSetup.ShellOpen("https://developer.spotify.com/dashboard/create"), CSpotify, null, Glyph.Globe);
            openDash.HorizontalAlignment = HorizontalAlignment.Left;
            openDash.Margin = new Thickness(0, 12, 0, 0);
            guide.Children.Add(openDash);
            Add(Card(status, clientRow, guide));

            // ---------- YouTube
            Add(Section("YouTube"));
            var keyBox = Input(st.YouTubeKey, 330);
            var keyRow = new StackPanel { Margin = new Thickness(0, 12, 0, 16) };
            keyRow.Children.Add(new TextBlock { Text = Loc.T("pc.youtube.key"), Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.Medium });
            var kr = HStack(keyBox, Btn(Loc.T("pc.save"), () =>
            {
                App.Settings.YouTubeKey = keyBox.Text.Trim();
                App.Settings.Save();
                App.Notch?.ShowInfo(Glyph.Check, Loc.T("pc.saved"), 1.6);
                SharedSettings.Push();
            }, CBlue));
            kr.Margin = new Thickness(0, 8, 0, 0);
            keyRow.Children.Add(kr);
            keyRow.Children.Add(Hint(Loc.T("pc.youtube.how")));
            var getKey = Btn(Loc.T("pc.youtube.getKey"), () => SystemSetup.ShellOpen("https://console.cloud.google.com/apis/library/youtube.googleapis.com"), CYouTube, null, Glyph.Globe);
            getKey.HorizontalAlignment = HorizontalAlignment.Left;
            getKey.Margin = new Thickness(0, 10, 0, 0);
            keyRow.Children.Add(getKey);
            Add(Card(Row(Glyph.Video, CYouTube, "YouTube", Loc.T("pc.youtube.sub"), null), keyRow));
        }

        // ================================================================== FEATURES
        void BuildFeatures()
        {
            var st = App.Settings;
            Add(Section(Loc.T("pc.feat.phoneLink")));
            var clipSend = Btn(Loc.T("pc.clip.send"), () => { if (ClipboardService.SendNow()) App.Notch?.ShowInfo(Glyph.Clipboard, Loc.T("pc.clip.sent"), 2); }, null, null, Glyph.Copy);
            Add(Card(
                Row(Glyph.Clipboard, CTeal, Loc.T("pc.feat.clip"), Loc.T("pc.feat.clipSub"), HStack(clipSend, Switch(st.ClipboardSync, v => SharedSettings.Set("clipSync", v)))),
                SwitchRow(Glyph.Lock, CIndigo, Loc.T("pc.feat.autoLock"), Loc.T("pc.feat.autoLockSub"), st.AutoLock, "autoLock"),
                Row(Glyph.Folder, COrange, Loc.T("pc.feat.files"), FileService.Folder, Btn(Loc.T("pc.open"), () => SystemSetup.ShellOpen(FileService.Folder), null, null, Glyph.Folder))
            ));

            Add(Section(Loc.T("pc.feat.sounds")));
            var vol = new Slider { Style = (Style)FindResource("IosSlider"), Minimum = 0, Maximum = 100, Value = st.SoundVolume, Width = 220 };
            vol.PreviewMouseLeftButtonUp += (s, e) => { SharedSettings.Set("soundVol", (int)vol.Value); SoundService.Play("notify", true); };
            vol.LostMouseCapture += (s, e) => { if ((int)vol.Value != App.Settings.SoundVolume) SharedSettings.Set("soundVol", (int)vol.Value); };
            Add(Card(
                SwitchRow(Glyph.Volume3, CPink, Loc.T("pc.feat.soundsOn"), Loc.T("pc.feat.soundsSub"), st.Sounds, "sounds"),
                Row(Glyph.Volume1, COrange, Loc.T("pc.feat.soundVol"), null, vol)
            ));

            Add(Section(Loc.T("pc.feat.system")));
            Add(Card(
                SwitchRow(Glyph.Camera, CBlue, Loc.T("pc.feat.shotCursor"), Loc.T("pc.feat.shotCursorSub"), st.ShotCursor, "shotCursor"),
                SwitchRow(Glyph.Game, CPurple, Loc.T("pc.feat.game"), Loc.T("pc.feat.gameSub"), st.GameMode, "gameMode"),
                SwitchRow(Glyph.Speed, CRed, Loc.T("pc.feat.history"), Loc.T("pc.feat.historySub"), st.TempHistory, "tempHistory"),
                SwitchRow(Glyph.Sync, CTeal, Loc.T("pc.feat.fans"), Loc.T("pc.feat.fansSub"), st.FanControl, "fanCtl")
            ));
        }

        // ================================================================== SETTINGS
        void BuildSettings()
        {
            var st = App.Settings;
            Add(Section(Loc.T("pc.set.language")));
            var langs = new UniformGrid { Columns = 4 };
            foreach (var (code, name) in Loc.Languages)
            {
                bool sel = code == st.Lang;
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(14, 12, 14, 12), Cursor = Cursors.Hand,
                    Background = sel ? B(Color.FromArgb(0x33, 0x0A, 0x84, 0xFF)) : CardBg,
                    BorderBrush = sel ? B(CBlue) : Brushes.Transparent, BorderThickness = new Thickness(1.5),
                    RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1)
                };
                var g = new Grid();
                g.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 15, FontWeight = sel ? FontWeights.SemiBold : FontWeights.Normal });
                if (sel) g.Children.Add(new TextBlock { Text = Glyph.Check, FontFamily = (FontFamily)FindResource("IconFont"), Foreground = B(CBlue), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
                chip.Child = g;
                string c = code;
                chip.MouseLeftButtonDown += (s, e) => Press(chip, 0.95);
                chip.MouseLeftButtonUp += (s, e) => { Press(chip, 1, true); SharedSettings.Set("lang", c); };
                chip.MouseLeave += (s, e) => Press(chip, 1);
                langs.Children.Add(chip);
            }
            Add(langs);

            Add(Section(Loc.T("pc.set.general")));
            var nameBox = Input(st.DisplayName, 230);
            if (string.IsNullOrEmpty(st.DisplayName)) nameBox.Text = Environment.MachineName;
            var saveName = Btn(Loc.T("pc.save"), () =>
            {
                SharedSettings.Set("pcName", nameBox.Text.Trim());
                App.Notch?.ShowInfo(Glyph.Check, Loc.T("pc.saved"), 1.6);
            }, CBlue);
            Add(Card(
                Row(Glyph.Edit, CBlue, Loc.T("pc.set.pcName"), Loc.F("pc.set.pcNameSub", Environment.MachineName), HStack(nameBox, saveName)),
                SwitchRow(Glyph.Power, CGreen, Loc.T("pc.set.autostart"), Loc.T("pc.set.autostartSub"), st.Autostart, "autostart")
            ));

            Add(Section(Loc.T("pc.set.alerts")));
            Add(Card(
                Row(Glyph.Warning, CRed, Loc.T("pc.set.cpuAlert"), null, Stepper(st.CpuAlert, v => SharedSettings.Set("cpuAlert", v))),
                Row(Glyph.Warning, COrange, Loc.T("pc.set.gpuAlert"), null, Stepper(st.GpuAlert, v => SharedSettings.Set("gpuAlert", v)))
            ));

            Add(Section(Loc.T("pc.set.about")));
            Add(Card(
                Row(Glyph.Info, CGray, "PC Panel " + App.Version, Loc.T("pc.set.aboutSub"), Btn("GitHub", () => SystemSetup.ShellOpen("https://github.com/" + GitHubRepo), null, null, Glyph.Code)),
                Row(Glyph.Folder, CGray, Loc.T("pc.set.logs"), AppSettings.Dir, Btn(Loc.T("pc.open"), () => SystemSetup.ShellOpen(AppSettings.Dir), null, null, Glyph.Folder)),
                Row(Glyph.Refresh, CBlue, Loc.T("pc.pair.reset"), Loc.T("pc.pair.resetSub"), Btn(Loc.T("pc.pair.resetBtn"), ResetPairing, Color.FromArgb(0x33, 0xFF, 0x45, 0x3A), CRed))
            ));
        }

        /// <summary>owner/repo – filled in by the GitHub publish script.</summary>
        public const string GitHubRepo = "lilDickyBoy/pc-panel";

        UIElement Stepper(int value, Action<int> onChange)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var txt = new TextBlock { Text = value + "°C", Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.Bold, Width = 64, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Button Round(string t, int d)
            {
                var b = new Button { Style = (Style)FindResource("CircleButton"), Content = t, FontFamily = (FontFamily)FindResource("UiFont"), FontSize = 18, Width = 36, Height = 36 };
                b.Click += (s, e) =>
                {
                    value = Math.Clamp(value + d, 60, 105);
                    txt.Text = value + "°C";
                    onChange(value);
                };
                return b;
            }
            sp.Children.Add(Round("−", -5));
            sp.Children.Add(txt);
            sp.Children.Add(Round("+", 5));
            return sp;
        }

        // ================================================================== window buttons
        void Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is FrameworkElement fe && !(fe is TextBox))
            {
                try { DragMove(); } catch { }
            }
        }

        void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        void Close_Click(object sender, RoutedEventArgs e)
        {
            AnimateOutThen(() =>
            {
                Close();
                App.UiTrayBalloon(Loc.T("pc.tray.runningBody"), Loc.T("pc.tray.runningTitle"));
            });
        }

        void Quit_Click(object sender, RoutedEventArgs e) => AnimateOutThen(App.Quit);
    }
}

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using PcPanel.Core;
using PcPanel.Services.Screen;

namespace PcPanel.UI
{
    /// <summary>
    /// "Your phone" window: the live phone screen (scrcpy, embedded) with a side bar of phone buttons.
    /// Not a layered window on purpose – the embedded video window would not show inside one.
    /// </summary>
    public sealed class MirrorWindow : Window
    {
        static MirrorWindow _open;

        public static void ShowSingle()
        {
            if (_open != null)
            {
                if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
                _open.Activate();
                return;
            }
            _open = new MirrorWindow();
            _open.Show();
        }

        public static bool IsOpen => _open != null;

        static readonly Color CBlue = Color.FromRgb(0x0A, 0x84, 0xFF), CGreen = Color.FromRgb(0x30, 0xD1, 0x58), CRed = Color.FromRgb(0xFF, 0x45, 0x3A),
            CGray = Color.FromRgb(0x8E, 0x8E, 0x93), CCard = Color.FromRgb(0x1C, 0x1C, 0x1E), CCard2 = Color.FromRgb(0x2C, 0x2C, 0x2E);
        static Brush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        static string G(int c) => ((char)c).ToString();

        readonly ChildHost _host = new ChildHost();
        readonly Border _screenFrame;
        readonly Grid _overlay;
        readonly TextBlock _ovGlyph, _ovTitle, _ovText;
        readonly Border _ovBarTrack, _ovBar;
        readonly Button _ovRetry;
        readonly Ellipse _spinner;
        readonly TextBlock _title, _pill;
        readonly Border _pillBox;
        readonly Button _pinBtn;
        readonly DispatcherTimer _ui = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        double _aspect = 1080.0 / 2340.0;   // width / height of the phone picture

        const double ToolbarW = 64, TitleH = 46, Pad = 12;

        public MirrorWindow()
        {
            Title = "PC Panel – " + Loc.T("pm.title");
            Background = Brushes.Black;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
            Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/PcPanel.ico"));
            UseLayoutRounding = true;
            Topmost = App.Settings.PmOnTop;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = TitleH, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false
            });

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TitleH) });
            root.RowDefinitions.Add(new RowDefinition());

            // ------------------------------------------------ title bar
            var bar = new Grid { Margin = new Thickness(14, 0, 8, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition());
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(7), Background = B(CBlue),
                Child = new TextBlock { Text = Glyph.Phone, FontFamily = IconFont, FontSize = 14, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
            _title = new TextBlock { Foreground = Brushes.White, FontSize = 14.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 170 };
            left.Children.Add(_title);
            _pill = new TextBlock { Foreground = B(CGreen), FontSize = 11.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            _pillBox = new Border { CornerRadius = new CornerRadius(9), Background = B(Color.FromArgb(0x2A, 0x30, 0xD1, 0x58)), Padding = new Thickness(8, 2, 8, 3), Margin = new Thickness(10, 0, 0, 0), Child = _pill, VerticalAlignment = VerticalAlignment.Center, Opacity = 0 };
            left.Children.Add(_pillBox);
            bar.Children.Add(left);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _pinBtn = TitleButton(G(0xE718), Loc.T("pm.pin"), () =>
            {
                App.Settings.PmOnTop = !App.Settings.PmOnTop; App.Settings.Save();
                Topmost = App.Settings.PmOnTop; UpdatePin();
            });
            right.Children.Add(_pinBtn);
            right.Children.Add(TitleButton(Glyph.Minimize, null, () => WindowState = WindowState.Minimized));
            right.Children.Add(TitleButton(Glyph.ChromeClose, null, Close, true));
            Grid.SetColumn(right, 1);
            bar.Children.Add(right);
            root.Children.Add(bar);

            // ------------------------------------------------ body: phone + side bar
            var body = new Grid { Margin = new Thickness(Pad, 0, 6, Pad) };
            Grid.SetRow(body, 1);
            body.ColumnDefinitions.Add(new ColumnDefinition());
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ToolbarW) });

            _screenFrame = new Border { Background = B(Color.FromRgb(0x0A, 0x0A, 0x0B)), CornerRadius = new CornerRadius(16), ClipToBounds = true, BorderBrush = B(Color.FromRgb(0x26, 0x26, 0x28)), BorderThickness = new Thickness(1) };
            var screenGrid = new Grid();
            screenGrid.Children.Add(_host);

            // overlay shown while connecting / on errors (the video window can't be covered, so it is hidden meanwhile)
            _overlay = new Grid { Background = B(Color.FromRgb(0x0A, 0x0A, 0x0B)) };
            var ov = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(24) };
            var iconGrid = new Grid { Width = 86, Height = 86, HorizontalAlignment = HorizontalAlignment.Center };
            _spinner = new Ellipse
            {
                Width = 86, Height = 86, StrokeThickness = 3.5, StrokeDashArray = new DoubleCollection { 18, 9 }, StrokeDashCap = PenLineCap.Round,
                Stroke = new LinearGradientBrush(CBlue, Color.FromArgb(0, 0x0A, 0x84, 0xFF), 0), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform()
            };
            iconGrid.Children.Add(_spinner);
            iconGrid.Children.Add(new Border
            {
                Width = 60, Height = 60, CornerRadius = new CornerRadius(18), Background = B(CCard2),
                Child = _ovGlyph = new TextBlock { Text = Glyph.Phone, FontFamily = IconFont, FontSize = 28, Foreground = B(CBlue), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
            ov.Children.Add(iconGrid);
            _ovTitle = new TextBlock { Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 20, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
            _ovText = new TextBlock { Foreground = B(CGray), FontSize = 13, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 260, LineHeight = 19 };
            ov.Children.Add(_ovTitle);
            ov.Children.Add(_ovText);
            _ovBar = new Border { CornerRadius = new CornerRadius(3), Background = B(CBlue), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            _ovBarTrack = new Border { Width = 200, Height = 6, CornerRadius = new CornerRadius(3), Background = B(CCard2), Margin = new Thickness(0, 16, 0, 0), Child = _ovBar, Visibility = Visibility.Collapsed };
            ov.Children.Add(_ovBarTrack);
            _ovRetry = new Button { Style = (Style)Application.Current.FindResource("PillButton"), Content = Loc.T("pm.retry"), Background = B(CBlue), Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
            _ovRetry.Click += (s, e) => _ = PhoneMirror.I.Start();
            ov.Children.Add(_ovRetry);
            _overlay.Children.Add(ov);
            screenGrid.Children.Add(_overlay);
            _screenFrame.Child = screenGrid;
            body.Children.Add(_screenFrame);

            var tools = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            void T(int glyph, string key, Action a, Color? c = null) => tools.Children.Add(ToolButton(G(glyph), Loc.T(key), a, c));
            T(0xE7E8, "pm.btn.power", () => PhoneMirror.I.Key("power"), CRed);
            Sep(tools);
            T(0xE995, "pm.btn.volup", () => PhoneMirror.I.Key("volup"));
            T(0xE993, "pm.btn.voldown", () => PhoneMirror.I.Key("voldown"));
            Sep(tools);
            T(0xE72B, "pm.btn.back", () => PhoneMirror.I.Key("back"));
            T(0xE80F, "pm.btn.home", () => PhoneMirror.I.Key("home"));
            T(0xE8A9, "pm.btn.recents", () => PhoneMirror.I.Key("recents"));
            Sep(tools);
            T(0xE7AD, "pm.btn.rotate", () => PhoneMirror.I.Rotate());
            T(0xEA8F, "pm.btn.notif", () => PhoneMirror.I.Key("notif"));
            T(0xE722, "pm.btn.shot", TakeShot, CBlue);
            Grid.SetColumn(tools, 1);
            body.Children.Add(tools);
            root.Children.Add(body);

            Content = root;
            UpdatePin();

            // size: a tall phone-shaped window on the right of the main screen
            var wa = SystemParameters.WorkArea;
            double areaH = Math.Min(860, wa.Height * 0.86);
            Height = areaH + TitleH + Pad;
            Width = areaH * _aspect + ToolbarW + Pad + 6;
            Left = wa.Right - Width - 40;
            Top = wa.Top + (wa.Height - Height) / 2;
            MinWidth = 260; MinHeight = 320;

            PhoneMirror.I.Changed += OnChanged;
            PhoneMirror.I.WindowReady += OnWindowReady;
            PhoneMirror.I.VideoSize += OnVideoSize;
            _ui.Tick += (s, e) => UpdateStatus();
            _ui.Start();

            SourceInitialized += (s, e) => StyleFrame();
            Loaded += (s, e) =>
            {
                Opacity = 0;
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
                UpdateStatus();
                if (PhoneMirror.I.State != PhoneMirror.St.Running && PhoneMirror.I.State != PhoneMirror.St.Preparing) _ = PhoneMirror.I.Start();
            };
            Closed += (s, e) =>
            {
                _ui.Stop();
                PhoneMirror.I.Changed -= OnChanged;
                PhoneMirror.I.WindowReady -= OnWindowReady;
                PhoneMirror.I.VideoSize -= OnVideoSize;
                PhoneMirror.I.Stop();
                _open = null;
            };
        }

        static FontFamily IconFont => (FontFamily)Application.Current.FindResource("IconFont");

        // ------------------------------------------------------------------ chrome
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        void StyleFrame()
        {
            var h = new WindowInteropHelper(this).Handle;
            int dark = 1, round = 2, border = 0x002E2C2C; // COLORREF (BGR)
            try { DwmSetWindowAttribute(h, 20, ref dark, 4); } catch { }
            try { DwmSetWindowAttribute(h, 33, ref round, 4); } catch { }
            try { DwmSetWindowAttribute(h, 34, ref border, 4); } catch { }
        }

        Button TitleButton(string glyph, string tip, Action a, bool danger = false)
        {
            var b = new Button
            {
                Width = 34, Height = 30, Margin = new Thickness(2, 0, 0, 0), Cursor = Cursors.Hand, ToolTip = tip,
                Content = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 11, Foreground = B(Color.FromRgb(0xD0, 0xD0, 0xD5)) },
                Template = HoverTemplate(danger ? CRed : Color.FromRgb(0x3A, 0x3A, 0x3C), 8)
            };
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            b.Click += (s, e) => a();
            return b;
        }

        static ControlTemplate HoverTemplate(Color hover, double radius)
        {
            var t = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border), "Bd");
            bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            t.VisualTree = bd;
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(hover), "Bd"));
            t.Triggers.Add(over);
            return t;
        }

        Button ToolButton(string glyph, string tip, Action a, Color? color)
        {
            var b = new Button
            {
                Width = 46, Height = 46, Margin = new Thickness(0, 3, 0, 3), Cursor = Cursors.Hand, ToolTip = tip,
                Content = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 17, Foreground = color.HasValue ? B(color.Value) : Brushes.White },
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1)
            };
            var t = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border), "Bd");
            bd.SetValue(Border.BackgroundProperty, new SolidColorBrush(CCard));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(23));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            t.VisualTree = bd;
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3C)), "Bd"));
            t.Triggers.Add(over);
            b.Template = t;
            // iOS-like press bounce
            b.PreviewMouseLeftButtonDown += (s, e) => Bounce(b, 0.86, 90);
            b.PreviewMouseLeftButtonUp += (s, e) => Bounce(b, 1, 420);
            b.MouseLeave += (s, e) => Bounce(b, 1, 300);
            b.Click += (s, e) => a();
            return b;
        }

        static void Bounce(FrameworkElement el, double to, int ms)
        {
            var st = (ScaleTransform)el.RenderTransform;
            var an = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = to < 1 ? new QuadraticEase() : (IEasingFunction)new SpringEase { Damping = 0.45, Stiffness = 14 } };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, an);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, an);
        }

        static void Sep(Panel p) => p.Children.Add(new Border { Height = 1, Width = 26, Background = B(CCard2), Margin = new Thickness(0, 7, 0, 7) });

        void UpdatePin()
        {
            ((TextBlock)_pinBtn.Content).Foreground = App.Settings.PmOnTop ? B(CBlue) : B(Color.FromRgb(0xD0, 0xD0, 0xD5));
        }

        // ------------------------------------------------------------------ state
        void OnChanged() => Dispatcher.BeginInvoke(new Action(UpdateStatus));

        void UpdateStatus()
        {
            var m = PhoneMirror.I;
            _title.Text = string.IsNullOrEmpty(m.Model) ? Loc.T("pm.title") : m.Model;
            bool running = m.State == PhoneMirror.St.Running;
            if (running)
            {
                _pill.Text = (m.Fps > 0 ? m.Fps + " FPS · " : "") + (m.Wireless ? "Wi-Fi" : "USB");
                if (_pillBox.Opacity == 0) _pillBox.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(300)));
            }
            else if (_pillBox.Opacity > 0) { _pillBox.BeginAnimation(OpacityProperty, null); _pillBox.Opacity = 0; }

            bool showOverlay = !running;
            _overlay.Visibility = showOverlay ? Visibility.Visible : Visibility.Collapsed;
            _host.Visibility = showOverlay ? Visibility.Hidden : Visibility.Visible;
            if (!showOverlay) { _spinner.BeginAnimation(OpacityProperty, null); return; }

            bool error = m.State == PhoneMirror.St.Error;
            bool off = m.State == PhoneMirror.St.Off;
            _ovGlyph.Text = error ? Glyph.Warning : Glyph.Phone;
            _ovGlyph.Foreground = error ? B(CRed) : B(CBlue);
            _spinner.Visibility = error || off ? Visibility.Collapsed : Visibility.Visible;
            var rt = (RotateTransform)_spinner.RenderTransform;
            if (_spinner.Visibility == Visibility.Visible && rt.HasAnimatedProperties == false)
                rt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
            if (error)
            {
                _ovTitle.Text = Loc.T(m.ErrorKey + ".title");
                _ovText.Text = Loc.T(m.ErrorKey);
            }
            else if (off)
            {
                _ovTitle.Text = Loc.T("pm.off.title");
                _ovText.Text = Loc.T("pm.off");
            }
            else
            {
                _ovTitle.Text = Loc.T(string.IsNullOrEmpty(m.StepKey) ? "pm.step.device" : m.StepKey);
                _ovText.Text = Loc.T("pm.wait");
            }
            _ovRetry.Visibility = error || off ? Visibility.Visible : Visibility.Collapsed;
            bool bar = m.State == PhoneMirror.St.Preparing && m.StepProgress >= 0;
            _ovBarTrack.Visibility = bar ? Visibility.Visible : Visibility.Collapsed;
            if (bar) _ovBar.Width = 200 * Math.Clamp(m.StepProgress, 0, 1);
        }

        void OnWindowReady(IntPtr hwnd) => Dispatcher.BeginInvoke(new Action(() =>
        {
            _host.Adopt(hwnd);
            UpdateStatus();
            var m = PhoneMirror.I;
            if (m.VideoW > 0) FitTo(m.VideoW, m.VideoH);
        }));

        void OnVideoSize(int w, int h) => Dispatcher.BeginInvoke(new Action(() => FitTo(w, h)));

        /// <summary>Reshape the window to the phone picture (e.g. after rotating), keeping it on screen.</summary>
        void FitTo(int w, int h)
        {
            if (w <= 0 || h <= 0 || WindowState != WindowState.Normal) return;
            double aspect = w / (double)h;
            if (Math.Abs(aspect - _aspect) < 0.01 && ActualWidth > 0) return;
            _aspect = aspect;
            var wa = SystemParameters.WorkArea;
            double maxAreaW = wa.Width * 0.8 - ToolbarW - Pad, maxAreaH = wa.Height * 0.86 - TitleH - Pad;
            double areaH = aspect < 1 ? Math.Min(860, maxAreaH) : Math.Min(maxAreaH, maxAreaW / aspect);
            double areaW = areaH * aspect;
            if (areaW > maxAreaW) { areaW = maxAreaW; areaH = areaW / aspect; }
            double newW = areaW + ToolbarW + Pad + 6, newH = areaH + TitleH + Pad;
            double cx = Left + ActualWidth / 2, cy = Top + ActualHeight / 2;
            Width = newW; Height = newH;
            Left = Math.Clamp(cx - newW / 2, wa.Left, wa.Right - newW);
            Top = Math.Clamp(cy - newH / 2, wa.Top, wa.Bottom - newH);
        }

        async void TakeShot()
        {
            var file = await PhoneMirror.I.Screenshot();
            if (file == null) { App.Notch?.ShowInfo(Glyph.Warning, Loc.T("pm.shot.failed"), 2); return; }
            try
            {
                var img = new System.Windows.Media.Imaging.BitmapImage();
                img.BeginInit(); img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; img.UriSource = new Uri(file); img.EndInit();
                Clipboard.SetImage(img);
            }
            catch { }
            App.Notch?.ShowInfo(Glyph.Camera, Loc.T("pm.shot.saved"), 2.5);
        }

        // ================================================================== embedding the video window
        sealed class ChildHost : HwndHost
        {
            [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            static extern IntPtr CreateWindowEx(int exStyle, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
            [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
            [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
            [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hgt, bool repaint);
            [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
            [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
            [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
            [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
            [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
            [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

            const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPCHILDREN = 0x02000000, WS_CLIPSIBLINGS = 0x04000000;
            const long WS_POPUP = 0x80000000L, WS_CAPTION = 0x00C00000L, WS_THICKFRAME = 0x00040000L, WS_SYSMENU = 0x00080000L, WS_MINMAX = 0x00030000L;
            const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
            const long WS_EX_APPWINDOW = 0x40000L, WS_EX_TOOLWINDOW = 0x80L;

            IntPtr _hwnd, _child;

            protected override HandleRef BuildWindowCore(HandleRef parent)
            {
                _hwnd = CreateWindowEx(0, "static", "", WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS, 0, 0, 10, 10, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                return new HandleRef(this, _hwnd);
            }

            protected override void DestroyWindowCore(HandleRef hwnd)
            {
                if (_child != IntPtr.Zero && IsWindow(_child)) SetParent(_child, IntPtr.Zero);
                DestroyWindow(hwnd.Handle);
            }

            public void Adopt(IntPtr child)
            {
                if (_hwnd == IntPtr.Zero || child == IntPtr.Zero) return;
                _child = child;
                long style = GetWindowLongPtr(child, GWL_STYLE).ToInt64();
                style &= ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINMAX);
                style |= WS_CHILD | WS_VISIBLE;
                SetWindowLongPtr(child, GWL_STYLE, new IntPtr(style));
                long ex = GetWindowLongPtr(child, GWL_EXSTYLE).ToInt64();
                ex &= ~WS_EX_APPWINDOW; ex |= WS_EX_TOOLWINDOW;   // no extra taskbar button
                SetWindowLongPtr(child, GWL_EXSTYLE, new IntPtr(ex));
                SetParent(child, _hwnd);
                SetWindowPos(child, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0020); // NOSIZE|NOMOVE|NOZORDER|FRAMECHANGED
                Fit();
            }

            void Fit()
            {
                if (_child == IntPtr.Zero || !IsWindow(_child)) return;
                if (GetClientRect(_hwnd, out var r)) MoveWindow(_child, 0, 0, r.R - r.L, r.B - r.T, true);
            }

            protected override void OnWindowPositionChanged(Rect rcBoundingBox)
            {
                base.OnWindowPositionChanged(rcBoundingBox);
                Fit();
            }
        }
    }
}

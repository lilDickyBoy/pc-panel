using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using PcPanel.Core;
using PcPanel.Services;

namespace PcPanel.UI
{
    /// <summary>
    /// Drag &amp; drop to the phone through the notch.
    ///   1. You start dragging something (Explorer reports it, or the cursor hits the top edge while dragging)
    ///      → the notch slides in as a small "peek" pill, even if it was hidden.
    ///   2. You move the drag over the notch → it springs open, "Drop image".
    ///   3. You let go → it morphs into a progress bar, then "Saved to phone ✓", then goes back to normal.
    /// </summary>
    public partial class NotchWindow
    {
        enum DropPhase { None, Peek, Hover, Sending }

        const double PW = 262, PH = 44, PR = 22;       // peek
        const double PNW = 292, PNH = 50;               // peek, cursor getting close
        const double HW = 372, HH = 178, HR = 42;      // hover
        const double SW = 430, SH = 92, SR = 34;        // sending

        DropPhase _phase = DropPhase.None;
        bool _peekNear;
        int _upTicks;
        Native.WinEventDelegate _dragProc;
        IntPtr _dragHook;
        readonly DispatcherTimer _dragWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        readonly DispatcherTimer _leaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        DropStrip _strip;
        static readonly string[] ImageExt = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".heif", ".bmp", ".tif", ".tiff", ".avif", ".jfif" };

        static bool IsImage(string name) => ImageExt.Contains(System.IO.Path.GetExtension(name ?? "").ToLowerInvariant());
        static bool DropEnabled => App.Settings.NotchEnabled && App.Settings.NotchDrop;
        static Session Phone => App.Server?.Sessions.FirstOrDefault(s => s.Authed);

        void InitDrop()
        {
            PeekGlyph.Text = Glyph.Upload;
            _dragProc = OnWinEvent;
            _dragHook = Native.SetWinEventHook(Native.EVENT_OBJECT_DRAGSTART, Native.EVENT_OBJECT_DRAGCOMPLETE, IntPtr.Zero, _dragProc, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
            _dragWatch.Tick += DragWatchTick;
            _leaveTimer.Tick += (s, e) => { _leaveTimer.Stop(); if (_phase == DropPhase.Hover) SetPhase(DropPhase.Peek); };

            DropCatcher.DragEnter += OnDropDrag;
            DropCatcher.DragOver += OnDropDrag;
            DropCatcher.DragLeave += (s, e) => { _leaveTimer.Stop(); _leaveTimer.Start(); e.Handled = true; };
            DropCatcher.Drop += OnDrop;

            _strip = new DropStrip(this);
            Closed += (s, e) => { try { Native.UnhookWinEvent(_dragHook); } catch { } _strip?.Close(); };
        }

        // ================================================================== drag detection
        void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (ev == Native.EVENT_OBJECT_DRAGSTART) DragBegan();
            // cancel/complete: the watch timer notices the mouse button going up, which also covers drops on us
        }

        /// <summary>Something started being dragged somewhere on the desktop.</summary>
        public void DragBegan()
        {
            if (!DropEnabled || _phase == DropPhase.Sending) return;
            if (_phase == DropPhase.None)
            {
                _peekNear = false;
                SetPhase(DropPhase.Peek);
            }
            _upTicks = 0;
            _dragWatch.Start();
        }

        void DragWatchTick(object sender, EventArgs e)
        {
            if (_phase != DropPhase.Peek && _phase != DropPhase.Hover) { _dragWatch.Stop(); return; }
            if (!Native.LeftButtonDown)
            {
                // drag ended without a drop on us (≈150 ms grace so our own Drop handler wins the race)
                if (++_upTicks >= 3) EndDrag();
                return;
            }
            _upTicks = 0;
            if (_phase == DropPhase.Peek && Native.GetCursorPos(out var p))
            {
                // grow a little as the cursor gets close – feels "magnetic"
                Point tl;
                try { tl = Island.PointToScreen(new Point(0, 0)); } catch { return; }
                var dpi = VisualTreeHelper.GetDpi(this);
                double cx = tl.X + Island.ActualWidth * dpi.DpiScaleX / 2;
                double dx = Math.Abs(p.X - cx), dy = Math.Max(0, p.Y - tl.Y);
                bool near = dx < 260 * dpi.DpiScaleX && dy < 200 * dpi.DpiScaleY;
                if (near != _peekNear)
                {
                    _peekNear = near;
                    Morph(near ? PNW : PW, near ? PNH : PH, near ? PNH / 2 : PR);
                }
            }
        }

        void EndDrag()
        {
            _dragWatch.Stop();
            _leaveTimer.Stop();
            if (_phase == DropPhase.Peek || _phase == DropPhase.Hover) SetPhase(DropPhase.None);
        }

        void SetPhase(DropPhase ph)
        {
            _phase = ph;
            bool catching = ph == DropPhase.Peek || ph == DropPhase.Hover;
            DropCatcher.Visibility = catching ? Visibility.Visible : Visibility.Collapsed;
            SetClickThrough(!catching && !_expanded);
            if (catching && _expanded) _expanded = false;
            Refresh(force: true);
        }

        Mode DropMode => _phase switch
        {
            DropPhase.Peek => Mode.DropPeek,
            DropPhase.Hover => Mode.DropHover,
            DropPhase.Sending => Mode.Sending,
            _ => Mode.Hidden
        };

        // ================================================================== WPF drop target
        void OnDropDrag(object sender, DragEventArgs e)
        {
            _leaveTimer.Stop();
            if (_phase == DropPhase.None || _phase == DropPhase.Peek)
            {
                if (_phase == DropPhase.None) { if (!DropEnabled) return; _dragWatch.Start(); }
                DescribeHover(e.Data);
                SetPhase(DropPhase.Hover);
            }
            e.Effects = Phone != null && HasPayload(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        void OnDrop(object sender, DragEventArgs e)
        {
            _leaveTimer.Stop();
            e.Handled = true;
            var phone = Phone;
            List<Item> items = null;
            try { items = Collect(e.Data); } catch (Exception ex) { Log.Write("Drop: " + ex.Message); }
            _dragWatch.Stop();
            if (phone == null || items == null || items.Count == 0)
            {
                SetPhase(DropPhase.None);
                if (phone == null) ShowResult(false, Loc.T("pc.drop.noPhone"));
                return;
            }
            e.Effects = DragDropEffects.Copy;
            StartSending(phone, items);
        }

        static bool HasPayload(IDataObject d) =>
            d.GetDataPresent(DataFormats.FileDrop) || d.GetDataPresent("FileGroupDescriptorW") || d.GetDataPresent(DataFormats.Bitmap);

        void DescribeHover(IDataObject d)
        {
            var phone = Phone;
            int count = 1; bool allImages = true;
            try
            {
                if (d.GetDataPresent(DataFormats.FileDrop) && d.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    var real = files.Where(File.Exists).ToArray();
                    count = Math.Max(1, real.Length);
                    allImages = real.Length > 0 && real.All(IsImage);
                }
                else if (d.GetDataPresent("FileGroupDescriptorW"))
                {
                    var names = DescriptorNames(d);
                    allImages = names.Count == 0 || names.All(IsImage);
                }
            }
            catch { }

            if (phone == null)
            {
                HoverGlyph.Text = Glyph.Phone;
                HoverGlyph.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
                HoverTitle.Text = Loc.T("pc.drop.noPhone");
                HoverSub.Text = Loc.T("pc.drop.noPhoneSub");
                return;
            }
            HoverGlyph.Foreground = Brushes.White;
            HoverGlyph.Text = allImages ? Glyph.Photo : Glyph.File;
            HoverTitle.Text = allImages
                ? (count == 1 ? Loc.T("pc.drop.image") : Loc.F("pc.drop.images", count))
                : (count == 1 ? Loc.T("pc.drop.file") : Loc.F("pc.drop.files", count));
            HoverSub.Text = Loc.F(allImages ? "pc.drop.toGallery" : "pc.drop.toDownloads", phone.DeviceName);
        }

        void UpdatePeek()
        {
            bool ok = Phone != null;
            PeekText.Text = ok ? Loc.T("pc.drop.peek") : Loc.T("pc.drop.noPhone");
            var c = ok ? Color.FromRgb(0x0A, 0x84, 0xFF) : Color.FromRgb(0xFF, 0x9F, 0x0A);
            PeekGlyph.Text = ok ? Glyph.Upload : Glyph.Phone;
            PeekGlyph.Foreground = new SolidColorBrush(c);
            PeekDisc.Fill = new SolidColorBrush(Color.FromArgb(0x2E, c.R, c.G, c.B));
        }

        // ================================================================== collecting what was dropped
        sealed class Item { public string Path; public string Name; public bool Temp; public bool Image; }

        static List<Item> Collect(IDataObject d)
        {
            var list = new List<Item>();
            if (d.GetDataPresent(DataFormats.FileDrop) && d.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (var f in files.Where(File.Exists))
                    list.Add(new Item { Path = f, Name = System.IO.Path.GetFileName(f), Image = IsImage(f) });
                if (list.Count > 0) return list;
            }
            if (d.GetDataPresent("FileGroupDescriptorW"))
            {
                // virtual file, e.g. an image dragged out of a browser
                var names = DescriptorNames(d);
                if (names.Count > 0 && d.GetData("FileContents") is MemoryStream ms)
                {
                    var name = names[0];
                    var path = TempPath(name);
                    File.WriteAllBytes(path, ms.ToArray());
                    list.Add(new Item { Path = path, Name = name, Temp = true, Image = IsImage(name) });
                    return list;
                }
            }
            if (d.GetDataPresent(DataFormats.Bitmap) && d.GetData(DataFormats.Bitmap) is BitmapSource bmp)
            {
                var name = "Image " + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss") + ".png";
                var path = TempPath(name);
                using (var fs = File.Create(path))
                {
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bmp));
                    enc.Save(fs);
                }
                list.Add(new Item { Path = path, Name = name, Temp = true, Image = true });
            }
            return list;
        }

        static List<string> DescriptorNames(IDataObject d)
        {
            var names = new List<string>();
            if (d.GetData("FileGroupDescriptorW") is not MemoryStream ms) return names;
            var b = ms.ToArray();
            if (b.Length < 4) return names;
            int count = BitConverter.ToInt32(b, 0);
            const int size = 592, nameOffset = 72;
            for (int i = 0; i < count && 4 + (i + 1) * size <= b.Length; i++)
            {
                int o = 4 + i * size + nameOffset;
                var n = System.Text.Encoding.Unicode.GetString(b, o, 520);
                int z = n.IndexOf('\0');
                if (z >= 0) n = n.Substring(0, z);
                if (!string.IsNullOrWhiteSpace(n)) names.Add(n);
            }
            return names;
        }

        static string TempPath(string name)
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PC Panel", "drop-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return System.IO.Path.Combine(dir, name);
        }

        // ================================================================== sending
        void StartSending(Session phone, List<Item> items)
        {
            SetPhase(DropPhase.Sending);
            SetSendItem(items[0], 0, items.Count, phone.DeviceName);
            SetSendProgress(0, animate: false);
            var shine = new DoubleAnimation(-1, 1, TimeSpan.FromMilliseconds(1300)) { RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(shine, 60);
            ShineMove.BeginAnimation(TranslateTransform.XProperty, shine);

            Task.Run(async () =>
            {
                int okCount = 0; string failed = null;
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i]; int idx = i;
                    if (idx > 0) App.Ui(() => SetSendItem(it, idx, items.Count, phone.DeviceName));
                    var r = await FileService.SendAsync(phone, it.Path,
                        p => App.Ui(() => SetSendProgress((idx + p) / items.Count, true)), null, it.Name);
                    if (r == FileService.SendResult.Ok) okCount++; else { failed = it.Name; break; }
                }
                foreach (var it in items.Where(x => x.Temp))
                    try { Directory.Delete(System.IO.Path.GetDirectoryName(it.Path), true); } catch { }
                bool allImages = items.All(x => x.Image);
                App.Ui(async () =>
                {
                    if (failed == null)
                    {
                        SetSendProgress(1, true);
                        await Task.Delay(380);
                    }
                    ShineMove.BeginAnimation(TranslateTransform.XProperty, null);
                    _phase = DropPhase.None;
                    if (failed == null)
                    {
                        string text = items.Count == 1
                            ? Loc.T(allImages ? "pc.drop.savedImage" : "pc.drop.savedFile")
                            : Loc.F("pc.drop.savedMany", items.Count);
                        ShowResult(true, text);
                        SoundService.Play("done");
                    }
                    else
                    {
                        ShowResult(false, Loc.F("pc.drop.failed", failed));
                        SoundService.Play("alert");
                    }
                });
            });
        }

        void SetSendItem(Item it, int idx, int count, string phoneName)
        {
            SendTitle.Text = count == 1 ? Loc.F("pc.drop.sendingTo", phoneName) : Loc.F("pc.drop.sendingOf", idx + 1, count, phoneName);
            SendName.Text = it.Name;
            SendGlyph.Text = it.Image ? Glyph.Photo : Glyph.File;
            SendGlyph.Visibility = Visibility.Visible;
            SendThumb.Fill = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2E));
            if (it.Image)
            {
                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    bi.DecodePixelWidth = 128;
                    bi.UriSource = new Uri(it.Path);
                    bi.EndInit();
                    bi.Freeze();
                    var brush = new ImageBrush(bi) { Stretch = Stretch.UniformToFill };
                    brush.Freeze();
                    SendThumb.Fill = brush;
                    SendGlyph.Visibility = Visibility.Collapsed;
                }
                catch { }
            }
        }

        void SetSendProgress(double frac, bool animate)
        {
            frac = Math.Clamp(frac, 0, 1);
            SendPct.Text = ((int)Math.Round(frac * 100)) + "%";
            double w = SendTrack.ActualWidth > 0 ? SendTrack.ActualWidth : SW - 18 - 22 - 58 - 14;
            var a = new DoubleAnimation(w * frac, TimeSpan.FromMilliseconds(animate ? 260 : 0)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Timeline.SetDesiredFrameRate(a, Fps);
            SendFill.BeginAnimation(WidthProperty, a);
        }

        void ShowResult(bool ok, string text)
        {
            InfoGlyph.Visibility = Visibility.Visible;
            _infoBattery.Visibility = Visibility.Collapsed;
            InfoGlyph.Text = ok ? Glyph.CheckBold : Glyph.Warning;
            var c = ok ? Color.FromRgb(0x30, 0xD1, 0x58) : Color.FromRgb(0xFF, 0x45, 0x3A);
            InfoGlyph.Foreground = new SolidColorBrush(c);
            InfoText.Text = text;
            InfoText.Foreground = Brushes.White;
            _transient = Mode.Info;
            Restart(_transientTimer, ok ? 2.6 : 4);
            Go(Mode.Info, _shown == Mode.Info);
            // a little "pop" on the check mark
            var sc = new ScaleTransform(0.4, 0.4);
            InfoGlyph.RenderTransformOrigin = new Point(0.5, 0.5);
            InfoGlyph.RenderTransform = sc;
            var pop = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(620)) { BeginTime = TimeSpan.FromMilliseconds(120), EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 4 } };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }

        // ================================================================== looping decorations
        void StartDropLoops(Mode m)
        {
            StopDropLoops();
            if (m == Mode.DropPeek)
            {
                var bob = new DoubleAnimation(1.5, -2.5, TimeSpan.FromMilliseconds(520)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                Timeline.SetDesiredFrameRate(bob, 60);
                ((TranslateTransform)PeekGlyph.RenderTransform).BeginAnimation(TranslateTransform.YProperty, bob);
            }
            else if (m == Mode.DropHover)
            {
                var sc = (ScaleTransform)HoverGlow.RenderTransform;
                var pulse = new DoubleAnimation(0.82, 1.12, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                Timeline.SetDesiredFrameRate(pulse, 60);
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
                var march = new DoubleAnimation(0, -11.2, TimeSpan.FromMilliseconds(900)) { RepeatBehavior = RepeatBehavior.Forever };
                Timeline.SetDesiredFrameRate(march, 60);
                HoverDash.BeginAnimation(Shape.StrokeDashOffsetProperty, march);
                var bob = new DoubleAnimation(2, -3, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                Timeline.SetDesiredFrameRate(bob, 60);
                ((TranslateTransform)HoverGlyph.RenderTransform).BeginAnimation(TranslateTransform.YProperty, bob);
            }
        }

        void StopDropLoops()
        {
            ((TranslateTransform)PeekGlyph.RenderTransform).BeginAnimation(TranslateTransform.YProperty, null);
            var sc = (ScaleTransform)HoverGlow.RenderTransform;
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            HoverDash.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            ((TranslateTransform)HoverGlyph.RenderTransform).BeginAnimation(TranslateTransform.YProperty, null);
        }
    }

    /// <summary>
    /// A 3 px strip along the top-centre edge. Some apps (browsers) don't report drag starts, so when the
    /// cursor reaches the top edge while dragging, this wakes the notch. It is click-through unless the
    /// left button is held, so normal clicks at the very top of the screen are never blocked.
    /// </summary>
    sealed class DropStrip : Window
    {
        readonly NotchWindow _notch;
        readonly DispatcherTimer _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        IntPtr _hwnd;
        bool _transparent = true;

        public DropStrip(NotchWindow notch)
        {
            _notch = notch;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Focusable = false;
            Width = 460; Height = 3;
            AllowDrop = true;
            Title = "PC Panel drop strip";
            SourceInitialized += (s, e) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                var ex = (long)Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE);
                ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT;
                Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, (IntPtr)ex);
            };
            DragEnter += (s, e) => { _notch.DragBegan(); e.Effects = DragDropEffects.None; e.Handled = true; };
            DragOver += (s, e) => { e.Effects = DragDropEffects.None; e.Handled = true; };
            _poll.Tick += (s, e) => Poll();
            Place();
            Show();
            _poll.Start();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) => Dispatcher.BeginInvoke(new Action(Place));
        }

        void Place()
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = 0;
        }

        void Poll()
        {
            if (_hwnd == IntPtr.Zero) return;
            bool enabled = App.Settings.NotchEnabled && App.Settings.NotchDrop;
            // only catch the mouse while a button is held (i.e. something might be dragged)
            bool wantTransparent = !(enabled && Native.LeftButtonDown);
            if (wantTransparent == _transparent) return;
            _transparent = wantTransparent;
            var ex = (long)Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE);
            ex = wantTransparent ? ex | Native.WS_EX_TRANSPARENT : ex & ~(long)Native.WS_EX_TRANSPARENT;
            Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, (IntPtr)ex);
            if (!wantTransparent) Topmost = true;
        }
    }
}

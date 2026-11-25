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
        static readonly string[] ImageExt = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".heif", ".bmp", ".tif", ".tiff", ".avif", ".jfif" };

        static bool IsImage(string name) => ImageExt.Contains(System.IO.Path.GetExtension(name ?? "").ToLowerInvariant());
        static bool DropEnabled => App.Settings.NotchEnabled && App.Settings.NotchDrop;
        static Session Phone => App.Server?.Sessions.FirstOrDefault(s => s.Authed);

        readonly DispatcherTimer _dragPoll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        string[] _hoverGuess;

        void InitDrop()
        {
            PeekGlyph.Text = Glyph.Upload;
            _dragProc = OnWinEvent;
            _dragHook = Native.SetWinEventHook(Native.EVENT_OBJECT_DRAGSTART, Native.EVENT_OBJECT_DRAGCOMPLETE, IntPtr.Zero, _dragProc, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
            _dragWatch.Tick += DragWatchTick;
            _leaveTimer.Tick += (s, e) => { _leaveTimer.Stop(); if (_phase == DropPhase.Hover) SetPhase(DropPhase.Peek); };

            // PC Panel runs as administrator, and Windows blocks normal (OLE) drag & drop from Explorer into
            // elevated windows. The classic WM_DROPFILES drop still works once these messages are allowed through.
            Native.ChangeWindowMessageFilterEx(_hwnd, Native.WM_DROPFILES, Native.MSGFLT_ALLOW, IntPtr.Zero);
            Native.ChangeWindowMessageFilterEx(_hwnd, Native.WM_COPYDATA, Native.MSGFLT_ALLOW, IntPtr.Zero);
            Native.ChangeWindowMessageFilterEx(_hwnd, Native.WM_COPYGLOBALDATA, Native.MSGFLT_ALLOW, IntPtr.Zero);
            Native.DragAcceptFiles(_hwnd, true);
            HwndSource.FromHwnd(_hwnd)?.AddHook(DropHook);

            // Some apps don't report drag starts; Explorer always shows its drag image window while dragging.
            _dragPoll.Tick += (s, e) =>
            {
                if (_phase != DropPhase.None || !DropEnabled || !Native.LeftButtonDown) return;
                var img = Native.FindWindow("SysDragImage", null);
                if (img != IntPtr.Zero && Native.IsWindowVisible(img)) DragBegan();
            };
            _dragPoll.Start();
            Closed += (s, e) => { try { Native.UnhookWinEvent(_dragHook); } catch { } };
        }

        IntPtr DropHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != Native.WM_DROPFILES) return IntPtr.Zero;
            handled = true;
            var paths = new List<string>();
            try
            {
                uint n = Native.DragQueryFile(wParam, 0xFFFFFFFF, null, 0);
                for (uint i = 0; i < n; i++)
                {
                    var sb = new System.Text.StringBuilder(1024);
                    Native.DragQueryFile(wParam, i, sb, (uint)sb.Capacity);
                    paths.Add(sb.ToString());
                }
            }
            catch (Exception ex) { Log.Write("Drop: " + ex.Message); }
            finally { Native.DragFinish(wParam); }
            Dispatcher.BeginInvoke(new Action(() => OnFilesDropped(paths.ToArray())));
            return IntPtr.Zero;
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
                _hoverGuess = ExplorerSelection();
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
            if (!Native.GetCursorPos(out var cp)) return;
            bool over = CursorOverCatcher(cp);
            if (over && _phase == DropPhase.Peek)
            {
                _leaveTimer.Stop();
                DescribeHover(_hoverGuess);
                SetPhase(DropPhase.Hover);
                return;
            }
            if (!over && _phase == DropPhase.Hover) { if (!_leaveTimer.IsEnabled) _leaveTimer.Start(); return; }
            if (over) { _leaveTimer.Stop(); return; }
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

        // ================================================================== drop target
        bool CursorOverCatcher(Native.POINT p)
        {
            try
            {
                if (DropCatcher.Visibility != Visibility.Visible) return false;
                var tl = DropCatcher.PointToScreen(new Point(0, 0));
                var br = DropCatcher.PointToScreen(new Point(DropCatcher.ActualWidth, DropCatcher.ActualHeight));
                return p.X >= tl.X && p.X <= br.X && p.Y >= tl.Y - 2 && p.Y <= br.Y;
            }
            catch { return false; }
        }

        void OnFilesDropped(string[] paths)
        {
            _leaveTimer.Stop();
            _dragWatch.Stop();
            var phone = Phone;
            var items = paths.Where(File.Exists).Select(f => new Item { Path = f, Name = System.IO.Path.GetFileName(f), Image = IsImage(f) }).ToList();
            if (phone == null || items.Count == 0)
            {
                SetPhase(DropPhase.None);
                if (phone == null) ShowResult(false, Loc.T("pc.drop.noPhone"));
                return;
            }
            StartSending(phone, items);
        }

        /// <summary>Hover text. We can't see what is being dragged until it is dropped, so we peek at
        /// Explorer's selection (that's what you're dragging) when possible.</summary>
        void DescribeHover(string[] guess)
        {
            var phone = Phone;
            if (phone == null)
            {
                HoverGlyph.Text = Glyph.Phone;
                HoverGlyph.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
                HoverTitle.Text = Loc.T("pc.drop.noPhone");
                HoverSub.Text = Loc.T("pc.drop.noPhoneSub");
                return;
            }
            HoverGlyph.Foreground = Brushes.White;
            var files = (guess ?? Array.Empty<string>()).Where(File.Exists).ToArray();
            bool known = files.Length > 0;
            bool allImages = known && files.All(IsImage);
            int count = Math.Max(1, files.Length);
            HoverGlyph.Text = allImages || !known ? Glyph.Photo : Glyph.File;
            HoverTitle.Text = !known ? Loc.T("pc.drop.image")
                : allImages ? (count == 1 ? Loc.T("pc.drop.image") : Loc.F("pc.drop.images", count))
                : (count == 1 ? Loc.T("pc.drop.file") : Loc.F("pc.drop.files", count));
            HoverSub.Text = Loc.F(allImages || !known ? "pc.drop.toGallery" : "pc.drop.toDownloads", phone.DeviceName);
        }

        /// <summary>Files selected in the foreground Explorer window (= what is being dragged). Best effort.</summary>
        static string[] ExplorerSelection()
        {
            string[] result = null;
            var t = new System.Threading.Thread(() =>
            {
                try
                {
                    var fg = Native.GetForegroundWindow();
                    dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                    foreach (dynamic w in shell.Windows())
                    {
                        try
                        {
                            if (new IntPtr((long)w.HWND) != fg) continue;
                            var list = new List<string>();
                            foreach (dynamic it in w.Document.SelectedItems()) list.Add((string)it.Path);
                            result = list.ToArray();
                            break;
                        }
                        catch { }
                    }
                }
                catch { }
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            t.Join(350);
            return result;
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

}

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using PcPanel.Core;
using PcPanel.Services;

namespace PcPanel.UI
{
    /// <summary>
    /// Lyrics mode: while music plays and synced lyrics were found, the compact player grows into a wider
    /// pill that shows the current line (and the next one, dimmed). Each new line slides up from a blur,
    /// and the notch smoothly resizes to fit it.
    /// </summary>
    public partial class NotchWindow
    {
        const double LH = 62, LR = 28, LMinW = 330, LMaxW = 620;
        readonly DispatcherTimer _lyricTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
        int _lyricIdx = -2;
        bool _lyricAIsFront = true;
        string _lyricTrack;

        bool LyricsActive =>
            App.Settings.Lyrics && LyricsService.I.HasLyrics && MediaService.I.Playing && !_expanded;

        void InitLyrics()
        {
            _lyricTimer.Tick += (s, e) => LyricTick();
            LyricsService.I.Changed += () => Dispatcher.BeginInvoke(new Action(LyricsChanged));
        }

        void LyricsChanged()
        {
            _lyricIdx = -2;
            if (_shown == Mode.Music && !_expanded) Go(Mode.Music, false);
        }

        /// <summary>Called from Go(Music) when not expanded.</summary>
        void ShowLyricsOrCompact()
        {
            if (LyricsActive)
            {
                if (_lyricTrack != LyricsService.I.TrackKey) { _lyricTrack = LyricsService.I.TrackKey; _lyricIdx = -2; }
                int idx = LyricsService.I.IndexAt(MediaService.I.PositionSec);
                SetLyric(idx, animate: _currentPanel == LyricsPanel);
                ShowPanel(LyricsPanel, false);
                _lyricTimer.Start();
            }
            else
            {
                _lyricTimer.Stop();
                Morph(CW, CH, CR);
                ShowPanel(CompactPanel, false);
            }
        }

        void LyricTick()
        {
            if (_shown != Mode.Music || _expanded) { _lyricTimer.Stop(); return; }
            if (!LyricsActive)
            {
                // lyrics switched off / music paused → back to the compact player
                if (_currentPanel == LyricsPanel) Go(Mode.Music, false);
                return;
            }
            int idx = LyricsService.I.IndexAt(MediaService.I.PositionSec);
            if (idx != _lyricIdx) SetLyric(idx, animate: true);
        }

        void SetLyric(int idx, bool animate)
        {
            var lines = LyricsService.I.Lines;
            bool changed = idx != _lyricIdx;
            _lyricIdx = idx;
            string cur = idx >= 0 && idx < lines.Count ? lines[idx].text : "";
            if (string.IsNullOrWhiteSpace(cur)) cur = "♪";
            string next = "";
            for (int i = idx + 1; i < lines.Count && i <= idx + 3; i++)
                if (!string.IsNullOrWhiteSpace(lines[i].text)) { next = lines[i].text; break; }

            var front = _lyricAIsFront ? LyricA : LyricB;
            var back = _lyricAIsFront ? LyricB : LyricA;
            if (!animate || !changed)
            {
                front.Text = cur;
                front.Opacity = 1; ((TranslateTransform)front.RenderTransform).Y = 0; front.Effect = null;
                back.Opacity = 0;
                LyricNext.Text = next;
            }
            else
            {
                // old line floats up and blurs away, new one rises from below out of a blur (iOS style)
                back.Text = cur;
                AnimateLine(front, 0, -16, 1, 0, 0, 7, 260, false);
                AnimateLine(back, 16, 0, 0, 1, 7, 0, 420, true);
                _lyricAIsFront = !_lyricAIsFront;
                var fade = new DoubleAnimationUsingKeyFrames();
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140))));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420))));
                Timeline.SetDesiredFrameRate(fade, Fps);
                var nextText = next;
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
                t.Tick += (s, e) => { t.Stop(); LyricNext.Text = nextText; };
                t.Start();
                LyricNext.BeginAnimation(OpacityProperty, fade);
            }

            // resize the pill to fit the longer of the two lines
            double w = Math.Max(Measure(cur, 15.5, FontWeights.SemiBold), Measure(next, 12.5, FontWeights.Medium));
            double target = Math.Clamp(w + 12 + 40 + 13 + 18 + 8, LMinW, LMaxW);
            Morph(target, LH, LR);

            // the phone's desk screen shows the line too
            if (changed) App.Server?.Broadcast(new { type = "lyric", line = cur == "♪" ? "" : cur, next });
        }

        void AnimateLine(TextBlock tb, double fromY, double toY, double fromO, double toO, double fromBlur, double toBlur, int ms, bool spring)
        {
            IEasingFunction ease = spring ? new SpringEase { Damping = 0.8, Stiffness = 13 } : new CubicEase { EasingMode = EasingMode.EaseIn };
            var tt = (TranslateTransform)tb.RenderTransform;
            var y = new DoubleAnimation(fromY, toY, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
            var o = new DoubleAnimation(fromO, toO, TimeSpan.FromMilliseconds(spring ? ms * 0.7 : ms));
            Timeline.SetDesiredFrameRate(y, Fps);
            Timeline.SetDesiredFrameRate(o, Fps);
            tt.BeginAnimation(TranslateTransform.YProperty, y);
            tb.BeginAnimation(OpacityProperty, o);
            var blur = new BlurEffect { Radius = fromBlur, RenderingBias = RenderingBias.Performance };
            tb.Effect = blur;
            var b = new DoubleAnimation(fromBlur, toBlur, TimeSpan.FromMilliseconds(ms));
            Timeline.SetDesiredFrameRate(b, Fps);
            b.Completed += (s, e) => { if (toBlur == 0) tb.Effect = null; };
            blur.BeginAnimation(BlurEffect.RadiusProperty, b);
        }

        double Measure(string text, double size, FontWeight weight)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(LyricA.FontFamily, FontStyles.Normal, weight, FontStretches.Normal), size, Brushes.White,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            return ft.WidthIncludingTrailingWhitespace;
        }
    }
}

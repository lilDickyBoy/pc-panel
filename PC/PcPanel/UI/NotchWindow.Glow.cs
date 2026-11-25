using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace PcPanel.UI
{
    /// <summary>
    /// Edge light: a thin glowing line around the notch / island so it stands out on dark wallpapers.
    ///   off   – plain black, like before
    ///   solid – one colour
    ///   flow  – the chosen colours slowly flow around the edge (e.g. blue → white → blue)
    ///   rgb   – rainbow
    ///   album – follows the colour of the current album cover
    /// </summary>
    public partial class NotchWindow
    {
        string _glowSig;
        RotateTransform _glowRot;
        DropShadowEffect _glowFx;

        static readonly Color[] Rainbow =
        {
            Color.FromRgb(0xFF, 0x3B, 0x30), Color.FromRgb(0xFF, 0x9F, 0x0A), Color.FromRgb(0xFF, 0xD6, 0x0A),
            Color.FromRgb(0x30, 0xD1, 0x58), Color.FromRgb(0x64, 0xD2, 0xFF), Color.FromRgb(0x0A, 0x84, 0xFF),
            Color.FromRgb(0xBF, 0x5A, 0xF2)
        };

        public static Color[] ParseColors(string csv)
        {
            var list = (csv ?? "").Split(',')
                .Select(p => p.Trim())
                .Where(p => p.Length == 7 && p[0] == '#')
                .Select(p => { try { return (Color)ColorConverter.ConvertFromString(p); } catch { return (Color?)null; } })
                .Where(c => c.HasValue).Select(c => c.Value).ToArray();
            return list.Length > 0 ? list : new[] { Color.FromRgb(0x0A, 0x84, 0xFF), Colors.White };
        }

        void ApplyGlow(bool force = false)
        {
            var st = App.Settings;
            string mode = st.NotchGlow ?? "flow";
            var accent = PcPanel.Services.MediaService.I.HasMedia ? PcPanel.Services.MediaService.I.Accent : Color.FromRgb(0x0A, 0x84, 0xFF);
            string sig = $"{mode}|{st.NotchGlowColors}|{st.NotchGlowSpeed}|{st.NotchGlowStrength}|{st.NotchStyle}|{(mode == "album" ? accent.ToString() : "")}";
            if (sig == _glowSig && !force) return;
            _glowSig = sig;

            _glowRot?.BeginAnimation(RotateTransform.AngleProperty, null);
            _glowFx?.BeginAnimation(DropShadowEffect.ColorProperty, null);
            _glowRot = null;

            if (mode == "off")
            {
                GlowEdge.Visibility = Visibility.Collapsed;
                GlowEdge.Effect = null;
                return;
            }

            double strength = st.NotchGlowStrength switch { "low" => 0.55, "high" => 1.0, _ => 0.8 };
            GlowEdge.StrokeThickness = st.NotchGlowStrength switch { "low" => 1.0, "high" => 1.9, _ => 1.4 };
            GlowEdge.Margin = IslandShape.Margin;

            Color[] cols = mode switch
            {
                "rgb" => Rainbow,
                "album" => new[] { accent, Lighten(accent, 0.45) },
                "solid" => new[] { ParseColors(st.NotchGlowColors)[0] },
                _ => ParseColors(st.NotchGlowColors)
            };
            if (mode == "flow" && cols.Length == 1) cols = new[] { cols[0], Colors.White };

            double secs = Math.Clamp(st.NotchGlowSpeed, 2, 20);
            Brush stroke;
            if (cols.Length == 1)
            {
                stroke = new SolidColorBrush(cols[0]);
            }
            else
            {
                // a gradient that wraps around (last stop = first colour) and slowly rotates,
                // so the colours appear to travel along the edge
                var lg = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
                for (int i = 0; i < cols.Length; i++) lg.GradientStops.Add(new GradientStop(cols[i], (double)i / cols.Length));
                lg.GradientStops.Add(new GradientStop(cols[0], 1));
                _glowRot = new RotateTransform(0, 0.5, 0.5);
                lg.RelativeTransform = _glowRot;
                stroke = lg;
            }
            GlowEdge.Stroke = stroke;

            _glowFx = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 8 + 10 * strength, Opacity = strength, Color = cols[0], RenderingBias = RenderingBias.Performance };
            GlowEdge.Effect = _glowFx;
            GlowEdge.Opacity = 0.55 + 0.45 * strength;
            GlowEdge.Visibility = Visibility.Visible;

            if (_glowRot != null)
            {
                var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(secs)) { RepeatBehavior = RepeatBehavior.Forever };
                Timeline.SetDesiredFrameRate(spin, 30);   // smooth enough, cheap on the CPU
                _glowRot.BeginAnimation(RotateTransform.AngleProperty, spin);

                // the soft halo cycles through the same colours
                var halo = new ColorAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(secs), RepeatBehavior = RepeatBehavior.Forever };
                for (int i = 0; i <= cols.Length; i++)
                    halo.KeyFrames.Add(new LinearColorKeyFrame(cols[i % cols.Length], KeyTime.FromTimeSpan(TimeSpan.FromSeconds(secs * i / cols.Length))));
                Timeline.SetDesiredFrameRate(halo, 30);
                _glowFx.BeginAnimation(DropShadowEffect.ColorProperty, halo);
            }
        }

        static Color Lighten(Color c, double t) =>
            Color.FromRgb((byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));
    }
}

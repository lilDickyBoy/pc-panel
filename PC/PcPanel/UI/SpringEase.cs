using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace PcPanel.UI
{
    /// <summary>iOS-like damped spring. Damping &lt; 1 gives a small, soft overshoot.</summary>
    public sealed class SpringEase : EasingFunctionBase
    {
        public double Damping { get; set; } = 0.62;
        public double Stiffness { get; set; } = 12.0;

        public SpringEase() { EasingMode = EasingMode.EaseIn; }

        protected override double EaseInCore(double t)
        {
            if (t >= 1) return 1;
            return Raw(t) / Raw(1);
        }

        double Raw(double t)
        {
            double z = Damping, w = Stiffness;
            if (z >= 1) return 1 - Math.Exp(-w * t) * (1 + w * t);
            double wd = w * Math.Sqrt(1 - z * z);
            return 1 - Math.Exp(-z * w * t) * (Math.Cos(wd * t) + (z * w / wd) * Math.Sin(wd * t));
        }

        protected override Freezable CreateInstanceCore() => new SpringEase();
    }
}

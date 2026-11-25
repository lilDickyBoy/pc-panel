using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PcPanel.Core;
using PcPanel.Services;

namespace PcPanel.UI
{
    public partial class NotchWindow
    {
        /// <summary>"AirPods Pro – Connected" with the Bluetooth logo popping in and two ripple rings.</summary>
        public void ShowBluetooth(string device, bool connected)
        {
            if (!CanShowTransient() || !App.Settings.NotchBluetooth) return;
            BtName.Text = string.IsNullOrWhiteSpace(device) ? "Bluetooth" : device;
            BtSub.Text = Loc.T(connected ? "pc.bt.connected" : "pc.bt.disconnected");
            BtIcon.Source = Brand.Get(connected ? "bluetooth" : "bluetooth_white");
            BtIcon.Opacity = connected ? 1 : 0.55;
            BtDisc.Fill = new SolidColorBrush(connected ? Color.FromArgb(0x30, 0x0A, 0x84, 0xFF) : Color.FromArgb(0x26, 0x8E, 0x8E, 0x93));
            ShowTransient(Mode.Bluetooth, 3.4);

            // icon pops in
            var sc = (ScaleTransform)BtIcon.RenderTransform;
            var pop = new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(700)) { BeginTime = TimeSpan.FromMilliseconds(120), EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 5 } };
            Timeline.SetDesiredFrameRate(pop, Fps);
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            if (connected)
            {
                Ripple(BtRing1, 150);
                Ripple(BtRing2, 520);
            }
            SoundService.Play(connected ? "connect" : "disconnect");
        }

        void Ripple(System.Windows.Shapes.Ellipse ring, int delayMs)
        {
            var sc = (ScaleTransform)ring.RenderTransform;
            var grow = new DoubleAnimation(0.9, 1.9, TimeSpan.FromMilliseconds(900)) { BeginTime = TimeSpan.FromMilliseconds(delayMs), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var fade = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(delayMs) };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))));
            Timeline.SetDesiredFrameRate(grow, Fps);
            Timeline.SetDesiredFrameRate(fade, Fps);
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            ring.BeginAnimation(OpacityProperty, fade);
        }
    }
}

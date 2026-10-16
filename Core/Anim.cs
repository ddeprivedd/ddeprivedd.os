using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ddeprivedd.os
{
    /// <summary>Small animation helpers used across the UI.</summary>
    public static class Anim
    {
        private static IEasingFunction Out()
        {
            return new CubicEase { EasingMode = EasingMode.EaseOut };
        }

        /// <summary>Fades an element in while sliding it up a little.</summary>
        public static void FadeUp(UIElement el, int delayMs = 0, double distance = 18, int durationMs = 420)
        {
            var shift = new TranslateTransform(0, distance);
            el.RenderTransform = shift;
            el.Opacity = 0;

            TimeSpan begin = TimeSpan.FromMilliseconds(delayMs);
            TimeSpan length = TimeSpan.FromMilliseconds(durationMs);

            var fade = new DoubleAnimation(0, 1, length) { BeginTime = begin, EasingFunction = Out() };
            fade.Completed += (s, e) =>
            {
                // Release the animation so Opacity can be set normally again.
                el.BeginAnimation(UIElement.OpacityProperty, null);
                el.Opacity = 1;
            };
            var slide = new DoubleAnimation(distance, 0, length) { BeginTime = begin, EasingFunction = Out() };

            el.BeginAnimation(UIElement.OpacityProperty, fade);
            shift.BeginAnimation(TranslateTransform.YProperty, slide);
        }

        /// <summary>FadeUp for every child of a panel, one after another.</summary>
        public static void Stagger(Panel panel, int stepMs = 70, int startMs = 0, double distance = 18)
        {
            int i = 0;
            foreach (UIElement child in panel.Children)
            {
                FadeUp(child, startMs + i * stepMs, distance);
                i++;
            }
        }

        /// <summary>Fades an element out, then runs the callback.</summary>
        public static void FadeOutThen(UIElement el, Action done, int durationMs = 160)
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(durationMs));
            fade.Completed += (s, e) => { if (done != null) done(); };
            el.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        /// <summary>Fades text in, holds, then fades it out again (for short feedback messages).</summary>
        public static void Flash(UIElement el)
        {
            var frames = new DoubleAnimationUsingKeyFrames();
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2800))));
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(3300))));
            el.BeginAnimation(UIElement.OpacityProperty, frames);
        }

        /// <summary>Smoothly moves a progress bar to a new value.</summary>
        public static void SetProgress(ProgressBar bar, double value, int durationMs = 450)
        {
            var move = new DoubleAnimation(value, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = Out() };
            bar.BeginAnimation(RangeBase.ValueProperty, move);
        }

        /// <summary>One full spin, e.g. for a refresh button.</summary>
        public static void Spin(FrameworkElement el, int durationMs = 600)
        {
            var rotate = new RotateTransform(0);
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = rotate;
            var spin = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            rotate.BeginAnimation(RotateTransform.AngleProperty, spin);
        }
    }
}

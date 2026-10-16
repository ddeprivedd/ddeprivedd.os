using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ddeprivedd.os
{
    /// <summary>
    /// Attached behavior: mouse-wheel scrolling that eases to its target.
    /// Used through the "SmoothScroll" ScrollViewer style (which also hides the scrollbar).
    /// </summary>
    public static class SmoothScroll
    {
        private const double PixelsPerNotch = 90;

        public static readonly DependencyProperty EnabledProperty =
            DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(SmoothScroll),
                new PropertyMetadata(false, OnEnabledChanged));

        public static bool GetEnabled(DependencyObject d) { return (bool)d.GetValue(EnabledProperty); }
        public static void SetEnabled(DependencyObject d, bool value) { d.SetValue(EnabledProperty, value); }

        // The offset we are animating towards (NaN when idle).
        private static readonly DependencyProperty TargetProperty =
            DependencyProperty.RegisterAttached("Target", typeof(double), typeof(SmoothScroll),
                new PropertyMetadata(double.NaN));

        // Animated stand-in for the scroll offset.
        private static readonly DependencyProperty OffsetProperty =
            DependencyProperty.RegisterAttached("Offset", typeof(double), typeof(SmoothScroll),
                new PropertyMetadata(0.0, OnOffsetChanged));

        private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var viewer = d as ScrollViewer;
            if (viewer == null) return;

            if ((bool)e.NewValue) viewer.PreviewMouseWheel += OnWheel;
            else viewer.PreviewMouseWheel -= OnWheel;
        }

        private static void OnOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var viewer = d as ScrollViewer;
            if (viewer != null) viewer.ScrollToVerticalOffset((double)e.NewValue);
        }

        private static void OnWheel(object sender, MouseWheelEventArgs e)
        {
            var viewer = (ScrollViewer)sender;
            if (viewer.ScrollableHeight <= 0) return;

            // If the mouse is over a nested scroller that can scroll, let that one handle it.
            DependencyObject node = e.OriginalSource as DependencyObject;
            while (node != null && node != viewer)
            {
                var inner = node as ScrollViewer;
                if (inner != null && inner.ScrollableHeight > 0) return;
                node = (node is Visual) ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            }

            double start = viewer.VerticalOffset;
            double baseline = (double)viewer.GetValue(TargetProperty);
            if (double.IsNaN(baseline)) baseline = start;

            double target = baseline - (e.Delta / 120.0) * PixelsPerNotch;
            target = Math.Max(0, Math.Min(viewer.ScrollableHeight, target));
            viewer.SetValue(TargetProperty, target);

            var animation = new DoubleAnimation(start, target, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            animation.Completed += (s, args) =>
            {
                // Only clear if no newer wheel tick has moved the target.
                double current = (double)viewer.GetValue(TargetProperty);
                if (Math.Abs(current - target) < 0.01) viewer.SetValue(TargetProperty, double.NaN);
            };

            viewer.BeginAnimation(OffsetProperty, animation);
            e.Handled = true;
        }
    }
}

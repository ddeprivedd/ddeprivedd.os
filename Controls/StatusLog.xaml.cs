using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ddeprivedd.os.Controls
{
    /// <summary>Animated activity list used by the Optimizer and Cleaner pages.</summary>
    public partial class StatusLog : UserControl
    {
        public StatusLog()
        {
            InitializeComponent();
        }

        public void Clear()
        {
            Rows.Children.Clear();
            EmptyText.Visibility = Visibility.Visible;
        }

        public void Add(string text, LogKind kind)
        {
            EmptyText.Visibility = Visibility.Collapsed;

            string glyph;
            string brushKey;
            switch (kind)
            {
                case LogKind.Success:   glyph = "\uE73E"; brushKey = "GoodBrush"; break;
                case LogKind.Warning:   glyph = "\uE7BA"; brushKey = "WarnBrush"; break;
                case LogKind.Error:     glyph = "\uE711"; brushKey = "BadBrush"; break;
                case LogKind.Protected: glyph = "\uE72E"; brushKey = "AccentSoftBrush"; break;
                default:                glyph = "\uE946"; brushKey = "InfoBrush"; break;
            }

            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var icon = new TextBlock
            {
                Text = glyph,
                FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
                FontSize = 14,
                Width = 26,
                Foreground = (Brush)Application.Current.Resources[brushKey],
                VerticalAlignment = VerticalAlignment.Center
            };

            var label = new TextBlock
            {
                Text = text,
                FontSize = 13.5,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 1);

            row.Children.Add(icon);
            row.Children.Add(label);
            Rows.Children.Add(row);

            Anim.FadeUp(row, 0, 10, 260);

            // Scroll to the newest line once layout has caught up
            Dispatcher.BeginInvoke(new Action(Scroller.ScrollToEnd), DispatcherPriority.Background);
        }
    }
}

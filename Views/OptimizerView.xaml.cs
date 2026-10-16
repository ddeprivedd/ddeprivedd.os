using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ddeprivedd.os.Views
{
    public partial class OptimizerView : UserControl
    {
        private bool busy;

        public OptimizerView()
        {
            InitializeComponent();

            Loaded += (s, e) =>
            {
                RefreshChips();
                Anim.Stagger(PageRoot);
            };
        }

        private async void ValorantOptimize_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync(p => OptimizerEngine.RunGameAsync("VALORANT", p));
        }

        private async void RobloxOptimize_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync(p => OptimizerEngine.RunGameAsync("ROBLOX", p));
        }

        private async void AMDOptimize_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync(p => OptimizerEngine.RunAmdAsync(p));
        }

        private void ManageWhitelist_Click(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this) as MainWindow;
            if (window != null) window.ShowWhitelist();
        }

        private async Task RunAsync(Func<IProgress<StatusUpdate>, Task> job)
        {
            if (busy) return;
            busy = true;
            SetButtonsEnabled(false);
            RefreshChips();

            Log.Clear();
            Anim.SetProgress(Bar, 0, 200);
            PercentText.Text = "0%";

            var progress = new Progress<StatusUpdate>(update =>
            {
                if (!string.IsNullOrEmpty(update.Message)) Log.Add(update.Message, update.Kind);
                if (update.Percent >= 0)
                {
                    Anim.SetProgress(Bar, update.Percent);
                    PercentText.Text = update.Percent + "%";
                }
            });

            try
            {
                await job(progress);
            }
            catch (Exception ex)
            {
                Log.Add("Optimization failed: " + ex.Message, LogKind.Error);
            }
            finally
            {
                busy = false;
                SetButtonsEnabled(true);
            }
        }

        private void SetButtonsEnabled(bool enabled)
        {
            ValorantBtn.IsEnabled = enabled;
            RobloxBtn.IsEnabled = enabled;
            AmdBtn.IsEnabled = enabled;
        }

        /// <summary>Shows which default apps will be closed and which are protected by the whitelist.</summary>
        private void RefreshChips()
        {
            Chips.Children.Clear();

            foreach (string app in OptimizerEngine.DefaultCloseList)
            {
                bool isProtected = Whitelist.IsProtected(app);

                var chip = new Border
                {
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(12, 8, 14, 8),
                    Margin = new Thickness(0, 0, 10, 10),
                    BorderThickness = new Thickness(1),
                    Background = Hex(isProtected ? "#2234D399" : "#22F87171"),
                    BorderBrush = Hex(isProtected ? "#5534D399" : "#55F87171")
                };

                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock
                {
                    Text = isProtected ? "\uE72E" : "\uE7E8",
                    FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)Application.Current.Resources[isProtected ? "GoodBrush" : "BadBrush"]
                });
                row.Children.Add(new TextBlock
                {
                    Text = app,
                    FontSize = 13.5,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)Application.Current.Resources["TextBrush"]
                });
                row.Children.Add(new TextBlock
                {
                    Text = isProtected ? "Protected" : "Closes",
                    FontSize = 12,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)Application.Current.Resources["MutedBrush"]
                });

                chip.Child = row;
                Chips.Children.Add(chip);
            }
        }

        private static Brush Hex(string color)
        {
            return (Brush)new BrushConverter().ConvertFromString(color);
        }
    }
}

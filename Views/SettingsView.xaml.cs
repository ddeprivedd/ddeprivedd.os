using System;
using System.Windows;
using System.Windows.Controls;

namespace ddeprivedd.os.Views
{
    public partial class SettingsView : UserControl
    {
        // Stops Checked/Unchecked from saving while we fill in the switches
        private bool loadedSettings;

        public SettingsView()
        {
            InitializeComponent();

            StartupCleanupBox.IsChecked     = AppSettings.StartupCleanup;
            LaunchOnStartupBox.IsChecked    = AppSettings.LaunchOnStartup;
            AutoTempCleanBox.IsChecked      = AppSettings.AutoTempClean;
            OptimizationTweaksBox.IsChecked = AppSettings.OptimizationTweaks;
            ReduceBackgroundBox.IsChecked   = AppSettings.ReduceBackgroundUsage;
            AutoRamCleanupBox.IsChecked     = AppSettings.AutoRamCleanup;
            PerformanceBoostBox.IsChecked   = AppSettings.PerformanceBoost;
            FPSBoostBox.IsChecked           = AppSettings.FPSBoost;
            GameModeBox.IsChecked           = AppSettings.GameMode;
            CpuPriorityBox.IsChecked        = AppSettings.CpuPriority;

            AutoUpdateBox.IsChecked         = AppSettings.AutoUpdate;

            loadedSettings = true;

            Action refresh = () => Dispatcher.BeginInvoke(new Action(RefreshUpdateUi));
            Loaded += (s, e) =>
            {
                Anim.Stagger(PageRoot);
                Updater.Changed += refresh;
                RefreshUpdateUi();
            };
            Unloaded += (s, e) => Updater.Changed -= refresh;
        }

        // ---------------------------------------------------------------
        // Updates
        // ---------------------------------------------------------------
        private void AutoUpdate_Changed(object sender, RoutedEventArgs e)
        {
            if (!loadedSettings) return;
            AppSettings.AutoUpdate = AutoUpdateBox.IsChecked.GetValueOrDefault();
            AppSettings.Save();
        }

        private void RefreshUpdateUi()
        {
            UpdateVersionText.Text = "Current version: v" + Updater.CurrentVersionText;
            UpdateStatusText.Text = Updater.Message ?? "";

            switch (Updater.State)
            {
                case UpdateState.Checking:
                case UpdateState.Downloading:
                    UpdateActionButton.IsEnabled = false;
                    break;
                case UpdateState.Available:
                    UpdateActionButton.Content = "Download v" + Updater.AvailableVersionText;
                    UpdateActionButton.IsEnabled = true;
                    break;
                case UpdateState.Ready:
                    UpdateActionButton.Content = "Restart to update";
                    UpdateActionButton.IsEnabled = true;
                    break;
                case UpdateState.NotConfigured:
                    UpdateActionButton.Content = "Check for updates";
                    UpdateActionButton.IsEnabled = false;
                    break;
                default:
                    UpdateActionButton.Content = "Check for updates";
                    UpdateActionButton.IsEnabled = true;
                    break;
            }
        }

        private async void UpdateActionButton_Click(object sender, RoutedEventArgs e)
        {
            switch (Updater.State)
            {
                case UpdateState.Ready:
                    string error;
                    if (Updater.ApplyAndRestart(false, out error))
                        Application.Current.Shutdown();
                    break;

                case UpdateState.Available:
                    await Updater.DownloadAsync();
                    break;

                default:
                    if (await Updater.CheckAsync() && AppSettings.AutoUpdate)
                        await Updater.DownloadAsync();
                    break;
            }
        }

        private void SettingChanged(object sender, RoutedEventArgs e)
        {
            if (!loadedSettings) return;

            AppSettings.StartupCleanup        = StartupCleanupBox.IsChecked.GetValueOrDefault();
            AppSettings.LaunchOnStartup       = LaunchOnStartupBox.IsChecked.GetValueOrDefault();
            AppSettings.AutoTempClean         = AutoTempCleanBox.IsChecked.GetValueOrDefault();
            AppSettings.OptimizationTweaks    = OptimizationTweaksBox.IsChecked.GetValueOrDefault();
            AppSettings.ReduceBackgroundUsage = ReduceBackgroundBox.IsChecked.GetValueOrDefault();
            AppSettings.AutoRamCleanup        = AutoRamCleanupBox.IsChecked.GetValueOrDefault();
            AppSettings.PerformanceBoost      = PerformanceBoostBox.IsChecked.GetValueOrDefault();
            AppSettings.FPSBoost              = FPSBoostBox.IsChecked.GetValueOrDefault();
            AppSettings.GameMode              = GameModeBox.IsChecked.GetValueOrDefault();
            AppSettings.CpuPriority           = CpuPriorityBox.IsChecked.GetValueOrDefault();

            AppSettings.Save();
        }
    }
}

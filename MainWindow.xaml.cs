using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ddeprivedd.os.Views;

namespace ddeprivedd.os
{
    public partial class MainWindow : Window
    {
        private const double NavPitch = 56;   // nav item height (50) + margin (6)

        private readonly RadioButton[] navButtons;
        private readonly UserControl[] views = new UserControl[6];
        private int current = -1;
        private int navToken;
        private bool closing;
        private System.Windows.Forms.NotifyIcon tray;
        private bool trayTipShown;

        public MainWindow()
        {
            InitializeComponent();

            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = "v" + v.Major + "." + v.Minor + (v.Build > 0 ? "." + v.Build : "");

            navButtons = new[] { NavOptimizer, NavWhitelist, NavCleaner, NavMonitor, NavRamLimiter, NavSettings };
            NavOptimizer.IsChecked = true;

            InitTray();

            Updater.Changed += () => Dispatcher.BeginInvoke(new Action(RefreshUpdateButton));
            RefreshUpdateButton();

            StateChanged += (s, e) =>
            {
                if (WindowState == WindowState.Minimized) HideToTray();
            };
        }

        // ---------------------------------------------------------------
        // Update button (top right): shows only when an update is available / ready
        // ---------------------------------------------------------------
        private void RefreshUpdateButton()
        {
            bool show = true;
            switch (Updater.State)
            {
                case UpdateState.Ready:
                    UpdateButton.Content = "Restart to update to v" + Updater.AvailableVersionText;
                    UpdateButton.IsEnabled = true;
                    break;
                case UpdateState.Available:
                    UpdateButton.Content = "Update v" + Updater.AvailableVersionText + " available";
                    UpdateButton.IsEnabled = true;
                    break;
                case UpdateState.Downloading:
                    UpdateButton.Content = "Downloading update… " + Updater.DownloadPercent + "%";
                    UpdateButton.IsEnabled = false;
                    break;
                default:
                    show = false;
                    break;
            }

            if (show && UpdateButton.Visibility != Visibility.Visible)
            {
                UpdateButton.Visibility = Visibility.Visible;
                Anim.FadeUp(UpdateButton, 0, 8, 320);
            }
            else if (!show)
            {
                UpdateButton.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (Updater.State == UpdateState.Ready)
            {
                string error;
                if (Updater.ApplyAndRestart(false, out error))
                    Application.Current.Shutdown();
                else
                    MessageBox.Show(error ?? "The update could not be installed.", "ddeprivedd.os",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                NavSettings.IsChecked = true;   // "Available": go to Settings > Updates to download it
            }
        }

        /// <summary>Starts hidden in the tray (used when an update restarts the app while it was in the tray).</summary>
        public void MinimizeToTray()
        {
            WindowState = WindowState.Minimized;
        }

        /// <summary>Lets pages jump to the Whitelist tab.</summary>
        public void ShowWhitelist()
        {
            NavWhitelist.IsChecked = true;
        }

        // ---------------------------------------------------------------
        // System tray (minimize goes here instead of the taskbar)
        // ---------------------------------------------------------------
        private void InitTray()
        {
            System.Drawing.Icon icon = null;
            try { icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); }
            catch { }
            if (icon == null) icon = System.Drawing.SystemIcons.Application;

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Open ddeprivedd.os", null, (s, e) => RestoreFromTray());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => ExitFromTray());

            tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = icon,
                Text = "ddeprivedd.os",
                ContextMenuStrip = menu,
                Visible = false
            };
            tray.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left) RestoreFromTray();
            };
        }

        private void HideToTray()
        {
            Hide();                 // also removes it from the taskbar
            tray.Visible = true;

            if (!trayTipShown)
            {
                trayTipShown = true;
                tray.ShowBalloonTip(2500, "ddeprivedd.os",
                    "Still running in the tray. The RAM Limiter keeps working. Click the icon to open it.",
                    System.Windows.Forms.ToolTipIcon.Info);
            }
        }

        private void RestoreFromTray()
        {
            BringToFront();
        }

        /// <summary>Shows the window and puts it in front, even if it was hidden in the tray.</summary>
        public void BringToFront()
        {
            if (tray != null) tray.Visible = false;
            WindowState = WindowState.Normal;
            Show();
            Activate();
            Topmost = true;    // brief topmost toggle makes Windows really bring it forward
            Topmost = false;
        }

        private void ExitFromTray()
        {
            tray.Visible = false;
            Application.Current.Shutdown();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
            }
            base.OnClosed(e);
        }

        // ---------------------------------------------------------------
        // Window chrome
        // ---------------------------------------------------------------
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)));
            var pop = new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(450)) { EasingFunction = ease };
            ShellScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            ShellScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }

        private void DragBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            try { DragMove(); } catch { }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            if (closing) return;
            closing = true;

            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
            fade.Completed += (s, args) => Close();
            BeginAnimation(OpacityProperty, fade);
        }

        // ---------------------------------------------------------------
        // Tab switching
        // ---------------------------------------------------------------
        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (navButtons == null) return;
            int index = Array.IndexOf(navButtons, sender);
            if (index >= 0) NavigateTo(index);
        }

        private UserControl GetView(int index)
        {
            if (views[index] == null)
            {
                switch (index)
                {
                    case 0: views[index] = new OptimizerView(); break;
                    case 1: views[index] = new WhitelistView(); break;
                    case 2: views[index] = new CleanerView(); break;
                    case 3: views[index] = new MonitorView(); break;
                    case 4: views[index] = new RamLimiterView(); break;
                    default: views[index] = new SettingsView(); break;
                }
            }
            return views[index];
        }

        private void NavigateTo(int index)
        {
            if (index == current) return;

            int token = ++navToken;
            int previous = current;
            current = index;

            UserControl next = GetView(index);

            // Slide the highlight pill to the new tab
            if (previous < 0)
            {
                PillShift.Y = index * NavPitch;
            }
            else
            {
                var move = new DoubleAnimation(index * NavPitch, TimeSpan.FromMilliseconds(420))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }
                };
                PillShift.BeginAnimation(TranslateTransform.YProperty, move);
            }

            // First page: just fade in. Later pages: fade the old one out, then bring the new one in.
            if (previous < 0)
            {
                Host.Content = next;
                PlayIn();
                return;
            }

            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (s, args) =>
            {
                if (token != navToken) return;   // user clicked another tab meanwhile
                Host.Content = next;
                PlayIn();
            };
            Host.BeginAnimation(OpacityProperty, fadeOut);
        }

        private void PlayIn()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Host.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
            HostShift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
        }
    }
}

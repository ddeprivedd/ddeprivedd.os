using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ddeprivedd.os.Views
{
    public partial class WhitelistView : UserControl
    {
        private sealed class AppInfo
        {
            public string Name;
            public string Title;
        }

        // Toggle buttons in the "Open right now" list, by process name
        private readonly Dictionary<string, Button> runningButtons =
            new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);

        public WhitelistView()
        {
            InitializeComponent();

            Loaded += async (s, e) =>
            {
                RefreshProtected();
                Anim.Stagger(PageRoot);
                await LoadRunningAsync();
            };
        }

        // ---------------------------------------------------------------
        // Add by name
        // ---------------------------------------------------------------
        private void NameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddFromBox();
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            AddFromBox();
        }

        private void AddFromBox()
        {
            string name = Whitelist.Normalize(NameBox.Text);

            switch (Whitelist.Add(NameBox.Text))
            {
                case AddResult.Added:
                    NameBox.Clear();
                    ShowHint("Added " + name + " to the whitelist.", "GoodBrush");
                    RefreshProtected();
                    SyncRunningButtons();
                    break;
                case AddResult.AlreadyExists:
                    ShowHint(name + " is already on the whitelist.", "WarnBrush");
                    break;
                default:
                    ShowHint("Enter a valid process name, like chrome.", "BadBrush");
                    break;
            }
        }

        private void ShowHint(string message, string brushKey)
        {
            Hint.Text = message;
            Hint.Foreground = (Brush)Application.Current.Resources[brushKey];
            Anim.Flash(Hint);
        }

        // ---------------------------------------------------------------
        // Open right now
        // ---------------------------------------------------------------
        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            Anim.Spin(RefreshBtn);
            await LoadRunningAsync();
        }

        private async Task LoadRunningAsync()
        {
            List<AppInfo> apps = await Task.Run(() => GetRunningApps());

            RunningList.Children.Clear();
            runningButtons.Clear();

            if (apps.Count == 0)
            {
                RunningList.Children.Add(new TextBlock
                {
                    Text = "No open apps found.",
                    Foreground = (Brush)Application.Current.Resources["MutedBrush"],
                    Margin = new Thickness(4, 8, 0, 0)
                });
                return;
            }

            foreach (AppInfo app in apps)
                RunningList.Children.Add(BuildRunningRow(app));

            Anim.Stagger(RunningList, 25, 0, 10);
        }

        private static List<AppInfo> GetRunningApps()
        {
            var found = new Dictionary<string, AppInfo>(StringComparer.OrdinalIgnoreCase);
            string self;
            using (Process me = Process.GetCurrentProcess()) { self = me.ProcessName; }

            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero) continue;

                    string title = p.MainWindowTitle;
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    string name = p.ProcessName;
                    if (name.Equals(self, StringComparison.OrdinalIgnoreCase)) continue;

                    if (!found.ContainsKey(name))
                        found[name] = new AppInfo { Name = name, Title = title };
                }
                catch { /* process exited or access denied */ }
                finally { p.Dispose(); }
            }

            return found.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private UIElement BuildRunningRow(AppInfo app)
        {
            var row = new Border
            {
                Background = Hex("#FF0A0E26"),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(BuildTile(app.Name.Substring(0, 1).ToUpperInvariant(), false));

            var texts = new StackPanel { Margin = new Thickness(14, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock
            {
                Text = app.Name,
                FontSize = 14.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextBrush"]
            });
            texts.Children.Add(new TextBlock
            {
                Text = app.Title,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (Brush)Application.Current.Resources["MutedBrush"]
            });
            Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);

            var toggle = new Button
            {
                Style = (Style)Application.Current.Resources["PrimaryButton"],
                Height = 34,
                MinWidth = 100,
                FontSize = 13
            };
            StyleToggle(toggle, Whitelist.IsProtected(app.Name));

            string name = app.Name;
            toggle.Click += (s, e) =>
            {
                if (Whitelist.IsProtected(name)) Whitelist.Remove(name);
                else Whitelist.Add(name);

                StyleToggle(toggle, Whitelist.IsProtected(name));
                RefreshProtected();
            };
            runningButtons[name] = toggle;

            Grid.SetColumn(toggle, 2);
            grid.Children.Add(toggle);

            row.Child = grid;
            return row;
        }

        private static void StyleToggle(Button button, bool isProtected)
        {
            button.Content = isProtected ? "Protected" : "Protect";
            button.Background = isProtected ? Hex("#2234D399") : (Brush)Application.Current.Resources["RaisedBrush"];
            button.Foreground = isProtected
                ? (Brush)Application.Current.Resources["GoodBrush"]
                : (Brush)Application.Current.Resources["TextBrush"];
        }

        private void SyncRunningButtons()
        {
            foreach (KeyValuePair<string, Button> pair in runningButtons)
                StyleToggle(pair.Value, Whitelist.IsProtected(pair.Key));
        }

        // ---------------------------------------------------------------
        // Protected apps
        // ---------------------------------------------------------------
        private void RefreshProtected()
        {
            List<string> all = Whitelist.GetAll();

            ProtectedList.Children.Clear();
            CountText.Text = all.Count.ToString();

            if (all.Count == 0)
            {
                ProtectedList.Children.Add(new Border
                {
                    Background = Hex("#FF0A0E26"),
                    CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(18),
                    Child = new TextBlock
                    {
                        Text = "No apps protected yet. Add one above, or pick one from the open apps.",
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)Application.Current.Resources["MutedBrush"]
                    }
                });
                return;
            }

            foreach (string name in all)
                ProtectedList.Children.Add(BuildProtectedRow(name));

            Anim.Stagger(ProtectedList, 35, 0, 10);
        }

        private UIElement BuildProtectedRow(string name)
        {
            var row = new Border
            {
                Background = Hex("#FF0A0E26"),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 9, 8, 9),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(BuildTile("\uE72E", true));

            var label = new TextBlock
            {
                Text = name,
                FontSize = 14.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(14, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["TextBrush"]
            };
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);

            var remove = new Button
            {
                Style = (Style)Application.Current.Resources["IconButton"],
                Content = "\uE711",
                Tag = Application.Current.Resources["HoverRedBrush"],
                ToolTip = "Remove from whitelist"
            };
            remove.Click += (s, e) =>
            {
                Anim.FadeOutThen(row, () =>
                {
                    Whitelist.Remove(name);
                    RefreshProtected();
                    SyncRunningButtons();
                });
            };
            Grid.SetColumn(remove, 2);
            grid.Children.Add(remove);

            row.Child = grid;
            return row;
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------
        private static Border BuildTile(string text, bool isIcon)
        {
            var label = new TextBlock
            {
                Text = text,
                FontSize = isIcon ? 15 : 16,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.Resources["AccentSoftBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (isIcon) label.FontFamily = (FontFamily)Application.Current.Resources["IconFont"];

            return new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(12),
                Background = Hex("#338B5CF6"),
                Child = label
            };
        }

        private static Brush Hex(string color)
        {
            return (Brush)new BrushConverter().ConvertFromString(color);
        }
    }
}

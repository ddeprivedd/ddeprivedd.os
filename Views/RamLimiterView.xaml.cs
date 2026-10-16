using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.VisualBasic.Devices;

namespace ddeprivedd.os.Views
{
    public partial class RamLimiterView : UserControl
    {
        // The cells of one row, kept so the numbers can be changed in place instead of redrawing the list
        private sealed class RowUi
        {
            public Border Root;
            public Ellipse Dot;
            public TextBlock State, Procs, Memory, Freed;
            public bool HasState;
            public LimitState LastState;
        }

        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly ComputerInfo info = new ComputerInfo();
        private readonly Dictionary<string, RowUi> rows =
            new Dictionary<string, RowUi>(StringComparer.OrdinalIgnoreCase);

        public RamLimiterView()
        {
            InitializeComponent();

            IntervalText.Text = "Each app is trimmed every " + (RamLimiterEngine.IntervalMs / 1000) +
                                " seconds. Every open process with that name is included.";

            AdminBanner.Visibility = RamLimiterEngine.IsAdmin ? Visibility.Collapsed : Visibility.Visible;

            timer.Tick += (s, e) => Refresh();

            // Only poll while this page is on screen. The limiting itself keeps running in the background.
            Loaded += (s, e) =>
            {
                Refresh();
                Anim.Stagger(PageRoot);
                timer.Start();
            };
            Unloaded += (s, e) => timer.Stop();
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
            string name;
            List<string> suggestions;

            switch (RamLimiterEngine.Add(NameBox.Text, out name, out suggestions))
            {
                case LimitAddResult.Added:
                    NameBox.Clear();
                    ShowHint("Now limiting " + name + ".", "GoodBrush");
                    Refresh();
                    break;
                case LimitAddResult.AlreadyLimited:
                    ShowHint(name + " is already being limited.", "WarnBrush");
                    break;
                case LimitAddResult.AddedNotRunning:
                    NameBox.Clear();
                    string message = name + " added. It isn't open right now, so it will be limited as soon as it starts.";
                    if (suggestions.Count > 0)
                        message += " Did you mean " + string.Join(", ", suggestions) + "?";
                    ShowHint(message, "InfoBrush");
                    Refresh();
                    break;
                default:
                    ShowHint("Enter a process name, like discord.", "BadBrush");
                    break;
            }
        }

        // Relaunches the app elevated (Windows shows the usual UAC prompt), then closes this copy.
        private void Elevate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Assembly.GetEntryAssembly().Location,
                    Arguments = "--restarted",   // tells the new copy to wait for this one to close
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = Environment.CurrentDirectory
                };
                Process.Start(psi);
                Application.Current.Shutdown();
            }
            catch
            {
                // The user pressed No on the UAC prompt, or Windows blocked it.
                ShowHint("Could not restart as administrator.", "BadBrush");
            }
        }

        private void ShowHint(string message, string brushKey)
        {
            Hint.Text = message;
            Hint.Foreground = (Brush)Application.Current.Resources[brushKey];
            Anim.Flash(Hint);
        }

        // ---------------------------------------------------------------
        // Live update (changes text in place, like Task Manager)
        // ---------------------------------------------------------------
        private void Refresh()
        {
            UpdateSystemMemory();

            List<LimitSnapshot> snapshots = RamLimiterEngine.GetSnapshots();
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Add rows for new apps (the list is sorted by name, so insert at the matching position)
            for (int i = 0; i < snapshots.Count; i++)
            {
                LimitSnapshot snap = snapshots[i];
                present.Add(snap.Name);

                RowUi row;
                if (!rows.TryGetValue(snap.Name, out row))
                {
                    row = BuildRow(snap.Name);
                    rows[snap.Name] = row;
                    LimitList.Children.Insert(Math.Min(i, LimitList.Children.Count), row.Root);
                    Anim.FadeUp(row.Root, 0, 10, 260);
                }
                UpdateRow(row, snap);
            }

            // Drop rows for apps that were removed
            var gone = new List<string>();
            foreach (string name in rows.Keys)
                if (!present.Contains(name)) gone.Add(name);
            foreach (string name in gone)
            {
                LimitList.Children.Remove(rows[name].Root);
                rows.Remove(name);
            }

            CountText.Text = snapshots.Count.ToString();
            EmptyBox.Visibility = snapshots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            HeaderRow.Visibility = snapshots.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateSystemMemory()
        {
            double usedGb = 0, totalGb = 0, percent = 0;
            try
            {
                ulong total = info.TotalPhysicalMemory;
                ulong available = info.AvailablePhysicalMemory;
                ulong used = total > available ? total - available : 0;
                usedGb = used / 1073741824.0;
                totalGb = total / 1073741824.0;
                percent = total > 0 ? used * 100.0 / total : 0;
            }
            catch { }

            SystemText.Text = usedGb.ToString("0.0") + " of " + totalGb.ToString("0.0") + " GB  (" + ((int)percent) + "%)";
            Anim.SetProgress(SystemBar, percent);
        }

        private static void UpdateRow(RowUi row, LimitSnapshot snap)
        {
            if (!row.HasState || row.LastState != snap.State)
            {
                string brushKey;
                string text;
                switch (snap.State)
                {
                    case LimitState.Limiting: brushKey = "GoodBrush"; text = "Limiting"; break;
                    case LimitState.AccessDenied:
                        brushKey = "WarnBrush";
                        // Already elevated and still refused: the app protects itself (anti-cheat, system process)
                        text = RamLimiterEngine.IsAdmin ? "Blocked" : "Needs admin";
                        break;
                    default: brushKey = "MutedBrush"; text = "Not running"; break;
                }

                var brush = (Brush)Application.Current.Resources[brushKey];
                row.Dot.Fill = brush;
                row.State.Foreground = brush;
                row.State.Text = text;
                row.LastState = snap.State;
                row.HasState = true;
            }

            bool running = snap.State != LimitState.Waiting;
            SetText(row.Procs, running ? snap.Instances.ToString() : "-");
            SetText(row.Memory, running ? FormatBytes(snap.WorkingSetBytes) : "-");
            SetText(row.Freed, running && snap.LastFreedBytes > 0 ? FormatBytes(snap.LastFreedBytes) : "-");
        }

        private static void SetText(TextBlock block, string text)
        {
            if (block.Text != text) block.Text = text;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 MB";
            double mb = bytes / 1048576.0;
            if (mb >= 1024) return (mb / 1024.0).ToString("0.0") + " GB";
            return mb.ToString("0") + " MB";
        }

        // ---------------------------------------------------------------
        // Row layout (columns match the header in the XAML)
        // ---------------------------------------------------------------
        private RowUi BuildRow(string name)
        {
            var ui = new RowUi();

            ui.Root = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString("#FF0A0E26"),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 9, 8, 9),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });

            // Name
            var nameText = new TextBlock
            {
                Text = name,
                FontSize = 14.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 10, 0),
                Foreground = (Brush)Application.Current.Resources["TextBrush"]
            };
            grid.Children.Add(nameText);

            // Status (dot + text)
            ui.Dot = new Ellipse
            {
                Width = 8,
                Height = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = (Brush)Application.Current.Resources["MutedBrush"]
            };
            ui.State = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var status = new StackPanel { Orientation = Orientation.Horizontal };
            status.Children.Add(ui.Dot);
            status.Children.Add(ui.State);
            Grid.SetColumn(status, 1);
            grid.Children.Add(status);

            // Numbers
            ui.Procs = NumberCell(2);
            ui.Memory = NumberCell(3);
            ui.Freed = NumberCell(4);
            grid.Children.Add(ui.Procs);
            grid.Children.Add(ui.Memory);
            grid.Children.Add(ui.Freed);

            // Remove button
            var remove = new Button
            {
                Style = (Style)Application.Current.Resources["IconButton"],
                Content = "\uE711",
                Tag = Application.Current.Resources["HoverRedBrush"],
                ToolTip = "Stop limiting " + name
            };
            remove.Click += (s, e) =>
            {
                Anim.FadeOutThen(ui.Root, () =>
                {
                    RamLimiterEngine.Remove(name);
                    Refresh();
                });
            };
            Grid.SetColumn(remove, 5);
            grid.Children.Add(remove);

            ui.Root.Child = grid;
            return ui;
        }

        private static TextBlock NumberCell(int column)
        {
            var block = new TextBlock
            {
                Text = "-",
                FontSize = 13.5,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["TextBrush"]
            };
            Grid.SetColumn(block, column);
            return block;
        }
    }
}

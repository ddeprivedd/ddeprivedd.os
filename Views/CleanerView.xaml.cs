using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ddeprivedd.os.Views
{
    public partial class CleanerView : UserControl
    {
        private bool busy;

        public CleanerView()
        {
            InitializeComponent();
            Loaded += (s, e) => Anim.Stagger(PageRoot);
        }

        private async void TempClean_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync(p => CleanerEngine.CleanTempAsync(p));
        }

        private async void RecycleClean_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync(p => CleanerEngine.EmptyRecycleBinAsync(p));
        }

        private async Task RunAsync(Func<IProgress<StatusUpdate>, Task> job)
        {
            if (busy) return;
            busy = true;
            TempBtn.IsEnabled = false;
            BinBtn.IsEnabled = false;

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
                Log.Add("Cleanup failed: " + ex.Message, LogKind.Error);
            }
            finally
            {
                busy = false;
                TempBtn.IsEnabled = true;
                BinBtn.IsEnabled = true;
            }
        }
    }
}

using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ddeprivedd.os
{
    public partial class App : Application
    {
        // Names shared by every copy of the app. They make sure only one copy can run at a time.
        private const string MutexName = "ddeprivedd.os.SingleInstance";
        private const string ShowEventName = "ddeprivedd.os.ShowWindow";

        private Mutex instanceMutex;
        private EventWaitHandle showEvent;
        private bool ownsMutex;
        private volatile bool exiting;
        private MainWindow mainWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnUnhandledException;

            // Already open? Then bring that copy to the front and quit this one.
            if (!AcquireSingleInstance(e.Args))
            {
                Shutdown();
                return;
            }

            AppSettings.Load();

            // Install a downloaded update / undo a broken one. If the app is relaunching, stop here.
            if (Updater.HandleStartup(e.Args))
            {
                Shutdown();
                return;
            }

            Whitelist.Load();
            RamLimiterEngine.Load();

            mainWindow = new MainWindow();
            mainWindow.Show();

            // Started by the updater while the window was hidden in the tray: go back to the tray.
            if (Array.IndexOf(e.Args, "--tray") >= 0) mainWindow.MinimizeToTray();

            // First run of a freshly installed update: if it is still alive after 20 s, it is considered good.
            if (Updater.StartedAfterUpdate)
            {
                var confirm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
                confirm.Tick += (s, args) => { confirm.Stop(); Updater.ConfirmGood(); };
                confirm.Start();
            }

            // Look for updates now and then (only when "Install updates automatically" is on).
            Updater.StartBackground(
                () => Dispatcher.Invoke<bool>(() => mainWindow == null || !mainWindow.IsVisible),
                () => Dispatcher.BeginInvoke(new Action(() => Shutdown())));
        }

        /// <summary>True if this is the only copy. Otherwise the running copy is asked to show itself.</summary>
        private bool AcquireSingleInstance(string[] args)
        {
            bool restarted = Array.IndexOf(args, "--restarted") >= 0;

            try
            {
                bool createdNew;
                instanceMutex = new Mutex(true, MutexName, out createdNew);
                ownsMutex = createdNew;

                if (!createdNew && restarted)
                {
                    // The other copy launched us ("Restart as admin") and is closing right now. Give it a moment.
                    try { ownsMutex = instanceMutex.WaitOne(5000); }
                    catch (AbandonedMutexException) { ownsMutex = true; }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // The running copy was started as administrator, so a normal copy is not allowed to talk to it.
                MessageBox.Show("ddeprivedd.os is already running (as administrator).\nClose it first, then open it again.",
                    "ddeprivedd.os", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (!ownsMutex)
            {
                try
                {
                    using (EventWaitHandle other = EventWaitHandle.OpenExisting(ShowEventName))
                        other.Set();
                }
                catch { /* nothing to signal: just quit */ }
                return false;
            }

            // First copy: listen for "show yourself" requests from later launches.
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            var listener = new Thread(() =>
            {
                while (true)
                {
                    try { showEvent.WaitOne(); }
                    catch { return; }

                    if (exiting) return;

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (mainWindow != null) mainWindow.BringToFront();
                    }));
                }
            })
            { IsBackground = true, Name = "ddeprivedd.os show listener" };
            listener.Start();

            return true;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            exiting = true;
            RamLimiterEngine.StopAll();
            if (Updater.StartedAfterUpdate) Updater.ConfirmGood();   // clean exit = the new version works

            if (ownsMutex && instanceMutex != null)
            {
                try { instanceMutex.ReleaseMutex(); } catch { }
            }

            base.OnExit(e);
        }

        private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(e.Exception.Message, "ddeprivedd.os", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }
    }
}

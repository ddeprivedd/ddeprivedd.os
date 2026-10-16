using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ddeprivedd.os
{
    /// <summary>All optimizer logic. Runs on a background thread and reports progress; it never touches the UI.</summary>
    public static class OptimizerEngine
    {
        /// <summary>
        /// Background apps the optimizer force-closes (process names, no ".exe").
        /// Anything on the Whitelist is skipped. Edit this list to change what gets closed.
        /// </summary>
        public static readonly string[] DefaultCloseList =
            { "Discord", "Spotify", "Overwolf", "Voicemod", "opera", "chrome", "brave", "msedge" };

        public static Task RunGameAsync(string game, IProgress<StatusUpdate> progress)
        {
            return Task.Run(() => RunGame(game, progress));
        }

        public static Task RunAmdAsync(IProgress<StatusUpdate> progress)
        {
            return Task.Run(() => RunAmd(progress));
        }

        // ---------------------------------------------------------------
        // VALORANT / ROBLOX
        // ---------------------------------------------------------------
        private static void RunGame(string game, IProgress<StatusUpdate> p)
        {
            StatusUpdate.Send(p, 3, "Starting " + game + " optimization...");
            Pace();

            StatusUpdate.Send(p, 8, "Closing background apps...");
            CloseBackgroundApps(p);
            StatusUpdate.Send(p, 20, null);

            StatusUpdate.Send(p, 22, "Cleaning temp files...");
            ShellHelper.RunHidden("cmd.exe", "/c del /q/f/s %temp%\\*");
            StatusUpdate.Send(p, 35, null);

            StatusUpdate.Send(p, 37, "Cleaning DirectX shader cache...");
            ShellHelper.RunHidden("cmd.exe", "/c del /q/f/s \"%localappdata%\\D3DSCache\\*\"");
            StatusUpdate.Send(p, 50, null);

            StatusUpdate.Send(p, 52, "Flushing DNS cache...");
            ShellHelper.RunHidden("cmd.exe", "/c ipconfig /flushdns");
            StatusUpdate.Send(p, 60, null);

            StatusUpdate.Send(p, 62, "Cleaning recycle bin...");
            ShellHelper.RunHidden("cmd.exe", "/c PowerShell.exe -NoProfile -Command Clear-RecycleBin -Force");
            StatusUpdate.Send(p, 70, null);

            StatusUpdate.Send(p, 72, "Enabling High Performance mode...");
            ShellHelper.RunHidden("cmd.exe", "/c powercfg -setactive SCHEME_MIN");
            StatusUpdate.Send(p, 80, null);

            StatusUpdate.Send(p, 82, "Enabling Windows Game Mode...");
            ShellHelper.RunHidden("reg.exe", "add HKCU\\Software\\Microsoft\\GameBar /v AllowAutoGameMode /t REG_DWORD /d 1 /f");
            StatusUpdate.Send(p, 90, null);

            StatusUpdate.Send(p, 92, "Applying FPS boost...");
            ApplyFpsBoost();
            StatusUpdate.Send(p, 96, null);

            StatusUpdate.Send(p, 97, "Reducing memory usage...");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Pace();

            StatusUpdate.Send(p, 100, game + " optimization completed.", LogKind.Success);
        }

        // ---------------------------------------------------------------
        // AMD
        // ---------------------------------------------------------------
        private static void RunAmd(IProgress<StatusUpdate> p)
        {
            StatusUpdate.Send(p, 5, "Starting AMD optimization...");
            Pace();

            StatusUpdate.Send(p, 12, "Cleaning AMD shader cache...");
            ShellHelper.RunHidden("cmd.exe", "/c del /q/f/s \"%localappdata%\\AMD\\DxCache\\*\"");
            StatusUpdate.Send(p, 35, null);

            StatusUpdate.Send(p, 38, "Cleaning DirectX cache...");
            ShellHelper.RunHidden("cmd.exe", "/c del /q/f/s \"%localappdata%\\D3DSCache\\*\"");
            StatusUpdate.Send(p, 60, null);

            StatusUpdate.Send(p, 62, "Enabling High Performance mode...");
            ShellHelper.RunHidden("cmd.exe", "/c powercfg -setactive SCHEME_MIN");
            StatusUpdate.Send(p, 80, null);

            StatusUpdate.Send(p, 82, "Applying AMD FPS tweaks...");
            ApplyFpsBoost();
            Pace();

            StatusUpdate.Send(p, 100, "AMD optimization completed.", LogKind.Success);
        }

        // ---------------------------------------------------------------
        // Closing background apps (respects the whitelist)
        // ---------------------------------------------------------------
        private static void CloseBackgroundApps(IProgress<StatusUpdate> p)
        {
            int closed = 0;

            foreach (string app in DefaultCloseList)
            {
                Process[] running;
                try { running = Process.GetProcessesByName(app); }
                catch { continue; }

                if (running.Length == 0) continue;

                if (Whitelist.IsProtected(app))
                {
                    StatusUpdate.Send(p, -1, "Kept " + app + " running (whitelisted).", LogKind.Protected);
                    foreach (Process r in running) r.Dispose();
                    continue;
                }

                foreach (Process proc in running)
                {
                    try { proc.Kill(); closed++; }
                    catch { /* already closed or access denied */ }
                    finally { proc.Dispose(); }
                }
            }

            StatusUpdate.Send(p, -1, closed + " background process(es) closed.", closed > 0 ? LogKind.Success : LogKind.Info);
            Pace();
        }

        private static void ApplyFpsBoost()
        {
            // Disable Game DVR
            ShellHelper.RunHidden("reg.exe", "add HKCU\\System\\GameConfigStore /v GameDVR_Enabled /t REG_DWORD /d 0 /f");
            // Disable network throttling (needs admin: writes to HKLM, silently fails otherwise)
            ShellHelper.RunHidden("reg.exe", "add \"HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile\" /v NetworkThrottlingIndex /t REG_DWORD /d 4294967295 /f");
        }

        // Tiny pause so each step is visible in the live log.
        private static void Pace()
        {
            Thread.Sleep(140);
        }
    }
}

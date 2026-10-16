using System;
using System.IO;

namespace ddeprivedd.os
{
    /// <summary>
    /// Simple settings store: one bool per line in settings.ini (inside %AppData%\ddeprivedd.os).
    /// NOTE: as in the original build, these values are saved/loaded but nothing else reads them yet.
    /// </summary>
    public static class AppSettings
    {
        public static bool StartupCleanup;
        public static bool LaunchOnStartup;
        public static bool AutoTempClean;
        public static bool OptimizationTweaks;
        public static bool ReduceBackgroundUsage;
        public static bool AutoRamCleanup;
        public static bool PerformanceBoost;
        public static bool FPSBoost;
        public static bool GameMode;
        public static bool CpuPriority;

        /// <summary>Check for, download and install updates automatically (see Core/Updater.cs).</summary>
        public static bool AutoUpdate = true;

        private static string FilePath { get { return AppPaths.GetFile("settings.ini"); } }

        public static void Save()
        {
            try
            {
                var lines = new[]
                {
                    StartupCleanup.ToString(),
                    LaunchOnStartup.ToString(),
                    AutoTempClean.ToString(),
                    OptimizationTweaks.ToString(),
                    ReduceBackgroundUsage.ToString(),
                    AutoRamCleanup.ToString(),
                    PerformanceBoost.ToString(),
                    FPSBoost.ToString(),
                    GameMode.ToString(),
                    CpuPriority.ToString(),
                    AutoUpdate.ToString(),
                };
                File.WriteAllLines(FilePath, lines);
            }
            catch { /* could not write: ignore */ }
        }

        public static void Load()
        {
            try
            {
                string path = FilePath;
                if (!File.Exists(path))
                {
                    // Older versions saved settings.ini next to the exe. Pick that up once.
                    if (File.Exists("settings.ini")) path = "settings.ini";
                    else return;
                }

                string[] lines = File.ReadAllLines(path);
                if (lines.Length < 10) return;

                StartupCleanup        = Parse(lines[0]);
                LaunchOnStartup       = Parse(lines[1]);
                AutoTempClean         = Parse(lines[2]);
                OptimizationTweaks    = Parse(lines[3]);
                ReduceBackgroundUsage = Parse(lines[4]);
                AutoRamCleanup        = Parse(lines[5]);
                PerformanceBoost      = Parse(lines[6]);
                FPSBoost              = Parse(lines[7]);
                GameMode              = Parse(lines[8]);
                CpuPriority           = Parse(lines[9]);
                if (lines.Length >= 11) AutoUpdate = Parse(lines[10]);   // older files have 10 lines: keep default (on)
            }
            catch { /* unreadable file: keep defaults */ }
        }

        private static bool Parse(string text)
        {
            bool value;
            return bool.TryParse(text, out value) && value;
        }
    }
}

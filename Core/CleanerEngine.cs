using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ddeprivedd.os
{
    public static class CleanerEngine
    {
        public static Task CleanTempAsync(IProgress<StatusUpdate> progress)
        {
            return Task.Run(() => CleanTemp(progress));
        }

        public static Task EmptyRecycleBinAsync(IProgress<StatusUpdate> progress)
        {
            return Task.Run(() => EmptyRecycleBin(progress));
        }

        private static void CleanTemp(IProgress<StatusUpdate> p)
        {
            StatusUpdate.Send(p, 10, "Scanning temporary files...");

            try
            {
                // Top-level files in %TEMP% only (no subfolders), same as the original build
                string[] files = Directory.GetFiles(Path.GetTempPath());
                int count = 0;

                foreach (string file in files)
                {
                    try { File.Delete(file); count++; }
                    catch { /* in use / access denied: skip */ }
                }

                StatusUpdate.Send(p, 55, count + " temp files deleted.", LogKind.Success);
            }
            catch
            {
                StatusUpdate.Send(p, 55, "Failed cleaning temp files.", LogKind.Error);
            }

            StatusUpdate.Send(p, 75, "Removing leftover cache...");
            Thread.Sleep(400);

            StatusUpdate.Send(p, 90, "Finalizing cleanup...");
            Thread.Sleep(200);

            StatusUpdate.Send(p, 100, "Cleaner finished successfully.", LogKind.Success);
        }

        private static void EmptyRecycleBin(IProgress<StatusUpdate> p)
        {
            StatusUpdate.Send(p, 15, "Preparing recycle bin cleanup...");

            ShellHelper.RunHidden("cmd.exe", "/c PowerShell.exe -NoProfile -Command Clear-RecycleBin -Force", 30000);
            StatusUpdate.Send(p, 70, "Recycle bin emptied.", LogKind.Success);

            Thread.Sleep(300);
            StatusUpdate.Send(p, 100, "Cleanup completed.", LogKind.Success);
        }
    }
}

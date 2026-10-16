using System.Diagnostics;

namespace ddeprivedd.os
{
    public static class ShellHelper
    {
        /// <summary>Runs a hidden process and waits for it (up to timeoutMs). Never throws.</summary>
        public static void RunHidden(string fileName, string arguments, int timeoutMs = 25000)
        {
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };

                using (Process p = Process.Start(info))
                {
                    if (p != null && !p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                    }
                }
            }
            catch { /* command missing or blocked: skip */ }
        }
    }
}

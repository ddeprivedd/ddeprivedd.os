using System;
using System.IO;

namespace ddeprivedd.os
{
    /// <summary>Where the app keeps its files (settings.ini, whitelist.txt, ramlimiter.txt): %AppData%\ddeprivedd.os</summary>
    public static class AppPaths
    {
        public static readonly string Folder = Init();

        private static string Init()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(root, "ddeprivedd.os");
            string oldFolder = Path.Combine(root, "PeasureBoosterX");

            // One-time import of the settings and whitelist saved by the old "Peasure BoosterX" build.
            try
            {
                if (!Directory.Exists(folder) && Directory.Exists(oldFolder))
                {
                    Directory.CreateDirectory(folder);
                    foreach (string file in Directory.GetFiles(oldFolder))
                        File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), false);
                }
            }
            catch { /* could not import: start fresh */ }

            return folder;
        }

        public static string GetFile(string name)
        {
            try { Directory.CreateDirectory(Folder); } catch { }
            return Path.Combine(Folder, name);
        }
    }
}

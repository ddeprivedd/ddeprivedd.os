using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ddeprivedd.os
{
    public enum AddResult { Added, AlreadyExists, Invalid }

    /// <summary>
    /// Apps the optimizer must never force-close. Stored by process name (no ".exe"),
    /// compared case-insensitively, saved one per line in %AppData%\ddeprivedd.os\whitelist.txt
    /// </summary>
    public static class Whitelist
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<string> Items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static string FilePath { get { return AppPaths.GetFile("whitelist.txt"); } }

        /// <summary>Turns "C:\Program Files\Google\Chrome\chrome.exe", "Chrome.EXE" or "chrome" into "chrome". Null if invalid.</summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            string name = raw.Trim().Trim('"').Trim();
            try { name = Path.GetFileName(name); } catch { /* keep as typed */ }
            if (string.IsNullOrEmpty(name)) return null;

            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);

            name = name.Trim();
            if (name.Length == 0) return null;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            return name;
        }

        public static bool IsProtected(string processName)
        {
            string name = Normalize(processName);
            if (name == null) return false;
            lock (Gate) { return Items.Contains(name); }
        }

        public static AddResult Add(string raw)
        {
            string name = Normalize(raw);
            if (name == null) return AddResult.Invalid;

            lock (Gate)
            {
                if (!Items.Add(name)) return AddResult.AlreadyExists;
            }
            Save();
            return AddResult.Added;
        }

        public static void Remove(string raw)
        {
            string name = Normalize(raw);
            if (name == null) return;

            bool removed;
            lock (Gate) { removed = Items.Remove(name); }
            if (removed) Save();
        }

        public static List<string> GetAll()
        {
            lock (Gate)
            {
                return Items.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        public static void Load()
        {
            try
            {
                string path = FilePath;
                if (!File.Exists(path)) return;

                lock (Gate)
                {
                    Items.Clear();
                    foreach (string line in File.ReadAllLines(path))
                    {
                        string name = Normalize(line);
                        if (name != null) Items.Add(name);
                    }
                }
            }
            catch { /* unreadable file: start with an empty list */ }
        }

        private static void Save()
        {
            try
            {
                List<string> all = GetAll();
                File.WriteAllLines(FilePath, all);
            }
            catch { /* could not write: ignore */ }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace ddeprivedd.os
{
    public enum LimitState { Waiting, Limiting, AccessDenied }

    public enum LimitAddResult { Added, AddedNotRunning, AlreadyLimited, Invalid }

    /// <summary>A read-only picture of one limited app, taken by the background monitor.</summary>
    public sealed class LimitSnapshot
    {
        public string Name;
        public LimitState State;
        public int Instances;          // how many processes with this name are running
        public long WorkingSetBytes;   // their combined RAM after the last trim
        public long LastFreedBytes;    // how much the last trim released
    }

    /// <summary>
    /// The RAM limiter. Every app on the list gets a background monitor that trims the working set
    /// of ALL running processes with that name (browsers and Electron apps run many) every few seconds,
    /// using the documented EmptyWorkingSet call. Nothing is printed anywhere: the UI just reads snapshots.
    /// The list is saved to %AppData%\ddeprivedd.os\ramlimiter.txt and restarted on the next launch.
    /// </summary>
    public static class RamLimiterEngine
    {
        public const int IntervalMs = 5000;

        // Processes using less than this are left alone (trimming them frees nothing worth the CPU).
        private const long MinTrimBytes = 16L * 1024 * 1024;

        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint PROCESS_SET_QUOTA = 0x0100;

        /// <summary>True when the app runs elevated. Many games and launchers can only be trimmed from an elevated app.</summary>
        public static readonly bool IsAdmin = DetectAdmin();

        private static bool DetectAdmin()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        // Opens the process with just the rights trimming needs. Tries the usual rights first,
        // then the "limited" set, which some protected processes still allow.
        private static IntPtr OpenForTrim(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_SET_QUOTA, false, pid);
            if (h == IntPtr.Zero)
                h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SET_QUOTA, false, pid);
            return h;
        }

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        private sealed class Watcher
        {
            public string Name;
            public CancellationTokenSource Cts = new CancellationTokenSource();
            public volatile LimitSnapshot Snap;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Watcher> Watchers =
            new Dictionary<string, Watcher>(StringComparer.OrdinalIgnoreCase);

        private static string FilePath { get { return AppPaths.GetFile("ramlimiter.txt"); } }

        // ---------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------

        // Lower-case letters and digits only, so "Rocket League", "rocket_league" and "RocketLeague" all compare equal.
        private static string Squash(string text)
        {
            return new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }

        /// <summary>
        /// Looks through everything that is running for the typed name. Returns the real process name
        /// (exact match first, then a match that ignores spaces and punctuation) or null when nothing fits.
        /// Close matches are returned in suggestions so the UI can say "Did you mean ...?".
        /// </summary>
        private static string FindProcessName(string typed, out List<string> suggestions)
        {
            suggestions = new List<string>();

            string want = Squash(typed);
            string exact = null, loose = null;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Process[] all;
            try { all = Process.GetProcesses(); }
            catch { return null; }

            foreach (Process p in all)
            {
                try
                {
                    string n = p.ProcessName;
                    if (!seen.Add(n)) continue;

                    string squashed = Squash(n);
                    if (string.Equals(n, typed, StringComparison.OrdinalIgnoreCase))
                        exact = n;
                    else if (want.Length > 0 && squashed == want)
                    {
                        if (loose == null) loose = n;
                    }
                    else if (want.Length >= 3 && squashed.Length >= 3 &&
                             (squashed.Contains(want) || want.Contains(squashed)))
                        suggestions.Add(n);
                }
                catch { /* the process exited or cannot be read */ }
                finally { p.Dispose(); }
            }

            suggestions = suggestions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(3).ToList();
            return exact ?? loose;
        }

        /// <summary>
        /// Adds an app by name (chrome, chrome.exe or a full path all work). The app does not have to be open:
        /// if it is not running, AddedNotRunning is returned and limiting starts as soon as it opens.
        /// When it is running, name is its real process name; suggestions lists close matches when it is not.
        /// </summary>
        public static LimitAddResult Add(string raw, out string name, out List<string> suggestions)
        {
            suggestions = new List<string>();
            name = Whitelist.Normalize(raw);
            if (name == null) return LimitAddResult.Invalid;

            string real = FindProcessName(name, out suggestions);
            bool running = real != null;
            if (running) name = real;

            lock (Gate)
            {
                if (Watchers.ContainsKey(name)) return LimitAddResult.AlreadyLimited;
                StartMonitor(name);
            }
            Save();
            return running ? LimitAddResult.Added : LimitAddResult.AddedNotRunning;
        }

        public static void Remove(string raw)
        {
            string name = Whitelist.Normalize(raw);
            if (name == null) return;

            bool removed = false;
            lock (Gate)
            {
                Watcher m;
                if (Watchers.TryGetValue(name, out m))
                {
                    try { m.Cts.Cancel(); } catch { }
                    Watchers.Remove(name);
                    removed = true;
                }
            }
            if (removed) Save();
        }

        public static List<LimitSnapshot> GetSnapshots()
        {
            lock (Gate)
            {
                return Watchers.Values
                    .Select(m => m.Snap)
                    .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        /// <summary>Restarts the monitors saved last time. Apps that are not open yet simply show "Not running".</summary>
        public static void Load()
        {
            try
            {
                string path = FilePath;
                if (!File.Exists(path)) return;

                lock (Gate)
                {
                    foreach (string line in File.ReadAllLines(path))
                    {
                        string name = Whitelist.Normalize(line);
                        if (name != null && !Watchers.ContainsKey(name)) StartMonitor(name);
                    }
                }
            }
            catch { /* unreadable file: start with an empty list */ }
        }

        /// <summary>Stops every monitor (the list on disk is kept).</summary>
        public static void StopAll()
        {
            lock (Gate)
            {
                foreach (Watcher m in Watchers.Values)
                {
                    try { m.Cts.Cancel(); } catch { }
                }
                Watchers.Clear();
            }
        }

        // ---------------------------------------------------------------
        // Monitoring
        // ---------------------------------------------------------------

        // Call with Gate held.
        private static void StartMonitor(string name)
        {
            var m = new Watcher
            {
                Name = name,
                Snap = new LimitSnapshot { Name = name, State = LimitState.Waiting }
            };
            Watchers[name] = m;
            Task.Run(() => RunAsync(m));
        }

        private static async Task RunAsync(Watcher m)
        {
            CancellationToken token = m.Cts.Token;

            while (!token.IsCancellationRequested)
            {
                try { m.Snap = TrimOnce(m.Name); }
                catch { /* keep the last snapshot and try again next round */ }

                try { await Task.Delay(IntervalMs, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        // Every running process whose name matches ignoring spaces, punctuation and capitals.
        private static Process[] FindBySquashedName(string name)
        {
            string want = Squash(name);
            var matches = new List<Process>();
            if (want.Length == 0) return matches.ToArray();

            Process[] all;
            try { all = Process.GetProcesses(); }
            catch { return matches.ToArray(); }

            foreach (Process p in all)
            {
                bool keep = false;
                try { keep = Squash(p.ProcessName) == want; }
                catch { }

                if (keep) matches.Add(p);
                else p.Dispose();
            }
            return matches.ToArray();
        }

        private static LimitSnapshot TrimOnce(string name)
        {
            Process[] procs;
            try { procs = Process.GetProcessesByName(name); }
            catch { procs = new Process[0]; }

            // Nothing with that exact name: the app may have been added as "rocket league" for RocketLeague.
            if (procs.Length == 0) procs = FindBySquashedName(name);

            int count = 0;
            long totalAfter = 0, freed = 0;
            bool denied = false;

            foreach (Process p in procs)
            {
                try
                {
                    long before = p.WorkingSet64;
                    count++;

                    if (before < MinTrimBytes)
                    {
                        totalAfter += before;
                        continue;
                    }

                    // Ask only for the rights trimming needs, so ordinary apps of the same user work without admin.
                    IntPtr handle = OpenForTrim(p.Id);
                    if (handle == IntPtr.Zero)
                    {
                        denied = true;
                        totalAfter += before;
                        continue;
                    }

                    try { EmptyWorkingSet(handle); }
                    finally { CloseHandle(handle); }

                    p.Refresh();
                    long after = p.WorkingSet64;
                    totalAfter += after;
                    if (before > after) freed += before - after;
                }
                catch { /* the process exited while we were looking at it */ }
                finally { p.Dispose(); }
            }

            LimitState state;
            if (count == 0) state = LimitState.Waiting;
            else if (denied && freed == 0) state = LimitState.AccessDenied;
            else state = LimitState.Limiting;

            return new LimitSnapshot
            {
                Name = name,
                State = state,
                Instances = count,
                WorkingSetBytes = totalAfter,
                LastFreedBytes = freed
            };
        }

        // ---------------------------------------------------------------
        // Saving
        // ---------------------------------------------------------------
        private static void Save()
        {
            try
            {
                string[] names;
                lock (Gate)
                {
                    names = Watchers.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
                }
                File.WriteAllLines(FilePath, names);
            }
            catch { /* could not write: ignore */ }
        }
    }
}

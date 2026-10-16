using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ddeprivedd.os
{
    public enum UpdateState { NotConfigured, Idle, Checking, UpToDate, Available, Downloading, Ready, Error }

    internal sealed class UpdateInfo
    {
        public Version Version;
        public string Tag;
        public string ExeUrl;
        public string SigUrl;
    }

    internal sealed class UpdateException : Exception
    {
        public UpdateException(string message) : base(message) { }
    }

    /// <summary>
    /// Self-updater. Checks the latest GitHub release, downloads the exe, verifies its RSA signature,
    /// then swaps it in (the running exe is renamed to .old, the new one copied in, and the app relaunched).
    /// If the new version crashes within 20 seconds of starting, the next launch restores the old one.
    /// </summary>
    public static class Updater
    {
        private const long MaxExeBytes = 120L * 1024 * 1024;
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

        private static int busy;                 // 0 = free, 1 = checking/downloading
        private static UpdateInfo found;         // newest release we know about
        private static int backgroundStarted;

        public static UpdateState State { get; private set; }
        public static string Message { get; private set; }
        public static int DownloadPercent { get; private set; }

        /// <summary>True when this process was started by the updater right after installing an update.</summary>
        public static bool StartedAfterUpdate { get; private set; }

        public static event Action Changed;

        static Updater()
        {
            State = UpdateConfig.IsConfigured ? UpdateState.Idle : UpdateState.NotConfigured;
            Message = UpdateConfig.IsConfigured ? "" : "Updates aren't set up yet. See the README (Auto-update).";
        }

        // ---------------------------------------------------------------
        // Paths / versions
        // ---------------------------------------------------------------
        private static string ExePath { get { return Assembly.GetExecutingAssembly().Location; } }
        private static string UpdateDir
        {
            get
            {
                string d = Path.Combine(AppPaths.Folder, "update");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }
        private static string StagedExe { get { return Path.Combine(UpdateDir, "staged.exe"); } }
        private static string StagedSig { get { return Path.Combine(UpdateDir, "staged.sig"); } }
        private static string PendingFile { get { return Path.Combine(UpdateDir, "pending.txt"); } }
        private static string BlockedFile { get { return Path.Combine(UpdateDir, "blocked.txt"); } }

        private static Version Normalize(Version v)
        {
            return new Version(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build), Math.Max(0, v.Revision));
        }

        public static Version CurrentVersion
        {
            get { return Normalize(Assembly.GetExecutingAssembly().GetName().Version); }
        }

        public static string CurrentVersionText { get { return VersionText(CurrentVersion); } }

        public static string AvailableVersionText
        {
            get { return found == null ? "" : VersionText(found.Version); }
        }

        private static string VersionText(Version v)
        {
            return v.Revision > 0 ? v.ToString() : v.Major + "." + v.Minor + "." + v.Build;
        }

        private static void Set(UpdateState state, string message)
        {
            State = state;
            Message = message;
            var h = Changed;
            if (h != null) h();
        }

        // ---------------------------------------------------------------
        // Startup handling (call once from App.OnStartup, after the single-instance check)
        // Returns true if the app is relaunching and this process should exit right away.
        // ---------------------------------------------------------------
        public static bool HandleStartup(string[] args)
        {
            StartedAfterUpdate = Array.IndexOf(args, "--updated") >= 0;
            string exe = ExePath;

            try
            {
                // 1) The last update never confirmed it was healthy (it crashed) -> put the old version back.
                if (!StartedAfterUpdate && File.Exists(PendingFile) && File.Exists(exe + ".old"))
                {
                    if (Rollback(exe)) return true;
                }

                // 2) Tidy up leftovers from a finished update.
                if (!File.Exists(PendingFile))
                {
                    TryDelete(exe + ".old");
                    TryDelete(exe + ".bad");
                }

                // 3) A verified update was downloaded earlier: install it now (before the window opens).
                if (!StartedAfterUpdate && UpdateConfig.IsConfigured && AppSettings.AutoUpdate && HasStagedUpdate())
                {
                    string error;
                    if (ApplyInternal(Array.IndexOf(args, "--tray") >= 0, out error)) return true;
                    if (error != null) Set(UpdateState.Error, error);
                }
            }
            catch { /* never block startup because of the updater */ }

            if (UpdateConfig.IsConfigured && HasStagedUpdate())
            {
                try
                {
                    found = ReadStagedInfo();
                    Set(UpdateState.Ready, "Version " + VersionText(found.Version) + " is ready to install.");
                }
                catch { }
            }
            return false;
        }

        /// <summary>The new version ran fine for a while: forget the backup.</summary>
        public static void ConfirmGood()
        {
            try
            {
                TryDelete(PendingFile);
                TryDelete(ExePath + ".old");
                TryDelete(StagedExe);
                TryDelete(StagedSig);
            }
            catch { }
        }

        private static bool Rollback(string exe)
        {
            try
            {
                Version badVersion = null;
                try { badVersion = Normalize(AssemblyName.GetAssemblyName(exe).Version); } catch { }

                string bad = exe + ".bad";
                TryDelete(bad);
                File.Move(exe, bad);                 // the broken new version (running right now)
                File.Move(exe + ".old", exe);        // the previous version
                TryDelete(PendingFile);
                TryDelete(StagedExe);
                TryDelete(StagedSig);
                if (badVersion != null) File.WriteAllText(BlockedFile, badVersion.ToString());

                Process.Start(new ProcessStartInfo(exe, "--restarted")
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ---------------------------------------------------------------
        // Check for a newer release
        // ---------------------------------------------------------------
        /// <summary>Returns true if a newer release exists (State becomes Available). Never throws.</summary>
        public static async Task<bool> CheckAsync()
        {
            if (!UpdateConfig.IsConfigured)
            {
                Set(UpdateState.NotConfigured, "Updates aren't set up yet. See the README (Auto-update).");
                return false;
            }
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return false;

            try
            {
                Set(UpdateState.Checking, "Checking for updates…");
                UpdateInfo info = await FetchLatestAsync().ConfigureAwait(false);

                Version blocked = ReadBlockedVersion();
                if (info == null || info.Version <= CurrentVersion || (blocked != null && info.Version <= blocked))
                {
                    found = null;
                    Set(UpdateState.UpToDate, "You're on the latest version (v" + CurrentVersionText + ").");
                    return false;
                }

                found = info;
                if (HasStagedUpdate() && ReadStagedInfo().Version == info.Version)
                {
                    Set(UpdateState.Ready, "Version " + VersionText(info.Version) + " is ready to install.");
                    return true;
                }

                Set(UpdateState.Available, "Version " + VersionText(info.Version) + " is available.");
                return true;
            }
            catch (Exception ex)
            {
                Set(UpdateState.Error, FriendlyError(ex));
                return false;
            }
            finally
            {
                Interlocked.Exchange(ref busy, 0);
            }
        }

        private static async Task<UpdateInfo> FetchLatestAsync()
        {
            string url = "https://api.github.com/repos/" + UpdateConfig.RepoOwner + "/" + UpdateConfig.RepoName + "/releases/latest";

            string json;
            using (HttpClient client = NewClient(30, true))
            using (HttpResponseMessage resp = await client.GetAsync(url).ConfigureAwait(false))
            {
                if (resp.StatusCode == HttpStatusCode.NotFound)
                    throw new UpdateException("No release found yet on " + UpdateConfig.RepoOwner + "/" + UpdateConfig.RepoName + ".");
                if (resp.StatusCode == HttpStatusCode.Forbidden)
                    throw new UpdateException("GitHub is limiting requests right now. Try again later.");
                resp.EnsureSuccessStatusCode();
                json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            }

            var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (root == null) throw new UpdateException("Unexpected answer from GitHub.");

            if (GetBool(root, "draft") || GetBool(root, "prerelease")) return null;

            string tag = GetString(root, "tag_name");
            Version version;
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out version))
                throw new UpdateException("The latest release tag \"" + tag + "\" isn't a version number (use e.g. v3.1.0).");

            var info = new UpdateInfo { Tag = tag, Version = Normalize(version) };

            var assets = root.ContainsKey("assets") ? root["assets"] as IEnumerable : null;
            if (assets != null)
            {
                foreach (object a in assets)
                {
                    var asset = a as Dictionary<string, object>;
                    if (asset == null) continue;
                    string name = GetString(asset, "name");
                    string dl = GetString(asset, "browser_download_url");
                    if (name == UpdateConfig.ExeAssetName) info.ExeUrl = dl;
                    else if (name == UpdateConfig.SigAssetName) info.SigUrl = dl;
                }
            }

            if (string.IsNullOrEmpty(info.ExeUrl) || string.IsNullOrEmpty(info.SigUrl))
                throw new UpdateException("Release " + tag + " is missing " + UpdateConfig.ExeAssetName + " or " + UpdateConfig.SigAssetName + ".");
            if (!IsTrustedAssetUrl(info.ExeUrl) || !IsTrustedAssetUrl(info.SigUrl))
                throw new UpdateException("Release " + tag + " points to a download location that isn't this repository.");

            return info;
        }

        // ---------------------------------------------------------------
        // Download + verify
        // ---------------------------------------------------------------
        /// <summary>Downloads the update found by CheckAsync, verifies it, and stages it. Never throws.</summary>
        public static async Task DownloadAsync()
        {
            UpdateInfo info = found;
            if (info == null) return;
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;

            string tmpExe = Path.Combine(UpdateDir, "download.exe");
            string tmpSig = Path.Combine(UpdateDir, "download.sig");

            try
            {
                DownloadPercent = 0;
                Set(UpdateState.Downloading, "Downloading version " + VersionText(info.Version) + "… 0%");

                await DownloadFileAsync(info.ExeUrl, tmpExe, MaxExeBytes, (read, total) =>
                {
                    int pct = total > 0 ? (int)(read * 100 / total) : 0;
                    if (pct != DownloadPercent)
                    {
                        DownloadPercent = pct;
                        Set(UpdateState.Downloading, "Downloading version " + VersionText(info.Version) + "… " + pct + "%");
                    }
                }).ConfigureAwait(false);
                await DownloadFileAsync(info.SigUrl, tmpSig, 16 * 1024, null).ConfigureAwait(false);

                string problem = VerifyFiles(tmpExe, tmpSig, info.Version);
                if (problem != null) throw new UpdateException(problem);

                TryDelete(StagedExe);
                TryDelete(StagedSig);
                File.Move(tmpExe, StagedExe);
                File.Move(tmpSig, StagedSig);
                File.WriteAllText(Path.Combine(UpdateDir, "staged.txt"), info.Version.ToString());

                Set(UpdateState.Ready, "Version " + VersionText(info.Version) + " is ready to install.");
            }
            catch (Exception ex)
            {
                TryDelete(tmpExe);
                TryDelete(tmpSig);
                Set(UpdateState.Error, FriendlyError(ex));
            }
            finally
            {
                Interlocked.Exchange(ref busy, 0);
            }
        }

        private static async Task DownloadFileAsync(string url, string dest, long maxBytes, Action<long, long> progress)
        {
            using (HttpClient client = NewClient(300, false))
            using (HttpResponseMessage resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                if (!IsTrustedHost(resp.RequestMessage.RequestUri))
                    throw new UpdateException("The download was redirected to an untrusted site and was blocked.");

                long total = resp.Content.Headers.ContentLength ?? -1;
                if (total > maxBytes) throw new UpdateException("The download is larger than expected and was blocked.");

                using (Stream src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    long read = 0;
                    int n;
                    while ((n = await src.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    {
                        read += n;
                        if (read > maxBytes) throw new UpdateException("The download is larger than expected and was blocked.");
                        await dst.WriteAsync(buffer, 0, n).ConfigureAwait(false);
                        if (progress != null) progress(read, total);
                    }
                }
            }
        }

        /// <summary>Returns null if the file is a genuine, newer build signed with your key; otherwise a reason.</summary>
        private static string VerifyFiles(string exePath, string sigPath, Version expected)
        {
            try
            {
                byte[] sig = Convert.FromBase64String(File.ReadAllText(sigPath).Trim());
                byte[] data = File.ReadAllBytes(exePath);

                using (var rsa = new RSACryptoServiceProvider())
                using (var sha = new SHA256CryptoServiceProvider())
                {
                    rsa.FromXmlString(UpdateConfig.PublicKeyXml);
                    if (!rsa.VerifyData(data, sha, sig))
                        return "The update's signature is not valid, so it was NOT installed.";
                }

                AssemblyName name = AssemblyName.GetAssemblyName(exePath);
                if (!string.Equals(name.Name, Assembly.GetExecutingAssembly().GetName().Name, StringComparison.OrdinalIgnoreCase))
                    return "The downloaded file is not this app.";

                Version v = Normalize(name.Version);
                if (v <= CurrentVersion)
                    return "The downloaded build (v" + VersionText(v) + ") is not newer than the one you have.";
                if (expected != null && v != expected)
                    return "The release is tagged " + VersionText(expected) + " but the exe says " + VersionText(v)
                           + ". Update <Version> in the .csproj before building.";

                return null;
            }
            catch (Exception ex)
            {
                return "Could not verify the update: " + ex.Message;
            }
        }

        private static bool HasStagedUpdate()
        {
            try
            {
                if (!File.Exists(StagedExe) || !File.Exists(StagedSig)) return false;
                return VerifyFiles(StagedExe, StagedSig, null) == null;     // re-checked right before installing
            }
            catch { return false; }
        }

        private static UpdateInfo ReadStagedInfo()
        {
            Version v = Normalize(AssemblyName.GetAssemblyName(StagedExe).Version);
            return new UpdateInfo { Version = v, Tag = "v" + VersionText(v) };
        }

        // ---------------------------------------------------------------
        // Install
        // ---------------------------------------------------------------
        /// <summary>Installs the staged update and starts the new version. On true, the caller must shut the app down.</summary>
        public static bool ApplyAndRestart(bool startHidden, out string error)
        {
            bool ok = ApplyInternal(startHidden, out error);
            if (!ok && error != null) Set(UpdateState.Error, error);
            return ok;
        }

        private static bool ApplyInternal(bool startHidden, out string error)
        {
            error = null;
            if (!HasStagedUpdate()) { error = "No verified update is waiting."; return false; }

            string exe = ExePath;
            string old = exe + ".old";

            try
            {
                if (File.Exists(old)) File.Delete(old);
                File.Move(exe, old);                       // a running exe can be renamed, just not overwritten
            }
            catch (Exception ex)
            {
                error = "Couldn't update: no permission to change the app's folder (" + ex.Message + "). Move the app to a folder you own.";
                return false;
            }

            try
            {
                File.Copy(StagedExe, exe, false);
                File.WriteAllText(PendingFile, VersionText(ReadStagedInfo().Version));

                string args = "--restarted --updated" + (startHidden ? " --tray" : "");
                Process.Start(new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                });
                return true;
            }
            catch (Exception ex)
            {
                // Put everything back exactly as it was.
                try { if (File.Exists(exe)) File.Delete(exe); } catch { }
                try { File.Move(old, exe); } catch { }
                TryDelete(PendingFile);
                error = "The update could not be installed and was rolled back: " + ex.Message;
                return false;
            }
        }

        // ---------------------------------------------------------------
        // Background loop: check shortly after launch, then every few hours
        // ---------------------------------------------------------------
        /// <param name="isWindowHidden">Called on any thread; true when the window is in the tray (safe to restart).</param>
        /// <param name="restartApp">Called after a new version was started; should shut this process down.</param>
        public static void StartBackground(Func<bool> isWindowHidden, Action restartApp)
        {
            if (Interlocked.Exchange(ref backgroundStarted, 1) != 0) return;

            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                while (true)
                {
                    try
                    {
                        if (UpdateConfig.IsConfigured && AppSettings.AutoUpdate)
                        {
                            if (State != UpdateState.Ready)
                            {
                                if (await CheckAsync().ConfigureAwait(false))
                                    await DownloadAsync().ConfigureAwait(false);
                            }

                            // Only restart on our own when nobody is looking at the window.
                            string error;
                            if (State == UpdateState.Ready && isWindowHidden() && ApplyAndRestart(true, out error))
                            {
                                restartApp();
                                return;
                            }
                        }
                    }
                    catch { /* try again next round */ }

                    await Task.Delay(CheckInterval).ConfigureAwait(false);
                }
            });
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------
        private static HttpClient NewClient(int timeoutSeconds, bool json)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("ddeprivedd.os-updater/" + CurrentVersionText);
            if (json) c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        private static bool IsTrustedHost(Uri u)
        {
            if (u == null || u.Scheme != Uri.UriSchemeHttps) return false;
            string h = u.Host.ToLowerInvariant();
            return h == "github.com" || h == "githubusercontent.com" || h.EndsWith(".githubusercontent.com");
        }

        /// <summary>Asset links must live under this repo's own releases/download path on github.com.</summary>
        private static bool IsTrustedAssetUrl(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            if (u.Scheme != Uri.UriSchemeHttps || !u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;
            string prefix = "/" + UpdateConfig.RepoOwner + "/" + UpdateConfig.RepoName + "/releases/download/";
            return u.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetString(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v != null ? v.ToString() : "";
        }

        private static bool GetBool(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v is bool && (bool)v;
        }

        private static Version ReadBlockedVersion()
        {
            try
            {
                Version v;
                return File.Exists(BlockedFile) && Version.TryParse(File.ReadAllText(BlockedFile).Trim(), out v) ? v : null;
            }
            catch { return null; }
        }

        private static string FriendlyError(Exception ex)
        {
            if (ex is UpdateException) return ex.Message;
            if (ex is HttpRequestException || ex is WebException || ex is TaskCanceledException)
                return "Couldn't reach GitHub. Check your internet connection.";
            return "Update failed: " + ex.Message;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}

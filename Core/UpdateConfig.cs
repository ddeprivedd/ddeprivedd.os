namespace ddeprivedd.os
{
    /// <summary>
    /// Where the app looks for updates. Run tools\Setup-Updates.ps1 once to fill these in
    /// (it also creates your private signing key). Until then, auto-update stays switched off.
    /// </summary>
    public static class UpdateConfig
    {
        // GitHub repository that hosts your releases, e.g. owner "yourname", repo "ddeprivedd-os".
        public const string RepoOwner = "ddeprivedd";   // <<REPO_OWNER>>
        public const string RepoName = "ddeprivedd.os";    // <<REPO_NAME>>

        // Public half of your signing key. Updates that are not signed with the matching
        // private key (kept only on your PC) are refused, even if someone tampers with GitHub.
        public const string PublicKeyXml = "";   // <<PUBLIC_KEY>>

        // The two files every release must contain.
        public const string ExeAssetName = "ddeprivedd.os.exe";
        public const string SigAssetName = "ddeprivedd.os.exe.sig";

        public static bool IsConfigured
        {
            get { return RepoOwner.Length > 0 && RepoName.Length > 0 && PublicKeyXml.Length > 0; }
        }
    }
}

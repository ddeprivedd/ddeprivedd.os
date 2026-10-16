# ddeprivedd.os  v3.1 (WPF, .NET Framework 4.7.2)

## Build the exe (Windows)
1. Install the free **.NET SDK** (https://dotnet.microsoft.com/download). Any SDK 6.0 or newer works.
2. Double-click **build.bat**. The first build downloads reference files, so you need internet once.
3. Your exe appears in the **dist** folder: `ddeprivedd.os.exe`

Prefer Visual Studio 2019/2022 (with the ".NET desktop development" workload)? Open
`ddeprivedd.os.csproj` and press Build. Nothing else is needed.

The exe runs on Windows 10/11 without installing anything. Edit the source any time and run build.bat again.

## Auto-update (v3.1)
The app can update itself from **GitHub Releases**. Updates are **signed**: the app only installs a download
whose signature matches the key that only you have, so nobody can push a fake update, even if your GitHub
account were ever compromised.

**One-time setup**
1. Put the project on GitHub (public repo, e.g. `ddeprivedd/ddeprivedd.os`).
2. Run `powershell -ExecutionPolicy Bypass -File tools\Setup-Updates.ps1 -Repo ddeprivedd/ddeprivedd.os`.
   It creates your private key in `%USERPROFILE%\ddeprivedd-signing-key\` (back it up, never share/upload it)
   and fills in `Core/UpdateConfig.cs`.
3. Build + sign: `tools\Release.ps1`, then create a GitHub release tagged `v3.1.0` and attach
   `dist\ddeprivedd.os.exe` and `dist\ddeprivedd.os.exe.sig` (or run `tools\Release.ps1 -Publish` with the GitHub CLI).
4. Give people that first exe once. From then on they update automatically.

**Every future update**
1. Edit the code, raise `<Version>` in `ddeprivedd.os.csproj` (e.g. 3.1.0.0 -> 3.1.1.0).
2. `tools\Release.ps1 -Publish -Notes "what changed"` (or upload the two files to a release tagged `v3.1.1`).

**What users see**
- Settings > Updates: version, "Install updates automatically" switch (on by default), "Check for updates".
- A button appears top-right ("Restart to update to v3.1.1") when an update is ready. If the window is hidden in the
  tray, the app restarts into the new version by itself. Otherwise it also installs on the next launch.
- It checks 10 seconds after launch and every 6 hours.

**Safety built in**
- HTTPS only, downloads must come from your repo's own release page, size-limited.
- RSA-SHA256 signature check with your key, plus: must be the same app, must be a higher version than the one
  running (no downgrade replays), version in the exe must match the release.
- The old exe is kept as `ddeprivedd.os.exe.old`. If the new version crashes within 20 s of starting, the next launch
  restores the old one automatically and skips that version.
- Only the `.exe` is replaced (not the `.config`). If you ever need a different config, ship a new full build.
- The app folder must be writable by the user (not "Program Files").

## What's new in v3
- **RAM Limiter tab** (the old console RAM-Limiter, now inside the app). Type a process name (`discord`, `chrome`, `obs64`) and press **Add**.
  The app does not have to be open: if it is not running it is added anyway (with "Did you mean ...?" when something similar is running) and shows **Not running** until it starts. Spaces and punctuation are ignored, so `rocket league` finds `RocketLeague`.
- **No more console spam**: the list updates in place like Task Manager (status, process count, memory, last trim). Nothing scrolls.
- Trims **every** running process with that name (browsers and Electron apps run many), not only the biggest one.
- Needs no admin for normal apps. Apps that refuse show **Needs admin** (with a **Restart as admin** button) or **Blocked** (anti-cheat/system apps that refuse even admins).
- The limited list is saved to `%AppData%\ddeprivedd.os\ramlimiter.txt` and limiting resumes next launch.
- **Minimize goes to the system tray**, not the taskbar. Click the tray icon (or right-click > Open) to bring it back, right-click > Exit to quit. The RAM Limiter keeps running while hidden.
- **Only one copy can run at a time.** Opening it again just brings the running copy to the front (even from the tray).
- Renamed to ddeprivedd.os. Your old whitelist and settings are imported once from `%AppData%\PeasureBoosterX`.

## What's new in v2
- **Whitelist tab**: apps on it are never force-closed. Add by process name, pick from open apps, or remove.
  Saved to `%AppData%\ddeprivedd.os\whitelist.txt` (one process name per line, you can edit it by hand).
- **Brand new GUI**: frameless window, animated sidebar tabs, animated buttons, toggle switches, live graphs.
- **No visible scrollbars**: every page scrolls with the mouse wheel and eases smoothly.
- **No more freezing**: optimizing and cleaning run in the background, so the window stays responsive.

## Where things are
```
App.xaml(.cs)              startup, loads settings + whitelist
MainWindow.xaml(.cs)       window frame, sidebar, tab switching animation
Theme/Styles.xaml          ALL colors, buttons, cards, switches. Change the look here.
Controls/StatusLog         animated activity list
Views/OptimizerView        game cards, background-app chips, progress + log
Views/WhitelistView        the whitelist page
Views/CleanerView          temp files + recycle bin
Views/MonitorView          live CPU / RAM / process graphs
Views/RamLimiterView       RAM Limiter page (add box + live list)
Views/SettingsView         toggle switches (saved to settings.ini)
Core/OptimizerEngine.cs    what the optimizer does. DefaultCloseList = apps it closes.
Core/CleanerEngine.cs      what the cleaner does
Core/RamLimiterEngine.cs   background trimming. IntervalMs = how often (default 5000)
Core/Whitelist.cs          whitelist storage and matching
Core/AppSettings.cs        settings.ini
Core/Anim.cs, SmoothScroll.cs   animation helpers
```

## Common edits
- **Change which apps get closed**: edit `DefaultCloseList` in `Core/OptimizerEngine.cs`.
- **Change colors**: edit the palette at the top of `Theme/Styles.xaml`.
- **Change what an Optimize button does**: edit `RunGame` / `RunAmd` in `Core/OptimizerEngine.cs`.

## Behavior notes (unchanged from the original)
- The network throttling tweak writes to HKLM, so it only works if the exe is run as administrator.
- The Settings switches are saved but nothing else in the app reads them yet.
- Cleaning temp files from the Cleaner tab only deletes top-level files in %TEMP%.
- Settings now live in `%AppData%\ddeprivedd.os\settings.ini` (an old settings.ini next to the exe is imported once).

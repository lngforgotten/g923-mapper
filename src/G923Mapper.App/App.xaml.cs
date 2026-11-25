using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace G923Mapper.App;

public partial class App : Application
{
    private readonly System.Collections.Generic.List<Mutex> _mutexes = new();
    private EventWaitHandle? _showEvent;
    private int _instance = 1;
    private AppServices? _services;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private IntPtr _hwnd;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Which copy is this? Normally only one runs. With "allow more than one copy" each extra copy drives the next wheel.
        int? requested = InstancePlan.ParseArgument(e.Args);
        bool allowMultiple = AppSettings.Load(1).AllowMultipleInstances;
        int? claimed = InstancePlan.Choose(allowMultiple, requested, TryClaim);

        AppLog.Init(Path.Combine(AppSettings.AppDataDir, "logs"), name: InstancePlan.LogName(claimed ?? 1));
        AppLog.Info($"G923 Mapper starting as copy {claimed?.ToString() ?? "(none)"} ({string.Join(" ", e.Args)}).");
        InstallCrashHandlers();

        if (claimed is not int instance)
        {
            // Already running. Just bring that window to the front; no error box.
            AppLog.Info("Another copy is already running; bringing it to the front.");
            if (!SignalExisting(requested ?? 1))
                MessageBox.Show("G923 Mapper is already running. Look for its icon in the system tray.",
                    "G923 Mapper", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        _instance = instance;

        // An update that was downloaded earlier is swapped in now, before anything else starts, and the new version takes over.
        if (instance == 1 && TryApplyStagedAtStart(e.Args)) { Shutdown(); return; }
        if (Environment.ProcessPath is { } runningExe && UpdatePackage.IsInstalledCopy(runningExe, InstallDir))
            Task.Run(() => UpdatePackage.CleanOld(runningExe));       // the programs earlier updates left aside

        StartShowListener(instance);
        try
        {
            string appData = AppSettings.AppDataDir;
            string profileDir = Path.Combine(appData, "profiles");
            string devicesPath = Path.Combine(appData, "devices.json");
            var settings = AppSettings.Load(instance);

            ThemeManager.Initialise();
            ThemeManager.Apply(ThemeManager.TryParse(settings.Theme, out var theme) ? theme : ThemeSetting.System);

            var devices = DeviceRegistry.CreateDefault();
            try { devices.LoadFile(devicesPath); }
            catch (Exception ex)
            {
                AppLog.Error("devices.json could not be read", ex);
                MessageBox.Show($"devices.json could not be read and was ignored:\n{ex.Message}", "G923 Mapper");
            }

            Directory.CreateDirectory(appData);
            AntiCheatList.Current = AntiCheatList.Load(AppSettings.AntiCheatPath);
            AppLog.Info($"Anti-cheat list: {string.Join(", ", AntiCheatList.Current.Names)}.");

            DefaultProfiles.EnsureWritten(profileDir);
            if (!settings.TemplatesSeeded)
            {
                try { StarterTemplates.WriteMissing(profileDir); } catch (IOException ex) { AppLog.Warn("Could not write the starter templates: " + ex.Message); }
                settings.TemplatesSeeded = true;
                settings.Save();
            }
            var store = new ProfileStore(profileDir, devices) { HistoryRoot = AppSettings.HistoryDir };
            var switcher = new AutoProfileSwitcher(AutoProfileSwitcher.FromStore(store), foreground: ForegroundWindow.ExeName);
            var service = new MapperService(devices, store, () => _hwnd)
            {
                WheelIndex = instance - 1,
                ProfileName = settings.Profile,
                Force = settings.Force.Clone(),
            };

            _services = new AppServices
            {
                Service = service, Store = store, Devices = devices, Settings = settings, Switcher = switcher,
                HidHide = new HidHideClient(), Instance = instance, ProfileDir = profileDir, DevicesPath = devicesPath, Hwnd = () => _hwnd,
            };

            _window = new MainWindow(_services);
            if (instance > 1) _window.Title = $"G923 Mapper (wheel {instance})";
            // DirectInput needs a window handle for its cooperative level; use our own, even while hidden.
            _hwnd = new WindowInteropHelper(_window).EnsureHandle();

            _tray = new TrayIcon(
                open: ShowWindow,
                toggleRun: () => _window.Dispatcher.Invoke(_window.ToggleRunning),
                exit: ExitApp,
                isRunning: () => service.IsRunning,
                profiles: TrayProfiles,
                chooseProfile: key => _window.Dispatcher.Invoke(() => _window.SelectProfile(key)),
                isPaused: () => service.Paused,
                togglePause: () => _window.Dispatcher.Invoke(TogglePause),
                hideState: () => (_services?.HidHide.IsInstalled == true, _services?.WheelHidden == true),
                toggleHidden: () => _window.Dispatcher.BeginInvoke(ToggleWheelHidden),
                popOut: () => _window.Dispatcher.BeginInvoke(_window.ShowMini));
            try { _window.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico")); }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
            {
                _window.Icon = AppIcon.ToBitmapSource(_tray.Glyph);   // the drawn fallback
            }
            _window.ExitRequested += ExitApp;
            _window.ProfileInUse += OnProfileInUse;
            StartHiddenGameWatch();

            // The optional shortcut that pauses and resumes the controller, and the "release when the game is not in front" option.
            _hotkeys = new HotkeyManager(_hwnd, () => _window.Dispatcher.BeginInvoke(TogglePause));
            _modeInputs = new ModeShortcutListener(_hwnd, action => service.RequestMode(action));    // watches only while shortcuts are set
            service.ShortcutsChanged += list => { _profileShortcuts = list; _window?.Dispatcher.BeginInvoke(ApplyEffectiveShortcuts); };
            StartNativeGameWatch();
            _modeHotkeys = new HotkeyManager(_hwnd, () => service.RequestMode("Next"), 0x6924);   // the loop does the switching, from any thread
            service.ModeChanged += OnModeChanged;
            _services.RestartForUpdate = RestartForUpdate;
            AnnounceUpdateState(_services);
            _services.InstallUpdateNow = InstallUpdateNowAsync;
            _autoUpdate = new AutoUpdate(_services, OnUpdateFound);
            _autoUpdate.Start();
            _services.ApplyInputOptions = ApplyInputOptions;
            service.Frame += OnFrame;
            ApplyInputOptions();
            _window.StatusTextChanged += text => _tray.SetTooltip(instance > 1 ? text.Replace("G923 Mapper:", $"G923 Mapper (wheel {instance}):") : text);
            _window.HiddenToTray += () =>
            {
                if (settings.WarnedAboutTray) return;
                settings.WarnedAboutTray = true;
                settings.Save();
                _tray.ShowBalloon("G923 Mapper is still running", "It keeps your wheel mapped from the system tray. Right-click the icon to exit.");
            };

            // Say so when a game's profile switches on by itself, so it never has to be guessed.
            _window.ProfileAutoSwitched += (profileName, exe) =>
            {
                if (profileName is null) _tray.ShowBalloon("Game closed", "Back to your own profile.");
                else _tray.ShowBalloon($"{profileName} profile is on", $"{exe} is running, so its profile was switched on automatically.");
            };

            // HidHide can be slow to answer, so ask off the UI thread. A wheel that is still hidden although this app is set to
            // give it back on exit means the last session did not get to (crash, kill, declined permission): say so.
            var services = _services;
            _ = services.RefreshHiddenStateAsync().ContinueWith(_ =>
            {
                if (instance != 1 || !WheelRestorePolicy.WarnAtStart(settings.UnhideWheelOnExit, services.HidHide.IsInstalled, services.WheelHidden)) return;
                AppLog.Warn("The wheel is still hidden by HidHide from an earlier session.");
                _window.Dispatcher.BeginInvoke(() => _tray.ShowBalloon("Your wheel is still hidden",
                    "HidHide is hiding it from other programs, such as Logitech G HUB. Open Settings and choose Show wheel again to give it back."));
            });
            switcher.Poll();                            // pick the right profile for an already-running game before the first frame
            service.Start();
            if (!e.Args.Contains("--minimized")) _window.Show();
            OnProfileInUse(_services.Switcher.ActiveKey ?? settings.Profile);   // the profile the app starts with counts too

            // The first time the app ever runs, walk the user through getting set up (driver, wheel, calibration, profile).
            if (settings.IsFirstRun && !settings.SetupDone && !e.Args.Contains("--minimized"))
                _window.Dispatcher.BeginInvoke(new Action(_window.ShowSetup), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            OfferDriverIfMissing(e.Args.Contains("--minimized"));
        }
        catch (Exception ex)
        {
            AppLog.Error("Startup failed", ex);
            MessageBox.Show("G923 Mapper could not start:\n\n" + ex.Message + "\n\nDetails are in the log folder.",
                "G923 Mapper", MessageBoxButton.OK, MessageBoxImage.Error);
            ExitApp();
        }
    }

    /// <summary>First-run help: without the ViGEmBus driver nothing else can work, so say so plainly.</summary>
    private void OfferDriverIfMissing(bool minimised)
    {
        if (minimised || HealthProbe.Service("ViGEmBus").installed) return;
        var answer = MessageBox.Show(
            "G923 Mapper needs the ViGEmBus driver to create the virtual Xbox 360 controller, and it is not installed yet.\n\n" +
            "Open the download page now? Install it (it asks for administrator permission), then restart G923 Mapper.",
            "One thing is missing", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer == MessageBoxResult.Yes)
        {
            try { Process.Start(new ProcessStartInfo("https://github.com/nefarius/ViGEmBus/releases") { UseShellExecute = true }); } catch { }
        }
    }

    private void InstallCrashHandlers()
    {
        // A problem in the UI is logged and shown, and the app keeps running.
        DispatcherUnhandledException += (_, ev) =>
        {
            AppLog.Error("Unhandled UI exception", ev.Exception);
            ev.Handled = true;
            MessageBox.Show("Something went wrong, but G923 Mapper is still running.\n\n" + ev.Exception.Message +
                            "\n\nDetails were written to the log (Settings, then Open log folder).",
                "G923 Mapper", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        // A crash elsewhere cannot be stopped, but held keys and the virtual pad are let go of first.
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
        {
            AppLog.Error("Fatal error", ev.ExceptionObject as Exception);
            try { _services?.Service.Stop(); } catch { }
        };
        TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            AppLog.Error("Unobserved task exception", ev.Exception);
            ev.SetObserved();
        };
    }

    /// <summary>
    /// Take copy number <paramref name="n"/> if nobody else has it. Copy 1 also takes the name older versions used, so
    /// an old copy and a new one can never both run as copy 1 and create two competing controllers.
    /// </summary>
    private bool TryClaim(int n)
    {
        var mine = new Mutex(true, $@"Local\G923Mapper.Instance.{n}", out bool created);
        if (!created) { mine.Dispose(); return false; }

        if (n == 1)
        {
            var legacy = new Mutex(true, @"Local\G923Mapper.SingleInstance", out bool legacyCreated);
            if (!legacyCreated) { legacy.Dispose(); mine.ReleaseMutex(); mine.Dispose(); return false; }
            _mutexes.Add(legacy);
        }
        _mutexes.Add(mine);
        return true;
    }

    /// <summary>Ask the copy that is already running to show its window. False if it did not answer (for example an older version).</summary>
    private static bool SignalExisting(int n)
    {
        try
        {
            using var show = EventWaitHandle.OpenExisting($@"Local\G923Mapper.Show.{n}");
            return show.Set();
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
    }

    /// <summary>Waits (on a background thread) for a second launch to ask this copy to show itself.</summary>
    private void StartShowListener(int n)
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\G923Mapper.Show.{n}");
        var handle = _showEvent;
        new Thread(() =>
        {
            while (true)
            {
                try { handle.WaitOne(); }
                catch (ObjectDisposedException) { return; }
                ShowWindow();
            }
        }) { IsBackground = true, Name = "Show-window listener" }.Start();
    }

    private void ShowWindow()
    {
        if (_window is null) return;
        _window.Dispatcher.Invoke(() =>
        {
            _window.Show();
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            _window.Activate();
        });
    }

    private HotkeyManager? _hotkeys;
    private HotkeyManager? _modeHotkeys;
    private ModeShortcutListener? _modeInputs;
    private System.Collections.Generic.IReadOnlyList<ModeShortcut>? _profileShortcuts;      // the running profile's own list; null = use the app-wide one

    /// <summary>The shortcuts in force now: the running profile's own, or the app-wide ones when it has none. Games bind different keys, so a profile can have its own.</summary>
    private void ApplyEffectiveShortcuts() => _modeInputs?.Apply(_profileShortcuts ?? _services?.Settings.ModeShortcuts);

    // ---------------------------------------------------------------- games with their own wheel support

    private System.Windows.Threading.DispatcherTimer? _nativeGameTimer;
    private readonly System.Collections.Generic.HashSet<string> _toldGames = new();

    /// <summary>
    /// Every few seconds: if a game with its own wheel support is running while a profile that holds the wheel is in use, say so once for that game.
    /// Only a suggestion: nothing is changed.
    /// </summary>
    private void StartNativeGameWatch()
    {
        _nativeGameTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _nativeGameTimer.Tick += (_, _) =>
        {
            if (_services is not { } s || !s.Settings.NativeWheelHints || s.Service.State != MapperState.Running) return;
            string? name = s.Service.Latest?.ProfileName;
            Profile? inUse = name is null ? null : s.Store.Get(name);
            var profiles = s.Store.Files.Select(f => s.Store.Get(f)).Where(p => p is not null).Select(p => p!).ToList();
            foreach (var game in NativeWheelGames.ToWarnAbout(RunningProcesses.Names(), profiles, inUse, _toldGames))
            {
                _toldGames.Add(game.Id);
                var (title, text) = NativeWheelGames.Notice(game);
                AppLog.Info($"{title}: {text}");
                _tray?.ShowBalloon(title, text);
            }
        };
        _nativeGameTimer.Start();
    }
    private AutoUpdate? _autoUpdate;

    // ---------------------------------------------------------------- updating itself

    private static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "G923Mapper");
    private static Version RunningVersion => typeof(App).Assembly.GetName().Version ?? new Version(1, 0, 0);
    private string? _stagedTag;

    private void UpdateUi() => _window?.Dispatcher.BeginInvoke(() => _services?.UpdateChanged?.Invoke());

    /// <summary>
    /// A newer version was found (by the automatic check or by the button). With automatic installing on it is downloaded and checked in the
    /// background and swapped in the next time the app starts; otherwise Home offers it and Windows says so once for each version.
    /// </summary>
    private void OnUpdateFound(UpdateInfo info)
    {
        if (_services is not { } s) return;
        s.AvailableUpdate = info;
        bool busy = s.UpdatePhase == UpdatePhase.Downloading;
        bool alreadyReady = s.UpdatePhase == UpdatePhase.Ready && _stagedTag == info.LatestTag;
        if (!busy && !alreadyReady) s.UpdatePhase = UpdatePhase.Available;
        _window?.Dispatcher.BeginInvoke(() =>
        {
            s.UpdateChanged?.Invoke();
            if (s.Settings.AutoInstallUpdates && s.UpdatePhase == UpdatePhase.Available)
            {
                _ = StageUpdateAsync(info, thenRestart: false);
                return;
            }
            if (s.Settings.UpdateNotifiedTag == info.LatestTag || s.Settings.UpdateDismissedTag == info.LatestTag) return;
            s.Settings.UpdateNotifiedTag = info.LatestTag;
            s.Settings.Save();
            _tray?.ShowBalloon("A new version is available", $"G923 Mapper {info.LatestTag} is out. Open the app to install it.");
        });
    }

    /// <summary>
    /// At start-up: if the app has just updated itself, say so once (Home and a notification). If an update is waiting that was not swapped in
    /// (the person chose to press Restart now themselves), show it as ready again.
    /// </summary>
    private void AnnounceUpdateState(AppServices s)
    {
        if (UpdatePackage.TakeUpdatedMarker(AppSettings.UpdatedMarkerPath, RunningVersion) is { } tag)
        {
            s.JustUpdatedTag = tag;
            AppLog.Info($"Updated to {tag}: telling the person.");
            _window?.Dispatcher.BeginInvoke(() => _tray?.ShowBalloon("G923 Mapper has updated", $"Now running {tag}. Open the app to see what is new."));
            return;
        }
        StagedUpdate? waiting = UpdatePackage.ReadStaged(AppSettings.StagedDir);
        if (waiting is not null && UpdatePackage.IsNewer(waiting, RunningVersion) && UpdatePackage.IsInstalledCopy(Environment.ProcessPath, InstallDir))
        {
            string repo = UpdateCheck.NormaliseRepo(UpdateCheck.RepoOrDefault(s.Settings.UpdateRepo)) ?? UpdateCheck.DefaultRepo;
            s.AvailableUpdate = new UpdateInfo(true, waiting.Tag, $"https://github.com/{repo}/releases/tag/{waiting.Tag}", $"{waiting.Tag} is ready.");
            s.UpdatePhase = UpdatePhase.Ready;
            _stagedTag = waiting.Tag;
        }
    }

    /// <summary>The "Install now" button: download the new version if it is not ready yet, then restart into it.</summary>
    private async Task InstallUpdateNowAsync()
    {
        if (_services is not { } s) return;
        if (s.UpdatePhase == UpdatePhase.Ready) { RestartForUpdate(); return; }
        if (s.AvailableUpdate is { Newer: true } info) await StageUpdateAsync(info, thenRestart: true);
    }

    /// <summary>Download and check a new version. It then waits to be swapped in, or, when asked to, the app restarts into it straight away.</summary>
    private async Task StageUpdateAsync(UpdateInfo info, bool thenRestart)
    {
        if (_services is not { } s || s.UpdatePhase == UpdatePhase.Downloading) return;
        if (!UpdatePackage.IsInstalledCopy(Environment.ProcessPath, InstallDir))
        {
            s.UpdatePhase = UpdatePhase.Failed;
            s.UpdateProblem = "This copy runs from a folder rather than where Setup installs it, so it does not update itself. Use the release page.";
            UpdateUi();
            return;
        }

        s.UpdatePhase = UpdatePhase.Downloading; s.UpdatePercent = 0; s.UpdateProblem = "";
        UpdateUi();
        var progress = new Progress<int>(p => { s.UpdatePercent = p; UpdateUi(); });
        var (staged, message) = await UpdateInstaller.StageAsync(info, s.Settings.UpdateRepo, progress);
        if (staged is null)
        {
            s.UpdatePhase = UpdatePhase.Failed; s.UpdateProblem = message;
            AppLog.Warn("Update " + info.LatestTag + ": " + message);
            UpdateUi();
            return;
        }

        _stagedTag = staged.Tag;
        s.UpdatePhase = UpdatePhase.Ready;
        AppLog.Info($"Update {staged.Tag} is downloaded and checked. It is installed the next time the app starts.");
        UpdateUi();
        if (thenRestart) { RestartForUpdate(); return; }
        if (s.Settings.UpdateNotifiedTag != staged.Tag)
        {
            s.Settings.UpdateNotifiedTag = staged.Tag;
            s.Settings.Save();
            _tray?.ShowBalloon($"G923 Mapper {staged.Tag} is ready", s.Settings.AutoApplyUpdates
                ? "It installs by itself the next time the app starts. Open the app and press Restart now to use it straight away."
                : "Open the app and press Restart now to install it.");
        }
    }

    /// <summary>Swap the ready update in and start the new version. The running program is renamed, not overwritten, so this works while it runs.</summary>
    private void RestartForUpdate()
    {
        if (_services is null) return;
        string? exe = Environment.ProcessPath;
        StagedUpdate? staged = UpdatePackage.ReadStaged(AppSettings.StagedDir);
        if (staged is null || !UpdatePackage.IsNewer(staged, RunningVersion) || exe is null || !UpdatePackage.IsInstalledCopy(exe, InstallDir))
        {
            MessageBox.Show("There is no update ready to install.", "G923 Mapper", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!UpdatePackage.Swap(staged.ExePath, exe, out string problem))
        {
            MessageBox.Show("The update could not be installed, so nothing was changed:\n\n" + problem, "G923 Mapper", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        FinishSwap(staged);
        LaunchLater(exe, Environment.GetCommandLineArgs().Skip(1));
        ExitApp();
    }

    /// <summary>Right after a swap: tidy the stage and tell Windows' list of installed apps which version is now installed.</summary>
    private static void FinishSwap(StagedUpdate staged)
    {
        AppLog.Info($"Updated to {staged.Tag}.");
        UpdatePackage.ClearStaged(AppSettings.UpdateDir);
        UpdatePackage.WriteUpdatedMarker(AppSettings.UpdatedMarkerPath, staged.Tag, staged.Version);   // the new copy says so, once
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\G923Mapper", writable: true);
            key?.SetValue("DisplayVersion", staged.Version.ToString(3));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
    }

    /// <summary>At start-up: swap in an update that is waiting. True when the new version has been started and this copy should close.</summary>
    private bool TryApplyStagedAtStart(string[] args)
    {
        try
        {
            StagedUpdate? staged = UpdatePackage.ReadStaged(AppSettings.StagedDir);
            if (staged is null) return false;
            if (!UpdatePackage.IsNewer(staged, RunningVersion)) { UpdatePackage.ClearStaged(AppSettings.UpdateDir); return false; }   // already on it
            if (!AppSettings.Load(1).AutoApplyUpdates) return false;     // the person wants to press Restart now themselves
            string? exe = Environment.ProcessPath;
            if (exe is null || !UpdatePackage.IsInstalledCopy(exe, InstallDir)) return false;
            if (!UpdatePackage.Swap(staged.ExePath, exe, out string problem)) { AppLog.Warn("Update at start-up: " + problem); return false; }
            FinishSwap(staged);
            LaunchLater(exe, args);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLog.Warn("Update at start-up failed: " + ex.Message);
            return false;
        }
    }

    /// <summary>When the app closes: swap in an update that is waiting, so the next start is the new version.</summary>
    private void ApplyStagedAtExit()
    {
        if (_instance != 1 || _services?.Settings.AutoApplyUpdates == false) return;
        try
        {
            StagedUpdate? staged = UpdatePackage.ReadStaged(AppSettings.StagedDir);
            string? exe = Environment.ProcessPath;
            if (staged is null || !UpdatePackage.IsNewer(staged, RunningVersion) || exe is null || !UpdatePackage.IsInstalledCopy(exe, InstallDir)) return;
            if (UpdatePackage.Swap(staged.ExePath, exe, out string problem)) FinishSwap(staged);
            else AppLog.Warn("Update at exit: " + problem);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLog.Warn("Update at exit failed: " + ex.Message);
        }
    }

    /// <summary>Start the program again in a few seconds, once this copy has gone and let go of its single-copy lock.</summary>
    private static void LaunchLater(string exe, System.Collections.Generic.IEnumerable<string>? args)
    {
        string extra = string.Join(" ", (args ?? []).Select(a => "\"" + a.Replace("\"", "") + "\""));
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 4 127.0.0.1 >nul & start \"\" \"{exe}\" {extra}")
            { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Warn("Could not start the new version: " + ex.Message);
        }
    }

    /// <summary>A mode was switched: a Windows notification, if the user wants one. (The wheel buzz is done by the mapper itself.)</summary>
    private void OnModeChanged(string? mode, int count, bool switched)
    {
        if (!switched || mode is null || _services is not { } s || !s.Settings.ModeToast) return;
        string profile = s.Service.Latest?.ProfileName ?? "";
        _window?.Dispatcher.BeginInvoke(() => _tray?.ShowBalloon($"Mode: {mode}", profile.Length > 0 ? $"{profile} ({s.Service.ModeIndex + 1} of {count})" : $"{s.Service.ModeIndex + 1} of {count}"));
    }

    /// <summary>The optional shortcut that moves to the next mode, and the buzz setting.</summary>
    private void ApplyModeOptions(AppServices s)
    {
        s.Service.ModeBuzz = s.Settings.ModeBuzz;
        s.ModeHotkeyProblem = "";
        ApplyEffectiveShortcuts();
        if (_modeHotkeys is null) return;
        if (!s.Settings.ModeHotkey.Enabled) { _modeHotkeys.Unregister(); return; }
        if (!Hotkey.TryParse(s.Settings.ModeHotkey.Combo, out Hotkey? key, out string? why))
        {
            _modeHotkeys.Unregister();
            s.ModeHotkeyProblem = "That shortcut cannot be used: " + why;
        }
        else if (!_modeHotkeys.Register(key!))
        {
            s.ModeHotkeyProblem = $"{key} is already used by another program (or by the pause shortcut). Pick a different one.";
            AppLog.Warn(s.ModeHotkeyProblem);
        }
        else AppLog.Info($"Mode shortcut registered: {key}.");
    }

    // Per-profile "hide the wheel": a profile can ask for the real wheel to be hidden from games while it is in use.
    private readonly WheelVisibilityTracker _visibility = new();

    private async void OnProfileInUse(string key)
    {
        if (_services is not { } s || !s.HidHide.IsInstalled) return;
        await s.RefreshHiddenStateAsync();   // do not act on a stale answer
        // Only a profile that is running because its game was detected gets to hide the wheel; a profile chosen by hand, or remembered from
        // last time, has no say. (When the game ends and another profile takes over, the wheel is given back.)
        bool gameDetected = string.Equals(s.Switcher.ActiveKey, key, StringComparison.OrdinalIgnoreCase);
        bool? wanted = WheelVisibility.WantedFor(s.Store.Get(key)?.HideWheel, gameDetected);

        // The game that hid the wheel only lost the focus (another window came to the front, or Alt-Tab): keep it hidden. Giving it back and
        // hiding it again each time would ask for permission every time. It is given back once that game has ended (see CheckHiddenGameEnded).
        if (wanted is null && _visibility.HiddenFor is { } hiddenFor
            && _visibility.KeepHidden(s.WheelHidden, WheelVisibility.GameRunning(s.Store.Get(hiddenFor), RunningProcesses.Names()))) return;

        // Editing the setting counts as a new decision, so the id includes it.
        bool? action = _visibility.OnProfileActive(key + "|" + (wanted?.ToString() ?? "-"), wanted, s.HidHide.IsInstalled, s.WheelHidden);
        if (action is not bool hide) return;
        if (!hide) _visibility.MarkRestoreTried();

        _tray?.ShowBalloon(hide ? "Hiding the wheel for this profile" : "Showing the wheel again", "Windows will ask for permission.");
        var result = await s.SetWheelHiddenAsync(hide);
        _visibility.Done(hide, result.Success, key);
        AppLog.Info($"Profile \"{key}\": {(hide ? "hide" : "show")} wheel: {(result.Success ? "done" : result.Message)}");
        if (!result.Success) _tray?.ShowBalloon("Could not change the wheel", result.Message);
    }

    private System.Windows.Threading.DispatcherTimer? _hiddenGameTimer;

    /// <summary>Every few seconds: if the wheel was hidden for a game and that game has now closed, give the wheel back (one permission prompt).</summary>
    private void StartHiddenGameWatch()
    {
        _hiddenGameTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _hiddenGameTimer.Tick += async (_, _) =>
        {
            if (_services is not { } s || !_visibility.HiddenByProfile || _visibility.HiddenFor is not { } key) return;
            bool running = WheelVisibility.GameRunning(s.Store.Get(key), RunningProcesses.Names());
            if (!_visibility.ShouldRestore(s.WheelHidden, running)) return;
            _visibility.MarkRestoreTried();
            _tray?.ShowBalloon("The game has closed: showing the wheel again", "Windows will ask for permission.");
            var result = await s.SetWheelHiddenAsync(false);
            _visibility.Done(false, result.Success, key);
            AppLog.Info($"The game for \"{key}\" has closed: show wheel: {(result.Success ? "done" : result.Message)}");
            if (!result.Success) _tray?.ShowBalloon("Could not show the wheel again", result.Message);
        };
        _hiddenGameTimer.Start();
    }

    /// <summary>Hide the wheel from games, or show it again, from the tray menu.</summary> One permission prompt; the result comes back as a notification.</summary>
    private async void ToggleWheelHidden()
    {
        if (_services is not { } s) return;
        bool hide = !s.WheelHidden;
        _tray?.ShowBalloon(hide ? "Hiding the wheel" : "Showing the wheel", "Windows will ask for permission.");
        var result = await s.SetWheelHiddenAsync(hide);
        AppLog.Info($"Tray: {(hide ? "hide" : "show")} wheel: {(result.Success ? "done" : result.Message)}");
        _tray?.ShowBalloon(result.Success ? (hide ? "Wheel hidden from games" : "Wheel visible again") : "Could not change it", result.Message);
    }

    /// <summary>The profiles offered in the tray menu: every valid one, with the one in use ticked.</summary>
    private System.Collections.Generic.IReadOnlyList<(string Name, bool Current)> TrayProfiles()
    {
        if (_services is not { } s) return [];
        string current = s.Switcher.ActiveKey ?? s.Settings.Profile;
        return s.Store.Files
            .Select(f => System.IO.Path.GetFileNameWithoutExtension(f))
            .Where(k => s.Store.Get(k) is not null)
            .Select(k => (k, string.Equals(k, current, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    // Wheel-button combinations: rebuilt from the settings whenever they change, fed by the mapper's frames.
    private sealed record ComboSet(ComboDetector Pause, ComboDetector Next, ComboDetector Previous);
    private volatile ComboSet? _combos;

    private void BuildCombos()
    {
        if (_services is not { } s || !s.Settings.Combos.Enabled) { _combos = null; return; }
        var c = s.Settings.Combos;
        _combos = new ComboSet(new ComboDetector(c.Pause, c.HoldMs), new ComboDetector(c.NextProfile, c.HoldMs), new ComboDetector(c.PreviousProfile, c.HoldMs));
    }

    /// <summary>Runs on the mapper thread about 30 times a second.</summary>
    private void OnFrame(MapperSnapshot snap)
    {
        if (_combos is not { } d) return;
        var down = ButtonNames.Down(snap.Input, snap.Device);
        long now = Environment.TickCount64;
        if (d.Pause.Update(down, now)) _window?.Dispatcher.BeginInvoke(TogglePause);
        if (d.Next.Update(down, now)) _window?.Dispatcher.BeginInvoke(() => CycleProfile(+1));
        if (d.Previous.Update(down, now)) _window?.Dispatcher.BeginInvoke(() => CycleProfile(-1));
    }

    private void CycleProfile(int direction)
    {
        string? name = _window?.CycleProfile(direction);
        if (name is null) return;
        AppLog.Info($"Profile changed to \"{name}\" by a wheel button combination.");
        _tray?.ShowBalloon("Profile changed", $"Now using \"{name}\".");
    }

    private void TogglePause()
    {
        if (_services is not { } s || !s.Service.IsRunning) return;
        s.Service.Paused = !s.Service.Paused;
        AppLog.Info(s.Service.Paused ? "Controller paused by the shortcut." : "Controller resumed by the shortcut.");
        _tray?.ShowBalloon(s.Service.Paused ? "Controller paused" : "Controller resumed",
            s.Service.Paused ? "Every control is released until you press the shortcut again." : "The wheel is driving the controller again.");
    }

    /// <summary>Apply the shortcut and focus options from the settings (called at start and whenever they change in Settings).</summary>
    private void ApplyInputOptions()
    {
        if (_services is not { } s) return;
        s.Service.ReleaseWhenGameNotInFront = s.Settings.ReleaseWhenGameNotInFront;
        s.Service.AutoCentre = s.Settings.AutoCentre;
        BuildCombos();
        ApplyModeOptions(s);
        s.HotkeyProblem = "";
        if (_hotkeys is null) return;

        if (!s.Settings.Hotkey.Enabled)
        {
            _hotkeys.Unregister();
            if (s.Service.Paused) s.Service.Paused = false;   // switching the shortcut off must never leave the controller stuck paused
            return;
        }
        if (!Hotkey.TryParse(s.Settings.Hotkey.Combo, out Hotkey? key, out string? why))
        {
            _hotkeys.Unregister();
            s.HotkeyProblem = "That shortcut cannot be used: " + why;
        }
        else if (!_hotkeys.Register(key!))
        {
            s.HotkeyProblem = $"{key} is already used by another program. Pick a different one.";
            AppLog.Warn(s.HotkeyProblem);
        }
        else AppLog.Info($"Pause shortcut registered: {key}.");
    }

    /// <summary>Whether another copy of the app (another wheel) is still running.</summary>
    private bool OtherCopyRunning()
    {
        for (int n = 1; n <= InstancePlan.MaxInstances; n++)
        {
            if (n == _instance) continue;
            if (Mutex.TryOpenExisting($@"Local\G923Mapper.Instance.{n}", out Mutex? other)) { other.Dispose(); return true; }
        }
        return false;
    }

    /// <summary>Give the wheel back to other programs if HidHide is hiding it and the user wants that. One permission prompt.</summary>
    private void RestoreWheelOnExit()
    {
        if (_services is not { } s) return;
        if (!s.Settings.UnhideWheelOnExit) { AppLog.Info("Exit: not showing the wheel again (the setting is off)."); return; }
        if (!s.HidHide.IsInstalled) { AppLog.Info("Exit: HidHide is not installed, nothing to restore."); return; }
        if (s.PrimaryIds is not { } ids) { AppLog.Warn("Exit: the wheel's USB ids are unknown, so it was not unhidden."); return; }
        try
        {
            bool? answer = s.HidHide.IsWheelHidden(ids.Vid, ids.Pid);
            bool hidden = answer == true;
            bool other = OtherCopyRunning();
            if (!WheelRestorePolicy.UnhideOnExit(s.Settings.UnhideWheelOnExit, s.HidHide.IsInstalled, hidden, other))
            {
                AppLog.Info(other ? "Exit: another copy of the app is running, so the wheel was left as it is."
                    : answer is null ? "Exit: HidHide did not answer, so the wheel was left as it is."
                    : "Exit: the wheel is not hidden, nothing to restore.");
                return;
            }
            HidHideResult result = s.HidHide.Unhide(ids.Vid, ids.Pid, Environment.ProcessPath ?? "");
            AppLog.Info("Showing the wheel again on exit: " + (result.Success ? "done." : result.Message));
        }
        catch (Exception ex) { AppLog.Warn("Could not show the wheel again on exit: " + ex.Message); }
    }

    private void ExitApp()
    {
        Dispatcher.Invoke(() =>
        {
            if (_window is not null) _window.Exiting = true;
            _services?.Switcher.Dispose();
            _hotkeys?.Dispose();
            _modeHotkeys?.Dispose();
            _modeInputs?.Dispose();
            _nativeGameTimer?.Stop();
            _autoUpdate?.Dispose();
            _services?.Service.Stop();       // removes the virtual pad and releases any held keys
            RestoreWheelOnExit();
            _tray?.Dispose();
            _services?.Store.Dispose();
            _window?.Close();
            ApplyStagedAtExit();
            AppLog.Info("G923 Mapper exiting.");
            try { _showEvent?.Dispose(); } catch (ObjectDisposedException) { }
            foreach (Mutex m in _mutexes) { try { m.ReleaseMutex(); } catch (ApplicationException) { } m.Dispose(); }
            _mutexes.Clear();
            Shutdown();
        });
    }
}




using System.Diagnostics;
using System.ServiceProcess;

namespace G923Mapper;

/// <summary>Looks at the real system and fills in <see cref="HealthFacts"/> for the pure rules in <see cref="HealthEvaluator"/>.</summary>
public static class HealthProbe
{
    /// <param name="wheelHidden">Whether the wheel is currently hidden by HidHide (cached by the caller; asking HidHide is slow).</param>
    /// <param name="missingExtraDevices">Names of configured extra devices that are not currently plugged in.</param>
    public static HealthFacts Gather(MapperService service, ProfileStore store, HidHideClient hidHide,
        bool wheelHidden, IReadOnlyList<string> missingExtraDevices)
    {
        (bool installed, bool running) = Service("ViGEmBus");
        var snap = service.Latest;
        var processes = RunningNames();

        var invalid = store.Files
            .Where(f => store.Get(f) is null || store.LastResult(f) is { Errors.Count: > 0 })
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToList();

        var pads = CachedPads();
        int copies = 0;
        for (int n = 1; n <= InstancePlan.MaxInstances; n++)
            if (Mutex.TryOpenExisting($@"Local\G923Mapper.Instance.{n}", out Mutex? m)) { m.Dispose(); copies++; }

        bool keyboardActive = snap is not null && store.Get(snap.ProfileName) is { } active && ProfileSafety.KeyboardAllowed(active);

        return new HealthFacts
        {
            ViGEmInstalled = installed,
            ViGEmRunning = running,
            MapperRunning = service.IsRunning,
            MapperError = service.State == MapperState.Error,
            MapperStatus = service.Status,
            WheelFound = service.State == MapperState.Running,
            WheelName = service.WheelName,
            WheelReporting = snap?.Raw.Live == true,
            WheelUncalibrated = service.State == MapperState.Running && !service.WheelCalibrated,
            FfbEnabled = service.Force.Enabled,
            FfbSupported = service.ForceSupported,
            FfbActive = service.ForceActive,
            FfbMessage = service.ForceMessage,
            HidHideInstalled = hidHide.IsInstalled,
            WheelHidden = wheelHidden,
            WheelCalibrationLopsided = snap?.Device.Axes.TryGetValue("wheel", out var wa) == true && wa.Signed
                                       && CalibrationAnalyzer.IsLopsided(wa.Min, wa.Centre, wa.Max),
            Conflicts = CachedConflicts(store, processes),
            VirtualPadsConnected = pads?.Count(p => p.Connected),
            VirtualPadsLeftover = pads is null ? null : VirtualPadText.Leftovers(pads).Count,
            AppCopiesRunning = Math.Max(1, copies),
            GHubRunning = processes.Contains("lghub") || processes.Contains("lghub_agent"),
            AntiCheatRunning = processes.Contains("beservice"),
            KeyboardOutputActive = keyboardActive,
            InvalidProfiles = invalid,
            MissingExtraDevices = missingExtraDevices,
        };
    }

    private static IReadOnlyList<HealthItem> _conflicts = [];
    private static long _conflictsAt = -1_000_000;
    private static int _conflictsBusy;

    /// <summary>
    /// Other software that fights over the controller, from a cache refreshed in the background about every 20 seconds (it looks at running
    /// programs and the folders of running games, which is slow enough not to do on the UI thread).
    /// </summary>
    private static IReadOnlyList<HealthItem> CachedConflicts(ProfileStore store, HashSet<string> processNames)
    {
        if (Environment.TickCount64 - _conflictsAt > 20000 && Interlocked.CompareExchange(ref _conflictsBusy, 1, 0) == 0)
        {
            var names = processNames.ToList();
            var gameExes = store.Files.Select(f => store.Get(f)).Where(p => p is not null).SelectMany(p => p!.Match.Exe).ToList();
            Task.Run(() =>
            {
                try { _conflicts = ConflictRules.Evaluate(names, GameFolderScanner.Scan(gameExes)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
                finally { _conflictsAt = Environment.TickCount64; Volatile.Write(ref _conflictsBusy, 0); }
            });
        }
        return _conflicts;
    }

    private static IReadOnlyList<VirtualPadInfo>? _pads;
    private static long _padsAt = -1_000_000;
    private static int _padsBusy;

    /// <summary>
    /// The virtual controllers, from a cache refreshed in the background about every 20 seconds (asking Windows takes a moment, and this
    /// is called often from the UI). Null until the first answer arrives.
    /// </summary>
    private static IReadOnlyList<VirtualPadInfo>? CachedPads()
    {
        if (Environment.TickCount64 - _padsAt > 20000 && Interlocked.CompareExchange(ref _padsBusy, 1, 0) == 0)
        {
            Task.Run(() =>
            {
                try { _pads = new VirtualPadClient().List(); }
                finally { _padsAt = Environment.TickCount64; Volatile.Write(ref _padsBusy, 0); }
            });
        }
        return _pads;
    }

    /// <summary>Forget the cached list so the next health check asks Windows again (after removing leftovers).</summary>
    public static void RefreshVirtualPads() => _padsAt = -1_000_000;

    /// <summary>Whether a Windows service exists and is running. A missing service is (false, false).</summary>
    public static (bool installed, bool running) Service(string name)
    {
        try
        {
            using var sc = new ServiceController(name);
            return (true, sc.Status == ServiceControllerStatus.Running);
        }
        catch (InvalidOperationException) { return (false, false); }
        catch (System.ComponentModel.Win32Exception) { return (false, false); }
    }

    private static HashSet<string> RunningNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try { names.Add(p.ProcessName); }
            catch (InvalidOperationException) { }
            finally { p.Dispose(); }
        }
        return names;
    }
}

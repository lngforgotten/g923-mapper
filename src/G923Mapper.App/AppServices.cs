using System;
using System.Threading.Tasks;

namespace G923Mapper.App;

/// <summary>Everything the windows share, so each window takes one object instead of ten.</summary>
public sealed class AppServices
{
    public required MapperService Service { get; init; }
    public required ProfileStore Store { get; init; }
    public required DeviceRegistry Devices { get; init; }
    public required AppSettings Settings { get; init; }
    public required AutoProfileSwitcher Switcher { get; init; }
    public required HidHideClient HidHide { get; init; }
    public required string ProfileDir { get; init; }
    public required string DevicesPath { get; init; }
    /// <summary>Which copy of the app this is (1 = the first; extra copies each drive the next wheel).</summary>
    public int Instance { get; init; } = 1;
    /// <summary>Health tips the user asked not to see again (shared by every copy of the app).</summary>
    public IgnoredTipsFile IgnoredTips { get; } = new(AppSettings.IgnoredTipsPath);
    /// <summary>A window handle DirectInput can use for its cooperative level.</summary>
    public required Func<IntPtr> Hwnd { get; init; }

    /// <summary>Re-reads the shortcut and "release when the game is not in front" options from the settings and applies them.</summary>
    public Action? ApplyInputOptions { get; set; }
    /// <summary>The message from the last attempt to register the shortcut (empty when it worked or is off).</summary>
    public string HotkeyProblem { get; set; } = "";
    /// <summary>The same for the shortcut that switches modes.</summary>
    public string ModeHotkeyProblem { get; set; } = "";

    /// <summary>A newer version the automatic check found (null if none), and a callback for the window to show it.</summary>
    public UpdateInfo? AvailableUpdate { get; set; }
    public Action? UpdateChanged { get; set; }
    /// <summary>Where the update is: found, being downloaded, ready to swap in, or the download failed.</summary>
    public UpdatePhase UpdatePhase { get; set; }
    /// <summary>The version the app has just updated itself to (set for the first run of the new copy), so Home can say so; empty otherwise.</summary>
    public string JustUpdatedTag { get; set; } = "";
    /// <summary>How the update's download is going; <see cref="DownloadText"/> is it in words.</summary>
    public DownloadMeter Download { get; } = new();
    public static TimeSpan Clock => TimeSpan.FromMilliseconds(Environment.TickCount64);
    public string DownloadText => Download.Describe(Clock);
    public string UpdateProblem { get; set; } = "";
    /// <summary>Download the new version now and restart into it (the "Install now" button).</summary>
    public Func<System.Threading.Tasks.Task>? InstallUpdateNow { get; set; }
    /// <summary>Swap in the update that is ready and start the new version (the "Restart now" button).</summary>
    public Action? RestartForUpdate { get; set; }

    /// <summary>Whether HidHide currently hides the wheel. Cached, because asking HidHide takes a moment.</summary>
    public bool WheelHidden { get; private set; }

    /// <summary>The wheel's USB ids: from the running device map, else the built-in one.</summary>
    public (int Vid, int Pid)? PrimaryIds =>
        Service.WheelUsbIds ?? Service.Latest?.Device.UsbIds ?? (Devices.Fallback?.UsbIds);

    /// <summary>Ask HidHide (off the UI thread) and remember the answer.</summary>
    public Task RefreshHiddenStateAsync() => Task.Run(() =>
    {
        if (PrimaryIds is not { } ids || !HidHide.IsInstalled) { WheelHidden = false; return; }
        WheelHidden = HidHide.IsWheelHidden(ids.Vid, ids.Pid) == true;
    });

    public void SetHiddenState(bool hidden) => WheelHidden = hidden;

    /// <summary>
    /// The calibration wizard in a window: pauses the mapper, runs it, and on Save adds the wheel to the device list and re-checks every
    /// profile against it. Returns whether a calibration was saved.
    /// </summary>
    public bool RunCalibration(System.Windows.Window owner)
    {
        bool wasRunning = Service.IsRunning;
        Service.Stop();    // calibration reads the wheel itself; the pad is not needed meanwhile
        var dlg = new CalibrationWindow(Devices, DevicesPath, Hwnd()) { Owner = owner };
        bool saved = dlg.ShowDialog() == true && dlg.SavedMap is not null;
        if (saved)
        {
            Devices.Add(dlg.SavedMap!);
            Store.ReloadAll();   // profiles are re-checked against the new device map
        }
        if (wasRunning) Service.Start();
        return saved;
    }

    /// <summary>
    /// Hide the wheel from games with HidHide, or show it again. Asks for administrator permission once. Used by Settings, the Devices tab
    /// and the tray menu so they all behave the same.
    /// </summary>
    public async Task<HidHideResult> SetWheelHiddenAsync(bool hide)
    {
        if (!HidHide.IsInstalled) return new(false, "HidHide is not installed.");
        if (PrimaryIds is not { } ids) return new(false, "Start the mapper with the wheel plugged in first.");
        string app = Environment.ProcessPath ?? "";
        HidHideResult result = await Task.Run(() => hide ? HidHide.Hide(ids.Vid, ids.Pid, app) : HidHide.Unhide(ids.Vid, ids.Pid, app));
        if (result.Success) SetHiddenState(hide);
        await RefreshHiddenStateAsync();
        return result;
    }
}


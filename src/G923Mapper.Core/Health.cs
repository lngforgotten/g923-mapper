namespace G923Mapper;

public enum HealthLevel { Ok = 0, Info = 1, Warning = 2, Problem = 3 }

/// <summary>What the UI can offer to do about a finding.</summary>
public enum HealthAction { None, GetViGEmBus, GetHidHide, OpenSettings, OpenLogs }

public sealed record HealthItem(string Title, HealthLevel Level, string Detail, HealthAction Action = HealthAction.None);

/// <summary>Everything the health check looks at, gathered by the caller so the rules below stay pure.</summary>
public sealed class HealthFacts
{
    public bool ViGEmInstalled { get; init; }
    public bool ViGEmRunning { get; init; }

    public bool MapperRunning { get; init; }
    public bool MapperError { get; init; }
    public string MapperStatus { get; init; } = "";

    public bool WheelFound { get; init; }
    public string WheelName { get; init; } = "";
    public bool WheelReporting { get; init; }
    /// <summary>The connected wheel has no calibrated entry (typical for a PlayStation edition on its first run), so a built-in layout is only a guess.</summary>
    public bool WheelUncalibrated { get; init; }

    public bool FfbEnabled { get; init; }
    public bool FfbSupported { get; init; }
    public bool FfbActive { get; init; }
    public string FfbMessage { get; init; } = "";

    public bool HidHideInstalled { get; init; }
    public bool WheelHidden { get; init; }

    /// <summary>The saved wheel calibration has very different travel left and right (a lock was not turned fully to its stop).</summary>
    public bool WheelCalibrationLopsided { get; init; }

    /// <summary>Virtual Xbox 360 controllers connected now (from every program), or null if Windows could not be asked.</summary>
    public int? VirtualPadsConnected { get; init; }
    /// <summary>How many copies of this app are running (each makes one virtual controller).</summary>
    public int AppCopiesRunning { get; init; } = 1;
    /// <summary>Entries left behind in Device Manager by earlier sessions, or null if unknown.</summary>
    public int? VirtualPadsLeftover { get; init; }

    public bool GHubRunning { get; init; }
    public bool AntiCheatRunning { get; init; }
    public bool KeyboardOutputActive { get; init; }

    /// <summary>Findings about other software that fights over the controller (see <see cref="ConflictRules"/>).</summary>
    public IReadOnlyList<HealthItem> Conflicts { get; init; } = [];

    public IReadOnlyList<string> InvalidProfiles { get; init; } = [];
    public IReadOnlyList<string> MissingExtraDevices { get; init; } = [];
}

public static class HealthEvaluator
{
    /// <summary>Findings, most serious first. Always includes the positive ones so the panel is reassuring when all is well.</summary>
    public static IReadOnlyList<HealthItem> Evaluate(HealthFacts f)
    {
        var items = new List<HealthItem>();

        // Virtual controller driver
        if (!f.ViGEmInstalled)
            items.Add(new("Virtual controller driver is missing", HealthLevel.Problem,
                "ViGEmBus is what lets this app present your wheel to games as an Xbox 360 controller. Install it, then restart the app.",
                HealthAction.GetViGEmBus));
        else if (!f.ViGEmRunning)
            items.Add(new("Virtual controller driver is not running", HealthLevel.Problem,
                "ViGEmBus is installed but its service is stopped. Restart Windows, or reinstall the driver.", HealthAction.GetViGEmBus));
        else
            items.Add(new("Virtual controller driver is ready", HealthLevel.Ok, "ViGEmBus is installed and running."));

        // Mapper itself
        if (f.MapperError)
            items.Add(new("The mapper hit a problem", HealthLevel.Problem, f.MapperStatus, HealthAction.OpenLogs));
        else if (!f.MapperRunning)
            items.Add(new("The mapper is stopped", HealthLevel.Info, "No virtual controller exists while it is stopped. Press Start."));

        // Wheel
        if (f.MapperRunning && !f.MapperError)
        {
            if (!f.WheelFound)
                items.Add(new("Wheel not found", HealthLevel.Warning,
                    "Plug the wheel in and connect its power supply. If it is plugged in, close any program that might be holding it exclusively."));
            else if (!f.WheelReporting)
                items.Add(new("Wheel is connected but quiet", HealthLevel.Info,
                    $"{f.WheelName} has not sent any input yet. This is normal until you move it a little."));
            else
                items.Add(new("Wheel is working", HealthLevel.Ok, $"{f.WheelName} is sending input."));
        }

        if (f.MapperRunning && f.WheelFound && f.WheelUncalibrated)
            items.Add(new("This wheel has not been calibrated", HealthLevel.Warning,
                $"{f.WheelName} has no saved calibration, so its axes and buttons are only guessed from the Xbox edition's layout and may be wrong (the PlayStation edition lays them out differently). " +
                "Run Calibrate: it measures this wheel and fixes that.", HealthAction.OpenSettings));

        // Virtual controllers: more than this app made means another program (x360ce, DS4Windows...) is making one too.
        if (f.MapperRunning && f.VirtualPadsConnected is int connected && connected > Math.Max(1, f.AppCopiesRunning))
            items.Add(new("More virtual controllers than expected", HealthLevel.Warning,
                $"{connected} virtual Xbox 360 controllers are connected but this app only makes {Math.Max(1, f.AppCopiesRunning)}. " +
                "Another program such as x360ce or DS4Windows may be making one too, so a game can see double input. Close it, or see the list in Settings.",
                HealthAction.OpenSettings));
        if (f.VirtualPadsLeftover is int leftover and > 0)
            items.Add(new(leftover == 1 ? "A leftover virtual controller" : $"{leftover} leftover virtual controllers", HealthLevel.Info,
                "Earlier sessions left unplugged controller entries in Device Manager. They are harmless but can confuse some games. Settings has a button to remove them.",
                HealthAction.OpenSettings));

        // Calibration that was recorded without reaching a stop makes one side of the wheel far more sensitive.
        if (f.WheelCalibrationLopsided)
            items.Add(new("Wheel calibration looks lopsided", HealthLevel.Warning,
                "Left and right travel are very different, so steering feels sharp on one side and slow on the other. " +
                "Run Calibrate again and turn the wheel all the way to the stop on both sides.", HealthAction.OpenSettings));

        // Force feedback
        if (f.FfbEnabled && f.MapperRunning && f.WheelFound)
        {
            if (!f.FfbSupported)
                items.Add(new("No force feedback on this wheel", HealthLevel.Info, "The wheel does not report force-feedback support, so rumble cannot be passed to it."));
            else if (!f.FfbActive)
                items.Add(new("Force feedback could not start", HealthLevel.Warning,
                    string.IsNullOrWhiteSpace(f.FfbMessage)
                        ? "Another program probably has exclusive control of the wheel (Logitech G HUB is a common one)."
                        : f.FfbMessage, HealthAction.OpenSettings));
            else
                items.Add(new("Force feedback is on", HealthLevel.Ok, "Game rumble and the centring spring are being sent to the wheel."));
        }

        // Double input
        if (!f.HidHideInstalled)
            items.Add(new("Games may see the wheel twice", HealthLevel.Info,
                "Without HidHide a game can see both your real wheel and the virtual controller, so steering can fight itself. " +
                "HidHide is optional but recommended; it hides the real wheel from games.", HealthAction.GetHidHide));
        else if (!f.WheelHidden)
            items.Add(new("The real wheel is visible to games", HealthLevel.Info,
                "HidHide is installed. Hide the wheel in Settings so games only see the virtual controller.", HealthAction.OpenSettings));
        else
            items.Add(new("The real wheel is hidden from games", HealthLevel.Ok, "Games only see the virtual controller."));

        // Other software
        if (f.GHubRunning)
            items.Add(new("Logitech G HUB is running", HealthLevel.Info,
                "Usually fine. If the wheel behaves oddly or force feedback will not start, close G HUB."));

        // Anti-cheat (spec: be careful with GTA Online)
        if (f.AntiCheatRunning)
            items.Add(new(f.KeyboardOutputActive ? "Keyboard output is on while anti-cheat is running" : "Anti-cheat (BattlEye) is running",
                f.KeyboardOutputActive ? HealthLevel.Warning : HealthLevel.Info,
                f.KeyboardOutputActive
                    ? "Simulated key presses are more likely to look like automation than a controller does. That is a judgement call, not a verified rule, but turn keyboard output off for online games."
                    : "This app only creates a virtual controller from outside the game and never touches the game. Whether anti-cheat ever flags a virtual controller is not known, so try Story Mode first for GTA."));

        // Profiles and extra devices
        if (f.InvalidProfiles.Count > 0)
            items.Add(new("Some profiles have errors", HealthLevel.Warning,
                $"{string.Join(", ", f.InvalidProfiles)} cannot be used until fixed. Open the profile to see what is wrong."));
        if (f.MissingExtraDevices.Count > 0)
            items.Add(new("An extra device is not connected", HealthLevel.Info,
                $"{string.Join(", ", f.MissingExtraDevices)} is set up but not plugged in. Rules that use it will read as released."));

        items.AddRange(f.Conflicts);
        return items.OrderByDescending(i => (int)i.Level).ToList();
    }

    public static HealthLevel Overall(IReadOnlyList<HealthItem> items) =>
        items.Count == 0 ? HealthLevel.Ok : items.Max(i => i.Level);

    /// <summary>Short text for the badge: how many things need attention (Warning or worse).</summary>
    public static string Summary(IReadOnlyList<HealthItem> items)
    {
        int problems = items.Count(i => i.Level == HealthLevel.Problem);
        int warnings = items.Count(i => i.Level == HealthLevel.Warning);
        int tips = items.Count(i => i.Level == HealthLevel.Info);
        if (problems > 0) return problems == 1 ? "1 problem" : $"{problems} problems";
        if (warnings > 0) return warnings == 1 ? "1 to check" : $"{warnings} to check";
        if (tips > 0) return tips == 1 ? "1 tip" : $"{tips} tips";
        return "All good";
    }
}

namespace G923Mapper;

public enum SetupState
{
    /// <summary>Finished.</summary>
    Done,
    /// <summary>Needs doing, and nothing is in the way.</summary>
    ToDo,
    /// <summary>Needs doing, but an earlier step comes first.</summary>
    Waiting,
    /// <summary>Not required; worth a look.</summary>
    Optional,
}

public enum SetupAction { None, GetDriver, OpenDevices, Calibrate, ChooseProfile, HideWheel, GetHidHide, TestInWindows }

/// <param name="Required">A step the app cannot work without.</param>
public sealed record SetupStep(string Id, string Title, string Detail, SetupState State, bool Required, SetupAction Action = SetupAction.None);

/// <summary>What the setup assistant looks at, gathered by the caller so the rules below stay pure.</summary>
public sealed class SetupFacts
{
    public bool DriverInstalled { get; init; }
    public bool DriverRunning { get; init; }
    public bool WheelFound { get; init; }
    public bool WheelReporting { get; init; }
    public string WheelName { get; init; } = "";
    public bool WheelCalibrated { get; init; }
    public bool PlayStationWheel { get; init; }
    public bool ProfileValid { get; init; }
    public string ProfileName { get; init; } = "";
    public bool HidHideInstalled { get; init; }
    public bool WheelHidden { get; init; }
}

public static class SetupChecklist
{
    public const string Driver = "driver", Wheel = "wheel", Calibrate = "calibrate", Profile = "profile", Hide = "hide", Test = "test";

    /// <summary>The steps in order, each with where it stands right now.</summary>
    public static IReadOnlyList<SetupStep> Evaluate(SetupFacts f)
    {
        var steps = new List<SetupStep>();

        // 1. The virtual controller driver
        bool driverOk = f.DriverInstalled && f.DriverRunning;
        steps.Add(driverOk
            ? new(Driver, "Virtual controller driver", "ViGEmBus is installed and running.", SetupState.Done, true)
            : f.DriverInstalled
                ? new(Driver, "Virtual controller driver", "ViGEmBus is installed but not running. Restart Windows, or reinstall it.", SetupState.ToDo, true, SetupAction.GetDriver)
                : new(Driver, "Virtual controller driver",
                      "Games need this to see your wheel as an Xbox controller. Install ViGEmBus (it asks for administrator permission), then restart this app.",
                      SetupState.ToDo, true, SetupAction.GetDriver));

        // 2. The wheel
        if (f.WheelFound && f.WheelReporting)
            steps.Add(new(Wheel, "Your wheel", $"{f.WheelName} is connected and sending input.", SetupState.Done, true));
        else if (f.WheelFound)
            steps.Add(new(Wheel, "Your wheel", $"{f.WheelName} is connected. Turn the wheel a little so it starts reporting.", SetupState.ToDo, true));
        else
            steps.Add(new(Wheel, "Your wheel",
                "Plug the wheel in and connect its power supply. If it is plugged in, close any program that might be holding it. The Devices tab shows what Windows can see.",
                driverOk ? SetupState.ToDo : SetupState.Waiting, true, SetupAction.OpenDevices));

        // 3. Calibration
        if (!f.WheelFound)
            steps.Add(new(Calibrate, "Calibrate", "Measures your wheel and pedals so every control is read correctly. Needs the wheel connected first.", SetupState.Waiting, true));
        else if (f.WheelCalibrated)
            steps.Add(new(Calibrate, "Calibrate", "This wheel has been calibrated. You can run it again any time.", SetupState.Done, true, SetupAction.Calibrate));
        else
            steps.Add(new(Calibrate, "Calibrate",
                f.PlayStationWheel
                    ? "This looks like the PlayStation edition, whose layout is measured rather than assumed. Calibrate once (about a minute): turn lock to lock, press each pedal, then each button."
                    : "Measures your wheel and pedals so every control is read correctly. Takes about a minute: turn lock to lock, press each pedal, then each button.",
                SetupState.ToDo, true, SetupAction.Calibrate));

        // 4. A profile
        steps.Add(f.ProfileValid
            ? new(Profile, "Profile", $"Using \"{f.ProfileName}\". Pick another here, or make your own on the Profiles tab. A game's profile switches on by itself when the game starts.",
                  SetupState.Done, false, SetupAction.ChooseProfile)
            : new(Profile, "Profile", "Choose a profile for the game you want to play.", SetupState.ToDo, false, SetupAction.ChooseProfile));

        // 5. Hide the real wheel (optional)
        if (!f.HidHideInstalled)
            steps.Add(new(Hide, "Hide the real wheel from games (optional)",
                "Without this a game can see both your real wheel and the virtual controller, so steering can fight itself. HidHide fixes that; it is free and optional.",
                SetupState.Optional, false, SetupAction.GetHidHide));
        else if (f.WheelHidden)
            steps.Add(new(Hide, "Hide the real wheel from games (optional)", "The real wheel is hidden, so games only see the virtual controller.", SetupState.Done, false, SetupAction.HideWheel));
        else
            steps.Add(new(Hide, "Hide the real wheel from games (optional)",
                "Recommended: stops a game seeing both your real wheel and the virtual controller. Asks for administrator permission once.",
                SetupState.Optional, false, SetupAction.HideWheel));

        // 6. Check it works (optional)
        steps.Add(new(Test, "Check it works (optional)",
            "Windows has its own controller test. You should see an Xbox 360 controller that moves when you turn the wheel and press the pedals.",
            SetupState.Optional, false, SetupAction.TestInWindows));

        return steps;
    }

    /// <summary>Everything the app cannot work without is done.</summary>
    public static bool IsReady(IReadOnlyList<SetupStep> steps) => steps.Where(s => s.Required).All(s => s.State == SetupState.Done);

    public static string Summary(IReadOnlyList<SetupStep> steps)
    {
        var required = steps.Where(s => s.Required).ToList();
        int done = required.Count(s => s.State == SetupState.Done);
        if (done == required.Count) return "All set. The rest is optional.";
        SetupStep? next = required.FirstOrDefault(s => s.State == SetupState.ToDo);
        return $"{done} of {required.Count} essential steps done." + (next is null ? "" : $" Next: {next.Title.ToLowerInvariant()}.");
    }
}

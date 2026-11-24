namespace G923Mapper;

public enum RemapKind { Steering, Trigger, Button }

/// <summary>One control of the virtual controller the user is asked to choose a wheel control for.</summary>
/// <param name="Target">The rule target: LeftStickX, RightTrigger, Button:A ...</param>
/// <param name="Hint">Plain words saying where it is on a controller and what it is usually for.</param>
public sealed record RemapStep(string Id, string Xbox, string PlayStation, string Target, RemapKind Kind, string Hint)
{
    public string Label(PadKind kind) => kind == PadKind.DualShock4 ? PlayStation : Xbox;
}

public static class RemapSteps
{
    /// <summary>The order of the walk-through: driving controls first, then the face buttons, then the rest.</summary>
    public static readonly IReadOnlyList<RemapStep> All =
    [
        new("steer", "Steering (left stick)", "Steering (left stick)", "LeftStickX", RemapKind.Steering,
            "The wheel itself. Most racing games steer with the left stick, so turn the wheel a little to the left or right."),
        new("rt", "Right trigger (RT)", "Right trigger (R2)", "RightTrigger", RemapKind.Trigger,
            "The trigger under your right index finger on a controller. In most racing games it is the accelerator."),
        new("lt", "Left trigger (LT)", "Left trigger (L2)", "LeftTrigger", RemapKind.Trigger,
            "The trigger under your left index finger on a controller. In most racing games it is the brake."),
        new("a", "A button", "Cross (✕)", "Button:A", RemapKind.Button,
            "The bottom one of the four coloured buttons on the right (green A on Xbox, Cross on PlayStation). Usually confirm or jump."),
        new("b", "B button", "Circle (○)", "Button:B", RemapKind.Button,
            "The right-hand one of the four buttons (red B on Xbox, Circle on PlayStation). Usually back or cancel."),
        new("x", "X button", "Square (□)", "Button:X", RemapKind.Button,
            "The left-hand one of the four buttons (blue X on Xbox, Square on PlayStation). Often reload or handbrake."),
        new("y", "Y button", "Triangle (△)", "Button:Y", RemapKind.Button,
            "The top one of the four buttons (yellow Y on Xbox, Triangle on PlayStation). Often change view or get in a vehicle."),
        new("lb", "Left bumper (LB)", "L1", "Button:LeftShoulder", RemapKind.Button,
            "The small shoulder button above the left trigger. A good place for a paddle shifter (down shift)."),
        new("rb", "Right bumper (RB)", "R1", "Button:RightShoulder", RemapKind.Button,
            "The small shoulder button above the right trigger. A good place for a paddle shifter (up shift)."),
        new("back", "Back / View", "Share", "Button:Back", RemapKind.Button,
            "The small button just left of the centre (View on Xbox, Share on PlayStation). Often the map or a menu."),
        new("start", "Start / Menu", "Options", "Button:Start", RemapKind.Button,
            "The small button just right of the centre (Menu on Xbox, Options on PlayStation). Usually the pause menu."),
        new("guide", "Guide (Xbox button)", "PS button", "Button:Guide", RemapKind.Button,
            "The big logo button in the middle of the controller. Most games do nothing with it, so skip it if unsure."),
        new("ls", "Left stick click", "L3", "Button:LeftThumb", RemapKind.Button,
            "Pressing the left stick straight down like a button. Often sprint or horn."),
        new("rs", "Right stick click", "R3", "Button:RightThumb", RemapKind.Button,
            "Pressing the right stick straight down like a button. Often melee, look behind or a camera toggle."),
        new("up", "D-pad up", "D-pad up", "Button:DPadUp", RemapKind.Button,
            "The top arm of the cross-shaped pad on the left. Often menus, radio or quick items."),
        new("down", "D-pad down", "D-pad down", "Button:DPadDown", RemapKind.Button,
            "The bottom arm of the cross-shaped pad on the left."),
        new("left", "D-pad left", "D-pad left", "Button:DPadLeft", RemapKind.Button,
            "The left arm of the cross-shaped pad on the left."),
        new("right", "D-pad right", "D-pad right", "Button:DPadRight", RemapKind.Button,
            "The right arm of the cross-shaped pad on the left."),
    ];
}
/// <summary>Collects the user's choices from the "Remap all" walk-through and turns them into a profile.</summary>
public sealed class RemapBuilder
{
    private readonly DeviceMap _device;
    private readonly Dictionary<string, string> _from = new(StringComparer.OrdinalIgnoreCase);   // step id -> input name

    /// <param name="device">The wheel's map, used to tell an axis from a button when the control picked is a pedal.</param>
    public RemapBuilder(DeviceMap device) => _device = device;

    public int Assigned => _from.Count;

    public string? Get(string stepId) => _from.TryGetValue(stepId, out var f) ? f : null;

    /// <summary>Choose <paramref name="input"/> for the step. A null or blank input leaves the step unmapped.</summary>
    public void Assign(RemapStep step, string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) _from.Remove(step.Id);
        else _from[step.Id] = input.Trim();
    }

    private bool IsAxis(string input) => _device.Axes.ContainsKey(input);

    /// <summary>A strict-mode profile with one rule for each step that was given an input, in the walk-through's order.</summary>
    public Profile Build(string name)
    {
        var rules = new List<Rule>();
        foreach (RemapStep step in RemapSteps.All)
        {
            if (!_from.TryGetValue(step.Id, out string? from)) continue;
            rules.Add(RuleFactory.Make(_device, from, step.Target));
        }
        return new Profile { Name = name.Trim(), Device = DeviceRegistry.AutoDevice, Safety = ProfileSafety.Strict, Outputs = new ProfileOutputs { Pad = PadKinds.AutoText }, Rules = rules };
    }
}

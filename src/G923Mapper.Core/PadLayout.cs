namespace G923Mapper;

/// <summary>A control's place in the controller picture (a 300 x 180 drawing): its box, and whether it is drawn round.</summary>
public readonly record struct PadShape(double X, double Y, double W, double H, bool Round)
{
    public double CentreX => X + W / 2;
    public double CentreY => Y + H / 2;
}

/// <summary>
/// Where each control sits in the controller picture, by its rule target name. One table used for drawing and for pointing at a control.
/// An Xbox controller and a DualShock 4 are laid out differently (the left stick and the D-pad swap places), so each has its own.
/// </summary>
public static class PadLayout
{
    public const double Width = 300, Height = 180;

    private static Dictionary<string, PadShape> Make(PadKind kind) => kind == PadKind.DualShock4 ? DualShock : Xbox;

    private static readonly Dictionary<string, PadShape> Xbox = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LeftStick"] = new(56, 46, 48, 48, true),
        ["RightStick"] = new(162, 100, 48, 48, true),
        ["LeftTrigger"] = new(40, 2, 62, 10, false),
        ["RightTrigger"] = new(198, 2, 62, 10, false),
        ["Button:LeftShoulder"] = new(40, 15, 62, 11, false),
        ["Button:RightShoulder"] = new(198, 15, 62, 11, false),
        ["Button:Y"] = new(215, 38, 22, 22, true),
        ["Button:A"] = new(215, 82, 22, 22, true),
        ["Button:X"] = new(193, 60, 22, 22, true),
        ["Button:B"] = new(237, 60, 22, 22, true),
        ["Button:Back"] = new(122, 66, 14, 14, true),
        ["Button:Start"] = new(164, 66, 14, 14, true),
        ["Button:Guide"] = new(143, 40, 14, 14, true),
        ["Button:DPadUp"] = new(104, 100, 17, 17, false),
        ["Button:DPadDown"] = new(104, 134, 17, 17, false),
        ["Button:DPadLeft"] = new(87, 117, 17, 17, false),
        ["Button:DPadRight"] = new(121, 117, 17, 17, false),
    };

    private static readonly Dictionary<string, PadShape> DualShock = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LeftStick"] = new(84, 104, 48, 48, true),
        ["RightStick"] = new(168, 104, 48, 48, true),
        ["LeftTrigger"] = new(40, 2, 62, 10, false),
        ["RightTrigger"] = new(198, 2, 62, 10, false),
        ["Button:LeftShoulder"] = new(40, 15, 62, 11, false),
        ["Button:RightShoulder"] = new(198, 15, 62, 11, false),
        ["Button:Y"] = new(211, 38, 22, 22, true),
        ["Button:A"] = new(211, 82, 22, 22, true),
        ["Button:X"] = new(189, 60, 22, 22, true),
        ["Button:B"] = new(233, 60, 22, 22, true),
        ["Button:Back"] = new(96, 40, 14, 14, true),      // Share
        ["Button:Start"] = new(190, 40, 14, 14, true),    // Options
        ["Button:Guide"] = new(143, 118, 14, 14, true),   // PS button
        ["Button:DPadUp"] = new(58, 42, 17, 17, false),
        ["Button:DPadDown"] = new(58, 76, 17, 17, false),
        ["Button:DPadLeft"] = new(41, 59, 17, 17, false),
        ["Button:DPadRight"] = new(75, 59, 17, 17, false),
    };

    /// <summary>
    /// The shape for a rule target (LeftStickX, Button:A...). Both axes of a stick, and a click of a stick, point at that stick.
    /// Null for a target that is not on the picture (a key, a mouse button).
    /// </summary>
    public static PadShape? Of(string target, PadKind kind = PadKind.Xbox360) =>
        Make(kind).TryGetValue(KeyOf(target), out PadShape s) ? s : null;

    /// <summary>The picture control a rule target belongs to: both axes and the click of a stick are "LeftStick" / "RightStick"; the rest are their own name.</summary>
    public static string KeyOf(string target) => target.Trim() switch
    {
        "LeftStickX" or "LeftStickY" or "Button:LeftThumb" => "LeftStick",
        "RightStickX" or "RightStickY" or "Button:RightThumb" => "RightStick",
        var t => t,
    };

    public static IEnumerable<string> Targets => Xbox.Keys;
    public static IEnumerable<string> TargetsFor(PadKind kind) => Make(kind).Keys;
}

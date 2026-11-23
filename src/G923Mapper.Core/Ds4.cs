namespace G923Mapper;

/// <summary>Which virtual controller a profile presents to the game.</summary>
public enum PadKind
{
    /// <summary>An Xbox 360 controller (the default; works with nearly every PC game).</summary>
    Xbox360,
    /// <summary>A DualShock 4, for games that show PlayStation button prompts or handle it better.</summary>
    DualShock4,
}

public static class PadKinds
{
    public const string Xbox360Text = "xbox360";
    public const string DualShock4Text = "ds4";

    /// <summary>"xbox360", "ds4" (also "dualshock4", "ps4", "playstation"), case-insensitive. Empty or null means the default, Xbox 360.</summary>
    public static bool TryParse(string? text, out PadKind kind)
    {
        kind = PadKind.Xbox360;
        if (string.IsNullOrWhiteSpace(text)) return true;
        switch (text.Trim().ToLowerInvariant())
        {
            case "xbox360" or "xbox" or "x360": kind = PadKind.Xbox360; return true;
            case "ds4" or "dualshock4" or "dualshock" or "ps4" or "playstation": kind = PadKind.DualShock4; return true;
            default: return false;
        }
    }

    public static string ToText(PadKind kind) => kind == PadKind.DualShock4 ? DualShock4Text : Xbox360Text;

    /// <summary>The pad kind a profile asks for (Xbox 360 when it says nothing or something unusable).</summary>
    public static PadKind Of(Profile p) => TryParse(p.Outputs.Pad, out PadKind k) ? k : PadKind.Xbox360;
}

/// <summary>A DualShock 4's buttons, independent of the driver library so the mapping below can be tested.</summary>
public enum Ds4Button { Cross, Circle, Square, Triangle, L1, R1, L2, R2, Share, Options, L3, R3, Ps }

public enum Ds4DPad { Neutral, North, NorthEast, East, SouthEast, South, SouthWest, West, NorthWest }

/// <summary>How the profile's Xbox-style outputs become DualShock 4 outputs. The rules still name Xbox buttons; this translates them.</summary>
public static class Ds4Mapping
{
    /// <summary>The DualShock 4 button for an Xbox-style button, or null for the D-pad directions (see <see cref="DPad"/>).</summary>
    public static Ds4Button? Button(PadButton b) => b switch
    {
        PadButton.A => Ds4Button.Cross,
        PadButton.B => Ds4Button.Circle,
        PadButton.X => Ds4Button.Square,
        PadButton.Y => Ds4Button.Triangle,
        PadButton.LeftShoulder => Ds4Button.L1,
        PadButton.RightShoulder => Ds4Button.R1,
        PadButton.Back => Ds4Button.Share,
        PadButton.Start => Ds4Button.Options,
        PadButton.Guide => Ds4Button.Ps,
        PadButton.LeftThumb => Ds4Button.L3,
        PadButton.RightThumb => Ds4Button.R3,
        _ => null,
    };

    /// <summary>A stick's horizontal position: -32768..32767 becomes 0..255 with the centre at 128.</summary>
    public static byte StickX(short v) => (byte)Math.Clamp((v + 32768 + 128) >> 8, 0, 255);

    /// <summary>
    /// A stick's vertical position. The Xbox convention is positive = up; the DualShock 4 reports 0 for up and 255 for down,
    /// so the direction is flipped.
    /// </summary>
    public static byte StickY(short v) => StickX((short)Math.Clamp(-v - 1, short.MinValue, short.MaxValue));

    /// <summary>Above this the analog trigger also counts as the digital L2/R2 button, as on a real DualShock 4.</summary>
    public const byte TriggerButtonThreshold = 30;

    public static bool TriggerPressed(byte value) => value > TriggerButtonThreshold;

    /// <summary>The four D-pad buttons as the single direction a DualShock 4 reports (opposite directions cancel out).</summary>
    public static Ds4DPad DPad(bool up, bool down, bool left, bool right)
    {
        if (up && down) up = down = false;
        if (left && right) left = right = false;
        return (up, down, left, right) switch
        {
            (true, _, true, _) => Ds4DPad.NorthWest,
            (true, _, _, true) => Ds4DPad.NorthEast,
            (_, true, true, _) => Ds4DPad.SouthWest,
            (_, true, _, true) => Ds4DPad.SouthEast,
            (true, _, _, _) => Ds4DPad.North,
            (_, true, _, _) => Ds4DPad.South,
            (_, _, true, _) => Ds4DPad.West,
            (_, _, _, true) => Ds4DPad.East,
            _ => Ds4DPad.Neutral,
        };
    }
}

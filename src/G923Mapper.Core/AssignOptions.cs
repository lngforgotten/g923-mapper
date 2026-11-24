using System.Globalization;

namespace G923Mapper;

/// <summary>Which of the usual rule options make sense for one rule, so the assign dialog offers only those.</summary>
public sealed record AssignOptionSet(bool Invert, bool Half, bool Deadzone, bool Curve, bool Threshold, bool Mode)
{
    public bool Any => Invert || Half || Deadzone || Curve || Threshold || Mode;
}

/// <summary>
/// The options the click-to-assign dialog shows under a control once a wheel control feeds it: invert, half of the axis, deadzone, curve, the
/// trigger point of a button and its mode. The same fields as in Edit rules, set in the same way, so the two never disagree.
/// </summary>
public static class AssignOptions
{
    public static readonly IReadOnlyList<string> HalfChoices = ["Whole axis", "Positive half", "Negative half"];
    public static readonly IReadOnlyList<string> CurveChoices = ["Linear", "Expo"];
    public static readonly IReadOnlyList<string> ModeChoices = ["Hold", "Toggle", "Pulse"];

    /// <summary>The options that apply to this rule: ones about an axis need an axis (a pedal or the wheel) on the input side, a deadzone and a curve a stick or trigger on the output side.</summary>
    public static AssignOptionSet For(Rule rule, DeviceMap device)
    {
        if (!Target.TryParse(rule.To, out Target? target, out _) || target is null) return new AssignOptionSet(false, false, false, false, false, false);
        bool axisInput = device.Axes.ContainsKey(rule.From.Trim());
        return target.Kind switch
        {
            TargetKind.Axis => new AssignOptionSet(Invert: axisInput, Half: axisInput, Deadzone: axisInput, Curve: axisInput, Threshold: false, Mode: false),
            TargetKind.Mode => new AssignOptionSet(false, false, false, false, Threshold: axisInput, Mode: false),
            _ => new AssignOptionSet(Invert: axisInput, Half: axisInput, Deadzone: false, Curve: false, Threshold: axisInput, Mode: true),   // a button or key
        };
    }

    // ---------------------------------------------------------------- half of the axis

    public static int HalfIndex(Rule r) => r.Part?.Trim().ToLowerInvariant() switch { "positive" => 1, "negative" => 2, _ => 0 };

    public static void SetHalf(Rule r, int index) => r.Part = index switch { 1 => "positive", 2 => "negative", _ => null };

    // ---------------------------------------------------------------- curve

    /// <summary>0 linear, 1 expo, -1 a custom curve (which only Edit rules can draw).</summary>
    public static int CurveIndex(Rule r) => r.Curve.Trim().ToLowerInvariant() switch { "linear" => 0, "expo" => 1, _ => -1 };

    public static void SetCurve(Rule r, int index)
    {
        r.Curve = index == 1 ? "expo" : "linear";
        if (index == 1 && r.Exponent <= 0) r.Exponent = 2.0;
        r.Points = null;
    }

    // ---------------------------------------------------------------- mode of a button

    public static int ModeIndex(Rule r) => r.Mode.Trim().ToLowerInvariant() switch { "toggle" => 1, "pulse" => 2, _ => 0 };

    public static void SetMode(Rule r, int index) => r.Mode = index switch { 1 => "toggle", 2 => "pulse", _ => "hold" };

    // ---------------------------------------------------------------- typed percentages

    /// <summary>
    /// A typed percentage ("4", "4%", "12.5") as a fraction (0.04). False for something that is not a number or is outside the allowed range.
    /// </summary>
    public static bool TryPercent(string? text, double minPercent, double maxPercent, bool allowZero, out double fraction)
    {
        fraction = 0;
        string t = (text ?? "").Trim().TrimEnd('%').Trim();
        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double pct)) return false;
        if (pct < minPercent || pct > maxPercent || (!allowZero && pct == 0)) return false;
        fraction = Math.Round(pct / 100.0, 6);
        return true;
    }

    /// <summary>A fraction as the text shown in a percentage box ("4", "12.5").</summary>
    public static string PercentText(double fraction) => (fraction * 100).ToString("0.##", CultureInfo.InvariantCulture);
}

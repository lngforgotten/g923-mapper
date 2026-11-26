namespace G923Mapper;

/// <summary>
/// A profile's or a mode's own centring, as the rule editor shows and keeps it. A curve is handled as a <see cref="CentringPreset"/>: its strength
/// (<see cref="ProfileForce.Spring"/>) and the four values that shape it.
/// </summary>
public static class ProfileCentring
{
    /// <summary>The curve to show for a feel: its own values where it has them, otherwise the ones in Settings, kept inside what the sliders allow.</summary>
    public static CentringPreset Shape(ProfileForce? feel, ForceSettings appWide) =>
        CentringPresetBook.FromSettings("Custom", ForceMath.Effective(appWide, feel));

    /// <summary>
    /// Put the chosen centring into a feel being built. A null style means "whatever applies anyway" (Settings, or for a mode the profile). A curve
    /// writes its whole shape, so it does not change when Settings does. For anything else, shape values the file already had are kept as long as
    /// the style was not changed, and dropped when it was.
    /// </summary>
    public static void Write(ProfileForce target, string? style, CentringPreset curve, ProfileForce? before)
    {
        target.CentringStyle = style;
        if (string.Equals(style, CentringCurve.StyleCurve, StringComparison.OrdinalIgnoreCase))
        {
            target.CentreDegrees = curve.CentreDegrees;
            target.PeakDegrees = curve.PeakDegrees;
            target.LighteningPercent = curve.LighteningPercent;
            target.DampingPercent = curve.DampingPercent;
        }
        else if (string.Equals(style, before?.CentringStyle, StringComparison.OrdinalIgnoreCase))
        {
            target.CentreDegrees = before?.CentreDegrees;
            target.PeakDegrees = before?.PeakDegrees;
            target.LighteningPercent = before?.LighteningPercent;
            target.DampingPercent = before?.DampingPercent;
        }
    }

    /// <summary>Which of these presets has exactly this strength and shape, or -1 (the picker then shows "Custom").</summary>
    public static int Matching(IReadOnlyList<CentringPreset> presets, CentringPreset curve)
    {
        for (int i = 0; i < presets.Count; i++)
            if (presets[i] with { Name = curve.Name } == curve) return i;
        return -1;
    }
}

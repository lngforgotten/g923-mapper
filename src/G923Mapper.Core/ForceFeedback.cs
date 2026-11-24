namespace G923Mapper;

/// <summary>User-facing force feedback options.</summary>
public sealed class ForceSettings
{
    /// <summary>Pass the game's rumble to the wheel and run the centring spring. Needs exclusive access to the wheel.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How strongly game rumble shakes the wheel, 0 to 100.</summary>
    public int RumblePercent { get; set; } = 50;

    /// <summary>
    /// A constant spring that pulls the wheel back to centre, 0 to 100. Off by default, because it is
    /// the first thing you notice and everyone likes a different amount.
    /// </summary>
    public int SpringPercent { get; set; } = 0;

    /// <summary>
    /// A steady resistance to turning the wheel, the same at any speed (it feels like a heavier, "stiffer" wheel without pulling back to centre),
    /// 0 to 100. Off by default.
    /// </summary>
    public int FrictionPercent { get; set; } = 0;

    /// <summary>"spring" (the default): the wheel's own straight-line spring. "curve": a shaped centring force worked out by the app, see <see cref="CentringCurve"/>. In both, <see cref="SpringPercent"/> is the strength.</summary>
    public string CentringStyle { get; set; } = CentringCurve.StyleSpring;

    /// <summary>Curve: degrees either side of the middle over which the force ramps up from nothing, so the middle is not twitchy.</summary>
    public int CentreDegrees { get; set; } = 12;

    /// <summary>Curve: the angle at which the force is strongest.</summary>
    public int PeakDegrees { get; set; } = 60;

    /// <summary>Curve: how much force is left at full lock, as a share of the peak (100 means none is lost).</summary>
    public int LighteningPercent { get; set; } = 40;

    /// <summary>Curve: how strongly fast turns are resisted, so the wheel does not swing past the middle. 0 to 100.</summary>
    public int DampingPercent { get; set; } = 5;

    /// <summary>Curve: the least force the curve ever uses away from the middle, as a share of the wheel's full force; measured with the Auto button.</summary>
    public int FloorPercent { get; set; } = 0;

    /// <summary>Curve: true when this wheel turns the opposite way to the force sent to it, which the Auto measurement finds out.</summary>
    public bool CentringReversed { get; set; } = false;

    /// <summary>
    /// Curve: the wheel the Auto measurement was made on (its USB ids as the app shows them). The curve runs only on that wheel, because a wheel
    /// that pushes the other way round would be driven away from the middle; any other wheel keeps the plain spring until it has been measured.
    /// </summary>
    public string CentringWheel { get; set; } = "";

    /// <summary>Which of the game's two rumble motors the wheel plays: "both" (the default), "heavy" (the slow, strong one only) or "light" (the fast, fine one only).</summary>
    public string Motors { get; set; } = ForceMath.MotorsBoth;

    /// <summary>The shape of the vibration: "smooth" (the default), "sharp" or "rough". Changing it reconnects the wheel.</summary>
    public string Style { get; set; } = ForceMath.StyleSmooth;

    public ForceSettings Clone() => (ForceSettings)MemberwiseClone();
}

/// <summary>DirectInput force values run -10000..10000; these helpers keep the scaling in one tested place.</summary>
public static class ForceMath
{
    public const int Max = 10000;

    public const string StyleSmooth = "smooth", StyleSharp = "sharp", StyleRough = "rough";
    public static readonly string[] StyleChoices = [StyleSmooth, StyleSharp, StyleRough];

    /// <summary>A style name made safe: anything unrecognised means smooth.</summary>
    public static string NormaliseStyle(string? style) =>
        StyleChoices.FirstOrDefault(s => s.Equals(style?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? StyleSmooth;

    public const string MotorsBoth = "both", MotorsHeavy = "heavy", MotorsLight = "light";
    public static readonly string[] MotorChoices = [MotorsBoth, MotorsHeavy, MotorsLight];

    /// <summary>
    /// The force settings to use right now: the app-wide ones, with whatever the active profile sets for itself laid over them. Whether force
    /// feedback is enabled at all always comes from the app-wide settings.
    /// </summary>
    public static ForceSettings Effective(ForceSettings appWide, ProfileForce? profile)
    {
        ForceSettings f = appWide.Clone();
        if (profile is null) return f;
        if (profile.Rumble is int r) f.RumblePercent = Math.Clamp(r, 0, 100);
        if (profile.Spring is int s) f.SpringPercent = Math.Clamp(s, 0, 100);
        if (profile.Friction is int fr) f.FrictionPercent = Math.Clamp(fr, 0, 100);
        if (profile.Motors is { } m && MotorChoices.Contains(m, StringComparer.OrdinalIgnoreCase)) f.Motors = m.ToLowerInvariant();
        return f;
    }

    /// <summary>The game's two motor strengths (large, small), with the one the user does not want silenced. Anything unrecognised means both.</summary>
    public static (byte Large, byte Small) SelectMotors(byte large, byte small, string? motors) => motors?.Trim().ToLowerInvariant() switch
    {
        MotorsHeavy => (large, (byte)0),
        MotorsLight => ((byte)0, small),
        _ => (large, small),
    };

    /// <summary>
    /// The spring at 100% on the slider. Well below the device's absolute maximum (10000), because a spring at
    /// full strength fights every turn of the wheel and feels like heavy, sluggish steering.
    /// </summary>
    public const int SpringMax = 5000;

    /// <summary>
    /// The friction at 100% on the slider. Like the spring it is kept well below the device maximum, because friction is felt at every speed.
    /// </summary>
    public const int FrictionMax = 5000;

    /// <summary>Friction strength for a percentage.</summary>
    public static int FrictionCoefficient(int percent) => (int)Math.Round(Math.Clamp(percent, 0, 100) / 100.0 * FrictionMax);

    /// <summary>A rumble motor (0..255) scaled by the user's strength (0..100) to a periodic-effect magnitude.</summary>
    public static int RumbleMagnitude(byte motor, int percent)
    {
        double p = Math.Clamp(percent, 0, 100) / 100.0;
        return (int)Math.Round(motor / 255.0 * p * Max);
    }

    /// <summary>Spring stiffness for a percentage.</summary>
    public static int SpringCoefficient(int percent) => (int)Math.Round(Math.Clamp(percent, 0, 100) / 100.0 * SpringMax);

    /// <summary>
    /// The G923 has two physical "motors" in the sense of rumble: the game's large (heavy, low
    /// frequency) and small (light, high frequency) motor. We play them as slow and fast vibrations.
    /// </summary>
    public const int HeavyPeriodMicroseconds = 60_000;   // about 17 Hz
    public const int LightPeriodMicroseconds = 20_000;   // 50 Hz
}

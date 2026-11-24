namespace G923Mapper;

/// <summary>A hand-tuned starting point for the centring curve, named after the kind of car it is meant to feel like.</summary>
public sealed record CentringPreset(string Name, int Strength, int CentreDegrees, int PeakDegrees, int LighteningPercent, int DampingPercent)
{
    public void ApplyTo(ForceSettings s)
    {
        s.SpringPercent = Strength;
        s.CentreDegrees = CentreDegrees;
        s.PeakDegrees = PeakDegrees;
        s.LighteningPercent = LighteningPercent;
        s.DampingPercent = DampingPercent;
    }

    /// <summary>True when the settings are exactly this preset (so the picker can show which one is in use).</summary>
    public bool Matches(ForceSettings s) =>
        s.SpringPercent == Strength && s.CentreDegrees == CentreDegrees && s.PeakDegrees == PeakDegrees
        && s.LighteningPercent == LighteningPercent && s.DampingPercent == DampingPercent;
}

/// <summary>
/// A centring force worked out by the app from where the wheel is, instead of the wheel's built-in straight-line spring. The force pulls the
/// wheel towards the middle and follows a shape: it ramps up over the <i>centre</i> zone so the middle is not twitchy, builds to its
/// strongest at the <i>peak</i> angle, then eases off towards full lock down to the <i>lightening</i> share of the peak, the way a real
/// steering rack lightens at large angles. A <i>floor</i> keeps it from ever dropping below the wheel's own friction, so the wheel always
/// comes home, and a little <i>damping</i> opposes fast turns so it does not swing past the middle.
/// </summary>
public static class CentringCurve
{
    public const string StyleSpring = "spring", StyleCurve = "curve";

    /// <summary>Anything unrecognised means the plain spring.</summary>
    public static string NormaliseStyle(string? style) =>
        string.Equals(style?.Trim(), StyleCurve, StringComparison.OrdinalIgnoreCase) ? StyleCurve : StyleSpring;

    /// <summary>The strongest damping, as a share of the wheel's full force, at 100% damping and a fast turn.</summary>
    public const double DampingMax = 0.20;

    /// <summary>A turn this fast (degrees a second) gives the full damping.</summary>
    public const double DampingSpeed = 300.0;

    /// <summary>The most force the floor measurement may use, as a share of the wheel's full force.</summary>
    public const double FloorMax = 0.30;

    public static readonly IReadOnlyList<CentringPreset> Presets =
    [
        new("Light and relaxed", 20, 10, 50, 50, 3),
        new("Family car", 30, 12, 60, 40, 5),
        new("Sports car", 40, 8, 70, 35, 6),
        new("Heavy truck", 45, 16, 80, 70, 10),
        new("Strong and simple", 60, 6, 90, 85, 4),
    ];

    /// <summary>How strong the curve is at this angle, 0 to 1 (1 is the peak), before the strength and the floor are applied. Symmetric about the middle.</summary>
    public static double Shape(double angleDegrees, ForceSettings s, double halfRangeDegrees)
    {
        double half = Math.Max(10, halfRangeDegrees);
        double a = Math.Min(Math.Abs(angleDegrees), half);
        double centre = Math.Clamp(s.CentreDegrees, 1, 90);
        double peak = Math.Clamp(s.PeakDegrees, centre + 1, Math.Max(centre + 1, half - 1));
        double keep = Math.Clamp(s.LighteningPercent, 0, 100) / 100.0;

        const double atCentreEdge = 0.85;                                    // the ramp over the centre zone reaches this much of the peak
        if (a < centre) return atCentreEdge * (a / centre);
        if (a < peak) return atCentreEdge + (1 - atCentreEdge) * Smooth((a - centre) / (peak - centre));
        double span = Math.Max(1, half - peak);
        return 1 - (1 - keep) * Smooth((a - peak) / span);
    }

    private static double Smooth(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }

    /// <summary>
    /// The force to send, as a share of the wheel's full force from -1 to 1, with a positive value pushing towards higher axis readings. The
    /// strength is <see cref="ForceSettings.SpringPercent"/>; at zero the whole curve is off. <paramref name="velocity"/> is in degrees a second.
    /// </summary>
    public static double Force(double angleDegrees, double velocity, ForceSettings s, double halfRangeDegrees)
    {
        double strength = Math.Clamp(s.SpringPercent, 0, 100) / 100.0;
        if (strength <= 0) return 0;

        double centre = Math.Clamp(s.CentreDegrees, 1, 90);
        double magnitude = strength * Shape(angleDegrees, s, halfRangeDegrees);
        double floor = Math.Clamp(s.FloorPercent, 0, 100) / 100.0 * Math.Min(1, Math.Abs(angleDegrees) / centre);
        magnitude = Math.Max(magnitude, Math.Min(floor, FloorMax));

        double towardsCentre = -Math.Sign(angleDegrees) * magnitude;
        double damp = Math.Clamp(s.DampingPercent, 0, 100) / 100.0 * DampingMax * Math.Clamp(velocity / DampingSpeed, -1, 1);
        return Math.Clamp(towardsCentre - damp, -1, 1);
    }

    /// <summary>The force at evenly spaced angles from full left to full right (at rest), for drawing the graph.</summary>
    public static double[] Sample(ForceSettings s, double halfRangeDegrees, int points)
    {
        var values = new double[Math.Max(2, points)];
        for (int i = 0; i < values.Length; i++)
        {
            double angle = -halfRangeDegrees + 2 * halfRangeDegrees * i / (values.Length - 1);
            values[i] = Force(angle, 0, s, halfRangeDegrees);
        }
        return values;
    }

    /// <summary>The device's signed force value for a fraction of full force, flipped when this wheel pushes the other way round.</summary>
    public static int ToDevice(double fraction, bool reversed)
    {
        int v = (int)Math.Round(Math.Clamp(fraction, -1, 1) * ForceMath.Max);
        return reversed ? -v : v;
    }
}

/// <summary>
/// Measures two things about a wheel with its hands off: the least force that gets it moving (the floor for the curve) and which way a
/// positive force pushes it. The force is raised slowly from nothing until the wheel turns a couple of degrees, then dropped at once.
/// </summary>
public sealed class FrictionProbe
{
    /// <summary>Force added every second, as a share of the wheel's full force.</summary>
    public const double RampPerSecond = 0.06;

    /// <summary>The wheel counts as moving after this many degrees.</summary>
    public const double MoveDegrees = 2.0;

    /// <summary>Give up when the force reaches this (the wheel is probably being held) .</summary>
    public const double GiveUp = 0.35;

    private readonly double _command;      // the sign of the force being sent: +1 or -1
    private double _startAngle;
    private long _startMs = -1;

    public bool Done { get; private set; }

    /// <summary>The least force that turned the wheel, 0 to 1, when the measurement worked.</summary>
    public double? Floor { get; private set; }

    /// <summary>True when a positive force turned the wheel towards lower axis readings.</summary>
    public bool Reversed { get; private set; }

    /// <summary>What went wrong, or empty.</summary>
    public string Problem { get; private set; } = "";

    /// <summary>Which way the force pushes: away from the nearer end of the wheel's turn, so it always has room to move.</summary>
    public FrictionProbe(double startAngleDegrees) => _command = startAngleDegrees > 0 ? -1 : 1;

    /// <summary>
    /// Feed it the wheel angle; get back the force to send now (a signed share of the full force, in the axis's own sense). Once <see cref="Done"/>
    /// the force is zero.
    /// </summary>
    public double Step(double angleDegrees, long nowMs, bool wheelLive = true)
    {
        if (Done) return 0;
        if (!wheelLive) { Fail("The wheel stopped reporting."); return 0; }
        if (_startMs < 0) { _startMs = nowMs; _startAngle = angleDegrees; return 0; }

        double seconds = (nowMs - _startMs) / 1000.0;
        double force = Math.Min(GiveUp, RampPerSecond * seconds);
        double moved = angleDegrees - _startAngle;
        if (Math.Abs(moved) >= MoveDegrees)
        {
            Done = true;
            Floor = Math.Min(GiveUp, force);
            Reversed = Math.Sign(moved) != Math.Sign(_command);
            return 0;
        }
        if (force >= GiveUp) { Fail("The wheel did not move even at " + (int)(GiveUp * 100) + "% force. Take your hands off it and try again."); return 0; }
        return _command * force;
    }

    private void Fail(string why) { Done = true; Problem = why; }
}

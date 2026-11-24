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

    /// <summary>Degrees either side of the middle where the curve pulls with no force at all (see <see cref="Force"/>).</summary>
    public const double DeadDegrees = 1.0;

    /// <summary>A turn this fast (degrees a second) gives the full damping.</summary>
    public const double DampingSpeed = 300.0;

    /// <summary>The most force the floor measurement may use, as a share of the wheel's full force.</summary>
    public const double FloorMax = 0.30;

    /// <summary>The curve's strength at 100% is this share of the wheel's full force (the strength slider scales it). Full force is far too much for a centring pull.</summary>
    public const double CurveMax = 0.5;

    /// <summary>
    /// The steepest the pull may rise near the middle, as a share of the wheel's full force for each degree. The app sends the force a little behind
    /// the wheel's movement (the loop, USB, the motor), and a pull that rises faster than this over a few degrees throws the wheel past the middle
    /// every time, so it swings by itself. A strong curve therefore spreads its rise over a wider centre zone instead of a steeper one.
    /// </summary>
    public const double MaxPullPerDegree = 0.008;

    public static readonly IReadOnlyList<CentringPreset> Presets =
    [
        new("Light and relaxed", 20, 10, 50, 50, 3),
        new("Family car", 30, 12, 60, 40, 5),
        new("Sports car", 40, 8, 70, 35, 6),
        new("Heavy truck", 45, 16, 80, 70, 10),
        new("Strong and simple", 60, 6, 90, 85, 4),
        new("Race car (single-seater)", 45, 6, 40, 30, 6),
        new("Rally / gravel", 25, 20, 90, 55, 4),
        new("Drift car", 20, 14, 120, 100, 3),
        new("Classic car", 35, 18, 100, 90, 12),
        new("Arcade", 40, 4, 60, 100, 4),
        new("Truck / bus / tractor", 45, 24, 90, 80, 15),
        new("Tank / armoured vehicle", 35, 30, 120, 100, 20),
        new("Boat", 12, 40, 150, 100, 25),
        new("Aircraft / flight yoke", 30, 25, 150, 100, 8),
        new("Off (no centring)", 0, 12, 60, 40, 5),
    ];

    /// <summary>How strong the curve is at this angle, 0 to 1 (1 is the peak), before the strength and the floor are applied. Symmetric about the middle.</summary>
    public static double Shape(double angleDegrees, ForceSettings s, double halfRangeDegrees)
    {
        double half = Math.Max(10, halfRangeDegrees);
        double a = Math.Min(Math.Abs(angleDegrees), half);
        double centre = EffectiveCentre(s);
        double keep = Math.Clamp(s.LighteningPercent, 0, 100) / 100.0;

        // The rise from the end of the centre zone up to the peak, and the fall after it, are held to the same limit on the pull per degree as the
        // centre zone is (1.5 is the steepest part of the smooth step used for both). A strong curve needs more room between the two.
        double peakForce = Math.Clamp(s.SpringPercent, 0, 100) / 100.0 * CurveMax;
        double upSpan = Math.Max(1, peakForce * 0.15 * 1.5 / MaxPullPerDegree);
        double downSpan = Math.Max(1, peakForce * (1 - keep) * 1.5 / MaxPullPerDegree);
        double peak = Math.Clamp(s.PeakDegrees, centre + upSpan, Math.Max(centre + upSpan, half - downSpan));

        const double atCentreEdge = 0.85;                                    // the ramp over the centre zone reaches this much of the peak
        if (a < centre) return atCentreEdge * (a / centre);
        if (a < peak) return atCentreEdge + (1 - atCentreEdge) * Smooth((a - centre) / (peak - centre));
        double span = Math.Max(1, half - peak);
        return 1 - (1 - keep) * Smooth((a - peak) / span);
    }

    /// <summary>
    /// The centre zone actually used: the one set, or wider when the strength is high enough that the pull would otherwise rise faster than
    /// <see cref="MaxPullPerDegree"/>.
    /// </summary>
    public static double EffectiveCentre(ForceSettings s)
    {
        double strength = Math.Clamp(s.SpringPercent, 0, 100) / 100.0;
        double set = Math.Clamp(s.CentreDegrees, 1, 90);
        return Math.Min(90, Math.Max(set, strength * CurveMax * 0.85 / MaxPullPerDegree));
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

        double damp = Math.Clamp(s.DampingPercent, 0, 100) / 100.0 * DampingMax * Math.Clamp(velocity / DampingSpeed, -1, 1);

        // Right in the middle there is no pull at all. A force that flips sign across the middle makes any wheel chatter (it overshoots, gets
        // pushed back, overshoots...), so the last degree either side is left alone. The damping, if any, still acts there.
        if (Math.Abs(angleDegrees) < DeadDegrees) return Math.Clamp(-damp, -1, 1);

        double centre = EffectiveCentre(s);
        double magnitude = strength * CurveMax * Shape(angleDegrees, s, halfRangeDegrees);
        double floor = Math.Clamp(s.FloorPercent, 0, 100) / 100.0 * Math.Min(1, Math.Abs(angleDegrees) / centre);
        magnitude = Math.Max(magnitude, Math.Min(floor, FloorMax));

        double towardsCentre = -Math.Sign(angleDegrees) * magnitude;
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
/// positive force pushes it. The force is raised slowly from nothing, first one way and then the other, until the wheel has turned a couple of
/// degrees each time. Pushing both ways is what makes the answer trustworthy: a spring that is already pulling the wheel (Logitech G HUB's
/// own centring spring, say) helps one push as much as it hinders the other, so the two cancel; and if the wheel went the same way for both
/// pushes, something else is moving it and the measurement says so instead of guessing.
/// </summary>
public sealed class FrictionProbe
{
    /// <summary>Force added every second, as a share of the wheel's full force.</summary>
    public const double RampPerSecond = 0.06;

    /// <summary>The wheel counts as moving after this many degrees.</summary>
    public const double MoveDegrees = 2.0;

    /// <summary>Give up when the force reaches this (the wheel is probably being held).</summary>
    public const double GiveUp = 0.35;

    /// <summary>How long the wheel is left alone between the two pushes to come to rest.</summary>
    public const long SettleMs = 700;

    private enum Phase { Start, First, Settle, Second, Done }

    private Phase _phase = Phase.Start;
    private double _reference;
    private long _phaseStartMs;
    private double _firstForce, _firstMove;

    public bool Done => _phase == Phase.Done;

    /// <summary>The least force that turned the wheel (the average of the two pushes), 0 to 1, when the measurement worked.</summary>
    public double? Floor { get; private set; }

    /// <summary>True when a positive force turned the wheel towards lower axis readings.</summary>
    public bool Reversed { get; private set; }

    /// <summary>What went wrong, or empty.</summary>
    public string Problem { get; private set; } = "";

    /// <summary>The first push (towards higher readings): how far the wheel went, in degrees with its sign, and the force that did it. Zero until it has.</summary>
    public double FirstMoveDegrees => _firstMove;
    public double FirstForce => _firstForce;

    /// <summary>The second push (towards lower readings): the same. Zero until it has moved.</summary>
    public double SecondMoveDegrees { get; private set; }
    public double SecondForce { get; private set; }

    /// <summary>A sentence with what each push did, for the log and for the person measuring.</summary>
    public string Detail => _firstMove == 0 ? "" :
        $"Push 1 (towards higher readings): the wheel moved {_firstMove:+0.0;-0.0}° at {_firstForce * 100:0}% force." +
        (SecondMoveDegrees == 0 ? "" : $" Push 2 (the other way): it moved {SecondMoveDegrees:+0.0;-0.0}° at {SecondForce * 100:0}% force.");

    /// <summary>
    /// Feed it the wheel position in degrees (any fixed reference will do); get back the force to send now, a signed share of the full force with
    /// positive meaning towards higher axis readings. Once <see cref="Done"/> the force is zero.
    /// </summary>
    public double Step(double positionDegrees, long nowMs, bool wheelLive = true)
    {
        if (Done) return 0;
        if (!wheelLive) { Fail("The wheel stopped reporting."); return 0; }

        switch (_phase)
        {
            case Phase.Start:
                Begin(Phase.First, positionDegrees, nowMs);
                return 0;

            case Phase.First:
            {
                double force = Ramp(nowMs);
                double moved = positionDegrees - _reference;
                if (Math.Abs(moved) >= MoveDegrees) { _firstForce = force; _firstMove = moved; Begin(Phase.Settle, positionDegrees, nowMs); return 0; }
                if (force >= GiveUp) { Fail(HeldMessage); return 0; }
                return force;
            }

            case Phase.Settle:
                if (nowMs - _phaseStartMs >= SettleMs) Begin(Phase.Second, positionDegrees, nowMs);
                return 0;

            default:   // the second push, the other way
            {
                double force = Ramp(nowMs);
                double moved = positionDegrees - _reference;
                if (Math.Abs(moved) >= MoveDegrees) { Finish(force, moved); return 0; }
                if (force >= GiveUp) { Fail(HeldMessage); return 0; }
                return -force;
            }
        }
    }

    private static readonly string HeldMessage = "The wheel did not move even at " + (int)(GiveUp * 100) + "% force. Take your hands off it and try again.";

    private void Begin(Phase phase, double position, long nowMs) { _phase = phase; _reference = position; _phaseStartMs = nowMs; }

    private double Ramp(long nowMs) => Math.Min(GiveUp, RampPerSecond * (nowMs - _phaseStartMs) / 1000.0);

    private void Finish(double secondForce, double secondMove)
    {
        SecondForce = secondForce; SecondMoveDegrees = secondMove;
        // A force pushing up that moved the wheel up, and a force pushing down that moved it down: the right way round. The opposite for both:
        // reversed. The same direction for both pushes means something else is pulling the wheel harder than the pushes were.
        bool upThenDown = _firstMove > 0 && secondMove < 0, downThenUp = _firstMove < 0 && secondMove > 0;
        if (!upThenDown && !downThenUp)
        {
            Fail("The wheel went the same way for both pushes, so something else is pulling it harder. Logitech G HUB's own centring spring is the usual cause: turn it off in G HUB (or take your hands off the wheel) and measure again.");
            return;
        }
        Reversed = downThenUp;
        Floor = Math.Min(GiveUp, (_firstForce + secondForce) / 2);
        _phase = Phase.Done;
    }

    private void Fail(string why) { Problem = why; _phase = Phase.Done; }
}




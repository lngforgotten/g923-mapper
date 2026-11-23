namespace G923Mapper;

/// <summary>Pure shaping maths for one axis value. No hardware, no state.</summary>
public static class Shaping
{
    /// <summary>
    /// Apply deadzone, then curve, then anti-deadzone to a value in -1..1. Works on the magnitude and
    /// keeps the sign. Order matters: the deadzone removes the centre first, the curve reshapes what
    /// is left, and the anti-deadzone lifts any non-zero result above the game's own deadzone.
    /// </summary>
    public static double Shape(double v, Rule rule) =>
        Shape(v, rule.Deadzone, rule.Curve, rule.Exponent, rule.Points, rule.AntiDeadzone);

    public static double Shape(double v, double deadzone, string curve, double exponent, double[][]? points, double antiDeadzone)
    {
        if (double.IsNaN(v)) return 0;
        double sign = v < 0 ? -1 : 1;
        double m = Math.Min(1, Math.Abs(v));

        if (deadzone > 0) m = m <= deadzone ? 0 : (m - deadzone) / (1 - deadzone);

        m = curve.ToLowerInvariant() switch
        {
            "expo" => Math.Pow(m, exponent),
            "custom" => Interpolate(points, m),
            _ => m,
        };

        if (antiDeadzone > 0 && m > 0) m = antiDeadzone + (1 - antiDeadzone) * m;

        m = Math.Clamp(m, 0, 1);
        return m == 0 ? 0 : sign * m;
    }

    /// <summary>Piecewise-linear lookup. Below the first point returns its y; above the last returns its y.</summary>
    public static double Interpolate(double[][]? points, double x)
    {
        if (points is null || points.Length == 0) return x;
        if (x <= points[0][0]) return points[0][1];
        for (int i = 1; i < points.Length; i++)
        {
            double x0 = points[i - 1][0], y0 = points[i - 1][1], x1 = points[i][0], y1 = points[i][1];
            if (x <= x1) return x1 == x0 ? y1 : y0 + (y1 - y0) * (x - x0) / (x1 - x0);
        }
        return points[^1][1];
    }

    public static short ToStick(double v)
    {
        v = Math.Clamp(v, -1, 1);
        return (short)Math.Round(v >= 0 ? v * 32767.0 : v * 32768.0);
    }

    public static byte ToTrigger(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255.0);
}

/// <summary>Turns normalised inputs into pad output according to a profile. Holds per-rule state for toggle/pulse.</summary>
public sealed class MappingEngine
{
    private sealed class CompiledRule
    {
        public required Rule Rule;
        public required Target Target;
        public bool Signed;
        // Button state
        public bool Previous, Toggled;
        public long PulseUntil;
        // Expression and turbo
        public Expression? Expr;
        public int Part;   // 0 whole axis, +1 positive half, -1 negative half
        public double SmoothMs, SmoothValue;
        public long SmoothAt = -1;
        public bool Turbo, TurboWasOn;
        public long TurboStart, TurboPeriod;
    }

    private readonly DeviceMap _device;
    private readonly CompiledRule[] _rules;

    public Profile Profile { get; }

    public MappingEngine(Profile profile, DeviceMap device, DeviceRegistry? registry = null)
    {
        Profile = profile;
        _device = device;
        var list = new List<CompiledRule>();
        bool turboAllowed = ProfileSafety.TurboAllowed(profile);   // checked once, when the profile is loaded
        foreach (Rule r in profile.Rules)
        {
            if (!Target.TryParse(r.To, out Target? t, out string? err))
                throw new ArgumentException($"Rule {r.From} -> {r.To}: {err}");
            // Keyboard rules only exist while keyboard output is switched on and the profile's safety mode (and game) allow it.
            if (t!.Kind == TargetKind.Key && !ProfileSafety.KeyboardAllowed(profile)) continue;
            if (t.Pad == 2 && !profile.Outputs.SecondPad) continue;   // the second controller only exists when the profile asks for it
            Expression? expr = null;
            if (!string.IsNullOrWhiteSpace(r.Expr) && !Expression.TryParse(r.Expr, out expr, out string? exprErr))
                throw new ArgumentException($"Rule {r.From} -> {r.To}: expression: {exprErr}");
            bool turbo = r.Turbo && t.Kind == TargetKind.Button && turboAllowed;
            list.Add(new CompiledRule
            {
                Rule = r, Target = t, Signed = registry?.IsSigned(device, r.From) ?? device.IsSigned(r.From),
                Expr = expr, Part = PartOf(r.Part), SmoothMs = Math.Clamp(r.Smooth, 0, 1000), Turbo = turbo, TurboPeriod = Math.Max(2, (long)Math.Round(1000.0 / Math.Clamp(r.TurboHz, 1, 30))),
            });
        }
        _rules = list.ToArray();
    }

    private static int PartOf(string? part) =>
        string.Equals(part, "positive", StringComparison.OrdinalIgnoreCase) ? 1 : string.Equals(part, "negative", StringComparison.OrdinalIgnoreCase) ? -1 : 0;

    /// <summary>One half of an axis stretched to 0..1. Signed axes (the wheel) split at the centre; unsigned ones (a pedal axis) at half travel.</summary>
    public static double HalfAxis(double v, bool signed, int part)
    {
        if (part == 0) return v;
        if (signed) return part > 0 ? Math.Max(0, v) : Math.Max(0, -v);
        return part > 0 ? Math.Clamp((v - 0.5) * 2, 0, 1) : Math.Clamp((0.5 - v) * 2, 0, 1);
    }

    /// <param name="nowMs">Monotonic milliseconds; only used by pulse mode.</param>
    public PadState Evaluate(InputFrame frame, long nowMs)
    {
        Span<double> axes = stackalloc double[6];
        Span<double> axes2 = stackalloc double[6];
        var state = new PadState();
        PadState? state2 = Profile.Outputs.SecondPad ? new PadState() : null;

        foreach (CompiledRule cr in _rules)
        {
            Rule r = cr.Rule;
            double v = frame.Get(r.From);

            if (cr.Signed && r.Range is double range)
                v = Math.Clamp(v * _device.RotationDegrees / range, -1, 1);
            if (cr.Part != 0) v = HalfAxis(v, cr.Signed, cr.Part);
            if (cr.Expr is { } expr)
            {
                double x = v;
                v = Math.Clamp(expr.Evaluate(name => name.Equals("x", StringComparison.OrdinalIgnoreCase) ? x : frame.Get(name)), -1, 1);
            }
            if (r.Invert) v = cr.Signed ? -v : 1 - v;

            if (cr.Target.Kind == TargetKind.Axis)
            {
                double shaped = Shaping.Shape(v, r);
                if (cr.SmoothMs > 0) shaped = Smooth(cr, shaped, nowMs);
                Span<double> into = cr.Target.Pad == 2 ? axes2 : axes;
                into[(int)cr.Target.Axis] += shaped;
            }
            else if (EvaluateButton(cr, v, nowMs))
            {
                if (cr.Target.Kind == TargetKind.Key)
                {
                    if (KeyNames.Canonical(cr.Target.Key) is string key) state.Keys.Add(key);
                }
                else (cr.Target.Pad == 2 ? state2! : state)[cr.Target.Button] = true;
            }
        }

        Fill(state, axes);
        if (state2 is not null) { Fill(state2, axes2); state.Second = state2; }
        return state;
    }

    private static void Fill(PadState state, ReadOnlySpan<double> axes)
    {
        state.LeftX = Shaping.ToStick(axes[(int)PadAxis.LeftStickX]);
        state.LeftY = Shaping.ToStick(axes[(int)PadAxis.LeftStickY]);
        state.RightX = Shaping.ToStick(axes[(int)PadAxis.RightStickX]);
        state.RightY = Shaping.ToStick(axes[(int)PadAxis.RightStickY]);
        state.LeftTrigger = Shaping.ToTrigger(axes[(int)PadAxis.LeftTrigger]);
        state.RightTrigger = Shaping.ToTrigger(axes[(int)PadAxis.RightTrigger]);
    }

    /// <summary>A one-pole low-pass filter: the value moves towards the target by a share that grows with the time since the last frame.</summary>
    private static double Smooth(CompiledRule cr, double target, long nowMs)
    {
        if (cr.SmoothAt < 0) { cr.SmoothValue = target; cr.SmoothAt = nowMs; return target; }   // the first frame starts where the input is
        double dt = Math.Max(0, nowMs - cr.SmoothAt);
        cr.SmoothAt = nowMs;
        double alpha = 1 - Math.Exp(-dt / cr.SmoothMs);
        cr.SmoothValue += (target - cr.SmoothValue) * alpha;
        return cr.SmoothValue;
    }

    private static bool EvaluateButton(CompiledRule cr, double v, long nowMs)
    {
        Rule r = cr.Rule;
        double threshold = r.Threshold ?? 0.5;
        bool pressed = threshold < 0 ? v <= threshold : v >= threshold;
        bool rising = pressed && !cr.Previous;
        cr.Previous = pressed;

        bool on;
        switch (r.Mode.ToLowerInvariant())
        {
            case "toggle":
                if (rising) cr.Toggled = !cr.Toggled;
                on = cr.Toggled;
                break;
            case "pulse":
                // One fixed-length press per physical press: still 1 physical input to 1 virtual input.
                if (rising) cr.PulseUntil = nowMs + r.PulseMs;
                on = nowMs < cr.PulseUntil;
                break;
            default:
                on = pressed;
                break;
        }

        // Turbo (standard profiles only): while the button would be held, it is pressed and released repeatedly.
        if (cr.Turbo)
        {
            if (on && !cr.TurboWasOn) cr.TurboStart = nowMs;
            cr.TurboWasOn = on;
            if (on) on = (nowMs - cr.TurboStart) % cr.TurboPeriod < cr.TurboPeriod / 2;
        }
        return on;
    }
}

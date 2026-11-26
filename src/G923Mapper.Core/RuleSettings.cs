namespace G923Mapper;

/// <summary>
/// Which groups of settings the rule editor shows for a rule. The response curve (curve, deadzones, smoothing) applies to sticks and triggers
/// only; the press settings (trigger point, button mode, turbo) to buttons, keys and mode switches only. A setting that does not apply is hidden
/// unless it holds something the checks complain about, so that it can still be put right.
/// </summary>
public static class RuleSettings
{
    /// <summary>The output is on or off (a controller button, a key, a mouse button or a mode switch), not a stick or trigger.</summary>
    public static bool IsPress(string to)
    {
        string text = to.Trim();
        if (Target.TryParse(text, out Target? t, out _)) return t!.Kind != TargetKind.Axis;
        // Half-typed or misspelt: go by how it starts, so "Button:Q" still offers the press settings.
        if (text.StartsWith(Target.SecondPadPrefix, StringComparison.OrdinalIgnoreCase)) text = text[Target.SecondPadPrefix.Length..];
        return new[] { "Button:", "Key:", KeyNames.MousePrefix, "Mode:" }.Any(p => text.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The output is a controller button, on either controller: the only kind turbo works on.</summary>
    public static bool IsPadButton(string to) => Target.TryParse(to.Trim(), out Target? t, out _) && t!.Kind == TargetKind.Button;

    /// <summary>The output switches modes, which happens once a press: hold, toggle and pulse make no difference to it.</summary>
    public static bool IsModeSwitch(string to) => to.Trim().StartsWith("Mode:", StringComparison.OrdinalIgnoreCase);

    /// <summary>A response setting of this rule would be complained about if its output is on or off.</summary>
    public static bool ResponseNeedsAttention(Rule r) =>
        r.Smooth != 0 || r.Exponent <= 0 || r.Deadzone < 0 || r.Deadzone >= 1 || r.AntiDeadzone < 0 || r.AntiDeadzone >= 1
        || (r.Curve.Equals("custom", StringComparison.OrdinalIgnoreCase) && !PointsUsable(r.Points));

    /// <summary>A press setting of this rule would be complained about if its output is a stick or trigger.</summary>
    public static bool PressNeedsAttention(Rule r) =>
        !r.Mode.Equals("hold", StringComparison.OrdinalIgnoreCase) || r.Turbo
        || r.Threshold is double t && (t == 0 || t < -1 || t > 1)
        || r.PulseMs < 1 || r.PulseMs > 1000 || r.TurboHz < 1 || r.TurboHz > 30;

    private static bool PointsUsable(double[][]? points)
    {
        if (points is null || points.Length < 2) return false;
        double previous = -1;
        foreach (double[] p in points)
        {
            if (p.Length != 2 || p[0] < 0 || p[0] > 1 || p[1] < 0 || p[1] > 1 || p[0] <= previous) return false;
            previous = p[0];
        }
        return true;
    }
}

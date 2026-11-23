namespace G923Mapper;

/// <summary>Where a rule's input is on its response curve right now: the live dot of the rule editor's graph.</summary>
public static class LiveCurve
{
    /// <summary>
    /// The position on the curve for a raw input value (-1..1 for a signed axis, 0..1 otherwise): the value as it reaches the shaping step
    /// (after the range, half-axis and invert settings) and what the curve turns it into. Null when the rule uses an expression, which
    /// can depend on other controls and so has no single position.
    /// </summary>
    public static (double X, double Y)? Position(double input, Rule rule, bool signed, double rotationDegrees)
    {
        if (!string.IsNullOrWhiteSpace(rule.Expr)) return null;
        double v = input;
        if (signed && rule.Range is double range && range > 0) v = Math.Clamp(v * rotationDegrees / range, -1, 1);
        int part = string.Equals(rule.Part, "positive", StringComparison.OrdinalIgnoreCase) ? 1 : string.Equals(rule.Part, "negative", StringComparison.OrdinalIgnoreCase) ? -1 : 0;
        if (part != 0) v = MappingEngine.HalfAxis(v, signed, part);
        if (rule.Invert) v = signed ? -v : 1 - v;
        double y;
        try { y = Shaping.Shape(v, rule); }
        catch (Exception) { y = 0; }
        return (v, y);
    }
}

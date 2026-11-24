using System.Globalization;

namespace G923Mapper;

/// <summary>The numbers shown beside a rule's gauge on the test page, kept here so they are tested.</summary>
public static class ReadingText
{
    private static string Percent(double v, bool signed) =>
        (signed ? (v * 100).ToString("+0;-0;0", CultureInfo.InvariantCulture) : (v * 100).ToString("0", CultureInfo.InvariantCulture)) + "%";

    /// <summary>
    /// For an axis rule: "in 62% -> out 41%". For a button or key rule: the trigger point (and mode) on one line, and on the next whether the input
    /// is past it and whether the button is down.
    /// </summary>
    public static string Describe(RuleReading r)
    {
        if (!r.IsButton)
        {
            bool stick = r.Rule.To.Contains("Stick", StringComparison.OrdinalIgnoreCase);
            return $"in {Percent(r.Input, r.Signed)} -> out {Percent(r.Output, stick)}";
        }
        string at = r.Threshold < 0 ? $"at or below {Percent(r.Threshold, true)}" : $"at {Percent(r.Threshold, r.Signed)}";
        string mode = r.Rule.Mode.Equals("hold", StringComparison.OrdinalIgnoreCase) ? "" : $" ({r.Rule.Mode.ToLowerInvariant()})";
        return $"in {Percent(r.Input, r.Signed)}, fires {at}{mode}\n{(r.Over ? "past it" : "below it")}, button {(r.On ? "DOWN" : "up")}";
    }
}

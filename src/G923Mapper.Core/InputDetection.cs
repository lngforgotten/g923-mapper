using System.Text.RegularExpressions;

namespace G923Mapper;

/// <summary>What the user just did on the wheel, as a profile input name.</summary>
/// <param name="Name">The input name to use in a rule, or null when it cannot be mapped.</param>
public sealed record DetectedInput(string? Name, string Description);

public static class InputDetector
{
    public const int AxisThreshold = 8000;

    /// <summary>
    /// Compare a baseline reading (taken before the gesture) with the current one and say which input
    /// changed: a newly pressed button, a hat direction, or an axis that moved a long way. Buttons win
    /// over hat, which wins over axes, because a button press is the least ambiguous gesture.
    /// Returns null when nothing happened, or when either reading is still the stale pre-wake snapshot.
    /// </summary>
    public static DetectedInput? Detect(RawInput baseline, RawInput now, DeviceMap device)
    {
        if (!baseline.Live || !now.Live) return null;

        // Buttons
        for (int i = 0; i < now.Buttons.Length; i++)
        {
            bool wasDown = i < baseline.Buttons.Length && baseline.Buttons[i];
            if (now.Buttons[i] && !wasDown)
            {
                string name = ButtonName(device, i + 1);
                return new DetectedInput(name, $"Button {i + 1}" + (name.StartsWith("button", StringComparison.Ordinal) ? "" : $" ({name})"));
            }
        }

        // Hat
        if (now.Pov >= 0 && now.Pov != baseline.Pov)
        {
            var (up, down, left, right) = DeviceMap.Hat(now.Pov);
            string? hat = up ? "dpadUp" : right ? "dpadRight" : down ? "dpadDown" : left ? "dpadLeft" : null;
            if (hat is not null) return new DetectedInput(hat, "D-pad " + hat[4..]);
        }

        // Axes
        int best = -1, bestChange = AxisThreshold - 1;
        for (int a = 0; a < 8; a++)
        {
            int change = Math.Abs(now.Axes[a] - baseline.Axes[a]);
            if (change > bestChange) { best = a; bestChange = change; }
        }
        if (best >= 0)
        {
            string source = AxisDef.SourceNames[best];
            foreach (var (name, def) in device.Axes)
                if (def.SourceIndex == best) return new DetectedInput(name, $"{name} (axis {source})");
            return new DetectedInput(null, $"Axis {source} moved, but it is not in this device's map (calibrate to add it).");
        }
        return null;
    }

    /// <summary>The friendly name for a button number, or buttonN when it has none.</summary>
    public static string ButtonName(DeviceMap device, int number)
    {
        foreach (var (name, n) in device.Buttons)
            if (n == number) return name;
        return "button" + number;
    }
}

public static class ButtonNaming
{
    private static readonly Regex Valid = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// Why this name cannot be used for a button (null if it can). Names must be single words made of
    /// letters, digits and underscores, must not clash with an axis, a hat direction or the automatic
    /// buttonN names, and must not repeat another button's name.
    /// </summary>
    public static string? Validate(string name, DeviceMap device, IReadOnlyDictionary<string, int> others, int number)
    {
        if (!Valid.IsMatch(name)) return "Use letters, digits and underscores, starting with a letter (for example shiftUp).";
        if (DeviceMap.TryParseButtonN(name, out _) || name.StartsWith("button", StringComparison.OrdinalIgnoreCase) && name[6..].All(char.IsDigit))
            return "Names like button12 are reserved for the automatic names.";
        if (device.Axes.ContainsKey(name)) return $"\"{name}\" is already the name of an axis.";
        if (DeviceMap.HatNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return $"\"{name}\" is a D-pad direction.";
        foreach (var (other, n) in others)
            if (n != number && string.Equals(other, name, StringComparison.OrdinalIgnoreCase))
                return $"\"{name}\" is already used for button {n}.";
        return null;
    }

    /// <summary>Names that existed in <paramref name="before"/> but are gone or reassigned in <paramref name="after"/>.</summary>
    public static IReadOnlyList<string> Removed(IReadOnlyDictionary<string, int> before, IReadOnlyDictionary<string, int> after) =>
        before.Where(b => !after.TryGetValue(b.Key, out int n) || n != b.Value).Select(b => b.Key).ToList();
}

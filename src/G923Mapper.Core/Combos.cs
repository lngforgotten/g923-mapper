using System.Text.RegularExpressions;

namespace G923Mapper;

/// <summary>Which wheel buttons are down, by name, in the form combinations use.</summary>
public static partial class ButtonNames
{
    [GeneratedRegex(@"^button(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex Numbered();

    /// <summary>
    /// The buttons (and D-pad directions) currently down in a frame. A button that has a name on this device (paddleL...) is reported by
    /// that name only; the others as buttonN. Axes are never included.
    /// </summary>
    public static IReadOnlyList<string> Down(InputFrame frame, DeviceMap device)
    {
        var names = new List<string>();
        var namedNumbers = new HashSet<int>(device.Buttons.Values);

        foreach (var (name, _) in device.Buttons.OrderBy(b => b.Value))
            if (frame.Get(name) >= 0.5) names.Add(name);

        foreach (var (key, value) in frame.Values.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (value < 0.5) continue;
            if (Numbered().Match(key) is { Success: true } m && int.TryParse(m.Groups[1].ValueSpan, out int n) && !namedNumbers.Contains(n)) names.Add(key);
        }

        foreach (string hat in DeviceMap.HatNames)
            if (frame.Get(hat) >= 0.5) names.Add(hat);
        return names;
    }
}

/// <summary>
/// Notices a combination of buttons held together for a while (for example both paddles for a second). Fires once, then waits until
/// the combination is let go, so holding it does not fire again and again. Extra buttons held at the same time do not matter.
/// </summary>
public sealed class ComboDetector
{
    private readonly HashSet<string> _inputs;
    private readonly int _holdMs;
    private long? _since;
    private bool _fired;

    public ComboDetector(IEnumerable<string> inputs, int holdMs)
    {
        _inputs = new HashSet<string>(inputs, StringComparer.OrdinalIgnoreCase);
        _holdMs = Math.Max(0, holdMs);
    }

    /// <summary>A detector with no inputs never fires (an unset combination).</summary>
    public bool IsEmpty => _inputs.Count == 0;

    /// <returns>True once, when the combination has been held for the hold time.</returns>
    public bool Update(IReadOnlyCollection<string> down, long nowMs)
    {
        if (_inputs.Count == 0) return false;
        bool all = _inputs.All(i => down.Contains(i, StringComparer.OrdinalIgnoreCase));
        if (!all) { _since = null; _fired = false; return false; }

        _since ??= nowMs;
        if (_fired || nowMs - _since.Value < _holdMs) return false;
        _fired = true;
        return true;
    }
}

/// <summary>Works out which buttons the user means by "this combination": the most that were held at once, until they are all let go.</summary>
public sealed class ComboCapture
{
    private IReadOnlyList<string> _best = [];

    /// <summary>The largest set of buttons seen down together so far.</summary>
    public IReadOnlyList<string> Best => _best;

    /// <summary>True once something was held and has now been fully released: the capture is complete.</summary>
    public bool Finished { get; private set; }

    public void Add(IReadOnlyCollection<string> down)
    {
        if (Finished) return;
        if (down.Count == 0) { if (_best.Count > 0) Finished = true; return; }
        if (down.Count >= _best.Count) _best = down.OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public static class ComboText
{
    public static string Format(IReadOnlyCollection<string>? inputs) =>
        inputs is null || inputs.Count == 0 ? "not set" : string.Join(" + ", inputs);

    /// <summary>"paddleL + paddleR" or "paddleL,paddleR" back to a list. Blank gives an empty list.</summary>
    public static IReadOnlyList<string> Parse(string? text) =>
        (text ?? "").Split(['+', ',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !s.Equals("not", StringComparison.OrdinalIgnoreCase) && !s.Equals("set", StringComparison.OrdinalIgnoreCase)).ToList();
}

namespace G923Mapper;

/// <summary>
/// PlayStation names for the buttons of a PlayStation edition wheel. Calibration names buttons the Xbox way (buttonA, view, menu...) because the
/// virtual controller is an Xbox one; for someone with a PlayStation wheel that reads wrongly on screen, so the same buttons also get their
/// PlayStation names (cross, circle, square, triangle, share, options), which are shown in preference. Both sets of names keep working in profiles.
/// </summary>
public static class PlayStationNames
{
    /// <summary>The Xbox-style name a calibration uses, and the PlayStation name for the same button (same position on the pad).</summary>
    public static readonly IReadOnlyList<(string Xbox, string PlayStation)> Pairs =
    [
        ("buttonA", "cross"), ("buttonB", "circle"), ("buttonX", "square"), ("buttonY", "triangle"),
        ("view", "share"), ("menu", "options"),
    ];

    /// <summary>
    /// Give every button that has an Xbox-style name its PlayStation name too, listing the PlayStation names first so they are the ones shown.
    /// A name that is already taken (an axis, or another button) is skipped. Changes and returns the same map.
    /// </summary>
    public static DeviceMap Apply(DeviceMap map)
    {
        var ps = new List<KeyValuePair<string, int>>();
        foreach (var (xbox, playStation) in Pairs)
        {
            if (!map.Buttons.TryGetValue(xbox, out int number)) continue;
            if (map.Buttons.ContainsKey(playStation) || map.Axes.ContainsKey(playStation)) continue;
            ps.Add(new(playStation, number));
        }
        if (ps.Count == 0) return map;

        var rebuilt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, number) in ps) rebuilt[name] = number;
        foreach (var (name, number) in map.Buttons) rebuilt[name] = number;
        map.Buttons = rebuilt;
        return map;
    }
}

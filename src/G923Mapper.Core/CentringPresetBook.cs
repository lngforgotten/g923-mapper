namespace G923Mapper;

/// <summary>
/// The presets for the centring curve: the ones that come with the app and the ones a person saves under their own names (a feel for one game,
/// say). Pure list handling, so it is tested without a window; the app keeps the person's own presets in its settings.
/// </summary>
public static class CentringPresetBook
{
    public const int MaxNameLength = 40, MaxOwnPresets = 30;

    /// <summary>The built-in presets followed by the person's own, in that order.</summary>
    public static IReadOnlyList<CentringPreset> All(IEnumerable<CentringPreset>? mine) => CentringCurve.Presets.Concat(mine ?? []).ToList();

    public static bool IsBuiltIn(string? name) =>
        CentringCurve.Presets.Any(p => string.Equals(p.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Why a name cannot be used for a saved preset, or null when it can: empty, too long, or the name of a built-in one.</summary>
    public static string? NameProblem(string? name)
    {
        string n = (name ?? "").Trim();
        if (n.Length == 0) return "Give the preset a name.";
        if (n.Length > MaxNameLength) return $"Keep the name to {MaxNameLength} characters or fewer.";
        if (IsBuiltIn(n)) return "That is the name of a built-in preset. Pick another.";
        return null;
    }

    /// <summary>The current curve settings as a preset with this name. Every value is kept inside what the sliders allow, so it can always be applied.</summary>
    public static CentringPreset FromSettings(string name, ForceSettings s)
    {
        int centre = Math.Clamp(s.CentreDegrees, 1, 45);
        return new CentringPreset(name.Trim(), Math.Clamp(s.SpringPercent, 0, 100), centre, Math.Clamp(s.PeakDegrees, Math.Max(10, centre + 1), 180),
            Math.Clamp(s.LighteningPercent, 0, 100), Math.Clamp(s.DampingPercent, 0, 100));
    }

    /// <summary>Whether this is one of the person's own presets.</summary>
    public static bool IsMine(IEnumerable<CentringPreset>? mine, string? name) =>
        (mine ?? []).Any(p => string.Equals(p.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Save a preset into the person's list: a preset with the same name (ignoring case) is replaced, otherwise it is added at the end. Returns
    /// false, with nothing changed, when the name is not allowed or the list is full.
    /// </summary>
    public static bool Save(List<CentringPreset> mine, CentringPreset preset)
    {
        if (NameProblem(preset.Name) is not null) return false;
        int at = mine.FindIndex(p => string.Equals(p.Name, preset.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (at >= 0) { mine[at] = preset with { Name = preset.Name.Trim() }; return true; }
        if (mine.Count >= MaxOwnPresets) return false;
        mine.Add(preset with { Name = preset.Name.Trim() });
        return true;
    }

    /// <summary>Delete one of the person's own presets. A built-in one cannot be deleted.</summary>
    public static bool Remove(List<CentringPreset> mine, string? name) =>
        mine.RemoveAll(p => string.Equals(p.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase)) > 0;
}

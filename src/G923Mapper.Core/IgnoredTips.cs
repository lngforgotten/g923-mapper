using System.Text.Json;
using System.Text.Json.Serialization;

namespace G923Mapper;

/// <summary>
/// The health tips the user has asked not to see again: particular leftover virtual controllers, or leftovers altogether. Kept in one
/// small file shared by every copy of the app (leftovers belong to the PC, not to a wheel).
/// </summary>
public sealed class IgnoredTips
{
    /// <summary>Never mention leftover virtual controllers, including ones that appear later.</summary>
    public bool AllLeftovers { get; set; }
    /// <summary>Instance ids of the leftovers to stay quiet about. A leftover that is not listed is still mentioned.</summary>
    public List<string> LeftoverIds { get; set; } = new();

    [JsonIgnore]
    public bool Any => AllLeftovers || LeftoverIds.Count > 0;

    public bool IsIgnored(string instanceId) => AllLeftovers || LeftoverIds.Contains(instanceId, StringComparer.OrdinalIgnoreCase);

    /// <summary>The leftovers still worth a tip.</summary>
    public IReadOnlyList<string> Unignored(IEnumerable<string> leftoverIds) => leftoverIds.Where(id => !IsIgnored(id)).ToList();

    public void Ignore(IEnumerable<string> instanceIds)
    {
        foreach (string id in instanceIds)
            if (VirtualPadText.IsVirtual(id) && !LeftoverIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                LeftoverIds.Add(id);
    }

    /// <summary>Stop ignoring these. Returns whether anything changed.</summary>
    public bool Restore(IEnumerable<string> instanceIds)
    {
        var gone = new HashSet<string>(instanceIds, StringComparer.OrdinalIgnoreCase);
        return LeftoverIds.RemoveAll(gone.Contains) > 0;
    }

    public void Reset()
    {
        AllLeftovers = false;
        LeftoverIds.Clear();
    }

    /// <summary>
    /// Drop ids whose entry is no longer on the PC at all, so a later leftover that reuses the number is mentioned. Does nothing for an
    /// empty list, which cannot be told apart from Windows' answer not being understood. Returns whether anything changed.
    /// </summary>
    public bool Prune(IReadOnlyList<VirtualPadInfo>? present) =>
        present is { Count: > 0 } && Restore(LeftoverIds.Where(id => !present.Any(p => p.InstanceId.Equals(id, StringComparison.OrdinalIgnoreCase))).ToList());

    /// <summary>"Leftover virtual controller 02" (the number is the one Settings lists).</summary>
    public static string Label(string instanceId) => $"Leftover virtual controller {instanceId.Split('\\').Last()}";

    /// <summary>One line for the saved health report.</summary>
    public string Describe() => !Any ? "none"
        : string.Join(", ", (AllLeftovers ? new[] { "all leftover virtual controllers" } : []).Concat(LeftoverIds.Select(Label)));
}

/// <summary>Reads and writes <see cref="IgnoredTips"/>. Every call reads the file afresh, so several copies of the app agree.</summary>
public sealed class IgnoredTipsFile(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    /// <summary>What is ignored now. A missing or unreadable file means nothing is.</summary>
    public IgnoredTips Read() => TryRead() ?? new IgnoredTips();

    /// <summary>Apply a change and save it. False when it could not be saved (the file is then left as it was).</summary>
    public bool Change(Action<IgnoredTips> change)
    {
        if (TryRead() is not { } tips) return false;
        change(tips);
        try
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(tips, Options));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Could not save the ignored tips: {ex.Message}");
            return false;
        }
    }

    /// <summary>What is ignored, after dropping (and saving the drop of) entries for controllers that are gone from the PC.</summary>
    public IgnoredTips ReadFor(IReadOnlyList<VirtualPadInfo>? present)
    {
        IgnoredTips tips = Read();
        if (tips.Prune(present)) Change(t => t.Prune(present));
        return tips;
    }

    // Null only when the file is there but cannot be opened, so that Change does not write over what it could not see.
    // A damaged file counts as empty: there is nothing in it to keep.
    private IgnoredTips? TryRead()
    {
        try
        {
            if (!File.Exists(path)) return new IgnoredTips();
            var tips = JsonSerializer.Deserialize<IgnoredTips>(File.ReadAllText(path), Options) ?? new IgnoredTips();
            tips.LeftoverIds ??= new();
            tips.LeftoverIds.RemoveAll(id => id is null || !VirtualPadText.IsVirtual(id));
            return tips;
        }
        catch (JsonException) { return new IgnoredTips(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}

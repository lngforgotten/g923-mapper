namespace G923Mapper;

/// <summary>One saved earlier version of a profile file.</summary>
public sealed record HistoryEntry(string Path, DateTime When, long Size);

/// <summary>
/// Keeps earlier versions of a profile every time it is about to be overwritten, so tuning is never lost to a bad edit. Versions live under
/// <c>history\&lt;profile&gt;\</c>, named by time, newest kept first, with the oldest removed past a limit. A save that changes nothing adds nothing.
/// </summary>
public static class ProfileHistory
{
    public const int DefaultKeep = 30;

    private static string FolderFor(string historyRoot, string profilePath) =>
        Path.Combine(historyRoot, Path.GetFileNameWithoutExtension(profilePath));

    /// <summary>
    /// Copy the profile's current file into its history, unless it is missing or identical to the newest saved version.
    /// Returns the new snapshot's path, or null when nothing was added. Never throws for a file problem: losing a snapshot must not stop a save.
    /// </summary>
    public static string? Snapshot(string profilePath, string historyRoot, DateTime? now = null, int keep = DefaultKeep)
    {
        try
        {
            if (!File.Exists(profilePath)) return null;
            string dir = FolderFor(historyRoot, profilePath);
            Directory.CreateDirectory(dir);

            byte[] current = File.ReadAllBytes(profilePath);
            var newest = Newest(dir);
            if (newest is not null && File.ReadAllBytes(newest).AsSpan().SequenceEqual(current)) return null;

            string target = Path.Combine(dir, (now ?? DateTime.Now).ToString("yyyyMMdd-HHmmss-fff") + ".json");
            for (int n = 2; File.Exists(target); n++) target = Path.Combine(dir, (now ?? DateTime.Now).ToString("yyyyMMdd-HHmmss-fff") + $"-{n}.json");
            File.WriteAllBytes(target, current);
            Prune(dir, keep);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Save this text as an earlier version of the profile called <paramref name="profileKey"/> (its file name without .json), unless it matches the newest
    /// saved version. Used when the previous text is already in hand, for example when the file has just been changed by another program.
    /// </summary>
    public static string? SnapshotText(string profileKey, string text, string historyRoot, DateTime? now = null, int keep = DefaultKeep)
    {
        try
        {
            string dir = Path.Combine(historyRoot, profileKey);
            Directory.CreateDirectory(dir);
            var newest = Newest(dir);
            if (newest is not null && File.ReadAllText(newest) == text) return null;

            DateTime t = now ?? DateTime.Now;
            string target = Path.Combine(dir, t.ToString("yyyyMMdd-HHmmss-fff") + ".json");
            for (int n = 2; File.Exists(target); n++) target = Path.Combine(dir, t.ToString("yyyyMMdd-HHmmss-fff") + $"-{n}.json");
            File.WriteAllText(target, text);
            Prune(dir, keep);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The saved versions of a profile, newest first.</summary>
    public static IReadOnlyList<HistoryEntry> List(string profilePath, string historyRoot)
    {
        string dir = FolderFor(historyRoot, profilePath);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.json")
            .Select(f => new HistoryEntry(f, Stamp(f), new FileInfo(f).Length))
            .OrderByDescending(e => e.When).ThenByDescending(e => e.Path, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Put an earlier version back. The version being replaced is saved first, so a restore can itself be undone.
    /// </summary>
    public static bool Restore(HistoryEntry entry, string profilePath, string historyRoot)
    {
        try
        {
            if (!File.Exists(entry.Path)) return false;
            byte[] old = File.ReadAllBytes(entry.Path);
            Snapshot(profilePath, historyRoot);
            File.WriteAllBytes(profilePath, old);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static string? Newest(string dir) =>
        Directory.EnumerateFiles(dir, "*.json").OrderByDescending(f => f, StringComparer.Ordinal).FirstOrDefault();

    private static void Prune(string dir, int keep)
    {
        var all = Directory.EnumerateFiles(dir, "*.json").OrderByDescending(f => f, StringComparer.Ordinal).ToList();
        foreach (string old in all.Skip(Math.Max(1, keep)))
        {
            try { File.Delete(old); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>The time in a snapshot's name (yyyyMMdd-HHmmss-fff...), or its file time if the name is unfamiliar.</summary>
    private static DateTime Stamp(string file)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        string head = name.Length >= 19 ? name[..19] : name;
        return DateTime.TryParseExact(head, "yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var t) ? t : File.GetLastWriteTime(file);
    }
}

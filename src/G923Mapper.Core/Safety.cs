using System.Text;

namespace G923Mapper;

/// <summary>
/// Programs with anti-cheat, where only the plain virtual controller is allowed: no keyboard output, whatever the profile says.
/// Starts with the GTA V executables; the user can add more in anticheat.txt (one name per line, # for comments).
/// </summary>
public sealed class AntiCheatList
{
    public static readonly string[] Defaults = ["GTA5.exe", "GTA5_Enhanced.exe"];

    private readonly HashSet<string> _names;

    public AntiCheatList(IEnumerable<string> exes) =>
        _names = exes.Select(ProfileSelector.Normalize).Where(n => n.Length > 0).ToHashSet();

    /// <summary>The list in force for this run. Tests and the app set it; it always starts as the defaults.</summary>
    public static AntiCheatList Current { get; set; } = new(Defaults);

    public IReadOnlyCollection<string> Names => _names;

    public bool Contains(string exe) => _names.Contains(ProfileSelector.Normalize(exe));

    /// <summary>The first of the profile's games that is on the list, or null.</summary>
    public string? FirstProtected(IEnumerable<string> exes) => exes.FirstOrDefault(Contains);

    public static string DefaultFileText() =>
        "# Games with anti-cheat. Profiles for these games can only use the plain virtual controller:\n" +
        "# keyboard output is switched off for them even if their profile asks for it.\n" +
        "# One program name per line. Lines starting with # are ignored. Restart G923 Mapper after editing.\n" +
        string.Concat(Defaults.Select(d => d + "\n"));

    public static AntiCheatList Parse(string text) =>
        new(text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')));

    /// <summary>Reads the user's list, creating the file with the defaults first if there is none. A file that cannot be read gives the defaults.</summary>
    public static AntiCheatList Load(string path)
    {
        try
        {
            if (!File.Exists(path)) File.WriteAllText(path, DefaultFileText());
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AntiCheatList(Defaults);
        }
    }
}

public static class ProfileSafety
{
    public const string Strict = "strict";
    public const string Standard = "standard";
    public static readonly string[] All = [Strict, Standard];

    /// <summary>
    /// The profile's safety mode. A profile written before the setting existed keeps working: with keyboard output on it counts as
    /// standard (as it always behaved), otherwise strict.
    /// </summary>
    public static string Effective(Profile p) =>
        p.Safety is { } s && All.Contains(s, StringComparer.OrdinalIgnoreCase) ? s.ToLowerInvariant()
        : p.Outputs.Keyboard || p.Outputs.Turbo ? Standard : Strict;

    /// <summary>Why keyboard output is not active for this profile, or null when it is (or was never asked for).</summary>
    public static string? KeyboardBlockedReason(Profile p, AntiCheatList? list = null)
    {
        if (!p.Outputs.Keyboard) return "outputs.keyboard is false";
        if (Effective(p) != Standard) return "the profile's safety mode is strict";
        if ((list ?? AntiCheatList.Current).FirstProtected(p.Match.Exe) is { } exe) return $"{exe} is on the anti-cheat list";
        return null;
    }

    /// <summary>Whether simulated key presses may be sent for this profile.</summary>
    public static bool KeyboardAllowed(Profile p, AntiCheatList? list = null) => KeyboardBlockedReason(p, list) is null;

    /// <summary>Why turbo (auto-repeating a button) is not active for this profile, or null when it is. Turbo is a standard-mode extra only.</summary>
    public static string? TurboBlockedReason(Profile p, AntiCheatList? list = null)
    {
        if (!p.Outputs.Turbo) return "outputs.turbo is false";
        if (Effective(p) != Standard) return "the profile's safety mode is strict";
        if ((list ?? AntiCheatList.Current).FirstProtected(p.Match.Exe) is { } exe) return $"{exe} is on the anti-cheat list";
        return null;
    }

    public static bool TurboAllowed(Profile p, AntiCheatList? list = null) => TurboBlockedReason(p, list) is null;
}

/// <summary>
/// When to give the wheel back to other programs. HidHide's setting outlives this app, so a crash or kill while the wheel is hidden
/// leaves G HUB and every other program blind to it until someone unhides it.
/// </summary>
public static class WheelRestorePolicy
{
    /// <summary>On exit: unhide only if the user wants that, HidHide is there, the wheel really is hidden, and no other copy of the app is still using it.</summary>
    public static bool UnhideOnExit(bool setting, bool hidHideInstalled, bool wheelHidden, bool otherCopyRunning) =>
        setting && hidHideInstalled && wheelHidden && !otherCopyRunning;

    /// <summary>
    /// On start: with "show the wheel again on exit" on, a wheel that is still hidden means the last session did not get to
    /// restore it (it crashed, was killed, or the permission prompt was declined), so say so.
    /// </summary>
    public static bool WarnAtStart(bool setting, bool hidHideInstalled, bool wheelHidden) =>
        setting && hidHideInstalled && wheelHidden;
}

/// <summary>Plain-text summary of the health check, for attaching to a bug report or sending to a friend.</summary>
public static class HealthReport
{
    public static string Build(DateTime when, IEnumerable<string> header, IReadOnlyList<HealthItem> items, IEnumerable<string> logTail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("G923 Mapper health report");
        sb.AppendLine($"Created: {when:yyyy-MM-dd HH:mm:ss}");
        foreach (string line in header) sb.AppendLine(line);
        sb.AppendLine();
        sb.AppendLine($"Summary: {HealthEvaluator.Summary(items)}");
        sb.AppendLine();
        foreach (HealthItem i in items)
        {
            string tag = i.Level switch { HealthLevel.Ok => "OK     ", HealthLevel.Info => "INFO   ", HealthLevel.Warning => "WARNING", _ => "PROBLEM" };
            sb.AppendLine($"[{tag}] {i.Title}");
            if (!string.IsNullOrWhiteSpace(i.Detail)) sb.AppendLine($"          {i.Detail}");
        }
        var tail = logTail.ToList();
        if (tail.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Recent log lines:");
            foreach (string l in tail) sb.AppendLine("  " + l);
        }
        return sb.ToString();
    }
}

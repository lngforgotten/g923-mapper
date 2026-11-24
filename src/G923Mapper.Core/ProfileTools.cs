using System.Globalization;
using System.Text;
using System.Text.Json;

namespace G923Mapper;

/// <summary>Chooses which profile applies to the programs that are currently running.</summary>
public static class ProfileSelector
{
    /// <summary>"GTA5.exe", "gta5.EXE" and "gta5" all compare equal.</summary>
    public static string Normalize(string exe)
    {
        exe = exe.Trim();
        return (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe[..^4] : exe).ToLowerInvariant();
    }

    /// <summary>
    /// The first profile (ordered by key, so the result is deterministic) whose match.exe list contains a
    /// running program; null when none match. Returns which program matched.
    /// </summary>
    public static (string Key, string Exe)? Select(IEnumerable<(string Key, Profile Profile)> profiles, IEnumerable<string> running)
    {
        var runningSet = running.Select(Normalize).ToHashSet();
        foreach (var (key, profile) in profiles.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            foreach (string exe in profile.Match.Exe)
                if (runningSet.Contains(Normalize(exe))) return (key, exe);
        }
        return null;
    }
}

/// <summary>When a profile's game counts as "active".</summary>
public enum AutoSwitchMode
{
    /// <summary>As long as the game's program is running, even in the background (the default).</summary>
    WhenRunning,
    /// <summary>Only while the game's window is the one in front.</summary>
    WhenFocused,
}

/// <summary>
/// Reports when a profile's game starts or stops (or comes to the front or leaves it, in focus mode).
/// Holds no manual choice: the caller falls back to the user's own selection when <see cref="ActiveKey"/> is null.
/// </summary>
public sealed class AutoProfileSwitcher : IDisposable
{
    /// <summary>
    /// Programs that never count as "something else is in front": this app, and the Windows shell that is in front
    /// during Alt-Tab, the taskbar, the desktop and the Start menu. Bringing them forward leaves the profile alone.
    /// </summary>
    private static readonly HashSet<string> Neutral = new(StringComparer.OrdinalIgnoreCase)
    {
        "g923mapper", "explorer", "shellexperiencehost", "searchhost", "startmenuexperiencehost",
        "textinputhost", "applicationframehost", "lockapp", "taskmgr",
    };

    private readonly Func<IEnumerable<(string Key, Profile Profile)>> _profiles;
    private readonly Func<IEnumerable<string>> _running;
    private readonly Func<string?> _foreground;
    private readonly Func<long> _clockMs;
    private readonly TimeSpan _lostDelay;
    private readonly Timer _timer;
    private readonly object _lock = new();
    private long? _lostSince;
    private int _ticks;

    public bool Enabled { get; set; } = true;

    private AutoSwitchMode _mode = AutoSwitchMode.WhenRunning;
    public AutoSwitchMode Mode
    {
        get => _mode;
        set { _mode = value; _lostSince = null; }
    }

    public string? ActiveKey { get; private set; }
    public string? ActiveExe { get; private set; }

    /// <summary>Raised when the matching profile changes: (key, exe) when a game is found, (null, null) when it is gone or switching is off.</summary>
    public event Action<string?, string?>? Changed;

    /// <param name="foreground">The program (name or name.exe) whose window is in front, or null. Used in focus mode.</param>
    /// <param name="lostDelay">How long something else must stay in front before the profile reverts (default 1.5 s).</param>
    public AutoProfileSwitcher(Func<IEnumerable<(string Key, Profile Profile)>> profiles, Func<IEnumerable<string>>? running = null,
                               TimeSpan? interval = null, Func<string?>? foreground = null, Func<long>? clockMs = null, TimeSpan? lostDelay = null)
    {
        _profiles = profiles;
        _running = running ?? RunningProcesses.Names;
        _foreground = foreground ?? (() => null);
        _clockMs = clockMs ?? (() => Environment.TickCount64);
        _lostDelay = lostDelay ?? TimeSpan.FromMilliseconds(1500);
        TimeSpan every = interval ?? TimeSpan.FromMilliseconds(500);
        _timer = new Timer(_ => Tick(), null, every, every);
    }

    /// <summary>Profiles currently served by a store, keyed by file name without .json.</summary>
    public static Func<IEnumerable<(string Key, Profile Profile)>> FromStore(ProfileStore store) => () =>
        store.Files
            .Select(f => (Key: Path.GetFileNameWithoutExtension(f), Profile: store.Get(f)))
            .Where(p => p.Profile is not null && !p.Profile.IsTemplate)   // a template is not attached to a game, whatever it lists
            .Select(p => (p.Key, p.Profile!))
            .ToList();

    /// <summary>Focus mode checks every tick (cheap); the process-list mode checks about every 2 seconds.</summary>
    private void Tick()
    {
        if (Mode == AutoSwitchMode.WhenRunning && (++_ticks % 4) != 0) return;
        Poll();
    }

    public void Poll()
    {
        string? key = null, exe = null;
        if (Enabled)
        {
            try
            {
                if (Mode == AutoSwitchMode.WhenFocused)
                {
                    if (!DecideFocused(out key, out exe)) return;   // no decision this time: leave things as they are
                }
                else
                {
                    var hit = ProfileSelector.Select(_profiles(), _running());
                    if (hit is { } h) { key = h.Key; exe = h.Exe; }
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException) { return; }
        }

        bool changed;
        lock (_lock)
        {
            changed = !string.Equals(key, ActiveKey, StringComparison.OrdinalIgnoreCase);
            ActiveKey = key;
            ActiveExe = exe;
        }
        if (changed) Changed?.Invoke(key, exe);
    }

    /// <summary>
    /// Focus mode. Returns false when there is nothing to decide (this app or the Windows shell is in front, or no window is).
    /// Otherwise sets the profile for the program in front; if it is not a game, the profile only reverts once that has
    /// stayed true for the delay, so overlays and the Alt-Tab flicker do nothing.
    /// </summary>
    private bool DecideFocused(out string? key, out string? exe)
    {
        key = exe = null;
        string? front = _foreground();
        if (string.IsNullOrWhiteSpace(front) || Neutral.Contains(ProfileSelector.Normalize(front)))
        {
            // Nothing to decide from what is in front (this app, the desktop, Alt-Tab). But if the game we switched
            // for has since closed, go back to the user's own profile instead of staying on a game that is gone.
            if (ActiveKey is not null && ActiveExe is not null && !IsRunning(ActiveExe))
            {
                _lostSince = null;
                return true;
            }
            return false;
        }

        var hit = ProfileSelector.Select(_profiles(), [front]);
        if (hit is { } h)
        {
            key = h.Key; exe = h.Exe;
            _lostSince = null;
            return true;
        }

        if (ActiveKey is null) { _lostSince = null; return true; }      // already on the user's own profile
        long now = _clockMs();
        _lostSince ??= now;
        if (now - _lostSince.Value < (long)_lostDelay.TotalMilliseconds) return false;   // not long enough yet
        _lostSince = null;
        return true;                                                     // revert: key stays null
    }

    private bool IsRunning(string exe) =>
        _running().Select(ProfileSelector.Normalize).Contains(ProfileSelector.Normalize(exe));

    public void Dispose() => _timer.Dispose();
}

public static class RunningProcesses
{
    /// <summary>Executable names (with .exe) of the running processes. Processes that vanish while listing are skipped.</summary>
    public static IEnumerable<string> Names()
    {
        var names = new List<string>();
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try { names.Add(p.ProcessName + ".exe"); }
            catch (InvalidOperationException) { }
            finally { p.Dispose(); }
        }
        return names;
    }
}

/// <summary>Reads and writes custom curve points as text: "0,0 0.5,0.2 1,1".</summary>
public static class CurveText
{
    public static string Format(double[][]? points) =>
        points is null ? "" : string.Join(" ", points.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p[0]:0.###},{p[1]:0.###}")));

    /// <summary>Parse; returns false with a message when the text is not a list of x,y pairs.</summary>
    public static bool TryParse(string text, out double[][]? points, out string? error)
    {
        points = null; error = null;
        if (string.IsNullOrWhiteSpace(text)) return true;

        var list = new List<double[]>();
        foreach (string pair in text.Split([' ', ';', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] xy = pair.Split(',');
            if (xy.Length != 2
                || !double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                || !double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
            {
                error = $"\"{pair}\" is not an x,y pair (for example 0.5,0.2).";
                return false;
            }
            list.Add([x, y]);
        }
        points = list.ToArray();
        return true;
    }
}

public static class ProfileWriter
{
    /// <summary>
    /// Profile as readable JSON in the same layout as the starter profiles: one rule per line, and
    /// only the fields that differ from their defaults. Comments in an original file are not kept.
    /// </summary>
    public static string ToJson(Profile p) => p.Parent is not null ? Templates.ChildJson(p) : PlainJson(p);

    /// <summary>
    /// The profile with every setting and rule written out, never in the form that stores only differences from a template. What is shared, copied
    /// and checked; a profile with a template is written to its file with <see cref="ToJson"/>.
    /// </summary>
    public static string PlainJson(Profile p)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"name\": {Str(p.Name)},");
        if (!string.IsNullOrWhiteSpace(p.Notes)) sb.AppendLine($"  \"notes\": {Str(p.Notes.Trim())},");
        if (p.IsTemplate) sb.AppendLine("  \"isTemplate\": true,");
        if (p.Match.Exe.Count > 0)
            sb.AppendLine($"  \"match\": {{ \"exe\": [{string.Join(", ", p.Match.Exe.Select(Str))}] }},");
        sb.AppendLine($"  \"device\": {Str(p.Device)},");
        sb.AppendLine($"  \"safety\": {Str(ProfileSafety.Effective(p))},");
        sb.AppendLine($"  \"outputs\": {{ \"keyboard\": {(p.Outputs.Keyboard ? "true" : "false")}{(p.Outputs.Turbo ? ", \"turbo\": true" : "")}{(PadKinds.IsAuto(p.Outputs.Pad) ? ", \"pad\": \"auto\"" : PadKinds.Of(p) == PadKind.DualShock4 ? ", \"pad\": \"ds4\"" : "")}{(p.Outputs.SecondPad ? ", \"secondPad\": true" : "")} }},");
        if (p.HideWheel is bool hideWheel) sb.AppendLine($"  \"hideWheel\": {(hideWheel ? "true" : "false")},");
        if (p.LeaveWheel) sb.AppendLine("  \"leaveWheel\": true,");
        if (p.Force is { IsEmpty: false } force)
        {
            var parts = new List<string>();
            if (force.Rumble is int rumble) parts.Add($"\"rumble\": {rumble}");
            if (force.Spring is int spring) parts.Add($"\"spring\": {spring}");
            if (force.Friction is int friction) parts.Add($"\"friction\": {friction}");
            if (force.Motors is { } motors) parts.Add($"\"motors\": {Str(motors.ToLowerInvariant())}");
            sb.AppendLine($"  \"force\": {{ {string.Join(", ", parts)} }},");
        }
        sb.AppendLine("  \"rules\": [");
        for (int i = 0; i < p.Rules.Count; i++)
            sb.AppendLine("    " + RuleText(p.Rules[i]) + (i < p.Rules.Count - 1 ? "," : ""));
        sb.AppendLine(p.Modes.Count > 0 ? "  ]," : "  ]");
        if (p.Modes.Count > 0) AppendModes(sb, p);
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>The "modes" array, as the last thing in the file.</summary>
    internal static void AppendModes(StringBuilder sb, Profile p)
    {
        sb.AppendLine("  \"modes\": [");
        for (int i = 0; i < p.Modes.Count; i++)
        {
            ProfileMode m = p.Modes[i];
            sb.AppendLine("    {");
            sb.AppendLine($"      \"name\": {Str(m.Name)},");
            if (m.Force is { IsEmpty: false } f)
            {
                var parts = new List<string>();
                if (f.Rumble is int ru) parts.Add($"\"rumble\": {ru}");
                if (f.Spring is int sp) parts.Add($"\"spring\": {sp}");
                if (f.Friction is int fr) parts.Add($"\"friction\": {fr}");
                if (f.Motors is { } mo) parts.Add($"\"motors\": {Str(mo.ToLowerInvariant())}");
                sb.AppendLine($"      \"force\": {{ {string.Join(", ", parts)} }},");
            }
            if (m.RemovedRules is { Count: > 0 } removed) sb.AppendLine($"      \"removedRules\": [{string.Join(", ", removed.Select(Str))}],");
            sb.AppendLine("      \"rules\": [");
            for (int j = 0; j < m.Rules.Count; j++)
                sb.AppendLine("        " + RuleText(m.Rules[j]) + (j < m.Rules.Count - 1 ? "," : ""));
            sb.AppendLine("      ]");
            sb.AppendLine("    }" + (i < p.Modes.Count - 1 ? "," : ""));
        }
        sb.AppendLine("  ]");
    }

    /// <summary>One rule as a line of JSON, with only the fields that differ from their defaults.</summary>
    public static string RuleText(Rule r)
    {
        var parts = new List<string> { $"\"from\": {Str(r.From)}", $"\"to\": {Str(r.To)}", $"\"curve\": {Str(r.Curve.ToLowerInvariant())}" };
        bool expo = r.Curve.Equals("expo", StringComparison.OrdinalIgnoreCase);
        bool custom = r.Curve.Equals("custom", StringComparison.OrdinalIgnoreCase);
        if (expo && r.Exponent != 2.0) parts.Add($"\"exponent\": {Num(r.Exponent)}");
        if (custom && r.Points is not null)
            parts.Add($"\"points\": [{string.Join(", ", r.Points.Select(pt => $"[{Num(pt[0])}, {Num(pt[1])}]"))}]");
        if (r.Deadzone != 0) parts.Add($"\"deadzone\": {Num(r.Deadzone)}");
        if (r.AntiDeadzone != 0) parts.Add($"\"antiDeadzone\": {Num(r.AntiDeadzone)}");
        if (r.Invert) parts.Add("\"invert\": true");
        if (r.Threshold is double t) parts.Add($"\"threshold\": {Num(t)}");
        if (!r.Mode.Equals("hold", StringComparison.OrdinalIgnoreCase)) parts.Add($"\"mode\": {Str(r.Mode.ToLowerInvariant())}");
        if (r.Mode.Equals("pulse", StringComparison.OrdinalIgnoreCase) && r.PulseMs != 50) parts.Add($"\"pulseMs\": {r.PulseMs}");
        if (r.Range is double range) parts.Add($"\"range\": {Num(range)}");
        if (r.Part is { } part && !part.Equals("full", StringComparison.OrdinalIgnoreCase)) parts.Add($"\"part\": {Str(part.ToLowerInvariant())}");
        if (r.Smooth > 0) parts.Add($"\"smooth\": {r.Smooth}");
        if (!string.IsNullOrWhiteSpace(r.Expr)) parts.Add($"\"expr\": {Str(r.Expr)}");
        if (r.Turbo) parts.Add("\"turbo\": true");
        if (r.Turbo && r.TurboHz != 10) parts.Add($"\"turboHz\": {Num(r.TurboHz)}");
        return "{ " + string.Join(", ", parts) + " }";
    }

    private static string Str(string s) => JsonSerializer.Serialize(s);
    private static string Num(double d) => d.ToString("R", CultureInfo.InvariantCulture);
}

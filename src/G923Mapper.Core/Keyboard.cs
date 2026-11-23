namespace G923Mapper;

/// <summary>A Windows virtual-key code, and whether the key is an "extended" key (arrows, Insert, right Ctrl...).</summary>
public readonly record struct KeyCode(ushort Vk, bool Extended);

public static class KeyNames
{
    private static readonly Dictionary<string, KeyCode> Map = Build();

    /// <summary>A short, friendly list for the rule editor. Any name accepted by <see cref="TryGet"/> can still be typed.</summary>
    public static readonly IReadOnlyList<string> Common =
    [
        "Space", "Enter", "Escape", "Tab", "Shift", "Ctrl", "Alt", "Up", "Down", "Left", "Right",
        "Q", "E", "F", "R", "C", "V", "X", "Z", "F1", "F2", "F3", "F4",
    ];

    /// <summary>The prefix of a mouse button name: "Mouse:Left". Mouse buttons are sent through the same output as keys.</summary>
    public const string MousePrefix = "Mouse:";

    /// <summary>The mouse buttons that can be an output.</summary>
    public static readonly IReadOnlyList<string> MouseButtonNames = ["Mouse:Left", "Mouse:Right", "Mouse:Middle", "Mouse:X1", "Mouse:X2"];

    /// <summary>Every keyboard key name, in a sensible order for a pick-list (letters, digits, function keys, then the rest).</summary>
    public static IReadOnlyList<string> AllKeyNames { get; } = Order();

    public static bool TryGet(string name, out KeyCode code) => Map.TryGetValue(name.Trim(), out code);

    /// <summary>The name of the key with this Windows virtual-key code (the first one in the pick-list order), or null if this app has no name for it.</summary>
    public static string? NameForVk(ushort vk) => AllKeyNames.FirstOrDefault(n => Map.TryGetValue(n, out var c) && c.Vk == vk);

    /// <summary>"Mouse:Left", "mouse:x2"... a mouse button this app can press.</summary>
    public static bool IsMouse(string name) => CanonicalMouse(name) is not null;

    /// <summary>A keyboard key or a mouse button.</summary>
    public static bool IsKnown(string name) => TryGet(name, out _) || IsMouse(name);

    /// <summary>The canonical spelling of a key or mouse button name (so "q" and "Q" are one key), or null if unknown.</summary>
    public static string? Canonical(string name) =>
        CanonicalMouse(name) ?? Map.Keys.FirstOrDefault(k => string.Equals(k, name.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string? CanonicalMouse(string name) =>
        MouseButtonNames.FirstOrDefault(k => string.Equals(k, name.Trim(), StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> Order()
    {
        var names = new List<string>();
        for (char c = 'A'; c <= 'Z'; c++) names.Add(c.ToString());
        for (char c = '0'; c <= '9'; c++) names.Add(c.ToString());
        for (int i = 1; i <= 24; i++) names.Add("F" + i);
        // Everything else, grouped by key code, with the aliases (Esc, Return, Control...) left out of the list.
        var skip = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase) { "Return", "Esc", "Control", "Apps", "Backtick", "Tilde" };
        names.AddRange(Map.Keys.Where(k => !skip.Contains(k)).OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
        return names;
    }

    private static Dictionary<string, KeyCode> Build()
    {
        var m = new Dictionary<string, KeyCode>(StringComparer.OrdinalIgnoreCase);
        for (char c = 'A'; c <= 'Z'; c++) m[c.ToString()] = new KeyCode(c, false);
        for (char c = '0'; c <= '9'; c++) m[c.ToString()] = new KeyCode(c, false);
        for (int i = 1; i <= 24; i++) m["F" + i] = new KeyCode((ushort)(0x6F + i), false);
        for (int i = 0; i <= 9; i++) m["Numpad" + i] = new KeyCode((ushort)(0x60 + i), false);

        void Add(string name, ushort vk, bool ext = false, params string[] aliases)
        {
            m[name] = new KeyCode(vk, ext);
            foreach (string a in aliases) m[a] = new KeyCode(vk, ext);
        }
        Add("Space", 0x20); Add("Enter", 0x0D, false, "Return"); Add("Escape", 0x1B, false, "Esc");
        Add("Tab", 0x09); Add("Backspace", 0x08); Add("CapsLock", 0x14);
        Add("Shift", 0x10); Add("LShift", 0xA0); Add("RShift", 0xA1);
        Add("Ctrl", 0x11, false, "Control"); Add("LCtrl", 0xA2); Add("RCtrl", 0xA3, true);
        Add("Alt", 0x12); Add("LAlt", 0xA4); Add("RAlt", 0xA5, true);
        Add("Up", 0x26, true); Add("Down", 0x28, true); Add("Left", 0x25, true); Add("Right", 0x27, true);
        Add("Home", 0x24, true); Add("End", 0x23, true); Add("PageUp", 0x21, true); Add("PageDown", 0x22, true);
        Add("Insert", 0x2D, true); Add("Delete", 0x2E, true);

        // Number pad operators, lock and system keys
        Add("NumpadMultiply", 0x6A); Add("NumpadAdd", 0x6B); Add("NumpadSubtract", 0x6D); Add("NumpadDecimal", 0x6E);
        Add("NumpadDivide", 0x6F, true); Add("NumpadEnter", 0x0D, true); Add("NumLock", 0x90, true);
        Add("ScrollLock", 0x91); Add("Pause", 0x13); Add("PrintScreen", 0x2C, true);
        Add("LWin", 0x5B, true); Add("RWin", 0x5C, true); Add("Menu", 0x5D, true, "Apps");

        // Punctuation (named by what the key is, so it does not matter which keyboard layout you use)
        Add("Semicolon", 0xBA); Add("Equals", 0xBB); Add("Comma", 0xBC); Add("Minus", 0xBD); Add("Period", 0xBE);
        Add("Slash", 0xBF); Add("Grave", 0xC0, false, "Backtick", "Tilde"); Add("LeftBracket", 0xDB);
        Add("Backslash", 0xDC); Add("RightBracket", 0xDD); Add("Quote", 0xDE);

        // Media keys
        Add("VolumeUp", 0xAF, true); Add("VolumeDown", 0xAE, true); Add("VolumeMute", 0xAD, true);
        Add("MediaPlayPause", 0xB3, true); Add("MediaNext", 0xB0, true); Add("MediaPrevious", 0xB1, true); Add("MediaStop", 0xB2, true);
        return m;
    }
}

/// <summary>Sends key presses to Windows. Implemented with SendInput in the Runtime project.</summary>
public interface IKeySink : IDisposable
{
    /// <summary>Make exactly this set of keys held down: press the new ones, release the ones no longer wanted.</summary>
    void Update(IReadOnlyCollection<string> wanted);
    /// <summary>Release everything currently held. Safe to call any time; used on stop, profile change and exit.</summary>
    void ReleaseAll();
}

/// <summary>The press/release bookkeeping, separate from the OS call so it can be tested.</summary>
public sealed class KeyStateTracker
{
    private readonly HashSet<string> _down = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Down => _down;

    /// <summary>Compare with the wanted set; returns what to press and release, and remembers the new state.</summary>
    public (IReadOnlyList<string> Press, IReadOnlyList<string> Release) Update(IReadOnlyCollection<string> wanted)
    {
        var want = new HashSet<string>(wanted, StringComparer.OrdinalIgnoreCase);
        var release = _down.Where(k => !want.Contains(k)).ToList();
        var press = want.Where(k => !_down.Contains(k)).ToList();
        foreach (string k in release) _down.Remove(k);
        foreach (string k in press) _down.Add(k);
        return (press, release);
    }

    public IReadOnlyList<string> ReleaseAll()
    {
        var all = _down.ToList();
        _down.Clear();
        return all;
    }
}

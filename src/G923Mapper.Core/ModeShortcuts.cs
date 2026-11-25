namespace G923Mapper;

public enum TriggerKind { Key, Mouse }

/// <summary>The mouse buttons a shortcut may use. Left and right are never offered: they would stop clicking working.</summary>
public enum MouseTriggerButton { Middle, Back, Forward }

/// <summary>
/// One keyboard key (with any of Ctrl, Alt, Shift, Win held) or one mouse button (middle, back or forward) that can switch modes, kept as text in
/// the settings: "F8", "Ctrl+F6", "Shift+Num7", "Mouse:Back", "Ctrl+Mouse:Middle". A plain letter or digit is refused (it would switch modes
/// whenever you typed it in any program); function keys, the navigation block, the number pad and any key with a modifier are fine.
/// </summary>
public sealed record InputTrigger(HotkeyModifiers Modifiers, TriggerKind Kind, int VirtualKey, MouseTriggerButton Mouse)
{
    public static InputTrigger ForKey(HotkeyModifiers modifiers, int virtualKey) => new(modifiers, TriggerKind.Key, virtualKey, default);
    public static InputTrigger ForMouse(HotkeyModifiers modifiers, MouseTriggerButton button) => new(modifiers, TriggerKind.Mouse, 0, button);

    // ---------------------------------------------------------------- key names

    private static readonly (string Name, int Vk, bool BareOk)[] Keys = BuildKeys();

    private static (string, int, bool)[] BuildKeys()
    {
        var list = new List<(string, int, bool)>();
        for (char c = 'A'; c <= 'Z'; c++) list.Add((c.ToString(), c, false));
        for (char c = '0'; c <= '9'; c++) list.Add((c.ToString(), c, false));
        for (int f = 1; f <= 24; f++) list.Add(("F" + f, 0x70 + f - 1, true));
        for (int n = 0; n <= 9; n++) list.Add(("Num" + n, 0x60 + n, true));
        list.AddRange(
        [
            ("NumMultiply", 0x6A, true), ("NumAdd", 0x6B, true), ("NumSubtract", 0x6D, true), ("NumDecimal", 0x6E, true), ("NumDivide", 0x6F, true),
            ("Left", 0x25, true), ("Up", 0x26, true), ("Right", 0x27, true), ("Down", 0x28, true),
            ("Insert", 0x2D, true), ("Delete", 0x2E, true), ("Home", 0x24, true), ("End", 0x23, true), ("PageUp", 0x21, true), ("PageDown", 0x22, true),
            ("Pause", 0x13, true), ("ScrollLock", 0x91, true),
            ("Space", 0x20, false), ("Tab", 0x09, false), ("Enter", 0x0D, false),
            ("Semicolon", 0xBA, false), ("Equals", 0xBB, false), ("Comma", 0xBC, false), ("Minus", 0xBD, false), ("Period", 0xBE, false),
            ("Slash", 0xBF, false), ("Backtick", 0xC0, false), ("LeftBracket", 0xDB, false), ("Backslash", 0xDC, false), ("RightBracket", 0xDD, false), ("Quote", 0xDE, false),
        ]);
        return list.ToArray();
    }

    /// <summary>The name of a key a shortcut may use, or null for any other key.</summary>
    public static string? KeyName(int virtualKey) => Keys.Where(k => k.Vk == virtualKey).Select(k => k.Name).FirstOrDefault();

    /// <summary>Whether this key is allowed on its own, with no Ctrl, Alt, Shift or Win held.</summary>
    public static bool BareKeyAllowed(int virtualKey) => Keys.Any(k => k.Vk == virtualKey && k.BareOk);

    /// <summary>Whether a virtual key is Ctrl, Alt, Shift or Win itself (never a trigger on its own).</summary>
    public static bool IsModifierKey(int virtualKey) => virtualKey is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C;

    /// <summary>The Ctrl, Alt, Shift or Win a modifier key stands for, or None.</summary>
    public static HotkeyModifiers ModifierOf(int virtualKey) => virtualKey switch
    {
        0x11 or 0xA2 or 0xA3 => HotkeyModifiers.Ctrl,
        0x12 or 0xA4 or 0xA5 => HotkeyModifiers.Alt,
        0x10 or 0xA0 or 0xA1 => HotkeyModifiers.Shift,
        0x5B or 0x5C => HotkeyModifiers.Win,
        _ => HotkeyModifiers.None,
    };

    // ---------------------------------------------------------------- text

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Kind == TriggerKind.Mouse ? "Mouse:" + Mouse : KeyName(VirtualKey) ?? $"Key{VirtualKey}");
        return string.Join("+", parts);
    }

    /// <summary>Read "Ctrl+F6", "Mouse:Back", "shift+mouse:middle". The reason is given when the text cannot be used.</summary>
    public static bool TryParse(string? text, out InputTrigger? trigger, out string? error)
    {
        trigger = null; error = null;
        if (string.IsNullOrWhiteSpace(text)) { error = "Nothing was pressed."; return false; }

        var mods = HotkeyModifiers.None;
        string? main = null;
        foreach (string raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= HotkeyModifiers.Ctrl; break;
                case "alt": mods |= HotkeyModifiers.Alt; break;
                case "shift": mods |= HotkeyModifiers.Shift; break;
                case "win" or "windows": mods |= HotkeyModifiers.Win; break;
                default:
                    if (main is not null) { error = $"Only one key or button, not both \"{main}\" and \"{raw}\"."; return false; }
                    main = raw;
                    break;
            }
        }
        if (main is null) { error = "Ctrl, Alt, Shift or Win on their own are not enough: add a key or a mouse button."; return false; }

        if (main.StartsWith("mouse:", StringComparison.OrdinalIgnoreCase))
        {
            string name = main[6..].Trim().ToLowerInvariant();
            MouseTriggerButton? button = name switch
            {
                "middle" => MouseTriggerButton.Middle,
                "back" or "x1" => MouseTriggerButton.Back,
                "forward" or "x2" => MouseTriggerButton.Forward,
                _ => null,
            };
            if (button is null)
            {
                error = name is "left" or "right" ? "The left and right mouse buttons cannot be used: they would stop clicking working. Use the middle, back or forward button."
                                                   : $"\"{main[6..]}\" is not a mouse button I know. Use Middle, Back or Forward.";
                return false;
            }
            trigger = ForMouse(mods, button.Value);
            return true;
        }

        var key = Keys.FirstOrDefault(k => k.Name.Equals(main, StringComparison.OrdinalIgnoreCase));
        if (key.Name is null) { error = $"\"{main}\" is not a key I can use. Letters, digits, F1-F24, the number pad, the arrows, Insert, Delete, Home, End, PageUp, PageDown, Pause and a few others work."; return false; }
        if (mods == HotkeyModifiers.None && !key.BareOk)
        {
            error = "A letter, digit or typing key needs Ctrl, Alt, Shift or Win with it, or it would switch modes whenever you typed it. A function key, the number pad or a mouse button can stand alone.";
            return false;
        }
        trigger = ForKey(mods, key.Vk);
        return true;
    }
}

/// <summary>What a shortcut does: the next mode, the previous one, or a mode by name.</summary>
public sealed class ModeShortcut
{
    public const string Next = "Next", Previous = "Previous";

    /// <summary>The key or mouse button, as <see cref="InputTrigger"/> text.</summary>
    public string Trigger { get; set; } = "";

    /// <summary>"Next", "Previous", or the name of a mode of the running profile.</summary>
    public string Action { get; set; } = Next;

    public string Describe() => Action.Equals(Next, StringComparison.OrdinalIgnoreCase) ? $"{Trigger}: next mode"
        : Action.Equals(Previous, StringComparison.OrdinalIgnoreCase) ? $"{Trigger}: previous mode"
        : $"{Trigger}: the mode \"{Action}\"";
}

/// <summary>Checking the list of shortcuts a person has set.</summary>
public static class ModeShortcutList
{
    public const int MaxShortcuts = 20;

    /// <summary>The shortcuts that can be used, with a note for each one that cannot (unreadable, or the same key twice).</summary>
    public static (List<(InputTrigger Trigger, ModeShortcut Shortcut)> Usable, List<string> Problems) Check(IEnumerable<ModeShortcut>? list)
    {
        var usable = new List<(InputTrigger, ModeShortcut)>();
        var problems = new List<string>();
        foreach (var s in list ?? [])
        {
            if (string.IsNullOrWhiteSpace(s.Action)) { problems.Add($"{s.Trigger}: no action."); continue; }
            if (!InputTrigger.TryParse(s.Trigger, out var t, out string? why)) { problems.Add($"{s.Trigger}: {why}"); continue; }
            if (usable.Any(u => u.Item1 == t)) { problems.Add($"{t} is set more than once; only the first is used."); continue; }
            if (usable.Count >= MaxShortcuts) { problems.Add($"{t}: at most {MaxShortcuts} shortcuts are used."); continue; }
            usable.Add((t!, s));
        }
        return (usable, problems);
    }
}

/// <summary>
/// Decides when a key press or mouse button press is one of the shortcuts. It does not touch the system: the app feeds it what it sees. A key that
/// is held down does not fire again (the repeat is ignored), the modifiers held must be exactly those of the shortcut, and Ctrl, Alt, Shift and
/// Win on their own never fire anything.
/// </summary>
public sealed class ModeShortcutMatcher
{
    private readonly List<(InputTrigger Trigger, ModeShortcut Shortcut)> _shortcuts;
    private readonly HashSet<int> _keysDown = [];
    private readonly HashSet<MouseTriggerButton> _mouseDown = [];

    public ModeShortcutMatcher(IEnumerable<ModeShortcut>? shortcuts) => _shortcuts = ModeShortcutList.Check(shortcuts).Usable;

    public bool IsEmpty => _shortcuts.Count == 0;

    /// <summary>A key went down while these modifiers were held. Returns the shortcut it triggers, or null.</summary>
    public ModeShortcut? KeyDown(int virtualKey, HotkeyModifiers modifiersHeld)
    {
        if (InputTrigger.IsModifierKey(virtualKey)) return null;
        if (!_keysDown.Add(virtualKey)) return null;              // still held: a repeat
        return _shortcuts.FirstOrDefault(s => s.Trigger.Kind == TriggerKind.Key && s.Trigger.VirtualKey == virtualKey && s.Trigger.Modifiers == modifiersHeld).Shortcut;
    }

    public void KeyUp(int virtualKey) => _keysDown.Remove(virtualKey);

    /// <summary>A mouse button went down while these modifiers were held. Returns the shortcut it triggers, or null.</summary>
    public ModeShortcut? MouseDown(MouseTriggerButton button, HotkeyModifiers modifiersHeld)
    {
        if (!_mouseDown.Add(button)) return null;
        return _shortcuts.FirstOrDefault(s => s.Trigger.Kind == TriggerKind.Mouse && s.Trigger.Mouse == button && s.Trigger.Modifiers == modifiersHeld).Shortcut;
    }

    public void MouseUp(MouseTriggerButton button) => _mouseDown.Remove(button);

    /// <summary>Forget what is held (for example when the window focus changed and key-up messages may have been missed).</summary>
    public void Reset() { _keysDown.Clear(); _mouseDown.Clear(); }
}

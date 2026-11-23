namespace G923Mapper;

[Flags]
public enum HotkeyModifiers { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }

/// <summary>A global keyboard shortcut such as Ctrl+Alt+F10, kept as text in the settings file.</summary>
public sealed record Hotkey(HotkeyModifiers Modifiers, int VirtualKey, string KeyName)
{
    public static readonly string[] Presets = ["Ctrl+Alt+F10", "Ctrl+Alt+F11", "Ctrl+Alt+F12", "Ctrl+Shift+F10", "Ctrl+Alt+P", "Pause"];
    public const string Default = "Ctrl+Alt+F10";

    private static readonly Dictionary<string, int> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Pause"] = 0x13, ["ScrollLock"] = 0x91, ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Home"] = 0x24, ["End"] = 0x23,
        ["PageUp"] = 0x21, ["PageDown"] = 0x22,
    };

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyName);
        return string.Join("+", parts);
    }

    /// <summary>Parses "Ctrl+Alt+F10", "shift+p", "Pause". Letters, digits, F1-F24 and a few named keys are accepted.</summary>
    public static bool TryParse(string? text, out Hotkey? hotkey, out string? error)
    {
        hotkey = null; error = null;
        if (string.IsNullOrWhiteSpace(text)) { error = "no shortcut given."; return false; }

        var mods = HotkeyModifiers.None;
        string? key = null;
        foreach (string raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= HotkeyModifiers.Ctrl; break;
                case "alt": mods |= HotkeyModifiers.Alt; break;
                case "shift": mods |= HotkeyModifiers.Shift; break;
                case "win" or "windows": mods |= HotkeyModifiers.Win; break;
                default:
                    if (key is not null) { error = $"only one key is allowed, not both \"{key}\" and \"{raw}\"."; return false; }
                    key = raw;
                    break;
            }
        }
        if (key is null) { error = "a shortcut needs a key as well as Ctrl, Alt or Shift."; return false; }

        int vk; string name;
        if (key.Length == 1 && char.IsAsciiLetter(key[0])) { vk = char.ToUpperInvariant(key[0]); name = char.ToUpperInvariant(key[0]).ToString(); }
        else if (key.Length == 1 && char.IsAsciiDigit(key[0])) { vk = key[0]; name = key; }
        else if (key.Length is 2 or 3 && (key[0] is 'F' or 'f') && int.TryParse(key[1..], out int f) && f is >= 1 and <= 24) { vk = 0x70 + f - 1; name = "F" + f; }
        else if (Named.TryGetValue(key, out int named)) { vk = named; name = Named.Keys.First(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)); }
        else { error = $"\"{key}\" is not a key I know. Use a letter, a digit, F1-F24, Pause, ScrollLock, Insert, Delete, Home, End, PageUp or PageDown."; return false; }

        // A bare letter or digit would stop that key working everywhere, so it needs a modifier. F-keys and the named keys may stand alone.
        if (mods == HotkeyModifiers.None && (vk is >= 0x30 and <= 0x5A))
        { error = "a letter or digit needs Ctrl, Alt or Shift with it, or it would stop that key working everywhere."; return false; }

        hotkey = new Hotkey(mods, vk, name);
        return true;
    }
}

/// <summary>
/// Decides whether the controls should be passed on to the game right now, for the "release the controls when the game is not in
/// front" option. It only ever releases for a game that is really running, so choosing a game's profile by hand to try it in
/// joy.cpl, with the game closed, still works.
/// </summary>
public static class FocusGate
{
    /// <param name="enabled">The user's option.</param>
    /// <param name="gameExes">The profile's match.exe list (empty = a profile not tied to any game).</param>
    /// <param name="foregroundExe">Program whose window is in front (name or name.exe), or null if there is none.</param>
    /// <param name="isRunning">Whether a given program is running.</param>
    public static bool Allows(bool enabled, IReadOnlyCollection<string> gameExes, string? foregroundExe, Func<string, bool> isRunning)
    {
        if (!enabled || gameExes.Count == 0) return true;
        if (!gameExes.Any(isRunning)) return true;                      // the game is not running: nothing to release for
        if (string.IsNullOrWhiteSpace(foregroundExe)) return false;     // nothing in front (lock screen, switching): release
        string front = ProfileSelector.Normalize(foregroundExe);
        return gameExes.Any(g => ProfileSelector.Normalize(g) == front);
    }
}

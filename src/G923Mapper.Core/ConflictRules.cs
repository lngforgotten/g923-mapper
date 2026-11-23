namespace G923Mapper;

/// <summary>What is in the folder of a game that is running (its executable, the folder, and the files in it).</summary>
public sealed record GameFolder(string Exe, string Folder, IReadOnlyList<(string Name, long Size)> Files);

/// <summary>
/// Finds other software that fights over the virtual controller: leftovers of x360ce in a game's folder (its xinput DLL replaces the
/// controller system for that game whatever this app does), other programs that create virtual gamepads, and Steam's controller layer.
/// Pure: the caller supplies the process names and the folder listings.
/// </summary>
public static class ConflictRules
{
    /// <summary>x360ce's own DLL is a few megabytes; Microsoft's real xinput DLLs are about 100 KB, so only large ones count.</summary>
    public const long X360ceDllMinBytes = 1_000_000;

    private static readonly string[] XInputDlls = ["xinput1_1.dll", "xinput1_2.dll", "xinput1_3.dll", "xinput1_4.dll", "xinput9_1_0.dll"];

    private static readonly (string Process, string Title, string Detail, HealthLevel Level)[] Programs =
    [
        ("x360ce", "x360ce is running", "x360ce also makes a virtual controller, so a game can see two of them and steering can fight itself. Close it (and remove its files from the game's folder) while using this app.", HealthLevel.Warning),
        ("x360ce_x64", "x360ce is running", "x360ce also makes a virtual controller, so a game can see two of them and steering can fight itself. Close it (and remove its files from the game's folder) while using this app.", HealthLevel.Warning),
        ("ds4windows", "DS4Windows is running", "DS4Windows creates a virtual controller too. Two virtual controllers at once can make a game see double input. Close it, or stop it handling the wheel.", HealthLevel.Warning),
        ("rewasd", "reWASD is running", "reWASD can create virtual gamepads of its own. Make sure it is not mapping the wheel as well, or a game may see two controllers.", HealthLevel.Warning),
        ("rewasdservice", "reWASD is running", "reWASD can create virtual gamepads of its own. Make sure it is not mapping the wheel as well, or a game may see two controllers.", HealthLevel.Warning),
        ("joytokey", "JoyToKey is running", "JoyToKey turns controller input into key presses. If it is set to handle the wheel, a game may get the same input twice.", HealthLevel.Info),
        ("xpadder", "Xpadder is running", "Xpadder remaps controller input. If it handles the wheel, a game may get the same input twice.", HealthLevel.Info),
    ];

    /// <param name="processNames">Names of the running programs, with or without .exe, in any case.</param>
    /// <param name="folders">The folders of the games whose profiles are running.</param>
    public static IReadOnlyList<HealthItem> Evaluate(IEnumerable<string> processNames, IReadOnlyList<GameFolder> folders)
    {
        var running = processNames.Select(ProfileSelector.Normalize).ToHashSet();
        var items = new List<HealthItem>();

        foreach (var (process, title, detail, level) in Programs)
            if (running.Contains(process) && !items.Any(i => i.Title == title))
                items.Add(new HealthItem(title, level, detail));

        foreach (GameFolder g in folders)
        {
            var found = X360ceFiles(g.Files);
            if (found.Count == 0) continue;
            items.Add(new HealthItem($"x360ce files found in {g.Exe}'s folder", HealthLevel.Warning,
                $"{string.Join(", ", found)} in {g.Folder}. These replace the game's controller support, so the game may ignore this app or see a second controller. " +
                "Delete those files from the game's folder (the game's own files are not affected)."));
        }

        // Steam wraps controllers with its own layer ("Steam Input"); it is usually fine, but it is the usual suspect when a game acts oddly.
        if (running.Contains("steam") || running.Contains("steamwebhelper"))
            items.Add(new HealthItem("Steam is running", HealthLevel.Info,
                "Steam can wrap controllers with Steam Input. If a game behaves strangely with the wheel, open the game's properties in Steam, then Controller, and set Steam Input to Disabled for it."));

        return items;
    }

    /// <summary>Names of files that look like an x360ce setup: its settings file, its program, or a large xinput DLL.</summary>
    public static IReadOnlyList<string> X360ceFiles(IEnumerable<(string Name, long Size)> files)
    {
        var found = new List<string>();
        foreach (var (name, size) in files)
        {
            string n = name.ToLowerInvariant();
            if (n is "x360ce.ini" or "x360ce.exe" or "x360ce_x64.exe" or "x360ce.dll" or "x360ce.gdb") found.Add(name);
            else if (XInputDlls.Contains(n) && size >= X360ceDllMinBytes) found.Add(name);
        }
        return found;
    }
}

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace G923Mapper.App;

public sealed class ComboSettings
{
    /// <summary>Off by default.</summary>
    public bool Enabled { get; set; }
    /// <summary>Wheel buttons held together to pause or resume the controller (empty = not set).</summary>
    public System.Collections.Generic.List<string> Pause { get; set; } = new();
    public System.Collections.Generic.List<string> NextProfile { get; set; } = new();
    public System.Collections.Generic.List<string> PreviousProfile { get; set; } = new();
    /// <summary>How long the buttons must be held, so a brush against them while driving does nothing.</summary>
    public int HoldMs { get; set; } = 800;
}

public sealed class HotkeySettings
{
    public bool Enabled { get; set; }
    public string Combo { get; set; } = G923Mapper.Hotkey.Default;
}

/// <summary>
/// Small per-user UI settings, stored next to the profiles: settings.json for the first copy of the app and
/// settings-2.json, settings-3.json... for extra copies (one per wheel).
/// </summary>
public sealed class AppSettings
{
    /// <summary>Which copy of the app these settings belong to (1 = the first). Not stored in the file.</summary>
    [JsonIgnore]
    public int Instance { get; private set; } = 1;

    /// <summary>Profile to run (file name without .json).</summary>
    public string Profile { get; set; } = "default";
    public bool WarnedAboutTray { get; set; }
    /// <summary>The user has been told, once, that Standard safety allows synthetic keyboard and mouse input that anti-cheat may treat differently, and accepted it.</summary>
    public bool WarnedAboutStandard { get; set; }
    /// <summary>The starter genre templates have been written to the profile folder (once; they are never put back).</summary>
    public bool TemplatesSeeded { get; set; }
    /// <summary>
    /// A GitHub repository ("owner/name") whose releases to compare with, when the user presses Check for updates. Empty (the default) means the
    /// app never contacts anything. It is never checked automatically.
    /// </summary>
    public string UpdateRepo { get; set; } = "lngforgotten/g923-mapper";
    /// <summary>The user has finished (or dismissed for good) the "Get started" checklist.</summary>
    public bool SetupDone { get; set; }
    /// <summary>True when there was no settings file yet, so this is the first time the app has run for this user. Not stored.</summary>
    [JsonIgnore]
    public bool IsFirstRun { get; private set; }
    /// <summary>Switch to a profile automatically while one of its games (match.exe) is running.</summary>
    public bool AutoSwitch { get; set; } = true;
    /// <summary>Auto-switch only while the game's window is in front, instead of whenever it is running.</summary>
    public bool AutoSwitchWhenFocused { get; set; }
    /// <summary>System (follow Windows), Light or Dark.</summary>
    public string Theme { get; set; } = "System";
    public ForceSettings Force { get; set; } = new();
    /// <summary>Tag each control of the controller picture with the wheel control that feeds it.</summary>
    public bool ShowPadLabels { get; set; }

    /// <summary>
    /// Give the wheel back to other programs (G HUB, other games) when this app closes, if HidHide is hiding it. HidHide's setting
    /// outlives the app, so without this a crash leaves the wheel hidden. Closing then asks for administrator permission once.
    /// </summary>
    public bool UnhideWheelOnExit { get; set; } = true;

    /// <summary>A global shortcut that pauses and resumes the virtual controller. Off by default.</summary>
    public HotkeySettings Hotkey { get; set; } = new();
    /// <summary>The optional shortcut that moves a profile to its next mode (Shooting, Driving...). Off by default.</summary>
    public HotkeySettings ModeHotkey { get; set; } = new() { Combo = "Ctrl+Alt+F11" };
    /// <summary>Buzz the wheel when the mode changes: once for the first mode, twice for the second and so on.</summary>
    public bool ModeBuzz { get; set; } = true;
    /// <summary>Show a Windows notification when the mode changes.</summary>
    public bool ModeToast { get; set; } = true;
    /// <summary>Which view the pop-out window was last in: Full, Compact, Mode, Controller or Rules.</summary>
    public string MiniView { get; set; } = "Full";
    /// <summary>Look for a new version by itself, quietly, a little after start-up and about once a day. On by default; one tick in Settings turns it off.</summary>
    public bool AutoUpdateCheck { get; set; } = true;
    /// <summary>When a new version is found, download and install it by itself (it takes effect the next time the app starts, or when asked to restart). On by default.</summary>
    public bool AutoInstallUpdates { get; set; } = true;
    /// <summary>When the last automatic check reached GitHub (UTC, ISO text), so it runs about once a day and not on every start.</summary>
    public string LastUpdateCheckUtc { get; set; } = "";
    /// <summary>The version the user told Home to stop showing ("Later"); a newer one shows again.</summary>
    public string UpdateDismissedTag { get; set; } = "";
    /// <summary>The version a Windows notification was already shown for, so there is one notification per version.</summary>
    public string UpdateNotifiedTag { get; set; } = "";

    /// <summary>Wheel-button combinations for pausing and for switching profile. Off by default.</summary>
    public ComboSettings Combos { get; set; } = new();

    /// <summary>Let the wheel's centre follow where it comes to rest, up to 3 degrees. On by default.</summary>
    public bool AutoCentre { get; set; } = true;

    /// <summary>Release every control while a profile's game is running but another window is in front. Off by default.</summary>
    public bool ReleaseWhenGameNotInFront { get; set; }

    /// <summary>
    /// Let more than one copy of the app run at once, each driving its own wheel. Off by default: a second copy then just
    /// brings the first one to the front. Only the first copy's file is consulted for this.
    /// </summary>
    public bool AllowMultipleInstances { get; set; }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "G923Mapper");

    /// <summary>Games with anti-cheat, shared by every copy of the app: keyboard output is never allowed for them.</summary>
    /// <summary>Earlier versions of every profile, one folder per profile.</summary>
    public static string HistoryDir => Path.Combine(AppDataDir, "history");
    /// <summary>Where a downloaded update waits (the unpacked program and a note of its version) until it is swapped in.</summary>
    public static string UpdateDir => Path.Combine(AppDataDir, "update");
    public static string StagedDir => Path.Combine(UpdateDir, "staged");

    public static string AntiCheatPath => Path.Combine(AppDataDir, "anticheat.txt");

    private static string PathFor(int instance) => Path.Combine(AppDataDir, InstancePlan.SettingsFileName(instance));

    public static AppSettings Load(int instance = 1)
    {
        try
        {
            if (File.Exists(PathFor(instance)))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(PathFor(instance)), Options) ?? new AppSettings();
                loaded.Instance = instance;
                return loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new AppSettings { Instance = instance, IsFirstRun = !File.Exists(PathFor(instance)) };
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            File.WriteAllText(PathFor(Instance), JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

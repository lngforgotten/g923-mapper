using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace G923Mapper.App;

public enum ThemeSetting { System, Light, Dark, HighContrast }

/// <summary>
/// Light / Dark / follow-Windows theming. Two layers switch together: WPF's built-in Fluent theme
/// (every native control, popup and title bar) and our own brush dictionary (cards, bars, buttons).
/// </summary>
public static class ThemeManager
{
    private static ResourceDictionary? _palette;

    public static ThemeSetting Setting { get; private set; } = ThemeSetting.System;

    /// <summary>The theme actually in effect (System resolved to Light or Dark).</summary>
    public static bool IsDark { get; private set; }

    /// <summary>Raised after the palette has been swapped, so custom-drawn elements can repaint.</summary>
    public static event Action? Changed;

    /// <summary>Start watching Windows' own light/dark preference; only matters while the setting is System.</summary>
    public static void Initialise()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if ((e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Accessibility) || Setting != ThemeSetting.System) return;
            Application.Current?.Dispatcher.BeginInvoke(() => Apply(ThemeSetting.System));
        };
    }

    public static void Apply(ThemeSetting setting)
    {
        Setting = setting;
        // High contrast is chosen here, or followed from Windows' own high contrast mode while the setting is Auto.
        bool contrast = setting == ThemeSetting.HighContrast || (setting == ThemeSetting.System && SystemParameters.HighContrast);
        bool dark = contrast || setting == ThemeSetting.Dark || (setting == ThemeSetting.System && WindowsPrefersDark());
        Application app = Application.Current;

        app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;

        var palette = new ResourceDictionary { Source = new Uri($"Themes/{(contrast ? "HighContrast" : dark ? "Dark" : "Light")}.xaml", UriKind.Relative) };
        var merged = app.Resources.MergedDictionaries;
        if (_palette is not null) merged.Remove(_palette);
        merged.Insert(0, palette);
        _palette = palette;
        IsDark = dark;
        Changed?.Invoke();
    }

    /// <summary>Next setting in the cycle System, Light, Dark.</summary>
    public static ThemeSetting Next(ThemeSetting s) => s switch
    {
        ThemeSetting.System => ThemeSetting.Light,
        ThemeSetting.Light => ThemeSetting.Dark,
        ThemeSetting.Dark => ThemeSetting.HighContrast,
        _ => ThemeSetting.System,
    };

    public static bool TryParse(string? text, out ThemeSetting setting) => Enum.TryParse(text, true, out setting) && Enum.IsDefined(setting);

    /// <summary>Windows' "choose your default app mode": 0 means dark. Missing or unreadable means light.</summary>
    public static bool WindowsPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
    }

    /// <summary>A brush from the current palette. Looked up on each use, so it is always the current theme's.</summary>
    public static Brush Brush(string key) =>
        Application.Current.TryFindResource(key) as Brush ?? System.Windows.Media.Brushes.Magenta;   // magenta = a missing key, easy to spot

    public static Brush Success => Brush("Success");
    public static Brush Warn => Brush("Warn");
    public static Brush Danger => Brush("Danger");
    public static Brush Neutral => Brush("Neutral");
    public static Brush Muted => Brush("TextMuted");
}

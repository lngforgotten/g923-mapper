using Microsoft.Win32;

namespace G923Mapper;

/// <summary>Start with Windows: one value under the current user's Run key. No administrator rights needed.</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string DefaultName = "G923Mapper";

    /// <summary>Starts hidden in the tray, so logging in does not throw a window at you.</summary>
    public static string Command(string exePath) => $"\"{exePath}\" --minimized";

    public static bool IsEnabled(string valueName = DefaultName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(valueName) is string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }

    /// <summary>The command currently registered, or null.</summary>
    public static string? CurrentCommand(string valueName = DefaultName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(valueName) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    public static void Set(bool enabled, string exePath, string valueName = DefaultName)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(valueName, Command(exePath));
        else key.DeleteValue(valueName, throwOnMissingValue: false);
    }
}

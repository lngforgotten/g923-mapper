using System.Text.RegularExpressions;

namespace G923Mapper;

public static class ExtraDeviceNaming
{
    private static readonly Regex Valid = new("^[A-Za-z][A-Za-z0-9_]{0,23}$", RegexOptions.Compiled);

    /// <summary>
    /// Why this name cannot be used for an extra device (null if it can). It becomes the prefix of its inputs
    /// in profiles, so it must be a short single word, and must not clash with another device.
    /// </summary>
    public static string? Validate(string name, DeviceRegistry registry, string? existingName = null)
    {
        if (!Valid.IsMatch(name)) return "Use a short single word: letters, digits and underscores, starting with a letter (for example handbrake).";
        if (registry.TryGet(name, out _) && !string.Equals(name, existingName, StringComparison.OrdinalIgnoreCase))
            return $"\"{name}\" is already the name of a device.";
        return null;
    }

    /// <summary>Axis names inside an extra device: single words too, so "handbrake.pull" stays unambiguous.</summary>
    public static string? ValidateInputName(string name) =>
        Valid.IsMatch(name) ? null : "Use a single word: letters, digits and underscores, starting with a letter.";
}

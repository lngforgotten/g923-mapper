using System.Text.Json;
using System.Text.RegularExpressions;

namespace G923Mapper;

/// <param name="Gaming">HidHide's own flag for "this interface is a game controller". A wheel has several HID interfaces;
/// only one is the gaming one, the rest carry vendor control and force-feedback traffic.</param>
public sealed record HidDeviceInfo(string FriendlyName, string InstancePath, bool Gaming = false);

/// <summary>
/// Understands the output of HidHide's command-line tool and builds the commands that hide a wheel from
/// games. Pure text handling, so it is unit-tested without HidHide installed. The exact command and
/// output formats follow HidHide's documentation; see the notes in README about what is untested.
/// </summary>
public static class HidHideText
{
    private static readonly Regex HidPath = new(@"HID\\VID_[0-9A-Fa-f]{4}&PID_[0-9A-Fa-f]{4}[^""\r\n,\]\}]*", RegexOptions.Compiled);

    /// <summary>
    /// Devices from `--dev-all` / `--dev-gaia`, which print JSON (a list of groups, each with devices that have a
    /// deviceInstancePath). If the output is not JSON we fall back to picking HID instance paths out of the text.
    /// </summary>
    public static IReadOnlyList<HidDeviceInfo> ParseDevices(string output)
    {
        var result = new List<HidDeviceInfo>();
        if (string.IsNullOrWhiteSpace(output)) return result;

        try
        {
            using var doc = JsonDocument.Parse(output, new JsonDocumentOptions { AllowTrailingCommas = true });
            Walk(doc.RootElement, "", result);
            if (result.Count > 0) return Dedupe(result);
        }
        catch (JsonException) { /* not JSON: fall through to the text scan */ }

        foreach (Match m in HidPath.Matches(output))
            result.Add(new HidDeviceInfo("", Unescape(m.Value.Trim())));
        return Dedupe(result);
    }

    private static void Walk(JsonElement e, string friendly, List<HidDeviceInfo> into)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray()) Walk(item, friendly, into);
                break;
            case JsonValueKind.Object:
                string name = e.TryGetProperty("friendlyName", out var fn) && fn.ValueKind == JsonValueKind.String ? fn.GetString() ?? friendly : friendly;
                if (e.TryGetProperty("deviceInstancePath", out var path) && path.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(path.GetString()))
                {
                    bool gaming = e.TryGetProperty("gamingDevice", out var g) && g.ValueKind == JsonValueKind.True;
                    into.Add(new HidDeviceInfo(name, path.GetString()!, gaming));
                }
                foreach (var prop in e.EnumerateObject())
                    if (prop.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object) Walk(prop.Value, name, into);
                break;
        }
    }

    private static IReadOnlyList<HidDeviceInfo> Dedupe(List<HidDeviceInfo> list) =>
        list.GroupBy(d => d.InstancePath, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

    private static string Unescape(string s) => s.Replace(@"\\", @"\");

    /// <summary>Paths from `--dev-list` (the currently hidden devices): one per line, or JSON strings.</summary>
    public static IReadOnlyList<string> ParseHiddenList(string output)
    {
        var paths = new List<string>();
        if (string.IsNullOrWhiteSpace(output)) return paths;
        foreach (Match m in HidPath.Matches(output)) paths.Add(Unescape(m.Value.Trim()));
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The wheel's HID interfaces (a wheel usually has several, MI_00, MI_01...). Matches on USB ids.</summary>
    public static IReadOnlyList<HidDeviceInfo> FindDevices(IEnumerable<HidDeviceInfo> devices, int vid, int pid)
    {
        string needle = $"VID_{vid:X4}&PID_{pid:X4}";
        return devices.Where(d => d.InstancePath.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// What to hide so games stop seeing the wheel: just its gaming interface when HidHide flags one. The other
    /// interfaces carry Logitech's control and force-feedback traffic, and hiding them would cut off G HUB.
    /// Falls back to every interface when none is flagged (for example when only plain text was available).
    /// </summary>
    public static IReadOnlyList<HidDeviceInfo> FindGaming(IEnumerable<HidDeviceInfo> devices, int vid, int pid)
    {
        var all = FindDevices(devices, vid, pid);
        var gaming = all.Where(d => d.Gaming).ToList();
        return gaming.Count > 0 ? gaming : all;
    }

    public static bool IsHidden(IEnumerable<string> hiddenPaths, int vid, int pid)
    {
        string needle = $"VID_{vid:X4}&PID_{pid:X4}";
        return hiddenPaths.Any(p => p.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}

public static class HidHideCommands
{
    /// <summary>
    /// The CLI invocations that hide a wheel: let this app see hidden devices, hide each interface of the wheel,
    /// then switch hiding on. Each entry is the argument string for one HidHideCLI run.
    /// </summary>
    public static IReadOnlyList<string> Hide(IEnumerable<string> instancePaths, string appPath)
    {
        var cmds = new List<string> { $"--app-reg \"{appPath}\"" };
        cmds.AddRange(instancePaths.Select(p => $"--dev-hide \"{p}\""));
        cmds.Add("--cloak-on");
        return cmds;
    }

    /// <summary>The CLI invocations that make the wheel visible to games again.</summary>
    public static IReadOnlyList<string> Unhide(IEnumerable<string> instancePaths, string appPath)
    {
        var cmds = instancePaths.Select(p => $"--dev-unhide \"{p}\"").ToList();
        cmds.Add($"--app-unreg \"{appPath}\"");
        return cmds;
    }

    /// <summary>
    /// One `cmd.exe` argument string that runs all the commands in sequence, so the user gets a single
    /// administrator prompt. Stops at the first failure.
    /// </summary>
    public static string Batch(string cliPath, IEnumerable<string> commands) =>
        "/c \"" + string.Join(" && ", commands.Select(c => $"\"{cliPath}\" {c}")) + "\"";
}

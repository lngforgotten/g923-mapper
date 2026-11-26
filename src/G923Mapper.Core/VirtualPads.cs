using System.Text.RegularExpressions;

namespace G923Mapper;

/// <summary>One Xbox 360 controller entry that Windows knows about (a live one, or a leftover from an earlier session).</summary>
public sealed record VirtualPadInfo(string InstanceId, string Description, string Status)
{
    /// <summary>Plugged in right now. Anything else ("Disconnected") is a leftover entry in Device Manager.</summary>
    public bool Connected => Status.Equals("Started", StringComparison.OrdinalIgnoreCase)
                             || Status.Equals("Running", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Reads Windows' list of Xbox 360 controllers (from <c>pnputil /enum-devices</c>) and builds the command that removes leftovers.</summary>
public static partial class VirtualPadText
{
    /// <summary>
    /// A controller made by ViGEmBus has a short numeric serial ("USB\VID_045E&amp;PID_028E\01"); a real wired Xbox 360 controller has a
    /// location id with "&amp;" in it. Only the first kind is ever treated as virtual, so a real controller is never touched.
    /// </summary>
    public static bool IsVirtual(string instanceId) => VirtualId().IsMatch(instanceId);

    [GeneratedRegex(@"^USB\\VID_045E&PID_028E\\\d{1,4}$", RegexOptions.IgnoreCase)]
    private static partial Regex VirtualId();

    /// <summary>The controllers in the output, in the order listed. Text that is not in the expected (English) format gives none.</summary>
    public static IReadOnlyList<VirtualPadInfo> Parse(string output)
    {
        var list = new List<VirtualPadInfo>();
        string? id = null, desc = "", status = "";

        void Flush()
        {
            if (id is not null && IsVirtual(id)) list.Add(new VirtualPadInfo(id, desc ?? "", status ?? ""));
            id = null; desc = ""; status = "";
        }

        foreach (string raw in output.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) { Flush(); continue; }
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            string key = line[..colon].Trim(), value = line[(colon + 1)..].Trim();
            if (key.Equals("Instance ID", StringComparison.OrdinalIgnoreCase)) { Flush(); id = value; }
            else if (key.Equals("Device Description", StringComparison.OrdinalIgnoreCase)) desc = value;
            else if (key.Equals("Status", StringComparison.OrdinalIgnoreCase)) status = value;
        }
        Flush();
        return list;
    }

    public static IReadOnlyList<VirtualPadInfo> Leftovers(IEnumerable<VirtualPadInfo> pads) => pads.Where(p => !p.Connected).ToList();

    /// <summary>Those of <paramref name="instanceIds"/> that are still unplugged entries, so a controller plugged in since is never removed.</summary>
    public static IReadOnlyList<string> StillLeftover(IEnumerable<string> instanceIds, IEnumerable<VirtualPadInfo> now)
    {
        var left = new HashSet<string>(Leftovers(now).Select(p => p.InstanceId), StringComparer.OrdinalIgnoreCase);
        return instanceIds.Where(left.Contains).ToList();
    }

    /// <summary>
    /// One <c>cmd.exe</c> argument string that removes each device and its children, so the user gets one administrator prompt.
    /// A failure on one does not stop the others. Refuses (returns null) if given nothing, or anything that is not a virtual controller.
    /// </summary>
    public static string? RemoveBatch(IEnumerable<string> instanceIds)
    {
        var ids = instanceIds.ToList();
        if (ids.Count == 0 || ids.Any(i => !IsVirtual(i))) return null;
        return "/c \"" + string.Join(" & ", ids.Select(i => $"pnputil /remove-device \"{i}\" /subtree")) + "\"";
    }
}

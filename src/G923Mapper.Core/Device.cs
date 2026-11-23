using System.Text.Json;
using System.Text.Json.Serialization;

namespace G923Mapper;

/// <summary>One raw reading from the wheel, in the order the DirectInput joystick state exposes it.</summary>
public sealed class RawInput
{
    /// <summary>X, Y, Z, RX, RY, RZ, Slider0, Slider1, each 0..65535.</summary>
    public int[] Axes { get; init; } = new int[8];
    /// <summary>Buttons, index 0 = button 1.</summary>
    public bool[] Buttons { get; init; } = Array.Empty<bool>();
    /// <summary>How many buttons the device really has (DirectInput always hands back 128 slots). 0 = unknown.</summary>
    public int ButtonCount { get; init; }
    /// <summary>Hat in hundredths of a degree; -1 = neutral.</summary>
    public int Pov { get; init; } = -1;
    /// <summary>
    /// False until the device has sent a real report. A freshly acquired G923 reports 32768 on every
    /// axis until something moves, which would look like half-pressed pedals.
    /// </summary>
    public bool Live { get; init; } = true;

    /// <summary>
    /// A freshly acquired DirectInput device reports 0 or 32768 on every axis until its first real
    /// report. A genuine reading essentially never has all eight axes at exactly those two values
    /// (G923 pedals rest at 65535); and if one did, treating it as neutral would still be right.
    /// </summary>
    public static bool LooksStale(IReadOnlyList<int> axes) => axes.All(a => a == 0 || a == 32768);
}

public sealed class AxisDef
{
    /// <summary>X, Y, Z, RX, RY, RZ, Slider0 or Slider1.</summary>
    public string Source { get; set; } = "X";
    /// <summary>Signed axes (the wheel) normalise to -1..1 around <see cref="Centre"/>; others to 0..1.</summary>
    public bool Signed { get; set; }
    /// <summary>
    /// Pedal reads high at rest and low when pressed (the G923 does). For a signed axis: turning
    /// left makes the raw value rise, so the sign is flipped to keep left negative.
    /// </summary>
    public bool Invert { get; set; }
    public int Min { get; set; }
    public int Max { get; set; } = 65535;
    public int Centre { get; set; } = 32768;

    [JsonIgnore]
    public int SourceIndex => Array.FindIndex(SourceNames, n => string.Equals(n, Source, StringComparison.OrdinalIgnoreCase));

    public static readonly string[] SourceNames = ["X", "Y", "Z", "RX", "RY", "RZ", "Slider0", "Slider1"];
}

/// <summary>Describes how a particular wheel model's raw inputs map to the named inputs used in profiles.</summary>
public sealed class DeviceMap
{
    public const int MaxButtons = 32;
    public static readonly string[] HatNames = ["dpadUp", "dpadDown", "dpadLeft", "dpadRight"];

    public string Name { get; set; } = "";
    /// <summary>USB vendor ID as hex text ("046D"), used to find this wheel. Empty = unknown.</summary>
    public string Vid { get; set; } = "";
    /// <summary>USB product ID as hex text ("C26E").</summary>
    public string Pid { get; set; } = "";
    /// <summary>
    /// An additional device (handbrake, shifter, pedals) that feeds the same virtual pad. Its inputs are
    /// used in profiles as "name.input", for example handbrake.pull.
    /// </summary>
    public bool Extra { get; set; }
    /// <summary>Total lock-to-lock rotation in degrees (G923: 900).</summary>
    public double RotationDegrees { get; set; } = 900;
    public Dictionary<string, AxisDef> Axes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Friendly names for buttons, name to 1-based button number. Every button is also reachable as buttonN.</summary>
    public Dictionary<string, int> Buttons { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsAxis(string name) => Axes.ContainsKey(name);
    public bool IsSigned(string name) => Axes.TryGetValue(name, out var a) && a.Signed;

    public bool IsKnownInput(string name)
    {
        if (Axes.ContainsKey(name) || Buttons.ContainsKey(name)) return true;
        if (HatNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
        return TryParseButtonN(name, out _);
    }

    public static bool TryParseButtonN(string name, out int number)
    {
        number = 0;
        return name.StartsWith("button", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(6), out number)
            && number >= 1 && number <= MaxButtons;
    }

    /// <summary>Raw reading to named inputs. Axes are normalised, buttons are 0/1.</summary>
    /// <param name="prefix">Prepended to every input name, for extra devices ("handbrake.").</param>
    public InputFrame Normalize(RawInput raw, string prefix = "")
    {
        var frame = new InputFrame();
        foreach (var (name, def) in Axes)
        {
            int idx = def.SourceIndex;
            int v = idx >= 0 && idx < raw.Axes.Length ? raw.Axes[idx] : 0;
            // Until the device is live every axis holds a meaningless 32768: report neutral, not that value.
            frame.Values[prefix + name] = !raw.Live ? 0 : def.Signed ? NormalizeSigned(v, def) : NormalizeUnsigned(v, def);
        }

        foreach (var (name, number) in Buttons)
            frame.Values[prefix + name] = number >= 1 && number <= raw.Buttons.Length && raw.Buttons[number - 1] ? 1 : 0;
        for (int i = 0; i < raw.Buttons.Length; i++)
            frame.Values[prefix + "button" + (i + 1)] = raw.Buttons[i] ? 1 : 0;

        var (up, down, left, right) = Hat(raw.Pov);
        frame.Values[prefix + "dpadUp"] = up ? 1 : 0;
        frame.Values[prefix + "dpadDown"] = down ? 1 : 0;
        frame.Values[prefix + "dpadLeft"] = left ? 1 : 0;
        frame.Values[prefix + "dpadRight"] = right ? 1 : 0;
        return frame;
    }

    /// <summary>Scaled separately each side of the centre so full lock reaches +-1 both ways.</summary>
    public static double NormalizeSigned(int raw, AxisDef d)
    {
        raw = Math.Clamp(raw, d.Min, d.Max);
        double v;
        if (raw >= d.Centre)
            v = d.Max == d.Centre ? 0 : (raw - d.Centre) / (double)(d.Max - d.Centre);
        else
            v = d.Centre == d.Min ? 0 : (raw - d.Centre) / (double)(d.Centre - d.Min);
        return d.Invert ? -v : v;
    }

    /// <summary>The USB ids as numbers, or null when this map has none.</summary>
    [JsonIgnore]
    public (int vid, int pid)? UsbIds =>
        int.TryParse(Vid, System.Globalization.NumberStyles.HexNumber, null, out int v)
        && int.TryParse(Pid, System.Globalization.NumberStyles.HexNumber, null, out int p) ? (v, p) : null;

    public static double NormalizeUnsigned(int raw, AxisDef d)
    {
        raw = Math.Clamp(raw, d.Min, d.Max);
        double span = d.Max - d.Min;
        if (span <= 0) return 0;
        double t = (raw - d.Min) / span;
        return d.Invert ? 1 - t : t;
    }

    /// <summary>DirectInput hat (hundredths of a degree, -1 = neutral) to four directions; diagonals set two.</summary>
    public static (bool up, bool down, bool left, bool right) Hat(int pov)
    {
        if (pov < 0 || pov > 35999) return (false, false, false, false);
        int deg = pov / 100;
        bool up = deg >= 315 || deg <= 45;
        bool right = deg >= 45 && deg <= 135;
        bool down = deg >= 135 && deg <= 225;
        bool left = deg >= 225 && deg <= 315;
        return (up, down, left, right);
    }
}

/// <summary>Named, normalised inputs for one frame.</summary>
public sealed class InputFrame
{
    public Dictionary<string, double> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public double Get(string name) => Values.TryGetValue(name, out var v) ? v : 0;
}

public sealed class DeviceRegistry
{
    private readonly Dictionary<string, DeviceMap> _devices = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> Names => _devices.Keys;
    public bool TryGet(string name, out DeviceMap map) => _devices.TryGetValue(name, out map!);

    /// <summary>
    /// The profile device name meaning "whichever wheel is connected": the calibrated entry for its USB ids. Works for the Xbox and the
    /// PlayStation edition alike, so one profile serves every wheel.
    /// </summary>
    public const string AutoDevice = "auto";

    public static bool IsAuto(string? name) => string.Equals(name?.Trim(), AutoDevice, StringComparison.OrdinalIgnoreCase);

    /// <summary>The layout used when no calibrated entry matches the connected wheel (the built-in Xbox edition, else the first primary device).</summary>
    public DeviceMap? Fallback => _devices.TryGetValue("g923_xbox", out var x) ? x : _devices.Values.FirstOrDefault(d => !d.Extra);

    /// <summary>Whether a primary device map exists for these USB ids (that is, this wheel has been calibrated or is built in).</summary>
    public bool IsKnownWheel(int vid, int pid) => FindByUsb(vid, pid) is not null;

    /// <summary>
    /// The device map a profile with this <c>device</c> value should use. A named device is looked up as is; "auto" picks the entry
    /// registered for the connected wheel's USB ids, falling back to <see cref="Fallback"/> (and with no wheel yet, the fallback).
    /// Null when a named device does not exist.
    /// </summary>
    public DeviceMap? Resolve(string? name, (int Vid, int Pid)? wheel)
    {
        if (IsAuto(name))
            return (wheel is { } w ? FindByUsb(w.Vid, w.Pid) : null) ?? Fallback;
        return name is not null && _devices.TryGetValue(name.Trim(), out var map) ? map : null;
    }

    /// <summary>The additional (extra) devices that have been set up.</summary>
    public IEnumerable<DeviceMap> ExtraDevices => _devices.Values.Where(d => d.Extra);

    public void Remove(string name) => _devices.Remove(name);

    /// <summary>
    /// Whether <paramref name="name"/> is an input a profile for <paramref name="primary"/> may use: one of the wheel's own,
    /// or "device.input" for a configured extra device.
    /// </summary>
    public bool IsKnownInput(DeviceMap primary, string name)
    {
        if (TrySplitExtra(name, out DeviceMap? extra, out string? inner)) return extra!.IsKnownInput(inner!);
        return primary.IsKnownInput(name);
    }

    /// <summary>Signed (-1..1, centred) inputs are the wheel; pedals, buttons and most extra axes are 0..1.</summary>
    public bool IsSigned(DeviceMap primary, string name)
    {
        if (TrySplitExtra(name, out DeviceMap? extra, out string? inner)) return extra!.IsSigned(inner!);
        return primary.IsSigned(name);
    }

    private bool TrySplitExtra(string name, out DeviceMap? extra, out string? inner)
    {
        extra = null; inner = null;
        int dot = name.IndexOf('.');
        if (dot <= 0 || dot == name.Length - 1) return false;
        if (!_devices.TryGetValue(name[..dot], out var map) || !map.Extra) return false;
        extra = map; inner = name[(dot + 1)..];
        return true;
    }
    public void Add(DeviceMap map) => _devices[map.Name] = map;

    /// <summary>
    /// Built-in devices. Only the Xbox edition is included because it is the one that has been
    /// observed on real hardware (VID 046D / PID C26E); the PlayStation edition is left to devices.json.
    /// </summary>
    public static DeviceRegistry CreateDefault()
    {
        var reg = new DeviceRegistry();
        reg.Add(G923Xbox());
        return reg;
    }

    public IEnumerable<DeviceMap> All => _devices.Values;

    /// <summary>The device map registered for these USB ids, if any.</summary>
    /// <summary>The primary device map for these USB ids (extra devices are looked up separately).</summary>
    public DeviceMap? FindByUsb(int vid, int pid, bool includeExtra = false) =>
        _devices.Values.FirstOrDefault(d => (includeExtra || !d.Extra) && d.UsbIds is var ids && ids == (vid, pid));

    public static DeviceMap G923Xbox() => new()
    {
        Name = "g923_xbox",
        Vid = "046D",
        Pid = "C26E",   // observed on the Xbox/PC edition
        RotationDegrees = 900,
        Axes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["wheel"] = new AxisDef { Source = "X", Signed = true },
            ["throttle"] = new AxisDef { Source = "Y", Invert = true },
            ["brake"] = new AxisDef { Source = "RZ", Invert = true },
            ["clutch"] = new AxisDef { Source = "Slider0", Invert = true },
        },
        // Observed: buttons 5 and 6 are the paddles (which is which is unconfirmed on the hardware).
        Buttons = new(StringComparer.OrdinalIgnoreCase) { ["paddleL"] = 5, ["paddleR"] = 6 },
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    /// <summary>
    /// Add or replace one device in a devices.json file, keeping every other entry. The result is the
    /// entry to send back from a different wheel edition.
    /// </summary>
    public static void SaveEntry(string path, DeviceMap map)
    {
        var entries = new Dictionary<string, DeviceMap>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(path))
            entries = JsonSerializer.Deserialize<Dictionary<string, DeviceMap>>(File.ReadAllText(path), JsonOptions)
                      ?? entries;
        entries = new(entries, StringComparer.OrdinalIgnoreCase) { [map.Name] = map };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(entries, WriteOptions));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Remove one device from a devices.json file, leaving the others. A missing file or entry is fine.</summary>
    public static void RemoveEntry(string path, string name)
    {
        if (!File.Exists(path)) return;
        var entries = JsonSerializer.Deserialize<Dictionary<string, DeviceMap>>(File.ReadAllText(path), JsonOptions);
        if (entries is null) return;
        var kept = entries.Where(e => !string.Equals(e.Key, name, StringComparison.OrdinalIgnoreCase))
                          .ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(kept, WriteOptions));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>One device as a devices.json fragment, for pasting or sending back.</summary>
    public static string ToJson(DeviceMap map) =>
        JsonSerializer.Serialize(new Dictionary<string, DeviceMap> { [map.Name] = map }, WriteOptions);

    private static readonly JsonSerializerOptions WriteOptions = new(JsonOptions)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,   // keep input names (paddleL) exactly as typed
    };

    /// <summary>Merge a devices.json (name to DeviceMap) over the built-ins. A missing file is fine.</summary>
    public void LoadFile(string path)
    {
        if (!File.Exists(path)) return;
        var loaded = JsonSerializer.Deserialize<Dictionary<string, DeviceMap>>(File.ReadAllText(path), JsonOptions);
        if (loaded is null) return;
        foreach (var (name, map) in loaded)
        {
            map.Name = name;
            map.Axes = new(map.Axes, StringComparer.OrdinalIgnoreCase);
            map.Buttons = new(map.Buttons, StringComparer.OrdinalIgnoreCase);
            Add(map);
        }
    }
}

using System.Text;
using System.Text.Json;

namespace G923Mapper;

/// <summary>Which edition of the wheel: the Xbox/PC one and the PlayStation one report different USB ids and button layouts.</summary>
public enum WheelEdition { Unknown, Xbox, PlayStation }

public static class WheelEditions
{
    /// <summary>The edition for USB ids: 046D:C26E is the Xbox/PC G923, 046D:C266 and C267 are the PlayStation ones. Anything else is unknown.</summary>
    public static WheelEdition Of(int vid, int pid) => vid == 0x046D
        ? pid switch { 0xC26E => WheelEdition.Xbox, 0xC266 or 0xC267 => WheelEdition.PlayStation, _ => WheelEdition.Unknown }
        : WheelEdition.Unknown;

    public static WheelEdition Of(DeviceMap map) => map.UsbIds is { } ids ? Of(ids.vid, ids.pid) : WheelEdition.Unknown;

    public static string Label(WheelEdition e) => e switch
    {
        WheelEdition.Xbox => "Xbox / PC edition",
        WheelEdition.PlayStation => "PlayStation edition",
        _ => "unknown edition",
    };
}

/// <summary>What reading a pasted profile found. Nothing is saved until the user confirms.</summary>
public sealed class ShareImport
{
    public bool Ok => Profile is not null && Errors.Count == 0;
    public Profile? Profile { get; init; }
    public DeviceMap? Calibration { get; init; }
    public WheelEdition Edition { get; init; }
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    /// <summary>The profile asked for things that were switched off on the way in (keyboard and mouse output, turbo, hiding the wheel).</summary>
    public List<string> TurnedOff { get; } = new();

    /// <summary>The words for the confirmation window: name, number of rules and what kinds of output it uses.</summary>
    public string Summary()
    {
        if (Profile is null) return "";
        var outputs = new List<string> { PadKinds.Of(Profile) == PadKind.DualShock4 ? "DualShock 4 controller" : "Xbox 360 controller" };
        if (Profile.Outputs.SecondPad) outputs.Add("second controller");
        int keys = Profile.Rules.Concat(Profile.Modes.SelectMany(m => m.Rules)).Count(r => Target.TryParse(r.To, out Target? t, out _) && t is { Kind: TargetKind.Key, Pad: 1 });
        if (keys > 0) outputs.Add($"{keys} key or mouse rule{(keys == 1 ? "" : "s")} (switched off)");
        var sb = new StringBuilder();
        sb.Append($"\"{Profile.Name}\": {Profile.Rules.Count} rule{(Profile.Rules.Count == 1 ? "" : "s")}");
        if (Profile.Modes.Count > 0) sb.Append($", {Profile.Modes.Count} mode{(Profile.Modes.Count == 1 ? "" : "s")} ({string.Join(", ", Profile.Modes.Select(m => m.Name))})");
        sb.Append($"; output: {string.Join(", ", outputs)}");
        if (Profile.Match.Exe.Count > 0) sb.Append($"; for {string.Join(", ", Profile.Match.Exe)}");
        return sb.ToString();
    }
}

/// <summary>
/// Copy and paste profile sharing: a profile (and optionally the wheel calibration it was made with) as one block of text that survives chat apps.
/// Reading it is strictly data-only: size is capped, nothing in it is ever run, unknown fields are ignored with a note, and whatever it asks for
/// that could affect other programs (keys and mouse, turbo, hiding the wheel) is switched off on the way in, for the user to enable themselves.
/// </summary>
public static class ProfileShare
{
    /// <summary>The start of the text form, so a pasted block can be recognised.</summary>
    public const string Prefix = "g923profile:";
    public const int FormatVersion = 1;
    /// <summary>The largest pasted text accepted, in characters. A real profile is a few kilobytes.</summary>
    public const int MaxChars = 100_000;
    private const int MaxDepth = 16;

    // ---------------------------------------------------------------- export

    /// <summary>
    /// The profile (with <paramref name="calibration"/>, the map of the wheel it was made for, when given) as text. With
    /// <paramref name="encode"/> it is base64 behind <see cref="Prefix"/>, which no chat app will reflow or "fix"; without it, plain JSON.
    /// </summary>
    public static string Export(Profile profile, DeviceMap? calibration, bool encode = true)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"g923mapper\": {FormatVersion},\n");
        if (calibration is not null)
            sb.Append($"  \"edition\": {JsonSerializer.Serialize(WheelEditions.Of(calibration).ToString())},\n");
        profile = Templates.Detach(profile);   // a profile based on a template is shared as the finished profile, so it needs nothing else to work
        sb.Append("  \"profile\": ").Append(Indent(ProfileWriter.ToJson(profile).TrimEnd())).Append(calibration is null ? "\n" : ",\n");
        if (calibration is not null) sb.Append("  \"calibration\": ").Append(Indent(DeviceRegistry.ToJsonObject(calibration))).Append('\n');
        sb.Append("}\n");
        string json = sb.ToString();
        return encode ? Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json)) : json;
    }

    private static string Indent(string json) => json.Replace("\r\n", "\n").Replace("\n", "\n  ");

    // ---------------------------------------------------------------- import

    private static readonly HashSet<string> ProfileFields = new(StringComparer.OrdinalIgnoreCase)
        { "name", "notes", "istemplate", "match", "device", "safety", "outputs", "force", "hidewheel", "rules", "modes" };
    private static readonly HashSet<string> ModeFields = new(StringComparer.OrdinalIgnoreCase) { "name", "force", "removedrules", "rules" };
    private static readonly HashSet<string> RuleFields = new(StringComparer.OrdinalIgnoreCase)
        { "from", "to", "curve", "exponent", "points", "deadzone", "antideadzone", "invert", "threshold", "mode", "pulsems", "range", "part", "smooth", "expr", "turbo", "turbohz" };
    private static readonly HashSet<string> OutputFields = new(StringComparer.OrdinalIgnoreCase) { "keyboard", "secondpad", "pad", "turbo" };
    private static readonly HashSet<string> ForceFields = new(StringComparer.OrdinalIgnoreCase) { "rumble", "spring", "friction", "motors" };
    private static readonly HashSet<string> MatchFields = new(StringComparer.OrdinalIgnoreCase) { "exe" };
    private static readonly HashSet<string> TopFields = new(StringComparer.OrdinalIgnoreCase) { "g923mapper", "edition", "profile", "calibration" };

    /// <summary>Read pasted text: a block from <see cref="Export"/>, plain JSON of the same, or a bare profile file.</summary>
    public static ShareImport Import(string? text, DeviceRegistry devices)
    {
        string raw = (text ?? "").Trim();
        if (raw.Length == 0) return Failed("There is nothing to import: the clipboard is empty.");
        if (raw.Length > MaxChars) return Failed($"That is too big to be a profile ({raw.Length:N0} characters; the limit is {MaxChars:N0}).");

        string json = raw;
        if (raw.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            string b64 = new string(raw[Prefix.Length..].Where(c => !char.IsWhiteSpace(c)).ToArray());
            try { json = Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
            catch (FormatException) { return Failed("That looks like a copied profile but part of it is missing or changed. Copy it again in full."); }
        }
        else if (!raw.StartsWith('{')) return Failed("That is not a copied G923 Mapper profile.");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxDepth, CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException ex) { return Failed("That is not valid profile text: " + ex.Message); }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Failed("That is not a profile.");

            bool bundle = root.TryGetProperty("profile", out JsonElement profileEl) && profileEl.ValueKind == JsonValueKind.Object;
            if (!bundle) profileEl = root;   // a bare profile file
            if (bundle && root.TryGetProperty("g923mapper", out JsonElement ver) && ver.ValueKind == JsonValueKind.Number && ver.TryGetInt32(out int v) && v > FormatVersion)
                return Failed("That was made by a newer version of G923 Mapper. Update the app and try again.");

            var notes = new List<string>();
            if (bundle) NoteUnknown(root, TopFields, "", notes);
            NoteUnknown(profileEl, ProfileFields, "", notes);
            if (profileEl.TryGetProperty("outputs", out var o) && o.ValueKind == JsonValueKind.Object) NoteUnknown(o, OutputFields, "outputs.", notes);
            if (profileEl.TryGetProperty("force", out var f) && f.ValueKind == JsonValueKind.Object) NoteUnknown(f, ForceFields, "force.", notes);
            if (profileEl.TryGetProperty("match", out var m) && m.ValueKind == JsonValueKind.Object) NoteUnknown(m, MatchFields, "match.", notes);
            if (profileEl.TryGetProperty("rules", out var rs) && rs.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (JsonElement r in rs.EnumerateArray()) { i++; if (r.ValueKind == JsonValueKind.Object) NoteUnknown(r, RuleFields, $"rule {i}.", notes); }
            }
            if (profileEl.TryGetProperty("modes", out var ms) && ms.ValueKind == JsonValueKind.Array)
            {
                int mi = 0;
                foreach (JsonElement mode in ms.EnumerateArray())
                {
                    mi++;
                    if (mode.ValueKind != JsonValueKind.Object) continue;
                    NoteUnknown(mode, ModeFields, $"mode {mi}.", notes);
                    if (mode.TryGetProperty("force", out var mf) && mf.ValueKind == JsonValueKind.Object) NoteUnknown(mf, ForceFields, $"mode {mi}.force.", notes);
                    if (mode.TryGetProperty("rules", out var mr) && mr.ValueKind == JsonValueKind.Array)
                    {
                        int ri = 0;
                        foreach (JsonElement r in mr.EnumerateArray()) { ri++; if (r.ValueKind == JsonValueKind.Object) NoteUnknown(r, RuleFields, $"mode {mi} rule {ri}.", notes); }
                    }
                }
            }

            // The calibration comes first, because the profile is checked against the wheel layout it was made for.
            DeviceMap? calibration = null;
            var errors = new List<string>();
            if (bundle && root.TryGetProperty("calibration", out JsonElement calEl) && calEl.ValueKind == JsonValueKind.Object)
                calibration = ReadCalibration(calEl, errors);

            DeviceRegistry checkAgainst = devices;
            if (calibration is not null)
            {
                checkAgainst = new DeviceRegistry();
                foreach (DeviceMap d in devices.All) checkAgainst.Add(d);
                checkAgainst.Add(calibration);
            }
            ProfileLoadResult loaded;
            try { loaded = ProfileLoader.Parse(profileEl.GetRawText(), checkAgainst); }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return Failed("The profile in that text could not be read: " + ex.Message); }

            WheelEdition edition = calibration is not null ? WheelEditions.Of(calibration)
                : bundle && root.TryGetProperty("edition", out var ed) && ed.ValueKind == JsonValueKind.String && Enum.TryParse(ed.GetString(), true, out WheelEdition fromText) ? fromText
                : WheelEdition.Unknown;
            var share = new ShareImport { Profile = loaded.Profile, Calibration = calibration, Edition = edition };
            share.Errors.AddRange(errors);
            share.Errors.AddRange(loaded.Errors);
            share.Warnings.AddRange(notes);
            share.Warnings.AddRange(loaded.Warnings);
            if (share.Profile is not null) SwitchOffExtras(share);
            return share;
        }
    }

    private static ShareImport Failed(string message)
    {
        var s = new ShareImport();
        s.Errors.Add(message);
        return s;
    }

    private static void NoteUnknown(JsonElement obj, HashSet<string> known, string where, List<string> notes)
    {
        foreach (JsonProperty p in obj.EnumerateObject())
            if (!known.Contains(p.Name)) notes.Add($"Ignored an unknown field \"{where}{p.Name}\".");
    }

    private static DeviceMap? ReadCalibration(JsonElement el, List<string> errors)
    {
        DeviceMap? map;
        try { map = JsonSerializer.Deserialize<DeviceMap>(el.GetRawText(), ProfileLoader.JsonOptions); }
        catch (JsonException ex) { errors.Add("The calibration in that text is not valid: " + ex.Message); return null; }
        if (map is null) return null;

        map.Axes = new(map.Axes, StringComparer.OrdinalIgnoreCase);
        map.Buttons = new(map.Buttons, StringComparer.OrdinalIgnoreCase);
        void Bad(string why) => errors.Add("The calibration in that text is not valid: " + why);

        if (string.IsNullOrWhiteSpace(map.Name) || map.Name.Length > 40 || !map.Name.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or ' ')) { Bad("its name is missing or has odd characters."); return null; }
        if (map.Extra) { Bad("it is for an extra device, not a wheel."); return null; }
        if (map.Axes.Count == 0 || map.Axes.Count > 16) { Bad("it has no axes, or too many."); return null; }
        if (map.Buttons.Count > 64) { Bad("it has too many named buttons."); return null; }
        if (map.RotationDegrees is < 90 or > 3000) { Bad("the rotation is not between 90 and 3000 degrees."); return null; }
        if (map.Vid.Length > 4 || map.Pid.Length > 4 || (map.Vid + map.Pid).Any(c => !Uri.IsHexDigit(c))) { Bad("the USB ids are not hexadecimal."); return null; }
        foreach (var (name, axis) in map.Axes)
        {
            if (name.Length > 32 || axis.SourceIndex < 0) { Bad($"axis \"{name}\" is not understood."); return null; }
            if (axis.Min < 0 || axis.Max > 65535 || axis.Min >= axis.Max || axis.Centre < axis.Min || axis.Centre > axis.Max) { Bad($"axis \"{name}\" has an impossible range."); return null; }
        }
        foreach (var (name, number) in map.Buttons)
            if (name.Length > 32 || number < 1 || number > DeviceMap.MaxButtons) { Bad($"button \"{name}\" is not understood."); return null; }
        return map;
    }

    /// <summary>
    /// Keys and mouse, turbo and hiding the wheel affect programs other than the controller, so a profile from somewhere else never switches
    /// them on by itself: they come in off, and stay off until the user turns them on.
    /// </summary>
    private static void SwitchOffExtras(ShareImport share)
    {
        Profile p = share.Profile!;
        // Keys, mouse and turbo anywhere in the profile, including inside its modes, come in off.
        IEnumerable<Rule> everyRule = p.Rules.Concat(p.Modes.SelectMany(m => m.Rules));
        int keyRules = everyRule.Count(r => Target.TryParse(r.To, out Target? t, out _) && t is { Kind: TargetKind.Key, Pad: 1 });
        if (p.Outputs.Keyboard || keyRules > 0 || ProfileSafety.Effective(p) == ProfileSafety.Standard)
            share.TurnedOff.Add(keyRules > 0
                ? $"Keyboard and mouse output (the {keyRules} key or mouse rule{(keyRules == 1 ? "" : "s")} stay in the profile but do nothing until you turn it on)"
                : "Keyboard and mouse output");
        p.Outputs.Keyboard = false;
        p.Safety = ProfileSafety.Strict;

        if (p.Outputs.Turbo || everyRule.Any(r => r.Turbo)) share.TurnedOff.Add("Turbo");
        p.Outputs.Turbo = false;
        foreach (Rule r in p.Rules.Concat(p.Modes.SelectMany(m => m.Rules))) r.Turbo = false;

        if (p.HideWheel is not null) share.TurnedOff.Add("Hiding the wheel from games");
        p.HideWheel = null;
    }

    // ---------------------------------------------------------------- calibration compatibility

    /// <summary>How the calibration that came with a profile compares with the wheel that is connected.</summary>
    public enum CalibrationFit { None, Same, Different, Unknown }

    public static CalibrationFit Fit(ShareImport import, WheelEdition connected)
    {
        if (import.Calibration is null) return CalibrationFit.None;
        if (connected == WheelEdition.Unknown || import.Edition == WheelEdition.Unknown) return CalibrationFit.Unknown;
        return connected == import.Edition ? CalibrationFit.Same : CalibrationFit.Different;
    }
}

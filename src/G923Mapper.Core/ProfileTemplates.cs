using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace G923Mapper;

/// <summary>
/// The template a game profile is based on, as it was when the profile last took it over. A profile never reads its template live: it keeps this
/// copy (<see cref="Base"/>), so changing a template changes nothing until the user applies the update.
/// </summary>
public sealed class ParentLink
{
    /// <summary>The template's name.</summary>
    public string Name { get; set; } = "";
    /// <summary>The template as it was when this profile was created from it or last updated.</summary>
    public Profile? Base { get; set; }
}

public enum DiffKind { Setting, RuleChanged, RuleAdded, RuleRemoved }

/// <summary>One way a game profile differs from its template: a setting or a rule (by its output target) that is its own.</summary>
/// <param name="Key">What differs: a setting such as "force.rumble", or a rule's output target such as "RightTrigger".</param>
/// <param name="Label">The words for a list.</param>
/// <param name="Template">What the template has (empty when it has nothing).</param>
/// <param name="Current">What this profile has (empty when it has removed it).</param>
public sealed record TemplateDiff(DiffKind Kind, string Key, string Label, string Template, string Current);

/// <summary>
/// Genre templates: a template is an ordinary profile marked <c>isTemplate</c> with no game attached; a game profile can name one as its
/// <c>parent</c> and then stores only what it does differently. Everything else comes from the template copy the profile has pinned.
/// A profile is always read into an ordinary, fully merged <see cref="Profile"/>, so the engine and everything else never see the difference;
/// writing it back stores only the differences. Pure, so inheritance, overrides and updates are tested in one place.
/// </summary>
public static class Templates
{
    // ---------------------------------------------------------------- copies and the revision

    /// <summary>A deep copy with every setting and rule written out (never the template form), so it can be changed freely.</summary>
    internal static Profile CloneRaw(Profile p)
    {
        Profile copy = JsonSerializer.Deserialize<Profile>(ProfileWriter.PlainJson(p), ProfileLoader.JsonOptions)!;
        copy.Parent = null;
        copy.RemovedRules = null;
        return copy;
    }

    /// <summary>
    /// A template as a plain profile: no game, not marked as a template, no parent. What a game profile pins, and what is compared to see
    /// whether the template has changed.
    /// </summary>
    public static Profile Flatten(Profile template)
    {
        Profile copy = CloneRaw(template);
        copy.IsTemplate = false;
        copy.Parent = null;
        copy.RemovedRules = null;
        copy.Modes = new List<ProfileMode>();   // modes belong to one game's profile, not to a template
        copy.Match = new ProfileMatch();
        return copy;
    }

    /// <summary>
    /// A short fingerprint of everything in a template that reaches the profiles built on it (rules, outputs, force, device, safety). Its name
    /// and notes are left out, so renaming a template or rewording its note does not make every profile show an update.
    /// </summary>
    public static string Revision(Profile template)
    {
        Profile copy = Flatten(template);
        copy.Name = "";
        copy.Notes = null;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(ProfileWriter.ToJson(copy)));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    /// <summary>Whether the template has changed since this profile pinned its copy of it. False when the template cannot be found.</summary>
    public static bool UpdateAvailable(Profile child, Profile? currentTemplate) =>
        child.Parent?.Base is { } pinned && currentTemplate is { IsTemplate: true } && Revision(pinned) != Revision(currentTemplate);

    // ---------------------------------------------------------------- reading a profile that has a parent

    private static bool Has(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.EnumerateObject().Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private static JsonElement Get(JsonElement obj, string name) =>
        obj.EnumerateObject().First(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    internal static string TargetKey(Rule r) => r.To.Trim().ToLowerInvariant();

    /// <summary>
    /// Merge a profile file that names a parent: the pinned template copy, with whatever the file sets laid over it. A setting the file leaves out
    /// comes from the template. A rule in the file replaces the template's rules for the same output target (or is added if the template has
    /// none), and <c>removedRules</c> drops the template's rules for the targets it lists. Name, game and notes are always the profile's own.
    /// </summary>
    internal static Profile? Resolve(Profile child, string json, out string? error)
    {
        error = null;
        if (child.Parent is not { } link || link.Base is null)
        {
            error = "This profile is based on a template but does not carry its copy of it, so it cannot be read.";
            return null;
        }
        if (string.IsNullOrWhiteSpace(link.Name)) { error = "This profile is based on a template without a name."; return null; }
        if (child.IsTemplate) { error = "A template cannot be based on another template."; return null; }
        if (link.Base.Parent is not null) { error = "The template copy inside this profile is itself based on a template."; return null; }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException ex) { error = "Invalid JSON: " + ex.Message; return null; }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            Profile r = CloneRaw(link.Base);
            r.Name = child.Name;
            r.IsTemplate = false;
            r.Parent = link;
            r.RemovedRules = null;
            r.Notes = child.Notes;                       // what a profile is for is its own, not the template's
            r.Match = child.Match ?? new ProfileMatch();  // and so is its game

            if (Has(root, "device")) r.Device = child.Device;
            if (Has(root, "safety")) r.Safety = child.Safety;
            if (Has(root, "hideWheel")) r.HideWheel = child.HideWheel;
            if (Has(root, "leaveWheel")) r.LeaveWheel = child.LeaveWheel;
            if (Has(root, "modeShortcuts")) r.ModeShortcuts = CopyShortcuts(child.ModeShortcuts);
            if (Has(root, "keepKeys")) r.KeepKeys = child.KeepKeys;
            if (Has(root, "outputs"))
            {
                JsonElement o = Get(root, "outputs");
                if (Has(o, "keyboard")) r.Outputs.Keyboard = child.Outputs.Keyboard;
                if (Has(o, "turbo")) r.Outputs.Turbo = child.Outputs.Turbo;
                if (Has(o, "pad")) r.Outputs.Pad = child.Outputs.Pad;
                if (Has(o, "secondPad")) r.Outputs.SecondPad = child.Outputs.SecondPad;
            }
            if (Has(root, "force") && Get(root, "force") is { ValueKind: JsonValueKind.Object } f && child.Force is { } cf)
            {
                r.Force ??= new ProfileForce();
                if (Has(f, "rumble")) r.Force.Rumble = cf.Rumble;
                if (Has(f, "spring")) r.Force.Spring = cf.Spring;
                if (Has(f, "friction")) r.Force.Friction = cf.Friction;
                if (Has(f, "motors")) r.Force.Motors = cf.Motors;
            }

            // Rules: the template's, with the profile's own laid over them by output target.
            var own = child.Rules.GroupBy(TargetKey).ToDictionary(g => g.Key, g => g.ToList());
            var removed = new HashSet<string>((child.RemovedRules ?? []).Select(t => t.Trim().ToLowerInvariant()));
            var merged = new List<Rule>();
            var placed = new HashSet<string>();
            foreach (Rule baseRule in r.Rules)
            {
                string key = TargetKey(baseRule);
                if (own.TryGetValue(key, out var replacement))
                {
                    if (placed.Add(key)) merged.AddRange(replacement);   // the profile's rules take the place of all the template's for this target
                }
                else if (!removed.Contains(key)) merged.Add(baseRule);
            }
            foreach (var (key, rules) in own)
                if (placed.Add(key)) merged.AddRange(rules);
            r.Rules = merged;
            r.Modes = child.Modes;   // modes are always the profile's own
            return r;
        }
    }

    // ---------------------------------------------------------------- writing it back: only the differences

    private static string RuleText(Rule r) => ProfileWriter.RuleText(r);

    internal static Dictionary<string, List<Rule>> Groups(Profile p) => p.Rules.GroupBy(TargetKey).ToDictionary(g => g.Key, g => g.ToList());

    internal static bool SameGroup(List<Rule> a, List<Rule> b) => a.Count == b.Count && a.Zip(b).All(z => RuleText(z.First) == RuleText(z.Second));

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>The settings in which a profile differs from its template copy, with their values, by setting name.</summary>
    private static List<(string Key, string Label, string Template, string Current)> SettingDiffs(Profile child, Profile b)
    {
        var list = new List<(string, string, string, string)>();
        void Add(string key, string label, string template, string current) { if (template != current) list.Add((key, label, template, current)); }
        string Opt(int? v) => v?.ToString() ?? "(app setting)";

        Add("device", "Wheel (device)", b.Device, child.Device);
        Add("safety", "Safety", ProfileSafety.Effective(b), ProfileSafety.Effective(child));
        Add("outputs.keyboard", "Keyboard and mouse output", b.Outputs.Keyboard ? "on" : "off", child.Outputs.Keyboard ? "on" : "off");
        Add("outputs.turbo", "Turbo", b.Outputs.Turbo ? "on" : "off", child.Outputs.Turbo ? "on" : "off");
        string PadName(Profile x) => PadKinds.Normalise(x) switch { PadKinds.AutoText => "match the wheel", PadKinds.DualShock4Text => "DualShock 4", _ => "Xbox 360" };
        Add("outputs.pad", "Controller", PadName(b), PadName(child));
        Add("outputs.secondPad", "Second controller", b.Outputs.SecondPad ? "on" : "off", child.Outputs.SecondPad ? "on" : "off");
        Add("hideWheel", "Hide the wheel", b.HideWheel is bool hb ? (hb ? "hide" : "show") : "leave", child.HideWheel is bool hc ? (hc ? "hide" : "show") : "leave");
        Add("leaveWheel", "Leave the wheel alone", b.LeaveWheel ? "on" : "off", child.LeaveWheel ? "on" : "off");
        Add("modeShortcuts", "Mode shortcuts", ShortcutText(b.ModeShortcuts), ShortcutText(child.ModeShortcuts));
        Add("keepKeys", "Keep sending keys", b.KeepKeys ? "on" : "off", child.KeepKeys ? "on" : "off");
        Add("force.rumble", "Rumble strength", Opt(b.Force?.Rumble), Opt(child.Force?.Rumble));
        Add("force.spring", "Centring spring", Opt(b.Force?.Spring), Opt(child.Force?.Spring));
        Add("force.friction", "Wheel friction", Opt(b.Force?.Friction), Opt(child.Force?.Friction));
        Add("force.motors", "Rumble motors", b.Force?.Motors ?? "(app setting)", child.Force?.Motors ?? "(app setting)");
        return list;
    }

    /// <summary>How a profile differs from its template: each setting and each rule (by output target) that is its own. Empty for a profile with no template.</summary>
    private static List<ModeShortcut>? CopyShortcuts(List<ModeShortcut>? list) =>
        list?.Select(m => new ModeShortcut { Trigger = m.Trigger, Action = m.Action }).ToList();

    private static string ShortcutText(List<ModeShortcut>? list) =>
        list is null ? "the app-wide ones" : list.Count == 0 ? "none" : string.Join(", ", list.Select(m => m.Describe()));

    public static IReadOnlyList<TemplateDiff> Diff(Profile child)
    {
        var diffs = new List<TemplateDiff>();
        if (child.Parent?.Base is not { } b) return diffs;
        foreach (var (key, label, template, current) in SettingDiffs(child, b))
            diffs.Add(new TemplateDiff(DiffKind.Setting, key, label, template, current));

        var baseGroups = Groups(b);
        var ownGroups = Groups(child);
        foreach (var (key, rules) in ownGroups)
        {
            string target = rules[0].To.Trim();
            if (!baseGroups.TryGetValue(key, out var original))
                diffs.Add(new TemplateDiff(DiffKind.RuleAdded, target, $"Rule to {target}", "", Summarise(rules)));
            else if (!SameGroup(original, rules))
                diffs.Add(new TemplateDiff(DiffKind.RuleChanged, target, $"Rule to {target}", Summarise(original), Summarise(rules)));
        }
        foreach (var (key, rules) in baseGroups)
            if (!ownGroups.ContainsKey(key))
                diffs.Add(new TemplateDiff(DiffKind.RuleRemoved, rules[0].To.Trim(), $"Rule to {rules[0].To.Trim()}", Summarise(rules), ""));
        return diffs;
    }

    private static string Summarise(List<Rule> rules) => string.Join("; ", rules.Select(r =>
    {
        var bits = new List<string> { r.From };
        if (r.Deadzone > 0) bits.Add($"{r.Deadzone * 100:0.##}% deadzone");
        if (r.AntiDeadzone > 0) bits.Add($"{r.AntiDeadzone * 100:0.##}% anti-deadzone");
        if (!r.Curve.Equals("linear", StringComparison.OrdinalIgnoreCase)) bits.Add(r.Curve.ToLowerInvariant());
        if (r.Threshold is double t) bits.Add($"at {t * 100:0.##}%");
        if (!r.Mode.Equals("hold", StringComparison.OrdinalIgnoreCase)) bits.Add(r.Mode.ToLowerInvariant());
        if (r.Invert) bits.Add("inverted");
        return string.Join(", ", bits);
    }));

    /// <summary>
    /// The profile as a file: its own name, game and notes, the template copy it has pinned, and only what differs from that copy. A profile
    /// with no template is written as a plain profile (see <see cref="ProfileWriter.ToJson"/>).
    /// </summary>
    /// <param name="useBase">Pin this template copy instead of the one the profile has, keeping every difference (used to apply an update).</param>
    internal static string ChildJson(Profile child, Profile? useBase = null)
    {
        Profile b = child.Parent!.Base!;
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"name\": {JsonSerializer.Serialize(child.Name)},");
        if (Nz(child.Notes) is { } notes) sb.AppendLine($"  \"notes\": {JsonSerializer.Serialize(notes)},");
        if (child.Match.Exe.Count > 0)
            sb.AppendLine($"  \"match\": {{ \"exe\": [{string.Join(", ", child.Match.Exe.Select(e => JsonSerializer.Serialize(e)))}] }},");

        sb.Append("  \"parent\": { \"name\": ").Append(JsonSerializer.Serialize(child.Parent.Name)).Append(", \"base\": ")
          .Append(ProfileWriter.ToJson(useBase ?? b).TrimEnd().Replace("\r\n", "\n").Replace("\n", "\n  ")).AppendLine(" },");

        var diffs = SettingDiffs(child, b).Select(d => d.Key).ToHashSet();
        if (diffs.Contains("device")) sb.AppendLine($"  \"device\": {JsonSerializer.Serialize(child.Device)},");
        if (diffs.Contains("safety")) sb.AppendLine($"  \"safety\": {JsonSerializer.Serialize(ProfileSafety.Effective(child))},");

        var outputs = new List<string>();
        if (diffs.Contains("outputs.keyboard")) outputs.Add($"\"keyboard\": {(child.Outputs.Keyboard ? "true" : "false")}");
        if (diffs.Contains("outputs.turbo")) outputs.Add($"\"turbo\": {(child.Outputs.Turbo ? "true" : "false")}");
        if (diffs.Contains("outputs.pad")) outputs.Add($"\"pad\": {JsonSerializer.Serialize(PadKinds.Normalise(child))}");
        if (diffs.Contains("outputs.secondPad")) outputs.Add($"\"secondPad\": {(child.Outputs.SecondPad ? "true" : "false")}");
        if (outputs.Count > 0) sb.AppendLine($"  \"outputs\": {{ {string.Join(", ", outputs)} }},");

        if (diffs.Contains("hideWheel")) sb.AppendLine(child.HideWheel is bool hw ? $"  \"hideWheel\": {(hw ? "true" : "false")}," : "  \"hideWheel\": null,");
        if (diffs.Contains("modeShortcuts"))
            sb.AppendLine(child.ModeShortcuts is { } shortcuts
                ? "  \"modeShortcuts\": [" + string.Join(", ", shortcuts.Select(m => $"{{ \"trigger\": \"{m.Trigger}\", \"action\": \"{m.Action.Replace("\"", "")}\" }}")) + "],"
                : "  \"modeShortcuts\": null,");
        if (diffs.Contains("leaveWheel")) sb.AppendLine($"  \"leaveWheel\": {(child.LeaveWheel ? "true" : "false")},");
        if (diffs.Contains("keepKeys")) sb.AppendLine($"  \"keepKeys\": {(child.KeepKeys ? "true" : "false")},");

        var force = new List<string>();
        if (diffs.Contains("force.rumble")) force.Add($"\"rumble\": {(child.Force?.Rumble is int ru ? ru.ToString() : "null")}");
        if (diffs.Contains("force.spring")) force.Add($"\"spring\": {(child.Force?.Spring is int sp ? sp.ToString() : "null")}");
        if (diffs.Contains("force.friction")) force.Add($"\"friction\": {(child.Force?.Friction is int fr ? fr.ToString() : "null")}");
        if (diffs.Contains("force.motors")) force.Add($"\"motors\": {(child.Force?.Motors is { } mo ? JsonSerializer.Serialize(mo.ToLowerInvariant()) : "null")}");
        if (force.Count > 0) sb.AppendLine($"  \"force\": {{ {string.Join(", ", force)} }},");

        // Rules of their own: every rule for an output target that differs from the template's (or that the template has none for).
        var baseGroups = Groups(b);
        var ownGroups = Groups(child);
        var different = ownGroups.Where(kv => !baseGroups.TryGetValue(kv.Key, out var o) || !SameGroup(o, kv.Value)).Select(kv => kv.Key).ToHashSet();
        var removedTargets = baseGroups.Where(kv => !ownGroups.ContainsKey(kv.Key)).Select(kv => kv.Value[0].To.Trim()).ToList();
        if (removedTargets.Count > 0) sb.AppendLine($"  \"removedRules\": [{string.Join(", ", removedTargets.Select(t => JsonSerializer.Serialize(t)))}],");
        var lines = child.Rules.Where(r => different.Contains(TargetKey(r))).Select(r => "    " + RuleText(r)).ToList();
        sb.AppendLine("  \"rules\": [");
        for (int i = 0; i < lines.Count; i++) sb.AppendLine(lines[i] + (i < lines.Count - 1 ? "," : ""));
        sb.AppendLine(child.Modes.Count > 0 ? "  ]," : "  ]");
        if (child.Modes.Count > 0) ProfileWriter.AppendModes(sb, child);
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ---------------------------------------------------------------- the operations

    /// <summary>A new game profile based on a template: it starts out identical to it (strict, with keyboard and mouse off), with its own name and game.</summary>
    public static Profile CreateChild(Profile template, string name, IEnumerable<string>? exes = null)
    {
        Profile pinned = Flatten(template);
        Profile child = CloneRaw(pinned);
        child.Name = name.Trim();
        child.Notes = null;
        child.IsTemplate = false;
        child.Match = new ProfileMatch { Exe = (exes ?? []).Select(e => e.Trim()).Where(e => e.Length > 0).ToList() };
        child.Parent = new ParentLink { Name = template.Name, Base = pinned };
        // Like a pasted profile, a new one never starts with keys, mouse or turbo switched on, even if its template has them. The user turns them on.
        child.Outputs.Keyboard = false;
        child.Outputs.Turbo = false;
        child.Safety = ProfileSafety.Strict;
        foreach (Rule r in child.Rules) r.Turbo = false;
        return child;
    }

    /// <summary>A template made from any profile: its settings and rules, with no game and no parent.</summary>
    public static Profile SaveAsTemplate(Profile source, string name)
    {
        Profile t = CloneRaw(source);
        t.Name = name.Trim();
        t.IsTemplate = true;
        t.Parent = null;
        t.RemovedRules = null;
        t.Modes = new List<ProfileMode>();
        t.Match = new ProfileMatch();
        return t;
    }

    /// <summary>The profile with its template taken out: the same behaviour as an ordinary profile (what is copied and shared).</summary>
    public static Profile Detach(Profile child)
    {
        Profile p = CloneRaw(child);
        p.Parent = null;
        p.RemovedRules = null;
        p.IsTemplate = false;
        return p;
    }

    /// <summary>Put one difference back to what the template has. The profile is changed in place.</summary>
    public static void Reset(Profile child, TemplateDiff diff)
    {
        if (child.Parent?.Base is not { } b) return;
        switch (diff.Kind)
        {
            case DiffKind.Setting:
                ResetSetting(child, b, diff.Key);
                break;
            case DiffKind.RuleAdded:
                child.Rules.RemoveAll(r => TargetKey(r) == diff.Key.ToLowerInvariant());
                break;
            default:   // changed or removed: the template's rules for this target come back, where the profile's own were (or at the end)
            {
                string key = diff.Key.ToLowerInvariant();
                var restore = b.Rules.Where(r => TargetKey(r) == key).Select(r => JsonSerializer.Deserialize<Rule>(JsonSerializer.Serialize(r, ProfileLoader.JsonOptions), ProfileLoader.JsonOptions)!).ToList();
                int at = child.Rules.FindIndex(r => TargetKey(r) == key);
                child.Rules.RemoveAll(r => TargetKey(r) == key);
                child.Rules.InsertRange(at < 0 ? child.Rules.Count : Math.Min(at, child.Rules.Count), restore);
                break;
            }
        }
    }

    private static void ResetSetting(Profile child, Profile b, string key)
    {
        switch (key)
        {
            case "device": child.Device = b.Device; break;
            case "safety": child.Safety = b.Safety; break;
            case "outputs.keyboard": child.Outputs.Keyboard = b.Outputs.Keyboard; break;
            case "outputs.turbo": child.Outputs.Turbo = b.Outputs.Turbo; break;
            case "outputs.pad": child.Outputs.Pad = b.Outputs.Pad; break;
            case "outputs.secondPad": child.Outputs.SecondPad = b.Outputs.SecondPad; break;
            case "hideWheel": child.HideWheel = b.HideWheel; break;
            case "leaveWheel": child.LeaveWheel = b.LeaveWheel; break;
            case "modeShortcuts": child.ModeShortcuts = CopyShortcuts(b.ModeShortcuts); break;
            case "keepKeys": child.KeepKeys = b.KeepKeys; break;
            case "force.rumble": SetForce(child, f => f.Rumble = b.Force?.Rumble); break;
            case "force.spring": SetForce(child, f => f.Spring = b.Force?.Spring); break;
            case "force.friction": SetForce(child, f => f.Friction = b.Force?.Friction); break;
            case "force.motors": SetForce(child, f => f.Motors = b.Force?.Motors); break;
        }
    }

    private static void SetForce(Profile child, Action<ProfileForce> set)
    {
        child.Force ??= new ProfileForce();
        set(child.Force);
        if (child.Force.IsEmpty) child.Force = null;
    }

    /// <summary>Put every difference back: the profile becomes exactly its template, keeping only its own name, game and notes.</summary>
    public static void ResetAll(Profile child)
    {
        foreach (TemplateDiff d in Diff(child).ToList()) Reset(child, d);
    }

    /// <summary>
    /// The profile re-based on the template as it is now. Everything the profile does differently is kept, and what it did not set follows the
    /// new template. Returns the result of reading the updated profile (it can fail if the new template no longer fits).
    /// </summary>
    public static ProfileLoadResult ApplyUpdate(Profile child, Profile newTemplate, DeviceRegistry devices) =>
        ProfileLoader.Parse(ChildJson(child, Flatten(newTemplate)), devices);
}


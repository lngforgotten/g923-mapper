namespace G923Mapper;

/// <summary>One rule that presses a keyboard key or a mouse button, with a friendly name for it ("Q", "Mouse Left").</summary>
public sealed record KeyMapping(Rule Rule, string Key);

public enum KeyOutput
{
    /// <summary>Keys and mouse buttons are sent for this profile.</summary>
    Active,
    /// <summary>Off for this profile (keyboard output is not switched on, or the profile is strict), but it can be allowed.</summary>
    NeedsEnabling,
    /// <summary>Never sent: one of the profile's games is on the anti-cheat list.</summary>
    Blocked,
}

public sealed record KeyOutputStatus(KeyOutput State, string? Reason);

/// <summary>
/// The keyboard and mouse side of the controller picture: which rules press keys or mouse buttons, whether that output is allowed for the
/// profile, and the edits that add, change and remove them. Pure, so the "Keys and mouse" strip and its window share one tested set of rules.
/// </summary>
public static class KeyMappings
{
    /// <summary>The rules of the first controller that press a key or a mouse button, in the profile's order.</summary>
    public static IReadOnlyList<KeyMapping> Of(Profile profile)
    {
        var list = new List<KeyMapping>();
        foreach (Rule r in profile.Rules)
            if (Target.TryParse(r.To, out Target? t, out _) && t is { Kind: TargetKind.Key, Pad: 1 })
                list.Add(new KeyMapping(r, Label(r.To)));
        return list;
    }

    /// <summary>"Key:Q" is "Q"; "Mouse:Left" is "Mouse Left".</summary>
    public static string Label(string target)
    {
        string t = target.Trim();
        if (t.StartsWith(KeyNames.MousePrefix, StringComparison.OrdinalIgnoreCase)) return "Mouse " + t[KeyNames.MousePrefix.Length..].Trim();
        return t.StartsWith("Key:", StringComparison.OrdinalIgnoreCase) ? t[4..].Trim() : t;
    }

    /// <summary>The rule target for a key or mouse button name as the user would give it ("q", "Space", "Mouse:Left", "Mouse Left"), or null if it is not one.</summary>
    public static string? TargetFor(string name)
    {
        string n = name.Trim();
        if (n.StartsWith("Mouse ", StringComparison.OrdinalIgnoreCase)) n = KeyNames.MousePrefix + n[6..].Trim();
        if (KeyNames.IsMouse(n)) return KeyNames.Canonical(n);
        return KeyNames.Canonical(n) is { } key && KeyNames.TryGet(key, out _) ? "Key:" + key : null;
    }

    /// <summary>Whether keys and mouse buttons are sent for this profile, and if not, whether the user can allow it.</summary>
    public static KeyOutputStatus Status(Profile profile, AntiCheatList? list = null)
    {
        string? why = ProfileSafety.KeyboardBlockedReason(profile, list);
        if (why is null) return new KeyOutputStatus(KeyOutput.Active, null);
        return (list ?? AntiCheatList.Current).FirstProtected(profile.Match.Exe) is not null
            ? new KeyOutputStatus(KeyOutput.Blocked, why)
            : new KeyOutputStatus(KeyOutput.NeedsEnabling, why);
    }

    /// <summary>Allow keys and mouse buttons for the profile: switch keyboard output on and make the profile standard. The caller asks the user first.</summary>
    public static void Enable(Profile profile)
    {
        profile.Outputs.Keyboard = true;
        profile.Safety = ProfileSafety.Standard;
    }

    /// <summary>
    /// Make <paramref name="from"/> press <paramref name="keyName"/>. With <paramref name="existing"/> that rule is changed in place; without it
    /// a rule is added at the end. Any other rule that already did exactly this is dropped. Returns the rule, or null for an unknown key.
    /// </summary>
    public static Rule? Set(Profile profile, DeviceMap device, Rule? existing, string from, string keyName)
    {
        if (TargetFor(keyName) is not { } target || string.IsNullOrWhiteSpace(from)) return null;
        Rule rule = RuleFactory.Make(device, from, target);
        if (existing is not null && profile.Rules.IndexOf(existing) is int at and >= 0)
        {
            // Keep what the user had tuned (trigger point, mode, turbo...) when only the wheel control or the key changes.
            existing.From = rule.From;
            existing.To = rule.To;
            if (existing.Threshold is null && rule.Threshold is not null) existing.Threshold = rule.Threshold;
            profile.Rules.RemoveAll(r => !ReferenceEquals(r, existing) && Same(r, rule));
            return existing;
        }
        profile.Rules.RemoveAll(r => Same(r, rule));
        profile.Rules.Add(rule);
        return rule;
    }

    private static bool Same(Rule a, Rule b) =>
        a.From.Trim().Equals(b.From.Trim(), StringComparison.OrdinalIgnoreCase) && a.To.Trim().Equals(b.To.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Remove one key or mouse rule. False if it was not in the profile.</summary>
    public static bool Remove(Profile profile, Rule rule) => profile.Rules.Remove(rule);
}

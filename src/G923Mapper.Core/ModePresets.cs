namespace G923Mapper;

/// <summary>One ready-made mode: what to call it, what it is for, and the rules it starts with.</summary>
/// <param name="UsesKeys">The rules press keys or mouse buttons, so they only work with Standard safety and keyboard output on.</param>
public sealed record ModePreset(string Id, string Name, string Description, bool UsesKeys);

/// <summary>
/// The modes that most games want, ready to add to a profile: the pedals and paddles for driving are the same in every game, so adding a Driving
/// mode needs no setup. Each preset gives only the rules that are not already in the profile (it never asks for a steering rule if the profile
/// already steers), and the user can change anything afterwards.
/// </summary>
public static class ModePresets
{
    public const string Driving = "driving", Shooting = "shooting", Flying = "flying", Blank = "blank";

    public static readonly IReadOnlyList<ModePreset> All =
    [
        new(Driving, "Driving",
            "Throttle and brake are the right and left triggers, the paddles are the bumpers, and the wheel steers. The usual set-up for any racing or driving game.", false),
        new(Shooting, "Shooting",
            "For shooters on keyboard and mouse: clutch leans left (Q), throttle leans right (E), brake crouches (C, as a toggle). Uses keys, so it needs Standard safety.", true),
        new(Flying, "Flying",
            "Throttle pitches up and brake pitches down (right stick), the clutch is engine power (right trigger), and the wheel rolls with a gentler curve.", false),
        new(Blank, "Blank mode", "Starts with nothing of its own: it behaves like the rest of the profile until you give it rules.", false),
    ];

    /// <summary>The preset a typed mode name stands for ("driving", "Shooting"...), so naming a mode after one is enough. Null for any other name.</summary>
    public static ModePreset? ForName(string name) =>
        All.FirstOrDefault(p => p.Id != Blank && string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    public static ModePreset? Find(string id) => All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    private static Rule R(string from, string to, Action<Rule>? tweak = null)
    {
        var rule = new Rule { From = from, To = to, Curve = "linear" };
        tweak?.Invoke(rule);
        return rule;
    }

    private static bool Steers(IEnumerable<Rule> profileRules) =>
        profileRules.Any(r => r.To.Trim().Equals("LeftStickX", StringComparison.OrdinalIgnoreCase));

    /// <summary>The rules a new mode of this kind starts with. <paramref name="profileRules"/> are the profile's own rules, which apply in every mode.</summary>
    public static List<Rule> RulesFor(string id, IEnumerable<Rule> profileRules)
    {
        var rules = new List<Rule>();
        bool steers = Steers(profileRules);
        switch (id.ToLowerInvariant())
        {
            case Driving:
                if (!steers) rules.Add(R("wheel", "LeftStickX", r => r.Deadzone = 0.04));
                rules.Add(R("throttle", "RightTrigger"));
                rules.Add(R("brake", "LeftTrigger"));
                rules.Add(R("paddleL", "Button:LeftShoulder"));
                rules.Add(R("paddleR", "Button:RightShoulder"));
                break;
            case Shooting:
                rules.Add(R("clutch", "Key:Q", r => r.Threshold = 0.35));
                rules.Add(R("throttle", "Key:E", r => r.Threshold = 0.5));
                rules.Add(R("brake", "Key:C", r => { r.Threshold = 0.45; r.Mode = "toggle"; }));
                break;
            case Flying:
                if (!steers) rules.Add(R("wheel", "LeftStickX", r => { r.Curve = "expo"; r.Exponent = 1.6; r.Deadzone = 0.03; }));
                rules.Add(R("throttle", "RightStickY", r => r.Deadzone = 0.03));
                rules.Add(R("brake", "RightStickY", r => { r.Deadzone = 0.03; r.Expr = "0 - x"; }));
                rules.Add(R("clutch", "RightTrigger"));
                break;
        }
        return rules;
    }

    /// <summary>
    /// Add a mode to a profile with a preset's rules (a blank mode for an unknown preset). Where the profile's own rules already use one of the
    /// inputs the preset uses for some other output (the pedals doing something else for shooting, say), those rules are turned off in the new mode,
    /// so the pedals do one job at a time. Returns the new mode.
    /// </summary>
    public static ProfileMode AddTo(Profile profile, string name, string presetId)
    {
        ProfileMode mode = ProfileModes.Add(profile, ProfileModes.FreeName(profile, name));
        mode.Rules = RulesFor(presetId, profile.Rules);
        var inputs = mode.Rules.Select(r => r.From.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targets = mode.Rules.Select(r => r.To.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var displaced = profile.Rules
            .Where(r => inputs.Contains(r.From.Trim()) && !targets.Contains(r.To.Trim())
                        && !(Target.TryParse(r.To, out Target? t, out _) && t is { Kind: TargetKind.Mode }))
            .Select(r => r.To.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        mode.RemovedRules = displaced.Count == 0 ? null : displaced;
        return mode;
    }
}

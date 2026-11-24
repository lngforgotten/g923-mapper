namespace G923Mapper;

/// <summary>One thing on a controller that can be given a wheel control: a button, a trigger, or one direction of a stick.</summary>
/// <param name="Target">The rule target (Button:A, RightTrigger, LeftStickX ...).</param>
public sealed record PadSlot(string Label, string Target);

/// <summary>
/// What a click on the controller picture means: which slots live under the control that was clicked, what each is called, and which rules of a
/// profile already feed them. Pure, so the picture and the assign window share one tested set of rules.
/// </summary>
public static class PadControls
{
    /// <summary>The control under a point of the picture (a key into <see cref="PadLayout"/>, such as "Button:A" or "LeftStick"), or null.</summary>
    public static string? At(double x, double y, PadKind kind = PadKind.Xbox360)
    {
        string? best = null;
        double bestArea = double.MaxValue;
        foreach (string key in PadLayout.TargetsFor(kind))
        {
            PadShape s = PadLayout.Of(key, kind)!.Value;
            bool hit = s.Round
                ? Math.Pow((x - s.CentreX) / (s.W / 2), 2) + Math.Pow((y - s.CentreY) / (s.H / 2), 2) <= 1
                : x >= s.X && x <= s.X + s.W && y >= s.Y && y <= s.Y + s.H;
            if (hit && s.W * s.H < bestArea) { best = key; bestArea = s.W * s.H; }
        }
        return best;
    }

    /// <summary>
    /// Not a control of the picture but a place in the assign window: the wheel buttons that move between a profile's modes (rules sending to
    /// Mode:Next and Mode:Previous). Opened from the Mode strip on Home.
    /// </summary>
    public const string ModeSwitchKey = "ModeSwitch";

    /// <summary>A name for the control: the Xbox or PlayStation wording of its step, or "Left stick" for a stick.</summary>
    public static string Title(string key, PadKind kind)
    {
        if (key == ModeSwitchKey) return "Mode switch buttons";
        if (key == "LeftStick") return "Left stick";
        if (key == "RightStick") return "Right stick";
        var step = RemapSteps.All.FirstOrDefault(s => s.Target.Equals(key, StringComparison.OrdinalIgnoreCase));
        return step?.Label(kind) ?? key;
    }

    /// <summary>The things that can be assigned under this control. A stick has three: left-right, up-down and its click.</summary>
    public static IReadOnlyList<PadSlot> Slots(string key, PadKind kind)
    {
        switch (key)
        {
            case ModeSwitchKey:
                return [new("Next mode (cycles round)", "Mode:Next"), new("Previous mode", "Mode:Previous")];
            case "LeftStick":
                return [new("Left stick, left and right", "LeftStickX"), new("Left stick, up and down", "LeftStickY"),
                        new(kind == PadKind.DualShock4 ? "Left stick click (L3)" : "Left stick click", "Button:LeftThumb")];
            case "RightStick":
                return [new("Right stick, left and right", "RightStickX"), new("Right stick, up and down", "RightStickY"),
                        new(kind == PadKind.DualShock4 ? "Right stick click (R3)" : "Right stick click", "Button:RightThumb")];
            default:
                return PadLayout.Of(key, kind) is null ? [] : [new PadSlot(Title(key, kind), key)];
        }
    }

    /// <summary>For each control of the picture, the rules of the profile that feed it ("paddleR -> Button:A"). Keys and mouse buttons are not on the picture.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<Rule>> Mappings(Profile profile)
    {
        var map = new Dictionary<string, List<Rule>>(StringComparer.OrdinalIgnoreCase);
        foreach (Rule r in profile.Rules)
        {
            if (!Target.TryParse(r.To, out Target? t, out _) || t is null || t.Pad != 1 || t.Kind is TargetKind.Key or TargetKind.Mode) continue;
            string key = PadLayout.KeyOf(r.To);
            if (!map.TryGetValue(key, out var list)) map[key] = list = new();
            list.Add(r);
        }
        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Rule>)kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A word for each mapped control of the picture: the wheel control that feeds it ("paddleR"), with a "+" when several do. A stick shows
    /// the one feeding its left-right axis first, since that is the one that matters most.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Labels(Profile profile)
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, rules) in Mappings(profile))
        {
            Rule first = rules.FirstOrDefault(r => r.To.Trim().EndsWith("X", StringComparison.OrdinalIgnoreCase)) ?? rules[0];
            labels[key] = rules.Count > 1 ? first.From + "+" : first.From;
        }
        return labels;
    }

    /// <summary>Every (wheel control, picture control) pair the profile wires up, each once, grouped by control. Drives the signal-flow lines.</summary>
    public static IReadOnlyList<(string From, string Key)> Flows(Profile profile)
    {
        var seen = new HashSet<(string, string)>();
        var list = new List<(string, string)>();
        foreach (var (key, rules) in Mappings(profile))
            foreach (Rule r in rules)
                if (!string.IsNullOrWhiteSpace(r.From) && seen.Add((r.From.Trim().ToLowerInvariant(), key.ToLowerInvariant()))) list.Add((r.From.Trim(), key));
        return list;
    }

    /// <summary>The rules that feed one slot (a single target), in the profile's order.</summary>
    public static IReadOnlyList<Rule> RulesFor(Profile profile, string target) =>
        profile.Rules.Where(r => Target.TryParse(r.To, out Target? t, out _) && t is { Pad: 1 } && r.To.Trim().Equals(target, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>A one-line description of a rule for a tooltip or list: "paddleR" or "wheel, 4% deadzone".</summary>
    public static string Describe(Rule r)
    {
        var bits = new List<string> { r.From };
        if (r.Deadzone > 0) bits.Add($"{r.Deadzone * 100:0.##}% deadzone");
        if (r.Threshold is double t) bits.Add($"at {t * 100:0.##}%");
        if (!string.IsNullOrWhiteSpace(r.Expr)) bits.Add("= " + r.Expr);
        return string.Join(", ", bits);
    }

    /// <summary>
    /// Give <paramref name="target"/> the wheel control <paramref name="from"/>: any rules already feeding that target are replaced by one new
    /// rule with sensible settings (a threshold for a pedal on a button, a small deadzone for steering). Returns the same profile.
    /// </summary>
    public static Profile Assign(Profile profile, DeviceMap device, string from, string target)
    {
        int at = profile.Rules.FindIndex(r => r.To.Trim().Equals(target, StringComparison.OrdinalIgnoreCase));
        profile.Rules.RemoveAll(r => r.To.Trim().Equals(target, StringComparison.OrdinalIgnoreCase));
        Rule rule = RuleFactory.Make(device, from, target);
        profile.Rules.Insert(at < 0 ? profile.Rules.Count : Math.Min(at, profile.Rules.Count), rule);
        return profile;
    }

    /// <summary>Remove every rule feeding a target. Returns how many were removed.</summary>
    public static int Clear(Profile profile, string target) =>
        profile.Rules.RemoveAll(r => r.To.Trim().Equals(target, StringComparison.OrdinalIgnoreCase));
}

/// <summary>The rule that makes a given wheel control drive a given controller target, with the defaults the walk-through also uses.</summary>
public static class RuleFactory
{
    public static Rule Make(DeviceMap device, string from, string target)
    {
        var rule = new Rule { From = from.Trim(), To = target.Trim(), Curve = "linear" };
        bool axisTarget = Target.TryParse(target, out Target? t, out _) && t is { Kind: TargetKind.Axis };
        bool isAxisInput = device.Axes.ContainsKey(rule.From);

        if (axisTarget && t!.Axis is PadAxis.LeftStickX or PadAxis.RightStickX && device.IsSigned(rule.From))
            rule.Deadzone = 0.04;                        // a slightly off-centre wheel does not drift
        else if (!axisTarget && isAxisInput)
            rule.Threshold = 0.5;                        // a pedal or axis presses a button once it is pushed half way
        return rule;
    }
}

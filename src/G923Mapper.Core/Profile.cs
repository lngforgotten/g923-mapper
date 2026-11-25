using System.Text.Json;

namespace G923Mapper;

public sealed class ProfileMatch
{
    public List<string> Exe { get; set; } = new();
}

public sealed class ProfileOutputs
{
    public bool Keyboard { get; set; }
    /// <summary>Also present a second virtual controller to the game, fed by rules whose target starts with "P2:" (for example "P2:Button:A").</summary>
    public bool SecondPad { get; set; }
    /// <summary>Which controller the game sees: "xbox360" (the default) or "ds4" (a DualShock 4, for games that want PlayStation prompts). Rules keep using the Xbox button names.</summary>
    public string? Pad { get; set; }
    /// <summary>Allow rules marked "turbo" to auto-repeat their button. Standard safety mode only, never for games on the anti-cheat list.</summary>
    public bool Turbo { get; set; }
}

/// <summary>
/// Force feedback for one profile, overriding the app-wide settings for as long as the profile is in use. Leave a value out to keep the
/// app-wide one. Whether force feedback is on at all stays an app-wide choice, because it decides how the wheel is opened.
/// </summary>
public sealed class ProfileForce
{
    /// <summary>How strongly game rumble shakes the wheel, 0 to 100.</summary>
    public int? Rumble { get; set; }
    /// <summary>The centring spring, 0 to 100.</summary>
    public int? Spring { get; set; }
    /// <summary>A steady resistance to turning the wheel, 0 to 100.</summary>
    public int? Friction { get; set; }
    /// <summary>"both", "heavy" or "light": which of the game's two rumble motors the wheel plays.</summary>
    public string? Motors { get; set; }

    public bool IsEmpty => Rumble is null && Spring is null && Friction is null && Motors is null;
}

/// <summary>
/// One mode of a profile (Shooting, Driving, Flying...): a set of rules and a feel that apply while the mode is active, on top of the profile's own
/// rules, which apply in every mode. A mode's rule replaces the profile's rules for the same output target, a mode can turn some of them off, and its
/// feel overrides the profile's for whatever it sets. The first mode is the one a profile starts in.
/// </summary>
public sealed class ProfileMode
{
    public string Name { get; set; } = "";
    /// <summary>Force feedback for this mode, laid over the profile's own feel for whatever it sets; null = the profile's.</summary>
    public ProfileForce? Force { get; set; }
    /// <summary>Output targets of the profile's rules that are off while this mode is active.</summary>
    public List<string>? RemovedRules { get; set; }
    /// <summary>Rules that apply only in this mode. Each replaces the profile's rules for the same output target, or adds one.</summary>
    public List<Rule> Rules { get; set; } = new();
}

public sealed class Rule
{
    /// <summary>Named input: wheel, throttle, brake, clutch, paddleL, button7, dpadUp...</summary>
    public string From { get; set; } = "";
    /// <summary>LeftStickX, RightTrigger, Button:RightThumb, Key:F (keyboard output, not implemented yet).</summary>
    public string To { get; set; } = "";

    /// <summary>linear, expo or custom.</summary>
    public string Curve { get; set; } = "linear";
    /// <summary>Power for the expo curve: output = input^exponent.</summary>
    public double Exponent { get; set; } = 2.0;
    /// <summary>Custom curve control points [[x,y],...], x ascending in 0..1, linearly interpolated.</summary>
    public double[][]? Points { get; set; }

    /// <summary>Input magnitude at or below this is treated as zero; the rest is rescaled to 0..1.</summary>
    public double Deadzone { get; set; }
    /// <summary>Skip the game's own deadzone: any non-zero output starts at this fraction of full travel.</summary>
    public double AntiDeadzone { get; set; }
    public bool Invert { get; set; }

    /// <summary>For button targets: fire when the (post-invert, post-range) input reaches this. Negative = at or below. Default 0.5.</summary>
    public double? Threshold { get; set; }
    /// <summary>For button targets: hold (default), toggle or pulse.</summary>
    public string Mode { get; set; } = "hold";
    /// <summary>For pulse mode: how long the output stays pressed after the physical press.</summary>
    public int PulseMs { get; set; } = 50;

    /// <summary>Degrees of wheel travel that map to full stick deflection. Only meaningful for the wheel.</summary>
    public double? Range { get; set; }

    /// <summary>
    /// Use only one half of the axis: "full" (the default), "positive" or "negative". For the wheel, positive is turning right and negative left;
    /// for a pedal axis that is centred (for example a combined accelerator/brake axis), the upper or lower half of its travel. The half is
    /// stretched to 0..1, which is useful for splitting a combined axis into two controls.
    /// </summary>
    public string? Part { get; set; }

    /// <summary>
    /// Smooths the output of an axis rule: the value glides towards its target with this time constant (milliseconds; 0 = off). Takes out
    /// jitter and sudden jumps, at the price of a little delay, so keep it small for steering.
    /// </summary>
    public int Smooth { get; set; }

    /// <summary>
    /// Replaces the input value with a small calculation, for example "max(0, x)" to take the accelerator half of a combined
    /// pedal axis. <c>x</c> is this rule's input; other names are other inputs of the device. Applied after range, before invert.
    /// </summary>
    public string? Expr { get; set; }

    /// <summary>For button targets: while the button is held, press and release it repeatedly (see <see cref="TurboHz"/>). Needs outputs.turbo, standard safety.</summary>
    public bool Turbo { get; set; }
    /// <summary>Presses per second for turbo.</summary>
    public double TurboHz { get; set; } = 10;
}

public sealed class Profile
{
    public string Name { get; set; } = "";
    /// <summary>Free text for the user ("anti-deadzone 0.08 felt right in GTA"). Never used by the mapping itself.</summary>
    public string? Notes { get; set; }
    /// <summary>A template: a profile with no game attached that other profiles can be based on (see <see cref="Templates"/>).</summary>
    public bool IsTemplate { get; set; }
    /// <summary>The template this profile is based on, and the copy of it the profile has pinned. A profile with a parent stores only what it does differently.</summary>
    public ParentLink? Parent { get; set; }
    /// <summary>Only in a profile file that has a parent: the output targets of template rules this profile drops. Cleared when the file is read.</summary>
    public List<string>? RemovedRules { get; set; }
    public ProfileMatch Match { get; set; } = new();
    public string Device { get; set; } = DeviceRegistry.AutoDevice;
    /// <summary>"strict" (controller mapping only: the safe choice for games with online play or anti-cheat) or "standard" (extras such as keyboard output allowed). See <see cref="ProfileSafety.Effective"/>.</summary>
    public string? Safety { get; set; }
    public ProfileOutputs Outputs { get; set; } = new();
    /// <summary>Force feedback feel for this profile (rumble strength, centring spring, which motors); null = use the app-wide settings.</summary>
    public ProfileForce? Force { get; set; }
    /// <summary>
    /// true: hide the real wheel from games (HidHide) while this profile is in use, and give it back afterwards. false: make sure it is visible
    /// while this profile is in use. Left out: leave it as it is. Each change asks for administrator permission.
    /// </summary>
    public bool? HideWheel { get; set; }
    /// <summary>
    /// true: while this profile is in use the mapper lets go of the wheel completely (it opens no wheel and creates no virtual controller), so a
    /// game with native wheel support, force feedback included, has the wheel to itself. The profile's rules do nothing while it is on.
    /// </summary>
    public bool LeaveWheel { get; set; }
    /// <summary>
    /// With <see cref="LeaveWheel"/>: still read the wheel, without taking it over, and send the keyboard and mouse rules (a gear shifter that presses keys,
    /// say) while the game keeps the wheel and its force feedback. There is no virtual controller, so rules that send to it do nothing.
    /// </summary>
    public bool KeepKeys { get; set; }
    /// <summary>
    /// Keyboard keys and mouse buttons that switch this profile's modes while it is running (see <see cref="ModeShortcut"/>). Null: the app-wide
    /// list from Settings is used. An empty list: this profile has no shortcuts. Games bind different keys, so a profile can have its own.
    /// </summary>
    public List<ModeShortcut>? ModeShortcuts { get; set; }
    public List<Rule> Rules { get; set; } = new();
    /// <summary>Modes of this profile (see <see cref="ProfileMode"/>). Empty = the profile has just its own rules. A rule sending to "Mode:Next", "Mode:Previous" or "Mode:Name" switches between them.</summary>
    public List<ProfileMode> Modes { get; set; } = new();
}

public sealed record ProfileLoadResult(Profile? Profile, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool Ok => Profile is not null && Errors.Count == 0;
}

public static class ProfileLoader
{
    public static readonly string[] Curves = ["linear", "expo", "custom"];
    public static readonly string[] Modes = ["hold", "toggle", "pulse"];
    public static readonly string[] Parts = ["full", "positive", "negative"];

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    public static ProfileLoadResult LoadFile(string path, DeviceRegistry devices)
    {
        try { return Parse(File.ReadAllText(path), devices); }
        catch (IOException ex) { return Fail($"Could not read {path}: {ex.Message}"); }
    }

    public static ProfileLoadResult Parse(string json, DeviceRegistry devices)
    {
        Profile? profile;
        try { profile = JsonSerializer.Deserialize<Profile>(json, JsonOptions); }
        catch (JsonException ex)
        {
            string where = ex.LineNumber is long l ? $" (line {l + 1})" : "";
            return Fail($"Invalid JSON{where}: {ex.Message}");
        }
        if (profile is null) return Fail("Profile is empty.");
        if (profile.Parent is not null)
        {
            // A profile based on a template is read as the template copy it carries with its own differences laid over it.
            Profile? merged = Templates.Resolve(profile, json, out string? mergeError);
            if (merged is null) return Fail(mergeError!);
            profile = merged;
        }

        var errors = new List<string>();
        var warnings = new List<string>();
        Validate(profile, devices, errors, warnings);
        return new ProfileLoadResult(errors.Count == 0 ? profile : null, errors, warnings);
    }

    private static ProfileLoadResult Fail(string message) => new(null, [message], []);

    private static void Validate(Profile p, DeviceRegistry devices, List<string> errors, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(p.Name)) errors.Add("Profile needs a \"name\".");
        if (p.IsTemplate && p.Match.Exe.Count > 0) warnings.Add("A template is not attached to a game; the games listed in it are ignored. Put them on the profiles made from it.");

        if (p.Force is { } force)
        {
            if (force.Rumble is int rb and (< 0 or > 100)) errors.Add($"force.rumble must be 0 to 100 (not {rb}).");
            if (force.Spring is int sb and (< 0 or > 100)) errors.Add($"force.spring must be 0 to 100 (not {sb}).");
            if (force.Friction is int fb and (< 0 or > 100)) errors.Add($"force.friction must be 0 to 100 (not {fb}).");
            if (force.Motors is { } m && !ForceMath.MotorChoices.Contains(m, StringComparer.OrdinalIgnoreCase))
                errors.Add($"force.motors must be one of {string.Join(", ", ForceMath.MotorChoices)} (not \"{m}\").");
        }

        if (!p.Outputs.SecondPad && p.Rules.Any(r => r.To.TrimStart().StartsWith(Target.SecondPadPrefix, StringComparison.OrdinalIgnoreCase)))
            warnings.Add("Some rules send to the second controller (P2:) but outputs.secondPad is off, so they do nothing.");
        if (!PadKinds.TryParse(p.Outputs.Pad, out _))
            errors.Add($"outputs.pad must be \"xbox360\" or \"ds4\" (not \"{p.Outputs.Pad}\").");

        if (p.Safety is { } safety && !ProfileSafety.All.Contains(safety, StringComparer.OrdinalIgnoreCase))
            errors.Add($"safety must be \"strict\" or \"standard\" (not \"{safety}\").");
        else if (p.Outputs.Keyboard && ProfileSafety.KeyboardBlockedReason(p) is { } blocked)
            warnings.Add($"Keyboard output is switched on but will not be used: {blocked}." +
                         (ProfileSafety.Effective(p) == ProfileSafety.Strict ? " Set \"safety\": \"standard\" to allow it for a game without anti-cheat." : ""));
        if (p.Outputs.Turbo && ProfileSafety.TurboBlockedReason(p) is { } turboBlocked && p.Safety is null or "strict" or "standard")
            warnings.Add($"Turbo is switched on but will not be used: {turboBlocked}.");

        // "auto" is checked against the built-in layout here; at run time it becomes the connected wheel's own calibrated entry.
        DeviceMap? device = devices.Resolve(p.Device, null);
        if (device is null)
            errors.Add($"Unknown device \"{p.Device}\". Use \"{DeviceRegistry.AutoDevice}\" (whichever wheel is connected) or one of: {string.Join(", ", devices.Names)}.");

        ValidateModes(p, errors, warnings);
        if (p.ModeShortcuts is { } shortcuts)
            foreach (string problem in ModeShortcutList.Check(shortcuts).Problems) warnings.Add("modeShortcuts: " + problem);

        foreach (var (r, at) in AllRules(p))
        {

            if (device is not null && !devices.IsKnownInput(device, r.From))
                errors.Add($"{at}: unknown input \"{r.From}\"" + (r.From.Contains('.') ? " (is that extra device set up?)." : "."));

            if (!Target.TryParse(r.To, out Target? target, out string? targetError))
                errors.Add($"{at}: {targetError}");
            else if (target is { Kind: TargetKind.Mode } && !ProfileModes.IsKnownTarget(p, target.Key))
                errors.Add($"{at}: there is no mode called \"{target.Key}\"." + (p.Modes.Count == 0 ? " This profile has no modes yet." : $" Modes: {string.Join(", ", p.Modes.Select(m => m.Name))}; or Next, Previous."));

            if (!Curves.Contains(r.Curve, StringComparer.OrdinalIgnoreCase))
                errors.Add($"{at}: curve must be one of {string.Join(", ", Curves)}.");
            if (r.Exponent <= 0) errors.Add($"{at}: exponent must be greater than 0.");
            if (string.Equals(r.Curve, "custom", StringComparison.OrdinalIgnoreCase))
                ValidatePoints(r.Points, at, errors);

            if (r.Deadzone < 0 || r.Deadzone >= 1) errors.Add($"{at}: deadzone must be 0 or more and below 1 (that is 0% to 99%).");
            if (r.AntiDeadzone < 0 || r.AntiDeadzone >= 1) errors.Add($"{at}: antiDeadzone must be 0 or more and below 1 (that is 0% to 99%).");
            if (r.Threshold is double t && (t == 0 || t < -1 || t > 1)) errors.Add($"{at}: threshold must be from -1 to 1 and not 0 (that is -100% to 100%, not 0%).");
            if (!Modes.Contains(r.Mode, StringComparer.OrdinalIgnoreCase))
                errors.Add($"{at}: mode must be one of {string.Join(", ", Modes)}.");
            if (r.PulseMs < 1 || r.PulseMs > 1000) errors.Add($"{at}: pulseMs must be 1..1000.");

            if (!string.IsNullOrWhiteSpace(r.Expr))
            {
                if (!Expression.TryParse(r.Expr, out Expression? expr, out string? exprError)) errors.Add($"{at}: expression: {exprError}");
                else if (device is not null)
                    foreach (string n in expr!.Names.Where(n => !n.Equals("x", StringComparison.OrdinalIgnoreCase) && !devices.IsKnownInput(device, n)))
                        errors.Add($"{at}: expression uses unknown input \"{n}\". Use x for this rule's own input or the name of another input.");
            }
            if (r.Part is { } part && !Parts.Contains(part, StringComparer.OrdinalIgnoreCase))
                errors.Add($"{at}: part must be one of {string.Join(", ", Parts)}.");
            if (r.Smooth < 0 || r.Smooth > 1000) errors.Add($"{at}: smooth must be 0 to 1000 (milliseconds).");
            else if (r.Smooth > 0 && target is { Kind: not TargetKind.Axis })
                warnings.Add($"{at}: smooth only applies to stick and trigger outputs and is ignored here.");
            if (r.TurboHz < 1 || r.TurboHz > 30) errors.Add($"{at}: turboHz must be 1..30.");
            if (r.Turbo)
            {
                if (target is not null && target.Kind != TargetKind.Button)
                    warnings.Add($"{at}: turbo only applies to controller button targets and is ignored here.");
                else if (ProfileSafety.TurboBlockedReason(p) is { } turboWhy)
                    warnings.Add($"{at}: turbo is not active ({turboWhy}); this rule just holds the button.");
            }
            if (r.Range is double lockCheck && lockCheck > 0 && device is not null && !r.From.Contains('.')
                && devices.IsSigned(device, r.From) && lockCheck > device.RotationDegrees + 0.5)
                warnings.Add($"{at}: range {lockCheck:0.#}° is more than this wheel's total rotation ({device.RotationDegrees:0.#}°), so full stick can never be reached. Use {device.RotationDegrees:0.#} or less.");

            if (r.Range is double range)
            {
                if (range <= 0) errors.Add($"{at}: range must be greater than 0.");
                else if (device is not null && !devices.IsSigned(device, r.From)) warnings.Add($"{at}: range only applies to the wheel and is ignored here.");
            }

            if (r.AntiDeadzone > 0 && r.Deadzone == 0 && target is { Kind: TargetKind.Axis }
                && device is not null && devices.IsSigned(device, r.From))
                warnings.Add($"{at}: antiDeadzone with no deadzone turns tiny wheel noise or a slightly off centre into a constant push of at least {r.AntiDeadzone:P0}. Add a small deadzone (for example 4%).");

            if (target is { Kind: TargetKind.Key })
            {
                if (target.Key.StartsWith(KeyNames.MousePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (!KeyNames.IsMouse(target.Key))
                        errors.Add($"{at}: unknown mouse button \"{target.Key[KeyNames.MousePrefix.Length..]}\". Use Left, Right, Middle, X1 or X2 (for example Mouse:Left).");
                }
                else if (!KeyNames.TryGet(target.Key, out _))
                    errors.Add($"{at}: unknown key \"{target.Key}\". Try a letter, a digit, F1-F24, Space, Enter, Escape, Tab, Shift, Ctrl, Alt, an arrow, a number pad or punctuation key name (for example Key:Semicolon), or a mouse button (Mouse:Left).");
                else if (!p.Outputs.Keyboard)
                    warnings.Add($"{at}: keyboard output is disabled (outputs.keyboard is false); this rule does nothing.");
                else if (ProfileSafety.KeyboardBlockedReason(p) is { } why)
                    warnings.Add($"{at}: keyboard output is not active ({why}); this rule does nothing.");
            }
            if (target is { Kind: TargetKind.Axis } && !string.Equals(r.Mode, "hold", StringComparison.OrdinalIgnoreCase))
                warnings.Add($"{at}: mode only applies to button targets and is ignored here.");
        }
    }

    /// <summary>The profile's own rules and every mode's rules, each with the words used to name it in a message.</summary>
    private static IEnumerable<(Rule Rule, string At)> AllRules(Profile p)
    {
        for (int i = 0; i < p.Rules.Count; i++)
            yield return (p.Rules[i], $"Rule {i + 1} ({p.Rules[i].From} -> {p.Rules[i].To})");
        foreach (ProfileMode m in p.Modes)
            for (int i = 0; i < m.Rules.Count; i++)
                yield return (m.Rules[i], $"Mode \"{m.Name}\" rule {i + 1} ({m.Rules[i].From} -> {m.Rules[i].To})");
    }

    private static void ValidateModes(Profile p, List<string> errors, List<string> warnings)
    {
        if (p.Modes.Count == 0) return;
        if (p.Modes.Count > ProfileModes.MaxModes) errors.Add($"A profile can have at most {ProfileModes.MaxModes} modes (this one has {p.Modes.Count}).");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < p.Modes.Count; i++)
        {
            ProfileMode m = p.Modes[i];
            string who = string.IsNullOrWhiteSpace(m.Name) ? $"Mode {i + 1}" : $"Mode \"{m.Name}\"";
            if (string.IsNullOrWhiteSpace(m.Name)) { errors.Add($"{who} needs a name."); continue; }
            if (m.Name.Length > 30) errors.Add($"{who}: the name is too long (30 characters at most).");
            if (ProfileModes.IsReservedName(m.Name)) errors.Add($"{who}: that name is used for switching (Next, Previous); choose another.");
            if (m.Name.Contains(':')) errors.Add($"{who}: the name cannot contain a colon.");
            if (!seen.Add(m.Name.Trim())) errors.Add($"{who}: another mode has the same name.");
            if (m.Force is { } force)
            {
                if (force.Rumble is int rb and (< 0 or > 100)) errors.Add($"{who}: force.rumble must be 0 to 100 (not {rb}).");
                if (force.Spring is int sb and (< 0 or > 100)) errors.Add($"{who}: force.spring must be 0 to 100 (not {sb}).");
                if (force.Friction is int fb and (< 0 or > 100)) errors.Add($"{who}: force.friction must be 0 to 100 (not {fb}).");
                if (force.Motors is { } mo && !ForceMath.MotorChoices.Contains(mo, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"{who}: force.motors must be one of {string.Join(", ", ForceMath.MotorChoices)} (not \"{mo}\").");
            }
            foreach (string t in m.RemovedRules ?? [])
                if (!p.Rules.Any(r => string.Equals(r.To.Trim(), t.Trim(), StringComparison.OrdinalIgnoreCase)))
                    warnings.Add($"{who} turns off \"{t}\", but the profile has no rule for that output.");
        }
    }

    private static void ValidatePoints(double[][]? points, string at, List<string> errors)
    {
        if (points is null || points.Length < 2) { errors.Add($"{at}: custom curve needs at least 2 points."); return; }
        double prev = -1;
        foreach (double[] pt in points)
        {
            if (pt.Length != 2 || pt[0] < 0 || pt[0] > 1 || pt[1] < 0 || pt[1] > 1)
            {
                errors.Add($"{at}: each custom point must be [x, y] with both in 0..1.");
                return;
            }
            if (pt[0] <= prev) { errors.Add($"{at}: custom points must have strictly ascending x."); return; }
            prev = pt[0];
        }
    }
}

/// <summary>Axis and Button are the virtual controller; Key is a keyboard key or mouse button; Mode switches the profile's mode (Mode:Next, Mode:Previous, Mode:Name).</summary>
public enum TargetKind { Axis, Button, Key, Mode }

public sealed record Target(TargetKind Kind, PadAxis Axis = default, PadButton Button = default, string Key = "", int Pad = 1)
{
    /// <summary>The prefix that sends a rule to the second virtual controller: "P2:LeftStickX", "P2:Button:A".</summary>
    public const string SecondPadPrefix = "P2:";

    public static bool TryParse(string text, out Target? target, out string? error)
    {
        target = null; error = null;
        string trimmed = text.Trim();
        if (trimmed.StartsWith(SecondPadPrefix, StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("Pad2:", StringComparison.OrdinalIgnoreCase))
        {
            string rest = trimmed[(trimmed.IndexOf(':') + 1)..];
            if (!TryParseOne(rest, out Target? inner, out error)) return false;
            if (inner!.Kind == TargetKind.Key) { error = "keys and mouse buttons cannot go to the second controller."; return false; }
            if (inner.Kind == TargetKind.Mode) { error = "switching modes cannot go to the second controller."; return false; }
            target = inner with { Pad = 2 };
            return true;
        }
        return TryParseOne(text, out target, out error);
    }

    private static bool TryParseOne(string text, out Target? target, out string? error)
    {
        target = null; error = null;
        if (string.IsNullOrWhiteSpace(text)) { error = "\"to\" is empty."; return false; }

        if (text.StartsWith("Button:", StringComparison.OrdinalIgnoreCase))
        {
            // Enum.TryParse also accepts "3"; only names are valid here.
            if (!int.TryParse(text[7..], out _) && Enum.TryParse(text[7..], true, out PadButton b) && Enum.IsDefined(b))
            { target = new Target(TargetKind.Button, Button: b); return true; }
            error = $"unknown button \"{text[7..]}\". Known: {string.Join(", ", Enum.GetNames<PadButton>())}.";
            return false;
        }
        if (text.StartsWith(KeyNames.MousePrefix, StringComparison.OrdinalIgnoreCase))
        {
            // A mouse button is output the same way as a key, just under its own name ("Mouse:Left").
            string button = text[KeyNames.MousePrefix.Length..].Trim();
            if (button.Length == 0) { error = "\"Mouse:\" needs a button name (Left, Right, Middle, X1 or X2)."; return false; }
            target = new Target(TargetKind.Key, Key: KeyNames.MousePrefix + button);
            return true;
        }
        if (text.StartsWith("Mode:", StringComparison.OrdinalIgnoreCase))
        {
            string mode = text[5..].Trim();
            if (mode.Length == 0) { error = "\"Mode:\" needs Next, Previous or the name of a mode."; return false; }
            target = new Target(TargetKind.Mode, Key: mode);
            return true;
        }
        if (text.StartsWith("Key:", StringComparison.OrdinalIgnoreCase))
        {
            string key = text[4..].Trim();
            if (key.Length == 0) { error = "\"Key:\" needs a key name."; return false; }
            target = new Target(TargetKind.Key, Key: key);
            return true;
        }
        if (!int.TryParse(text, out _) && Enum.TryParse(text, true, out PadAxis a) && Enum.IsDefined(a))
        { target = new Target(TargetKind.Axis, Axis: a); return true; }

        error = $"unknown target \"{text}\". Use one of {string.Join(", ", Enum.GetNames<PadAxis>())}, Button:<name>, Key:<name> or Mode:<name>.";
        return false;
    }
}


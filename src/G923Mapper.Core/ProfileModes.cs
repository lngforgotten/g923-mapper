namespace G923Mapper;

/// <summary>
/// Modes inside one profile (Shooting, Driving, Flying...): the profile's own rules apply in every mode, and the active mode lays its own rules
/// and feel over them. A mode is read into an ordinary, fully merged <see cref="Profile"/>, so the engine and everything else see no difference; a
/// mode is edited the same way (change the merged profile, then <see cref="Capture"/> keeps only what differs from the profile's own).
/// Switching is always the user's doing (a wheel button, a shortcut, a click): the app never reads a game to find out what is going on in it.
/// </summary>
public static class ProfileModes
{
    public const int MaxModes = 8;

    public static bool IsNext(string name) => name.Trim().Equals("next", StringComparison.OrdinalIgnoreCase);
    public static bool IsPrevious(string name) =>
        name.Trim().Equals("previous", StringComparison.OrdinalIgnoreCase) || name.Trim().Equals("prev", StringComparison.OrdinalIgnoreCase);

    /// <summary>Names that mean "the next mode" and "the previous one", so a mode cannot be called that.</summary>
    public static bool IsReservedName(string name) => IsNext(name) || IsPrevious(name);

    /// <summary>Whether a rule sending to <c>Mode:name</c> means something in this profile: Next, Previous or one of its modes.</summary>
    public static bool IsKnownTarget(Profile p, string name) =>
        IsNext(name) || IsPrevious(name) || p.Modes.Any(m => string.Equals(m.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The name of the mode at this index, or null for no modes (or an index out of range).</summary>
    public static string? NameAt(Profile p, int index) => index >= 0 && index < p.Modes.Count ? p.Modes[index].Name : null;

    /// <summary>
    /// Which mode a request leads to. Next and Previous go round (after the last comes the first); a name finds that mode. Returns -1 when the
    /// profile has no modes or the name is not one of them.
    /// </summary>
    public static int Resolve(Profile p, string request, int current)
    {
        int n = p.Modes.Count;
        if (n == 0) return -1;
        if (IsNext(request)) return (Math.Clamp(current, 0, n - 1) + 1) % n;
        if (IsPrevious(request)) return (Math.Clamp(current, 0, n - 1) + n - 1) % n;
        return p.Modes.FindIndex(m => string.Equals(m.Name.Trim(), request.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The index to run after the profile changed (or reloaded): the mode with the same name if it is still there, else the first.</summary>
    public static int Keep(Profile p, string? previousName)
    {
        if (p.Modes.Count == 0) return -1;
        int at = previousName is null ? -1 : p.Modes.FindIndex(m => string.Equals(m.Name.Trim(), previousName.Trim(), StringComparison.OrdinalIgnoreCase));
        return at >= 0 ? at : 0;
    }

    /// <summary>
    /// The profile as it runs in one mode: its own rules with the mode's laid over them (a mode's rule replaces the profile's rules for the same
    /// output target, a mode can turn some off), and the feel with the mode's settings laid over it. A profile without modes is returned as it is.
    /// The result has no modes of its own and is a copy that can be changed freely.
    /// </summary>
    public static Profile Resolve(Profile p, int index)
    {
        if (p.Modes.Count == 0 || index < 0 || index >= p.Modes.Count) return p;
        Profile r = Templates.CloneRaw(p);
        r.Modes = new List<ProfileMode>();
        ProfileMode m = p.Modes[index];

        var own = m.Rules.GroupBy(Templates.TargetKey).ToDictionary(g => g.Key, g => g.ToList());
        var removed = new HashSet<string>((m.RemovedRules ?? []).Select(t => t.Trim().ToLowerInvariant()));
        var merged = new List<Rule>();
        var placed = new HashSet<string>();
        foreach (Rule baseRule in r.Rules)
        {
            string key = Templates.TargetKey(baseRule);
            if (own.TryGetValue(key, out var replacement)) { if (placed.Add(key)) merged.AddRange(replacement); }
            else if (!removed.Contains(key)) merged.Add(baseRule);
        }
        foreach (var (key, rules) in own)
            if (placed.Add(key)) merged.AddRange(rules);
        r.Rules = merged;

        r.Force = MergeForce(r.Force, m.Force);
        return r;
    }

    /// <summary>The feel of a mode: the profile's, with whatever the mode sets laid over it. Null when neither sets anything.</summary>
    public static ProfileForce? MergeForce(ProfileForce? profile, ProfileForce? mode)
    {
        if (mode is null || mode.IsEmpty) return profile;
        var f = new ProfileForce
        {
            Rumble = mode.Rumble ?? profile?.Rumble,
            Spring = mode.Spring ?? profile?.Spring,
            Friction = mode.Friction ?? profile?.Friction,
            Motors = mode.Motors ?? profile?.Motors,
            CentringStyle = mode.CentringStyle ?? profile?.CentringStyle,
            CentreDegrees = mode.CentreDegrees ?? profile?.CentreDegrees,
            PeakDegrees = mode.PeakDegrees ?? profile?.PeakDegrees,
            LighteningPercent = mode.LighteningPercent ?? profile?.LighteningPercent,
            DampingPercent = mode.DampingPercent ?? profile?.DampingPercent,
        };
        return f.IsEmpty ? null : f;
    }

    /// <summary>
    /// Keep a change made to the merged profile of one mode: the mode's rules become the ones that differ from the profile's own, the targets it
    /// no longer has become its removed rules, and its feel becomes whatever differs from the profile's. The profile's own rules are not touched.
    /// </summary>
    /// <param name="edited">The profile as it was after <see cref="Resolve(Profile, int)"/> and then changed.</param>
    public static void Capture(Profile p, int index, Profile edited)
    {
        if (index < 0 || index >= p.Modes.Count) return;
        ProfileMode m = p.Modes[index];
        var baseGroups = Templates.Groups(p);
        var editedGroups = Templates.Groups(edited);

        m.Rules = edited.Rules
            .Where(r => { string k = Templates.TargetKey(r); return !baseGroups.TryGetValue(k, out var original) || !Templates.SameGroup(original, editedGroups[k]); })
            .ToList();
        var removed = baseGroups.Where(kv => !editedGroups.ContainsKey(kv.Key)).Select(kv => kv.Value[0].To.Trim()).ToList();
        m.RemovedRules = removed.Count == 0 ? null : removed;

        var force = new ProfileForce();
        if (edited.Force?.Rumble != p.Force?.Rumble) force.Rumble = edited.Force?.Rumble;
        if (edited.Force?.Spring != p.Force?.Spring) force.Spring = edited.Force?.Spring;
        if (edited.Force?.Friction != p.Force?.Friction) force.Friction = edited.Force?.Friction;
        if (!string.Equals(edited.Force?.Motors, p.Force?.Motors, StringComparison.OrdinalIgnoreCase)) force.Motors = edited.Force?.Motors;
        if (!string.Equals(edited.Force?.CentringStyle, p.Force?.CentringStyle, StringComparison.OrdinalIgnoreCase)) force.CentringStyle = edited.Force?.CentringStyle;
        if (edited.Force?.CentreDegrees != p.Force?.CentreDegrees) force.CentreDegrees = edited.Force?.CentreDegrees;
        if (edited.Force?.PeakDegrees != p.Force?.PeakDegrees) force.PeakDegrees = edited.Force?.PeakDegrees;
        if (edited.Force?.LighteningPercent != p.Force?.LighteningPercent) force.LighteningPercent = edited.Force?.LighteningPercent;
        if (edited.Force?.DampingPercent != p.Force?.DampingPercent) force.DampingPercent = edited.Force?.DampingPercent;
        m.Force = force.IsEmpty ? null : force;
    }

    /// <summary>A mode with this name, starting with nothing of its own (it behaves exactly like the profile until it is given rules).</summary>
    public static ProfileMode Add(Profile p, string name)
    {
        var mode = new ProfileMode { Name = name.Trim() };
        p.Modes.Add(mode);
        return mode;
    }

    /// <summary>A name for a new mode that no other mode has: "Mode 2", "Mode 3"...</summary>
    public static string FreeName(Profile p, string wanted)
    {
        string name = string.IsNullOrWhiteSpace(wanted) ? "Mode" : wanted.Trim();
        if (!p.Modes.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) && !IsReservedName(name)) return name;
        for (int n = 2; ; n++)
        {
            string candidate = $"{name} {n}";
            if (!p.Modes.Any(m => string.Equals(m.Name, candidate, StringComparison.OrdinalIgnoreCase))) return candidate;
        }
    }
}

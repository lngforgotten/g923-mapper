namespace G923Mapper;

/// <summary>Chooses which copy of the app this is, when more than one may run (one per wheel).</summary>
public static class InstancePlan
{
    public const int MaxInstances = 4;

    /// <summary>
    /// The number (1, 2, ...) this copy should run as, or null if it should not run.
    /// With multiple copies off only copy 1 exists, so a second launch gets null (the caller brings copy 1 to the front).
    /// With them on, a launch takes the first free number. An explicit request is honoured only if that number is free.
    /// </summary>
    /// <param name="tryClaim">Atomically takes a number if it is free (an OS mutex in the real app); true on success.</param>
    public static int? Choose(bool allowMultiple, int? requested, Func<int, bool> tryClaim)
    {
        if (requested is int r)
        {
            if (r < 1 || r > MaxInstances) return null;
            if (r > 1 && !allowMultiple) return null;
            return tryClaim(r) ? r : null;
        }
        int last = allowMultiple ? MaxInstances : 1;
        for (int n = 1; n <= last; n++)
            if (tryClaim(n)) return n;
        return null;
    }

    /// <summary>Settings and log file names are shared by copy 1 (as before) and separate for the others.</summary>
    public static string SettingsFileName(int instance) => instance <= 1 ? "settings.json" : $"settings-{instance}.json";

    public static string LogName(int instance) => instance <= 1 ? "g923mapper" : $"g923mapper-{instance}";

    /// <summary>Reads "--instance N" from the command line.</summary>
    public static int? ParseArgument(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count - 1; i++)
            if (args[i].Equals("--instance", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out int n) && n >= 1)
                return n;
        return null;
    }
}

/// <summary>Chooses which attached wheel a copy of the app drives.</summary>
public static class WheelPicker
{
    public sealed record Candidate(Guid Id, int Vid, int Pid, string Name);

    /// <summary>
    /// The wheel at position <paramref name="index"/> (0 = the first). Candidates are the controllers matching the
    /// preferred USB ids; failing that, any with ids the device registry knows; failing that, anything named G923. They
    /// are ordered by their instance id so each copy of the app gets the same wheel every time. Null when there are
    /// fewer wheels than <paramref name="index"/> + 1 (that copy then waits for its wheel).
    /// </summary>
    public static Guid? Pick(IReadOnlyList<Candidate> attached, (int Vid, int Pid)? preferred, Func<int, int, bool> isRegistered, int index)
    {
        if (index < 0) return null;
        IEnumerable<Candidate> chosen = Enumerable.Empty<Candidate>();
        if (preferred is { } want)
            chosen = attached.Where(c => c.Vid == want.Vid && c.Pid == want.Pid);
        if (!chosen.Any()) chosen = attached.Where(c => isRegistered(c.Vid, c.Pid));
        if (!chosen.Any()) chosen = attached.Where(c => c.Name.Contains("G923", StringComparison.OrdinalIgnoreCase));

        var ordered = chosen.OrderBy(c => c.Id.ToString(), StringComparer.OrdinalIgnoreCase).ToList();
        return index < ordered.Count ? ordered[index].Id : null;
    }
}

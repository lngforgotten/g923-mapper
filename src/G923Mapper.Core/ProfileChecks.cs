namespace G923Mapper;

/// <summary>The words on the rule editor's Checks button, in the same style as the health badge (<see cref="HealthEvaluator.Summary"/>).</summary>
public static class ProfileChecks
{
    /// <summary>Problems stop a profile being saved; warnings do not. As on the health badge, only the more serious kind is counted when there are both.</summary>
    public static string Summary(int problems, int warnings)
    {
        if (problems > 0) return problems == 1 ? "1 problem" : $"{problems} problems";
        if (warnings > 0) return warnings == 1 ? "1 to check" : $"{warnings} to check";
        return "all good";
    }
}

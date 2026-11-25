namespace G923Mapper;

/// <summary>A game that drives a Logitech wheel itself, with its own force feedback and often its own rev lights.</summary>
public sealed record NativeWheelGame(string Id, string Name, string[] Exes);

/// <summary>
/// Games with native wheel support. Run through the app's virtual controller they work, but the app holding the wheel (it takes it exclusively
/// for force feedback) can get in the way of the game's own force feedback and rev lights. For these, a profile that leaves the wheel alone is
/// the right one, so the app offers ready-made ones and says so once when one of these games runs under a profile that holds the wheel.
/// Assetto Corsa is the one confirmed on a real wheel; the program names of the others are the ones the games ship with and can be corrected
/// in Edit rules, Games, if a game does not switch its profile on.
/// </summary>
public static class NativeWheelGames
{
    public static readonly IReadOnlyList<NativeWheelGame> All =
    [
        new("assetto-corsa", "Assetto Corsa", ["acs.exe"]),
        new("assetto-corsa-competizione", "Assetto Corsa Competizione", ["AC2-Win64-Shipping.exe"]),
        new("iracing", "iRacing", ["iRacingSim64DX11.exe"]),
        new("rfactor-2", "rFactor 2", ["rFactor2.exe"]),
        new("beamng", "BeamNG.drive", ["BeamNG.drive.x64.exe"]),
        new("automobilista-2", "Automobilista 2", ["AMS2AVX.exe", "AMS2.exe"]),
    ];

    /// <summary>The profile (as JSON) that leaves the wheel alone for this game and claims its program, so it switches on by itself.</summary>
    public static string ProfileJson(NativeWheelGame game) =>
        "{ \"name\": \"" + game.Name.Replace("\"", "") + "\", \"match\": { \"exe\": [" + string.Join(", ", game.Exes.Select(e => "\"" + e + "\"")) + "] }, "
        + "\"device\": \"auto\", \"safety\": \"strict\", \"outputs\": { \"keyboard\": false }, \"leaveWheel\": true, \"rules\": [] }";

    /// <summary>The games from the list that are among these running programs (names with or without .exe, any case).</summary>
    public static IReadOnlyList<NativeWheelGame> Running(IEnumerable<string> runningPrograms)
    {
        var names = new HashSet<string>(runningPrograms.Select(ProfileSelector.Normalize), StringComparer.OrdinalIgnoreCase);
        return All.Where(g => g.Exes.Any(e => names.Contains(ProfileSelector.Normalize(e)))).ToList();
    }

    /// <summary>Whether some profile already leaves the wheel alone for this game, so there is nothing to suggest.</summary>
    public static bool IsCovered(NativeWheelGame game, IEnumerable<Profile> profiles) =>
        profiles.Any(p => !p.IsTemplate && p.LeaveWheel && p.Match.Exe.Any(e => game.Exes.Any(g => ProfileSelector.Normalize(g) == ProfileSelector.Normalize(e))));

    /// <summary>
    /// The games to tell the person about now: running, not already covered by a profile that leaves the wheel alone, not told about already,
    /// and only while the profile in use holds the wheel (a profile that leaves it alone is the right answer already).
    /// </summary>
    public static IReadOnlyList<NativeWheelGame> ToWarnAbout(IEnumerable<string> runningPrograms, IEnumerable<Profile> profiles, Profile? profileInUse, ISet<string> alreadyTold)
    {
        if (profileInUse is null || profileInUse.LeaveWheel) return [];
        var all = profiles.ToList();
        return Running(runningPrograms).Where(g => !alreadyTold.Contains(g.Id) && !IsCovered(g, all)).ToList();
    }

    /// <summary>The wording of the notice for one game.</summary>
    public static (string Title, string Text) Notice(NativeWheelGame game) =>
        ($"{game.Name} has its own wheel support",
         "The mapper is holding the wheel, which can stop the game's own force feedback and rev lights. New profile, Game: " + game.Name + ", wheel left to the game, makes a profile that lets go of the wheel while it runs.");
}

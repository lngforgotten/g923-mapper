namespace G923Mapper;

/// <summary>
/// Decides when a profile should hide or show the real wheel (HidHide). A profile with <c>"hideWheel": true</c> hides the wheel while it is
/// in use and gives it back afterwards; <c>false</c> makes sure it is visible while the profile is in use; leaving it out changes nothing
/// (except that a wheel this feature itself hid is shown again once that profile is no longer active).
/// Every change asks for administrator permission, so it only acts when a profile becomes active, and never retries the same profile.
/// </summary>
public static class WheelVisibility
{
    /// <summary>
    /// What a profile's <c>hideWheel</c> means right now: only when the profile is in use because its game was detected. A profile that is merely
    /// the one chosen by hand (or remembered from last time) has no say, so opening the app never hides the wheel by itself.
    /// </summary>
    public static bool? WantedFor(bool? profileSetting, bool gameDetected) => gameDetected ? profileSetting : null;

    /// <summary>True when one of the profile's games (its match.exe list) is among the running programs, in front or not.</summary>
    public static bool GameRunning(Profile? profile, IEnumerable<string> running)
    {
        if (profile is null || profile.Match.Exe.Count == 0) return false;
        var names = new HashSet<string>(running.Select(ProfileSelector.Normalize), StringComparer.OrdinalIgnoreCase);
        return profile.Match.Exe.Any(e => names.Contains(ProfileSelector.Normalize(e)));
    }

    /// <param name="wanted">The active profile's <c>hideWheel</c> (null = no opinion).</param>
    /// <param name="hidHideInstalled">HidHide can be driven.</param>
    /// <param name="currentlyHidden">Whether the wheel is hidden right now.</param>
    /// <param name="hiddenByProfile">Whether this feature hid it earlier (so it should be restored later).</param>
    /// <returns>true = hide it now, false = show it now, null = nothing to do.</returns>
    public static bool? Decide(bool? wanted, bool hidHideInstalled, bool currentlyHidden, bool hiddenByProfile)
    {
        if (!hidHideInstalled) return null;
        return wanted switch
        {
            true => currentlyHidden ? null : true,
            false => currentlyHidden ? false : null,
            _ => hiddenByProfile && currentlyHidden ? false : null,   // restore what a profile changed, once it no longer applies
        };
    }
}

/// <summary>Remembers what the feature has done, so one profile is never retried (a declined permission prompt must not repeat).</summary>
public sealed class WheelVisibilityTracker
{
    private string? _lastKey;

    /// <summary>True once the feature itself hid the wheel (and so should show it again later).</summary>
    public bool HiddenByProfile { get; private set; }

    /// <summary>
    /// A profile became active. Returns what to do (true = hide, false = show, null = nothing), or null without asking again when this profile
    /// has already been handled.
    /// </summary>
    public bool? OnProfileActive(string key, bool? wanted, bool hidHideInstalled, bool currentlyHidden)
    {
        if (string.Equals(key, _lastKey, StringComparison.OrdinalIgnoreCase)) return null;
        _lastKey = key;
        return WheelVisibility.Decide(wanted, hidHideInstalled, currentlyHidden, HiddenByProfile);
    }

    /// <summary>The profile whose game this feature hid the wheel for (null when it has not).</summary>
    public string? HiddenFor { get; private set; }

    /// <summary>Record the outcome of a hide or show that was carried out; <paramref name="key"/> is the profile a hide was for.</summary>
    public void Done(bool hid, bool success, string? key = null)
    {
        if (!success) return;
        HiddenByProfile = hid;   // showing it again clears the flag
        HiddenFor = hid ? key : null;
        _restoreTried = false;
    }

    private bool _restoreTried;

    /// <summary>Note that giving the wheel back has been started, so nothing else asks again (a declined permission prompt must not repeat).</summary>
    public void MarkRestoreTried() => _restoreTried = true;

    /// <summary>
    /// The wheel was hidden for a game: give it back now only when that game has ended. A game that merely lost focus, or went behind another
    /// window, keeps the wheel hidden, so there is no permission prompt every time it is switched away from. Asks once.
    /// </summary>
    public bool ShouldRestore(bool wheelHidden, bool gameStillRunning) => HiddenByProfile && wheelHidden && !gameStillRunning && !_restoreTried;

    /// <summary>The wheel was hidden for a game that is still running, so another profile coming to the front must not give it back.</summary>
    public bool KeepHidden(bool wheelHidden, bool gameStillRunning) => HiddenByProfile && wheelHidden && gameStillRunning;
}

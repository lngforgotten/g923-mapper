using G923Mapper;

namespace G923Mapper.Tests;

public class FocusSwitchTests
{
    private static Profile Game(string name, params string[] exes) => new() { Name = name, Match = new ProfileMatch { Exe = exes.ToList() } };

    private sealed class Harness
    {
        public string? Front;                        // the program whose window is in front
        public List<string> Running = new();         // everything that is running (focus mode ignores this)
        public long Now;                             // fake clock, milliseconds
        public readonly List<(string?, string?)> Events = new();
        public readonly AutoProfileSwitcher Switcher;

        public Harness(AutoSwitchMode mode = AutoSwitchMode.WhenFocused)
        {
            Switcher = new AutoProfileSwitcher(
                () => [("gta-v", Game("GTA V", "GTA5.exe", "GTA5_Enhanced.exe")), ("forza", Game("Forza", "ForzaHorizon5.exe")), ("default", new Profile { Name = "default" })],
                () => Running.ToList(),
                interval: TimeSpan.FromHours(1),     // never fires on its own; tests call Poll()
                foreground: () => Front,
                clockMs: () => Now,
                lostDelay: TimeSpan.FromMilliseconds(1500)) { Mode = mode };
            Switcher.Changed += (k, e) => Events.Add((k, e));
        }

        public void At(long ms, string? front) { Now = ms; Front = front; Switcher.Poll(); }
    }

    [Fact]
    public void The_game_coming_to_the_front_switches_at_once_and_a_browser_does_not()
    {
        var h = new Harness();
        h.At(0, "chrome.exe");
        Assert.Empty(h.Events);
        h.At(1000, "GTA5_Enhanced.exe");
        Assert.Equal([("gta-v", "GTA5_Enhanced.exe")], h.Events);
        Assert.Equal("gta-v", h.Switcher.ActiveKey);
    }

    [Fact]
    public void A_game_that_is_running_but_in_the_background_does_not_count_in_focus_mode()
    {
        var h = new Harness();
        h.Running.Add("GTA5_Enhanced.exe");
        h.At(0, "chrome.exe");
        Assert.Null(h.Switcher.ActiveKey);
        Assert.Empty(h.Events);
    }

    [Fact]
    public void Leaving_the_game_reverts_only_after_the_delay()
    {
        var h = new Harness();
        h.At(0, "GTA5.exe");
        h.At(1000, "chrome.exe");                    // just left
        Assert.Equal("gta-v", h.Switcher.ActiveKey);   // not yet
        h.At(2000, "chrome.exe");                    // 1.0 s of browsing: still not
        Assert.Equal("gta-v", h.Switcher.ActiveKey);
        h.At(2600, "chrome.exe");                    // 1.6 s: now it reverts
        Assert.Null(h.Switcher.ActiveKey);
        Assert.Equal((null, null), h.Events[^1]);
    }

    [Fact]
    public void A_brief_overlay_or_popup_in_front_causes_no_flicker()
    {
        var h = new Harness();
        h.At(0, "GTA5.exe");
        h.At(1000, "steamwebhelper.exe");            // an overlay for half a second
        h.At(1500, "GTA5.exe");                      // back to the game
        h.At(4000, "GTA5.exe");
        Assert.Single(h.Events);                       // only the initial switch on; never reverted
        Assert.Equal("gta-v", h.Switcher.ActiveKey);
    }

    [Fact]
    public void The_delay_restarts_after_a_return_to_the_game()
    {
        var h = new Harness();
        h.At(0, "GTA5.exe");
        h.At(1000, "chrome.exe");
        h.At(2000, "GTA5.exe");                      // came back: the "lost" timer must reset
        h.At(2500, "chrome.exe");
        h.At(3500, "chrome.exe");                    // only 1.0 s since leaving again, not 2.5 s
        Assert.Equal("gta-v", h.Switcher.ActiveKey);
    }

    [Theory]
    [InlineData("G923Mapper.exe")]
    [InlineData("explorer.exe")]
    [InlineData("ShellExperienceHost.exe")]
    [InlineData("SearchHost.exe")]
    [InlineData("StartMenuExperienceHost.exe")]
    [InlineData("ApplicationFrameHost.exe")]
    public void This_app_and_the_Windows_shell_in_front_never_change_the_profile(string shellProgram)
    {
        var h = new Harness();
        h.Running.Add("GTA5.exe");
        h.At(0, "GTA5.exe");
        h.At(5000, shellProgram);                    // far longer than the delay
        h.At(20000, shellProgram);
        Assert.Equal("gta-v", h.Switcher.ActiveKey);   // opening the settings window mid-game does nothing
        Assert.Single(h.Events);
    }

    [Fact]
    public void No_foreground_window_at_all_is_not_a_decision()
    {
        var h = new Harness();
        h.Running.Add("GTA5.exe");
        h.At(0, "GTA5.exe");
        h.At(9000, null);
        Assert.Equal("gta-v", h.Switcher.ActiveKey);
    }

    [Fact]
    public void With_two_games_running_the_one_in_front_wins_and_follows_alt_tab()
    {
        var h = new Harness();
        h.Running.AddRange(["GTA5.exe", "ForzaHorizon5.exe"]);
        h.At(0, "GTA5.exe");
        Assert.Equal("gta-v", h.Switcher.ActiveKey);
        h.At(1000, "ForzaHorizon5.exe");             // straight from one game to the other: no delay needed
        Assert.Equal("forza", h.Switcher.ActiveKey);
        h.At(2000, "GTA5.exe");
        Assert.Equal("gta-v", h.Switcher.ActiveKey);
    }

    [Fact]
    public void Process_names_match_with_or_without_exe_and_any_case()
    {
        var h = new Harness();
        h.At(0, "gta5_enhanced");
        Assert.Equal("gta-v", h.Switcher.ActiveKey);
    }

    [Fact]
    public void Switching_modes_changes_what_counts_and_resets_the_delay()
    {
        var h = new Harness(AutoSwitchMode.WhenRunning);
        h.Running.Add("GTA5.exe");
        h.At(0, "chrome.exe");
        Assert.Equal("gta-v", h.Switcher.ActiveKey);   // running mode: counts even though it is in the background

        h.Switcher.Mode = AutoSwitchMode.WhenFocused;
        h.At(100, "chrome.exe");                       // focus mode: chrome in front, but the revert waits for the delay
        Assert.Equal("gta-v", h.Switcher.ActiveKey);
        h.At(1800, "chrome.exe");
        Assert.Null(h.Switcher.ActiveKey);
    }

    [Fact]
    public void Turning_switching_off_reverts_at_once_in_focus_mode_too()
    {
        var h = new Harness();
        h.At(0, "GTA5.exe");
        h.Switcher.Enabled = false;
        h.At(10, "GTA5.exe");
        Assert.Null(h.Switcher.ActiveKey);
    }

    [Fact]
    public void Running_mode_is_unchanged_and_ignores_what_is_in_front()
    {
        var h = new Harness(AutoSwitchMode.WhenRunning);
        h.Running.Add("GTA5.exe");
        h.At(0, null);
        Assert.Equal("gta-v", h.Switcher.ActiveKey);
        h.Running.Clear();
        h.At(1, "GTA5.exe");                           // in front but not in the running list: running mode trusts the list
        Assert.Null(h.Switcher.ActiveKey);
    }

    [Fact]
    public void When_the_game_closes_and_only_the_desktop_or_this_app_is_in_front_the_profile_reverts()
    {
        var h = new Harness();
        h.Running.Add("GTA5.exe");
        h.At(0, "GTA5.exe");
        Assert.Equal("gta-v", h.Switcher.ActiveKey);

        h.Running.Clear();                    // the game closes
        h.At(1000, "explorer.exe");           // with the desktop (or this app, or no window) now in front
        Assert.Null(h.Switcher.ActiveKey);
        Assert.Equal((null, null), h.Events[^1]);
    }

    [Theory]
    [InlineData("G923Mapper.exe")]
    [InlineData("explorer.exe")]
    [InlineData(null)]
    public void A_neutral_window_in_front_alone_never_reverts_while_the_game_is_still_running(string? front)
    {
        var h = new Harness();
        h.Running.Add("GTA5.exe");
        h.At(0, "GTA5.exe");
        h.At(60000, front);
        Assert.Equal("gta-v", h.Switcher.ActiveKey);
    }
}
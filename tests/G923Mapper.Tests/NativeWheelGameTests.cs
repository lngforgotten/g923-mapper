using G923Mapper;

namespace G923Mapper.Tests;

public class NativeWheelGameTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();

    private static Profile P(string json) => ProfileLoader.Parse(json, Devices).Profile!;

    private static Profile Holding(string name = "wardogs") => P($$"""{ "name": "{{name}}", "rules": [] }""");

    [Fact]
    public void Every_game_has_a_name_a_program_and_a_unique_id()
    {
        Assert.True(NativeWheelGames.All.Count >= 6);
        Assert.Equal(NativeWheelGames.All.Count, NativeWheelGames.All.Select(g => g.Id).Distinct().Count());
        foreach (var g in NativeWheelGames.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(g.Name));
            Assert.NotEmpty(g.Exes);
            Assert.All(g.Exes, e => Assert.EndsWith(".exe", e, StringComparison.OrdinalIgnoreCase));
        }
        Assert.Contains(NativeWheelGames.All, g => g.Exes.Contains("acs.exe"));      // Assetto Corsa: confirmed on a real wheel
    }

    [Fact]
    public void Each_games_profile_loads_cleanly_and_leaves_the_wheel_alone_for_that_game()
    {
        foreach (var g in NativeWheelGames.All)
        {
            var r = ProfileLoader.Parse(NativeWheelGames.ProfileJson(g), Devices);
            Assert.True(r.Ok, g.Name + ": " + string.Join("; ", r.Errors));
            Assert.Empty(r.Warnings);
            Assert.True(r.Profile!.LeaveWheel, g.Name);
            Assert.False(r.Profile.KeepKeys);
            Assert.Equal(g.Name, r.Profile.Name);
            Assert.Equal(g.Exes, r.Profile.Match.Exe);
            Assert.Equal(ProfileSafety.Strict, ProfileSafety.Effective(r.Profile));
        }
    }

    [Fact]
    public void The_games_are_in_the_new_profile_list_as_ready_made_profiles()
    {
        foreach (var g in NativeWheelGames.All)
        {
            var preset = Presets.Gallery.Single(p => p.Id == "sim-" + g.Id);
            Assert.Contains(g.Name, preset.Title);
            Assert.Contains("wheel left to the game", preset.Title);
            var made = Presets.Instantiate(preset, "My " + g.Name, Devices)!;
            Assert.True(made.LeaveWheel);
            Assert.Equal("My " + g.Name, made.Name);
        }
        Assert.Contains(Presets.Gallery, p => p.Id == "game-wardogs-lean");              // the earlier game is still there
        Assert.Equal(Presets.Gallery.Count, Presets.Gallery.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void A_running_game_is_found_whatever_the_case_or_extension()
    {
        var found = NativeWheelGames.Running(["chrome.exe", "ACS.EXE", "explorer"]);
        Assert.Single(found);
        Assert.Equal("assetto-corsa", found[0].Id);
        Assert.Single(NativeWheelGames.Running(["acs"]));
        Assert.Empty(NativeWheelGames.Running(["notepad.exe"]));
        Assert.Empty(NativeWheelGames.Running([]));
    }

    [Fact]
    public void A_game_is_covered_only_by_a_profile_that_leaves_the_wheel_alone_for_its_program()
    {
        var ac = NativeWheelGames.All.Single(g => g.Id == "assetto-corsa");
        var leaves = P("""{ "name": "AC", "match": { "exe": ["acs.exe"] }, "leaveWheel": true, "rules": [] }""");
        var holds = P("""{ "name": "AC old", "match": { "exe": ["acs.exe"] }, "rules": [] }""");
        var other = P("""{ "name": "Other", "match": { "exe": ["other.exe"] }, "leaveWheel": true, "rules": [] }""");
        Assert.True(NativeWheelGames.IsCovered(ac, [leaves]));
        Assert.False(NativeWheelGames.IsCovered(ac, [holds]));
        Assert.False(NativeWheelGames.IsCovered(ac, [other]));
        Assert.False(NativeWheelGames.IsCovered(ac, []));
    }

    [Fact]
    public void The_person_is_told_once_per_game_while_a_profile_that_holds_the_wheel_is_in_use()
    {
        var running = new[] { "acs.exe", "chrome.exe" };
        var told = new HashSet<string>();

        var first = NativeWheelGames.ToWarnAbout(running, [Holding()], Holding(), told);
        Assert.Single(first);
        Assert.Equal("assetto-corsa", first[0].Id);

        told.Add(first[0].Id);
        Assert.Empty(NativeWheelGames.ToWarnAbout(running, [Holding()], Holding(), told));      // not again
    }

    [Fact]
    public void Nothing_is_said_when_the_profile_in_use_already_lets_go_of_the_wheel_or_no_profile_runs()
    {
        var leaves = P("""{ "name": "AC", "match": { "exe": ["acs.exe"] }, "leaveWheel": true, "rules": [] }""");
        var keys = P("""{ "name": "AC keys", "match": { "exe": ["acs.exe"] }, "leaveWheel": true, "keepKeys": true, "rules": [] }""");
        Assert.Empty(NativeWheelGames.ToWarnAbout(["acs.exe"], [leaves], leaves, new HashSet<string>()));
        Assert.Empty(NativeWheelGames.ToWarnAbout(["acs.exe"], [keys], keys, new HashSet<string>()));
        Assert.Empty(NativeWheelGames.ToWarnAbout(["acs.exe"], [Holding()], null, new HashSet<string>()));
    }

    [Fact]
    public void Nothing_is_said_when_a_profile_for_the_game_already_leaves_the_wheel_alone()
    {
        // The profile that holds the wheel is the one in use (auto-switching is off, say), but the person has already made the right one.
        var leaves = P("""{ "name": "AC", "match": { "exe": ["acs.exe"] }, "leaveWheel": true, "rules": [] }""");
        Assert.Empty(NativeWheelGames.ToWarnAbout(["acs.exe"], [Holding(), leaves], Holding(), new HashSet<string>()));
    }

    [Fact]
    public void The_notice_names_the_game_and_says_what_to_do()
    {
        var (title, text) = NativeWheelGames.Notice(NativeWheelGames.All[0]);
        Assert.Contains("Assetto Corsa", title);
        Assert.Contains("own wheel support", title);
        Assert.Contains("wheel left to the game", text);
        Assert.Contains("force feedback", text);
    }
}

public class ProfileModeShortcutTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();

    [Fact]
    public void A_profile_without_shortcuts_uses_the_app_wide_ones()
    {
        var p = ProfileLoader.Parse("""{ "name": "t", "rules": [] }""", Devices).Profile!;
        Assert.Null(p.ModeShortcuts);
        Assert.DoesNotContain("modeShortcuts", ProfileWriter.ToJson(p));
    }

    [Fact]
    public void A_profile_can_have_its_own_shortcuts_and_they_survive_saving()
    {
        var p = ProfileLoader.Parse("""{ "name": "t", "modeShortcuts": [ { "trigger": "F8", "action": "Next" }, { "trigger": "Mouse:Back", "action": "Previous" }, { "trigger": "Ctrl+F6", "action": "Driving" } ], "rules": [] }""", Devices).Profile!;
        Assert.Equal(3, p.ModeShortcuts!.Count);
        string json = ProfileWriter.ToJson(p);
        var back = ProfileLoader.Parse(json, Devices).Profile!;
        Assert.Equal(["F8", "Mouse:Back", "Ctrl+F6"], back.ModeShortcuts!.Select(m => m.Trigger).ToArray());
        Assert.Equal("Driving", back.ModeShortcuts![2].Action);
    }

    [Fact]
    public void An_empty_list_means_this_profile_has_no_shortcuts_which_is_not_the_same_as_none_set()
    {
        var p = ProfileLoader.Parse("""{ "name": "t", "modeShortcuts": [], "rules": [] }""", Devices).Profile!;
        Assert.NotNull(p.ModeShortcuts);
        Assert.Empty(p.ModeShortcuts!);
        var back = ProfileLoader.Parse(ProfileWriter.ToJson(p), Devices).Profile!;
        Assert.NotNull(back.ModeShortcuts);
        Assert.Empty(back.ModeShortcuts!);
    }

    [Fact]
    public void A_shortcut_that_cannot_be_used_is_a_warning_not_an_error_so_the_profile_still_loads()
    {
        var r = ProfileLoader.Parse("""{ "name": "t", "modeShortcuts": [ { "trigger": "Q", "action": "Next" }, { "trigger": "F8", "action": "Next" }, { "trigger": "f8", "action": "Previous" } ], "rules": [] }""", Devices);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Contains(r.Warnings, w => w.Contains("modeShortcuts") && w.Contains("needs Ctrl"));
        Assert.Contains(r.Warnings, w => w.Contains("more than once"));
    }

    [Fact]
    public void Shortcuts_are_kept_in_the_copy_of_a_profile_for_a_mode()
    {
        var p = ProfileLoader.Parse("""{ "name": "t", "modeShortcuts": [ { "trigger": "F8", "action": "Next" } ], "rules": [], "modes": [ { "name": "A", "rules": [] } ] }""", Devices).Profile!;
        Assert.Equal("F8", ProfileModes.Resolve(p, 0).ModeShortcuts![0].Trigger);
    }

    [Fact]
    public void A_profile_made_from_a_template_can_have_its_own_shortcuts_and_reset_them()
    {
        var template = ProfileLoader.Parse(StarterTemplates.All.First().Json, Devices).Profile!;
        var child = ProfileLoader.Parse(ProfileWriter.ToJson(Templates.CreateChild(template, "Game", ["game.exe"])), Devices).Profile!;
        child.ModeShortcuts = [new ModeShortcut { Trigger = "F8", Action = "Next" }];
        var back = ProfileLoader.Parse(ProfileWriter.ToJson(child), Devices).Profile!;
        Assert.Equal("F8", back.ModeShortcuts![0].Trigger);
        var diff = Templates.Diff(back).Single(d => d.Key == "modeShortcuts");
        Assert.Contains("F8", diff.Current);
        Templates.Reset(back, diff);
        Assert.Null(back.ModeShortcuts);
    }
}

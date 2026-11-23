using G923Mapper;

namespace G923Mapper.Tests;

public class SafetyTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();

    private static ProfileLoadResult Load(string safety, bool keyboard, string exe = "game.exe", string rules = "")
    {
        string safetyLine = safety.Length == 0 ? "" : $"\"safety\": \"{safety}\",";
        return ProfileLoader.Parse($$"""
            { "name": "t", "match": { "exe": ["{{exe}}"] }, "device": "g923_xbox", {{safetyLine}}
              "outputs": { "keyboard": {{(keyboard ? "true" : "false")}} },
              "rules": [ {{rules}} ] }
            """, Devices);
    }

    [Theory]
    [InlineData("", false, "strict")]            // nothing asked for: the safe mode
    [InlineData("", true, "standard")]           // an older profile that already used the keyboard keeps working
    [InlineData("strict", true, "strict")]
    [InlineData("STANDARD", false, "standard")]
    public void Effective_safety_defaults_sensibly(string safety, bool keyboard, string expected)
    {
        var r = Load(safety, keyboard);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(expected, ProfileSafety.Effective(r.Profile!));
    }

    [Fact]
    public void An_unknown_safety_value_is_an_error()
    {
        var r = Load("relaxed", false);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Contains("safety"));
    }

    [Fact]
    public void Strict_blocks_keyboard_output_with_a_warning_saying_how_to_allow_it()
    {
        var r = Load("strict", true);
        Assert.True(r.Ok);
        Assert.False(ProfileSafety.KeyboardAllowed(r.Profile!));
        Assert.Contains(r.Warnings, w => w.Contains("strict") && w.Contains("standard"));
    }

    [Fact]
    public void Standard_allows_keyboard_output_for_a_game_without_anti_cheat()
    {
        var r = Load("standard", true);
        Assert.True(ProfileSafety.KeyboardAllowed(r.Profile!));
        Assert.Empty(r.Warnings);
    }

    [Theory]
    [InlineData("GTA5.exe")]
    [InlineData("gta5")]
    [InlineData("GTA5_Enhanced.exe")]
    public void Standard_mode_cannot_enable_the_keyboard_for_a_game_on_the_anti_cheat_list(string exe)
    {
        var r = Load("standard", true, exe);
        Assert.True(r.Ok);
        Assert.False(ProfileSafety.KeyboardAllowed(r.Profile!));
        Assert.Contains(r.Warnings, w => w.Contains("anti-cheat list"));
    }

    [Fact]
    public void Key_rules_are_dropped_by_the_engine_whenever_the_keyboard_is_not_allowed()
    {
        const string rule = """{ "from": "paddleR", "to": "Key:F" }""";
        Devices.TryGet("g923_xbox", out var device);

        var allowed = Load("standard", true, "game.exe", rule).Profile!;
        var inFrame = new InputFrame();
        inFrame.Values["paddleR"] = 1;

        Assert.Contains("F", new MappingEngine(allowed, device, Devices).Evaluate(inFrame, 0).Keys);
        foreach (var blocked in new[] { Load("strict", true, "game.exe", rule).Profile!, Load("standard", true, "GTA5.exe", rule).Profile! })
            Assert.Empty(new MappingEngine(blocked, device, Devices).Evaluate(inFrame, 0).Keys);
    }

    [Fact]
    public void A_key_rule_warns_with_the_reason_it_does_nothing()
    {
        var r = Load("strict", true, "game.exe", """{ "from": "paddleR", "to": "Key:F" }""");
        Assert.Contains(r.Warnings, w => w.Contains("Rule 1") && w.Contains("strict"));
    }

    [Fact]
    public void The_profile_writer_saves_the_safety_mode_and_it_survives_a_round_trip()
    {
        var original = Load("standard", true).Profile!;
        var again = ProfileLoader.Parse(ProfileWriter.ToJson(original), Devices).Profile!;
        Assert.Equal("standard", again.Safety);
        Assert.True(again.Outputs.Keyboard);

        var strict = ProfileLoader.Parse(ProfileWriter.ToJson(Load("", false).Profile!), Devices).Profile!;
        Assert.Equal("strict", strict.Safety);
    }

    [Fact]
    public void The_starter_profiles_are_strict()
    {
        string dir = Path.Combine(Path.GetTempPath(), "g923-safe-" + Guid.NewGuid());
        try
        {
            DefaultProfiles.EnsureWritten(dir);
            foreach (string file in Directory.GetFiles(dir, "*.json"))
                Assert.Equal("strict", ProfileLoader.LoadFile(file, Devices).Profile!.Safety);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}

public class AntiCheatListTests
{
    [Fact]
    public void Starts_with_the_gta_executables()
    {
        var list = new AntiCheatList(AntiCheatList.Defaults);
        Assert.True(list.Contains("GTA5.exe"));
        Assert.True(list.Contains("gta5_enhanced"));
        Assert.False(list.Contains("notepad.exe"));
    }

    [Fact]
    public void A_user_list_ignores_comments_and_blank_lines_and_matches_any_spelling()
    {
        var list = AntiCheatList.Parse("# my games\n\nFortniteClient-Win64-Shipping.exe\r\n  valorant  \n");
        Assert.True(list.Contains("fortniteclient-win64-shipping"));
        Assert.True(list.Contains("VALORANT.EXE"));
        Assert.Equal(2, list.Names.Count);
    }

    [Fact]
    public void Load_creates_the_file_with_the_defaults_and_then_reads_edits()
    {
        string path = Path.Combine(Path.GetTempPath(), "g923-ac-" + Guid.NewGuid() + ".txt");
        try
        {
            Assert.True(AntiCheatList.Load(path).Contains("GTA5.exe"));
            Assert.True(File.Exists(path));
            File.AppendAllText(path, "mygame.exe\n");
            var edited = AntiCheatList.Load(path);
            Assert.True(edited.Contains("mygame"));
            Assert.True(edited.Contains("GTA5.exe"));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void An_unreadable_location_falls_back_to_the_defaults()
    {
        var list = AntiCheatList.Load(Path.Combine(Path.GetTempPath(), "g923-no-such-dir-" + Guid.NewGuid(), "x.txt"));
        Assert.True(list.Contains("GTA5.exe"));
    }

    [Fact]
    public void A_custom_list_can_be_used_for_the_check()
    {
        var p = new Profile { Safety = "standard", Match = new ProfileMatch { Exe = ["mygame.exe"] }, Outputs = new ProfileOutputs { Keyboard = true } };
        Assert.True(ProfileSafety.KeyboardAllowed(p, new AntiCheatList(["other.exe"])));
        Assert.False(ProfileSafety.KeyboardAllowed(p, new AntiCheatList(["MyGame.exe"])));
    }
}

public class HealthReportTests
{
    [Fact]
    public void The_report_lists_every_finding_with_its_level_and_the_log_tail()
    {
        var items = new List<HealthItem>
        {
            new("Driver missing", HealthLevel.Problem, "Install ViGEmBus."),
            new("Wheel is working", HealthLevel.Ok, "G923 is sending input."),
        };
        string text = HealthReport.Build(new DateTime(2026, 11, 23, 9, 5, 0), ["App: 1.0.0"], items, ["12:00 started", "12:01 running"]);

        Assert.Contains("2026-11-23 09:05:00", text);
        Assert.Contains("App: 1.0.0", text);
        Assert.Contains("Summary: 1 problem", text);
        Assert.Contains("[PROBLEM] Driver missing", text);
        Assert.Contains("Install ViGEmBus.", text);
        Assert.Contains("[OK     ] Wheel is working", text);
        Assert.Contains("Recent log lines:", text);
        Assert.Contains("  12:01 running", text);
    }

    [Fact]
    public void Without_log_lines_the_section_is_left_out()
    {
        string text = HealthReport.Build(DateTime.Now, [], [], []);
        Assert.DoesNotContain("Recent log lines", text);
        Assert.Contains("Summary: All good", text);
    }
}

public class WheelRestorePolicyTests
{
    [Theory]
    [InlineData(true, true, true, false, true)]      // the normal case: hidden by us, nobody else needs it
    [InlineData(false, true, true, false, false)]    // the user chose to keep it hidden
    [InlineData(true, false, true, false, false)]    // HidHide is not installed
    [InlineData(true, true, false, false, false)]    // nothing to restore
    [InlineData(true, true, true, true, false)]      // another copy of the app (second wheel) is still running
    public void Unhide_on_exit_only_when_it_is_wanted_and_safe(bool setting, bool installed, bool hidden, bool other, bool expected) =>
        Assert.Equal(expected, WheelRestorePolicy.UnhideOnExit(setting, installed, hidden, other));

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, true, false)]           // persistent hiding is what they asked for: no nagging
    [InlineData(true, false, true, false)]
    public void Warn_at_start_when_the_wheel_was_left_hidden(bool setting, bool installed, bool hidden, bool expected) =>
        Assert.Equal(expected, WheelRestorePolicy.WarnAtStart(setting, installed, hidden));
}

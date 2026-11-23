using G923Mapper;

namespace G923Mapper.Tests;

public class ProfileSelectorTests
{
    private static (string, Profile) P(string key, params string[] exes) =>
        (key, new Profile { Name = key, Match = new ProfileMatch { Exe = exes.ToList() } });

    [Theory]
    [InlineData("GTA5.exe", "gta5")]
    [InlineData("gta5.EXE", "gta5")]
    [InlineData("gta5", "gta5")]
    [InlineData("  GTA5_Enhanced.exe ", "gta5_enhanced")]
    public void Names_normalise_to_the_same_form(string input, string expected) => Assert.Equal(expected, ProfileSelector.Normalize(input));

    [Fact]
    public void Matches_a_running_game_regardless_of_case_or_extension()
    {
        var profiles = new[] { P("default"), P("gta-v", "GTA5.exe", "GTA5_Enhanced.exe") };
        Assert.Equal(("gta-v", "GTA5.exe"), ProfileSelector.Select(profiles, ["explorer.exe", "gta5.EXE"]));
        Assert.Equal(("gta-v", "GTA5_Enhanced.exe"), ProfileSelector.Select(profiles, ["gta5_enhanced"]));
    }

    [Fact]
    public void Returns_null_when_nothing_matches()
    {
        var profiles = new[] { P("default"), P("gta-v", "GTA5.exe") };
        Assert.Null(ProfileSelector.Select(profiles, ["explorer.exe", "chrome.exe"]));
        Assert.Null(ProfileSelector.Select(profiles, []));
    }

    [Fact]
    public void A_profile_with_no_exe_list_never_auto_matches() =>
        Assert.Null(ProfileSelector.Select([P("default")], ["default.exe", "default"]));

    [Fact]
    public void When_two_games_run_the_choice_is_deterministic_by_file_name()
    {
        var profiles = new[] { P("zzz-game", "z.exe"), P("aaa-game", "a.exe") };
        Assert.Equal("aaa-game", ProfileSelector.Select(profiles, ["z.exe", "a.exe"])!.Value.Key);
        Assert.Equal("aaa-game", ProfileSelector.Select(profiles.Reverse(), ["a.exe", "z.exe"])!.Value.Key);
    }

    [Fact]
    public void Substrings_do_not_match()
        => Assert.Null(ProfileSelector.Select([P("g", "gta5.exe")], ["gta5launcher.exe", "mygta5.exe"]));
}

public class AutoProfileSwitcherTests
{
    private static Profile Game(string exe) => new() { Name = "g", Match = new ProfileMatch { Exe = [exe] } };

    private sealed class Harness
    {
        public List<string> Running = new();
        public List<(string?, string?)> Events = new();
        public readonly AutoProfileSwitcher Switcher;
        public Harness()
        {
            Switcher = new AutoProfileSwitcher(
                () => [("gta-v", Game("GTA5.exe")), ("default", new Profile { Name = "default" })],
                () => Running.ToList(),
                TimeSpan.FromHours(1));   // never fires on its own; tests call Poll()
            Switcher.Changed += (k, e) => Events.Add((k, e));
        }
    }

    [Fact]
    public void Fires_once_when_the_game_starts_and_once_when_it_stops()
    {
        var h = new Harness();
        h.Switcher.Poll();
        Assert.Empty(h.Events);                                  // nothing running, nothing active: no event

        h.Running.Add("GTA5.exe"); h.Switcher.Poll(); h.Switcher.Poll(); h.Switcher.Poll();
        Assert.Equal([("gta-v", "GTA5.exe")], h.Events);          // repeated polls do not repeat the event
        Assert.Equal("gta-v", h.Switcher.ActiveKey);

        h.Running.Clear(); h.Switcher.Poll();
        Assert.Equal(2, h.Events.Count);
        Assert.Equal((null, null), h.Events[1]);
        Assert.Null(h.Switcher.ActiveKey);
    }

    [Fact]
    public void Disabling_switches_back_immediately_even_with_the_game_still_running()
    {
        var h = new Harness();
        h.Running.Add("GTA5.exe"); h.Switcher.Poll();
        h.Switcher.Enabled = false; h.Switcher.Poll();
        Assert.Equal((null, null), h.Events[^1]);
        Assert.Null(h.Switcher.ActiveKey);

        h.Switcher.Enabled = true; h.Switcher.Poll();
        Assert.Equal(("gta-v", "GTA5.exe"), h.Events[^1]);
    }

    [Fact]
    public void A_failing_process_listing_is_ignored_and_keeps_the_current_state()
    {
        bool fail = false;
        var sw = new AutoProfileSwitcher(() => [("gta-v", Game("GTA5.exe"))],
            () => fail ? throw new InvalidOperationException("boom") : ["GTA5.exe"], TimeSpan.FromHours(1));
        sw.Poll();
        fail = true; sw.Poll();
        Assert.Equal("gta-v", sw.ActiveKey);
    }

    [Fact]
    public void The_real_process_list_works_and_finds_this_test_host()
    {
        var names = RunningProcesses.Names().ToList();
        Assert.NotEmpty(names);
        Assert.All(names, n => Assert.EndsWith(".exe", n));
        Assert.Contains(names, n => n.Contains("testhost", StringComparison.OrdinalIgnoreCase) || n.Contains("dotnet", StringComparison.OrdinalIgnoreCase));
    }
}

public class ProfileWriterTests
{
    private static Profile Roundtrip(Profile p)
    {
        var r = ProfileLoader.Parse(ProfileWriter.ToJson(p), T.Devices);
        Assert.True(r.Ok, string.Join("; ", r.Errors) + "\n" + ProfileWriter.ToJson(p));
        return r.Profile!;
    }

    [Fact]
    public void Starter_profiles_survive_a_write_and_reload_unchanged()
    {
        foreach (string json in new[] { DefaultProfiles.Default, DefaultProfiles.GtaV })
        {
            var original = ProfileLoader.Parse(json, T.Devices).Profile!;
            var again = Roundtrip(original);
            Assert.Equal(original.Name, again.Name);
            Assert.Equal(original.Device, again.Device);
            Assert.Equal(original.Match.Exe, again.Match.Exe);
            Assert.Equal(original.Rules.Count, again.Rules.Count);
            for (int i = 0; i < original.Rules.Count; i++)
            {
                Rule a = original.Rules[i], b = again.Rules[i];
                Assert.Equal((a.From, a.To, a.Curve, a.Deadzone, a.AntiDeadzone, a.Invert, a.Threshold, a.Mode, a.Range),
                             (b.From, b.To, b.Curve, b.Deadzone, b.AntiDeadzone, b.Invert, b.Threshold, b.Mode, b.Range));
            }
        }
    }

    [Fact]
    public void Every_rule_field_round_trips()
    {
        var p = new Profile
        {
            Name = "all \"quoted\" fields",
            Device = "g923_xbox",
            Match = new ProfileMatch { Exe = ["a.exe", "b.exe"] },
            Outputs = new ProfileOutputs { Keyboard = true },
            Rules =
            [
                new Rule { From = "wheel", To = "LeftStickX", Curve = "expo", Exponent = 1.5, Deadzone = 0.04, AntiDeadzone = 0.06, Invert = true, Range = 540 },
                new Rule { From = "throttle", To = "RightTrigger", Curve = "custom", Points = [[0, 0], [0.5, 0.2], [1, 1]] },
                new Rule { From = "clutch", To = "Button:RightThumb", Threshold = -0.4, Mode = "pulse", PulseMs = 120 },
                new Rule { From = "paddleR", To = "Button:A", Mode = "toggle" },
            ],
        };
        var q = Roundtrip(p);
        Assert.Equal(p.Name, q.Name);
        Assert.Equal(p.Match.Exe, q.Match.Exe);
        Assert.True(q.Outputs.Keyboard);
        Assert.Equal(1.5, q.Rules[0].Exponent);
        Assert.Equal(0.04, q.Rules[0].Deadzone);
        Assert.True(q.Rules[0].Invert);
        Assert.Equal(540, q.Rules[0].Range);
        Assert.Equal([[0, 0], [0.5, 0.2], [1, 1]], q.Rules[1].Points);
        Assert.Equal(-0.4, q.Rules[2].Threshold);
        Assert.Equal("pulse", q.Rules[2].Mode);
        Assert.Equal(120, q.Rules[2].PulseMs);
        Assert.Equal("toggle", q.Rules[3].Mode);
    }

    [Fact]
    public void Defaults_are_left_out_to_keep_the_file_readable()
    {
        string json = ProfileWriter.ToJson(new Profile
        {
            Name = "x", Rules = [new Rule { From = "wheel", To = "LeftStickX" }],
        });
        Assert.Contains("\"curve\": \"linear\"", json);
        foreach (string field in new[] { "deadzone", "antiDeadzone", "invert", "threshold", "mode", "pulseMs", "range", "exponent", "points", "match" })
            Assert.DoesNotContain($"\"{field}\"", json);
    }

    [Fact]
    public void Numbers_are_culture_independent()
    {
        var old = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");   // decimal comma
            string json = ProfileWriter.ToJson(new Profile { Name = "x", Rules = [new Rule { From = "wheel", To = "LeftStickX", Deadzone = 0.04 }] });
            Assert.Contains("\"deadzone\": 0.04", json);
        }
        finally { Thread.CurrentThread.CurrentCulture = old; }
    }

    [Fact]
    public void A_rule_with_a_Key_target_is_preserved_even_though_it_does_nothing_yet()
    {
        var q = Roundtrip(new Profile { Name = "k", Rules = [new Rule { From = "paddleR", To = "Key:Q" }] });
        Assert.Equal("Key:Q", q.Rules[0].To);
    }
}

public class CurveTextTests
{
    [Fact]
    public void Formats_and_parses_points()
    {
        double[][] pts = [[0, 0], [0.5, 0.2], [1, 1]];
        string text = CurveText.Format(pts);
        Assert.Equal("0,0 0.5,0.2 1,1", text);
        Assert.True(CurveText.TryParse(text, out var back, out var err));
        Assert.Null(err);
        Assert.Equal(pts, back);
    }

    [Theory]
    [InlineData("0,0;1,1")]
    [InlineData("0,0\n1,1")]
    [InlineData("  0,0    1,1 ")]
    public void Accepts_several_separators(string text)
    {
        Assert.True(CurveText.TryParse(text, out var pts, out _));
        Assert.Equal(2, pts!.Length);
    }

    [Theory]
    [InlineData("0.5")]
    [InlineData("a,b")]
    [InlineData("0,0,0")]
    [InlineData("0,0 1")]
    public void Rejects_malformed_text_with_a_message(string text)
    {
        Assert.False(CurveText.TryParse(text, out var pts, out var err));
        Assert.Null(pts);
        Assert.False(string.IsNullOrEmpty(err));
    }

    [Fact]
    public void Empty_text_means_no_points()
    {
        Assert.True(CurveText.TryParse("   ", out var pts, out _));
        Assert.Null(pts);
        Assert.Equal("", CurveText.Format(null));
    }

    [Fact]
    public void Uses_a_dot_decimal_regardless_of_culture()
    {
        var old = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.True(CurveText.TryParse("0.5,0.25", out var pts, out _));
            Assert.Equal(0.25, pts![0][1]);
            Assert.Equal("0.5,0.25", CurveText.Format(pts));
        }
        finally { Thread.CurrentThread.CurrentCulture = old; }
    }
}

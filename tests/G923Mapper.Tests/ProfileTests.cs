using G923Mapper;

namespace G923Mapper.Tests;

public class ProfileTests
{
    private static ProfileLoadResult Parse(string json) => ProfileLoader.Parse(json, T.Devices);

    private static ProfileLoadResult OneRule(string rule) =>
        Parse($$"""{ "name": "t", "device": "g923_xbox", "rules": [ {{rule}} ] }""");

    [Fact]
    public void Spec_example_parses_cleanly()
    {
        var r = Parse("""
            {
              "name": "GTA V",
              "match": { "exe": ["GTA5.exe", "GTA5_Enhanced.exe"] },
              "device": "g923_xbox",
              "outputs": { "keyboard": false },
              "rules": [
                { "from": "wheel",    "to": "LeftStickX",    "curve": "linear", "deadzone": 0.0, "antiDeadzone": 0.06, "range": 540 },
                { "from": "throttle", "to": "RightTrigger",  "curve": "linear", "invert": false },
                { "from": "brake",    "to": "LeftTrigger",   "curve": "linear" },
                { "from": "clutch",   "to": "Button:RightThumb", "threshold": 0.5, "mode": "hold" },
                { "from": "paddleR",  "to": "Button:RightShoulder" },
                { "from": "paddleL",  "to": "Button:LeftShoulder" }
              ]
            }
            """);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        // The spec's example has antiDeadzone 0.06 with deadzone 0.0, which we warn about (see below).
        Assert.Single(r.Warnings);
        Assert.Contains("antiDeadzone", r.Warnings[0]);
        Assert.Equal("GTA V", r.Profile!.Name);
        Assert.Equal(["GTA5.exe", "GTA5_Enhanced.exe"], r.Profile.Match.Exe);
        Assert.Equal(6, r.Profile.Rules.Count);
        Assert.Equal(540, r.Profile.Rules[0].Range);
        Assert.Equal(0.06, r.Profile.Rules[0].AntiDeadzone);
        Assert.Equal(0.5, r.Profile.Rules[3].Threshold);
        Assert.False(r.Profile.Outputs.Keyboard);
    }

    [Fact]
    public void Comments_trailing_commas_and_any_key_case_are_accepted()
    {
        var r = Parse("""
            {
              // a comment
              "Name": "x",
              "RULES": [ { "FROM": "wheel", "TO": "LeftStickX", }, ],
            }
            """);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
    }

    [Fact]
    public void Defaults_are_sensible()
    {
        var rule = OneRule("""{ "from": "wheel", "to": "LeftStickX" }""").Profile!.Rules[0];
        Assert.Equal("linear", rule.Curve);
        Assert.Equal(0, rule.Deadzone);
        Assert.Equal(0, rule.AntiDeadzone);
        Assert.False(rule.Invert);
        Assert.Equal("hold", rule.Mode);
        Assert.Null(rule.Threshold);
        Assert.Null(rule.Range);
    }

    [Fact]
    public void The_starter_profiles_are_valid()
    {
        var d = Parse(DefaultProfiles.Default);
        var g = Parse(DefaultProfiles.GtaV);
        Assert.True(d.Ok, string.Join("; ", d.Errors));
        Assert.True(g.Ok, string.Join("; ", g.Errors));
        Assert.Empty(d.Warnings);
        Assert.Empty(g.Warnings);
        Assert.False(g.Profile!.Outputs.Keyboard);   // spec section 8: keyboard output off in the GTA profile
    }

    [Fact]
    public void Broken_json_reports_a_line_number_and_no_profile()
    {
        var r = Parse("{\n \"name\": \"x\",\n \"rules\": [ { \"from\": }\n }");
        Assert.False(r.Ok);
        Assert.Null(r.Profile);
        Assert.Contains("line", r.Errors[0]);
    }

    [Fact]
    public void Empty_document_is_an_error() => Assert.False(Parse("null").Ok);

    [Fact]
    public void Name_is_required() => Assert.Contains(Parse("""{ "rules": [] }""").Errors, e => e.Contains("name"));

    [Fact]
    public void Unknown_device_is_an_error()
    {
        var r = Parse("""{ "name": "x", "device": "g29", "rules": [] }""");
        Assert.Contains(r.Errors, e => e.Contains("Unknown device"));
    }

    [Theory]
    [InlineData("""{ "from": "steering", "to": "LeftStickX" }""", "unknown input")]
    [InlineData("""{ "from": "wheel", "to": "MiddleStickZ" }""", "unknown target")]
    [InlineData("""{ "from": "wheel", "to": "" }""", "empty")]
    [InlineData("""{ "from": "wheel", "to": "Button:Turbo" }""", "unknown button")]
    [InlineData("""{ "from": "wheel", "to": "Button:3" }""", "unknown button")]
    [InlineData("""{ "from": "wheel", "to": "3" }""", "unknown target")]
    [InlineData("""{ "from": "wheel", "to": "Key:" }""", "key name")]
    [InlineData("""{ "from": "wheel", "to": "LeftStickX", "curve": "wobbly" }""", "curve must")]
    [InlineData("""{ "from": "wheel", "to": "LeftStickX", "curve": "custom" }""", "at least 2 points")]
    [InlineData("""{ "from": "wheel", "to": "LeftStickX", "curve": "custom", "points": [[0,0]] }""", "at least 2 points")]
    [InlineData("""{ "from": "wheel", "to": "LeftStickX", "curve": "custom", "points": [[0,0],[0,1]] }""", "ascending")]
    [InlineData("""{ "from": "wheel", "to": "LeftStickX", "curve": "custom", "points": [[0,0],[2,1]] }""", "0..1")]
    [InlineData("""{ "from": "wheel", "to": "LeftStickX", "curve": "expo", "exponent": 0 }""", "exponent")]
    [InlineData("""{ "from": "wheel", "to": "LeftStickX", "deadzone": 1 }""", "deadzone")]
    [InlineData("""{ "from": "wheel", "to": "LeftStickX", "deadzone": -0.1 }""", "deadzone")]
    [InlineData("""{ "from": "wheel", "to": "LeftStickX", "antiDeadzone": 1.5 }""", "antiDeadzone")]
    [InlineData("""{ "from": "clutch", "to": "Button:A", "threshold": 0 }""", "threshold")]
    [InlineData("""{ "from": "clutch", "to": "Button:A", "threshold": 2 }""", "threshold")]
    [InlineData("""{ "from": "clutch", "to": "Button:A", "mode": "turbo" }""", "mode must")]
    [InlineData("""{ "from": "clutch", "to": "Button:A", "mode": "pulse", "pulseMs": 0 }""", "pulseMs")]
    [InlineData("""{ "from": "wheel", "to": "LeftStickX", "range": 0 }""", "range")]
    public void Invalid_rules_are_rejected_with_a_helpful_message(string rule, string expectedFragment)
    {
        var r = OneRule(rule);
        Assert.False(r.Ok);
        Assert.Null(r.Profile);
        Assert.Contains(r.Errors, e => e.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(r.Errors, e => e.Contains("Rule 1"));   // says which rule
    }

    [Fact]
    public void Every_bad_rule_is_reported_not_just_the_first()
    {
        var r = Parse("""
            { "name": "x", "rules": [
              { "from": "nope", "to": "LeftStickX" },
              { "from": "wheel", "to": "Nope" }
            ] }
            """);
        Assert.Contains(r.Errors, e => e.Contains("Rule 1"));
        Assert.Contains(r.Errors, e => e.Contains("Rule 2"));
    }

    [Theory]
    [InlineData("Button:RightThumb")]
    [InlineData("button:rightthumb")]
    [InlineData("LeftStickX")]
    [InlineData("leftstickx")]
    [InlineData("RightTrigger")]
    public void Targets_are_case_insensitive(string to) => Assert.True(OneRule($$"""{ "from": "clutch", "to": "{{to}}" }""").Ok);

    [Fact]
    public void Keyboard_rule_is_valid_but_warns_that_it_does_nothing()
    {
        var r = OneRule("""{ "from": "paddleR", "to": "Key:Q" }""");
        Assert.True(r.Ok);
        Assert.Contains(r.Warnings, w => w.Contains("keyboard output is disabled"));
    }

    [Fact]
    public void AntiDeadzone_on_the_wheel_without_a_deadzone_warns_about_centre_noise()
    {
        var r = OneRule("""{ "from": "wheel", "to": "LeftStickX", "antiDeadzone": 0.06 }""");
        Assert.True(r.Ok);
        Assert.Contains(r.Warnings, w => w.Contains("antiDeadzone") && w.Contains("deadzone"));
    }

    [Theory]
    [InlineData("""{ "from": "wheel", "to": "LeftStickX", "antiDeadzone": 0.06, "deadzone": 0.02 }""")]   // has a deadzone
    [InlineData("""{ "from": "wheel", "to": "LeftStickX", "deadzone": 0.02 }""")]                          // no anti-deadzone
    [InlineData("""{ "from": "throttle", "to": "RightTrigger", "antiDeadzone": 0.05 }""")]                // a pedal rests at exactly 0
    public void No_antiDeadzone_warning_when_it_does_not_apply(string rule)
        => Assert.DoesNotContain(OneRule(rule).Warnings, w => w.Contains("antiDeadzone"));

    [Fact]
    public void Range_on_a_pedal_warns()
    {
        var r = OneRule("""{ "from": "throttle", "to": "RightTrigger", "range": 540 }""");
        Assert.True(r.Ok);
        Assert.Contains(r.Warnings, w => w.Contains("range"));
    }

    [Fact]
    public void Mode_on_an_axis_target_warns()
    {
        var r = OneRule("""{ "from": "throttle", "to": "RightTrigger", "mode": "toggle" }""");
        Assert.True(r.Ok);
        Assert.Contains(r.Warnings, w => w.Contains("mode"));
    }

    [Fact]
    public void LoadFile_on_a_missing_file_is_an_error_not_an_exception()
    {
        var r = ProfileLoader.LoadFile(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid() + ".json"), T.Devices);
        Assert.False(r.Ok);
    }
}

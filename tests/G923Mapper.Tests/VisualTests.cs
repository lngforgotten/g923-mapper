using G923Mapper;

namespace G923Mapper.Tests;

public class PadLabelTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();
    private static Profile Load(string rules) =>
        ProfileLoader.Parse("{ \"name\": \"t\", \"rules\": [ " + rules + " ] }", Devices).Profile!;

    [Fact]
    public void Each_mapped_control_is_labelled_with_the_wheel_control_that_feeds_it()
    {
        var p = Load("""
            { "from": "wheel", "to": "LeftStickX" },
            { "from": "paddleR", "to": "Button:A" },
            { "from": "throttle", "to": "RightTrigger" }
            """);
        var labels = PadControls.Labels(p);
        Assert.Equal("wheel", labels["LeftStick"]);
        Assert.Equal("paddleR", labels["Button:A"]);
        Assert.Equal("throttle", labels["RightTrigger"]);
        Assert.False(labels.ContainsKey("Button:B"));
    }

    [Fact]
    public void Several_rules_on_one_control_get_a_plus_and_a_stick_shows_its_steering_axis()
    {
        var p = Load("""
            { "from": "paddleL", "to": "Button:LeftThumb" },
            { "from": "wheel", "to": "LeftStickX" },
            { "from": "paddleR", "to": "Button:A" },
            { "from": "clutch", "to": "Button:A" }
            """);
        var labels = PadControls.Labels(p);
        Assert.Equal("wheel+", labels["LeftStick"]);
        Assert.Equal("paddleR+", labels["Button:A"]);
    }

    [Fact]
    public void Flows_list_each_wheel_control_and_picture_control_pair_once()
    {
        var p = Load("""
            { "from": "wheel", "to": "LeftStickX" },
            { "from": "wheel", "to": "LeftStickY" },
            { "from": "paddleR", "to": "Button:A" },
            { "from": "paddleR", "to": "Button:B" }
            """);
        var flows = PadControls.Flows(p);
        Assert.Equal(3, flows.Count);
        Assert.Contains(("wheel", "LeftStick"), flows);
        Assert.Contains(("paddleR", "Button:A"), flows);
        Assert.Contains(("paddleR", "Button:B"), flows);
    }

    [Fact]
    public void An_empty_profile_has_no_labels_or_flows()
    {
        var p = Load("");
        Assert.Empty(PadControls.Labels(p));
        Assert.Empty(PadControls.Flows(p));
    }
}

public class SampleRingTests
{
    [Fact]
    public void Samples_come_back_oldest_first_and_the_ring_forgets_the_oldest_when_full()
    {
        var r = new SampleRing(3);
        Assert.Empty(r.ToArray());
        r.Add(1, 10); r.Add(2, 20);
        Assert.Equal([(1, 10), (2, 20)], r.ToArray().Select(s => ((int)s.First, (int)s.Second)));
        r.Add(3, 30); r.Add(4, 40);
        Assert.Equal(3, r.Count);
        Assert.Equal([2, 3, 4], r.ToArray().Select(s => (int)s.First));
        Assert.Equal([20, 30, 40], r.ToArray().Select(s => (int)s.Second));
    }

    [Fact]
    public void Clear_empties_it_and_a_capacity_below_one_is_refused()
    {
        var r = new SampleRing(2);
        r.Add(5, 5);
        r.Clear();
        Assert.Equal(0, r.Count);
        Assert.Empty(r.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => new SampleRing(0));
    }
}

public class LiveCurveTests
{
    private static Rule R(Action<Rule>? tweak = null)
    {
        var r = new Rule { From = "throttle", To = "RightTrigger", Curve = "linear" };
        tweak?.Invoke(r);
        return r;
    }

    [Fact]
    public void A_plain_rule_puts_the_dot_on_the_straight_line()
    {
        var (x, y) = LiveCurve.Position(0.4, R(), signed: false, 360)!.Value;
        Assert.Equal(0.4, x, 6);
        Assert.Equal(0.4, y, 6);
    }

    [Fact]
    public void A_deadzone_flattens_the_output_near_zero()
    {
        var (x, y) = LiveCurve.Position(0.03, R(r => r.Deadzone = 0.1), false, 360)!.Value;
        Assert.Equal(0.03, x, 6);
        Assert.Equal(0, y, 6);
    }

    [Fact]
    public void Invert_flips_the_position_for_a_pedal_and_for_the_wheel()
    {
        Assert.Equal(0.7, LiveCurve.Position(0.3, R(r => r.Invert = true), false, 360)!.Value.X, 6);
        Assert.Equal(-0.5, LiveCurve.Position(0.5, R(r => r.Invert = true), true, 360)!.Value.X, 6);
    }

    [Fact]
    public void A_half_axis_stretches_one_side_to_the_full_range()
    {
        Assert.Equal(0.5, LiveCurve.Position(0.75, R(r => r.Part = "positive"), false, 360)!.Value.X, 6);   // upper half of a pedal axis
        Assert.Equal(0.4, LiveCurve.Position(-0.4, R(r => r.Part = "negative"), true, 360)!.Value.X, 6);
    }

    [Fact]
    public void A_range_narrower_than_the_wheel_reaches_full_lock_sooner()
    {
        // The wheel turned a quarter of 360 degrees is 90; a 180 degree range makes that half of the way.
        var (x, _) = LiveCurve.Position(0.25, new Rule { From = "wheel", To = "LeftStickX", Range = 180, Curve = "linear" }, true, 360)!.Value;
        Assert.Equal(0.5, x, 6);
    }

    [Fact]
    public void A_rule_with_an_expression_has_no_single_position()
    {
        Assert.Null(LiveCurve.Position(0.5, R(r => r.Expr = "x * 2"), false, 360));
    }
}

public class KeyMappingTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();
    private static DeviceMap Wheel() { Devices.TryGet("g923_xbox", out var d); return d; }

    private static Profile Load(string json) => ProfileLoader.Parse(json, Devices).Profile!;

    private const string Standard = """
        { "name": "t", "safety": "standard", "outputs": { "keyboard": true }, "match": { "exe": ["game.exe"] },
          "rules": [ { "from": "wheel", "to": "LeftStickX" }, { "from": "paddleR", "to": "Key:Q" }, { "from": "clutch", "to": "Mouse:Left" } ] }
        """;

    [Fact]
    public void Key_and_mouse_rules_are_listed_with_friendly_names_and_pad_rules_are_not()
    {
        var list = KeyMappings.Of(Load(Standard));
        Assert.Equal(["Q", "Mouse Left"], list.Select(k => k.Key));
        Assert.Equal(["paddleR", "clutch"], list.Select(k => k.Rule.From));
    }

    [Theory]
    [InlineData("q", "Key:Q")]
    [InlineData("Space", "Key:Space")]
    [InlineData("Mouse:Left", "Mouse:Left")]
    [InlineData("mouse right", "Mouse:Right")]
    [InlineData("Mouse X1", "Mouse:X1")]
    [InlineData("Banana", null)]
    [InlineData("", null)]
    public void Names_become_rule_targets(string name, string? expected) => Assert.Equal(expected, KeyMappings.TargetFor(name));

    [Fact]
    public void A_plain_standard_profile_sends_keys_a_strict_one_can_be_allowed_and_an_anticheat_game_never_can()
    {
        Assert.Equal(KeyOutput.Active, KeyMappings.Status(Load(Standard)).State);

        var strict = Load("""{ "name": "t", "match": { "exe": ["game.exe"] }, "rules": [ { "from": "paddleR", "to": "Key:Q" } ] }""");
        Assert.Equal(KeyOutput.NeedsEnabling, KeyMappings.Status(strict).State);
        KeyMappings.Enable(strict);
        Assert.Equal(KeyOutput.Active, KeyMappings.Status(strict).State);
        Assert.Equal("standard", strict.Safety);

        var protectedGame = Load("""{ "name": "t", "safety": "standard", "outputs": { "keyboard": true }, "match": { "exe": ["GTA5.exe"] }, "rules": [] }""");
        var status = KeyMappings.Status(protectedGame, new AntiCheatList(["GTA5.exe"]));
        Assert.Equal(KeyOutput.Blocked, status.State);
        Assert.Contains("GTA5.exe", status.Reason);
    }

    [Fact]
    public void Adding_a_mapping_puts_a_rule_at_the_end_with_a_threshold_for_a_pedal()
    {
        var p = Load(Standard);
        var rule = KeyMappings.Set(p, Wheel(), null, "brake", "E")!;
        Assert.Equal("Key:E", rule.To);
        Assert.Equal(0.5, rule.Threshold);
        Assert.Same(rule, p.Rules[^1]);
        Assert.Null(KeyMappings.Set(p, Wheel(), null, "brake", "Banana"));
        Assert.Null(KeyMappings.Set(p, Wheel(), null, "", "E"));
    }

    [Fact]
    public void Changing_a_mapping_edits_that_rule_in_place_and_keeps_its_tuning()
    {
        var p = Load(Standard);
        var q = KeyMappings.Of(p)[0].Rule;
        q.Mode = "toggle";
        int index = p.Rules.IndexOf(q);
        var changed = KeyMappings.Set(p, Wheel(), q, "paddleL", "Space")!;
        Assert.Same(q, changed);
        Assert.Equal(index, p.Rules.IndexOf(q));
        Assert.Equal("paddleL", q.From);
        Assert.Equal("Key:Space", q.To);
        Assert.Equal("toggle", q.Mode);
    }

    [Fact]
    public void Making_the_same_mapping_twice_leaves_one_rule()
    {
        var p = Load(Standard);
        KeyMappings.Set(p, Wheel(), null, "paddleR", "Q");
        Assert.Single(p.Rules, r => r.To == "Key:Q" && r.From == "paddleR");
    }

    [Fact]
    public void Removing_a_mapping_takes_only_that_rule_and_the_result_still_loads()
    {
        var p = Load(Standard);
        var mouse = KeyMappings.Of(p)[1].Rule;
        Assert.True(KeyMappings.Remove(p, mouse));
        Assert.False(KeyMappings.Remove(p, mouse));
        Assert.Equal(["Q"], KeyMappings.Of(p).Select(k => k.Key));
        Assert.True(ProfileLoader.Parse(ProfileWriter.ToJson(p), Devices).Ok);
    }

    [Fact]
    public void A_virtual_key_code_finds_its_name()
    {
        Assert.Equal("Q", KeyNames.NameForVk(0x51));
        Assert.Equal("Space", KeyNames.NameForVk(0x20));
        Assert.Equal("F5", KeyNames.NameForVk(0x74));
        Assert.Null(KeyNames.NameForVk(0xFF));
    }
}

using G923Mapper;

namespace G923Mapper.Tests;

public class PadControlsTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();
    private static DeviceMap Wheel() { Devices.TryGet("g923_xbox", out var d); return d; }

    private static Profile Load(string rules) =>
        ProfileLoader.Parse("{ \"name\": \"t\", \"rules\": [ " + rules + " ] }", Devices).Profile!;

    // ---------------------------------------------------------------- what is under a click

    [Theory]
    [InlineData(PadKind.Xbox360)]
    [InlineData(PadKind.DualShock4)]
    public void Clicking_the_centre_of_every_control_finds_that_control(PadKind kind)
    {
        foreach (string key in PadLayout.TargetsFor(kind))
        {
            var s = PadLayout.Of(key, kind)!.Value;
            Assert.Equal(key, PadControls.At(s.CentreX, s.CentreY, kind));
        }
    }

    [Theory]
    [InlineData(PadKind.Xbox360)]
    [InlineData(PadKind.DualShock4)]
    public void Clicking_the_corners_of_a_round_control_misses_it_and_empty_space_finds_nothing(PadKind kind)
    {
        var stick = PadLayout.Of("LeftStickX", kind)!.Value;
        Assert.NotEqual("LeftStick", PadControls.At(stick.X + 1, stick.Y + 1, kind));   // the square corner of a round well
        Assert.Null(PadControls.At(0, 0, kind));
        Assert.Null(PadControls.At(299, 179, kind));
    }

    [Fact]
    public void The_edges_of_a_square_control_count()
    {
        var up = PadLayout.Of("Button:DPadUp")!.Value;
        Assert.Equal("Button:DPadUp", PadControls.At(up.X + 1, up.Y + 1));
        Assert.Equal("Button:DPadUp", PadControls.At(up.X + up.W - 1, up.Y + up.H - 1));
        Assert.NotEqual("Button:DPadUp", PadControls.At(up.X - 3, up.Y - 3));
    }

    // ---------------------------------------------------------------- slots and names

    [Fact]
    public void A_stick_has_three_slots_and_a_button_has_one()
    {
        var stick = PadControls.Slots("LeftStick", PadKind.Xbox360);
        Assert.Equal(["LeftStickX", "LeftStickY", "Button:LeftThumb"], stick.Select(s => s.Target));
        Assert.Equal(["RightStickX", "RightStickY", "Button:RightThumb"], PadControls.Slots("RightStick", PadKind.Xbox360).Select(s => s.Target));

        var a = Assert.Single(PadControls.Slots("Button:A", PadKind.Xbox360));
        Assert.Equal("Button:A", a.Target);
        Assert.Equal("A button", a.Label);
        Assert.Equal("RightTrigger", Assert.Single(PadControls.Slots("RightTrigger", PadKind.Xbox360)).Target);
        Assert.Empty(PadControls.Slots("Nonsense", PadKind.Xbox360));
    }

    [Fact]
    public void Names_follow_the_controller_kind()
    {
        Assert.Equal("A button", PadControls.Title("Button:A", PadKind.Xbox360));
        Assert.Contains("Cross", PadControls.Title("Button:A", PadKind.DualShock4));
        Assert.Equal("L1", PadControls.Title("Button:LeftShoulder", PadKind.DualShock4));
        Assert.Equal("Left stick", PadControls.Title("LeftStick", PadKind.DualShock4));
        Assert.Contains("(L3)", PadControls.Slots("LeftStick", PadKind.DualShock4)[2].Label);
    }

    [Fact]
    public void Every_control_on_the_picture_has_at_least_one_slot_and_every_slot_target_is_valid()
    {
        foreach (PadKind kind in new[] { PadKind.Xbox360, PadKind.DualShock4 })
            foreach (string key in PadLayout.TargetsFor(kind))
            {
                var slots = PadControls.Slots(key, kind);
                Assert.NotEmpty(slots);
                Assert.All(slots, s => Assert.True(Target.TryParse(s.Target, out _, out var err), err));
            }
    }

    // ---------------------------------------------------------------- what already feeds a control

    [Fact]
    public void Mappings_group_rules_by_the_control_they_drive()
    {
        var p = Load("""
            { "from": "wheel", "to": "LeftStickX" },
            { "from": "paddleR", "to": "Button:A" },
            { "from": "paddleL", "to": "Button:LeftThumb" },
            { "from": "throttle", "to": "RightTrigger" }
            """);
        var map = PadControls.Mappings(p);
        Assert.Equal(["wheel", "paddleL"], map["LeftStick"].Select(r => r.From));   // the click and the axis share one picture control
        Assert.Equal("paddleR", Assert.Single(map["Button:A"]).From);
        Assert.Equal("throttle", Assert.Single(map["RightTrigger"]).From);
        Assert.False(map.ContainsKey("Button:B"));
    }

    [Fact]
    public void Keys_mouse_buttons_and_the_second_controller_are_not_on_the_picture()
    {
        var p = ProfileLoader.Parse("""
            { "name": "t", "safety": "standard", "outputs": { "keyboard": true, "secondPad": true },
              "rules": [ { "from": "paddleR", "to": "Key:Q" }, { "from": "paddleL", "to": "Mouse:Left" }, { "from": "throttle", "to": "P2:RightTrigger" } ] }
            """, Devices).Profile!;
        Assert.Empty(PadControls.Mappings(p));
    }

    [Fact]
    public void Rules_for_a_slot_are_found_by_their_exact_target()
    {
        var p = Load("""{ "from": "wheel", "to": "LeftStickX" }, { "from": "paddleL", "to": "Button:LeftThumb" }""");
        Assert.Equal("wheel", Assert.Single(PadControls.RulesFor(p, "LeftStickX")).From);
        Assert.Equal("paddleL", Assert.Single(PadControls.RulesFor(p, "button:leftthumb")).From);
        Assert.Empty(PadControls.RulesFor(p, "LeftStickY"));
    }

    [Fact]
    public void A_rule_is_described_in_a_line()
    {
        Assert.Equal("paddleR", PadControls.Describe(new Rule { From = "paddleR" }));
        Assert.Equal("wheel, 4% deadzone", PadControls.Describe(new Rule { From = "wheel", Deadzone = 0.04 }));
        Assert.Equal("clutch, at 35%", PadControls.Describe(new Rule { From = "clutch", Threshold = 0.35 }));
    }

    // ---------------------------------------------------------------- assigning and clearing

    [Fact]
    public void Assigning_replaces_what_fed_the_slot_and_keeps_its_place_in_the_list()
    {
        var p = Load("""
            { "from": "wheel", "to": "LeftStickX" },
            { "from": "paddleR", "to": "Button:A" },
            { "from": "throttle", "to": "RightTrigger" }
            """);
        PadControls.Assign(p, Wheel(), "paddleL", "Button:A");
        Assert.Equal(["LeftStickX", "Button:A", "RightTrigger"], p.Rules.Select(r => r.To));
        Assert.Equal("paddleL", p.Rules[1].From);
    }

    [Fact]
    public void Assigning_a_new_slot_adds_a_rule_at_the_end()
    {
        var p = Load("""{ "from": "wheel", "to": "LeftStickX" }""");
        PadControls.Assign(p, Wheel(), "paddleR", "Button:B");
        Assert.Equal(["LeftStickX", "Button:B"], p.Rules.Select(r => r.To));
    }

    [Fact]
    public void Assigning_collapses_duplicates_for_the_same_slot_to_one_rule()
    {
        var p = Load("""{ "from": "paddleL", "to": "Button:A" }, { "from": "paddleR", "to": "Button:A" }""");
        PadControls.Assign(p, Wheel(), "clutch", "Button:A");
        Assert.Single(p.Rules);
    }

    [Fact]
    public void A_pedal_on_a_button_gets_a_threshold_and_steering_a_small_deadzone_and_other_things_nothing()
    {
        var p = Load("");
        PadControls.Assign(p, Wheel(), "clutch", "Button:X");          // axis on a button
        PadControls.Assign(p, Wheel(), "wheel", "LeftStickX");         // steering on a stick
        PadControls.Assign(p, Wheel(), "throttle", "RightTrigger");    // pedal on a trigger
        PadControls.Assign(p, Wheel(), "paddleR", "Button:Y");         // button on a button

        Assert.Equal(0.5, PadControls.RulesFor(p, "Button:X").Single().Threshold);
        Assert.Equal(0.04, PadControls.RulesFor(p, "LeftStickX").Single().Deadzone);
        Assert.Equal(0, PadControls.RulesFor(p, "RightTrigger").Single().Deadzone);
        Assert.Null(PadControls.RulesFor(p, "RightTrigger").Single().Threshold);
        Assert.Null(PadControls.RulesFor(p, "Button:Y").Single().Threshold);
    }

    [Fact]
    public void An_assigned_profile_still_loads_cleanly_when_saved_and_read_back()
    {
        var p = Load("");
        PadControls.Assign(p, Wheel(), "wheel", "LeftStickX");
        PadControls.Assign(p, Wheel(), "clutch", "Button:A");
        var again = ProfileLoader.Parse(ProfileWriter.ToJson(p), Devices);
        Assert.True(again.Ok, string.Join("; ", again.Errors));
        Assert.Equal(2, again.Profile!.Rules.Count);
    }

    [Fact]
    public void Clearing_removes_only_the_rules_for_that_target()
    {
        var p = Load("""{ "from": "wheel", "to": "LeftStickX" }, { "from": "paddleR", "to": "Button:A" }, { "from": "paddleL", "to": "Button:A" }""");
        Assert.Equal(2, PadControls.Clear(p, "Button:A"));
        Assert.Equal(["LeftStickX"], p.Rules.Select(r => r.To));
        Assert.Equal(0, PadControls.Clear(p, "Button:A"));
    }

    [Fact]
    public void The_remap_walk_through_and_the_click_assignment_make_the_same_rules()
    {
        var builder = new RemapBuilder(Wheel());
        builder.Assign(RemapSteps.All.Single(s => s.Id == "a"), "clutch");
        builder.Assign(RemapSteps.All.Single(s => s.Id == "steer"), "wheel");
        var viaWalk = builder.Build("x").Rules;

        var clicked = Load("");
        PadControls.Assign(clicked, Wheel(), "wheel", "LeftStickX");
        PadControls.Assign(clicked, Wheel(), "clutch", "Button:A");

        Assert.Equal(viaWalk.Select(r => (r.From, r.To, r.Deadzone, r.Threshold)).OrderBy(x => x.To),
                     clicked.Rules.Select(r => (r.From, r.To, r.Deadzone, r.Threshold)).OrderBy(x => x.To));
    }
}

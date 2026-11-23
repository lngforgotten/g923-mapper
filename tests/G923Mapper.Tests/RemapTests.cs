using G923Mapper;

namespace G923Mapper.Tests;

public class RemapTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();
    private static DeviceMap Wheel() { Devices.TryGet("g923_xbox", out var d); return d; }
    private static RemapStep Step(string id) => RemapSteps.All.Single(s => s.Id == id);

    [Fact]
    public void Every_step_target_is_a_valid_rule_target_and_ids_are_unique()
    {
        Assert.Equal(RemapSteps.All.Count, RemapSteps.All.Select(s => s.Id).Distinct().Count());
        foreach (var s in RemapSteps.All)
            Assert.True(Target.TryParse(s.Target, out _, out var err), $"{s.Id}: {err}");
    }

    [Fact]
    public void The_walk_through_covers_every_xbox_button_and_the_sticks_triggers()
    {
        var targets = RemapSteps.All.Select(s => s.Target).ToHashSet();
        foreach (PadButton b in Enum.GetValues<PadButton>()) Assert.Contains("Button:" + b, targets);
        Assert.Contains("LeftStickX", targets);
        Assert.Contains("RightTrigger", targets);
        Assert.Contains("LeftTrigger", targets);
    }

    [Fact]
    public void Labels_switch_to_the_playstation_names_for_a_dualshock_profile()
    {
        Assert.Equal("A button", Step("a").Label(PadKind.Xbox360));
        Assert.Contains("Cross", Step("a").Label(PadKind.DualShock4));
        Assert.Equal("L1", Step("lb").Label(PadKind.DualShock4));
    }

    [Fact]
    public void Chosen_controls_become_rules_in_the_walk_through_order()
    {
        var b = new RemapBuilder(Wheel());
        b.Assign(Step("a"), "paddleR");           // chosen out of order
        b.Assign(Step("steer"), "wheel");
        b.Assign(Step("rt"), "throttle");
        var p = b.Build("  My layout ");

        Assert.Equal("My layout", p.Name);
        Assert.Equal("auto", p.Device);
        Assert.Equal("strict", p.Safety);
        Assert.Equal(["LeftStickX", "RightTrigger", "Button:A"], p.Rules.Select(r => r.To));
        Assert.Equal(["wheel", "throttle", "paddleR"], p.Rules.Select(r => r.From));
        Assert.Equal(3, b.Assigned);
    }

    [Fact]
    public void Steering_gets_a_small_deadzone_and_a_pedal_on_a_button_gets_a_threshold()
    {
        var b = new RemapBuilder(Wheel());
        b.Assign(Step("steer"), "wheel");
        b.Assign(Step("a"), "clutch");            // a pedal pressing a button
        b.Assign(Step("b"), "paddleL");           // a real button
        b.Assign(Step("up"), "dpadUp");
        var rules = b.Build("t").Rules;

        Assert.Equal(0.04, rules.Single(r => r.To == "LeftStickX").Deadzone);
        Assert.Equal(0.5, rules.Single(r => r.To == "Button:A").Threshold);
        Assert.Null(rules.Single(r => r.To == "Button:B").Threshold);
        Assert.Null(rules.Single(r => r.To == "Button:DPadUp").Threshold);
    }

    [Fact]
    public void Skipped_and_cleared_steps_make_no_rule()
    {
        var b = new RemapBuilder(Wheel());
        b.Assign(Step("a"), "paddleL");
        b.Assign(Step("b"), null);
        b.Assign(Step("x"), "  ");
        b.Assign(Step("a"), "");                  // changed their mind
        Assert.Empty(b.Build("t").Rules);
        Assert.Equal(0, b.Assigned);
    }

    [Fact]
    public void A_profile_with_every_step_filled_loads_cleanly()
    {
        var b = new RemapBuilder(Wheel());
        string[] inputs = ["wheel", "throttle", "brake", "button1", "button2", "button3", "button4", "paddleL", "paddleR", "button7", "button8", "button20", "button21", "button22", "dpadUp", "dpadDown", "dpadLeft", "dpadRight"];
        for (int i = 0; i < RemapSteps.All.Count; i++) b.Assign(RemapSteps.All[i], inputs[i]);
        Assert.Equal(RemapSteps.All.Count, b.Assigned);

        var profile = b.Build("Everything");
        var result = ProfileLoader.Parse(ProfileWriter.ToJson(profile), Devices);
        Assert.True(result.Ok, string.Join("; ", result.Errors));
        Assert.Equal(RemapSteps.All.Count, result.Profile!.Rules.Count);
    }

    [Fact]
    public void The_same_wheel_control_may_be_used_for_two_outputs()
    {
        var b = new RemapBuilder(Wheel());
        b.Assign(Step("a"), "paddleR");
        b.Assign(Step("rb"), "paddleR");
        Assert.Equal(2, b.Build("t").Rules.Count);
    }
}

public class RemapVisualTests
{
    [Fact]
    public void Every_step_has_a_plain_hint_and_a_place_on_the_controller_picture()
    {
        foreach (var step in RemapSteps.All)
        {
            Assert.True(step.Hint.Length > 30, $"{step.Id} has no real description");
            Assert.NotNull(PadLayout.Of(step.Target));
        }
    }

    [Fact]
    public void Both_axes_and_the_click_of_a_stick_point_at_the_same_stick()
    {
        Assert.Equal(PadLayout.Of("LeftStickX"), PadLayout.Of("LeftStickY"));
        Assert.Equal(PadLayout.Of("LeftStickX"), PadLayout.Of("Button:LeftThumb"));
        Assert.Equal(PadLayout.Of("RightStickX"), PadLayout.Of("Button:RightThumb"));
        Assert.NotEqual(PadLayout.Of("LeftStickX"), PadLayout.Of("RightStickX"));
    }

    [Fact]
    public void Keys_and_mouse_buttons_are_not_on_the_picture()
    {
        Assert.Null(PadLayout.Of("Key:Q"));
        Assert.Null(PadLayout.Of("Mouse:Left"));
    }

    public static IEnumerable<object[]> Kinds() => [[PadKind.Xbox360], [PadKind.DualShock4]];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void No_two_buttons_sit_on_top_of_each_other(PadKind kind)
    {
        var shapes = PadLayout.TargetsFor(kind).Select(t => (t, s: PadLayout.Of(t, kind)!.Value)).ToList();
        foreach (var (a, sa) in shapes)
            foreach (var (b, sb) in shapes.Where(x => string.CompareOrdinal(x.t, a) > 0))
            {
                bool overlap = sa.X < sb.X + sb.W && sb.X < sa.X + sa.W && sa.Y < sb.Y + sb.H && sb.Y < sa.Y + sa.H;
                Assert.False(overlap, $"{kind}: {a} overlaps {b}");
            }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Everything_fits_inside_the_picture(PadKind kind)
    {
        foreach (string t in PadLayout.TargetsFor(kind))
        {
            var s = PadLayout.Of(t, kind)!.Value;
            Assert.InRange(s.X, 0, PadLayout.Width - s.W);
            Assert.InRange(s.Y, 0, PadLayout.Height - s.H);
        }
    }

    [Fact]
    public void Both_controller_kinds_have_every_step_and_the_same_set_of_controls()
    {
        Assert.Equal(PadLayout.TargetsFor(PadKind.Xbox360).OrderBy(x => x), PadLayout.TargetsFor(PadKind.DualShock4).OrderBy(x => x));
        foreach (var step in RemapSteps.All)
            Assert.NotNull(PadLayout.Of(step.Target, PadKind.DualShock4));
    }

    [Fact]
    public void On_a_dualshock_the_left_stick_is_low_and_the_dpad_high_unlike_an_xbox()
    {
        var xboxStick = PadLayout.Of("LeftStickX", PadKind.Xbox360)!.Value;
        var psStick = PadLayout.Of("LeftStickX", PadKind.DualShock4)!.Value;
        var xboxDpad = PadLayout.Of("Button:DPadUp", PadKind.Xbox360)!.Value;
        var psDpad = PadLayout.Of("Button:DPadUp", PadKind.DualShock4)!.Value;
        Assert.True(xboxStick.CentreY < xboxDpad.CentreY);     // Xbox: stick above the D-pad
        Assert.True(psStick.CentreY > psDpad.CentreY);          // PlayStation: D-pad above the stick
    }
}
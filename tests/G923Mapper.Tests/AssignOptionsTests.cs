using G923Mapper;

namespace G923Mapper.Tests;

public class AssignOptionsTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();
    private static DeviceMap Wheel() { Devices.TryGet("g923_xbox", out var d); return d; }

    private static Rule R(string from, string to) => new() { From = from, To = to, Curve = "linear" };

    [Fact]
    public void A_pedal_on_a_trigger_offers_invert_half_deadzone_and_curve_but_no_trigger_point()
    {
        var o = AssignOptions.For(R("throttle", "RightTrigger"), Wheel());
        Assert.Equal(new AssignOptionSet(true, true, true, true, false, false), o);
    }

    [Fact]
    public void A_pedal_on_a_button_or_key_offers_a_trigger_point_and_mode_instead_of_a_curve()
    {
        foreach (string to in new[] { "Button:A", "Key:Q", "Mouse:Left" })
        {
            var o = AssignOptions.For(R("clutch", to), Wheel());
            Assert.True(o.Invert && o.Half && o.Threshold && o.Mode, to);
            Assert.False(o.Deadzone || o.Curve, to);
        }
    }

    [Fact]
    public void A_wheel_button_on_a_button_offers_only_its_mode()
    {
        var o = AssignOptions.For(R("paddleR", "Button:RightShoulder"), Wheel());
        Assert.Equal(new AssignOptionSet(false, false, false, false, false, true), o);
    }

    [Fact]
    public void A_wheel_button_on_a_stick_offers_nothing_and_a_mode_switch_offers_only_a_trigger_point_for_a_pedal()
    {
        Assert.False(AssignOptions.For(R("paddleR", "LeftStickX"), Wheel()).Any);
        Assert.Equal(new AssignOptionSet(false, false, false, false, true, false), AssignOptions.For(R("clutch", "Mode:Next"), Wheel()));
        Assert.False(AssignOptions.For(R("button8", "Mode:Next"), Wheel()).Any);
    }

    [Fact]
    public void An_unusable_target_offers_nothing()
    {
        Assert.False(AssignOptions.For(R("wheel", "Nonsense"), Wheel()).Any);
    }

    [Fact]
    public void Half_of_the_axis_is_set_and_read_the_same_way_as_in_the_rule_editor()
    {
        var r = R("wheel", "LeftStickX");
        Assert.Equal(0, AssignOptions.HalfIndex(r));
        AssignOptions.SetHalf(r, 1);
        Assert.Equal("positive", r.Part);
        Assert.Equal(1, AssignOptions.HalfIndex(r));
        AssignOptions.SetHalf(r, 2);
        Assert.Equal("negative", r.Part);
        AssignOptions.SetHalf(r, 0);
        Assert.Null(r.Part);
        Assert.Equal(0, AssignOptions.HalfIndex(new Rule { Part = "full" }));
    }

    [Fact]
    public void Curve_and_mode_choices_round_trip_and_a_custom_curve_is_left_alone()
    {
        var r = R("throttle", "RightTrigger");
        AssignOptions.SetCurve(r, 1);
        Assert.Equal("expo", r.Curve);
        Assert.Equal(1, AssignOptions.CurveIndex(r));
        AssignOptions.SetCurve(r, 0);
        Assert.Equal("linear", r.Curve);
        Assert.Equal(-1, AssignOptions.CurveIndex(new Rule { Curve = "custom" }));

        var b = R("clutch", "Button:A");
        AssignOptions.SetMode(b, 1);
        Assert.Equal("toggle", b.Mode);
        AssignOptions.SetMode(b, 2);
        Assert.Equal(2, AssignOptions.ModeIndex(b));
        AssignOptions.SetMode(b, 0);
        Assert.Equal("hold", b.Mode);
    }

    [Theory]
    [InlineData("4", 0, 99, true, 0.04, true)]
    [InlineData(" 12.5 % ", 0, 99, true, 0.125, true)]
    [InlineData("0", 0, 99, true, 0, true)]
    [InlineData("0", -100, 100, false, 0, false)]     // a trigger point of 0% is not allowed
    [InlineData("-35", -100, 100, false, -0.35, true)]
    [InlineData("100", 0, 99, true, 0, false)]
    [InlineData("abc", 0, 99, true, 0, false)]
    [InlineData("", 0, 99, true, 0, false)]
    public void Typed_percentages_are_checked_and_turned_into_fractions(string text, double min, double max, bool zero, double expected, bool ok)
    {
        Assert.Equal(ok, AssignOptions.TryPercent(text, min, max, zero, out double f));
        if (ok) Assert.Equal(expected, f, 6);
    }

    [Fact]
    public void Changing_options_on_an_assigned_rule_gives_a_profile_that_still_loads_and_behaves_as_set()
    {
        var p = ProfileLoader.Parse("""{ "name": "t", "rules": [] }""", Devices).Profile!;
        PadControls.Assign(p, Wheel(), "clutch", "RightTrigger");
        var rule = PadControls.RulesFor(p, "RightTrigger").Single();
        rule.Invert = true;
        AssignOptions.SetHalf(rule, 1);
        Assert.True(AssignOptions.TryPercent("8", 0, 99, true, out double dz));
        rule.Deadzone = dz;
        AssignOptions.SetCurve(rule, 1);

        var again = ProfileLoader.Parse(ProfileWriter.ToJson(p), Devices);
        Assert.True(again.Ok, string.Join("; ", again.Errors));
        var r2 = again.Profile!.Rules.Single();
        Assert.True(r2.Invert);
        Assert.Equal("positive", r2.Part);
        Assert.Equal(0.08, r2.Deadzone, 6);
        Assert.Equal("expo", r2.Curve);
        Assert.Equal("0", AssignOptions.PercentText(0));
        Assert.Equal("12.5", AssignOptions.PercentText(0.125));
    }

    [Fact]
    public void Options_set_while_in_a_mode_belong_to_that_mode_only()
    {
        var p = ProfileLoader.Parse("""{ "name": "t", "rules": [ { "from": "throttle", "to": "RightTrigger" } ], "modes": [ { "name": "A", "rules": [] }, { "name": "B", "rules": [] } ] }""", Devices).Profile!;
        Profile working = ProfileModes.Resolve(p, 1);
        PadControls.RulesFor(working, "RightTrigger").Single().Invert = true;
        ProfileModes.Capture(p, 1, working);
        Assert.False(p.Rules.Single().Invert);                                   // the profile's own rule is untouched
        Assert.True(ProfileModes.Resolve(p, 1).Rules.Single().Invert);            // mode B inverts
        Assert.False(ProfileModes.Resolve(p, 0).Rules.Single().Invert);          // mode A does not
    }
}

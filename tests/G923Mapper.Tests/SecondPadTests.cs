using G923Mapper;

namespace G923Mapper.Tests;

public class SecondPadTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();

    private static Profile Load(string rules, bool second = true)
    {
        var r = ProfileLoader.Parse("{ \"name\": \"t\", \"outputs\": { \"secondPad\": " + (second ? "true" : "false") + " }, \"rules\": [ " + rules + " ] }", Devices);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        return r.Profile!;
    }

    private static PadState Run(Profile p, params (string, double)[] values)
    {
        Devices.TryGet("g923_xbox", out var d);
        var frame = new InputFrame();
        foreach (var (k, v) in values) frame.Values[k] = v;
        return new MappingEngine(p, d, Devices).Evaluate(frame, 0);
    }

    [Theory]
    [InlineData("P2:Button:A", TargetKind.Button, 2)]
    [InlineData("p2:RightTrigger", TargetKind.Axis, 2)]
    [InlineData("Pad2:LeftStickX", TargetKind.Axis, 2)]
    [InlineData("Button:A", TargetKind.Button, 1)]
    [InlineData("LeftStickX", TargetKind.Axis, 1)]
    public void Targets_know_which_controller_they_are_for(string text, TargetKind kind, int pad)
    {
        Assert.True(Target.TryParse(text, out var t, out var err), err);
        Assert.Equal(kind, t!.Kind);
        Assert.Equal(pad, t.Pad);
    }

    [Theory]
    [InlineData("P2:Key:Q")]
    [InlineData("P2:Mouse:Left")]
    [InlineData("P2:")]
    [InlineData("P2:Nonsense")]
    public void The_second_controller_takes_only_sticks_triggers_and_buttons(string text) =>
        Assert.False(Target.TryParse(text, out _, out _));

    [Fact]
    public void Rules_with_p2_targets_feed_a_separate_state()
    {
        var p = Load("""
            { "from": "wheel", "to": "LeftStickX" },
            { "from": "throttle", "to": "P2:RightTrigger" },
            { "from": "paddleR", "to": "P2:Button:A" },
            { "from": "paddleL", "to": "Button:B" }
            """);
        var s = Run(p, ("wheel", 1), ("throttle", 1), ("paddleR", 1), ("paddleL", 1));

        Assert.Equal(short.MaxValue, s.LeftX);
        Assert.Equal(0, s.RightTrigger);                 // the throttle went to the second controller, not this one
        Assert.False(s[PadButton.A]);
        Assert.True(s[PadButton.B]);

        Assert.NotNull(s.Second);
        Assert.Equal(255, s.Second!.RightTrigger);
        Assert.True(s.Second[PadButton.A]);
        Assert.Equal(0, s.Second.LeftX);
        Assert.False(s.Second[PadButton.B]);
    }

    [Fact]
    public void Without_the_option_there_is_no_second_state_and_p2_rules_do_nothing()
    {
        var r = ProfileLoader.Parse("""{ "name": "t", "outputs": { "secondPad": false }, "rules": [ { "from": "paddleR", "to": "P2:Button:A" } ] }""", Devices);
        Assert.True(r.Ok);
        Assert.Contains(r.Warnings, w => w.Contains("second controller") && w.Contains("secondPad"));
        var s = Run(r.Profile!, ("paddleR", 1));
        Assert.Null(s.Second);
        Assert.False(s[PadButton.A]);
    }

    [Fact]
    public void A_profile_with_the_option_but_no_p2_rules_still_gives_an_idle_second_controller()
    {
        var s = Run(Load("""{ "from": "wheel", "to": "LeftStickX" }"""), ("wheel", 1));
        Assert.NotNull(s.Second);
        Assert.Equal(0, s.Second!.LeftX);
    }

    [Fact]
    public void A_change_on_the_second_controller_alone_counts_as_a_change_to_send()
    {
        var p = Load("""{ "from": "paddleR", "to": "P2:Button:A" }""");
        var off = Run(p, ("paddleR", 0));
        var on = Run(p, ("paddleR", 1));
        Assert.False(off.SameAs(on));
        Assert.True(off.SameAs(Run(p, ("paddleR", 0))));

        var withoutSecond = new PadState();
        Assert.False(withoutSecond.SameAs(off));         // one has a second controller and the other does not
        Assert.False(off.SameAs(withoutSecond));
    }

    [Fact]
    public void The_option_and_the_targets_survive_saving()
    {
        var p = Load("""{ "from": "throttle", "to": "P2:RightTrigger" }""");
        string json = ProfileWriter.ToJson(p);
        Assert.Contains("\"secondPad\": true", json);
        Assert.Contains("\"to\": \"P2:RightTrigger\"", json);
        var again = ProfileLoader.Parse(json, Devices);
        Assert.True(again.Ok, string.Join("; ", again.Errors));
        Assert.True(again.Profile!.Outputs.SecondPad);

        Assert.DoesNotContain("secondPad", ProfileWriter.ToJson(Load("""{ "from": "wheel", "to": "LeftStickX" }""", second: false)));
    }

    [Fact]
    public void Smoothing_and_curves_work_the_same_on_the_second_controller()
    {
        var p = Load("""{ "from": "throttle", "to": "P2:RightTrigger", "curve": "expo", "exponent": 2 }""");
        Assert.Equal(64, Run(p, ("throttle", 0.5)).Second!.RightTrigger);   // 0.5 squared = 0.25 of 255
    }
}

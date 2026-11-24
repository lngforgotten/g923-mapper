using G923Mapper;

namespace G923Mapper.Tests;

public class ReadingTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();
    private static DeviceMap Wheel() { Devices.TryGet("g923_xbox", out var d); return d; }

    private static MappingEngine Engine(string rules, string extra = "")
    {
        var p = ProfileLoader.Parse("{ \"name\": \"t\", " + extra + " \"rules\": [ " + rules + " ] }", Devices).Profile!;
        return new MappingEngine(p, Wheel(), Devices);
    }

    private static InputFrame Frame(params (string Name, double Value)[] values)
    {
        var f = new InputFrame();
        foreach (var (n, v) in values) f.Values[n] = v;
        return f;
    }

    [Fact]
    public void An_axis_rule_reports_its_input_and_shaped_output_without_touching_the_pad_result()
    {
        var engine = Engine("""{ "from": "throttle", "to": "RightTrigger", "deadzone": 0.2 }""");
        var pad = engine.Evaluate(Frame(("throttle", 0.6)), 0);
        var r = Assert.Single(engine.Readings());
        Assert.False(r.IsButton);
        Assert.Equal(0.6, r.Input, 6);
        Assert.Equal(0.5, r.Output, 6);              // (0.6 - 0.2) / 0.8
        Assert.InRange(pad.RightTrigger, 127, 128);   // half way, give or take rounding
    }

    [Fact]
    public void A_button_rule_reports_the_threshold_and_whether_the_input_is_past_it()
    {
        var engine = Engine("""{ "from": "clutch", "to": "Button:A", "threshold": 0.35 }""");
        engine.Evaluate(Frame(("clutch", 0.2)), 0);
        var below = Assert.Single(engine.Readings());
        Assert.True(below.IsButton);
        Assert.Equal(0.35, below.Threshold, 6);
        Assert.False(below.Over);
        Assert.False(below.On);

        engine.Evaluate(Frame(("clutch", 0.4)), 10);
        var past = Assert.Single(engine.Readings());
        Assert.True(past.Over);
        Assert.True(past.On);
    }

    [Fact]
    public void A_toggle_rule_stays_on_after_the_input_goes_back_below_the_threshold()
    {
        var engine = Engine("""{ "from": "brake", "to": "Button:B", "mode": "toggle" }""");
        engine.Evaluate(Frame(("brake", 0.9)), 0);
        engine.Evaluate(Frame(("brake", 0.0)), 10);
        var r = Assert.Single(engine.Readings());
        Assert.False(r.Over);     // the pedal is up
        Assert.True(r.On);        // but the toggle is still on
        Assert.Equal(0.5, r.Threshold, 6);   // the default
    }

    [Fact]
    public void A_negative_threshold_on_the_wheel_fires_on_the_left_and_is_reported_as_signed()
    {
        var engine = Engine("""{ "from": "wheel", "to": "Button:X", "threshold": -0.5 }""");
        engine.Evaluate(Frame(("wheel", -0.7)), 0);
        var r = Assert.Single(engine.Readings());
        Assert.True(r.Signed);
        Assert.True(r.Over);
        Assert.True(r.On);
        engine.Evaluate(Frame(("wheel", 0.7)), 10);
        Assert.False(Assert.Single(engine.Readings()).Over);
    }

    [Fact]
    public void Rules_that_are_not_in_use_have_no_reading_and_the_order_follows_the_profile()
    {
        var engine = Engine("""
            { "from": "wheel", "to": "LeftStickX" }, { "from": "clutch", "to": "Key:Q" }, { "from": "throttle", "to": "RightTrigger" }
            """);   // strict profile: the key rule is not compiled in
        engine.Evaluate(Frame(("wheel", 0.5), ("throttle", 0.5)), 0);
        Assert.Equal(["LeftStickX", "RightTrigger"], engine.Readings().Select(r => r.Rule.To));
    }

    [Fact]
    public void The_readings_are_a_copy_that_later_frames_do_not_change()
    {
        var engine = Engine("""{ "from": "throttle", "to": "RightTrigger" }""");
        engine.Evaluate(Frame(("throttle", 0.25)), 0);
        var kept = engine.Readings();
        engine.Evaluate(Frame(("throttle", 0.75)), 10);
        Assert.Equal(0.25, kept[0].Input, 6);
        Assert.Equal(0.75, engine.Readings()[0].Input, 6);
    }

    [Fact]
    public void Gathering_readings_does_not_change_what_the_engine_sends()
    {
        const string rules = """
            { "from": "wheel", "to": "LeftStickX", "deadzone": 0.04 }, { "from": "throttle", "to": "RightTrigger", "curve": "expo" },
            { "from": "clutch", "to": "Button:A", "mode": "pulse" }
            """;
        var a = Engine(rules);
        var b = Engine(rules);
        for (int i = 0; i < 50; i++)
        {
            var frame = Frame(("wheel", Math.Sin(i / 5.0)), ("throttle", i / 50.0), ("clutch", i % 20 > 10 ? 1 : 0));
            var withReadings = a.Evaluate(frame, i * 10);
            a.Readings();
            var without = b.Evaluate(frame, i * 10);
            Assert.True(withReadings.SameAs(without));
        }
    }

    // ---------------------------------------------------------------- the words beside a gauge

    [Fact]
    public void An_axis_reading_reads_input_to_output_and_a_stick_output_is_signed()
    {
        var engine = Engine("""{ "from": "wheel", "to": "LeftStickX" }, { "from": "throttle", "to": "RightTrigger" }""");
        engine.Evaluate(Frame(("wheel", -0.5), ("throttle", 0.4)), 0);
        var rs = engine.Readings();
        Assert.Equal("in -50% -> out -50%", ReadingText.Describe(rs[0]));
        Assert.Equal("in 40% -> out 40%", ReadingText.Describe(rs[1]));
    }

    [Fact]
    public void A_button_reading_says_where_it_fires_and_whether_it_is_past_and_down()
    {
        var engine = Engine("""{ "from": "clutch", "to": "Button:A", "threshold": 0.35, "mode": "toggle" }""");
        engine.Evaluate(Frame(("clutch", 0.5)), 0);
        string text = ReadingText.Describe(Assert.Single(engine.Readings()));
        Assert.Contains("fires at 35% (toggle)", text);
        Assert.Contains("past it, button DOWN", text);
        engine.Evaluate(Frame(("clutch", 0.0)), 10);
        string later = ReadingText.Describe(Assert.Single(engine.Readings()));
        Assert.Contains("below it, button DOWN", later);   // toggled on, pedal released
    }
}

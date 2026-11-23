using G923Mapper;

namespace G923Mapper.Tests;

public class SmoothingTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();

    private static MappingEngine Engine(string rule)
    {
        var r = ProfileLoader.Parse("{ \"name\": \"t\", \"rules\": [ " + rule + " ] }", Devices);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Devices.TryGet("g923_xbox", out var d);
        return new MappingEngine(r.Profile!, d, Devices);
    }

    private static PadState Step(MappingEngine e, double wheel, long ms)
    {
        var frame = new InputFrame();
        frame.Values["wheel"] = wheel;
        return e.Evaluate(frame, ms);
    }

    [Fact]
    public void Without_smoothing_the_output_follows_the_input_at_once()
    {
        var e = Engine("""{ "from": "wheel", "to": "LeftStickX" }""");
        Step(e, 0, 0);
        Assert.Equal(short.MaxValue, Step(e, 1, 10).LeftX);
    }

    [Fact]
    public void With_smoothing_the_output_glides_towards_a_sudden_change()
    {
        var e = Engine("""{ "from": "wheel", "to": "LeftStickX", "smooth": 100 }""");
        Step(e, 0, 0);
        int after10 = Step(e, 1, 10).LeftX;
        int after100 = Step(e, 1, 100).LeftX;
        int after1000 = Step(e, 1, 1000).LeftX;
        Assert.InRange(after10, 1, 9000);                       // a small first step: about 9.5% of the way
        Assert.True(after100 > after10);
        Assert.InRange(after100, 14000, 24000);                 // by one time constant, well under all the way
        Assert.True(after1000 > 32000);                         // settles at the input
    }

    [Fact]
    public void The_first_frame_starts_at_the_input_so_there_is_no_swing_from_zero()
    {
        var e = Engine("""{ "from": "wheel", "to": "LeftStickX", "smooth": 200 }""");
        Assert.Equal(short.MaxValue, Step(e, 1, 5000).LeftX);
    }

    [Fact]
    public void A_long_pause_lets_the_value_catch_up_completely()
    {
        var e = Engine("""{ "from": "wheel", "to": "LeftStickX", "smooth": 100 }""");
        Step(e, -1, 0);
        Assert.Equal(short.MaxValue, Step(e, 1, 60_000).LeftX);
    }

    [Fact]
    public void A_bigger_time_constant_is_slower()
    {
        var quick = Engine("""{ "from": "wheel", "to": "LeftStickX", "smooth": 30 }""");
        var slow = Engine("""{ "from": "wheel", "to": "LeftStickX", "smooth": 300 }""");
        Step(quick, 0, 0); Step(slow, 0, 0);
        Assert.True(Step(quick, 1, 50).LeftX > Step(slow, 1, 50).LeftX);
    }

    [Fact]
    public void Smoothing_works_on_triggers_and_each_rule_has_its_own_state()
    {
        var e = Engine("""{ "from": "throttle", "to": "RightTrigger", "smooth": 100 }, { "from": "brake", "to": "LeftTrigger" }""");
        var frame = new InputFrame();
        frame.Values["throttle"] = 0; frame.Values["brake"] = 0;
        e.Evaluate(frame, 0);
        frame.Values["throttle"] = 1; frame.Values["brake"] = 1;
        var s = e.Evaluate(frame, 20);
        Assert.Equal(255, s.LeftTrigger);                       // unsmoothed rule is immediate
        Assert.InRange(s.RightTrigger, 1, 100);                 // the smoothed one is still catching up
    }

    [Fact]
    public void Smoothing_never_overshoots()
    {
        var e = Engine("""{ "from": "wheel", "to": "LeftStickX", "smooth": 50 }""");
        Step(e, 0, 0);
        int last = 0;
        for (long t = 5; t < 2000; t += 5)
        {
            int v = Step(e, 0.5, t).LeftX;
            Assert.True(v >= last);
            Assert.True(v <= 16384 + 1);
            last = v;
        }
    }

    [Fact]
    public void Validation_limits_the_time_and_warns_for_buttons()
    {
        Assert.False(ProfileLoader.Parse("""{ "name": "t", "rules": [ { "from": "wheel", "to": "LeftStickX", "smooth": 5000 } ] }""", Devices).Ok);
        Assert.False(ProfileLoader.Parse("""{ "name": "t", "rules": [ { "from": "wheel", "to": "LeftStickX", "smooth": -1 } ] }""", Devices).Ok);
        var button = ProfileLoader.Parse("""{ "name": "t", "rules": [ { "from": "paddleR", "to": "Button:A", "smooth": 50 } ] }""", Devices);
        Assert.True(button.Ok);
        Assert.Contains(button.Warnings, w => w.Contains("smooth only applies"));
    }

    [Fact]
    public void The_setting_survives_saving_and_zero_is_not_written()
    {
        var p = ProfileLoader.Parse("""{ "name": "t", "rules": [ { "from": "wheel", "to": "LeftStickX", "smooth": 80 }, { "from": "throttle", "to": "RightTrigger" } ] }""", Devices).Profile!;
        string json = ProfileWriter.ToJson(p);
        Assert.Contains("\"smooth\": 80", json);
        Assert.Equal(1, json.Split("\"smooth\"").Length - 1);
        Assert.Equal(80, ProfileLoader.Parse(json, Devices).Profile!.Rules[0].Smooth);
    }
}

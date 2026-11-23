using G923Mapper;

namespace G923Mapper.Tests;

public class AxisPartTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();

    [Theory]
    [InlineData(0.6, true, 1, 0.6)]
    [InlineData(-0.6, true, 1, 0)]
    [InlineData(-0.6, true, -1, 0.6)]
    [InlineData(0.6, true, -1, 0)]
    [InlineData(0.75, false, 1, 0.5)]      // upper half of a centred pedal axis, stretched to 0..1
    [InlineData(0.25, false, 1, 0)]
    [InlineData(0.25, false, -1, 0.5)]
    [InlineData(1.0, false, 1, 1)]
    [InlineData(0.0, false, -1, 1)]
    [InlineData(0.5, false, 1, 0)]
    [InlineData(0.5, false, -1, 0)]
    [InlineData(0.3, true, 0, 0.3)]
    public void Half_axes_are_stretched_to_the_full_range(double v, bool signed, int part, double expected) =>
        Assert.Equal(expected, MappingEngine.HalfAxis(v, signed, part), 6);

    private static PadState Run(string rule, params (string, double)[] values)
    {
        var p = ProfileLoader.Parse("{ \"name\": \"t\", \"rules\": [ " + rule + " ] }", Devices);
        Assert.True(p.Ok, string.Join("; ", p.Errors));
        Devices.TryGet("g923_xbox", out var d);
        var frame = new InputFrame();
        foreach (var (k, v) in values) frame.Values[k] = v;
        return new MappingEngine(p.Profile!, d, Devices).Evaluate(frame, 0);
    }

    [Fact]
    public void The_wheel_can_be_split_into_two_triggers_without_an_expression()
    {
        const string rules = """{ "from": "wheel", "to": "RightTrigger", "part": "positive" }, { "from": "wheel", "to": "LeftTrigger", "part": "negative" }""";
        var right = Run(rules, ("wheel", 0.5));
        Assert.Equal(128, right.RightTrigger);
        Assert.Equal(0, right.LeftTrigger);
        var left = Run(rules, ("wheel", -1));
        Assert.Equal(255, left.LeftTrigger);
        Assert.Equal(0, left.RightTrigger);
    }

    [Fact]
    public void A_half_axis_can_press_a_button()
    {
        const string rule = """{ "from": "wheel", "to": "Button:A", "part": "negative", "threshold": 0.5 }""";
        Assert.True(Run(rule, ("wheel", -0.8))[PadButton.A]);
        Assert.False(Run(rule, ("wheel", -0.3))[PadButton.A]);
        Assert.False(Run(rule, ("wheel", 0.9))[PadButton.A]);
    }

    [Fact]
    public void A_bad_part_is_an_error_and_full_is_the_default()
    {
        var bad = ProfileLoader.Parse("""{ "name": "t", "rules": [ { "from": "wheel", "to": "RightTrigger", "part": "upper" } ] }""", Devices);
        Assert.False(bad.Ok);
        Assert.Contains(bad.Errors, e => e.Contains("part must be"));
        Assert.True(ProfileLoader.Parse("""{ "name": "t", "rules": [ { "from": "wheel", "to": "RightTrigger", "part": "FULL" } ] }""", Devices).Ok);
    }

    [Fact]
    public void The_part_survives_saving_and_full_is_not_written()
    {
        var p = ProfileLoader.Parse("""{ "name": "t", "rules": [ { "from": "wheel", "to": "RightTrigger", "part": "positive" }, { "from": "throttle", "to": "LeftTrigger", "part": "full" } ] }""", Devices).Profile!;
        string json = ProfileWriter.ToJson(p);
        Assert.Contains("\"part\": \"positive\"", json);
        Assert.Equal(1, json.Split("\"part\"").Length - 1);
        Assert.Equal("positive", ProfileLoader.Parse(json, Devices).Profile!.Rules[0].Part);
    }
}

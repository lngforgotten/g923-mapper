using G923Mapper;

namespace G923Mapper.Tests;

public class ExpressionTests
{
    private static double Eval(string text, double x = 0, params (string, double)[] others)
    {
        Assert.True(Expression.TryParse(text, out var e, out var err), err);
        return e!.Evaluate(n => n == "x" ? x : others.FirstOrDefault(o => o.Item1 == n).Item2);
    }

    [Theory]
    [InlineData("1 + 2 * 3", 7, 0)]
    [InlineData("(1 + 2) * 3", 9, 0)]
    [InlineData("-x", -0.25, 0.25)]
    [InlineData("max(0, x)", 0.25, 0.25)]
    [InlineData("max(0, -x)", 0, 0.25)]
    [InlineData("max(0, -x)", 0.5, -0.5)]
    [InlineData("abs(x)", 0.4, -0.4)]
    [InlineData("min(x, 0.3)", 0.3, 0.9)]
    [InlineData("clamp(x, 0, 1)", 1, 5)]
    [InlineData("clamp(x, 1, 0)", 0, -3)]
    [InlineData("pow(x, 2)", 0.25, 0.5)]
    [InlineData("sqrt(x)", 0.5, 0.25)]
    [InlineData("x * 2 - 1", 0, 0.5)]
    [InlineData("MAX(0, x)", 0.25, 0.25)]
    [InlineData(".5 + .25", 0.75, 0)]
    [InlineData("--x", 0.5, 0.5)]
    public void Evaluates_arithmetic_and_functions(string text, double expected, double x) =>
        Assert.Equal(expected, Eval(text, x), 6);

    [Fact]
    public void Other_names_are_looked_up_as_inputs()
    {
        Assert.Equal(0.7, Eval("brake - throttle", 0, ("brake", 0.9), ("throttle", 0.2)), 6);
        Assert.Equal(0.4, Eval("handbrake.axis * 2", 0, ("handbrake.axis", 0.2)), 6);
    }

    [Fact]
    public void Reports_the_names_used()
    {
        Expression.TryParse("max(x, brake) + brake - clutch", out var e, out _);
        Assert.Equal(new[] { "x", "brake", "clutch" }, e!.Names);
    }

    [Fact]
    public void Division_by_zero_gives_zero_instead_of_a_crash()
    {
        Assert.Equal(0, Eval("1 / 0"));
        Assert.Equal(0, Eval("x / (x - x)", 0.5));
        Assert.Equal(0, Eval("sqrt(-1)"));
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("1 +", "ends too soon")]
    [InlineData("(1 + 2", "missing")]
    [InlineData("1 + 2)", "unexpected")]
    [InlineData("foo(1)", "unknown function")]
    [InlineData("max(1)", "takes")]
    [InlineData("abs(1, 2)", "takes")]
    [InlineData("1 $ 2", "unexpected")]
    [InlineData("1..2", "not a number")]
    public void Bad_expressions_give_a_readable_error(string text, string phrase)
    {
        Assert.False(Expression.TryParse(text, out var e, out var err));
        Assert.Null(e);
        Assert.Contains(phrase, err);
    }
}

public class ExpressionRuleTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();

    private static Profile Load(string rules, string outputs = "{}", string safety = "")
    {
        string safetyLine = safety.Length == 0 ? "" : "\"safety\": \"" + safety + "\",";
        var r = ProfileLoader.Parse(
            "{ \"name\": \"t\", \"device\": \"g923_xbox\", " + safetyLine + " \"outputs\": " + outputs + ", \"rules\": [ " + rules + " ] }", Devices);
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

    [Fact]
    public void An_expression_can_split_the_wheel_axis_into_two_triggers()
    {
        var p = Load("""
            { "from": "wheel", "to": "RightTrigger", "expr": "max(0, x)" },
            { "from": "wheel", "to": "LeftTrigger",  "expr": "max(0, -x)" }
            """);
        var right = Run(p, ("wheel", 0.5));
        Assert.Equal(128, right.RightTrigger);
        Assert.Equal(0, right.LeftTrigger);
        var left = Run(p, ("wheel", -1));
        Assert.Equal(255, left.LeftTrigger);
        Assert.Equal(0, left.RightTrigger);
    }

    [Fact]
    public void An_expression_can_combine_two_inputs()
    {
        var p = Load("""{ "from": "throttle", "to": "RightTrigger", "expr": "x - brake" }""");
        Assert.Equal(128, Run(p, ("throttle", 1), ("brake", 0.5)).RightTrigger);
        Assert.Equal(0, Run(p, ("throttle", 0.2), ("brake", 0.5)).RightTrigger);
    }

    [Fact]
    public void An_expression_can_drive_a_button()
    {
        var p = Load("""{ "from": "throttle", "to": "Button:A", "expr": "x + brake", "threshold": 0.8 }""");
        Assert.True(Run(p, ("throttle", 0.5), ("brake", 0.5))[PadButton.A]);
        Assert.False(Run(p, ("throttle", 0.5), ("brake", 0.1))[PadButton.A]);
    }

    [Fact]
    public void A_bad_expression_or_unknown_input_stops_the_profile_loading_with_a_clear_message()
    {
        var r = ProfileLoader.Parse("""{ "name": "t", "device": "g923_xbox", "rules": [ { "from": "wheel", "to": "LeftStickX", "expr": "max(0," } ] }""", Devices);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Contains("expression"));

        var r2 = ProfileLoader.Parse("""{ "name": "t", "device": "g923_xbox", "rules": [ { "from": "wheel", "to": "LeftStickX", "expr": "x + nothing" } ] }""", Devices);
        Assert.False(r2.Ok);
        Assert.Contains(r2.Errors, e => e.Contains("nothing"));
    }

    [Fact]
    public void The_profile_writer_keeps_expressions_and_turbo()
    {
        var p = Load("""{ "from": "paddleR", "to": "Button:A", "turbo": true, "turboHz": 8, "expr": "x" }""", """{ "turbo": true }""", "standard");
        var again = ProfileLoader.Parse(ProfileWriter.ToJson(p), Devices).Profile!;
        Assert.Equal("x", again.Rules[0].Expr);
        Assert.True(again.Rules[0].Turbo);
        Assert.Equal(8, again.Rules[0].TurboHz);
        Assert.True(again.Outputs.Turbo);
    }
}

public class TurboTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();

    private static MappingEngine Engine(string safety, bool turboOn, string exe = "game.exe")
    {
        var r = ProfileLoader.Parse(
            "{ \"name\": \"t\", \"match\": { \"exe\": [\"" + exe + "\"] }, \"device\": \"g923_xbox\", \"safety\": \"" + safety + "\"," +
            " \"outputs\": { \"turbo\": " + (turboOn ? "true" : "false") + " }," +
            " \"rules\": [ { \"from\": \"paddleR\", \"to\": \"Button:A\", \"turbo\": true, \"turboHz\": 10 } ] }", Devices);
        Devices.TryGet("g923_xbox", out var d);
        return new MappingEngine(r.Profile!, d, Devices);
    }

    private static List<bool> Sample(MappingEngine e, bool held = true)
    {
        var frame = new InputFrame();
        frame.Values["paddleR"] = held ? 1 : 0;
        var seen = new List<bool>();
        for (long t = 1000; t < 1400; t += 10) seen.Add(e.Evaluate(frame, t)[PadButton.A]);
        return seen;
    }

    [Fact]
    public void Standard_profile_with_turbo_on_repeats_the_button_while_held()
    {
        var seen = Sample(Engine("standard", true));
        Assert.Contains(true, seen);
        Assert.Contains(false, seen);
        int changes = seen.Zip(seen.Skip(1), (a, b) => a != b).Count(c => c);
        Assert.InRange(changes, 6, 9);    // about 4 presses in 400 ms at 10 per second
        Assert.True(seen[0]);             // the first press is immediate
    }

    [Fact]
    public void Releasing_the_button_stops_the_repeat()
    {
        Assert.All(Sample(Engine("standard", true), held: false), on => Assert.False(on));
    }

    [Theory]
    [InlineData("strict", true, "game.exe")]
    [InlineData("standard", false, "game.exe")]
    [InlineData("standard", true, "GTA5.exe")]
    public void Turbo_is_just_a_plain_hold_unless_standard_mode_the_flag_and_a_non_protected_game_all_agree(string safety, bool flag, string exe)
    {
        var seen = Sample(Engine(safety, flag, exe));
        Assert.All(seen, on => Assert.True(on));
    }

    [Fact]
    public void Turbo_in_strict_mode_is_reported_as_a_warning()
    {
        var r = ProfileLoader.Parse("""
            { "name": "t", "device": "g923_xbox", "safety": "strict", "outputs": { "turbo": true },
              "rules": [ { "from": "paddleR", "to": "Button:A", "turbo": true } ] }
            """, Devices);
        Assert.True(r.Ok);
        Assert.Contains(r.Warnings, w => w.Contains("Turbo") && w.Contains("strict"));
        Assert.Contains(r.Warnings, w => w.Contains("Rule 1") && w.Contains("turbo"));
    }

    [Fact]
    public void Turbo_on_an_axis_rule_is_ignored_with_a_warning()
    {
        var r = ProfileLoader.Parse("""
            { "name": "t", "device": "g923_xbox", "safety": "standard", "outputs": { "turbo": true },
              "rules": [ { "from": "throttle", "to": "RightTrigger", "turbo": true } ] }
            """, Devices);
        Assert.True(r.Ok);
        Assert.Contains(r.Warnings, w => w.Contains("only applies to controller button"));
    }

    [Fact]
    public void An_out_of_range_rate_is_an_error()
    {
        var r = ProfileLoader.Parse("""{ "name": "t", "device": "g923_xbox", "rules": [ { "from": "paddleR", "to": "Button:A", "turbo": true, "turboHz": 99 } ] }""", Devices);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Contains("turboHz"));
    }
}

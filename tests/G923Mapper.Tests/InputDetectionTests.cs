using G923Mapper;

namespace G923Mapper.Tests;

public class InputDetectorTests
{
    private static readonly DeviceMap G = DeviceRegistry.G923Xbox();

    private static RawInput Rest() => T.Raw();

    [Fact]
    public void A_named_button_is_reported_by_its_friendly_name()
    {
        var d = InputDetector.Detect(Rest(), T.Raw(buttons: T.Buttons(6)), G)!;
        Assert.Equal("paddleR", d.Name);
        Assert.Contains("Button 6", d.Description);
    }

    [Fact]
    public void An_unnamed_button_is_reported_as_buttonN()
    {
        var d = InputDetector.Detect(Rest(), T.Raw(buttons: T.Buttons(17)), G)!;
        Assert.Equal("button17", d.Name);
    }

    [Fact]
    public void A_button_already_down_in_the_baseline_is_ignored()
    {
        var baseline = T.Raw(buttons: T.Buttons(5));
        Assert.Null(InputDetector.Detect(baseline, T.Raw(buttons: T.Buttons(5)), G));
        Assert.Equal("button9", InputDetector.Detect(baseline, T.Raw(buttons: T.Buttons(5, 9)), G)!.Name);
    }

    [Fact]
    public void Pedals_are_detected_by_name_whichever_axis_they_use()
    {
        Assert.Equal("throttle", InputDetector.Detect(Rest(), T.Raw(throttle: 0), G)!.Name);
        Assert.Equal("brake", InputDetector.Detect(Rest(), T.Raw(brake: 0), G)!.Name);
        Assert.Equal("clutch", InputDetector.Detect(Rest(), T.Raw(clutch: 0), G)!.Name);
    }

    [Fact]
    public void The_wheel_is_detected_when_turned_a_long_way()
        => Assert.Equal("wheel", InputDetector.Detect(Rest(), T.Raw(wheel: 60000), G)!.Name);

    [Fact]
    public void Small_movements_and_noise_are_not_a_gesture()
    {
        Assert.Null(InputDetector.Detect(Rest(), T.Raw(wheel: 32768 + 500, throttle: 65535 - 300), G));
        Assert.Null(InputDetector.Detect(Rest(), Rest(), G));
    }

    [Fact]
    public void A_half_pressed_pedal_counts()
        => Assert.Equal("brake", InputDetector.Detect(Rest(), T.Raw(brake: 30000), G)!.Name);

    [Theory]
    [InlineData(0, "dpadUp")]
    [InlineData(9000, "dpadRight")]
    [InlineData(18000, "dpadDown")]
    [InlineData(27000, "dpadLeft")]
    public void Hat_directions_are_reported(int pov, string expected)
        => Assert.Equal(expected, InputDetector.Detect(Rest(), T.Raw(pov: pov), G)!.Name);

    [Fact]
    public void A_button_beats_a_simultaneous_axis_movement()
        => Assert.Equal("paddleL", InputDetector.Detect(Rest(), T.Raw(wheel: 60000, buttons: T.Buttons(5)), G)!.Name);

    [Fact]
    public void An_axis_that_is_not_in_the_device_map_is_explained_not_guessed()
    {
        var axes = new int[8]; axes[2] = 0;
        var baseline = new RawInput { Axes = [33000, 65535, 0, 0, 0, 65535, 65535, 0], Buttons = new bool[23] };
        var moved = new RawInput { Axes = [33000, 65535, 0, 0, 40000, 65535, 65535, 0], Buttons = new bool[23] };   // RY, unmapped on the G923
        var d = InputDetector.Detect(baseline, moved, G)!;
        Assert.Null(d.Name);
        Assert.Contains("RY", d.Description);
    }

    [Fact]
    public void Nothing_is_detected_while_the_device_is_still_stale()
    {
        var stale = T.Raw(live: false);
        Assert.Null(InputDetector.Detect(stale, T.Raw(buttons: T.Buttons(5)), G));
        Assert.Null(InputDetector.Detect(Rest(), T.Raw(buttons: T.Buttons(5), live: false), G));
    }

    [Fact]
    public void Detected_names_are_valid_profile_inputs()
    {
        foreach (var raw in new[] { T.Raw(buttons: T.Buttons(6)), T.Raw(buttons: T.Buttons(20)), T.Raw(throttle: 0), T.Raw(pov: 0), T.Raw(wheel: 0) })
            Assert.True(G.IsKnownInput(InputDetector.Detect(Rest(), raw, G)!.Name!));
    }
}

public class ButtonNamingTests
{
    private static readonly DeviceMap G = DeviceRegistry.G923Xbox();
    private static readonly Dictionary<string, int> Others = new(StringComparer.OrdinalIgnoreCase) { ["paddleL"] = 5, ["paddleR"] = 6 };

    [Theory]
    [InlineData("shiftUp")]
    [InlineData("a")]
    [InlineData("Gear_2")]
    public void Good_names_pass(string name) => Assert.Null(ButtonNaming.Validate(name, G, Others, 7));

    [Theory]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("1st")]
    [InlineData("shift-up")]
    [InlineData("ñ")]
    public void Malformed_names_are_rejected(string name) => Assert.NotNull(ButtonNaming.Validate(name, G, Others, 7));

    [Theory]
    [InlineData("button3")]
    [InlineData("Button17")]
    [InlineData("button")]
    public void The_automatic_names_are_reserved(string name) => Assert.Contains("reserved", ButtonNaming.Validate(name, G, Others, 7) ?? "reserved-missing");

    [Theory]
    [InlineData("wheel")]
    [InlineData("Throttle")]
    [InlineData("clutch")]
    public void Axis_names_are_taken(string name) => Assert.Contains("axis", ButtonNaming.Validate(name, G, Others, 7));

    [Fact]
    public void Hat_names_are_taken() => Assert.Contains("D-pad", ButtonNaming.Validate("dpadUp", G, Others, 7));

    [Fact]
    public void Another_buttons_name_is_taken_but_your_own_is_fine()
    {
        Assert.Contains("already used for button 5", ButtonNaming.Validate("PADDLEL", G, Others, 7));   // case-insensitive
        Assert.Null(ButtonNaming.Validate("paddleL", G, Others, 5));                                       // keeping your own name
    }

    [Fact]
    public void Removed_lists_names_that_disappeared_or_moved()
    {
        var before = new Dictionary<string, int> { ["paddleL"] = 5, ["paddleR"] = 6, ["gearUp"] = 9 };
        var after = new Dictionary<string, int> { ["paddleL"] = 5, ["paddleR"] = 7 };
        Assert.Equal(["paddleR", "gearUp"], ButtonNaming.Removed(before, after).OrderBy(x => x == "gearUp"));
    }

    [Fact]
    public void A_new_name_works_end_to_end_as_a_profile_input_and_a_rule()
    {
        var device = DeviceRegistry.G923Xbox();
        device.Buttons = new Dictionary<string, int>(device.Buttons, StringComparer.OrdinalIgnoreCase) { ["gearUp"] = 9 };
        var reg = DeviceRegistry.CreateDefault();
        reg.Add(device);

        var r = ProfileLoader.Parse("""{ "name": "t", "rules": [ { "from": "gearUp", "to": "Button:Y" } ] }""", reg);
        Assert.True(r.Ok, string.Join("; ", r.Errors));

        var engine = new MappingEngine(r.Profile!, device);
        var raw = T.Raw(buttons: T.Buttons(9));
        Assert.True(engine.Evaluate(device.Normalize(raw), 0)[PadButton.Y]);
        Assert.False(engine.Evaluate(device.Normalize(T.Raw()), 0)[PadButton.Y]);
    }

    [Fact]
    public void Every_wheel_button_and_pedal_can_drive_any_output()
    {
        // The user-facing promise: any of the wheel's 23 buttons, each pedal and the hat can map to any pad output.
        var targets = Enum.GetNames<PadButton>().Select(b => "Button:" + b).Concat(Enum.GetNames<PadAxis>()).ToList();
        var inputs = Enumerable.Range(1, 23).Select(i => "button" + i)
            .Concat(["throttle", "brake", "clutch", "wheel", "dpadUp", "dpadDown", "dpadLeft", "dpadRight"]).ToList();
        foreach (string from in inputs)
            foreach (string to in targets)
            {
                var r = ProfileLoader.Parse($$"""{ "name": "t", "rules": [ { "from": "{{from}}", "to": "{{to}}" } ] }""", T.Devices);
                Assert.True(r.Ok, $"{from} -> {to}: " + string.Join("; ", r.Errors));
            }
    }
}

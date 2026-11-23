using G923Mapper;

namespace G923Mapper.Tests;

public class EngineTests
{
    private const string Steer = """{ "from": "wheel", "to": "LeftStickX" }""";

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(1.0, 32767)]
    [InlineData(-1.0, -32768)]
    public void Steering_defaults_to_raw_one_to_one(double wheel, int expected)
    {
        var s = T.Engine(Steer).Evaluate(T.Frame(("wheel", wheel)), 0);
        Assert.Equal(expected, s.LeftX);
    }

    [Fact]
    public void Steering_is_symmetric_left_and_right()
    {
        var e = T.Engine(Steer);
        foreach (double v in new[] { 0.1, 0.33, 0.5, 0.9 })
        {
            int right = e.Evaluate(T.Frame(("wheel", v)), 0).LeftX;
            int left = e.Evaluate(T.Frame(("wheel", -v)), 0).LeftX;
            Assert.InRange(right + left, -1, 1);   // mirror images, apart from the one-count short asymmetry
        }
    }

    [Fact]
    public void Range_limits_wheel_travel_to_the_given_degrees()
    {
        // 900 degree wheel, 540 degrees mapped to full stick: 60% of travel is full lock.
        var e = T.Engine("""{ "from": "wheel", "to": "LeftStickX", "range": 540 }""");
        Assert.Equal(32767, e.Evaluate(T.Frame(("wheel", 0.6)), 0).LeftX);
        Assert.Equal(32767, e.Evaluate(T.Frame(("wheel", 1.0)), 0).LeftX);     // clamped
        Assert.Equal(-32768, e.Evaluate(T.Frame(("wheel", -0.6)), 0).LeftX);
        Assert.InRange(e.Evaluate(T.Frame(("wheel", 0.3)), 0).LeftX, 16383, 16384);   // half of 540 = half stick
    }

    [Fact]
    public void Wheel_antiDeadzone_gives_a_small_nonzero_start_but_zero_at_centre()
    {
        var e = T.Engine("""{ "from": "wheel", "to": "LeftStickX", "antiDeadzone": 0.06 }""");
        Assert.Equal(0, e.Evaluate(T.Frame(("wheel", 0)), 0).LeftX);
        Assert.InRange(e.Evaluate(T.Frame(("wheel", 0.001)), 0).LeftX, 1900, 2100);   // about 6% of 32767
        Assert.Equal(32767, e.Evaluate(T.Frame(("wheel", 1)), 0).LeftX);
    }

    [Fact]
    public void Starter_GTA_profile_stays_still_when_the_centre_is_a_little_off()
    {
        // Regression: on the real wheel the rest position is a few hundred counts from the stored centre,
        // and with antiDeadzone but no deadzone that became a constant ~8% push.
        var dev = DeviceRegistry.G923Xbox();
        var profile = ProfileLoader.Parse(DefaultProfiles.GtaV, T.Devices).Profile!;
        var engine = new MappingEngine(profile, dev);

        // 700 counts off centre (the most seen on the real wheel) is 0.021 of travel; the 540 degree range
        // scales that to 0.036, which is inside the starter profile's 0.04 deadzone.
        Assert.Equal(0, engine.Evaluate(dev.Normalize(T.Raw(wheel: 32768 + 700)), 0).LeftX);
        Assert.Equal(0, engine.Evaluate(dev.Normalize(T.Raw(wheel: 32768 - 700)), 0).LeftX);
        // Real steering still gets the anti-deadzone lift and reaches full lock.
        Assert.True(engine.Evaluate(dev.Normalize(T.Raw(wheel: 32768 + 3000)), 0).LeftX > 2000);
        Assert.Equal(32767, engine.Evaluate(dev.Normalize(T.Raw(wheel: 65535)), 0).LeftX);

        // And the case that went wrong: anti-deadzone without a deadzone pushes on tiny noise.
        var bad = T.Engine("""{ "from": "wheel", "to": "LeftStickX", "antiDeadzone": 0.06, "range": 540 }""");
        Assert.True(Math.Abs(bad.Evaluate(dev.Normalize(T.Raw(wheel: 32768 + 5)), 0).LeftX) > 1800);
    }

    [Fact]
    public void Invert_flips_a_signed_axis()
    {
        var e = T.Engine("""{ "from": "wheel", "to": "LeftStickX", "invert": true }""");
        Assert.Equal(-32768, e.Evaluate(T.Frame(("wheel", 1)), 0).LeftX);
        Assert.Equal(32767, e.Evaluate(T.Frame(("wheel", -1)), 0).LeftX);
    }

    [Fact]
    public void Invert_flips_a_pedal_about_its_range()
    {
        var e = T.Engine("""{ "from": "throttle", "to": "RightTrigger", "invert": true }""");
        Assert.Equal(255, e.Evaluate(T.Frame(("throttle", 0)), 0).RightTrigger);
        Assert.Equal(0, e.Evaluate(T.Frame(("throttle", 1)), 0).RightTrigger);
    }

    [Fact]
    public void Pedals_drive_triggers()
    {
        var e = T.Engine(
            """{ "from": "throttle", "to": "RightTrigger" }""",
            """{ "from": "brake", "to": "LeftTrigger" }""");
        var s = e.Evaluate(T.Frame(("throttle", 1), ("brake", 0.5)), 0);
        Assert.Equal(255, s.RightTrigger);
        Assert.Equal(128, s.LeftTrigger);
        s = e.Evaluate(T.Frame(), 0);
        Assert.Equal(0, s.RightTrigger);
        Assert.Equal(0, s.LeftTrigger);
    }

    [Fact]
    public void Expo_curve_softens_the_middle_of_a_pedal()
    {
        var e = T.Engine("""{ "from": "throttle", "to": "RightTrigger", "curve": "expo" }""");
        Assert.Equal(64, e.Evaluate(T.Frame(("throttle", 0.5)), 0).RightTrigger);   // 0.25 * 255
    }

    [Fact]
    public void Full_chain_runs_in_order_invert_range_deadzone_curve_antiDeadzone()
    {
        // wheel -0.3 inverted = 0.3; range 540 scales it to 0.5; deadzone 0.1 -> 0.444..;
        // squared -> 0.1975; anti-deadzone 0.1 -> 0.1 + 0.9 * 0.1975 = 0.27778
        var e = T.Engine("""{ "from": "wheel", "to": "LeftStickX", "invert": true, "range": 540, "deadzone": 0.1, "curve": "expo", "antiDeadzone": 0.1 }""");
        int expected = (int)Math.Round(0.2777777 * 32767);
        Assert.InRange(e.Evaluate(T.Frame(("wheel", -0.3)), 0).LeftX, expected - 2, expected + 2);
    }

    // --- buttons ---

    [Fact]
    public void Axis_drives_a_button_through_a_threshold()
    {
        var e = T.Engine("""{ "from": "clutch", "to": "Button:RightThumb", "threshold": 0.4, "mode": "hold" }""");
        Assert.False(e.Evaluate(T.Frame(("clutch", 0.39)), 0)[PadButton.RightThumb]);
        Assert.True(e.Evaluate(T.Frame(("clutch", 0.40)), 0)[PadButton.RightThumb]);
        Assert.True(e.Evaluate(T.Frame(("clutch", 1.0)), 0)[PadButton.RightThumb]);
        Assert.False(e.Evaluate(T.Frame(("clutch", 0.0)), 0)[PadButton.RightThumb]);   // released
    }

    [Fact]
    public void Threshold_defaults_to_half()
    {
        var e = T.Engine("""{ "from": "clutch", "to": "Button:A" }""");
        Assert.False(e.Evaluate(T.Frame(("clutch", 0.49)), 0)[PadButton.A]);
        Assert.True(e.Evaluate(T.Frame(("clutch", 0.5)), 0)[PadButton.A]);
    }

    [Fact]
    public void Negative_threshold_fires_on_the_wheel_turning_left()
    {
        var e = T.Engine("""{ "from": "wheel", "to": "Button:X", "threshold": -0.5 }""");
        Assert.True(e.Evaluate(T.Frame(("wheel", -0.6)), 0)[PadButton.X]);
        Assert.False(e.Evaluate(T.Frame(("wheel", -0.4)), 0)[PadButton.X]);
        Assert.False(e.Evaluate(T.Frame(("wheel", 0.9)), 0)[PadButton.X]);
    }

    [Fact]
    public void Button_drives_button()
    {
        var e = T.Engine("""{ "from": "paddleR", "to": "Button:RightShoulder" }""");
        Assert.True(e.Evaluate(T.Frame(("paddleR", 1)), 0)[PadButton.RightShoulder]);
        Assert.False(e.Evaluate(T.Frame(("paddleR", 0)), 0)[PadButton.RightShoulder]);
    }

    [Fact]
    public void Button_can_drive_an_axis()
    {
        var e = T.Engine("""{ "from": "paddleR", "to": "RightTrigger" }""");
        Assert.Equal(255, e.Evaluate(T.Frame(("paddleR", 1)), 0).RightTrigger);
    }

    [Fact]
    public void Several_inputs_can_drive_the_same_button_and_it_ORs_them()
    {
        var e = T.Engine(
            """{ "from": "paddleL", "to": "Button:LeftShoulder" }""",
            """{ "from": "clutch", "to": "Button:LeftShoulder" }""");
        Assert.True(e.Evaluate(T.Frame(("paddleL", 1)), 0)[PadButton.LeftShoulder]);
        Assert.True(e.Evaluate(T.Frame(("clutch", 1)), 0)[PadButton.LeftShoulder]);
        Assert.False(e.Evaluate(T.Frame(), 0)[PadButton.LeftShoulder]);
    }

    [Fact]
    public void Several_rules_on_one_axis_add_and_clamp()
    {
        var e = T.Engine(
            """{ "from": "wheel", "to": "LeftStickX" }""",
            """{ "from": "paddleR", "to": "LeftStickX" }""");
        Assert.Equal(32767, e.Evaluate(T.Frame(("wheel", 0.8), ("paddleR", 1)), 0).LeftX);   // 1.8 clamped
        Assert.Equal(0, e.Evaluate(T.Frame(("wheel", -1), ("paddleR", 1)), 0).LeftX);        // cancels
    }

    [Fact]
    public void Toggle_flips_on_each_press_and_ignores_release()
    {
        var e = T.Engine("""{ "from": "paddleR", "to": "Button:Y", "mode": "toggle" }""");
        bool Y(double v) => e.Evaluate(T.Frame(("paddleR", v)), 0)[PadButton.Y];
        Assert.False(Y(0));
        Assert.True(Y(1));     // press: on
        Assert.True(Y(1));     // held: still on, no re-toggle
        Assert.True(Y(0));     // release: stays on
        Assert.False(Y(1));    // second press: off
        Assert.False(Y(0));
        Assert.True(Y(1));     // third press: on
    }

    [Fact]
    public void Pulse_presses_for_a_fixed_time_then_releases_even_while_held()
    {
        var e = T.Engine("""{ "from": "paddleR", "to": "Button:B", "mode": "pulse", "pulseMs": 50 }""");
        bool B(double v, long t) => e.Evaluate(T.Frame(("paddleR", v)), t)[PadButton.B];
        Assert.False(B(0, 0));
        Assert.True(B(1, 1000));       // press
        Assert.True(B(1, 1049));       // still within 50 ms
        Assert.False(B(1, 1050));      // over, although the paddle is still held
        Assert.False(B(1, 1500));      // held: no repeat (no turbo)
        Assert.False(B(0, 1600));
        Assert.True(B(1, 2000));       // a new physical press gives a new pulse
        Assert.False(B(0, 2100));
    }

    [Fact]
    public void Keyboard_targets_are_ignored_not_fatal()
    {
        var e = T.Engine("""{ "from": "paddleR", "to": "Key:Q" }""", Steer);
        var s = e.Evaluate(T.Frame(("paddleR", 1), ("wheel", 1)), 0);
        Assert.Equal(32767, s.LeftX);
    }

    [Fact]
    public void Unknown_inputs_read_zero()
    {
        var e = T.Engine(Steer);
        Assert.Equal(0, e.Evaluate(T.Frame(), 0).LeftX);
    }

    [Fact]
    public void Whole_pipeline_from_raw_reading_to_pad_state()
    {
        var dev = DeviceRegistry.G923Xbox();
        var e = T.Engine(
            """{ "from": "wheel", "to": "LeftStickX" }""",
            """{ "from": "throttle", "to": "RightTrigger" }""",
            """{ "from": "brake", "to": "LeftTrigger" }""",
            """{ "from": "paddleR", "to": "Button:RightShoulder" }""");

        // Rest: centred wheel, pedals up, nothing pressed.
        var rest = e.Evaluate(dev.Normalize(T.Raw()), 0);
        Assert.Equal(0, rest.LeftX);
        Assert.Equal(0, rest.RightTrigger);
        Assert.Equal(0, rest.LeftTrigger);

        // Full right lock, throttle floored, brake half, paddle 6 down.
        var s = e.Evaluate(dev.Normalize(T.Raw(wheel: 65535, throttle: 0, brake: 32768, buttons: T.Buttons(6))), 0);
        Assert.Equal(32767, s.LeftX);
        Assert.Equal(255, s.RightTrigger);
        Assert.InRange((int)s.LeftTrigger, 127, 128);
        Assert.True(s[PadButton.RightShoulder]);

        // Stale first report: pedals must not look half pressed.
        var stale = e.Evaluate(dev.Normalize(T.Raw(throttle: 32768, brake: 32768, live: false)), 0);
        Assert.Equal(0, stale.RightTrigger);
        Assert.Equal(0, stale.LeftTrigger);
    }

    [Fact]
    public void PadState_writes_everything_to_the_pad_and_submits_once()
    {
        var state = new PadState { LeftX = 1234, RightY = -5, LeftTrigger = 7, RightTrigger = 200 };
        state[PadButton.A] = true;
        state[PadButton.DPadLeft] = true;
        var pad = new FakePad();
        state.WriteTo(pad);
        Assert.Equal(1234, pad.LX);
        Assert.Equal(-5, pad.RY);
        Assert.Equal(7, pad.LT);
        Assert.Equal(200, pad.RT);
        Assert.Equal(new[] { PadButton.A, PadButton.DPadLeft }.OrderBy(x => x), pad.Down.OrderBy(x => x));
        Assert.Equal(1, pad.Submits);
    }
}

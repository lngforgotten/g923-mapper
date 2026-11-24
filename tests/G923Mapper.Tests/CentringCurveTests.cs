using G923Mapper;

namespace G923Mapper.Tests;

public class CentringCurveTests
{
    private static ForceSettings Curve(int strength = 50) => new()
    {
        CentringStyle = CentringCurve.StyleCurve, SpringPercent = strength, CentreDegrees = 10, PeakDegrees = 60,
        LighteningPercent = 40, DampingPercent = 0, FloorPercent = 0,
    };

    private const double Half = 450;

    [Fact]
    public void The_style_defaults_to_the_plain_spring_and_anything_unknown_means_the_spring()
    {
        Assert.Equal(CentringCurve.StyleSpring, new ForceSettings().CentringStyle);
        Assert.Equal(CentringCurve.StyleSpring, CentringCurve.NormaliseStyle("nonsense"));
        Assert.Equal(CentringCurve.StyleSpring, CentringCurve.NormaliseStyle(null));
        Assert.Equal(CentringCurve.StyleCurve, CentringCurve.NormaliseStyle(" Curve "));
        Assert.Equal("", new ForceSettings().CentringWheel);
        Assert.False(new ForceSettings().CentringReversed);
    }

    [Fact]
    public void The_shape_is_zero_in_the_middle_one_at_the_peak_and_the_lightening_share_at_full_lock()
    {
        var s = Curve(5);
        Assert.Equal(0, CentringCurve.Shape(0, s, Half), 6);
        Assert.Equal(1, CentringCurve.Shape(60, s, Half), 6);
        Assert.Equal(0.4, CentringCurve.Shape(450, s, Half), 6);
        Assert.Equal(0.85, CentringCurve.Shape(10, s, Half), 6);      // the end of the centre ramp
    }

    [Fact]
    public void The_shape_is_the_same_on_both_sides_and_stays_between_zero_and_one()
    {
        var s = Curve(5);
        for (double a = 0; a <= 450; a += 7.5)
        {
            double shape = CentringCurve.Shape(a, s, Half);
            Assert.Equal(shape, CentringCurve.Shape(-a, s, Half), 9);
            Assert.InRange(shape, 0, 1);
        }
    }

    [Fact]
    public void The_shape_rises_to_the_peak_then_only_falls()
    {
        var s = Curve(5);
        double last = -1;
        for (double a = 0; a <= 60; a += 2) { double v = CentringCurve.Shape(a, s, Half); Assert.True(v >= last - 1e-9, $"{a}"); last = v; }
        for (double a = 60; a <= 450; a += 5) { double v = CentringCurve.Shape(a, s, Half); Assert.True(v <= last + 1e-9, $"{a}"); last = v; }
    }

    [Fact]
    public void Nonsense_numbers_cannot_break_the_shape()
    {
        var s = new ForceSettings { CentreDegrees = 500, PeakDegrees = -5, LighteningPercent = 900 };
        foreach (double a in new[] { 0, 1, 90, 449, 900 })
            Assert.InRange(CentringCurve.Shape(a, s, Half), 0, 1);
        Assert.InRange(CentringCurve.Shape(10, s, 3), 0, 1);       // a tiny wheel range too
    }

    [Fact]
    public void The_force_pulls_towards_the_middle_whichever_side_the_wheel_is_on()
    {
        var s = Curve(5);
        Assert.True(CentringCurve.Force(40, 0, s, Half) < 0);        // turned towards higher readings: pulled back towards lower ones
        Assert.True(CentringCurve.Force(-40, 0, s, Half) > 0);
        Assert.Equal(0, CentringCurve.Force(0, 0, s, Half), 9);
        Assert.Equal(-CentringCurve.Force(40, 0, s, Half), CentringCurve.Force(-40, 0, s, Half), 9);
    }

    [Fact]
    public void The_strength_scales_the_force_and_zero_turns_the_curve_off()
    {
        Assert.Equal(-0.25, CentringCurve.Force(60, 0, Curve(50), Half), 6);     // 100% would be half of full force; 50% is a quarter
        Assert.Equal(-0.125, CentringCurve.Force(60, 0, Curve(25), Half), 6);
        var off = Curve(0);
        off.FloorPercent = 20;
        off.DampingPercent = 100;
        Assert.Equal(0, CentringCurve.Force(60, 200, off, Half));
    }

    [Fact]
    public void The_floor_stops_the_force_falling_below_what_it_takes_to_turn_the_wheel_but_not_in_the_dead_middle()
    {
        var s = Curve(10);                                     // far weaker than the floor
        s.LighteningPercent = 0;                               // and nothing left at full lock
        s.FloorPercent = 15;
        Assert.Equal(-0.15, CentringCurve.Force(450, 0, s, Half), 6);   // held up to the floor at full lock
        Assert.Equal(0.15, CentringCurve.Force(-300, 0, s, Half), 6);
        Assert.Equal(0, CentringCurve.Force(0, 0, s, Half), 9);         // the middle is still quiet
        Assert.True(Math.Abs(CentringCurve.Force(3, 0, s, Half)) < 0.15);   // and eases in over the centre zone
    }

    [Fact]
    public void The_floor_is_capped_so_a_bad_measurement_cannot_make_the_wheel_pull_hard()
    {
        var s = Curve(10);
        s.FloorPercent = 100;
        Assert.Equal(-CentringCurve.FloorMax, CentringCurve.Force(200, 0, s, Half), 6);
    }

    [Fact]
    public void Damping_opposes_the_turn_and_is_limited()
    {
        var s = Curve(50);
        s.DampingPercent = 50;
        double still = CentringCurve.Force(0, 0, s, Half);
        double turningUp = CentringCurve.Force(0, 150, s, Half);      // moving towards higher readings at the middle: pushed back down
        double turningDown = CentringCurve.Force(0, -150, s, Half);
        Assert.Equal(0, still, 9);
        Assert.True(turningUp < 0 && turningDown > 0);
        s.DampingPercent = 100;
        Assert.Equal(-CentringCurve.DampingMax, CentringCurve.Force(0, 5000, s, Half), 6);   // however fast, never more than the limit
    }

    [Fact]
    public void The_force_never_leaves_minus_one_to_one_and_converts_to_device_units()
    {
        var s = Curve(100);
        s.FloorPercent = 100; s.DampingPercent = 100;
        for (double a = -450; a <= 450; a += 30)
            Assert.InRange(CentringCurve.Force(a, 9999, s, Half), -1, 1);
        Assert.Equal(5000, CentringCurve.ToDevice(0.5, false));
        Assert.Equal(-5000, CentringCurve.ToDevice(0.5, true));      // a wheel that pushes the other way round
        Assert.Equal(ForceMath.Max, CentringCurve.ToDevice(7, false));
    }

    [Fact]
    public void Every_preset_is_sensible_and_applying_it_sets_exactly_its_values()
    {
        Assert.True(CentringCurve.Presets.Count >= 3);
        Assert.Equal(CentringCurve.Presets.Count, CentringCurve.Presets.Select(p => p.Name).Distinct().Count());
        foreach (var p in CentringCurve.Presets)
        {
            Assert.InRange(p.Strength, 5, 70);
            Assert.True(p.CentreDegrees < p.PeakDegrees, p.Name);
            Assert.InRange(p.LighteningPercent, 0, 100);
            var s = new ForceSettings();
            p.ApplyTo(s);
            Assert.True(p.Matches(s), p.Name);
            s.PeakDegrees++;
            Assert.False(p.Matches(s), p.Name);
        }
    }

    [Fact]
    public void The_graph_samples_run_from_full_left_to_full_right_and_are_odd_about_the_middle()
    {
        var v = CentringCurve.Sample(Curve(), Half, 91);
        Assert.Equal(91, v.Length);
        Assert.True(v[0] > 0 && v[^1] < 0);                 // full left pushes right (positive), full right pushes left
        Assert.Equal(-v[0], v[^1], 9);
        Assert.Equal(0, v[45], 9);
    }

    [Fact]
    public void The_curve_settings_survive_a_round_trip_through_json()
    {
        var s = Curve(33);
        s.FloorPercent = 12; s.CentringReversed = true; s.CentringWheel = "VID_046D PID_C26E";
        var back = System.Text.Json.JsonSerializer.Deserialize<ForceSettings>(System.Text.Json.JsonSerializer.Serialize(s))!;
        Assert.Equal(33, back.SpringPercent);
        Assert.Equal(12, back.FloorPercent);
        Assert.True(back.CentringReversed);
        Assert.Equal("VID_046D PID_C26E", back.CentringWheel);
        Assert.Equal(CentringCurve.StyleCurve, back.CentringStyle);
        // and a profile's own strength still lays over the app-wide curve settings
        var eff = ForceMath.Effective(s, new ProfileForce { Spring = 80 });
        Assert.Equal(80, eff.SpringPercent);
        Assert.Equal(CentringCurve.StyleCurve, eff.CentringStyle);
        Assert.Equal(12, eff.FloorPercent);
    }
}

public class FrictionProbeTests
{
    /// <summary>
    /// A wheel to measure. It stays put until the net force on it reaches <paramref name="breakaway"/>, then slips half a degree a step the way
    /// the net force points. The net force is the push (flipped if <paramref name="reversed"/>) plus a spring pulling towards <paramref name="springCentre"/>
    /// (<paramref name="springPerDegree"/> of force for each degree away from it), the way Logitech G HUB's own spring does.
    /// </summary>
    private static FrictionProbe Measure(double breakaway, bool reversed = false, double springPerDegree = 0, double springCentre = 0, double constantPull = 0, double start = 0, bool held = false)
    {
        var probe = new FrictionProbe();
        double position = start;
        for (long t = 0; t < 60_000 && !probe.Done; t += 5)
        {
            double push = probe.Step(position, t);
            if (held) continue;
            double net = (reversed ? -push : push) - springPerDegree * (position - springCentre) + constantPull;
            if (Math.Abs(net) >= breakaway && push != 0) position += Math.Sign(net) * 0.5;
        }
        return probe;
    }

    [Fact]
    public void A_plain_wheel_gives_its_breakaway_force_and_is_the_right_way_round()
    {
        var p = Measure(0.12);
        Assert.True(p.Done);
        Assert.Equal("", p.Problem);
        Assert.False(p.Reversed);
        Assert.InRange(p.Floor!.Value, 0.12, 0.12 + 0.02);
    }

    [Fact]
    public void A_wheel_that_turns_the_opposite_way_to_the_force_is_found_to_be_reversed()
    {
        var p = Measure(0.12, reversed: true);
        Assert.Equal("", p.Problem);
        Assert.True(p.Reversed);
        Assert.InRange(p.Floor!.Value, 0.12, 0.14);
    }

    [Fact]
    public void A_spring_already_pulling_the_wheel_does_not_fool_the_direction()
    {
        // The old one-way test read this as "reversed": the wheel rests 4 degrees off the spring's middle, so a small push the same way as the spring
        // gets it moving. Pushing both ways and comparing cancels the spring.
        foreach (bool reversed in new[] { false, true })
        {
            var p = Measure(breakaway: 0.10, reversed, springPerDegree: 0.012, springCentre: -4, start: 0);
            Assert.Equal("", p.Problem);
            Assert.Equal(reversed, p.Reversed);
            Assert.InRange(p.Floor!.Value, 0.05, 0.16);
        }
    }

    [Fact]
    public void Something_pulling_the_wheel_harder_than_the_pushes_is_reported_not_guessed_at()
    {
        var p = Measure(breakaway: 0.05, constantPull: 0.30);       // moves the same way whichever way it is pushed
        Assert.True(p.Done);
        Assert.Null(p.Floor);
        Assert.Contains("same way for both", p.Problem);
        Assert.Contains("G HUB", p.Problem);
    }

    [Fact]
    public void The_force_starts_at_nothing_ramps_slowly_and_is_dropped_the_moment_the_wheel_moves()
    {
        var p = new FrictionProbe();
        Assert.Equal(0, p.Step(0, 0));
        Assert.Equal(0.06 * 0.5, p.Step(0, 500), 6);
        Assert.Equal(0.06 * 1.0, p.Step(0, 1000), 6);
        Assert.Equal(0, p.Step(2.5, 1100));                       // it moved: the force is dropped at once
        Assert.False(p.Done);                                     // the second push still has to be made
        Assert.Equal(0, p.Step(2.5, 1500));                       // and the wheel is left alone to settle first
        Assert.Equal(0, p.Step(2.5, 1100 + FrictionProbe.SettleMs));
        Assert.True(Math.Abs(p.Step(2.5, 1100 + FrictionProbe.SettleMs + 5)) < 0.001);   // the second ramp starts from nothing
        double second = p.Step(2.5, 1100 + FrictionProbe.SettleMs + 500);
        Assert.Equal(-0.06 * 0.5, second, 6);                     // and pushes the other way
    }

    [Fact]
    public void A_wheel_that_is_held_gives_up_at_the_limit_with_a_message_and_no_result()
    {
        var p = Measure(0.12, held: true);
        Assert.True(p.Done);
        Assert.Null(p.Floor);
        Assert.Contains("hands", p.Problem);
    }

    [Fact]
    public void It_never_asks_for_more_than_the_limit_in_either_direction()
    {
        var p = new FrictionProbe();
        double most = 0;
        for (long t = 0; t < 60_000 && !p.Done; t += 10) most = Math.Max(most, Math.Abs(p.Step(0, t)));
        Assert.True(most <= FrictionProbe.GiveUp + 1e-9);
    }

    [Fact]
    public void A_wheel_that_stops_reporting_ends_the_measurement_with_no_force()
    {
        var p = new FrictionProbe();
        p.Step(0, 0);
        Assert.Equal(0, p.Step(0, 300, wheelLive: false));
        Assert.True(p.Done);
        Assert.Null(p.Floor);
        Assert.NotEqual("", p.Problem);
    }

    [Fact]
    public void The_measurement_does_not_depend_on_where_the_wheel_starts()
    {
        foreach (double start in new[] { -300.0, -3, 0, 7, 250 })
        {
            var p = Measure(0.10, start: start);
            Assert.Equal("", p.Problem);
            Assert.False(p.Reversed);
        }
    }
}

public class CurveStabilityTests
{
    [Fact]
    public void There_is_no_pull_in_the_last_degree_either_side_of_the_middle()
    {
        var s = new ForceSettings { CentringStyle = CentringCurve.StyleCurve, SpringPercent = 60, CentreDegrees = 4, FloorPercent = 20, DampingPercent = 0 };
        foreach (double a in new[] { 0, 0.3, -0.7, 0.99 })
            Assert.Equal(0, CentringCurve.Force(a, 0, s, 450), 9);
        Assert.NotEqual(0, CentringCurve.Force(1.5, 0, s, 450));
        Assert.Equal(-CentringCurve.Force(1.5, 0, s, 450), CentringCurve.Force(-1.5, 0, s, 450), 9);
    }

    [Fact]
    public void The_pull_just_outside_the_quiet_zone_is_gentle_so_there_is_no_jolt_at_its_edge()
    {
        var s = new ForceSettings { CentringStyle = CentringCurve.StyleCurve, SpringPercent = 30, CentreDegrees = 12, FloorPercent = 0, DampingPercent = 0 };
        Assert.True(Math.Abs(CentringCurve.Force(1.0, 0, s, 450)) < 0.03);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 200)]
    [InlineData(100, ForceMath.DamperMax)]
    [InlineData(250, ForceMath.DamperMax)]
    [InlineData(-3, 0)]
    public void The_curve_damping_slider_scales_to_a_damper_strength_and_is_clamped(int percent, int expected) =>
        Assert.Equal(expected, ForceMath.DamperCoefficient(percent));
}

public class CurveScalingTests
{
    private static ForceSettings Settings(int strength, int centre = 12, int floor = 0) => new()
    {
        CentringStyle = CentringCurve.StyleCurve, SpringPercent = strength, CentreDegrees = centre, PeakDegrees = 60,
        LighteningPercent = 40, DampingPercent = 0, FloorPercent = floor,
    };

    [Fact]
    public void Strength_100_is_half_of_the_wheels_full_force_not_all_of_it()
    {
        var s = Settings(100);
        double most = Enumerable.Range(0, 451).Max(a => Math.Abs(CentringCurve.Force(a, 0, s, 450)));
        Assert.Equal(CentringCurve.CurveMax, most, 3);
        Assert.Equal(0.5, CentringCurve.CurveMax);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(100)]
    public void The_pull_never_rises_faster_than_the_wheel_can_be_trusted_with(int strength)
    {
        // The wheel swung by itself, 18 degrees either side, when the pull rose to 85% of full force within 12 degrees. Whatever the strength and
        // however short the centre zone is set, the rise per degree is held to the limit.
        foreach (int centre in new[] { 1, 6, 12, 45 })
        {
            var s = Settings(strength, centre);
            double previous = CentringCurve.Force(1.0, 0, s, 450), worst = 0;
            for (double a = 2; a <= 120; a += 1)
            {
                double now = CentringCurve.Force(a, 0, s, 450);
                worst = Math.Max(worst, Math.Abs(now - previous));
                previous = now;
            }
            Assert.True(worst <= CentringCurve.MaxPullPerDegree * 1.05, $"strength {strength}, centre {centre}: {worst} per degree");
        }
    }

    [Fact]
    public void A_gentle_curve_keeps_the_centre_zone_that_was_set_and_a_strong_one_widens_it()
    {
        Assert.Equal(12, CentringCurve.EffectiveCentre(Settings(10, centre: 12)));
        Assert.True(CentringCurve.EffectiveCentre(Settings(100, centre: 12)) > 40);
        Assert.True(CentringCurve.EffectiveCentre(Settings(100, centre: 1)) <= 90);
        Assert.Equal(1, CentringCurve.EffectiveCentre(Settings(0, centre: 1)));
    }

    [Fact]
    public void The_floor_also_rises_no_faster_than_the_limit_over_the_centre_zone()
    {
        var s = Settings(10, centre: 3, floor: 30);          // a strong floor over a short zone
        double previous = CentringCurve.Force(1.0, 0, s, 450), worst = 0;
        for (double a = 2; a <= 20; a += 1)
        {
            double now = CentringCurve.Force(a, 0, s, 450);
            worst = Math.Max(worst, Math.Abs(now - previous));
            previous = now;
        }
        // The floor ramps over the centre zone, which is as short as set, so this one is allowed to be steeper than the pull itself:
        // it is the least force that turns the wheel, a few percent, not a strong pull. A measured floor is typically 5% or less.
        Assert.True(worst <= 0.30 / 3 + 1e-9);
    }
}

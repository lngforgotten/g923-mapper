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
        var s = Curve();
        Assert.Equal(0, CentringCurve.Shape(0, s, Half), 6);
        Assert.Equal(1, CentringCurve.Shape(60, s, Half), 6);
        Assert.Equal(0.4, CentringCurve.Shape(450, s, Half), 6);
        Assert.Equal(0.85, CentringCurve.Shape(10, s, Half), 6);      // the end of the centre ramp
    }

    [Fact]
    public void The_shape_is_the_same_on_both_sides_and_stays_between_zero_and_one()
    {
        var s = Curve();
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
        var s = Curve();
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
        var s = Curve();
        Assert.True(CentringCurve.Force(40, 0, s, Half) < 0);        // turned towards higher readings: pulled back towards lower ones
        Assert.True(CentringCurve.Force(-40, 0, s, Half) > 0);
        Assert.Equal(0, CentringCurve.Force(0, 0, s, Half), 9);
        Assert.Equal(-CentringCurve.Force(40, 0, s, Half), CentringCurve.Force(-40, 0, s, Half), 9);
    }

    [Fact]
    public void The_strength_scales_the_force_and_zero_turns_the_curve_off()
    {
        Assert.Equal(-0.5, CentringCurve.Force(60, 0, Curve(50), Half), 6);
        Assert.Equal(-0.25, CentringCurve.Force(60, 0, Curve(25), Half), 6);
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
    /// <summary>A wheel that does not move until it is pushed by at least <paramref name="breakaway"/>, then moves <paramref name="direction"/>.</summary>
    private static FrictionProbe Run(double startAngle, double breakaway, double direction, out double lastForce, bool stuck = false)
    {
        var probe = new FrictionProbe(startAngle);
        double angle = startAngle;
        lastForce = 0;
        for (long t = 0; t < 20_000 && !probe.Done; t += 5)
        {
            double force = probe.Step(angle, t);
            lastForce = force;
            if (!stuck && Math.Abs(force) >= breakaway) angle += direction * 0.5;   // it slips, a little each step
        }
        return probe;
    }

    [Fact]
    public void It_finds_the_force_at_which_the_wheel_starts_to_move()
    {
        var p = Run(0, 0.12, direction: 1, out _);
        Assert.True(p.Done);
        Assert.InRange(p.Floor!.Value, 0.12, 0.12 + FrictionProbe.RampPerSecond * 0.05 + 0.02);
        Assert.Equal("", p.Problem);
    }

    [Fact]
    public void A_wheel_that_moves_the_way_it_is_pushed_is_not_reversed_and_the_opposite_is()
    {
        // From the middle the force is sent towards higher readings (+1). A wheel that then moves up is the right way round.
        Assert.False(Run(0, 0.1, direction: +1, out _).Reversed);
        Assert.True(Run(0, 0.1, direction: -1, out _).Reversed);
        // Starting turned to the right (positive) the force is sent the other way (-1), so a wheel moving down is the right way round.
        Assert.False(Run(200, 0.1, direction: -1, out _).Reversed);
        Assert.True(Run(200, 0.1, direction: +1, out _).Reversed);
    }

    [Fact]
    public void The_force_starts_at_nothing_ramps_slowly_and_is_dropped_the_moment_the_wheel_moves()
    {
        var p = new FrictionProbe(0);
        Assert.Equal(0, p.Step(0, 0));
        Assert.Equal(0.06 * 0.5, Math.Abs(p.Step(0, 500)), 6);
        Assert.Equal(0.06 * 1.0, Math.Abs(p.Step(0, 1000)), 6);
        Assert.Equal(0, p.Step(2.5, 1100));                       // it moved: the force is dropped at once
        Assert.True(p.Done);
        Assert.Equal(0, p.Step(2.5, 1200));                       // and stays off
    }

    [Fact]
    public void A_wheel_that_is_held_gives_up_at_the_limit_with_a_message_and_no_result()
    {
        var p = Run(0, 1, direction: 1, out double last, stuck: true);
        Assert.True(p.Done);
        Assert.Null(p.Floor);
        Assert.Contains("hands", p.Problem);
        Assert.Equal(0, last);
    }

    [Fact]
    public void It_never_asks_for_more_than_the_limit()
    {
        var p = new FrictionProbe(0);
        double most = 0;
        for (long t = 0; t < 30_000 && !p.Done; t += 10) most = Math.Max(most, Math.Abs(p.Step(0, t)));
        Assert.True(most <= FrictionProbe.GiveUp + 1e-9);
    }

    [Fact]
    public void A_wheel_that_stops_reporting_ends_the_measurement_with_no_force()
    {
        var p = new FrictionProbe(0);
        p.Step(0, 0);
        Assert.Equal(0, p.Step(0, 300, wheelLive: false));
        Assert.True(p.Done);
        Assert.Null(p.Floor);
        Assert.NotEqual("", p.Problem);
    }
}

using System.Text.Json;
using G923Mapper;

namespace G923Mapper.Tests;

/// <summary>
/// A simulated wheel that is also the wizard's "user": each Step() runs the next scripted action
/// (turn the wheel, press a pedal...) and then returns, as if the person had pressed Enter.
/// Axis order is X, Y, Z, RX, RY, RZ, Slider0, Slider1.
/// </summary>
internal sealed class SimWheel : IWheelSource, IWizardIO
{
    public static readonly int[] Stale = [32768, 32768, 0, 0, 0, 32768, 32768, 0];

    public int[] Axes = (int[])Stale.Clone();
    public int[] AwakeRest = [33199, 65535, 0, 0, 0, 65535, 65535, 0];   // as observed on the G923 Xbox edition
    public bool[] Buttons = new bool[23];
    public bool NeverWakes, Jitter;
    public string RotationAnswer = "900";

    public readonly Queue<Func<bool>> Script = new();
    public readonly List<string> Said = new();
    public readonly List<string> Prompts = new();

    private int _reads, _stepStart;
    private bool _awake;

    /// <summary>Runs on every read with the number of reads since the last Step (for movements that change over time).</summary>
    public Action<int>? Tick;
    /// <summary>Make AcceptNow() return true once, after this many reads since the last Step.</summary>
    public int? AcceptAfter;
    public readonly List<WizardProgress> Progresses = new();

    public SimWheel Then(Action a) { Script.Enqueue(() => { a(); return true; }); return this; }
    public SimWheel Skip() { Script.Enqueue(() => false); return this; }

    public RawInput Read()
    {
        _reads++;
        Tick?.Invoke(_reads - _stepStart);
        int[] a;
        if (NeverWakes || (!_awake && _reads < 6)) a = (int[])Stale.Clone();
        else
        {
            if (!_awake) { _awake = true; Axes = (int[])AwakeRest.Clone(); a = (int[])Axes.Clone(); a[0] += 4000; }   // the wiggle
            else a = (int[])Axes.Clone();
            for (int i = 0; i < 8; i++) a[i] = Math.Clamp(a[i] + (_reads % 5) - 2, 0, 65535);   // +-2 counts of noise
            if (Jitter) a[0] = Math.Clamp(a[0] + (_reads % 2) * 10000, 0, 65535);
        }
        return new RawInput { Axes = a, Buttons = (bool[])Buttons.Clone(), Pov = -1, Live = !RawInput.LooksStale(a) };
    }

    public void Say(string text) => Said.Add(text);
    public void Pause(int ms) { }
    public void Progress(WizardProgress p) => Progresses.Add(p);
    public bool AcceptNow()
    {
        if (AcceptAfter is int n && _reads - _stepStart >= n) { AcceptAfter = null; return true; }
        return false;
    }
    public string Ask(string prompt, string defaultValue) => RotationAnswer;

    public bool Step(string prompt, bool allowSkip)
    {
        Prompts.Add(prompt);
        Tick = null; AcceptAfter = null; _stepStart = _reads;
        if (Script.Count == 0) throw new InvalidOperationException("Wizard asked for more steps than the test scripted: " + prompt);
        bool go = Script.Dequeue()();
        if (!go) Assert.True(allowSkip, "wizard accepted a skip on a step that must not be skippable: " + prompt);
        return go;
    }

    // Common scripted gestures
    public SimWheel Rest() => Then(() => { Axes = (int[])AwakeRest.Clone(); Jitter = false; });
    public SimWheel Lock(int rawX) => Then(() => Axes[0] = rawX);
}

public class CalibrationAnalyzerTests
{
    private static RawInput Sample(int[] axes, params int[] down) =>
        new() { Axes = axes, Buttons = T.Buttons(down) };

    [Fact]
    public void Median_ignores_a_stray_spike()
    {
        var samples = new[] { 100, 101, 99, 5000, 100 }.Select(v => Sample([v, 0, 0, 0, 0, 0, 0, 0])).ToList();
        Assert.Equal(100, CalibrationAnalyzer.MedianAxes(samples)[0]);
    }

    [Fact]
    public void MaxSpread_finds_the_wanderer()
    {
        var samples = new[] { 100, 200, 4100 }.Select(v => Sample([v, 7, 0, 0, 0, 0, 0, 0])).ToList();
        Assert.Equal(4000, CalibrationAnalyzer.MaxSpread(samples));
    }

    [Fact]
    public void ButtonsDown_needs_the_button_held_for_most_of_the_recording()
    {
        var samples = new List<RawInput>();
        for (int i = 0; i < 10; i++) samples.Add(Sample(new int[8], i < 6 ? [5] : []));   // button 5 down in 6 of 10
        samples.AddRange(Enumerable.Range(0, 10).Select(i => Sample(new int[8], i == 0 ? [7] : [])));   // button 7 only once in 10
        var down = CalibrationAnalyzer.ButtonsDown(samples.Take(10).ToList());
        Assert.True(down[4]);
        Assert.False(down[6]);
        Assert.False(CalibrationAnalyzer.ButtonsDown(samples.Skip(10).ToList())[6]);
    }

    [Fact]
    public void DetectAxis_picks_the_biggest_change_and_honours_the_exclusion_list()
    {
        int[] rest = [33199, 65535, 0, 0, 0, 65535, 65535, 0];
        int[] pressed = [33199, 0, 0, 0, 0, 30000, 65535, 0];
        Assert.Equal(1, CalibrationAnalyzer.DetectAxis(rest, pressed, []));
        Assert.Equal(5, CalibrationAnalyzer.DetectAxis(rest, pressed, [1]));
        Assert.Null(CalibrationAnalyzer.DetectAxis(rest, pressed, [1, 5]));
    }

    [Fact]
    public void DetectAxis_ignores_small_movements()
    {
        int[] a = new int[8];
        int[] b = [CalibrationAnalyzer.MinChange - 1, 0, 0, 0, 0, 0, 0, 0];
        Assert.Null(CalibrationAnalyzer.DetectAxis(a, b, []));
        b[0] = CalibrationAnalyzer.MinChange;
        Assert.Equal(0, CalibrationAnalyzer.DetectAxis(a, b, []));
    }

    [Fact]
    public void Steering_measures_axis_range_and_centre()
    {
        int[] rest = [33199, 65535, 0, 0, 0, 65535, 65535, 0];
        int[] left = [0, 65535, 0, 0, 0, 65535, 65535, 0];
        int[] right = [65535, 65535, 0, 0, 0, 65535, 65535, 0];
        var w = CalibrationAnalyzer.Steering(rest, left, right)!;
        Assert.Equal(new WheelCalibration(0, 0, 65535, 33199, false), w);
    }

    [Fact]
    public void Steering_detects_an_inverted_wheel()
    {
        int[] rest = [30000, 0, 0, 0, 0, 0, 0, 0];
        int[] left = [60000, 0, 0, 0, 0, 0, 0, 0];
        int[] right = [1000, 0, 0, 0, 0, 0, 0, 0];
        var w = CalibrationAnalyzer.Steering(rest, left, right)!;
        Assert.True(w.Inverted);
        Assert.Equal(1000, w.Min);
        Assert.Equal(60000, w.Max);
    }

    [Fact]
    public void Steering_keeps_the_centre_strictly_inside_the_range()
    {
        int[] left = [0, 0, 0, 0, 0, 0, 0, 0];
        int[] right = [60000, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal(1, CalibrationAnalyzer.Steering([-50, 0, 0, 0, 0, 0, 0, 0], left, right)!.Centre);
        Assert.Equal(59999, CalibrationAnalyzer.Steering([65000, 0, 0, 0, 0, 0, 0, 0], left, right)!.Centre);
    }

    [Fact]
    public void Steering_is_null_when_nothing_moved()
        => Assert.Null(CalibrationAnalyzer.Steering(new int[8], new int[8], new int[8]));

    [Fact]
    public void Pedal_reading_low_when_pressed_is_inverted()
    {
        int[] rest = [33199, 65535, 0, 0, 0, 65535, 65535, 0];
        int[] pressed = [33199, 0, 0, 0, 0, 65535, 65535, 0];
        Assert.Equal(new PedalCalibration(1, 0, 65535, true), CalibrationAnalyzer.Pedal(rest, pressed, [0]));
    }

    [Fact]
    public void Pedal_reading_high_when_pressed_is_not_inverted_and_keeps_its_observed_range()
    {
        int[] rest = [32768, 0, 5, 0, 0, 0, 0, 0];
        int[] pressed = [32768, 0, 64000, 0, 0, 0, 0, 0];
        Assert.Equal(new PedalCalibration(2, 5, 64000, false), CalibrationAnalyzer.Pedal(rest, pressed, [0]));
    }

    [Fact]
    public void Pedal_cannot_reuse_an_axis_that_is_already_assigned()
    {
        int[] rest = [0, 65535, 0, 0, 0, 0, 0, 0];
        int[] pressed = [0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Null(CalibrationAnalyzer.Pedal(rest, pressed, [1]));
    }

    [Fact]
    public void DetectButton_finds_exactly_one_new_button()
    {
        Assert.Equal(4, CalibrationAnalyzer.DetectButton(new bool[23], T.Buttons(5), [], out var p));
        Assert.Null(p);
    }

    [Fact]
    public void DetectButton_ignores_a_button_that_was_already_down_at_rest()
    {
        Assert.Equal(5, CalibrationAnalyzer.DetectButton(T.Buttons(3), T.Buttons(3, 6), [], out _));
    }

    [Theory]
    [InlineData(new int[] { }, "No button")]
    [InlineData(new[] { 5, 6 }, "Several")]
    public void DetectButton_reports_why_it_failed(int[] down, string fragment)
    {
        Assert.Null(CalibrationAnalyzer.DetectButton(new bool[23], T.Buttons(down), [], out var problem));
        Assert.Contains(fragment, problem);
    }

    [Fact]
    public void DetectButton_refuses_a_button_that_is_already_named()
    {
        Assert.Null(CalibrationAnalyzer.DetectButton(new bool[23], T.Buttons(5), [4], out var problem));
        Assert.Contains("already assigned", problem);
    }

    [Fact]
    public void Stale_snapshots_are_recognised_and_real_ones_are_not()
    {
        Assert.True(RawInput.LooksStale(SimWheel.Stale));
        Assert.True(RawInput.LooksStale(new int[8]));
        Assert.False(RawInput.LooksStale([33199, 65535, 0, 0, 0, 65535, 65535, 0]));   // real G923 rest
        Assert.False(RawInput.LooksStale([32769, 32768, 0, 0, 0, 32768, 32768, 0]));   // one count off is enough
    }
}

public class CalibrationWizardTests
{
    private static CalibrationOptions Opt(int wakePolls = 300) => new()
    {
        DeviceId = "g923_xbox", Vid = "046D", Pid = "C26E", SampleCount = 20, SampleIntervalMs = 1, WakeTimeoutPolls = wakePolls,
    };

    /// <summary>Rest, left, right, throttle, brake, clutch, then the two paddles, then six skipped buttons.</summary>
    private static SimWheel FullG923Script()
    {
        var sim = new SimWheel();
        sim.Rest().Lock(0).Lock(65535)
           .Then(() => { sim.Axes[0] = 33199; sim.Axes[1] = 0; })                       // accelerator down
           .Then(() => { sim.Axes[1] = 65535; sim.Axes[5] = 0; })                       // brake down
           .Then(() => { sim.Axes[5] = 65535; sim.Axes[6] = 0; })                       // clutch down
           .Then(() => { sim.Axes[6] = 65535; sim.Buttons[4] = true; })                 // left paddle = button 5
           .Then(() => { sim.Buttons[4] = false; sim.Buttons[5] = true; })              // right paddle = button 6
           .Then(() => sim.Buttons[5] = false);
        for (int i = 0; i < 6; i++) sim.Skip();
        return sim;
    }

    [Fact]
    public void Full_run_reproduces_the_layout_observed_on_the_real_g923()
    {
        var sim = FullG923Script();
        var result = new CalibrationWizard(sim, sim, Opt()).Run()!;
        Assert.NotNull(result);
        var m = result.Map;

        Assert.Equal("g923_xbox", m.Name);
        Assert.Equal("046D", m.Vid);
        Assert.Equal("C26E", m.Pid);
        Assert.Equal(900, m.RotationDegrees);

        Assert.Equal("X", m.Axes["wheel"].Source);
        Assert.True(m.Axes["wheel"].Signed);
        Assert.False(m.Axes["wheel"].Invert);
        Assert.InRange(m.Axes["wheel"].Min, 0, 3);
        Assert.InRange(m.Axes["wheel"].Max, 65533, 65535);
        Assert.InRange(m.Axes["wheel"].Centre, 33197, 33201);

        Assert.Equal("Y", m.Axes["throttle"].Source);
        Assert.Equal("RZ", m.Axes["brake"].Source);
        Assert.Equal("Slider0", m.Axes["clutch"].Source);
        foreach (string p in new[] { "throttle", "brake", "clutch" })
        {
            Assert.True(m.Axes[p].Invert, p);
            Assert.False(m.Axes[p].Signed, p);
            Assert.InRange(m.Axes[p].Min, 0, 3);
            Assert.InRange(m.Axes[p].Max, 65533, 65535);
        }

        Assert.Equal(5, m.Buttons["paddleL"]);
        Assert.Equal(6, m.Buttons["paddleR"]);
        Assert.Equal(2, m.Buttons.Count);
        Assert.Empty(sim.Script);   // consumed every scripted step
    }

    [Fact]
    public void Calibrated_map_drives_the_pipeline_correctly()
    {
        var sim = FullG923Script();
        var m = new CalibrationWizard(sim, sim, Opt()).Run()!.Map;

        Assert.Equal(0, m.Normalize(T.Raw(wheel: m.Axes["wheel"].Centre)).Get("wheel"), 6);
        Assert.Equal(-1, m.Normalize(T.Raw(wheel: 0)).Get("wheel"), 3);
        Assert.Equal(1, m.Normalize(T.Raw(wheel: 65535)).Get("wheel"), 3);
        Assert.Equal(0, m.Normalize(T.Raw()).Get("throttle"), 3);
        Assert.Equal(1, m.Normalize(T.Raw(throttle: 0)).Get("throttle"), 3);
        Assert.Equal(1, m.Normalize(T.Raw(brake: 0)).Get("brake"), 3);
        Assert.Equal(1, m.Normalize(T.Raw(clutch: 0)).Get("clutch"), 3);
        Assert.Equal(1, m.Normalize(T.Raw(buttons: T.Buttons(5))).Get("paddleL"));
    }

    [Fact]
    public void Report_lists_each_measurement()
    {
        var sim = FullG923Script();
        var report = new CalibrationWizard(sim, sim, Opt()).Run()!.Report;
        Assert.Contains(report, l => l.StartsWith("wheel: axis X"));
        Assert.Contains(report, l => l.StartsWith("throttle: axis Y") && l.Contains("inverted"));
        Assert.Contains(report, l => l == "paddleL: button 5");
        Assert.Contains(report, l => l == "buttonA: skipped");
        Assert.Contains(report, l => l == "rotation: 900 degrees");
    }

    [Fact]
    public void Wheel_with_inverted_steering_gets_the_sign_flipped()
    {
        var sim = new SimWheel();
        sim.Rest().Lock(65535).Lock(0)
           .Then(() => { sim.Axes[0] = 33199; sim.Axes[1] = 0; })
           .Then(() => { sim.Axes[1] = 65535; sim.Axes[5] = 0; })
           .Skip();
        for (int i = 0; i < 8; i++) sim.Skip();
        var m = new CalibrationWizard(sim, sim, Opt()).Run()!.Map;

        Assert.True(m.Axes["wheel"].Invert);
        Assert.Equal(-1, m.Normalize(T.Raw(wheel: 65535)).Get("wheel"), 3);   // turning left (raw high) is still negative
        Assert.Equal(1, m.Normalize(T.Raw(wheel: 0)).Get("wheel"), 3);
        Assert.False(m.Axes.ContainsKey("clutch"));                             // skipped
    }

    [Fact]
    public void Pedals_that_read_high_when_pressed_on_other_axes_are_handled()
    {
        // A different edition: pedals rest at 0 and rise when pressed, on Z / RX / RY.
        var sim = new SimWheel { AwakeRest = [32800, 0, 0, 0, 0, 0, 0, 0] };
        sim.Rest().Lock(0).Lock(65535)
           .Then(() => { sim.Axes[0] = 32800; sim.Axes[2] = 65535; })                 // throttle on Z
           .Then(() => { sim.Axes[2] = 0; sim.Axes[3] = 65535; })                     // brake on RX
           .Then(() => { sim.Axes[3] = 0; sim.Axes[4] = 65535; });                    // clutch on RY
        for (int i = 0; i < 8; i++) sim.Skip();
        var m = new CalibrationWizard(sim, sim, Opt()).Run()!.Map;

        Assert.Equal("Z", m.Axes["throttle"].Source);
        Assert.Equal("RX", m.Axes["brake"].Source);
        Assert.Equal("RY", m.Axes["clutch"].Source);
        Assert.False(m.Axes["throttle"].Invert);
        Assert.Equal(1, m.Normalize(RawWith(z: 65535)).Get("throttle"), 3);
        Assert.Equal(0, m.Normalize(RawWith()).Get("throttle"), 3);

        static RawInput RawWith(int z = 0) => new() { Axes = [32800, 0, z, 0, 0, 0, 0, 0], Buttons = new bool[23] };
    }

    [Fact]
    public void A_wheel_that_never_wakes_gives_up_with_advice()
    {
        var sim = new SimWheel { NeverWakes = true };
        Assert.Null(new CalibrationWizard(sim, sim, Opt(wakePolls: 50)).Run());
        Assert.Contains(sim.Said, s => s.Contains("No movement"));
        Assert.Empty(sim.Prompts);   // never got as far as asking for anything
    }

    [Fact]
    public void Rest_recording_is_repeated_if_something_moved()
    {
        var sim = new SimWheel();
        sim.Then(() => { sim.Axes = (int[])sim.AwakeRest.Clone(); sim.Jitter = true; })   // attempt 1: wheel wobbling
           .Rest()                                                                       // attempt 2: still
           .Lock(0).Lock(65535)
           .Then(() => { sim.Axes[0] = 33199; sim.Axes[1] = 0; })
           .Then(() => { sim.Axes[1] = 65535; sim.Axes[5] = 0; })
           .Skip();
        for (int i = 0; i < 8; i++) sim.Skip();
        var result = new CalibrationWizard(sim, sim, Opt()).Run();
        Assert.NotNull(result);
        Assert.Contains(sim.Said, s => s.Contains("Hold still"));
    }

    [Fact]
    public void Gives_up_if_the_wheel_is_never_steady()
    {
        var sim = new SimWheel();
        for (int i = 0; i < 3; i++) sim.Then(() => { sim.Axes = (int[])sim.AwakeRest.Clone(); sim.Jitter = true; });
        Assert.Null(new CalibrationWizard(sim, sim, Opt()).Run());
        Assert.Contains(sim.Said, s => s.Contains("steady rest"));
    }

    [Fact]
    public void Gives_up_if_left_and_right_never_differ()
    {
        var sim = new SimWheel();
        sim.Rest();
        for (int i = 0; i < 3; i++) sim.Lock(33199).Lock(33199);
        Assert.Null(new CalibrationWizard(sim, sim, Opt()).Run());
        Assert.Contains(sim.Said, s => s.Contains("did not seem to move"));
    }

    [Fact]
    public void A_pedal_that_never_moves_is_skipped_after_the_retries_and_the_rest_continue()
    {
        var sim = new SimWheel();
        sim.Rest().Lock(0).Lock(65535);
        for (int i = 0; i < 3; i++) sim.Then(() => sim.Axes[0] = 33199);                // accelerator: nothing happens, x3
        sim.Then(() => { sim.Axes[1] = 0; })                                             // brake on Y
           .Skip();                                                                      // no clutch
        for (int i = 0; i < 8; i++) sim.Skip();
        var m = new CalibrationWizard(sim, sim, Opt()).Run()!.Map;

        Assert.False(m.Axes.ContainsKey("throttle"));
        Assert.Equal("Y", m.Axes["brake"].Source);
        Assert.Contains(sim.Said, s => s.Contains("No pedal moved"));
    }

    [Fact]
    public void Button_retries_report_the_problem_then_succeed()
    {
        var sim = new SimWheel();
        sim.Rest().Lock(0).Lock(65535)
           .Then(() => { sim.Axes[0] = 33199; sim.Axes[1] = 0; })
           .Then(() => { sim.Axes[1] = 65535; sim.Axes[5] = 0; })
           .Skip()
           .Then(() => { })                                                              // paddleL attempt 1: nothing pressed
           .Then(() => { sim.Buttons[4] = true; sim.Buttons[8] = true; })                // attempt 2: two buttons
           .Then(() => { sim.Buttons[8] = false; });                                     // attempt 3: just button 5
        sim.Skip();                                                                      // paddleR skipped
        for (int i = 0; i < 6; i++) sim.Skip();
        var m = new CalibrationWizard(sim, sim, Opt()).Run()!.Map;

        Assert.Equal(5, m.Buttons["paddleL"]);
        Assert.Contains(sim.Said, s => s.Contains("No button was pressed"));
        Assert.Contains(sim.Said, s => s.Contains("Several buttons"));
    }

    [Fact]
    public void The_same_button_cannot_be_given_two_names()
    {
        var sim = new SimWheel();
        sim.Rest().Lock(0).Lock(65535)
           .Then(() => { sim.Axes[0] = 33199; sim.Axes[1] = 0; })
           .Then(() => { sim.Axes[1] = 65535; sim.Axes[5] = 0; })
           .Skip()
           .Then(() => sim.Buttons[4] = true)       // paddleL = 5
           .Then(() => { })                         // paddleR: button 5 still held -> already assigned, x3
           .Then(() => { })
           .Then(() => { });
        for (int i = 0; i < 6; i++) sim.Skip();
        var m = new CalibrationWizard(sim, sim, Opt()).Run()!.Map;
        Assert.Equal(5, m.Buttons["paddleL"]);
        Assert.False(m.Buttons.ContainsKey("paddleR"));
    }

    [Theory]
    [InlineData("540", 540)]
    [InlineData("900", 900)]
    [InlineData("abc", 900)]
    [InlineData("5", 900)]       // implausible, falls back
    [InlineData("99999", 900)]
    public void Rotation_answer_is_parsed_with_a_safe_fallback(string answer, double expected)
    {
        var sim = FullG923Script();
        sim.RotationAnswer = answer;
        Assert.Equal(expected, new CalibrationWizard(sim, sim, Opt()).Run()!.Map.RotationDegrees);
    }

    [Fact]
    public void A_pause_part_way_to_the_stop_does_not_count_as_the_stop()
    {
        var sim = new SimWheel();
        sim.Rest()
           .Then(() => sim.Tick = n => sim.Axes[0] = n < 300 ? 18000 : 0)       // hesitates at 18000 for 300 reads, then goes on to the stop
           .Lock(65535)
           .Then(() => { sim.Axes[0] = 33199; sim.Axes[1] = 0; })
           .Then(() => { sim.Axes[1] = 65535; sim.Axes[5] = 0; })
           .Skip();
        for (int i = 0; i < 8; i++) sim.Skip();
        var wheel = new CalibrationWizard(sim, sim, Opt()).Run()!.Map.Axes["wheel"];
        Assert.InRange(wheel.Min, 0, 10);   // the stop, not the pause
    }

    [Fact]
    public void Right_stop_must_be_on_the_other_side_of_centre()
    {
        var sim = new SimWheel();
        sim.Rest();
        for (int i = 0; i < 3; i++) sim.Lock(0).Lock(5000);   // "right" on the same side as left never counts
        Assert.Null(new CalibrationWizard(sim, sim, Opt()).Run());
        Assert.Contains(sim.Said, s => s.Contains("did not seem to move"));
    }

    [Fact]
    public void Use_this_position_takes_the_current_reading_without_waiting_for_the_hold()
    {
        var sim = new SimWheel();
        sim.Rest()
           .Then(() => { sim.Axes[0] = 9000; sim.AcceptAfter = 50; })
           .Lock(65535)
           .Then(() => { sim.Axes[0] = 33199; sim.Axes[1] = 0; })
           .Then(() => { sim.Axes[1] = 65535; sim.Axes[5] = 0; })
           .Skip();
        for (int i = 0; i < 8; i++) sim.Skip();
        var wheel = new CalibrationWizard(sim, sim, Opt()).Run()!.Map.Axes["wheel"];
        Assert.InRange(wheel.Min, 8990, 9010);
    }

    [Fact]
    public void Use_this_position_is_ignored_while_the_wheel_is_still_near_centre()
    {
        var sim = new SimWheel();
        sim.Rest();
        for (int i = 0; i < 3; i++) sim.Then(() => sim.AcceptAfter = 5).Lock(33199);   // asks to accept while nothing has moved
        Assert.Null(new CalibrationWizard(sim, sim, Opt()).Run());
    }

    [Fact]
    public void Live_progress_reports_the_hold_filling_up_and_how_far_along_the_range_it_is()
    {
        var sim = FullG923Script();
        new CalibrationWizard(sim, sim, Opt()).Run();

        var left = sim.Progresses.Where(p => p.Label == "Left stop").ToList();
        Assert.NotEmpty(left);
        Assert.True(left.Last().Locked);
        Assert.Equal(1.0, left.Last().Hold, 3);
        Assert.Contains(left, p => p.Hold > 0 && p.Hold < 1);                // the meter was seen part way
        Assert.Contains(left, p => p.Message.Contains("% of the way"));
        Assert.Contains(sim.Progresses, p => p.Label == "Resting");
    }

    [Fact]
    public void A_stop_short_of_the_end_of_the_range_gets_a_gentle_warning_but_is_still_accepted()
    {
        // Typical with G HUB set to a smaller angle: a lock at 25000 is only ~25% of the way to the end of the axis.
        var sim = new SimWheel();
        sim.Rest().Lock(22000).Lock(45000)
           .Then(() => { sim.Axes[0] = 33199; sim.Axes[1] = 0; })
           .Then(() => { sim.Axes[1] = 65535; sim.Axes[5] = 0; })
           .Skip();
        for (int i = 0; i < 8; i++) sim.Skip();
        var result = new CalibrationWizard(sim, sim, Opt()).Run();
        Assert.NotNull(result);
        Assert.Contains(sim.Progresses, p => p.Label == "Left stop" && p.Message.Contains("short of the end"));
    }

    [Fact]
    public void Only_the_clutch_and_buttons_may_be_skipped()
    {
        // SimWheel.Step asserts if the wizard accepts a skip on a mandatory step; the prompts must say so.
        var sim = FullG923Script();
        new CalibrationWizard(sim, sim, Opt()).Run();
        Assert.Contains(sim.Prompts, p => p.Contains("CLUTCH") && p.Contains("skip"));
        Assert.DoesNotContain(sim.Prompts, p => p.Contains("ACCELERATOR") && p.Contains("skip"));
        Assert.DoesNotContain(sim.Prompts, p => p.Contains("LEFT and hold") && p.Contains("skip"));
    }
}

public sealed class DevicesFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "g923-dev-" + Guid.NewGuid());
    private string File_ => Path.Combine(_dir, "devices.json");
    public DevicesFileTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Saved_entry_loads_back_identically()
    {
        var sim = new SimWheel();
        var map = FullRun(sim);
        DeviceRegistry.SaveEntry(File_, map);

        var reg = DeviceRegistry.CreateDefault();
        reg.LoadFile(File_);
        Assert.True(reg.TryGet("g923_xbox", out var loaded));
        Assert.Equal(map.Axes["wheel"].Centre, loaded.Axes["wheel"].Centre);
        Assert.Equal(map.Axes["brake"].Source, loaded.Axes["brake"].Source);
        Assert.Equal(map.Axes["brake"].Invert, loaded.Axes["brake"].Invert);
        Assert.Equal(map.Buttons["paddleL"], loaded.Buttons["paddleL"]);
        Assert.Equal(map.Vid, loaded.Vid);
        Assert.Equal(map.RotationDegrees, loaded.RotationDegrees);
    }

    [Fact]
    public void Calibration_replaces_the_built_in_defaults_for_that_device()
    {
        var map = FullRun(new SimWheel());
        map.Axes["wheel"].Centre = 31111;
        DeviceRegistry.SaveEntry(File_, map);
        var reg = DeviceRegistry.CreateDefault();
        reg.LoadFile(File_);
        reg.TryGet("g923_xbox", out var m);
        Assert.Equal(31111, m.Axes["wheel"].Centre);
    }

    [Fact]
    public void Saving_keeps_other_devices_and_leaves_no_temp_file()
    {
        var ps = new DeviceMap { Name = "g923_ps", Vid = "046D", Pid = "C267" };
        ps.Axes["wheel"] = new AxisDef { Source = "X", Signed = true };
        DeviceRegistry.SaveEntry(File_, ps);
        DeviceRegistry.SaveEntry(File_, FullRun(new SimWheel()));

        var reg = new DeviceRegistry();
        reg.LoadFile(File_);
        Assert.True(reg.TryGet("g923_ps", out _));
        Assert.True(reg.TryGet("g923_xbox", out _));
        Assert.False(File.Exists(File_ + ".tmp"));
    }

    [Fact]
    public void File_uses_readable_camelCase_keys_and_keeps_input_names()
    {
        DeviceRegistry.SaveEntry(File_, FullRun(new SimWheel()));
        string json = File.ReadAllText(File_);
        using var doc = JsonDocument.Parse(json);
        var entry = doc.RootElement.GetProperty("g923_xbox");
        Assert.Equal("046D", entry.GetProperty("vid").GetString());
        Assert.True(entry.GetProperty("axes").TryGetProperty("throttle", out _));
        Assert.True(entry.GetProperty("buttons").TryGetProperty("paddleL", out _));
        Assert.DoesNotContain("usbIds", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_profile_validates_against_a_calibrated_device()
    {
        DeviceRegistry.SaveEntry(File_, FullRun(new SimWheel()));
        var reg = DeviceRegistry.CreateDefault();
        reg.LoadFile(File_);
        Assert.True(ProfileLoader.Parse(DefaultProfiles.GtaV, reg).Ok);
    }

    [Fact]
    public void FindByUsb_matches_the_built_in_and_calibrated_ids()
    {
        var reg = DeviceRegistry.CreateDefault();
        Assert.Equal("g923_xbox", reg.FindByUsb(0x046D, 0xC26E)?.Name);
        Assert.Null(reg.FindByUsb(0x046D, 0x0001));
        reg.Add(new DeviceMap { Name = "g923_ps", Vid = "046d", Pid = "c267" });   // lower-case hex is fine
        Assert.Equal("g923_ps", reg.FindByUsb(0x046D, 0xC267)?.Name);
    }

    private static DeviceMap FullRun(SimWheel sim)
    {
        sim.Rest().Lock(0).Lock(65535)
           .Then(() => { sim.Axes[0] = 33199; sim.Axes[1] = 0; })
           .Then(() => { sim.Axes[1] = 65535; sim.Axes[5] = 0; })
           .Then(() => { sim.Axes[5] = 65535; sim.Axes[6] = 0; })
           .Then(() => { sim.Axes[6] = 65535; sim.Buttons[4] = true; })
           .Then(() => { sim.Buttons[4] = false; sim.Buttons[5] = true; });
        for (int i = 0; i < 6; i++) sim.Skip();
        var opt = new CalibrationOptions { DeviceId = "g923_xbox", Vid = "046D", Pid = "C26E", SampleCount = 20, SampleIntervalMs = 1, WakeTimeoutPolls = 300 };
        return new CalibrationWizard(sim, sim, opt).Run()!.Map;
    }
}

public class LopsidedCalibrationTests
{
    [Theory]
    [InlineData(0, 32768, 65535, false)]       // a textbook wheel
    [InlineData(2104, 32850, 65535, false)]    // the real stops measured on this G923
    [InlineData(23477, 32552, 65535, true)]    // the calibration that was saved: the left lock was never reached
    [InlineData(0, 32768, 20000, true)]
    [InlineData(1000, 32000, 63000, false)]    // a little uneven is normal
    [InlineData(0, 0, 65535, false)]           // no left travel at all is not "lopsided", it is just not a wheel measurement
    public void Lopsidedness_is_detected_from_left_and_right_travel(int min, int centre, int max, bool expected)
        => Assert.Equal(expected, CalibrationAnalyzer.IsLopsided(min, centre, max) && centre > min);

    [Fact]
    public void The_wizard_asks_for_a_retry_when_one_lock_was_not_turned_to_the_stop()
    {
        var sim = new SimWheel();
        sim.Rest()
           .Lock(20000).Lock(65535)                       // first try: left only part of the way
           .Lock(0).Lock(65535)                            // second try: both stops
           .Then(() => { sim.Axes[0] = 33199; sim.Axes[1] = 0; })
           .Then(() => { sim.Axes[1] = 65535; sim.Axes[5] = 0; })
           .Skip();
        for (int i = 0; i < 8; i++) sim.Skip();
        var result = new CalibrationWizard(sim, sim, new CalibrationOptions { SampleCount = 20, SampleIntervalMs = 1, WakeTimeoutPolls = 300 }).Run()!;

        Assert.Contains(sim.Said, s => s.Contains("very different"));
        Assert.InRange(result.Map.Axes["wheel"].Min, 0, 3);          // the good measurement was kept, not the partial one
        Assert.DoesNotContain(result.Report, l => l.StartsWith("WARNING"));
    }

    [Fact]
    public void If_it_is_still_lopsided_on_the_last_try_it_is_accepted_but_clearly_marked()
    {
        var sim = new SimWheel();
        sim.Rest();
        for (int i = 0; i < 3; i++) sim.Lock(20000).Lock(65535);
        sim.Then(() => { sim.Axes[0] = 33199; sim.Axes[1] = 0; })
           .Then(() => { sim.Axes[1] = 65535; sim.Axes[5] = 0; })
           .Skip();
        for (int i = 0; i < 8; i++) sim.Skip();
        var result = new CalibrationWizard(sim, sim, new CalibrationOptions { SampleCount = 20, SampleIntervalMs = 1, WakeTimeoutPolls = 300 }).Run()!;
        Assert.Contains(result.Report, l => l.StartsWith("WARNING") && l.Contains("uneven"));
    }

    [Fact]
    public void The_health_check_flags_a_saved_lopsided_calibration()
    {
        var facts = new HealthFacts
        {
            ViGEmInstalled = true, ViGEmRunning = true, MapperRunning = true, WheelFound = true, WheelReporting = true,
            HidHideInstalled = true, WheelHidden = true, WheelCalibrationLopsided = true,
        };
        var item = HealthEvaluator.Evaluate(facts).Single(i => i.Title.Contains("lopsided"));
        Assert.Equal(HealthLevel.Warning, item.Level);
        Assert.Contains("all the way", item.Detail);
        Assert.Equal(HealthAction.OpenSettings, item.Action);
        Assert.DoesNotContain(HealthEvaluator.Evaluate(new HealthFacts { ViGEmInstalled = true, ViGEmRunning = true }), i => i.Title.Contains("lopsided"));
    }

    private static DeviceRegistry RegistryWithRotation(double degrees)
    {
        var reg = DeviceRegistry.CreateDefault();
        reg.TryGet("g923_xbox", out var d);
        d.RotationDegrees = degrees;
        return reg;
    }

    [Fact]
    public void A_range_larger_than_the_wheels_rotation_warns_because_full_stick_can_never_be_reached()
    {
        var r = ProfileLoader.Parse(DefaultProfiles.GtaV, RegistryWithRotation(360));   // the starter profile asks for 540
        Assert.True(r.Ok);
        Assert.Contains(r.Warnings, w => w.Contains("540") && w.Contains("360") && w.Contains("full stick"));
    }

    [Theory]
    [InlineData(900)]    // the built-in G923 default: 540 fits comfortably
    [InlineData(540)]
    public void A_range_that_fits_the_wheel_does_not_warn(double rotation)
        => Assert.DoesNotContain(ProfileLoader.Parse(DefaultProfiles.GtaV, RegistryWithRotation(rotation)).Warnings, w => w.Contains("full stick"));

    [Fact]
    public void A_range_equal_to_the_rotation_is_fine_and_pedals_are_not_checked()
    {
        var reg = RegistryWithRotation(360);
        var ok = ProfileLoader.Parse("""{ "name": "t", "rules": [ { "from": "wheel", "to": "LeftStickX", "range": 360 } ] }""", reg);
        Assert.DoesNotContain(ok.Warnings, w => w.Contains("full stick"));
        var pedal = ProfileLoader.Parse("""{ "name": "t", "rules": [ { "from": "throttle", "to": "RightTrigger", "range": 540 } ] }""", reg);
        Assert.DoesNotContain(pedal.Warnings, w => w.Contains("full stick"));      // only the wheel has a rotation
    }
}

public class HoldDetectorTests
{
    private static HoldDetector Det(int needed = 10) => new(33000, 8000, 250, needed);

    [Fact]
    public void Locks_after_the_reading_stays_steady_far_from_rest()
    {
        var d = Det();
        for (int i = 0; i < 9; i++) d.Add(2000 + i % 3);
        Assert.False(d.Locked);
        Assert.True(d.Progress is > 0.8 and < 1);
        d.Add(2001);
        Assert.True(d.Locked);
        Assert.Equal(1.0, d.Progress);
    }

    [Fact]
    public void Moving_restarts_the_count()
    {
        var d = Det();
        for (int i = 0; i < 8; i++) d.Add(2000);
        d.Add(5000);
        Assert.Equal(1, d.Streak);
        Assert.False(d.Locked);
    }

    [Fact]
    public void Steady_near_rest_never_locks()
    {
        var d = Det(3);
        for (int i = 0; i < 100; i++) d.Add(33000 + i % 5);
        Assert.False(d.Locked);
        Assert.Equal(0, d.Progress);
    }

    [Fact]
    public void A_slow_drift_does_not_lock_because_the_band_is_measured_over_the_whole_run()
    {
        var d = Det(10);
        for (int i = 0; i < 40; i++) d.Add(2000 + i * 100);
        Assert.False(d.Locked);
    }

    [Fact]
    public void Extreme_tracks_the_furthest_point_from_rest()
    {
        var d = Det();
        d.Add(20000); d.Add(3000); d.Add(10000);
        Assert.Equal(3000, d.Extreme);
    }

    [Fact]
    public void The_qualifier_can_demand_the_other_side_of_rest()
    {
        var d = new HoldDetector(33000, 8000, 250, 3, v => v > 33000);
        for (int i = 0; i < 10; i++) d.Add(2000);
        Assert.False(d.Locked);
        for (int i = 0; i < 3; i++) d.Add(64000);
        Assert.True(d.Locked);
    }
}

public class StillDetectorTests
{
    private static int[] Axes(int x = 33000) => [x, 65535, 0, 0, 0, 65535, 65535, 0];

    [Fact]
    public void Is_still_after_the_hold_time_of_small_noise()
    {
        var d = new StillDetector(500, 20);
        for (int i = 0; i < 19; i++) d.Add(Axes(33000 + i % 4));
        Assert.False(d.Still);
        d.Add(Axes());
        Assert.True(d.Still);
    }

    [Fact]
    public void Any_axis_moving_resets_it()
    {
        var d = new StillDetector(500, 20);
        for (int i = 0; i < 15; i++) d.Add(Axes());
        var moved = Axes(); moved[1] = 60000;
        d.Add(moved);
        Assert.Equal(1, d.Streak);
        Assert.False(d.Still);
    }
}

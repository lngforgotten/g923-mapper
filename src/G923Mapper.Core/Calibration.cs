namespace G923Mapper;

// ---------------------------------------------------------------------------------------------
// Calibration. The analysis is pure functions over recorded samples; the wizard drives it through
// two small interfaces so the whole flow can be unit-tested with a simulated wheel.
// ---------------------------------------------------------------------------------------------

public sealed record WheelCalibration(int Axis, int Min, int Max, int Centre, bool Inverted);
public sealed record PedalCalibration(int Axis, int Min, int Max, bool Invert);

public static class CalibrationAnalyzer
{
    /// <summary>An axis counts as having moved if its median changed by at least this much (about 12% of travel).</summary>
    public const int MinChange = 8000;

    /// <summary>During a "hold still" recording no axis may wander more than this between its min and max sample.</summary>
    public const int MaxSteadySpread = 3000;

    public static int[] MedianAxes(IReadOnlyList<RawInput> samples)
    {
        var result = new int[8];
        for (int a = 0; a < 8; a++)
        {
            int[] v = samples.Select(s => s.Axes[a]).Order().ToArray();
            result[a] = v.Length == 0 ? 0 : v[v.Length / 2];
        }
        return result;
    }

    /// <summary>Largest min-to-max swing of any axis across the samples.</summary>
    public static int MaxSpread(IReadOnlyList<RawInput> samples)
    {
        int worst = 0;
        for (int a = 0; a < 8; a++)
        {
            if (samples.Count == 0) break;
            worst = Math.Max(worst, samples.Max(s => s.Axes[a]) - samples.Min(s => s.Axes[a]));
        }
        return worst;
    }

    /// <summary>Buttons held down for at least <paramref name="fraction"/> of the samples.</summary>
    public static bool[] ButtonsDown(IReadOnlyList<RawInput> samples, double fraction = 0.5)
    {
        int n = samples.Count == 0 ? 0 : samples.Max(s => s.Buttons.Length);
        var down = new bool[n];
        for (int b = 0; b < n; b++)
            down[b] = samples.Count > 0 && samples.Count(s => b < s.Buttons.Length && s.Buttons[b]) >= samples.Count * fraction;
        return down;
    }

    /// <summary>The axis that changed most between two snapshots, ignoring excluded axes; null if none changed enough.</summary>
    public static int? DetectAxis(int[] from, int[] to, IReadOnlyCollection<int> exclude)
    {
        int best = -1, bestChange = MinChange - 1;
        for (int a = 0; a < 8; a++)
        {
            if (exclude.Contains(a)) continue;
            int change = Math.Abs(to[a] - from[a]);
            if (change > bestChange) { best = a; bestChange = change; }
        }
        return best < 0 ? null : best;
    }

    /// <summary>
    /// Wheel from three snapshots: hands off, full left, full right. The axis is whichever moved most
    /// between the two locks; if full left reads higher than full right the wheel is inverted.
    /// </summary>
    public static WheelCalibration? Steering(int[] rest, int[] left, int[] right)
    {
        int? axis = DetectAxis(left, right, []);
        if (axis is not int a) return null;

        int lo = Math.Min(left[a], right[a]), hi = Math.Max(left[a], right[a]);
        int centre = Math.Clamp(rest[a], lo + 1, hi - 1);
        return new WheelCalibration(a, lo, hi, centre, Inverted: left[a] > right[a]);
    }

    /// <summary>
    /// True when the travel either side of the centre is very different (the smaller is under 70% of the larger). A real wheel
    /// is close to symmetrical, so this almost always means a lock was not turned all the way to its stop.
    /// </summary>
    public static bool IsLopsided(int min, int centre, int max)
    {
        double left = centre - min, right = max - centre;
        double larger = Math.Max(left, right);
        return larger > 0 && Math.Min(left, right) / larger < 0.7;
    }

    public static bool IsLopsided(WheelCalibration w) => IsLopsided(w.Min, w.Centre, w.Max);

    /// <summary>
    /// A pedal from hands-off and pressed snapshots. The range is what was actually observed, and
    /// "inverted" means it reads lower when pressed.
    /// </summary>
    public static PedalCalibration? Pedal(int[] rest, int[] pressed, IReadOnlyCollection<int> used)
    {
        int? axis = DetectAxis(rest, pressed, used);
        if (axis is not int a) return null;
        return new PedalCalibration(a, Math.Min(rest[a], pressed[a]), Math.Max(rest[a], pressed[a]), Invert: pressed[a] < rest[a]);
    }

    /// <summary>The single button that went down; null with a reason when none or several did, or it is already named.</summary>
    public static int? DetectButton(bool[] baseline, bool[] pressed, IReadOnlyCollection<int> used, out string? problem)
    {
        problem = null;
        var fresh = new List<int>();
        for (int b = 0; b < pressed.Length; b++)
            if (pressed[b] && !(b < baseline.Length && baseline[b])) fresh.Add(b);

        if (fresh.Count == 0) { problem = "No button was pressed."; return null; }
        if (fresh.Count > 1)
        {
            problem = $"Several buttons were down at once ({string.Join(", ", fresh.Select(b => b + 1))}); press only one.";
            return null;
        }
        if (used.Contains(fresh[0]))
        {
            problem = $"Button {fresh[0] + 1} is already assigned to another name.";
            return null;
        }
        return fresh[0];
    }
}

public interface IWheelSource
{
    /// <summary>Poll the device now.</summary>
    RawInput Read();
}

/// <summary>Live feedback while the wizard watches a control, so the person can see what it sees.</summary>
/// <param name="Label">What is being measured, e.g. "Left stop".</param>
/// <param name="Value">The current raw reading of that control.</param>
/// <param name="Rest">Where it sits at rest.</param>
/// <param name="Extreme">The furthest from rest it has been.</param>
/// <param name="Hold">0..1: how far through the "hold it steady" countdown (1 = locked in).</param>
public sealed record WizardProgress(string Label, int Value, int Rest, int Extreme, double Hold, bool Locked, string Message);

public interface IWizardIO
{
    void Say(string text);
    /// <summary>Wait for the user. Returns true to record, false if they chose to skip (only when <paramref name="allowSkip"/>).</summary>
    bool Step(string prompt, bool allowSkip);
    string Ask(string prompt, string defaultValue);
    void Pause(int milliseconds);

    /// <summary>Called several times a second while a measurement is running. Optional.</summary>
    void Progress(WizardProgress progress) { }

    /// <summary>
    /// True once if the person asked to use the current position now instead of waiting for it to lock
    /// (a button in the window, or Enter in the console). Checked while a measurement is running.
    /// </summary>
    bool AcceptNow() => false;
}

/// <summary>
/// Decides when a control is being held at a stop: the reading has stayed inside a narrow band for the whole hold time
/// and is well away from rest. A half turn, or a pause part way, cannot lock because the reading either keeps moving or
/// never gets far from rest.
/// </summary>
public sealed class HoldDetector
{
    private readonly int _rest, _minTravel, _tolerance, _needed;
    private readonly Func<int, bool>? _qualifies;
    private int _lo, _hi;
    private bool _any;

    /// <param name="needed">How many consecutive samples must be steady.</param>
    /// <param name="qualifies">Extra condition on the value (for example "on the other side of rest from the first stop").</param>
    public HoldDetector(int rest, int minTravel, int tolerance, int needed, Func<int, bool>? qualifies = null)
    {
        _rest = rest; _minTravel = minTravel; _tolerance = tolerance; _needed = Math.Max(1, needed); _qualifies = qualifies;
    }

    public int Value { get; private set; }
    /// <summary>The furthest from rest the reading has been.</summary>
    public int Extreme { get; private set; }
    /// <summary>How many samples in a row have stayed within the tolerance band.</summary>
    public int Streak { get; private set; }

    public bool Far => Math.Abs(Value - _rest) >= _minTravel && (_qualifies?.Invoke(Value) ?? true);
    public double Progress => Far ? Math.Min(1.0, (double)Streak / _needed) : 0;
    public bool Locked => Far && Streak >= _needed;

    public void Add(int v)
    {
        Value = v;
        if (!_any || Math.Abs(v - _rest) > Math.Abs(Extreme - _rest)) Extreme = v;
        _any = true;
        if (Streak == 0) { _lo = _hi = v; Streak = 1; return; }
        int lo = Math.Min(_lo, v), hi = Math.Max(_hi, v);
        if (hi - lo > _tolerance) { _lo = _hi = v; Streak = 1; }   // it moved: start the count again
        else { _lo = lo; _hi = hi; Streak++; }
    }
}

/// <summary>The same idea for "everything is still": every axis stays inside a band for the whole hold time.</summary>
public sealed class StillDetector
{
    private readonly int _tolerance, _needed;
    private readonly int[] _lo = new int[8], _hi = new int[8];

    public StillDetector(int tolerance, int needed) { _tolerance = tolerance; _needed = Math.Max(1, needed); }

    public int Streak { get; private set; }
    public double Progress => Math.Min(1.0, (double)Streak / _needed);
    public bool Still => Streak >= _needed;

    public void Add(int[] axes)
    {
        if (Streak == 0) { Reset(axes); return; }
        for (int a = 0; a < 8; a++)
            if (Math.Max(_hi[a], axes[a]) - Math.Min(_lo[a], axes[a]) > _tolerance) { Reset(axes); return; }
        for (int a = 0; a < 8; a++) { _lo[a] = Math.Min(_lo[a], axes[a]); _hi[a] = Math.Max(_hi[a], axes[a]); }
        Streak++;
    }

    private void Reset(int[] axes)
    {
        Array.Copy(axes, _lo, 8);
        Array.Copy(axes, _hi, 8);
        Streak = 1;
    }
}

public sealed class CalibrationOptions
{
    /// <summary>Name to store the device under, e.g. g923_xbox.</summary>
    public string DeviceId { get; init; } = "g923_xbox";
    /// <summary>Name the buttons the PlayStation way (Cross, Circle, Square, Triangle, Share, Options) instead of the Xbox way. The stored names are the same.</summary>
    public bool PlayStationLabels { get; init; }

    /// <summary>Whether these ids / this device name look like the PlayStation edition (PID C266 in PC mode, C267 in PlayStation mode, or a name containing "ps").</summary>
    public static bool LooksPlayStation(string deviceId, string pid) =>
        pid.Equals("C266", StringComparison.OrdinalIgnoreCase) || pid.Equals("C267", StringComparison.OrdinalIgnoreCase)
        || deviceId.Contains("ps", StringComparison.OrdinalIgnoreCase) || deviceId.Contains("playstation", StringComparison.OrdinalIgnoreCase);

    public string Vid { get; init; } = "";
    public string Pid { get; init; } = "";
    /// <summary>Samples taken for a button press (buttons are a simple "is it down" check).</summary>
    public int SampleCount { get; init; } = 100;
    public int SampleIntervalMs { get; init; } = 15;
    public int WakeTimeoutPolls { get; init; } = 3000;   // about 45 seconds
    public int MaxAttempts { get; init; } = 3;

    /// <summary>How long a control must stay steady (at a stop, or at rest) before it counts. Long enough that a pause cannot pass for a stop.</summary>
    public int HoldMs { get; init; } = 1200;
    /// <summary>How long to wait for a stop to be reached and held before giving up on that try.</summary>
    public int StopTimeoutMs { get; init; } = 40000;
    /// <summary>How long to wait for the wheel to settle at rest.</summary>
    public int RestTimeoutMs { get; init; } = 15000;
    /// <summary>The wheel's reading may wander this much (about 0.8% of its range) and still count as steady.</summary>
    public int WheelTolerance { get; init; } = 250;
    /// <summary>Pedals are springier, so they are given more room.</summary>
    public int PedalTolerance { get; init; } = 450;
    /// <summary>At rest every axis must stay within this band (a little above the wheel's own noise).</summary>
    public int RestTolerance { get; init; } = 500;
}

public sealed record CalibrationResult(DeviceMap Map, IReadOnlyList<string> Report);

public sealed class CalibrationWizard
{
    private static readonly (string name, string label)[] Pedals =
    [
        ("throttle", "the ACCELERATOR"),
        ("brake", "the BRAKE"),
        ("clutch", "the CLUTCH"),
    ];

    private static readonly (string name, string label)[] Buttons =
    [
        ("paddleL", "the LEFT paddle shifter"),
        ("paddleR", "the RIGHT paddle shifter"),
        ("buttonA", "the A button"),
        ("buttonB", "the B button"),
        ("buttonX", "the X button"),
        ("buttonY", "the Y button"),
        ("view", "the View (back) button"),
        ("menu", "the Menu (start) button"),
    ];

    // Same stored names (so profiles are the same for every wheel); only the wording differs.
    private static readonly (string name, string label)[] PlayStationButtons =
    [
        ("paddleL", "the LEFT paddle shifter"),
        ("paddleR", "the RIGHT paddle shifter"),
        ("buttonA", "the Cross (X) button"),
        ("buttonB", "the Circle button"),
        ("buttonX", "the Square button"),
        ("buttonY", "the Triangle button"),
        ("view", "the Share (or Create) button"),
        ("menu", "the Options button"),
    ];

    private readonly IWheelSource _wheel;
    private readonly IWizardIO _io;
    private readonly CalibrationOptions _opt;

    public CalibrationWizard(IWheelSource wheel, IWizardIO io, CalibrationOptions options)
    {
        _wheel = wheel; _io = io; _opt = options;
    }

    private int Interval => Math.Max(1, _opt.SampleIntervalMs);
    private int HoldSamples => Math.Max(5, _opt.HoldMs / Interval);
    private int ProgressEvery => Math.Max(1, 60 / Interval);   // about 16 updates a second

    /// <summary>A short fixed-length recording, used for buttons (a press is simply "down" or "up").</summary>
    private List<RawInput> Capture()
    {
        var samples = new List<RawInput>(_opt.SampleCount);
        for (int i = 0; i < _opt.SampleCount; i++)
        {
            samples.Add(_wheel.Read());
            _io.Pause(_opt.SampleIntervalMs);
        }
        return samples;
    }

    /// <summary>Position along the axis as a percentage of the way from rest to the end of its 0..65535 range.</summary>
    private static int PercentToEnd(int rest, int value)
    {
        double room = value < rest ? rest : 65535 - rest;
        return room <= 0 ? 0 : (int)Math.Clamp(Math.Round(Math.Abs(value - rest) / room * 100), 0, 100);
    }

    /// <summary>
    /// Wait until every axis has been still for the hold time, with live feedback. Returns the median reading, or null if
    /// something kept moving for the whole timeout.
    /// </summary>
    private int[]? WatchStill(string label)
    {
        var det = new StillDetector(_opt.RestTolerance, HoldSamples);
        var recent = new List<RawInput>();
        int polls = Math.Max(50, _opt.RestTimeoutMs / Interval);
        for (int i = 0; i < polls; i++)
        {
            RawInput r = _wheel.Read();
            recent.Add(r);
            if (recent.Count > HoldSamples) recent.RemoveAt(0);
            det.Add(r.Axes);

            if (i % ProgressEvery == 0 || det.Still)
                _io.Progress(new WizardProgress(label, r.Axes[0], r.Axes[0], r.Axes[0], det.Progress, det.Still,
                    det.Streak < 5 ? "Still moving… let go and keep everything still." : $"Settling… {det.Progress:P0}"));
            if (det.Still)
            {
                _io.Pause(500);   // let "locked in" show for a moment
                return CalibrationAnalyzer.MedianAxes(recent);
            }
            _io.Pause(_opt.SampleIntervalMs);
        }
        return null;
    }

    /// <summary>
    /// Watch a control until it is held steady at a stop. With <paramref name="fixedAxis"/> unset, the first axis to move a long way is
    /// taken as the control. <paramref name="qualifies"/> can require the reading to be on a particular side of rest.
    /// Returns the median reading of the held position, or null on timeout.
    /// </summary>
    private int[]? WatchForStop(string label, string hint, int[] rest, IReadOnlyCollection<int> excluded, int? fixedAxis,
                                int tolerance, Func<int, bool>? qualifies, out int axis)
    {
        axis = fixedAxis ?? -1;
        HoldDetector? det = fixedAxis is int fa ? new HoldDetector(rest[fa], CalibrationAnalyzer.MinChange, tolerance, HoldSamples, qualifies) : null;
        var recent = new List<RawInput>();
        int polls = Math.Max(50, _opt.StopTimeoutMs / Interval);
        for (int i = 0; i < polls; i++)
        {
            RawInput r = _wheel.Read();
            recent.Add(r);
            if (recent.Count > HoldSamples) recent.RemoveAt(0);

            if (axis < 0)
            {
                if (CalibrationAnalyzer.DetectAxis(rest, r.Axes, excluded) is int found)
                {
                    axis = found;
                    det = new HoldDetector(rest[axis], CalibrationAnalyzer.MinChange, tolerance, HoldSamples, qualifies);
                    foreach (RawInput old in recent) det.Add(old.Axes[axis]);
                }
            }
            else det!.Add(r.Axes[axis]);

            if (i % ProgressEvery == 0 || det is { Locked: true })
            {
                _io.Progress(det is null
                    ? new WizardProgress(label, r.Axes[0], r.Axes[0], r.Axes[0], 0, false, hint)
                    : new WizardProgress(label, det.Value, rest[axis], det.Extreme, det.Progress, det.Locked, Describe(det, rest[axis])));

                // "Use this position": allowed once the control has clearly moved away from rest.
                if (det is not null && det.Far && _io.AcceptNow())
                {
                    int[] taken = CalibrationAnalyzer.MedianAxes(recent.TakeLast(Math.Min(recent.Count, 12)).ToList());
                    _io.Say($"{label}: used {taken[axis]} ({PercentToEnd(rest[axis], taken[axis])}% of the way to the end of the range).");
                    return taken;
                }
            }
            if (det is { Locked: true })
            {
                int[] held = CalibrationAnalyzer.MedianAxes(recent);
                _io.Say($"{label}: locked at {held[axis]} ({PercentToEnd(rest[axis], held[axis])}% of the way to the end of the range).");
                _io.Pause(600);   // let "locked in" show for a moment
                return held;
            }
            _io.Pause(_opt.SampleIntervalMs);
        }
        return null;
    }

    private string Describe(HoldDetector det, int rest)
    {
        if (!det.Far) return "Keep going: turn or press it further.";
        int pct = PercentToEnd(rest, det.Value);
        string where = $"{pct}% of the way to the end of the range";
        if (det.Locked) return $"Locked in at {where}.";
        if (det.Streak < 5) return $"Moving… push firmly against the stop and hold it there. ({where})";
        string hold = $"Steady, keep holding… {det.Progress:P0}  ({where})";
        return pct < 60 ? hold + " That is short of the end. If it can go further, push on." : hold;
    }

    /// <summary>Runs the wizard. Null means it had to give up (the wheel never woke, or steering could not be measured).</summary>
    public CalibrationResult? Run()
    {
        var report = new List<string>();

        // --- 0. Wake the device. Until something moves it reports a meaningless default snapshot.
        _io.Say("Step 1: wiggle the wheel left and right a little so it starts reporting.");
        int[] baseline = _wheel.Read().Axes.ToArray();
        bool awake = false;
        for (int i = 0; i < _opt.WakeTimeoutPolls && !awake; i++)
        {
            int[] now = _wheel.Read().Axes;
            awake = Enumerable.Range(0, 8).Any(a => Math.Abs(now[a] - baseline[a]) > 1500);
            if (!awake) _io.Pause(_opt.SampleIntervalMs);
        }
        if (!awake)
        {
            _io.Say("No movement detected. Check the wheel is powered and plugged in, then try again.");
            return null;
        }
        _io.Say("Wheel is awake. If you have limited the steering angle in Logitech G HUB, turn only to the stop G HUB sets, not the wheel's mechanical limit: this measures whatever range the wheel reports.");

        // --- 1. Rest: wait until everything has been still for a moment.
        int[]? rest = null;
        RawInput[] restFrames = [];
        for (int attempt = 1; attempt <= _opt.MaxAttempts && rest is null; attempt++)
        {
            _io.Step("Step 2: take your hands off the wheel and your feet off the pedals. After you press Record it waits until everything has settled.", false);
            rest = WatchStill("Resting");
            if (rest is null) _io.Say("Something was still moving, so the wheel never settled. Hold still and try again.");
        }
        if (rest is null) { _io.Say("Could not get a steady rest reading."); return null; }
        bool[] restButtons = CalibrationAnalyzer.ButtonsDown(Capture());

        // --- 2. Locks: live, each one held steady at its stop.
        WheelCalibration? wheel = null;
        for (int attempt = 1; attempt <= _opt.MaxAttempts && wheel is null; attempt++)
        {
            _io.Step("Step 3: turn the wheel all the way to the LEFT stop and keep pushing against it. After you press Record, hold it there until the meter is full.", false);
            int[]? left = WatchForStop("Left stop", "Turn the wheel to the left stop…", rest, [], null, _opt.WheelTolerance, null, out int axis);

            int[]? right = null;
            if (left is not null)
            {
                _io.Step("Step 4: now turn it all the way to the RIGHT stop and keep pushing against it. After you press Record, hold it there until the meter is full.", false);
                int leftValue = left[axis];
                right = WatchForStop("Right stop", "Turn the wheel to the right stop…", rest, [], axis, _opt.WheelTolerance,
                                     v => (long)(v - rest[axis]) * (leftValue - rest[axis]) < 0, out _);   // the far side of centre
            }

            if (left is null || right is null)
            {
                _io.Say("The wheel did not seem to move to a stop that it held steady. Let's try that again.");
                continue;
            }

            var measured = CalibrationAnalyzer.Steering(rest, left, right);
            if (measured is null)
                _io.Say("The wheel did not seem to move between left and right. Let's try that again.");
            else if (CalibrationAnalyzer.IsLopsided(measured) && attempt < _opt.MaxAttempts)
                _io.Say($"Left and right travel are very different ({measured.Centre - measured.Min} counts to the left, {measured.Max - measured.Centre} to the right). " +
                        "That usually means one side was not turned all the way. Turn the wheel firmly to the stop on both sides and try again.");
            else wheel = measured;
        }
        if (wheel is null) { _io.Say("Could not measure the steering."); return null; }
        var usedAxes = new HashSet<int> { wheel.Axis };
        if (CalibrationAnalyzer.IsLopsided(wheel))
            report.Add("WARNING: left and right travel differ a lot, so steering will feel uneven. Run calibration again and turn all the way to both stops.");
        report.Add($"wheel: axis {AxisDef.SourceNames[wheel.Axis]}, {wheel.Min}..{wheel.Max}, centre {wheel.Centre}{(wheel.Inverted ? ", inverted (left reads high)" : "")}");
        int leftTravel = wheel.Centre - wheel.Min, rightTravel = wheel.Max - wheel.Centre;
        report.Add($"travel: {leftTravel} counts one way, {rightTravel} the other ({Math.Min(leftTravel, rightTravel) * 100 / Math.Max(1, Math.Max(leftTravel, rightTravel))}% balanced)");

        // --- 3. Pedals
        var map = new DeviceMap
        {
            Name = _opt.DeviceId,
            Vid = _opt.Vid,
            Pid = _opt.Pid,
        };
        map.Axes["wheel"] = new AxisDef
        {
            Source = AxisDef.SourceNames[wheel.Axis], Signed = true, Invert = wheel.Inverted,
            Min = wheel.Min, Max = wheel.Max, Centre = wheel.Centre,
        };

        int pedalNumber = 5;
        foreach (var (name, label) in Pedals)
        {
            bool allowSkip = name == "clutch";
            PedalCalibration? pedal = null;
            bool skipped = false;
            for (int attempt = 1; attempt <= _opt.MaxAttempts && pedal is null && !skipped; attempt++)
            {
                string skipHint = allowSkip ? " (or skip if there is no clutch)" : "";
                if (!_io.Step($"Step {pedalNumber}: release the other pedals, then press {label} all the way down and keep it pressed. After you press Record, hold it until the meter is full{skipHint}.", allowSkip))
                {
                    skipped = true;
                    break;
                }
                int[]? pressed = WatchForStop(label.Replace("the ", ""), $"Press {label.Replace("the ", "").ToLowerInvariant()} all the way down…", rest, usedAxes, null, _opt.PedalTolerance, null, out _);
                pedal = pressed is null ? null : CalibrationAnalyzer.Pedal(rest, pressed, usedAxes);
                if (pedal is null) _io.Say($"No pedal moved enough for {name}. Press it fully and hold it.");
            }
            pedalNumber++;

            if (pedal is null)
            {
                report.Add($"{name}: skipped");
                continue;
            }
            usedAxes.Add(pedal.Axis);
            map.Axes[name] = new AxisDef
            {
                Source = AxisDef.SourceNames[pedal.Axis], Signed = false, Invert = pedal.Invert,
                Min = pedal.Min, Max = pedal.Max,
            };
            report.Add($"{name}: axis {AxisDef.SourceNames[pedal.Axis]}, {pedal.Min}..{pedal.Max}{(pedal.Invert ? ", inverted (reads low when pressed)" : "")}");
        }

        // --- 4. Buttons
        _io.Say("Next: name your buttons. For each one, press and hold it, then continue. You can skip any.");
        var usedButtons = new HashSet<int>();
        foreach (var (name, label) in _opt.PlayStationLabels ? PlayStationButtons : Buttons)
        {
            int? found = null;
            bool skipped = false;
            for (int attempt = 1; attempt <= _opt.MaxAttempts && found is null && !skipped; attempt++)
            {
                if (!_io.Step($"Press and hold {label}, then continue (or skip).", true)) { skipped = true; break; }
                found = CalibrationAnalyzer.DetectButton(restButtons, CalibrationAnalyzer.ButtonsDown(Capture()), usedButtons, out string? problem);
                if (found is null) _io.Say(problem ?? "Not detected.");
            }
            if (found is int b)
            {
                usedButtons.Add(b);
                map.Buttons[name] = b + 1;
                report.Add($"{name}: button {b + 1}");
            }
            else report.Add($"{name}: skipped");
        }

        // --- 5. Rotation
        string answer = _io.Ask("Wheel rotation lock to lock in degrees: the angle set in Logitech G HUB, or 900 if you have not changed it", "900");
        map.RotationDegrees = double.TryParse(answer, out double deg) && deg is >= 90 and <= 2520 ? deg : 900;
        report.Add($"rotation: {map.RotationDegrees} degrees");

        return new CalibrationResult(map, report);
    }
}
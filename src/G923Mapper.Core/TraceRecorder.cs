using System.Globalization;
using System.Text;

namespace G923Mapper;

/// <summary>One moment of the wheel: when, where it is, and the centring force being sent to it.</summary>
/// <param name="Ms">Milliseconds since the recording began.</param>
/// <param name="Angle">Degrees from the wheel's centre (the centre may be followed by the auto-centre, so this can step).</param>
/// <param name="Raw">Position in degrees with no centre at all, which only ever changes when the wheel moves.</param>
/// <param name="Force">The signed force sent for the curve, in the device's units (-10000 to 10000); 0 when the curve is not running.</param>
/// <param name="CurveOn">Whether the app's own curve (rather than the plain spring) was in charge.</param>
public sealed record TraceSample(long Ms, double Angle, double Raw, int Force, bool CurveOn);

/// <summary>What a recording shows, in numbers and in a sentence.</summary>
public sealed record TraceSummary(int Count, double Seconds, double PeakToPeakDegrees, int Crossings, double SwingsPerSecond, double FirstThirdDegrees, double LastThirdDegrees, string Verdict);

/// <summary>
/// A short recording of the wheel and the force sent to it, so that "it keeps swinging left and right" can be looked at as numbers: how far, how
/// fast, and whether the swing dies out or grows. Collects samples for a set time, writes them as a CSV, and says in a sentence what happened.
/// </summary>
public sealed class TraceRecorder
{
    private readonly List<TraceSample> _samples = [];
    private long _startMs = -1;

    /// <param name="durationMs">How long to record.</param>
    /// <param name="minGapMs">The least time between samples, so a fast loop does not fill the file.</param>
    public TraceRecorder(long durationMs, long minGapMs = 2)
    {
        DurationMs = Math.Max(500, durationMs);
        MinGapMs = Math.Max(1, minGapMs);
    }

    public long DurationMs { get; }
    public long MinGapMs { get; }
    public IReadOnlyList<TraceSample> Samples => _samples;

    /// <summary>Add what the wheel is doing now. Returns false once the recording time is up (the sample that ends it is still kept).</summary>
    public bool Add(long nowMs, double angle, double raw, int force, bool curveOn)
    {
        if (_startMs < 0) _startMs = nowMs;
        long t = nowMs - _startMs;
        if (_samples.Count == 0 || t - _samples[^1].Ms >= MinGapMs) _samples.Add(new TraceSample(t, angle, raw, force, curveOn));
        return t < DurationMs;
    }

    /// <summary>The samples as a CSV that opens in a spreadsheet.</summary>
    public string ToCsv()
    {
        var sb = new StringBuilder("ms,angle_deg,raw_deg,force_units,curve_on\n");
        foreach (var s in _samples)
            sb.Append(s.Ms.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(s.Angle.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
              .Append(s.Raw.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
              .Append(s.Force.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(s.CurveOn ? 1 : 0).Append('\n');
        return sb.ToString();
    }

    /// <summary>
    /// Say what happened. The swing is measured on the raw position around where the wheel spent most of its time (its middle value), in thirds
    /// of the recording: a nudge at the start that settles shows a big first third and a small last one; a wheel that keeps going shows both big.
    /// </summary>
    public TraceSummary Summarise()
    {
        if (_samples.Count < 10)
            return new TraceSummary(_samples.Count, 0, 0, 0, 0, 0, 0, "Too little was recorded to say anything.");

        double seconds = _samples[^1].Ms / 1000.0;
        var raws = _samples.Select(s => s.Raw).OrderBy(r => r).ToList();
        double rest = raws[raws.Count / 2];                          // the middle value: where the wheel mostly was
        double peakToPeak = raws[^1] - raws[0];

        int third = _samples.Count / 3;
        double Swing(IEnumerable<TraceSample> part)
        {
            var list = part.Select(s => s.Raw).ToList();
            return list.Count == 0 ? 0 : (list.Max() - list.Min()) / 2;
        }
        double first = Swing(_samples.Take(third)), last = Swing(_samples.Skip(_samples.Count - third));

        // Count the times the wheel crosses its rest position in the last third, ignoring tiny jitter, to get how fast it swings.
        const double band = 0.4;
        int crossings = 0, side = 0;
        foreach (var s in _samples.Skip(_samples.Count - third))
        {
            double d = s.Raw - rest;
            int now = d > band ? 1 : d < -band ? -1 : 0;
            if (now == 0) continue;
            if (side != 0 && now != side) crossings++;
            side = now;
        }
        double lastSeconds = Math.Max(0.001, (_samples[^1].Ms - _samples[_samples.Count - third].Ms) / 1000.0);
        double swingsPerSecond = crossings / 2.0 / lastSeconds;

        string verdict;
        if (peakToPeak < 1.5) verdict = "The wheel stayed still: it moved less than 1.5 degrees, so there was nothing to swing.";
        else if (last <= 0.4 * first || last < 0.6) verdict = $"The wheel moved up to {peakToPeak:0} degrees and then settled. It does not keep swinging.";
        else if (crossings >= 4 && last > 1.3 * first) verdict = $"The swing is growing: about {swingsPerSecond:0.0} swings a second, now {last:0.0} degrees either side. The force is feeding it.";
        else if (crossings >= 4) verdict = $"The wheel keeps swinging by itself: about {swingsPerSecond:0.0} swings a second, {last:0.0} degrees either side of rest, and it is not dying out.";
        else verdict = $"The wheel moved up to {peakToPeak:0} degrees and ended {last:0.0} degrees from where it rested; it did not swing back and forth.";

        return new TraceSummary(_samples.Count, seconds, peakToPeak, crossings, swingsPerSecond, first, last, verdict);
    }
}

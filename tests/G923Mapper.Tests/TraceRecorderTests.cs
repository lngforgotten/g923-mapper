using G923Mapper;

namespace G923Mapper.Tests;

public class TraceRecorderTests
{
    /// <summary>A wheel swinging at <paramref name="hz"/> swings a second, its size multiplied by <paramref name="envelope"/> of the progress 0..1, for 10 seconds.</summary>
    private static TraceRecorder Record(double hz, Func<double, double> envelope, double startOffset = 100, long ms = 10_000)
    {
        var rec = new TraceRecorder(ms);
        for (long t = 0; t <= ms; t += 4)
        {
            double raw = startOffset + envelope(t / (double)ms) * Math.Sin(2 * Math.PI * hz * t / 1000.0);
            rec.Add(t, raw - startOffset, raw, (int)(raw * 10), curveOn: true);
        }
        return rec;
    }

    [Fact]
    public void A_still_wheel_is_reported_as_still()
    {
        var s = Record(2, _ => 0.2).Summarise();
        Assert.Contains("stayed still", s.Verdict);
    }

    [Fact]
    public void A_nudge_that_dies_away_is_reported_as_settling()
    {
        var s = Record(2.5, p => 15 * Math.Exp(-5 * p)).Summarise();
        Assert.Contains("settled", s.Verdict);
        Assert.True(s.FirstThirdDegrees > s.LastThirdDegrees * 2);
    }

    [Fact]
    public void A_swing_that_carries_on_is_reported_with_its_speed_and_size()
    {
        var s = Record(3, _ => 6).Summarise();
        Assert.Contains("keeps swinging", s.Verdict);
        Assert.InRange(s.SwingsPerSecond, 2.5, 3.5);
        Assert.InRange(s.LastThirdDegrees, 5, 7);
    }

    [Fact]
    public void A_swing_that_grows_is_reported_as_growing()
    {
        var s = Record(2, p => 2 + 14 * p).Summarise();
        Assert.Contains("growing", s.Verdict);
    }

    [Fact]
    public void A_wheel_that_moved_and_stayed_there_is_not_called_a_swing()
    {
        var rec = new TraceRecorder(10_000);
        for (long t = 0; t <= 10_000; t += 4) { double raw = t < 2000 ? t / 100.0 : 20; rec.Add(t, raw, raw, 0, false); }
        var s = rec.Summarise();
        Assert.DoesNotContain("keeps swinging", s.Verdict);
        Assert.Contains("settled", s.Verdict);
    }

    [Fact]
    public void Too_few_samples_say_so_instead_of_inventing_a_verdict()
    {
        var rec = new TraceRecorder(5000);
        rec.Add(0, 0, 0, 0, false);
        Assert.Contains("Too little", rec.Summarise().Verdict);
    }

    [Fact]
    public void Recording_stops_asking_for_samples_when_the_time_is_up_and_keeps_the_last_one()
    {
        var rec = new TraceRecorder(1000, minGapMs: 1);
        Assert.True(rec.Add(5000, 0, 0, 0, false));        // the first call fixes the start
        Assert.True(rec.Add(5500, 0, 0, 0, false));
        Assert.False(rec.Add(6000, 0, 0, 0, false));       // 1000 ms after the start: done
        Assert.Equal(3, rec.Samples.Count);
        Assert.Equal(1000, rec.Samples[^1].Ms);
    }

    [Fact]
    public void Samples_closer_than_the_gap_are_not_kept_so_a_fast_loop_does_not_fill_the_file()
    {
        var rec = new TraceRecorder(10_000, minGapMs: 5);
        for (long t = 0; t < 100; t++) rec.Add(t, 0, 0, 0, false);
        Assert.InRange(rec.Samples.Count, 18, 21);
    }

    [Fact]
    public void The_csv_has_a_header_and_one_line_per_sample_with_dots_for_decimals()
    {
        var rec = new TraceRecorder(1000, minGapMs: 1);
        rec.Add(0, 1.5, 100.25, -2500, true);
        rec.Add(10, -0.5, 99.75, 0, false);
        string[] lines = rec.ToCsv().TrimEnd('\n').Split('\n');
        Assert.Equal("ms,angle_deg,raw_deg,force_units,curve_on", lines[0]);
        Assert.Equal("0,1.500,100.250,-2500,1", lines[1]);
        Assert.Equal("10,-0.500,99.750,0,0", lines[2]);
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public void A_very_short_duration_is_raised_to_something_usable()
    {
        Assert.True(new TraceRecorder(1).DurationMs >= 500);
    }
}

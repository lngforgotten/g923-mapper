using G923Mapper;

namespace G923Mapper.Tests;

public class CentreTrackerTests
{
    // A 360 degree wheel on a 65535-count axis: about 182 counts per degree.
    private const int Origin = 32850;
    private const double Cpd = 65535.0 / 360;

    private static CentreTracker New() => new(Origin, Cpd);

    /// <summary>Feed the same reading every 20 ms for the given number of ms; returns the last centre change, if any.</summary>
    private static int? Hold(CentreTracker t, int raw, ref long now, int ms, bool eligible = true)
    {
        int? last = null;
        for (int i = 0; i < ms; i += 20, now += 20)
            if (t.Update(raw, now, eligible) is int c) last = c;
        return last;
    }

    [Fact]
    public void A_wheel_that_settles_a_little_off_becomes_the_new_centre()
    {
        var t = New();
        long now = 0;
        int offByTwoDegrees = Origin + (int)(2 * Cpd);
        Assert.Equal(offByTwoDegrees, Hold(t, offByTwoDegrees, ref now, 1000));
        Assert.Equal(offByTwoDegrees, t.Centre);
        Assert.Equal(2, t.ShiftDegrees, 1);
    }

    [Fact]
    public void Nothing_changes_before_the_hold_time_is_up()
    {
        var t = New();
        long now = 0;
        Assert.Null(Hold(t, Origin + (int)(2 * Cpd), ref now, 500));
        Assert.Equal(Origin, t.Centre);
    }

    [Fact]
    public void A_moving_wheel_is_never_taken_as_the_centre()
    {
        var t = New();
        long now = 0;
        for (int i = 0; i < 200; i++, now += 20)
            Assert.Null(t.Update(Origin + (int)(1.5 * Cpd) + (i % 2) * (int)(2 * Cpd), now, true));   // jitters by 2 degrees
        Assert.Equal(Origin, t.Centre);
    }

    [Fact]
    public void A_wheel_held_well_off_centre_is_not_the_centre()
    {
        var t = New();
        long now = 0;
        Assert.Null(Hold(t, Origin + (int)(10 * Cpd), ref now, 5000));   // a steady corner
        Assert.Equal(Origin, t.Centre);
    }

    [Fact]
    public void The_centre_never_strays_more_than_the_cap_from_where_it_started()
    {
        var t = New();
        long now = 0;
        Hold(t, Origin + (int)(2.5 * Cpd), ref now, 1000);     // fine: within 3 degrees
        Assert.Equal(2.5, t.ShiftDegrees, 1);
        Hold(t, Origin + (int)(5 * Cpd), ref now, 3000);       // beyond the cap: ignored
        Assert.Equal(2.5, t.ShiftDegrees, 1);
    }

    [Fact]
    public void Tiny_differences_are_ignored_so_the_centre_does_not_creep()
    {
        var t = New();
        long now = 0;
        Assert.Null(Hold(t, Origin + (int)(0.1 * Cpd), ref now, 3000));
        Assert.Equal(Origin, t.Centre);
    }

    [Fact]
    public void A_pressed_pedal_or_a_sleeping_wheel_pauses_learning_and_restarts_the_timing()
    {
        var t = New();
        long now = 0;
        int off = Origin + (int)(2 * Cpd);
        Assert.Null(Hold(t, off, ref now, 3000, eligible: false));
        Assert.Equal(Origin, t.Centre);
        Assert.Null(Hold(t, off, ref now, 500));                // timing starts only once eligible again
        Assert.NotNull(Hold(t, off, ref now, 500));
    }

    [Fact]
    public void It_can_follow_the_wheel_back_when_it_settles_somewhere_else_within_the_cap()
    {
        var t = New();
        long now = 0;
        Hold(t, Origin + (int)(2 * Cpd), ref now, 1000);
        Assert.Equal(2, t.ShiftDegrees, 1);
        Hold(t, Origin - (int)(1 * Cpd), ref now, 1500);
        Assert.Equal(-1, t.ShiftDegrees, 1);
    }

    [Fact]
    public void Reset_takes_a_new_centre_and_forgets_the_pending_timing()
    {
        var t = New();
        long now = 0;
        Hold(t, Origin + (int)(2 * Cpd), ref now, 500);
        t.Reset(Origin + 5);
        Assert.Equal(Origin + 5, t.Centre);
        Assert.Null(Hold(t, Origin + (int)(2 * Cpd), ref now, 500));   // timing restarted
    }

    [Fact]
    public void An_already_centred_wheel_stays_put()
    {
        var t = New();
        long now = 0;
        Assert.Null(Hold(t, Origin, ref now, 5000));
        Assert.Equal(0, t.ShiftDegrees, 3);
    }
}

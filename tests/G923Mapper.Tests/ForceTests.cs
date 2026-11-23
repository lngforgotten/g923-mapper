using G923Mapper;

namespace G923Mapper.Tests;

public class ForceMathTests
{
    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(255, 100, 10000)]
    [InlineData(255, 50, 5000)]
    [InlineData(255, 0, 0)]
    [InlineData(128, 100, 5020)]
    [InlineData(255, 250, 10000)]   // percent is clamped
    [InlineData(255, -5, 0)]
    public void Rumble_scales_motor_by_strength(int motor, int percent, int expected)
        => Assert.Equal(expected, ForceMath.RumbleMagnitude((byte)motor, percent));

    [Fact]
    public void Rumble_never_exceeds_the_directinput_maximum()
    {
        for (int m = 0; m <= 255; m++)
            for (int p = 0; p <= 100; p += 10)
                Assert.InRange(ForceMath.RumbleMagnitude((byte)m, p), 0, ForceMath.Max);
    }

    [Fact]
    public void Rumble_grows_with_motor_and_with_strength()
    {
        Assert.True(ForceMath.RumbleMagnitude(200, 50) > ForceMath.RumbleMagnitude(100, 50));
        Assert.True(ForceMath.RumbleMagnitude(100, 80) > ForceMath.RumbleMagnitude(100, 40));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(20, 1000)]
    [InlineData(100, 5000)]
    [InlineData(300, 5000)]
    [InlineData(-1, 0)]
    public void Spring_coefficient_follows_the_percentage(int percent, int expected)
        => Assert.Equal(expected, ForceMath.SpringCoefficient(percent));

    [Fact]
    public void The_springs_top_end_is_well_below_the_devices_maximum()
    {
        // 100% on the slider must not be the absolute maximum: that fights every turn and feels sluggish.
        Assert.True(ForceMath.SpringCoefficient(100) <= ForceMath.Max / 2);
        Assert.True(ForceMath.SpringCoefficient(100) > 0);
    }

    [Fact]
    public void Unchanged_pad_frames_are_recognised_so_they_are_not_resent()
    {
        var a = new PadState { LeftX = 100, RightTrigger = 50 }; a[PadButton.A] = true;
        var b = new PadState { LeftX = 100, RightTrigger = 50 }; b[PadButton.A] = true;
        Assert.True(a.SameAs(b));

        b.LeftX = 101; Assert.False(a.SameAs(b));
        b.LeftX = 100; b[PadButton.B] = true; Assert.False(a.SameAs(b));
        b[PadButton.B] = false; b.Keys.Add("Q"); Assert.False(a.SameAs(b));
        b.Keys.Clear(); b.LeftTrigger = 1; Assert.False(a.SameAs(b));
        b.LeftTrigger = 0; Assert.True(a.SameAs(b));
    }

    [Fact]
    public void Heavy_rumble_is_slower_than_light_rumble()
        => Assert.True(ForceMath.HeavyPeriodMicroseconds > ForceMath.LightPeriodMicroseconds);

    [Fact]
    public void Defaults_are_gentle()
    {
        var s = new ForceSettings();
        Assert.True(s.Enabled);
        Assert.Equal(50, s.RumblePercent);
        Assert.Equal(0, s.SpringPercent);   // no surprise pull on the wheel
        var c = s.Clone(); c.RumblePercent = 90;
        Assert.Equal(50, s.RumblePercent);
    }

    [Fact]
    public void The_fake_pad_delivers_rumble_events()
    {
        var pad = new FakePad();
        (byte, byte)? got = null;
        pad.Rumble += (l, s) => got = (l, s);
        pad.RaiseRumble(200, 40);
        Assert.Equal(((byte)200, (byte)40), got);
    }
}

public class MotorChoiceTests
{
    [Theory]
    [InlineData("both", 200, 100, 200, 100)]
    [InlineData("heavy", 200, 100, 200, 0)]
    [InlineData("light", 200, 100, 0, 100)]
    [InlineData("HEAVY", 200, 100, 200, 0)]
    [InlineData("", 200, 100, 200, 100)]
    [InlineData(null, 200, 100, 200, 100)]
    [InlineData("nonsense", 200, 100, 200, 100)]
    public void Only_the_chosen_motor_is_played(string? choice, int large, int small, int expectLarge, int expectSmall)
    {
        var (l, s) = ForceMath.SelectMotors((byte)large, (byte)small, choice);
        Assert.Equal(expectLarge, l);
        Assert.Equal(expectSmall, s);
    }

    [Fact]
    public void The_default_is_both_and_it_survives_cloning()
    {
        var f = new ForceSettings();
        Assert.Equal("both", f.Motors);
        f.Motors = "light";
        Assert.Equal("light", f.Clone().Motors);
    }
}

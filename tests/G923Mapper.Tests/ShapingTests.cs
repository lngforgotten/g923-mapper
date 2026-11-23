using G923Mapper;

namespace G923Mapper.Tests;

public class ShapingTests
{
    private static double S(double v, double dz = 0, string curve = "linear", double exp = 2, double[][]? pts = null, double ad = 0)
        => Shaping.Shape(v, dz, curve, exp, pts, ad);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.25, 0.25)]
    [InlineData(-0.7, -0.7)]
    [InlineData(1, 1)]
    [InlineData(-1, -1)]
    public void Linear_is_identity(double input, double expected) => Assert.Equal(expected, S(input), 10);

    [Fact]
    public void Out_of_range_input_is_clamped()
    {
        Assert.Equal(1, S(3));
        Assert.Equal(-1, S(-3));
    }

    [Fact]
    public void NaN_becomes_zero() => Assert.Equal(0, S(double.NaN));

    [Theory]
    [InlineData(0.05, 0)]        // inside the deadzone
    [InlineData(0.10, 0)]        // exactly on the edge
    [InlineData(0.55, 0.5)]      // (0.55 - 0.1) / 0.9
    [InlineData(1.0, 1.0)]       // full travel still reaches full output
    [InlineData(-0.55, -0.5)]    // symmetric
    public void Deadzone_removes_centre_and_rescales(double input, double expected)
        => Assert.Equal(expected, S(input, dz: 0.1), 10);

    [Theory]
    [InlineData(0.5, 0.25)]
    [InlineData(-0.5, -0.25)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    public void Expo_squares_by_default(double input, double expected)
        => Assert.Equal(expected, S(input, curve: "expo"), 10);

    [Fact]
    public void Expo_uses_the_exponent() => Assert.Equal(0.125, S(0.5, curve: "expo", exp: 3), 10);

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.25, 0.1)]
    [InlineData(0.5, 0.2)]
    [InlineData(0.75, 0.6)]
    [InlineData(1.0, 1.0)]
    [InlineData(-0.75, -0.6)]
    public void Custom_curve_interpolates_between_points(double input, double expected)
    {
        double[][] pts = [[0, 0], [0.5, 0.2], [1, 1]];
        Assert.Equal(expected, S(input, curve: "custom", pts: pts), 10);
    }

    [Fact]
    public void Custom_curve_holds_the_end_values_outside_its_points()
    {
        double[][] pts = [[0.2, 0.1], [0.8, 0.9]];
        Assert.Equal(0.1, S(0.05, curve: "custom", pts: pts), 10);
        Assert.Equal(0.9, S(0.95, curve: "custom", pts: pts), 10);
    }

    [Fact]
    public void AntiDeadzone_lifts_any_nonzero_input_but_leaves_zero_alone()
    {
        Assert.Equal(0, S(0, ad: 0.06));
        Assert.Equal(0.06, S(1e-9, ad: 0.06), 6);
        Assert.Equal(1, S(1, ad: 0.06), 10);
        Assert.Equal(-0.53, S(-0.5, ad: 0.06), 10);   // 0.06 + 0.94 * 0.5
    }

    [Fact]
    public void Deadzone_is_applied_before_the_antiDeadzone()
    {
        // Inside the deadzone the output must stay exactly zero, not jump to the anti-deadzone.
        Assert.Equal(0, S(0.05, dz: 0.1, ad: 0.06));
        Assert.Equal(0.06, S(0.10001, dz: 0.1, ad: 0.06), 3);
    }

    [Fact]
    public void Curve_is_applied_after_the_deadzone()
    {
        // (0.6 - 0.2) / 0.8 = 0.5, then squared = 0.25
        Assert.Equal(0.25, S(0.6, dz: 0.2, curve: "expo"), 10);
    }

    [Theory]
    [InlineData(1.0, 32767)]
    [InlineData(-1.0, -32768)]
    [InlineData(0.0, 0)]
    [InlineData(5.0, 32767)]
    [InlineData(-5.0, -32768)]
    public void ToStick_covers_the_full_short_range(double v, int expected) => Assert.Equal(expected, Shaping.ToStick(v));

    [Fact]
    public void ToStick_is_monotonic()
    {
        short prev = short.MinValue;
        for (double v = -1; v <= 1; v += 0.001)
        {
            short s = Shaping.ToStick(v);
            Assert.True(s >= prev);
            prev = s;
        }
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(1.0, 255)]
    [InlineData(-1.0, 0)]
    [InlineData(2.0, 255)]
    public void ToTrigger_clamps_to_a_byte(double v, int expected) => Assert.Equal(expected, Shaping.ToTrigger(v));
}

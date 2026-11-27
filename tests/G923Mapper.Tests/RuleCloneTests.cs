using G923Mapper;

namespace G923Mapper.Tests;

public class RuleCloneTests
{
    private static Rule Filled() => new()
    {
        From = "wheel", To = "LeftStickX", Curve = "custom", Exponent = 3, Points = [[0, 0], [0.5, 0.2], [1, 1]],
        Deadzone = 0.04, AntiDeadzone = 0.06, Invert = true, Threshold = 0.35, Mode = "pulse", PulseMs = 80, Range = 270,
        Part = "positive", Smooth = 25, Expr = "max(0, x)", Turbo = true, TurboHz = 15,
    };

    [Fact]
    public void A_copy_has_every_setting()
    {
        Rule original = Filled(), copy = original.Clone(), blank = new();

        Assert.NotSame(original, copy);
        foreach (var property in typeof(Rule).GetProperties())
        {
            // A property added to Rule must be given a value in Filled, or this test would not be checking it.
            Assert.False(Equals(property.GetValue(original), property.GetValue(blank)), $"{property.Name} is not set in the test");
            Assert.Equal(property.GetValue(original), property.GetValue(copy));
        }
    }

    [Fact]
    public void Changing_the_copys_curve_points_leaves_the_original_alone()
    {
        Rule original = Filled(), copy = original.Clone();

        copy.Points![1][1] = 0.9;

        Assert.Equal(0.2, original.Points![1][1]);
    }
}

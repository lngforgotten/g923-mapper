using G923Mapper;

namespace G923Mapper.Tests;

public class ForceStyleTests
{
    [Theory]
    [InlineData("smooth", "smooth")]
    [InlineData("SHARP", "sharp")]
    [InlineData(" rough ", "rough")]
    [InlineData("", "smooth")]
    [InlineData(null, "smooth")]
    [InlineData("wobbly", "smooth")]
    public void Style_names_are_forgiving_and_unknown_means_smooth(string? input, string expected) =>
        Assert.Equal(expected, ForceMath.NormaliseStyle(input));

    [Fact]
    public void The_default_style_is_smooth_and_it_survives_cloning_and_the_profile_override()
    {
        var f = new ForceSettings();
        Assert.Equal("smooth", f.Style);
        f.Style = "rough";
        Assert.Equal("rough", f.Clone().Style);
        Assert.Equal("rough", ForceMath.Effective(f, new ProfileForce { Rumble = 10 }).Style);   // a profile does not change the style
    }

    [Fact]
    public void Every_choice_is_one_the_normaliser_keeps()
    {
        Assert.All(ForceMath.StyleChoices, c => Assert.Equal(c, ForceMath.NormaliseStyle(c)));
        Assert.Equal(3, ForceMath.StyleChoices.Length);
    }
}

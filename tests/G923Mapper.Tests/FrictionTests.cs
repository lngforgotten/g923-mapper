using G923Mapper;

namespace G923Mapper.Tests;

public class FrictionTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();

    [Fact]
    public void Friction_defaults_to_off_both_app_wide_and_in_a_profile()
    {
        Assert.Equal(0, new ForceSettings().FrictionPercent);
        Assert.Equal(0, ForceMath.Effective(new ForceSettings(), null).FrictionPercent);
        Assert.Null(new ProfileForce().Friction);
        Assert.True(new ProfileForce().IsEmpty);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 2500)]
    [InlineData(100, ForceMath.FrictionMax)]
    [InlineData(-5, 0)]
    [InlineData(250, ForceMath.FrictionMax)]
    public void The_slider_scales_to_a_coefficient_and_is_clamped(int percent, int expected) =>
        Assert.Equal(expected, ForceMath.FrictionCoefficient(percent));

    [Fact]
    public void A_profile_friction_lays_over_the_app_wide_value_and_leaving_it_out_keeps_the_app_wide_one()
    {
        var app = new ForceSettings { FrictionPercent = 30 };
        Assert.Equal(70, ForceMath.Effective(app, new ProfileForce { Friction = 70 }).FrictionPercent);
        Assert.Equal(30, ForceMath.Effective(app, new ProfileForce { Rumble = 10 }).FrictionPercent);
        Assert.Equal(100, ForceMath.Effective(app, new ProfileForce { Friction = 500 }).FrictionPercent);   // clamped
    }

    [Fact]
    public void A_profile_with_only_friction_counts_as_having_its_own_feel()
    {
        Assert.False(new ProfileForce { Friction = 20 }.IsEmpty);
    }

    [Fact]
    public void Friction_is_stored_per_profile_and_survives_saving()
    {
        var p = ProfileLoader.Parse("""{ "name": "t", "force": { "friction": 35 }, "rules": [] }""", Devices).Profile!;
        Assert.Equal(35, p.Force!.Friction);
        string json = ProfileWriter.ToJson(p);
        Assert.Contains("\"friction\": 35", json);
        Assert.Equal(35, ProfileLoader.Parse(json, Devices).Profile!.Force!.Friction);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Friction_outside_zero_to_one_hundred_is_an_error(int value)
    {
        var r = ProfileLoader.Parse($$"""{ "name": "t", "force": { "friction": {{value}} }, "rules": [] }""", Devices);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Contains("force.friction"));
    }
}

using G923Mapper;

namespace G923Mapper.Tests;

public class ProfileForceTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();

    private static ProfileLoadResult Load(string force) =>
        ProfileLoader.Parse("{ \"name\": \"t\", " + force + " \"rules\": [] }", Devices);

    [Fact]
    public void A_profile_can_ask_for_its_own_feel()
    {
        var r = Load("\"force\": { \"rumble\": 70, \"spring\": 15, \"motors\": \"heavy\" },");
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(70, r.Profile!.Force!.Rumble);
        Assert.Equal(15, r.Profile.Force.Spring);
        Assert.Equal("heavy", r.Profile.Force.Motors);
    }

    [Fact]
    public void A_profile_without_a_force_block_uses_the_app_settings()
    {
        Assert.Null(Load("").Profile!.Force);
    }

    [Theory]
    [InlineData("\"force\": { \"rumble\": 101 },", "force.rumble")]
    [InlineData("\"force\": { \"rumble\": -1 },", "force.rumble")]
    [InlineData("\"force\": { \"spring\": 250 },", "force.spring")]
    [InlineData("\"force\": { \"motors\": \"all\" },", "force.motors")]
    public void Out_of_range_values_stop_the_profile_with_a_clear_message(string force, string phrase)
    {
        var r = Load(force);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Contains(phrase));
    }

    [Fact]
    public void Profile_values_are_laid_over_the_app_wide_ones_and_the_enabled_switch_stays_app_wide()
    {
        var app = new ForceSettings { Enabled = true, RumblePercent = 50, SpringPercent = 30, Motors = "both" };
        var eff = ForceMath.Effective(app, new ProfileForce { Rumble = 80, Motors = "light" });
        Assert.Equal(80, eff.RumblePercent);
        Assert.Equal(30, eff.SpringPercent);              // not set by the profile: stays as in Settings
        Assert.Equal("light", eff.Motors);
        Assert.True(eff.Enabled);

        var off = ForceMath.Effective(new ForceSettings { Enabled = false }, new ProfileForce { Rumble = 100, Spring = 100 });
        Assert.False(off.Enabled);                         // a profile cannot switch force feedback on
    }

    [Fact]
    public void With_nothing_from_the_profile_the_result_equals_the_app_settings_without_touching_them()
    {
        var app = new ForceSettings { RumblePercent = 12, SpringPercent = 34, Motors = "heavy" };
        var eff = ForceMath.Effective(app, null);
        Assert.Equal(12, eff.RumblePercent);
        Assert.Equal(34, eff.SpringPercent);
        Assert.Equal("heavy", eff.Motors);
        Assert.NotSame(app, eff);

        var changed = ForceMath.Effective(app, new ProfileForce { Rumble = 99 });
        Assert.Equal(12, app.RumblePercent);               // the app-wide object is never modified
        Assert.Equal(99, changed.RumblePercent);
    }

    [Fact]
    public void Values_are_clamped_if_they_ever_get_past_validation()
    {
        var eff = ForceMath.Effective(new ForceSettings(), new ProfileForce { Rumble = 500, Spring = -5, Motors = "nonsense" });
        Assert.Equal(100, eff.RumblePercent);
        Assert.Equal(0, eff.SpringPercent);
        Assert.Equal("both", eff.Motors);
    }

    [Fact]
    public void The_feel_survives_saving_and_an_empty_block_is_not_written()
    {
        var p = Load("\"force\": { \"rumble\": 60, \"spring\": 20, \"motors\": \"light\" },").Profile!;
        string json = ProfileWriter.ToJson(p);
        Assert.Contains("\"force\": { \"rumble\": 60, \"spring\": 20, \"motors\": \"light\" }", json);
        var again = ProfileLoader.Parse(json, Devices).Profile!;
        Assert.Equal(60, again.Force!.Rumble);
        Assert.Equal("light", again.Force.Motors);

        p.Force = new ProfileForce();
        Assert.DoesNotContain("\"force\"", ProfileWriter.ToJson(p));
        p.Force = null;
        Assert.DoesNotContain("\"force\"", ProfileWriter.ToJson(p));
    }

    [Fact]
    public void Only_the_values_that_are_set_are_written()
    {
        var p = Load("\"force\": { \"spring\": 40 },").Profile!;
        string json = ProfileWriter.ToJson(p);
        Assert.Contains("\"force\": { \"spring\": 40 }", json);
        Assert.DoesNotContain("rumble", json);
    }
}

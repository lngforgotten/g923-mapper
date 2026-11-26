using G923Mapper;

namespace G923Mapper.Tests;

public class ProfileCentringTests
{
    private static readonly ForceSettings App = new()
    {
        SpringPercent = 30, CentringStyle = CentringCurve.StyleCurve, CentreDegrees = 12, PeakDegrees = 60, LighteningPercent = 40, DampingPercent = 5,
        FloorPercent = 8, CentringReversed = true, CentringWheel = "wheel-1",
    };

    private const string Own =
        "\"rumble\": 40, \"spring\": 45, \"centringStyle\": \"curve\", \"centreDegrees\": 16, \"peakDegrees\": 80, \"lighteningPercent\": 70, \"dampingPercent\": 10";

    private static ProfileLoadResult Load(string force, string modes = "") => ProfileLoader.Parse($$"""
        { "name": "p", "device": "g923_xbox", "force": { {{force}} },
          "rules": [ { "from": "wheel", "to": "LeftStickX" } ]{{modes}} }
        """, T.Devices);

    [Fact]
    public void A_profiles_curve_replaces_the_one_in_settings_and_leaves_the_wheels_calibration_alone()
    {
        ForceSettings f = ForceMath.Effective(App, Load(Own).Profile!.Force);

        Assert.Equal((CentringCurve.StyleCurve, 45, 16, 80, 70, 10), (f.CentringStyle, f.SpringPercent, f.CentreDegrees, f.PeakDegrees, f.LighteningPercent, f.DampingPercent));
        Assert.Equal((8, true, "wheel-1"), (f.FloorPercent, f.CentringReversed, f.CentringWheel));
    }

    [Fact]
    public void A_profile_that_says_nothing_about_centring_follows_settings()
    {
        ForceSettings f = ForceMath.Effective(App, Load("\"rumble\": 40").Profile!.Force);

        Assert.Equal((CentringCurve.StyleCurve, 12, 60), (f.CentringStyle, f.CentreDegrees, f.PeakDegrees));
    }

    [Fact]
    public void A_profile_can_ask_for_the_straight_spring_when_settings_uses_the_curve()
    {
        Assert.Equal(CentringCurve.StyleSpring, ForceMath.Effective(App, Load("\"centringStyle\": \"spring\"").Profile!.Force).CentringStyle);
    }

    [Fact]
    public void The_centring_survives_saving_and_reading_back()
    {
        Profile p = Load(Own, ", \"modes\": [ { \"name\": \"Foot\", \"force\": { \"centringStyle\": \"spring\", \"spring\": 10 }, \"rules\": [] } ]").Profile!;

        Profile again = ProfileLoader.Parse(ProfileWriter.ToJson(p), T.Devices).Profile!;

        Assert.Equal(("curve", 16, 80, 70, 10), (again.Force!.CentringStyle, again.Force.CentreDegrees!.Value, again.Force.PeakDegrees!.Value, again.Force.LighteningPercent!.Value, again.Force.DampingPercent!.Value));
        Assert.Equal("spring", again.Modes[0].Force!.CentringStyle);
    }

    [Fact]
    public void A_mode_changes_only_what_it_sets_and_keeps_only_that()
    {
        Profile p = Load(Own, ", \"modes\": [ { \"name\": \"Foot\", \"force\": { \"centringStyle\": \"spring\" }, \"rules\": [] } ]").Profile!;

        Profile running = ProfileModes.Resolve(p, 0);
        Assert.Equal(("spring", 16, 80), (running.Force!.CentringStyle, running.Force.CentreDegrees!.Value, running.Force.PeakDegrees!.Value));

        running.Force.PeakDegrees = 100;
        ProfileModes.Capture(p, 0, running);
        Assert.Equal("spring", p.Modes[0].Force!.CentringStyle);
        Assert.Equal(100, p.Modes[0].Force!.PeakDegrees);
        Assert.Null(p.Modes[0].Force!.CentreDegrees);
    }

    [Theory]
    [InlineData("\"centringStyle\": \"wobble\"", "centringStyle")]
    [InlineData("\"centreDegrees\": 0", "centreDegrees")]
    [InlineData("\"peakDegrees\": 500", "peakDegrees")]
    [InlineData("\"lighteningPercent\": 101", "lighteningPercent")]
    [InlineData("\"dampingPercent\": -1", "dampingPercent")]
    public void Values_the_sliders_could_not_give_are_rejected(string force, string named)
    {
        Assert.Contains(Load(force).Errors, e => e.Contains(named));
    }

    [Fact]
    public void Choosing_a_curve_writes_its_whole_shape()
    {
        var force = new ProfileForce();

        ProfileCentring.Write(force, CentringCurve.StyleCurve, new CentringPreset("x", 45, 16, 80, 70, 10), null);

        Assert.Equal(("curve", 16, 80, 70, 10), (force.CentringStyle, force.CentreDegrees!.Value, force.PeakDegrees!.Value, force.LighteningPercent!.Value, force.DampingPercent!.Value));
    }

    [Fact]
    public void Going_back_to_settings_drops_the_shape_but_an_untouched_hand_written_value_stays()
    {
        var curve = new CentringPreset("x", 45, 16, 80, 70, 10);
        var dropped = new ProfileForce();
        ProfileCentring.Write(dropped, null, curve, new ProfileForce { CentringStyle = "curve", PeakDegrees = 80 });
        Assert.Null(dropped.PeakDegrees);

        var kept = new ProfileForce();
        ProfileCentring.Write(kept, null, curve, new ProfileForce { PeakDegrees = 80 });
        Assert.Equal(80, kept.PeakDegrees);
    }

    [Fact]
    public void The_editor_starts_a_curve_from_settings_and_recognises_a_preset()
    {
        CentringPreset truck = CentringCurve.Presets.Single(p => p.Name == "Heavy truck");

        Assert.Equal((30, 12, 60), (ProfileCentring.Shape(null, App).Strength, ProfileCentring.Shape(null, App).CentreDegrees, ProfileCentring.Shape(null, App).PeakDegrees));
        Assert.Equal(truck.Name, CentringCurve.Presets[ProfileCentring.Matching(CentringCurve.Presets, truck with { Name = "Custom" })].Name);
        Assert.Equal(-1, ProfileCentring.Matching(CentringCurve.Presets, truck with { PeakDegrees = 85 }));
    }
}

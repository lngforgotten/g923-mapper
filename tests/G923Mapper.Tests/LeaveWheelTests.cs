using G923Mapper;

namespace G923Mapper.Tests;

public class LeaveWheelTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();

    [Fact]
    public void It_is_off_by_default_and_a_profile_can_turn_it_on()
    {
        Assert.False(ProfileLoader.Parse("""{ "name": "t", "rules": [] }""", Devices).Profile!.LeaveWheel);
        Assert.True(ProfileLoader.Parse("""{ "name": "t", "leaveWheel": true, "rules": [] }""", Devices).Profile!.LeaveWheel);
    }

    [Fact]
    public void It_survives_saving_and_is_not_written_when_off()
    {
        var on = ProfileLoader.Parse("""{ "name": "t", "match": { "exe": ["acs.exe"] }, "leaveWheel": true, "rules": [] }""", Devices).Profile!;
        string json = ProfileWriter.ToJson(on);
        Assert.Contains("\"leaveWheel\": true", json);
        Assert.True(ProfileLoader.Parse(json, Devices).Profile!.LeaveWheel);

        on.LeaveWheel = false;
        Assert.DoesNotContain("leaveWheel", ProfileWriter.ToJson(on));
    }

    [Fact]
    public void It_is_kept_in_a_mode_resolved_copy_of_the_profile()
    {
        var p = ProfileLoader.Parse("""{ "name": "t", "leaveWheel": true, "rules": [], "modes": [ { "name": "A", "rules": [] } ] }""", Devices).Profile!;
        Assert.True(ProfileModes.Resolve(p, 0).LeaveWheel);
    }

    [Fact]
    public void A_profile_based_on_a_template_can_differ_in_it_and_reset_it()
    {
        var template = ProfileLoader.Parse(StarterTemplates.All.First().Json, Devices).Profile!;
        var child = ProfileLoader.Parse(ProfileWriter.ToJson(Templates.CreateChild(template, "Game", ["game.exe"])), Devices).Profile!;
        Assert.False(child.LeaveWheel);
        child.LeaveWheel = true;
        Assert.Contains(Templates.Diff(child), d => d.Key == "leaveWheel");

        var back = ProfileLoader.Parse(ProfileWriter.ToJson(child), Devices).Profile!;
        Assert.True(back.LeaveWheel);
        Assert.Contains(Templates.Diff(back), d => d.Key == "leaveWheel");

        Templates.Reset(back, Templates.Diff(back).Single(d => d.Key == "leaveWheel"));
        Assert.False(back.LeaveWheel);
    }
}

public class KeepKeysTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();

    [Fact]
    public void Keys_only_is_off_by_default_and_a_profile_can_turn_it_on_alongside_leaving_the_wheel()
    {
        Assert.False(ProfileLoader.Parse("""{ "name": "t", "rules": [] }""", Devices).Profile!.KeepKeys);
        var p = ProfileLoader.Parse("""{ "name": "t", "leaveWheel": true, "keepKeys": true, "rules": [] }""", Devices).Profile!;
        Assert.True(p.LeaveWheel && p.KeepKeys);
    }

    [Fact]
    public void It_survives_saving_but_is_only_written_with_leaveWheel()
    {
        var p = ProfileLoader.Parse("""{ "name": "t", "leaveWheel": true, "keepKeys": true, "rules": [] }""", Devices).Profile!;
        string json = ProfileWriter.ToJson(p);
        Assert.Contains("\"keepKeys\": true", json);
        Assert.True(ProfileLoader.Parse(json, Devices).Profile!.KeepKeys);

        p.LeaveWheel = false;                                           // not leaving the wheel: the keys tick means nothing
        Assert.DoesNotContain("keepKeys", ProfileWriter.ToJson(p));
    }

    [Fact]
    public void A_profile_based_on_a_template_can_differ_in_it_and_reset_it()
    {
        var template = ProfileLoader.Parse(StarterTemplates.All.First().Json, Devices).Profile!;
        var child = ProfileLoader.Parse(ProfileWriter.ToJson(Templates.CreateChild(template, "Game", ["game.exe"])), Devices).Profile!;
        child.LeaveWheel = true; child.KeepKeys = true;
        var back = ProfileLoader.Parse(ProfileWriter.ToJson(child), Devices).Profile!;
        Assert.True(back.KeepKeys);
        Assert.Contains(Templates.Diff(back), d => d.Key == "keepKeys");
        Templates.Reset(back, Templates.Diff(back).Single(d => d.Key == "keepKeys"));
        Assert.False(back.KeepKeys);
    }

    [Fact]
    public void The_do_nothing_controller_accepts_everything_and_never_rumbles()
    {
        using var pad = new NullPad();
        int rumbles = 0;
        pad.Rumble += (_, _) => rumbles++;
        pad.Connect();
        pad.SetLeftStick(1, 2); pad.SetRightStick(3, 4); pad.SetTriggers(5, 6); pad.SetButton(PadButton.A, true); pad.Submit();
        Assert.Equal(0, rumbles);
    }
}

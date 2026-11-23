using G923Mapper;

namespace G923Mapper.Tests;

public class SetupChecklistTests
{
    private static SetupFacts Good() => new()
    {
        DriverInstalled = true, DriverRunning = true, WheelFound = true, WheelReporting = true, WheelName = "G923",
        WheelCalibrated = true, ProfileValid = true, ProfileName = "gta-v", HidHideInstalled = true, WheelHidden = true,
    };

    private static SetupStep Step(SetupFacts f, string id) => SetupChecklist.Evaluate(f).Single(s => s.Id == id);

    [Fact]
    public void When_everything_is_in_place_every_step_is_done_and_the_app_is_ready()
    {
        var steps = SetupChecklist.Evaluate(Good());
        Assert.All(steps.Where(s => s.Id != SetupChecklist.Test), s => Assert.Equal(SetupState.Done, s.State));
        Assert.True(SetupChecklist.IsReady(steps));
        Assert.StartsWith("All set", SetupChecklist.Summary(steps));
    }

    [Fact]
    public void A_missing_driver_comes_first_and_holds_up_the_wheel_step()
    {
        var f = new SetupFacts { DriverInstalled = false };
        var steps = SetupChecklist.Evaluate(f);
        Assert.Equal(SetupState.ToDo, steps[0].State);
        Assert.Equal(SetupAction.GetDriver, steps[0].Action);
        Assert.Equal(SetupState.Waiting, Step(f, SetupChecklist.Wheel).State);
        Assert.Equal(SetupState.Waiting, Step(f, SetupChecklist.Calibrate).State);
        Assert.False(SetupChecklist.IsReady(steps));
        Assert.Contains("Next: virtual controller driver", SetupChecklist.Summary(steps));
    }

    [Fact]
    public void An_installed_but_stopped_driver_says_so()
    {
        var s = Step(new SetupFacts { DriverInstalled = true, DriverRunning = false }, SetupChecklist.Driver);
        Assert.Equal(SetupState.ToDo, s.State);
        Assert.Contains("not running", s.Detail);
    }

    [Fact]
    public void With_the_driver_ok_a_missing_wheel_is_the_next_thing_to_do()
    {
        var f = new SetupFacts { DriverInstalled = true, DriverRunning = true };
        Assert.Equal(SetupState.ToDo, Step(f, SetupChecklist.Wheel).State);
        Assert.Equal(SetupState.Waiting, Step(f, SetupChecklist.Calibrate).State);
    }

    [Fact]
    public void A_connected_but_quiet_wheel_is_asked_to_move()
    {
        var f = new SetupFacts { DriverInstalled = true, DriverRunning = true, WheelFound = true, WheelName = "G923" };
        var s = Step(f, SetupChecklist.Wheel);
        Assert.Equal(SetupState.ToDo, s.State);
        Assert.Contains("Turn the wheel", s.Detail);
    }

    [Fact]
    public void An_uncalibrated_wheel_is_asked_to_calibrate_with_playstation_wording_when_it_is_one()
    {
        var xbox = new SetupFacts { DriverInstalled = true, DriverRunning = true, WheelFound = true, WheelReporting = true };
        var ps = new SetupFacts { DriverInstalled = true, DriverRunning = true, WheelFound = true, WheelReporting = true, PlayStationWheel = true };
        Assert.Equal(SetupState.ToDo, Step(xbox, SetupChecklist.Calibrate).State);
        Assert.Equal(SetupAction.Calibrate, Step(xbox, SetupChecklist.Calibrate).Action);
        Assert.DoesNotContain("PlayStation", Step(xbox, SetupChecklist.Calibrate).Detail);
        Assert.Contains("PlayStation", Step(ps, SetupChecklist.Calibrate).Detail);
    }

    [Fact]
    public void Hiding_the_wheel_is_optional_in_every_state()
    {
        var notInstalled = Step(new SetupFacts { HidHideInstalled = false }, SetupChecklist.Hide);
        Assert.Equal(SetupState.Optional, notInstalled.State);
        Assert.Equal(SetupAction.GetHidHide, notInstalled.Action);
        Assert.False(notInstalled.Required);

        Assert.Equal(SetupState.Optional, Step(new SetupFacts { HidHideInstalled = true, WheelHidden = false }, SetupChecklist.Hide).State);
        Assert.Equal(SetupState.Done, Step(new SetupFacts { HidHideInstalled = true, WheelHidden = true }, SetupChecklist.Hide).State);
    }

    [Fact]
    public void Only_the_driver_the_wheel_and_calibration_are_required()
    {
        var required = SetupChecklist.Evaluate(Good()).Where(s => s.Required).Select(s => s.Id).ToList();
        Assert.Equal([SetupChecklist.Driver, SetupChecklist.Wheel, SetupChecklist.Calibrate], required);
    }

    [Fact]
    public void An_optional_step_never_blocks_being_ready()
    {
        var f = new SetupFacts
        {
            DriverInstalled = true, DriverRunning = true, WheelFound = true, WheelReporting = true, WheelCalibrated = true,
            ProfileValid = false, HidHideInstalled = false,
        };
        Assert.True(SetupChecklist.IsReady(SetupChecklist.Evaluate(f)));
    }

    [Fact]
    public void The_summary_counts_the_essential_steps()
    {
        var f = new SetupFacts { DriverInstalled = true, DriverRunning = true, WheelFound = true, WheelReporting = true };
        Assert.Contains("2 of 3 essential steps done", SetupChecklist.Summary(SetupChecklist.Evaluate(f)));
    }
}

public class WheelVisibilityTests
{
    [Theory]
    [InlineData(true, true, false, false, true)]       // profile wants it hidden, it is visible: hide
    [InlineData(true, true, true, false, null)]       // already hidden
    [InlineData(false, true, true, false, false)]     // profile wants it visible, it is hidden: show
    [InlineData(false, true, false, false, null)]
    [InlineData(null, true, true, true, false)]       // a profile with no opinion after one that hid it: give it back
    [InlineData(null, true, true, false, null)]       // hidden by the user, not by a profile: leave it
    [InlineData(null, true, false, true, null)]
    [InlineData(true, false, false, false, null)]     // HidHide not installed: nothing can be done
    public void Decides_what_to_do(bool? wanted, bool installed, bool hidden, bool byProfile, bool? expected) =>
        Assert.Equal(expected, WheelVisibility.Decide(wanted, installed, hidden, byProfile));
}

public class WheelVisibilityTrackerTests
{
    [Fact]
    public void A_hiding_profile_hides_once_and_the_wheel_comes_back_when_a_plain_profile_takes_over()
    {
        var t = new WheelVisibilityTracker();
        Assert.True(t.OnProfileActive("game", true, true, currentlyHidden: false));
        t.Done(hid: true, success: true);
        Assert.True(t.HiddenByProfile);

        Assert.Null(t.OnProfileActive("game", true, true, currentlyHidden: true));      // same profile again: nothing
        Assert.False(t.OnProfileActive("default", null, true, currentlyHidden: true));  // game over: give it back
        t.Done(hid: false, success: true);
        Assert.False(t.HiddenByProfile);
    }

    [Fact]
    public void A_declined_prompt_is_not_asked_again_for_the_same_profile()
    {
        var t = new WheelVisibilityTracker();
        Assert.True(t.OnProfileActive("game", true, true, currentlyHidden: false));
        t.Done(hid: true, success: false);                                              // permission declined
        Assert.False(t.HiddenByProfile);
        Assert.Null(t.OnProfileActive("game", true, true, currentlyHidden: false));     // not asked again
        Assert.True(t.OnProfileActive("other", true, true, currentlyHidden: false));    // a different profile may ask
    }

    [Fact]
    public void A_wheel_the_user_hid_themselves_is_never_unhidden_by_a_profile()
    {
        var t = new WheelVisibilityTracker();
        Assert.Null(t.OnProfileActive("default", null, true, currentlyHidden: true));
        Assert.False(t.HiddenByProfile);
    }

    [Fact]
    public void The_profile_setting_survives_saving()
    {
        var devices = DeviceRegistry.CreateDefault();
        var p = ProfileLoader.Parse("""{ "name": "t", "hideWheel": true, "rules": [] }""", devices).Profile!;
        Assert.True(p.HideWheel);
        string json = ProfileWriter.ToJson(p);
        Assert.Contains("\"hideWheel\": true", json);
        Assert.True(ProfileLoader.Parse(json, devices).Profile!.HideWheel);

        var no = ProfileLoader.Parse("""{ "name": "t", "rules": [] }""", devices).Profile!;
        Assert.Null(no.HideWheel);
        Assert.DoesNotContain("hideWheel", ProfileWriter.ToJson(no));
        Assert.Contains("\"hideWheel\": false", ProfileWriter.ToJson(ProfileLoader.Parse("""{ "name": "t", "hideWheel": false, "rules": [] }""", devices).Profile!));
    }
}

public class WheelVisibilityGameTests
{
    [Theory]
    [InlineData(true, true, true)]      // the game was detected: the profile's wish counts
    [InlineData(false, true, false)]
    [InlineData(null, true, null)]
    [InlineData(true, false, null)]     // chosen by hand or remembered from last time: no say
    [InlineData(false, false, null)]
    public void A_profile_only_decides_when_its_game_was_detected(bool? setting, bool detected, bool? expected) =>
        Assert.Equal(expected, WheelVisibility.WantedFor(setting, detected));

    [Fact]
    public void Opening_the_app_with_a_hiding_profile_remembered_does_nothing_and_the_game_starting_hides_then_ending_restores()
    {
        var t = new WheelVisibilityTracker();

        // Start-up: the remembered profile wants the wheel hidden, but its game is not running.
        Assert.Null(t.OnProfileActive("wardogs|-", WheelVisibility.WantedFor(true, gameDetected: false), true, currentlyHidden: false));

        // The game starts and the profile switches on for it.
        Assert.True(t.OnProfileActive("wardogs|True", WheelVisibility.WantedFor(true, gameDetected: true), true, currentlyHidden: false));
        t.Done(hid: true, success: true);

        // The game closes; the profile falls back to the user's own choice, which has no say, so the wheel comes back.
        Assert.False(t.OnProfileActive("default|-", WheelVisibility.WantedFor(null, gameDetected: false), true, currentlyHidden: true));
    }
}

using G923Mapper;

namespace G923Mapper.Tests;

public class HiddenWheelGameTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();

    private static Profile Game(params string[] exes) =>
        ProfileLoader.Parse($$"""{ "name": "g", "match": { "exe": [{{string.Join(",", exes.Select(e => $"\"{e}\""))}}] }, "rules": [] }""", Devices).Profile!;

    [Fact]
    public void A_game_counts_as_running_whether_or_not_it_is_in_front_and_names_compare_loosely()
    {
        var p = Game("WardogsClient-Win64-Shipping.exe");
        Assert.True(WheelVisibility.GameRunning(p, ["chrome.exe", "wardogsclient-win64-shipping.EXE"]));
        Assert.True(WheelVisibility.GameRunning(p, ["WardogsClient-Win64-Shipping"]));
        Assert.False(WheelVisibility.GameRunning(p, ["chrome.exe", "explorer.exe"]));
    }

    [Fact]
    public void A_profile_with_no_game_or_no_profile_is_never_running()
    {
        Assert.False(WheelVisibility.GameRunning(Game(), ["a.exe"]));
        Assert.False(WheelVisibility.GameRunning(null, ["a.exe"]));
    }

    [Fact]
    public void Any_one_of_several_games_is_enough()
    {
        var p = Game("a.exe", "b.exe");
        Assert.True(WheelVisibility.GameRunning(p, ["b.exe"]));
    }

    [Fact]
    public void A_game_that_only_lost_the_focus_keeps_the_wheel_hidden_so_there_is_no_prompt()
    {
        var t = new WheelVisibilityTracker();
        t.Done(hid: true, success: true, key: "wardogs");
        Assert.Equal("wardogs", t.HiddenFor);
        Assert.True(t.KeepHidden(wheelHidden: true, gameStillRunning: true));
        Assert.False(t.ShouldRestore(wheelHidden: true, gameStillRunning: true));
    }

    [Fact]
    public void Once_the_game_has_closed_the_wheel_is_given_back_and_only_once()
    {
        var t = new WheelVisibilityTracker();
        t.Done(hid: true, success: true, key: "wardogs");
        Assert.True(t.ShouldRestore(wheelHidden: true, gameStillRunning: false));
        t.MarkRestoreTried();
        Assert.False(t.ShouldRestore(wheelHidden: true, gameStillRunning: false));   // a declined prompt is not repeated every few seconds
        t.Done(hid: false, success: true);
        Assert.Null(t.HiddenFor);
        Assert.False(t.HiddenByProfile);
    }

    [Fact]
    public void Hiding_again_for_a_later_game_allows_a_new_give_back()
    {
        var t = new WheelVisibilityTracker();
        t.Done(hid: true, success: true, key: "a");
        t.MarkRestoreTried();
        t.Done(hid: false, success: true);
        t.Done(hid: true, success: true, key: "b");
        Assert.True(t.ShouldRestore(wheelHidden: true, gameStillRunning: false));
    }

    [Fact]
    public void Nothing_is_given_back_that_this_feature_did_not_hide_or_that_is_already_visible()
    {
        var t = new WheelVisibilityTracker();
        Assert.False(t.ShouldRestore(wheelHidden: true, gameStillRunning: false));   // the user hid it themselves
        t.Done(hid: true, success: true, key: "a");
        Assert.False(t.ShouldRestore(wheelHidden: false, gameStillRunning: false));
        Assert.False(t.KeepHidden(wheelHidden: false, gameStillRunning: true));
    }

    [Fact]
    public void A_failed_hide_records_nothing()
    {
        var t = new WheelVisibilityTracker();
        t.Done(hid: true, success: false, key: "a");
        Assert.False(t.HiddenByProfile);
        Assert.Null(t.HiddenFor);
    }
}

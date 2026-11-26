using G923Mapper;

namespace G923Mapper.Tests;

public class RuleSettingsTests
{
    [Theory]
    [InlineData("LeftStickX", false)]
    [InlineData("RightTrigger", false)]
    [InlineData("P2:LeftTrigger", false)]
    [InlineData("", false)]
    [InlineData("Button:A", true)]
    [InlineData("P2:Button:A", true)]
    [InlineData("Key:Q", true)]
    [InlineData("Mouse:Left", true)]
    [InlineData("Mode:Next", true)]
    [InlineData("Button:Nonsense", true)]   // misspelt: still meant as a button
    [InlineData("P2:Key:Q", true)]          // not allowed, but still a key
    public void Press_outputs_are_told_apart_from_sticks_and_triggers(string to, bool press) =>
        Assert.Equal(press, RuleSettings.IsPress(to));

    [Theory]
    [InlineData("Button:A", true)]
    [InlineData("P2:Button:B", true)]
    [InlineData("Key:Q", false)]
    [InlineData("Mode:Next", false)]
    [InlineData("LeftStickX", false)]
    public void Turbo_is_for_controller_buttons_on_either_controller(string to, bool button) =>
        Assert.Equal(button, RuleSettings.IsPadButton(to));

    [Fact]
    public void An_untouched_rule_has_nothing_hidden_that_needs_putting_right()
    {
        var r = new Rule { From = "throttle", To = "Button:A", Curve = "expo", Deadzone = 0.04, AntiDeadzone = 0.1, Threshold = -0.5 };
        Assert.False(RuleSettings.ResponseNeedsAttention(r));
        Assert.False(RuleSettings.PressNeedsAttention(r));
    }

    [Fact]
    public void Response_settings_that_are_complained_about_stay_reachable()
    {
        Assert.True(RuleSettings.ResponseNeedsAttention(new Rule { Smooth = 20 }));
        Assert.True(RuleSettings.ResponseNeedsAttention(new Rule { Deadzone = 1.2 }));
        Assert.True(RuleSettings.ResponseNeedsAttention(new Rule { Exponent = 0 }));
        Assert.True(RuleSettings.ResponseNeedsAttention(new Rule { Curve = "custom" }));
        Assert.False(RuleSettings.ResponseNeedsAttention(new Rule { Curve = "custom", Points = [[0, 0], [1, 1]] }));
    }

    [Fact]
    public void Press_settings_that_are_complained_about_stay_reachable()
    {
        Assert.True(RuleSettings.PressNeedsAttention(new Rule { Mode = "toggle" }));
        Assert.True(RuleSettings.PressNeedsAttention(new Rule { Turbo = true }));
        Assert.True(RuleSettings.PressNeedsAttention(new Rule { Threshold = 0 }));
        Assert.True(RuleSettings.PressNeedsAttention(new Rule { PulseMs = 0 }));
        Assert.True(RuleSettings.PressNeedsAttention(new Rule { TurboHz = 99 }));
    }
}

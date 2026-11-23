using G923Mapper;

namespace G923Mapper.Tests;

public class ComboDetectorTests
{
    private static readonly string[] Both = ["paddleL", "paddleR"];

    [Fact]
    public void Fires_once_after_the_buttons_have_been_held_for_the_hold_time()
    {
        var d = new ComboDetector(Both, 800);
        Assert.False(d.Update(["paddleL", "paddleR"], 1000));
        Assert.False(d.Update(["paddleL", "paddleR"], 1500));
        Assert.True(d.Update(["paddleL", "paddleR"], 1800));
        Assert.False(d.Update(["paddleL", "paddleR"], 2600));   // still held: does not repeat
    }

    [Fact]
    public void Letting_go_re_arms_it()
    {
        var d = new ComboDetector(Both, 100);
        d.Update(Both, 0);
        Assert.True(d.Update(Both, 200));
        d.Update([], 300);
        d.Update(Both, 400);
        Assert.True(d.Update(Both, 600));
    }

    [Fact]
    public void Releasing_one_button_part_way_starts_the_count_again()
    {
        var d = new ComboDetector(Both, 800);
        d.Update(Both, 0);
        d.Update(["paddleL"], 500);
        d.Update(Both, 600);
        Assert.False(d.Update(Both, 1300));     // only 700 ms since the restart
        Assert.True(d.Update(Both, 1400));
    }

    [Fact]
    public void Only_one_of_the_two_never_fires()
    {
        var d = new ComboDetector(Both, 0);
        Assert.False(d.Update(["paddleL"], 0));
        Assert.False(d.Update(["paddleL"], 5000));
    }

    [Fact]
    public void Extra_buttons_held_as_well_do_not_matter_and_names_are_case_insensitive()
    {
        var d = new ComboDetector(["PADDLEL", "paddleR"], 0);
        Assert.True(d.Update(["buttonA", "paddleL", "paddleR", "dpadUp"], 0));
    }

    [Fact]
    public void An_unset_combination_never_fires()
    {
        var d = new ComboDetector([], 0);
        Assert.True(d.IsEmpty);
        Assert.False(d.Update(Both, 10_000));
    }

    [Fact]
    public void A_zero_hold_fires_at_once()
    {
        Assert.True(new ComboDetector(["paddleL"], 0).Update(["paddleL"], 5));
    }
}

public class ComboCaptureTests
{
    [Fact]
    public void Takes_the_largest_set_held_together_and_finishes_when_everything_is_released()
    {
        var c = new ComboCapture();
        c.Add([]);
        Assert.False(c.Finished);
        c.Add(["paddleR"]);
        c.Add(["paddleR", "paddleL"]);
        c.Add(["paddleL"]);                 // letting go gradually does not shrink the answer
        Assert.False(c.Finished);
        c.Add([]);
        Assert.True(c.Finished);
        Assert.Equal(["paddleL", "paddleR"], c.Best);
    }

    [Fact]
    public void Nothing_is_captured_until_something_is_pressed()
    {
        var c = new ComboCapture();
        c.Add([]); c.Add([]);
        Assert.False(c.Finished);
        Assert.Empty(c.Best);
    }

    [Fact]
    public void After_it_finishes_later_presses_are_ignored()
    {
        var c = new ComboCapture();
        c.Add(["a"]); c.Add([]);
        c.Add(["x", "y", "z"]);
        Assert.Equal(["a"], c.Best);
    }
}

public class ButtonNamesTests
{
    private static (DeviceMap Device, InputFrame Frame) Setup(params string[] down)
    {
        var devices = DeviceRegistry.CreateDefault();
        devices.TryGet("g923_xbox", out var d);
        var frame = new InputFrame();
        foreach (string n in down) frame.Values[n] = 1;
        return (d, frame);
    }

    [Fact]
    public void Named_buttons_are_reported_by_name_and_not_a_second_time_by_number()
    {
        var (device, frame) = Setup();
        // paddleR is button N on this device: both spellings are present in a real frame.
        int n = device.Buttons["paddleR"];
        frame.Values["paddleR"] = 1;
        frame.Values["button" + n] = 1;
        var down = ButtonNames.Down(frame, device);
        Assert.Equal(["paddleR"], down);
    }

    [Fact]
    public void Unnamed_buttons_use_their_number_and_the_dpad_is_included()
    {
        var (device, frame) = Setup("button22", "dpadLeft");
        Assert.Contains("button22", ButtonNames.Down(frame, device));
        Assert.Contains("dpadLeft", ButtonNames.Down(frame, device));
    }

    [Fact]
    public void Axes_and_released_buttons_are_never_reported()
    {
        var (device, frame) = Setup();
        frame.Values["throttle"] = 1;
        frame.Values["wheel"] = 0.9;
        frame.Values["paddleL"] = 0;
        Assert.Empty(ButtonNames.Down(frame, device));
    }
}

public class ComboTextTests
{
    [Fact]
    public void Formats_and_parses_a_combination()
    {
        Assert.Equal("paddleL + paddleR", ComboText.Format(["paddleL", "paddleR"]));
        Assert.Equal("not set", ComboText.Format([]));
        Assert.Equal(["paddleL", "paddleR"], ComboText.Parse("paddleL + paddleR"));
        Assert.Equal(["a", "b"], ComboText.Parse("a, b"));
        Assert.Empty(ComboText.Parse("not set"));
        Assert.Empty(ComboText.Parse(null));
    }
}

public class DpadPresetTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();

    private static PadState Play(Preset preset, params (string, double)[] values)
    {
        var p = ProfileLoader.Parse(preset.Json, Devices).Profile!;
        Devices.TryGet("g923_xbox", out var d);
        var frame = new InputFrame();
        foreach (var (k, v) in values) frame.Values[k] = v;
        return new MappingEngine(p, d, Devices).Evaluate(frame, 0);
    }

    [Fact]
    public void The_menu_preset_turns_the_wheel_and_pedals_into_dpad_presses()
    {
        var menus = Presets.All.Single(p => p.Id == "menus");
        Assert.True(ProfileLoader.Parse(menus.Json, Devices).Ok);

        Assert.True(Play(menus, ("wheel", -0.8))[PadButton.DPadLeft]);
        Assert.False(Play(menus, ("wheel", -0.8))[PadButton.DPadRight]);
        Assert.True(Play(menus, ("wheel", 0.8))[PadButton.DPadRight]);
        Assert.False(Play(menus, ("wheel", 0.2))[PadButton.DPadRight]);      // not turned far enough
        Assert.False(Play(menus, ("wheel", -0.2))[PadButton.DPadLeft]);
        Assert.True(Play(menus, ("throttle", 0.9))[PadButton.DPadUp]);
        Assert.True(Play(menus, ("brake", 0.9))[PadButton.DPadDown]);
        Assert.True(Play(menus, ("paddleR", 1))[PadButton.A]);
        Assert.True(Play(menus, ("paddleL", 1))[PadButton.B]);
    }

    [Fact]
    public void Presets_have_unique_ids_and_all_load()
    {
        Assert.Equal(Presets.All.Count, Presets.All.Select(p => p.Id).Distinct().Count());
        foreach (var preset in Presets.All) Assert.True(ProfileLoader.Parse(preset.Json, Devices).Ok, preset.Id);
    }
}

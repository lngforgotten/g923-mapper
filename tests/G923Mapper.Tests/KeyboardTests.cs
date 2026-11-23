using G923Mapper;

namespace G923Mapper.Tests;

public class KeyNamesTests
{
    [Theory]
    [InlineData("A", 0x41, false)]
    [InlineData("z", 0x5A, false)]
    [InlineData("0", 0x30, false)]
    [InlineData("F1", 0x70, false)]
    [InlineData("f12", 0x7B, false)]
    [InlineData("Space", 0x20, false)]
    [InlineData("Enter", 0x0D, false)]
    [InlineData("Return", 0x0D, false)]
    [InlineData("Esc", 0x1B, false)]
    [InlineData("Escape", 0x1B, false)]
    [InlineData("Shift", 0x10, false)]
    [InlineData("Ctrl", 0x11, false)]
    [InlineData("Up", 0x26, true)]
    [InlineData("RCtrl", 0xA3, true)]
    [InlineData("Insert", 0x2D, true)]
    [InlineData("Numpad5", 0x65, false)]
    public void Known_keys_map_to_virtual_key_codes(string name, int vk, bool extended)
    {
        Assert.True(KeyNames.TryGet(name, out var code));
        Assert.Equal((ushort)vk, code.Vk);
        Assert.Equal(extended, code.Extended);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Banana")]
    [InlineData("F25")]
    [InlineData("AA")]
    public void Unknown_keys_are_rejected(string name) => Assert.False(KeyNames.TryGet(name, out _));

    [Fact]
    public void Canonical_names_make_different_spellings_one_key()
    {
        Assert.Equal(KeyNames.Canonical("q"), KeyNames.Canonical("Q"));
        Assert.Null(KeyNames.Canonical("nope"));
    }

    [Fact]
    public void Every_name_in_the_editor_list_is_valid()
        => Assert.All(KeyNames.Common, n => Assert.True(KeyNames.TryGet(n, out _), n));

    [Fact]
    public void All_letters_digits_and_function_keys_exist()
    {
        for (char c = 'A'; c <= 'Z'; c++) Assert.True(KeyNames.TryGet(c.ToString(), out _));
        for (char c = '0'; c <= '9'; c++) Assert.True(KeyNames.TryGet(c.ToString(), out _));
        for (int i = 1; i <= 12; i++) Assert.True(KeyNames.TryGet("F" + i, out _));
    }
}

public class KeyStateTrackerTests
{
    [Fact]
    public void New_keys_are_pressed_once_and_gone_keys_released()
    {
        var t = new KeyStateTracker();
        var (press, release) = t.Update(["Q"]);
        Assert.Equal(["Q"], press); Assert.Empty(release);

        (press, release) = t.Update(["Q"]);                      // still held: nothing to send, no key repeat from us
        Assert.Empty(press); Assert.Empty(release);

        (press, release) = t.Update(["Q", "E"]);
        Assert.Equal(["E"], press); Assert.Empty(release);

        (press, release) = t.Update(["E"]);
        Assert.Empty(press); Assert.Equal(["Q"], release);

        (press, release) = t.Update([]);
        Assert.Empty(press); Assert.Equal(["E"], release);
    }

    [Fact]
    public void Spelling_case_does_not_cause_a_release_and_re_press()
    {
        var t = new KeyStateTracker();
        t.Update(["Q"]);
        var (press, release) = t.Update(["q"]);
        Assert.Empty(press); Assert.Empty(release);
    }

    [Fact]
    public void ReleaseAll_lets_go_of_everything_and_forgets_it()
    {
        var t = new KeyStateTracker();
        t.Update(["Q", "Shift", "Up"]);
        Assert.Equal(3, t.ReleaseAll().Count);
        Assert.Empty(t.Down);
        Assert.Empty(t.ReleaseAll());          // idempotent
        var (press, _) = t.Update(["Q"]);      // and a later press works again
        Assert.Equal(["Q"], press);
    }
}

public class KeyboardOutputTests
{
    private static MappingEngine Engine(bool keyboard, params string[] rules)
    {
        string json = $$"""{ "name": "t", "device": "g923_xbox", "outputs": { "keyboard": {{(keyboard ? "true" : "false")}} }, "rules": [ {{string.Join(",", rules)}} ] }""";
        var r = ProfileLoader.Parse(json, T.Devices);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        return new MappingEngine(r.Profile!, DeviceRegistry.G923Xbox());
    }

    [Fact]
    public void A_key_rule_holds_the_key_while_the_input_is_held()
    {
        var e = Engine(true, """{ "from": "paddleR", "to": "Key:E" }""");
        Assert.Contains("E", e.Evaluate(T.Frame(("paddleR", 1)), 0).Keys);
        Assert.Empty(e.Evaluate(T.Frame(("paddleR", 0)), 0).Keys);
    }

    [Fact]
    public void Without_keyboard_output_key_rules_do_nothing_at_all()
    {
        var e = Engine(false, """{ "from": "paddleR", "to": "Key:E" }""");
        Assert.Empty(e.Evaluate(T.Frame(("paddleR", 1)), 0).Keys);
    }

    [Fact]
    public void An_axis_can_press_a_key_through_a_threshold_like_any_button()
    {
        var e = Engine(true, """{ "from": "clutch", "to": "Key:Space", "threshold": 0.4 }""");
        Assert.Empty(e.Evaluate(T.Frame(("clutch", 0.3)), 0).Keys);
        Assert.Contains("Space", e.Evaluate(T.Frame(("clutch", 0.5)), 0).Keys);
    }

    [Fact]
    public void Key_rules_support_toggle_and_pulse_modes()
    {
        var toggle = Engine(true, """{ "from": "paddleL", "to": "Key:F", "mode": "toggle" }""");
        Assert.Contains("F", toggle.Evaluate(T.Frame(("paddleL", 1)), 0).Keys);
        Assert.Contains("F", toggle.Evaluate(T.Frame(("paddleL", 0)), 0).Keys);      // stays on after release
        Assert.Empty(toggle.Evaluate(T.Frame(("paddleL", 1)), 0).Keys);               // second press turns it off

        var pulse = Engine(true, """{ "from": "paddleL", "to": "Key:F", "mode": "pulse", "pulseMs": 50 }""");
        Assert.Contains("F", pulse.Evaluate(T.Frame(("paddleL", 1)), 1000).Keys);
        Assert.Empty(pulse.Evaluate(T.Frame(("paddleL", 1)), 1060).Keys);             // one short press, not auto-repeat
    }

    [Fact]
    public void The_same_key_from_two_inputs_is_one_held_key()
    {
        var e = Engine(true, """{ "from": "paddleL", "to": "Key:Q" }""", """{ "from": "paddleR", "to": "Key:q" }""");
        var keys = e.Evaluate(T.Frame(("paddleL", 1), ("paddleR", 1)), 0).Keys;
        Assert.Single(keys);
    }

    [Fact]
    public void Pad_and_keyboard_outputs_work_together()
    {
        var e = Engine(true, """{ "from": "paddleR", "to": "Key:E" }""", """{ "from": "paddleR", "to": "Button:RightShoulder" }""");
        var s = e.Evaluate(T.Frame(("paddleR", 1)), 0);
        Assert.Contains("E", s.Keys);
        Assert.True(s[PadButton.RightShoulder]);
    }

    [Fact]
    public void Unknown_key_names_are_a_validation_error()
    {
        var r = ProfileLoader.Parse("""{ "name": "t", "outputs": { "keyboard": true }, "rules": [ { "from": "paddleR", "to": "Key:Banana" } ] }""", T.Devices);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Contains("unknown key"));
    }

    [Fact]
    public void A_key_rule_warns_only_when_keyboard_output_is_off()
    {
        var off = ProfileLoader.Parse("""{ "name": "t", "rules": [ { "from": "paddleR", "to": "Key:E" } ] }""", T.Devices);
        Assert.Contains(off.Warnings, w => w.Contains("disabled"));
        var on = ProfileLoader.Parse("""{ "name": "t", "outputs": { "keyboard": true }, "rules": [ { "from": "paddleR", "to": "Key:E" } ] }""", T.Devices);
        Assert.Empty(on.Warnings);
    }

    [Fact]
    public void Starter_profiles_and_presets_never_enable_keyboard_output()
    {
        Assert.False(ProfileLoader.Parse(DefaultProfiles.GtaV, T.Devices).Profile!.Outputs.Keyboard);
        Assert.All(Presets.All, p => Assert.False(ProfileLoader.Parse(p.Json, T.Devices).Profile!.Outputs.Keyboard));
    }

    [Fact]
    public void Key_rules_survive_saving_and_loading_with_keyboard_on()
    {
        var r = ProfileLoader.Parse("""{ "name": "t", "outputs": { "keyboard": true }, "rules": [ { "from": "paddleR", "to": "Key:E" } ] }""", T.Devices);
        var again = ProfileLoader.Parse(ProfileWriter.ToJson(r.Profile!), T.Devices);
        Assert.True(again.Ok);
        Assert.True(again.Profile!.Outputs.Keyboard);
        Assert.Equal("Key:E", again.Profile.Rules[0].To);
    }
}

public class SendInputKeySinkTests
{
    [Fact]
    public void The_native_INPUT_struct_is_the_size_Windows_expects()
    {
        // SendInput silently does nothing if cbSize is wrong. 40 bytes on x64, 28 on x86.
        var input = typeof(SendInputKeySink).GetNestedType("INPUT", System.Reflection.BindingFlags.NonPublic)!;
        Assert.Equal(Environment.Is64BitProcess ? 40 : 28, System.Runtime.InteropServices.Marshal.SizeOf(input));
    }

    [Fact]
    public void Releasing_or_disposing_with_nothing_held_sends_nothing_and_does_not_throw()
    {
        var sink = new SendInputKeySink();
        sink.ReleaseAll();
        sink.Update([]);
        sink.Dispose();
    }
}

public class FullKeyboardAndMouseTests
{
    [Theory]
    [InlineData("F13", 0x7C, false)]
    [InlineData("F24", 0x87, false)]
    [InlineData("Semicolon", 0xBA, false)]
    [InlineData("Backtick", 0xC0, false)]
    [InlineData("LeftBracket", 0xDB, false)]
    [InlineData("NumpadAdd", 0x6B, false)]
    [InlineData("NumpadEnter", 0x0D, true)]
    [InlineData("NumpadDivide", 0x6F, true)]
    [InlineData("PrintScreen", 0x2C, true)]
    [InlineData("LWin", 0x5B, true)]
    [InlineData("Menu", 0x5D, true)]
    [InlineData("Apps", 0x5D, true)]
    [InlineData("VolumeMute", 0xAD, true)]
    public void More_keys_are_known(string name, int vk, bool extended)
    {
        Assert.True(KeyNames.TryGet(name, out var code), name);
        Assert.Equal((ushort)vk, code.Vk);
        Assert.Equal(extended, code.Extended);
    }

    [Fact]
    public void Every_name_in_the_full_list_is_valid_and_there_are_no_duplicates()
    {
        Assert.All(KeyNames.AllKeyNames, n => Assert.True(KeyNames.TryGet(n, out _), n));
        Assert.Equal(KeyNames.AllKeyNames.Count, KeyNames.AllKeyNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("Q", KeyNames.AllKeyNames);
        Assert.Contains("Space", KeyNames.AllKeyNames);
        Assert.DoesNotContain("Esc", KeyNames.AllKeyNames);   // aliases are accepted but not listed twice
    }

    [Theory]
    [InlineData("Mouse:Left", "Mouse:Left")]
    [InlineData("mouse:right", "Mouse:Right")]
    [InlineData("MOUSE:MIDDLE", "Mouse:Middle")]
    [InlineData("Mouse:x1", "Mouse:X1")]
    [InlineData("Mouse:X2", "Mouse:X2")]
    public void Mouse_buttons_are_known_and_have_one_spelling(string text, string canonical)
    {
        Assert.True(KeyNames.IsMouse(text));
        Assert.True(KeyNames.IsKnown(text));
        Assert.Equal(canonical, KeyNames.Canonical(text));
    }

    [Theory]
    [InlineData("Mouse:Wheel")]
    [InlineData("Mouse:")]
    [InlineData("Left")]
    public void Other_names_are_not_mouse_buttons(string text) => Assert.False(KeyNames.IsMouse(text));

    private static Profile Load(string rule)
    {
        var r = ProfileLoader.Parse("{ \"name\": \"t\", \"safety\": \"standard\", \"outputs\": { \"keyboard\": true }, \"rules\": [ " + rule + " ] }", T.Devices);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        return r.Profile!;
    }

    [Fact]
    public void A_rule_can_press_a_mouse_button_and_a_punctuation_key_through_the_same_output()
    {
        var p = Load("""{ "from": "paddleR", "to": "mouse:left" }, { "from": "paddleL", "to": "Key:Semicolon" }""");
        T.Devices.TryGet("g923_xbox", out var d);
        var frame = new InputFrame();
        frame.Values["paddleR"] = 1; frame.Values["paddleL"] = 1;
        var keys = new MappingEngine(p, d, T.Devices).Evaluate(frame, 0).Keys;
        Assert.Contains("Mouse:Left", keys);
        Assert.Contains("Semicolon", keys);
    }

    [Fact]
    public void An_unknown_mouse_button_stops_the_profile_with_a_clear_message()
    {
        var r = ProfileLoader.Parse("""{ "name": "t", "outputs": { "keyboard": true }, "rules": [ { "from": "paddleR", "to": "Mouse:Wheel" } ] }""", T.Devices);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Contains("unknown mouse button") && e.Contains("Left"));

        var empty = ProfileLoader.Parse("""{ "name": "t", "rules": [ { "from": "paddleR", "to": "Mouse:" } ] }""", T.Devices);
        Assert.Contains(empty.Errors, e => e.Contains("button name"));
    }

    [Fact]
    public void Mouse_output_follows_the_same_safety_rules_as_the_keyboard()
    {
        var strict = ProfileLoader.Parse("""{ "name": "t", "safety": "strict", "outputs": { "keyboard": true }, "rules": [ { "from": "paddleR", "to": "Mouse:Left" } ] }""", T.Devices);
        Assert.True(strict.Ok);
        Assert.Contains(strict.Warnings, w => w.Contains("strict"));
        T.Devices.TryGet("g923_xbox", out var d);
        var frame = new InputFrame(); frame.Values["paddleR"] = 1;
        Assert.Empty(new MappingEngine(strict.Profile!, d, T.Devices).Evaluate(frame, 0).Keys);
    }

    [Fact]
    public void The_tracker_presses_and_releases_mouse_buttons_like_keys()
    {
        var t = new KeyStateTracker();
        Assert.Equal(["Mouse:Left"], t.Update(["Mouse:Left"]).Press);
        Assert.Equal(["Mouse:Left"], t.Update([]).Release);
    }
}

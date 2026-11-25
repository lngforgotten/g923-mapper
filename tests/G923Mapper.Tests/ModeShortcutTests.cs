using G923Mapper;

namespace G923Mapper.Tests;

public class InputTriggerTests
{
    private static InputTrigger Parse(string text)
    {
        Assert.True(InputTrigger.TryParse(text, out var t, out string? why), why);
        return t!;
    }

    [Theory]
    [InlineData("F8", "F8")]
    [InlineData("ctrl+f6", "Ctrl+F6")]
    [InlineData("Shift+Ctrl+Num7", "Ctrl+Shift+Num7")]
    [InlineData("Mouse:Back", "Mouse:Back")]
    [InlineData("mouse:x1", "Mouse:Back")]
    [InlineData("MOUSE:X2", "Mouse:Forward")]
    [InlineData("Ctrl+Mouse:Middle", "Ctrl+Mouse:Middle")]
    [InlineData("Pause", "Pause")]
    [InlineData("PageDown", "PageDown")]
    [InlineData("Alt+Q", "Alt+Q")]
    [InlineData("Win+Shift+F12", "Shift+Win+F12")]
    public void Text_is_read_and_written_back_in_one_tidy_form(string text, string tidy)
    {
        var t = Parse(text);
        Assert.Equal(tidy, t.ToString());
        Assert.Equal(t, Parse(t.ToString()));
    }

    [Fact]
    public void Function_keys_the_number_pad_and_mouse_buttons_may_stand_alone_but_typing_keys_may_not()
    {
        foreach (string ok in new[] { "F1", "F24", "Num0", "NumAdd", "Insert", "Home", "Left", "ScrollLock", "Mouse:Middle", "Mouse:Back", "Mouse:Forward" })
            Assert.True(InputTrigger.TryParse(ok, out _, out _), ok);
        foreach (string bad in new[] { "Q", "7", "Space", "Tab", "Enter", "Comma", "Quote" })
        {
            Assert.False(InputTrigger.TryParse(bad, out _, out string? why), bad);
            Assert.Contains("needs Ctrl", why);
        }
        foreach (string withMod in new[] { "Ctrl+Q", "Alt+7", "Shift+Space", "Ctrl+Comma" })
            Assert.True(InputTrigger.TryParse(withMod, out _, out _), withMod);
    }

    [Theory]
    [InlineData("Mouse:Left")]
    [InlineData("Mouse:Right")]
    [InlineData("Ctrl+Mouse:Left")]
    public void The_left_and_right_mouse_buttons_are_refused_with_a_reason(string text)
    {
        Assert.False(InputTrigger.TryParse(text, out _, out string? why));
        Assert.Contains("stop clicking", why);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Alt")]
    [InlineData("F8+F9")]
    [InlineData("Banana")]
    [InlineData("Mouse:Wheel")]
    [InlineData("Mouse:")]
    public void Nonsense_is_refused_without_an_exception(string? text)
    {
        Assert.False(InputTrigger.TryParse(text, out var t, out string? why));
        Assert.Null(t);
        Assert.False(string.IsNullOrWhiteSpace(why));
    }

    [Fact]
    public void Virtual_keys_have_names_and_modifier_keys_are_recognised()
    {
        Assert.Equal("F8", InputTrigger.KeyName(0x77));
        Assert.Equal("Q", InputTrigger.KeyName('Q'));
        Assert.Equal("Num3", InputTrigger.KeyName(0x63));
        Assert.Null(InputTrigger.KeyName(0xFF));
        Assert.True(InputTrigger.IsModifierKey(0xA2));
        Assert.False(InputTrigger.IsModifierKey(0x77));
        Assert.Equal(HotkeyModifiers.Ctrl, InputTrigger.ModifierOf(0xA3));
        Assert.Equal(HotkeyModifiers.Alt, InputTrigger.ModifierOf(0x12));
        Assert.Equal(HotkeyModifiers.Shift, InputTrigger.ModifierOf(0xA0));
        Assert.Equal(HotkeyModifiers.Win, InputTrigger.ModifierOf(0x5B));
        Assert.Equal(HotkeyModifiers.None, InputTrigger.ModifierOf(0x41));
    }

    [Fact]
    public void Every_named_key_round_trips_through_its_name()
    {
        for (int vk = 0; vk < 256; vk++)
        {
            string? name = InputTrigger.KeyName(vk);
            if (name is null) continue;
            var t = InputTrigger.ForKey(HotkeyModifiers.Ctrl, vk);
            Assert.Equal(t, Parse(t.ToString()));
        }
    }
}

public class ModeShortcutMatcherTests
{
    private static ModeShortcut S(string trigger, string action = "Next") => new() { Trigger = trigger, Action = action };

    private const HotkeyModifiers None = HotkeyModifiers.None;
    private const int F8 = 0x77, F9 = 0x78, Q = 'Q';

    [Fact]
    public void A_key_fires_its_shortcut_once_and_the_repeat_while_held_is_ignored()
    {
        var m = new ModeShortcutMatcher([S("F8")]);
        Assert.NotNull(m.KeyDown(F8, None));
        Assert.Null(m.KeyDown(F8, None));          // auto-repeat
        Assert.Null(m.KeyDown(F8, None));
        m.KeyUp(F8);
        Assert.NotNull(m.KeyDown(F8, None));        // pressed again
    }

    [Fact]
    public void The_modifiers_held_must_be_exactly_the_ones_in_the_shortcut()
    {
        var m = new ModeShortcutMatcher([S("Ctrl+F8", "Previous"), S("F9")]);
        Assert.Null(m.KeyDown(F8, None)); m.KeyUp(F8);                                // F8 alone is not Ctrl+F8
        Assert.Null(m.KeyDown(F8, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift)); m.KeyUp(F8);   // too many
        Assert.Equal("Previous", m.KeyDown(F8, HotkeyModifiers.Ctrl)!.Action); m.KeyUp(F8);
        Assert.Null(m.KeyDown(F9, HotkeyModifiers.Ctrl)); m.KeyUp(F9);                // F9 needs no modifier
        Assert.NotNull(m.KeyDown(F9, None));
    }

    [Fact]
    public void Modifier_keys_on_their_own_never_fire_anything()
    {
        var m = new ModeShortcutMatcher([S("Ctrl+F8")]);
        foreach (int vk in new[] { 0x11, 0xA2, 0x10, 0xA0, 0x12, 0x5B })
            Assert.Null(m.KeyDown(vk, InputTrigger.ModifierOf(vk)));
    }

    [Fact]
    public void A_mouse_button_fires_once_per_press_and_only_its_own_button()
    {
        var m = new ModeShortcutMatcher([S("Mouse:Back", "Previous"), S("Mouse:Forward")]);
        Assert.Equal("Previous", m.MouseDown(MouseTriggerButton.Back, None)!.Action);
        Assert.Null(m.MouseDown(MouseTriggerButton.Back, None));                     // no press message again before the release
        m.MouseUp(MouseTriggerButton.Back);
        Assert.NotNull(m.MouseDown(MouseTriggerButton.Back, None));
        Assert.Equal("Next", m.MouseDown(MouseTriggerButton.Forward, None)!.Action);
        Assert.Null(m.MouseDown(MouseTriggerButton.Middle, None));
    }

    [Fact]
    public void A_key_shortcut_does_not_fire_for_a_mouse_button_and_the_other_way_round()
    {
        var m = new ModeShortcutMatcher([S("F8"), S("Mouse:Middle")]);
        Assert.Null(m.MouseDown(MouseTriggerButton.Back, None));
        Assert.NotNull(m.MouseDown(MouseTriggerButton.Middle, None));
        Assert.Null(m.KeyDown(F9, None));
    }

    [Fact]
    public void A_mode_by_name_comes_back_as_that_name()
    {
        var m = new ModeShortcutMatcher([S("F6", "Shooting"), S("F7", "Driving")]);
        Assert.Equal("Driving", m.KeyDown(0x76, None)!.Action);      // F7
        Assert.Equal("Shooting", m.KeyDown(0x75, None)!.Action);     // F6
    }

    [Fact]
    public void Shortcuts_that_cannot_be_used_are_left_out_and_never_fire()
    {
        var m = new ModeShortcutMatcher([S("Q"), S("Mouse:Left"), S("nonsense"), S("F8", ""), S("F9")]);
        Assert.Null(m.KeyDown(Q, None));
        Assert.NotNull(m.KeyDown(F9, None));
        Assert.Null(m.KeyDown(F8, None));
        Assert.False(m.IsEmpty);
        Assert.True(new ModeShortcutMatcher(null).IsEmpty);
        Assert.True(new ModeShortcutMatcher([]).IsEmpty);
    }

    [Fact]
    public void Resetting_forgets_keys_that_looked_held_because_a_release_was_missed()
    {
        var m = new ModeShortcutMatcher([S("F8")]);
        Assert.NotNull(m.KeyDown(F8, None));
        Assert.Null(m.KeyDown(F8, None));
        m.Reset();
        Assert.NotNull(m.KeyDown(F8, None));
    }

    [Fact]
    public void The_same_trigger_twice_uses_the_first_and_says_so()
    {
        var (usable, problems) = ModeShortcutList.Check([S("F8", "Next"), S("f8", "Previous")]);
        Assert.Single(usable);
        Assert.Equal("Next", usable[0].Shortcut.Action);
        Assert.Contains(problems, p => p.Contains("more than once"));
    }

    [Fact]
    public void There_is_a_limit_on_how_many_shortcuts_are_used()
    {
        var many = Enumerable.Range(1, 24).Select(i => S("F" + i)).ToList();
        var (usable, problems) = ModeShortcutList.Check(many);
        Assert.Equal(ModeShortcutList.MaxShortcuts, usable.Count);
        Assert.Equal(4, problems.Count);
    }

    [Fact]
    public void A_shortcut_describes_itself_in_words()
    {
        Assert.Equal("F8: next mode", S("F8").Describe());
        Assert.Equal("Mouse:Back: previous mode", S("Mouse:Back", "previous").Describe());
        Assert.Equal("Ctrl+F6: the mode \"Driving\"", S("Ctrl+F6", "Driving").Describe());
    }

    [Fact]
    public void Shortcuts_survive_the_settings_file()
    {
        var list = new List<ModeShortcut> { S("F8"), S("Mouse:Back", "Previous"), S("Ctrl+F6", "Driving") };
        string json = System.Text.Json.JsonSerializer.Serialize(list);
        var back = System.Text.Json.JsonSerializer.Deserialize<List<ModeShortcut>>(json)!;
        Assert.Equal(3, back.Count);
        Assert.Equal("Mouse:Back", back[1].Trigger);
        Assert.Equal("Driving", back[2].Action);
    }
}

using G923Mapper;

namespace G923Mapper.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+F10", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x79, "Ctrl+Alt+F10")]
    [InlineData("shift+ctrl+p", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 0x50, "Ctrl+Shift+P")]
    [InlineData("Pause", HotkeyModifiers.None, 0x13, "Pause")]
    [InlineData("f1", HotkeyModifiers.None, 0x70, "F1")]
    [InlineData("Win + F24", HotkeyModifiers.Win, 0x87, "Win+F24")]
    [InlineData("Alt+5", HotkeyModifiers.Alt, 0x35, "Alt+5")]
    [InlineData("control+scrolllock", HotkeyModifiers.Ctrl, 0x91, "Ctrl+ScrollLock")]
    public void Parses_and_formats_shortcuts(string text, HotkeyModifiers mods, int vk, string back)
    {
        Assert.True(Hotkey.TryParse(text, out var h, out var err), err);
        Assert.Equal(mods, h!.Modifiers);
        Assert.Equal(vk, h.VirtualKey);
        Assert.Equal(back, h.ToString());
    }

    [Theory]
    [InlineData("", "no shortcut")]
    [InlineData("Ctrl+Alt", "needs a key")]
    [InlineData("Ctrl+A+B", "only one key")]
    [InlineData("Ctrl+F25", "not a key")]
    [InlineData("Ctrl+?", "not a key")]
    [InlineData("P", "needs Ctrl, Alt or Shift")]
    [InlineData("5", "needs Ctrl, Alt or Shift")]
    public void Rejects_unusable_shortcuts_with_a_reason(string text, string phrase)
    {
        Assert.False(Hotkey.TryParse(text, out var h, out var err));
        Assert.Null(h);
        Assert.Contains(phrase, err);
    }

    [Fact]
    public void Every_preset_and_the_default_parse()
    {
        foreach (string p in Hotkey.Presets.Append(Hotkey.Default))
            Assert.True(Hotkey.TryParse(p, out _, out var err), p + ": " + err);
    }
}

public class FocusGateTests
{
    private static readonly string[] Gta = ["GTA5.exe", "GTA5_Enhanced.exe"];

    [Fact]
    public void Off_means_everything_passes()
    {
        Assert.True(FocusGate.Allows(false, Gta, "chrome.exe", _ => true));
    }

    [Fact]
    public void A_profile_not_tied_to_a_game_always_passes()
    {
        Assert.True(FocusGate.Allows(true, [], "chrome.exe", _ => true));
    }

    [Fact]
    public void With_the_game_closed_nothing_is_released()
    {
        Assert.True(FocusGate.Allows(true, Gta, "chrome.exe", _ => false));
    }

    [Theory]
    [InlineData("GTA5_Enhanced.exe", true)]
    [InlineData("gta5", true)]
    [InlineData("chrome.exe", false)]
    [InlineData("G923Mapper.exe", false)]
    [InlineData("explorer.exe", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void With_the_game_running_only_the_game_in_front_receives_input(string? front, bool expected)
    {
        Assert.Equal(expected, FocusGate.Allows(true, Gta, front, exe => exe == "GTA5_Enhanced.exe"));
    }
}

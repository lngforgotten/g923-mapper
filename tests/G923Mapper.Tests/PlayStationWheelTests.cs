using System.Text.Json;
using G923Mapper;

namespace G923Mapper.Tests;

public class PlayStationWheelTests
{
    /// <summary>The calibration a friend made with a real PlayStation edition wheel (046D:C266).</summary>
    private const string FriendsFile = """
        { "g923_ps": { "name": "g923_ps", "vid": "046D", "pid": "C266", "extra": false, "rotationDegrees": 900,
            "axes": {
              "wheel": { "source": "X", "signed": true, "invert": false, "min": 0, "max": 65535, "centre": 32660 },
              "throttle": { "source": "Y", "signed": false, "invert": true, "min": 0, "max": 65535, "centre": 32768 },
              "brake": { "source": "RZ", "signed": false, "invert": true, "min": 0, "max": 65535, "centre": 32768 },
              "clutch": { "source": "Slider0", "signed": false, "invert": true, "min": 0, "max": 65535, "centre": 32768 } },
            "buttons": { "paddleL": 6, "paddleR": 5, "buttonA": 1, "buttonB": 3, "buttonX": 2, "buttonY": 4, "view": 9, "menu": 10 } } }
        """;

    [Fact]
    public void The_built_in_devices_now_include_the_playstation_edition_and_the_xbox_one_is_unchanged()
    {
        var reg = DeviceRegistry.CreateDefault();
        Assert.True(reg.TryGet("g923_ps", out var ps));
        Assert.Equal(("046D", "C266"), (ps.Vid, ps.Pid));
        Assert.Equal(32660, ps.Axes["wheel"].Centre);
        Assert.True(ps.Axes["wheel"].Signed);
        Assert.True(ps.Axes["throttle"].Invert && ps.Axes["brake"].Invert && ps.Axes["clutch"].Invert);
        Assert.Equal(WheelEdition.PlayStation, WheelEditions.Of(ps));
        Assert.Equal(WheelEdition.Xbox, WheelEditions.Of(reg.Fallback!));
        Assert.Equal("g923_xbox", reg.Fallback!.Name);
        Assert.Equal(2, reg.All.Count(d => !d.Extra));
    }

    [Fact]
    public void A_playstation_wheel_is_found_by_its_usb_ids_and_an_xbox_one_still_gets_the_xbox_layout()
    {
        var reg = DeviceRegistry.CreateDefault();
        Assert.Equal("g923_ps", reg.Resolve("auto", (0x046D, 0xC266))!.Name);
        Assert.Equal("g923_xbox", reg.Resolve("auto", (0x046D, 0xC26E))!.Name);
        Assert.Equal("g923_xbox", reg.Resolve("auto", null)!.Name);
    }

    [Fact]
    public void A_playstation_edition_wheel_nobody_has_calibrated_gets_the_playstation_layout_not_the_xbox_one()
    {
        var reg = DeviceRegistry.CreateDefault();
        Assert.Equal("g923_ps", reg.Resolve("auto", (0x046D, 0xC267))!.Name);   // the other PlayStation product id
        Assert.Equal("g923_xbox", reg.Resolve("auto", (0x046D, 0xC24F))!.Name); // a wheel of another kind falls back as before
    }

    [Fact]
    public void The_face_buttons_and_the_middle_ones_are_named_the_playstation_way_first_with_the_xbox_names_kept()
    {
        var ps = DeviceRegistry.G923PlayStation();
        Assert.Equal("cross", InputDetector.ButtonName(ps, 1));
        Assert.Equal("square", InputDetector.ButtonName(ps, 2));
        Assert.Equal("circle", InputDetector.ButtonName(ps, 3));
        Assert.Equal("triangle", InputDetector.ButtonName(ps, 4));
        Assert.Equal("share", InputDetector.ButtonName(ps, 9));
        Assert.Equal("options", InputDetector.ButtonName(ps, 10));
        Assert.Equal("paddleR", InputDetector.ButtonName(ps, 5));
        Assert.Equal("paddleL", InputDetector.ButtonName(ps, 6));
        Assert.Equal("button7", InputDetector.ButtonName(ps, 7));   // not named yet: the plain number
        foreach (var (xbox, number) in new[] { ("buttonA", 1), ("buttonB", 3), ("buttonX", 2), ("buttonY", 4), ("view", 9), ("menu", 10) })
            Assert.Equal(number, ps.Buttons[xbox]);                   // older profiles that use these names still work
    }

    [Fact]
    public void A_friends_own_calibration_file_gets_the_playstation_names_added_and_keeps_its_numbers()
    {
        string dir = Path.Combine(Path.GetTempPath(), "g923-ps-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "devices.json");
            File.WriteAllText(file, FriendsFile);
            var reg = DeviceRegistry.CreateDefault();
            reg.LoadFile(file);
            Assert.True(reg.TryGet("g923_ps", out var ps));
            Assert.Equal(32660, ps.Axes["wheel"].Centre);
            Assert.Equal("cross", InputDetector.ButtonName(ps, 1));
            Assert.Equal("options", InputDetector.ButtonName(ps, 10));
            Assert.Equal(1, ps.Buttons["buttonA"]);
            Assert.Equal(10, ps.Buttons["menu"]);
            Assert.Equal(9, ps.Buttons["share"]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Both_the_playstation_and_the_xbox_style_names_are_valid_inputs_in_a_profile()
    {
        var reg = DeviceRegistry.CreateDefault();
        const string json = """
            { "name": "t", "device": "g923_ps", "rules": [
              { "from": "cross", "to": "Button:A" }, { "from": "buttonA", "to": "Button:B" }, { "from": "share", "to": "Button:Back" },
              { "from": "view", "to": "Button:Start" }, { "from": "options", "to": "Button:Guide" }, { "from": "paddleL", "to": "Button:LeftShoulder" } ] }
            """;
        var r = ProfileLoader.Parse(json, reg);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
    }

    [Fact]
    public void The_xbox_wheel_does_not_get_playstation_names()
    {
        var xbox = DeviceRegistry.G923Xbox();
        PlayStationNames.Apply(xbox);   // harmless if called on it by mistake: there is nothing to rename
        Assert.DoesNotContain("cross", xbox.Buttons.Keys);
        Assert.Equal("paddleL", InputDetector.ButtonName(xbox, 5));
    }

    [Fact]
    public void A_name_that_is_already_in_use_is_never_taken_over()
    {
        var map = new DeviceMap { Name = "x", Axes = new(StringComparer.OrdinalIgnoreCase) { ["cross"] = new AxisDef { Source = "X" } }, Buttons = new(StringComparer.OrdinalIgnoreCase) { ["buttonA"] = 1, ["circle"] = 7, ["buttonB"] = 3 } };
        PlayStationNames.Apply(map);
        Assert.False(map.Buttons.ContainsKey("cross"));   // an axis has it
        Assert.Equal(7, map.Buttons["circle"]);            // another button has it
        Assert.Equal(3, map.Buttons["buttonB"]);
    }

    [Fact]
    public void A_playstation_wheel_gets_a_dualshock_for_auto_profiles_and_an_xbox_wheel_an_xbox_pad()
    {
        try
        {
            PadKinds.SetFor(WheelEditions.Of(DeviceRegistry.G923PlayStation()));
            Assert.Equal(PadKind.DualShock4, PadKinds.AutoKind);
            PadKinds.SetFor(WheelEditions.Of(DeviceRegistry.G923Xbox()));
            Assert.Equal(PadKind.Xbox360, PadKinds.AutoKind);
        }
        finally { PadKinds.AutoKind = PadKind.Xbox360; }
    }
}

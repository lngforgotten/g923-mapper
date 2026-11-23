using G923Mapper;

namespace G923Mapper.Tests;

public sealed class ExtraDeviceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "g923-extra-" + Guid.NewGuid());
    public ExtraDeviceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    /// <summary>A USB handbrake: one axis that reads 0 at rest and 65535 when pulled, plus a few buttons.</summary>
    private static DeviceMap Handbrake() => new()
    {
        Name = "handbrake", Extra = true, Vid = "1234", Pid = "ABCD",
        Axes = new(StringComparer.OrdinalIgnoreCase) { ["pull"] = new AxisDef { Source = "X", Min = 0, Max = 65535 } },
        Buttons = new(StringComparer.OrdinalIgnoreCase) { ["gearUp"] = 3 },
    };

    private static DeviceRegistry Registry()
    {
        var reg = DeviceRegistry.CreateDefault();
        reg.Add(Handbrake());
        return reg;
    }

    private static Profile Parse(DeviceRegistry reg, string rule)
    {
        var r = ProfileLoader.Parse($$"""{ "name": "t", "rules": [ {{rule}} ] }""", reg);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        return r.Profile!;
    }

    [Fact]
    public void Inputs_are_namespaced_with_the_device_name()
    {
        var frame = Handbrake().Normalize(new RawInput { Axes = [65535, 0, 0, 0, 0, 0, 0, 0], Buttons = T.Buttons(3), Live = true }, "handbrake.");
        Assert.Equal(1.0, frame.Get("handbrake.pull"), 6);
        Assert.Equal(1, frame.Get("handbrake.gearUp"));
        Assert.Equal(1, frame.Get("handbrake.button3"));
        Assert.Equal(0, frame.Get("pull"));             // no un-prefixed leakage that could collide with the wheel
        Assert.Equal(0, frame.Get("gearUp"));
    }

    [Fact]
    public void Without_a_prefix_normalising_is_unchanged()
        => Assert.Equal(1.0, Handbrake().Normalize(new RawInput { Axes = [65535, 0, 0, 0, 0, 0, 0, 0], Live = true }).Get("pull"), 6);

    [Theory]
    [InlineData("handbrake.pull", true)]
    [InlineData("handbrake.gearUp", true)]
    [InlineData("handbrake.button12", true)]
    [InlineData("handbrake.dpadUp", true)]
    [InlineData("handbrake.nothing", false)]
    [InlineData("shifter.pull", false)]         // no such device
    [InlineData("handbrake.", false)]
    [InlineData(".pull", false)]
    [InlineData("wheel", true)]                 // the primary wheel still works
    [InlineData("pull", false)]                 // extra inputs need their prefix
    public void Known_inputs_include_extra_devices_by_prefix(string input, bool known)
        => Assert.Equal(known, Registry().IsKnownInput(DeviceRegistry.G923Xbox(), input));

    [Fact]
    public void A_device_that_is_not_marked_extra_cannot_be_used_as_a_prefix()
    {
        var reg = DeviceRegistry.CreateDefault();
        Assert.False(reg.IsKnownInput(DeviceRegistry.G923Xbox(), "g923_xbox.wheel"));
    }

    [Fact]
    public void Profiles_can_use_extra_inputs_and_unknown_ones_get_a_helpful_error()
    {
        var reg = Registry();
        Parse(reg, """{ "from": "handbrake.pull", "to": "Button:X", "threshold": 0.3 }""");
        Parse(reg, """{ "from": "handbrake.gearUp", "to": "Button:Y" }""");

        var bad = ProfileLoader.Parse("""{ "name": "t", "rules": [ { "from": "shifter.pull", "to": "Button:X" } ] }""", reg);
        Assert.False(bad.Ok);
        Assert.Contains(bad.Errors, e => e.Contains("shifter.pull") && e.Contains("set up"));
    }

    [Fact]
    public void Signedness_comes_from_the_extra_device()
    {
        var reg = Registry();
        var map = reg.TryGet("handbrake", out var h) ? h : null!;
        map.Axes["bar"] = new AxisDef { Source = "Y", Signed = true };
        Assert.True(reg.IsSigned(DeviceRegistry.G923Xbox(), "handbrake.bar"));
        Assert.False(reg.IsSigned(DeviceRegistry.G923Xbox(), "handbrake.pull"));
        Assert.True(reg.IsSigned(DeviceRegistry.G923Xbox(), "wheel"));
    }

    [Fact]
    public void An_extra_axis_drives_a_pad_output_through_the_engine()
    {
        var reg = Registry();
        var wheel = DeviceRegistry.G923Xbox();
        var profile = Parse(reg, """{ "from": "handbrake.pull", "to": "RightTrigger" }""");
        var engine = new MappingEngine(profile, wheel, reg);

        var frame = wheel.Normalize(T.Raw());
        foreach (var kv in Handbrake().Normalize(new RawInput { Axes = [32768, 0, 0, 0, 0, 0, 0, 0], Live = true }, "handbrake.").Values)
            frame.Values[kv.Key] = kv.Value;

        Assert.InRange((int)engine.Evaluate(frame, 0).RightTrigger, 127, 128);   // half pulled
    }

    [Fact]
    public void An_extra_button_and_the_wheel_can_drive_the_same_output()
    {
        var reg = Registry();
        var wheel = DeviceRegistry.G923Xbox();
        var profile = Parse(reg, """{ "from": "handbrake.gearUp", "to": "Button:Y" }""");
        var engine = new MappingEngine(profile, wheel, reg);
        var frame = wheel.Normalize(T.Raw());
        foreach (var kv in Handbrake().Normalize(new RawInput { Axes = new int[8], Buttons = T.Buttons(3), Live = true }, "handbrake.").Values)
            frame.Values[kv.Key] = kv.Value;
        Assert.True(engine.Evaluate(frame, 0)[PadButton.Y]);
    }

    [Fact]
    public void Range_on_an_extra_signed_axis_scales_by_the_primary_devices_rotation_only_for_the_wheel()
    {
        // The engine must not crash or mis-flag an extra axis; its signedness is looked up, not assumed.
        var reg = Registry();
        var p = Parse(reg, """{ "from": "handbrake.pull", "to": "LeftStickX", "range": 540 }""");
        var r = ProfileLoader.Parse("""{ "name": "t", "rules": [ { "from": "handbrake.pull", "to": "LeftStickX", "range": 540 } ] }""", reg);
        Assert.Contains(r.Warnings, w => w.Contains("range"));
        Assert.NotNull(new MappingEngine(p, DeviceRegistry.G923Xbox(), reg));
    }

    [Fact]
    public void Primary_lookup_by_usb_ids_never_returns_an_extra_device()
    {
        var reg = Registry();
        Assert.Null(reg.FindByUsb(0x1234, 0xABCD));
        Assert.Equal("handbrake", reg.FindByUsb(0x1234, 0xABCD, includeExtra: true)?.Name);
        Assert.Equal("g923_xbox", reg.FindByUsb(0x046D, 0xC26E)?.Name);
    }

    [Fact]
    public void Extra_devices_are_listed_separately_and_can_be_removed()
    {
        var reg = Registry();
        Assert.Equal(["handbrake"], reg.ExtraDevices.Select(d => d.Name));
        reg.Remove("handbrake");
        Assert.Empty(reg.ExtraDevices);
        Assert.False(reg.IsKnownInput(DeviceRegistry.G923Xbox(), "handbrake.pull"));
    }

    [Fact]
    public void The_extra_flag_and_names_survive_saving_and_loading()
    {
        string path = Path.Combine(_dir, "devices.json");
        DeviceRegistry.SaveEntry(path, Handbrake());

        var reg = DeviceRegistry.CreateDefault();
        reg.LoadFile(path);
        Assert.True(reg.TryGet("handbrake", out var loaded));
        Assert.True(loaded.Extra);
        Assert.Equal("1234", loaded.Vid);
        Assert.Equal(3, loaded.Buttons["gearUp"]);
        Assert.True(reg.IsKnownInput(DeviceRegistry.G923Xbox(), "handbrake.pull"));
        Assert.Contains("\"extra\": true", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("handbrake", true)]
    [InlineData("shifter2", true)]
    [InlineData("A", true)]
    [InlineData("hand brake", false)]
    [InlineData("hand-brake", false)]
    [InlineData("1st", false)]
    [InlineData("", false)]
    [InlineData("handbrake.x", false)]            // a dot would break the "device.input" syntax
    [InlineData("averyveryveryverylongdevicename", false)]
    public void Device_names_must_be_short_single_words(string name, bool ok)
        => Assert.Equal(ok, ExtraDeviceNaming.Validate(name, DeviceRegistry.CreateDefault()) is null);

    [Fact]
    public void A_device_name_cannot_repeat_but_you_can_keep_your_own()
    {
        var reg = Registry();
        Assert.Contains("already", ExtraDeviceNaming.Validate("handbrake", reg));
        Assert.Contains("already", ExtraDeviceNaming.Validate("g923_xbox", reg));
        Assert.Null(ExtraDeviceNaming.Validate("handbrake", reg, existingName: "handbrake"));
    }

    [Theory]
    [InlineData("pull", true)]
    [InlineData("pull.hard", false)]
    [InlineData("", false)]
    public void Input_names_inside_a_device_are_single_words(string name, bool ok)
        => Assert.Equal(ok, ExtraDeviceNaming.ValidateInputName(name) is null);
}

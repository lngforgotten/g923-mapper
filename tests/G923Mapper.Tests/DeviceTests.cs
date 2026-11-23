using G923Mapper;

namespace G923Mapper.Tests;

public class DeviceTests
{
    private static readonly DeviceMap G = DeviceRegistry.G923Xbox();

    [Theory]
    [InlineData(0, -1.0)]
    [InlineData(32768, 0.0)]
    [InlineData(65535, 1.0)]
    public void Wheel_normalises_to_minus_one_to_one(int raw, double expected)
        => Assert.Equal(expected, G.Normalize(T.Raw(wheel: raw)).Get("wheel"), 10);

    [Fact]
    public void Wheel_is_symmetric_around_an_off_centre_rest_position()
    {
        // The real wheel rests around 32054..33425 depending on the run.
        var d = DeviceRegistry.G923Xbox();
        d.Axes["wheel"].Centre = 33425;
        Assert.Equal(0, d.Normalize(T.Raw(wheel: 33425)).Get("wheel"), 10);
        Assert.Equal(-1, d.Normalize(T.Raw(wheel: 0)).Get("wheel"), 10);
        Assert.Equal(1, d.Normalize(T.Raw(wheel: 65535)).Get("wheel"), 10);
        // Half way to each lock reads as exactly half.
        Assert.Equal(0.5, d.Normalize(T.Raw(wheel: 33425 + (65535 - 33425) / 2)).Get("wheel"), 3);
        Assert.Equal(-0.5, d.Normalize(T.Raw(wheel: 33425 / 2)).Get("wheel"), 3);
    }

    [Fact]
    public void Wheel_out_of_range_is_clamped()
    {
        Assert.Equal(1, G.Normalize(T.Raw(wheel: 70000)).Get("wheel"), 10);
        Assert.Equal(-1, G.Normalize(T.Raw(wheel: -5)).Get("wheel"), 10);
    }

    [Theory]
    [InlineData(65535, 0.0)]   // G923 pedals read high at rest
    [InlineData(0, 1.0)]       // and low when fully pressed
    public void Pedals_are_inverted_to_zero_at_rest(int raw, double expected)
    {
        var f = G.Normalize(T.Raw(throttle: raw, brake: raw, clutch: raw));
        Assert.Equal(expected, f.Get("throttle"), 10);
        Assert.Equal(expected, f.Get("brake"), 10);
        Assert.Equal(expected, f.Get("clutch"), 10);
    }

    [Fact]
    public void Pedal_half_way_reads_half()
        => Assert.Equal(0.5, G.Normalize(T.Raw(throttle: 32768)).Get("throttle"), 3);

    [Fact]
    public void Pedals_read_zero_until_the_device_is_live()
    {
        // A fresh acquire reports 32768 everywhere, which would look like half-pressed pedals.
        var f = G.Normalize(T.Raw(throttle: 32768, brake: 32768, clutch: 32768, live: false));
        Assert.Equal(0, f.Get("throttle"));
        Assert.Equal(0, f.Get("brake"));
        Assert.Equal(0, f.Get("clutch"));
    }

    [Fact]
    public void Wheel_reads_centred_until_the_device_is_live()
    {
        // The stale 32768 is only "centre" by coincidence; with a real rest position of 33199 it would
        // otherwise read as a left push until the wheel is first moved.
        var d = DeviceRegistry.G923Xbox();
        d.Axes["wheel"].Centre = 33199;
        Assert.Equal(0, d.Normalize(T.Raw(wheel: 32768, live: false)).Get("wheel"));
        Assert.True(d.Normalize(T.Raw(wheel: 32768, live: true)).Get("wheel") < 0);
    }

    [Fact]
    public void Paddles_and_numbered_buttons_both_resolve()
    {
        var f = G.Normalize(T.Raw(buttons: T.Buttons(5)));
        Assert.Equal(1, f.Get("paddleL"));
        Assert.Equal(1, f.Get("button5"));
        Assert.Equal(0, f.Get("paddleR"));
        Assert.Equal(0, f.Get("button6"));
    }

    [Theory]
    [InlineData(-1, false, false, false, false)]
    [InlineData(0, true, false, false, false)]
    [InlineData(9000, false, false, false, true)]
    [InlineData(18000, false, true, false, false)]
    [InlineData(27000, false, false, true, false)]
    [InlineData(4500, true, false, false, true)]    // diagonal sets two directions
    public void Hat_maps_to_dpad_directions(int pov, bool up, bool down, bool left, bool right)
    {
        var f = G.Normalize(T.Raw(pov: pov));
        Assert.Equal(up, f.Get("dpadUp") == 1);
        Assert.Equal(down, f.Get("dpadDown") == 1);
        Assert.Equal(left, f.Get("dpadLeft") == 1);
        Assert.Equal(right, f.Get("dpadRight") == 1);
    }

    [Theory]
    [InlineData("wheel", true)]
    [InlineData("Throttle", true)]
    [InlineData("paddleL", true)]
    [InlineData("button1", true)]
    [InlineData("button23", true)]
    [InlineData("button99", false)]
    [InlineData("button0", false)]
    [InlineData("dpadUp", true)]
    [InlineData("steering", false)]
    public void Known_input_names(string name, bool known) => Assert.Equal(known, G.IsKnownInput(name));

    [Fact]
    public void Devices_json_overrides_and_adds_devices()
    {
        string path = Path.Combine(Path.GetTempPath(), "g923-devices-" + Guid.NewGuid() + ".json");
        File.WriteAllText(path, """
            {
              "g923_ps": {
                "rotationDegrees": 900,
                "axes": { "wheel": { "source": "X", "signed": true }, "throttle": { "source": "RZ", "invert": true } },
                "buttons": { "paddleL": 7 }
              }
            }
            """);
        try
        {
            var reg = DeviceRegistry.CreateDefault();
            reg.LoadFile(path);
            Assert.True(reg.TryGet("g923_ps", out var ps));
            Assert.Equal("g923_ps", ps.Name);
            Assert.True(ps.IsSigned("wheel"));
            Assert.True(ps.IsKnownInput("PADDLEL"));   // case-insensitive
            Assert.True(reg.TryGet("g923_xbox", out _));   // built-in still there
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Missing_devices_json_is_fine()
    {
        var reg = DeviceRegistry.CreateDefault();
        reg.LoadFile(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".json"));
        Assert.True(reg.TryGet("g923_xbox", out _));
    }
}

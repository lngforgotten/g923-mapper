using G923Mapper;

namespace G923Mapper.Tests;

public class AutoDeviceTests
{
    // A PlayStation-edition wheel as it might be calibrated: a different USB id and a different axis order.
    private static DeviceMap PsWheel() => new()
    {
        Name = "g923_ps", Vid = "046D", Pid = "C266", RotationDegrees = 900,
        Axes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["wheel"] = new AxisDef { Source = "X", Signed = true, Min = 0, Max = 65535, Centre = 32768 },
            ["throttle"] = new AxisDef { Source = "RZ", Min = 0, Max = 65535 },
            ["brake"] = new AxisDef { Source = "Y", Min = 0, Max = 65535 },
        },
    };

    private static DeviceRegistry WithPs()
    {
        var reg = DeviceRegistry.CreateDefault();
        reg.Add(PsWheel());
        return reg;
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("AUTO")]
    [InlineData(" auto ")]
    public void Auto_is_recognised_in_any_spelling(string name) => Assert.True(DeviceRegistry.IsAuto(name));

    [Fact]
    public void Auto_picks_the_calibrated_entry_for_the_connected_wheel()
    {
        var reg = WithPs();
        Assert.Equal("g923_xbox", reg.Resolve("auto", (0x046D, 0xC26E))!.Name);
        Assert.Equal("g923_ps", reg.Resolve("auto", (0x046D, 0xC266))!.Name);
    }

    [Fact]
    public void Auto_falls_back_to_the_built_in_layout_for_a_wheel_nobody_has_calibrated()
    {
        var reg = DeviceRegistry.CreateDefault();
        Assert.False(reg.IsKnownWheel(0x046D, 0xC24F));                            // a wheel of some other kind
        Assert.Equal("g923_xbox", reg.Resolve("auto", (0x046D, 0xC24F))!.Name);
        Assert.True(reg.IsKnownWheel(0x046D, 0xC266));                             // the PlayStation edition is built in now
        Assert.Equal("g923_xbox", reg.Resolve("auto", null)!.Name);
    }

    [Fact]
    public void A_named_device_is_used_as_is_whatever_wheel_is_connected()
    {
        var reg = WithPs();
        Assert.Equal("g923_xbox", reg.Resolve("g923_xbox", (0x046D, 0xC266))!.Name);
        Assert.Null(reg.Resolve("nonsense", null));
        Assert.Null(reg.Resolve(null, null));
    }

    [Fact]
    public void One_auto_profile_gives_each_wheel_its_own_layout()
    {
        var reg = WithPs();
        var profile = ProfileLoader.Parse("""
            { "name": "t", "device": "auto", "rules": [ { "from": "throttle", "to": "RightTrigger" } ] }
            """, reg).Profile!;

        // Same raw readings, different wheels: the throttle comes from a different axis on each.
        var raw = new RawInput { Axes = [32768, 65535, 0, 0, 0, 65535, 0, 0], Buttons = new bool[23], Live = true };
        double xbox = reg.Resolve("auto", (0x046D, 0xC26E))!.Normalize(raw).Get("throttle");
        double ps = reg.Resolve("auto", (0x046D, 0xC266))!.Normalize(raw).Get("throttle");
        Assert.NotEqual(xbox, ps);
        Assert.NotNull(profile);
    }

    [Fact]
    public void The_starter_profiles_and_presets_use_auto()
    {
        var reg = DeviceRegistry.CreateDefault();
        string dir = Path.Combine(Path.GetTempPath(), "g923-auto-" + Guid.NewGuid());
        try
        {
            DefaultProfiles.EnsureWritten(dir);
            foreach (string file in Directory.GetFiles(dir, "*.json"))
                Assert.Equal("auto", ProfileLoader.LoadFile(file, reg).Profile!.Device);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }

        foreach (var preset in Presets.All)
            Assert.Equal("auto", ProfileLoader.Parse(preset.Json, reg).Profile!.Device);
    }

    [Fact]
    public void A_profile_with_no_device_field_means_auto()
    {
        var p = ProfileLoader.Parse("""{ "name": "t", "rules": [] }""", DeviceRegistry.CreateDefault()).Profile!;
        Assert.Equal("auto", p.Device);
    }

    [Fact]
    public void An_unknown_device_error_points_at_auto()
    {
        var r = ProfileLoader.Parse("""{ "name": "t", "device": "g999", "rules": [] }""", DeviceRegistry.CreateDefault());
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Contains("auto"));
    }

    [Fact]
    public void An_uncalibrated_wheel_is_flagged_in_the_health_check()
    {
        var items = HealthEvaluator.Evaluate(new HealthFacts
        {
            ViGEmInstalled = true, ViGEmRunning = true, MapperRunning = true, WheelFound = true, WheelReporting = true,
            WheelName = "G923 Racing Wheel for PlayStation 4 and PC", WheelUncalibrated = true,
        });
        var item = items.Single(i => i.Title.Contains("not been calibrated"));
        Assert.Equal(HealthLevel.Warning, item.Level);
        Assert.Contains("PlayStation", item.Detail);
        Assert.Equal(HealthAction.OpenSettings, item.Action);
    }
}

public class PlayStationCalibrationTests
{
    [Theory]
    [InlineData("g923_ps", "C26E", true)]
    [InlineData("anything", "C266", true)]
    [InlineData("anything", "c267", true)]
    [InlineData("g923_playstation", "0000", true)]
    [InlineData("g923_xbox", "C26E", false)]
    [InlineData("wheel_c24f", "C24F", false)]
    public void Recognises_the_playstation_edition(string id, string pid, bool expected) =>
        Assert.Equal(expected, CalibrationOptions.LooksPlayStation(id, pid));

    [Fact]
    public void Playstation_wording_asks_for_cross_circle_square_triangle_but_stores_the_same_button_names()
    {
        var sim = new SimWheel();
        sim.Rest().Lock(0).Lock(65535)
           .Then(() => { sim.Axes[0] = 33199; sim.Axes[1] = 0; })
           .Then(() => { sim.Axes[1] = 65535; sim.Axes[5] = 0; })
           .Skip()
           .Then(() => sim.Buttons[4] = true)                         // paddleL
           .Then(() => { sim.Buttons[4] = false; sim.Buttons[5] = true; })   // paddleR
           .Then(() => { sim.Buttons[5] = false; sim.Buttons[0] = true; })   // Cross
           .Then(() => { sim.Buttons[0] = false; sim.Buttons[1] = true; });  // Circle
        for (int i = 0; i < 4; i++) sim.Skip();
        var map = new CalibrationWizard(sim, sim, new CalibrationOptions { DeviceId = "g923_ps", Vid = "046D", Pid = "C266", PlayStationLabels = true, SampleCount = 20, SampleIntervalMs = 1, WakeTimeoutPolls = 300 }).Run()!.Map;

        Assert.Contains(sim.Prompts, p => p.Contains("Cross") && p.Contains("Circle") == false);
        Assert.Contains(sim.Prompts, p => p.Contains("Triangle"));
        Assert.DoesNotContain(sim.Prompts, p => p.Contains("A button"));
        Assert.Equal(1, map.Buttons["buttonA"]);
        Assert.Equal(2, map.Buttons["buttonB"]);
        Assert.Equal("C266", map.Pid);
    }
}

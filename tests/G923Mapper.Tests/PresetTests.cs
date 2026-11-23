using G923Mapper;

namespace G923Mapper.Tests;

public class PresetTests
{
    [Fact]
    public void There_is_a_small_set_with_unique_ids_and_real_descriptions()
    {
        Assert.InRange(Presets.All.Count, 4, 8);   // clean, not a catalogue
        Assert.Equal(Presets.All.Count, Presets.All.Select(p => p.Id).Distinct().Count());
        Assert.All(Presets.All, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Title));
            Assert.True(p.Description.Length > 30, p.Id);
        });
    }

    [Fact]
    public void Every_preset_is_a_valid_profile_with_no_warnings()
    {
        foreach (var preset in Presets.All)
        {
            var r = ProfileLoader.Parse(preset.Json, T.Devices);
            Assert.True(r.Ok, $"{preset.Id}: " + string.Join("; ", r.Errors));
            Assert.Empty(r.Warnings);
        }
    }

    [Fact]
    public void Every_preset_keeps_keyboard_output_off()
        => Assert.All(Presets.All, p => Assert.False(ProfileLoader.Parse(p.Json, T.Devices).Profile!.Outputs.Keyboard, p.Id));

    [Fact]
    public void Every_preset_steers_and_drives_both_trigger_pedals_or_buttons()
    {
        foreach (var preset in Presets.All.Where(p => p.Id != "menus"))   // the menu preset deliberately has no driving
        {
            var rules = ProfileLoader.Parse(preset.Json, T.Devices).Profile!.Rules;
            Assert.Contains(rules, r => r.From == "wheel" && r.To == "LeftStickX");
            Assert.Contains(rules, r => r.From == "throttle");
            Assert.Contains(rules, r => r.From == "brake");
        }
    }

    [Fact]
    public void Only_the_GTA_preset_claims_a_game()
    {
        foreach (var preset in Presets.All)
        {
            var exe = ProfileLoader.Parse(preset.Json, T.Devices).Profile!.Match.Exe;
            if (preset.Id == "gta-v") Assert.Contains("GTA5.exe", exe);
            else Assert.Empty(exe);
        }
    }

    [Fact]
    public void Instantiating_applies_the_chosen_name_and_leaves_the_preset_untouched()
    {
        var preset = Presets.All[0];
        var p = Presets.Instantiate(preset, "  My racing  ", T.Devices)!;
        Assert.Equal("My racing", p.Name);
        Assert.Equal(ProfileLoader.Parse(preset.Json, T.Devices).Profile!.Name, ProfileLoader.Parse(preset.Json, T.Devices).Profile!.Name);
        Assert.NotEqual("My racing", ProfileLoader.Parse(preset.Json, T.Devices).Profile!.Name);
    }

    [Fact]
    public void A_preset_round_trips_through_the_profile_writer()
    {
        foreach (var preset in Presets.All)
        {
            var p = Presets.Instantiate(preset, "x", T.Devices)!;
            var again = ProfileLoader.Parse(ProfileWriter.ToJson(p), T.Devices);
            Assert.True(again.Ok, preset.Id);
            Assert.Equal(p.Rules.Count, again.Profile!.Rules.Count);
        }
    }

    [Fact]
    public void The_pedal_button_preset_really_presses_buttons_when_the_pedals_are_pushed()
    {
        var dev = DeviceRegistry.G923Xbox();
        var p = Presets.Instantiate(Presets.All.Single(x => x.Id == "pedals-as-buttons"), "t", T.Devices)!;
        var engine = new MappingEngine(p, dev);
        var s = engine.Evaluate(dev.Normalize(T.Raw(throttle: 0, brake: 65535, clutch: 20000)), 0);   // throttle floored, brake up, clutch ~70%
        Assert.True(s[PadButton.A]);
        Assert.False(s[PadButton.B]);
        Assert.True(s[PadButton.X]);
        Assert.Equal(0, s.RightTrigger);   // pedals are buttons here, not triggers
    }

    [Fact]
    public void Soft_steering_is_softer_in_the_middle_but_still_reaches_full_lock()
    {
        var dev = DeviceRegistry.G923Xbox();
        var soft = new MappingEngine(Presets.Instantiate(Presets.All.Single(x => x.Id == "soft-steering"), "t", T.Devices)!, dev);
        var plain = new MappingEngine(Presets.Instantiate(Presets.All.Single(x => x.Id == "racing"), "t", T.Devices)!, dev);

        int half(MappingEngine e) => e.Evaluate(dev.Normalize(T.Raw(wheel: 32768 + 16384)), 0).LeftX;
        Assert.True(half(soft) < half(plain));
        Assert.Equal(32767, soft.Evaluate(dev.Normalize(T.Raw(wheel: 65535)), 0).LeftX);
    }
}

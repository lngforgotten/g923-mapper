using G923Mapper;

namespace G923Mapper.Tests;

public class GalleryTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();

    [Fact]
    public void Every_gallery_profile_loads_cleanly_and_claims_its_game()
    {
        Assert.NotEmpty(Presets.Gallery);
        foreach (var g in Presets.Gallery)
        {
            var r = ProfileLoader.Parse(g.Json, Devices);
            Assert.True(r.Ok, $"{g.Id}: " + string.Join("; ", r.Errors));
            Assert.Empty(r.Warnings);
            Assert.NotEmpty(r.Profile!.Match.Exe);
            Assert.True(g.Description.Length > 30);
        }
    }

    [Fact]
    public void Ids_are_unique_across_the_templates_and_the_gallery()
    {
        var all = Presets.All.Concat(Presets.Gallery).Select(p => p.Id).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void Gallery_profiles_that_send_keys_are_standard_mode_and_never_for_a_game_on_the_anti_cheat_list()
    {
        foreach (var g in Presets.Gallery)
        {
            var p = ProfileLoader.Parse(g.Json, Devices).Profile!;
            if (!p.Outputs.Keyboard) continue;
            Assert.Equal("standard", ProfileSafety.Effective(p));
            Assert.True(ProfileSafety.KeyboardAllowed(p, new AntiCheatList(AntiCheatList.Defaults)), g.Id);
        }
    }

    [Fact]
    public void The_wardogs_profile_leans_with_q_and_e_from_the_pedals()
    {
        var p = ProfileLoader.Parse(Presets.Gallery.Single(g => g.Id == "game-wardogs-lean").Json, Devices).Profile!;
        Devices.TryGet("g923_xbox", out var d);
        var frame = new InputFrame();
        frame.Values["clutch"] = 0.5;
        var state = new MappingEngine(p, d, Devices).Evaluate(frame, 0);
        Assert.Contains("Q", state.Keys);
        Assert.DoesNotContain("E", state.Keys);
        frame.Values["clutch"] = 0; frame.Values["throttle"] = 0.5;
        Assert.Contains("E", new MappingEngine(p, d, Devices).Evaluate(frame, 0).Keys);
    }

    [Fact]
    public void Instantiating_a_gallery_profile_keeps_the_game_match_and_takes_the_chosen_name()
    {
        var g = Presets.Gallery[0];
        var p = Presets.Instantiate(g, "  My lean ", Devices)!;
        Assert.Equal("My lean", p.Name);
        Assert.Contains("WardogsClient-Win64-Shipping.exe", p.Match.Exe);
    }
}

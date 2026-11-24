using G923Mapper;

namespace G923Mapper.Tests;

public class AutoPadTests : IDisposable
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();

    public AutoPadTests() => PadKinds.AutoKind = PadKind.Xbox360;
    public void Dispose() => PadKinds.AutoKind = PadKind.Xbox360;

    private static Profile Parse(string pad) =>
        ProfileLoader.Parse("{ \"name\": \"t\", \"outputs\": { " + (pad.Length > 0 ? "\"pad\": \"" + pad + "\"" : "") + " }, \"rules\": [] }", Devices).Profile!;

    [Theory]
    [InlineData(0x046D, 0xC26E, PadKind.Xbox360)]       // Xbox / PC edition
    [InlineData(0x046D, 0xC266, PadKind.DualShock4)]    // PlayStation editions
    [InlineData(0x046D, 0xC267, PadKind.DualShock4)]
    [InlineData(0x046D, 0xC24F, PadKind.Xbox360)]       // a wheel we do not know: the safe default
    [InlineData(0x045E, 0x028E, PadKind.Xbox360)]
    public void Auto_follows_the_edition_of_the_connected_wheel(int vid, int pid, PadKind expected)
    {
        PadKinds.SetFor(WheelEditions.Of(vid, pid));
        Assert.Equal(expected, PadKinds.Of(Parse("auto")));
    }

    [Fact]
    public void A_profile_with_no_choice_or_an_explicit_one_never_changes_with_the_wheel()
    {
        PadKinds.SetFor(WheelEdition.PlayStation);
        Assert.Equal(PadKind.Xbox360, PadKinds.Of(Parse("")));          // every profile that existed before behaves as it did
        Assert.Equal(PadKind.Xbox360, PadKinds.Of(Parse("xbox360")));
        Assert.Equal(PadKind.DualShock4, PadKinds.Of(Parse("ds4")));
        PadKinds.SetFor(WheelEdition.Xbox);
        Assert.Equal(PadKind.DualShock4, PadKinds.Of(Parse("ds4")));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("AUTO")]
    [InlineData("match")]
    [InlineData("wheel")]
    public void Several_spellings_mean_match_the_wheel(string text)
    {
        Assert.True(PadKinds.IsAuto(text));
        Assert.True(PadKinds.TryParse(text, out _));
    }

    [Fact]
    public void The_choice_survives_saving_and_xbox_is_still_left_out_of_the_file()
    {
        Assert.Contains("\"pad\": \"auto\"", ProfileWriter.ToJson(Parse("auto")));
        Assert.Contains("\"pad\": \"ds4\"", ProfileWriter.ToJson(Parse("ds4")));
        Assert.DoesNotContain("\"pad\"", ProfileWriter.ToJson(Parse("xbox360")));
        Assert.DoesNotContain("\"pad\"", ProfileWriter.ToJson(Parse("")));
        PadKinds.SetFor(WheelEdition.PlayStation);   // and it stays "auto" in the file whatever it means right now
        Assert.Equal("auto", PadKinds.Normalise(ProfileLoader.Parse(ProfileWriter.ToJson(Parse("auto")), Devices).Profile!));
        Assert.Contains("\"pad\": \"auto\"", ProfileWriter.ToJson(Parse("auto")));
    }

    [Fact]
    public void The_profiles_and_templates_that_come_with_the_app_and_new_ones_follow_the_wheel()
    {
        string dir = Path.Combine(Path.GetTempPath(), "g923-auto-" + Guid.NewGuid().ToString("N"));
        try
        {
            DefaultProfiles.EnsureWritten(dir);
            StarterTemplates.WriteMissing(dir);
            var files = Directory.GetFiles(dir, "*.json");
            Assert.NotEmpty(files);
            foreach (string file in files)
            {
                var r = ProfileLoader.LoadFile(file, Devices);
                Assert.True(r.Ok, file);
                Assert.True(PadKinds.IsAuto(r.Profile!.Outputs.Pad), Path.GetFileName(file));
            }
        }
        finally { Directory.Delete(dir, true); }
        foreach (var preset in Presets.All)
            Assert.True(PadKinds.IsAuto(ProfileLoader.Parse(preset.Json, Devices).Profile!.Outputs.Pad), preset.Title);
        Assert.True(PadKinds.IsAuto(new RemapBuilder(Devices.Fallback!).Build("x").Outputs.Pad));
    }

    [Fact]
    public void A_profile_made_from_a_template_follows_the_wheel_and_a_change_of_choice_shows_as_a_difference()
    {
        Profile template = ProfileLoader.Parse(StarterTemplates.All[0].Json, Devices).Profile!;
        Profile child = Templates.CreateChild(template, "G", ["g.exe"]);
        Assert.True(PadKinds.IsAuto(child.Outputs.Pad));
        Assert.Empty(Templates.Diff(child));
        child.Outputs.Pad = "ds4";
        var diff = Assert.Single(Templates.Diff(child), d => d.Key == "outputs.pad");
        Assert.Equal("match the wheel", diff.Template);
        Assert.Equal("DualShock 4", diff.Current);
        var back = ProfileLoader.Parse(ProfileWriter.ToJson(child), Devices).Profile!;
        Assert.Equal("ds4", PadKinds.Normalise(back));
    }

    [Fact]
    public void An_unknown_pad_text_is_still_an_error()
    {
        Assert.False(ProfileLoader.Parse("""{ "name": "t", "outputs": { "pad": "wii" }, "rules": [] }""", Devices).Ok);
    }
}

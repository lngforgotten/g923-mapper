using G923Mapper;

namespace G923Mapper.Tests;

public class NotesTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();

    [Fact]
    public void Notes_survive_saving_and_loading_including_quotes_and_new_lines()
    {
        var p = new Profile { Name = "t", Notes = "anti-deadzone 0.08 felt right in \"GTA\"\nsecond line" };
        var again = ProfileLoader.Parse(ProfileWriter.ToJson(p), Devices).Profile!;
        Assert.Equal(p.Notes, again.Notes);
    }

    [Fact]
    public void A_profile_without_notes_writes_no_notes_field_and_loads_with_none()
    {
        var p = new Profile { Name = "t" };
        Assert.DoesNotContain("notes", ProfileWriter.ToJson(p));
        Assert.Null(ProfileLoader.Parse(ProfileWriter.ToJson(p), Devices).Profile!.Notes);
        Assert.DoesNotContain("notes", ProfileWriter.ToJson(new Profile { Name = "t", Notes = "   " }));
    }

    [Fact]
    public void Notes_do_not_change_what_the_rules_do()
    {
        var plain = ProfileLoader.Parse("""{ "name": "t", "rules": [ { "from": "wheel", "to": "LeftStickX" } ] }""", Devices).Profile!;
        var noted = ProfileLoader.Parse("""{ "name": "t", "notes": "hello", "rules": [ { "from": "wheel", "to": "LeftStickX" } ] }""", Devices).Profile!;
        Assert.Equal(ProfileWriter.ToJson(plain).Replace("  \"notes\": \"hello\",\r\n", ""), ProfileWriter.ToJson(noted).Replace("  \"notes\": \"hello\",\r\n", ""));
    }
}

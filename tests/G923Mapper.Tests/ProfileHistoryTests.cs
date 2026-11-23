using G923Mapper;

namespace G923Mapper.Tests;

public sealed class ProfileHistoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "g923-hist-" + Guid.NewGuid());
    private string Profile => Path.Combine(_dir, "profiles", "gta-v.json");
    private string Root => Path.Combine(_dir, "history");

    public ProfileHistoryTests() => Directory.CreateDirectory(Path.GetDirectoryName(Profile)!);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private void Write(string text) => File.WriteAllText(Profile, text);

    [Fact]
    public void Saving_over_a_profile_keeps_the_old_version()
    {
        Write("one");
        var snap = ProfileHistory.Snapshot(Profile, Root, new DateTime(2026, 1, 1, 10, 0, 0));
        Assert.NotNull(snap);
        Assert.Equal("one", File.ReadAllText(snap!));
        var list = ProfileHistory.List(Profile, Root);
        Assert.Single(list);
        Assert.Equal(new DateTime(2026, 1, 1, 10, 0, 0), list[0].When);
    }

    [Fact]
    public void Nothing_is_added_when_the_file_has_not_changed_since_the_last_snapshot()
    {
        Write("same");
        Assert.NotNull(ProfileHistory.Snapshot(Profile, Root, new DateTime(2026, 1, 1, 10, 0, 0)));
        Assert.Null(ProfileHistory.Snapshot(Profile, Root, new DateTime(2026, 1, 1, 10, 5, 0)));
        Assert.Single(ProfileHistory.List(Profile, Root));
    }

    [Fact]
    public void A_missing_file_has_nothing_to_snapshot()
    {
        Assert.Null(ProfileHistory.Snapshot(Profile, Root));
        Assert.Empty(ProfileHistory.List(Profile, Root));
    }

    [Fact]
    public void Versions_are_listed_newest_first()
    {
        Write("a"); ProfileHistory.Snapshot(Profile, Root, new DateTime(2026, 1, 1, 9, 0, 0));
        Write("b"); ProfileHistory.Snapshot(Profile, Root, new DateTime(2026, 1, 1, 11, 0, 0));
        Write("c"); ProfileHistory.Snapshot(Profile, Root, new DateTime(2026, 1, 1, 10, 0, 0));
        var times = ProfileHistory.List(Profile, Root).Select(e => e.When.Hour).ToList();
        Assert.Equal([11, 10, 9], times);
    }

    [Fact]
    public void Only_the_newest_few_are_kept()
    {
        for (int i = 0; i < 8; i++)
        {
            Write("v" + i);
            ProfileHistory.Snapshot(Profile, Root, new DateTime(2026, 1, 1, 10, i, 0), keep: 5);
        }
        var list = ProfileHistory.List(Profile, Root);
        Assert.Equal(5, list.Count);
        Assert.Equal("v7", File.ReadAllText(list[0].Path));
        Assert.Equal("v3", File.ReadAllText(list[^1].Path));
    }

    [Fact]
    public void Two_saves_in_the_same_instant_do_not_overwrite_each_other()
    {
        var t = new DateTime(2026, 1, 1, 10, 0, 0);
        Write("x"); ProfileHistory.Snapshot(Profile, Root, t);
        Write("y"); ProfileHistory.Snapshot(Profile, Root, t);
        Assert.Equal(2, ProfileHistory.List(Profile, Root).Count);
    }

    [Fact]
    public void Restoring_puts_the_old_text_back_and_keeps_what_it_replaced()
    {
        Write("original");
        ProfileHistory.Snapshot(Profile, Root, new DateTime(2026, 1, 1, 9, 0, 0));
        Write("edited");
        var old = ProfileHistory.List(Profile, Root).Single();

        Assert.True(ProfileHistory.Restore(old, Profile, Root));
        Assert.Equal("original", File.ReadAllText(Profile));

        var after = ProfileHistory.List(Profile, Root);
        Assert.Equal(2, after.Count);                                  // the edit is saved too, so the restore can be undone
        Assert.Contains(after, e => File.ReadAllText(e.Path) == "edited");
    }

    [Fact]
    public void Restoring_a_version_that_has_gone_fails_quietly()
    {
        Write("x");
        Assert.False(ProfileHistory.Restore(new HistoryEntry(Path.Combine(_dir, "nope.json"), DateTime.Now, 0), Profile, Root));
        Assert.Equal("x", File.ReadAllText(Profile));
    }

    [Fact]
    public void Each_profile_has_its_own_history()
    {
        Write("g");
        ProfileHistory.Snapshot(Profile, Root);
        string other = Path.Combine(_dir, "profiles", "wardogs.json");
        File.WriteAllText(other, "w");
        ProfileHistory.Snapshot(other, Root);
        Assert.Single(ProfileHistory.List(Profile, Root));
        Assert.Single(ProfileHistory.List(other, Root));
    }

    [Fact]
    public void An_unwritable_history_location_never_stops_a_save()
    {
        Write("x");
        Assert.Null(ProfileHistory.Snapshot(Profile, Path.Combine(_dir, "con:")));   // cannot be created
    }
}

public sealed class ProfileStoreHistoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "g923-storehist-" + Guid.NewGuid());
    public ProfileStoreHistoryTests() => Directory.CreateDirectory(Path.Combine(_dir, "profiles"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private const string Good = """{ "name": "A", "rules": [ { "from": "throttle", "to": "RightTrigger" } ] }""";
    private const string Better = """{ "name": "A", "rules": [ { "from": "throttle", "to": "RightTrigger" }, { "from": "brake", "to": "LeftTrigger" } ] }""";

    [Fact]
    public void Changing_a_profile_file_keeps_the_text_it_had_before_even_when_the_new_text_is_broken()
    {
        string profiles = Path.Combine(_dir, "profiles"), history = Path.Combine(_dir, "history");
        string file = Path.Combine(profiles, "a.json");
        File.WriteAllText(file, Good);
        using var store = new ProfileStore(profiles, DeviceRegistry.CreateDefault()) { HistoryRoot = history };

        File.WriteAllText(file, Better); store.Reload("a.json");
        File.WriteAllText(file, "{ this is not json"); store.Reload("a.json");

        var versions = ProfileHistory.List(file, history).Select(e => File.ReadAllText(e.Path)).ToList();
        Assert.Contains(Good, versions);
        Assert.Contains(Better, versions);                  // the good one that the broken edit replaced is not lost
        Assert.Equal(2, versions.Count);
    }

    [Fact]
    public void A_reload_that_changes_nothing_adds_no_version()
    {
        string profiles = Path.Combine(_dir, "profiles"), history = Path.Combine(_dir, "history");
        string file = Path.Combine(profiles, "a.json");
        File.WriteAllText(file, Good);
        using var store = new ProfileStore(profiles, DeviceRegistry.CreateDefault()) { HistoryRoot = history };
        store.Reload("a.json"); store.Reload("a.json");
        Assert.Empty(ProfileHistory.List(file, history));
    }

    [Fact]
    public void Without_a_history_folder_nothing_is_written()
    {
        string profiles = Path.Combine(_dir, "profiles");
        string file = Path.Combine(profiles, "a.json");
        File.WriteAllText(file, Good);
        using var store = new ProfileStore(profiles, DeviceRegistry.CreateDefault());
        File.WriteAllText(file, Better); store.Reload("a.json");
        Assert.False(Directory.Exists(Path.Combine(_dir, "history")));
    }
}

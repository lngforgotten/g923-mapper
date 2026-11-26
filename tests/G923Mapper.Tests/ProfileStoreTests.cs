using G923Mapper;

namespace G923Mapper.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "g923-store-" + Guid.NewGuid());

    public ProfileStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static string Json(string name, double deadzone) => $$"""
        { "name": "{{name}}", "device": "g923_xbox",
          "rules": [ { "from": "wheel", "to": "LeftStickX", "deadzone": {{deadzone}} } ] }
        """;

    private string Write(string file, string content)
    {
        string path = Path.Combine(_dir, file);
        File.WriteAllText(path, content);
        return path;
    }

    private ProfileStore Open() => new(_dir, T.Devices) { Debounce = TimeSpan.FromMilliseconds(50) };

    private static bool WaitFor(Func<bool> condition, int ms = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            if (condition()) return true;
            Thread.Sleep(25);
        }
        return condition();
    }

    [Fact]
    public void Loads_existing_profiles_by_file_name_or_profile_name()
    {
        Write("gta-v.json", Json("GTA V", 0));
        using var store = Open();
        Assert.NotNull(store.Get("gta-v"));
        Assert.NotNull(store.Get("gta-v.json"));
        Assert.NotNull(store.Get("GTA V"));
        Assert.Null(store.Get("missing"));
        Assert.Equal(["gta-v.json"], store.Files);
    }

    [Fact]
    public void Hot_reloads_when_the_file_is_saved()
    {
        string path = Write("p.json", Json("p", 0.0));
        using var store = Open();
        int v0 = store.Version;
        Assert.Equal(0.0, store.Get("p")!.Rules[0].Deadzone);

        File.WriteAllText(path, Json("p", 0.25));

        Assert.True(WaitFor(() => store.Version > v0), "version never advanced");
        Assert.Equal(0.25, store.Get("p")!.Rules[0].Deadzone);
    }

    [Fact]
    public void Picks_up_a_new_file_dropped_into_the_folder()
    {
        using var store = Open();
        Assert.Null(store.Get("new"));
        Write("new.json", Json("new", 0));
        Assert.True(WaitFor(() => store.Get("new") is not null));
    }

    [Fact]
    public void A_bad_save_keeps_the_last_good_profile_and_reports_the_errors()
    {
        string path = Write("p.json", Json("p", 0.1));
        using var store = Open();
        var seen = new List<ProfileLoadResult>();
        store.Loaded += (_, r) => { lock (seen) seen.Add(r); };

        File.WriteAllText(path, "{ this is not json");

        Assert.True(WaitFor(() => { lock (seen) return seen.Count > 0; }), "no reload attempt");
        Assert.False(seen[^1].Ok);
        Assert.Equal(0.1, store.Get("p")!.Rules[0].Deadzone);   // still the old one
        Assert.NotEmpty(store.LastResult("p")!.Errors);
    }

    [Fact]
    public void Recovers_once_the_file_is_fixed()
    {
        string path = Write("p.json", Json("p", 0.1));
        using var store = Open();
        File.WriteAllText(path, "{ broken");
        Assert.True(WaitFor(() => store.LastResult("p")!.Errors.Count > 0));

        int v = store.Version;
        File.WriteAllText(path, Json("p", 0.4));

        Assert.True(WaitFor(() => store.Version > v));
        Assert.Equal(0.4, store.Get("p")!.Rules[0].Deadzone);
        Assert.Empty(store.LastResult("p")!.Errors);
    }

    [Fact]
    public void An_invalid_profile_at_startup_is_listed_but_not_served()
    {
        Write("bad.json", """{ "name": "bad", "rules": [ { "from": "nope", "to": "LeftStickX" } ] }""");
        using var store = Open();
        Assert.Null(store.Get("bad"));
        Assert.NotEmpty(store.LastResult("bad")!.Errors);
    }

    [Fact]
    public void Deleting_a_file_removes_the_profile()
    {
        string path = Write("p.json", Json("p", 0));
        using var store = Open();
        File.Delete(path);
        Assert.True(WaitFor(() => store.Get("p") is null));
    }

    [Fact]
    public void Removing_a_file_is_announced_once_so_the_profile_list_can_refresh()
    {
        string path = Write("p.json", Json("p", 0));
        using var store = Open();
        var removed = new List<string>();
        store.Removed += f => { lock (removed) removed.Add(f); };

        File.Delete(path);
        store.Reload("p.json");   // the app reloads at once...
        Thread.Sleep(300);        // ...and the file watcher follows

        lock (removed) Assert.Equal(["p.json"], removed);
        Assert.Empty(store.Files);
    }

    [Fact]
    public void Writes_starter_profiles_without_overwriting_existing_ones()
    {
        Write("default.json", "mine");
        DefaultProfiles.EnsureWritten(_dir);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_dir, "default.json")));
        Assert.True(File.Exists(Path.Combine(_dir, "gta-v.json")));
    }
}

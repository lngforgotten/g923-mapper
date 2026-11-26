using G923Mapper;

namespace G923Mapper.Tests;

public sealed class IgnoredTipsTests : IDisposable
{
    private const string Two = @"USB\VID_045E&PID_028E\02";
    private const string Three = @"USB\VID_045E&PID_028E\03";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "g923-ignored-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "ignored-tips.json");
    /// <summary>A new object each time, as after a restart of the app.</summary>
    private IgnoredTipsFile Open() => new(FilePath);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static VirtualPadInfo Pad(string id, bool connected = false) => new(id, "Xbox 360 Controller", connected ? "Started" : "Disconnected");

    private static HealthItem? Tip(IgnoredTips ignored, params string[] leftovers) =>
        HealthEvaluator.Evaluate(new HealthFacts { VirtualPadsLeftover = ignored.Unignored(leftovers) })
            .SingleOrDefault(i => i.Action == HealthAction.RemoveLeftovers);

    [Fact]
    public void Nothing_is_ignored_to_begin_with_and_the_tip_names_the_devices_to_remove()
    {
        IgnoredTips tips = Open().Read();
        Assert.False(tips.Any);
        Assert.Equal([Two, Three], Tip(tips, Two, Three)!.DeviceIds);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void An_ignored_leftover_stays_quiet_after_a_restart_but_a_new_one_is_mentioned()
    {
        Assert.True(Open().Change(t => t.Ignore([Two])));

        IgnoredTips tips = Open().Read();
        Assert.Null(Tip(tips, Two));
        Assert.Null(Tip(tips, Two.ToLowerInvariant()));
        Assert.Equal([Three], Tip(tips, Two, Three)!.DeviceIds);
    }

    [Fact]
    public void Never_mentioning_leftovers_covers_new_ones_after_a_restart()
    {
        Open().Change(t => t.AllLeftovers = true);
        Assert.Null(Tip(Open().Read(), Two, Three));
    }

    [Fact]
    public void One_can_be_turned_back_on_without_the_others()
    {
        Open().Change(t => t.Ignore([Two, Three]));
        Open().Change(t => t.Restore([Two]));

        IgnoredTips tips = Open().Read();
        Assert.Equal([Three], tips.LeftoverIds);
        Assert.Equal([Two], Tip(tips, Two, Three)!.DeviceIds);
    }

    [Fact]
    public void Reset_turns_everything_back_on()
    {
        Open().Change(t => { t.Ignore([Two, Three]); t.AllLeftovers = true; });
        Open().Change(t => t.Reset());

        IgnoredTips tips = Open().Read();
        Assert.False(tips.Any);
        Assert.Equal("none", tips.Describe());
        Assert.Equal([Two, Three], Tip(tips, Two, Three)!.DeviceIds);
    }

    [Fact]
    public void Ignoring_twice_lists_it_once_and_only_virtual_controllers_are_accepted()
    {
        Open().Change(t => t.Ignore([Two]));
        Open().Change(t => t.Ignore([Two.ToLowerInvariant(), @"USB\VID_046D&PID_C26E\01"]));
        Assert.Equal([Two], Open().Read().LeftoverIds);
    }

    [Fact]
    public void An_entry_gone_from_the_pc_is_forgotten_so_the_same_number_is_mentioned_next_time()
    {
        Open().Change(t => t.Ignore([Two, Three]));

        IgnoredTips tips = Open().ReadFor([Pad(Two, connected: true)]);

        Assert.Equal([Two], tips.LeftoverIds);
        Assert.Equal([Two], Open().Read().LeftoverIds);
    }

    [Fact]
    public void Nothing_is_forgotten_when_windows_gave_no_list()
    {
        Open().Change(t => t.Ignore([Two]));
        Assert.Equal([Two], Open().ReadFor(null).LeftoverIds);
        Assert.Equal([Two], Open().ReadFor([]).LeftoverIds);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{ \"LeftoverIds\": null }")]
    [InlineData("{ \"LeftoverIds\": [ null, \"x\" ] }")]
    public void A_damaged_file_counts_as_nothing_ignored_and_can_be_written_again(string text)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, text);

        Assert.False(Open().Read().Any);
        Assert.True(Open().Change(t => t.Ignore([Two])));
        Assert.Equal([Two], Open().Read().LeftoverIds);
    }

    [Fact]
    public void A_file_that_cannot_be_opened_is_not_written_over()
    {
        Open().Change(t => t.Ignore([Two]));
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.False(Open().Change(t => t.Ignore([Three])));
        Assert.Equal([Two], Open().Read().LeftoverIds);
    }

    [Fact]
    public void Only_entries_that_are_still_unplugged_are_removed()
    {
        var now = new[] { Pad(Two), Pad(Three, connected: true) };
        Assert.Equal([Two], VirtualPadText.StillLeftover([Two, Three, @"USB\VID_045E&PID_028E\04"], now));
    }
}

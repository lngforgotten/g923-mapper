using G923Mapper;

namespace G923Mapper.Tests;

public class DownloadMeterTests
{
    private const long MB = 1024 * 1024;

    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    private static DownloadMeter Started(long total)
    {
        var meter = new DownloadMeter();
        meter.Start(At(0), total);
        return meter;
    }

    [Fact]
    public void Says_how_much_of_how_much_and_how_fast()
    {
        var meter = Started(67 * MB);
        meter.Report(At(1), 4 * MB, 67 * MB);
        meter.Report(At(2), 8 * MB, 67 * MB);

        Assert.Equal("11%, 8 of 67 MB at 4.0 MB/s", meter.Describe(At(2)));
    }

    [Fact]
    public void Claims_no_speed_in_the_first_second()
    {
        var meter = Started(67 * MB);
        meter.Report(At(0.3), 2 * MB, 67 * MB);

        Assert.Equal("2%, 2 of 67 MB", meter.Describe(At(0.3)));
    }

    [Fact]
    public void The_speed_is_the_recent_one_not_the_average_since_the_start()
    {
        var meter = Started(100 * MB);
        for (int s = 1; s <= 10; s++) meter.Report(At(s), s * 5 * MB, 100 * MB);      // 5 MB/s
        for (int s = 11; s <= 20; s++) meter.Report(At(s), 50 * MB + (s - 10) * 102_400, 100 * MB);   // then 100 KB/s

        Assert.EndsWith(" at 100 KB/s", meter.Describe(At(20)));
    }

    [Fact]
    public void Says_it_is_waiting_when_nothing_arrives()
    {
        var meter = Started(67 * MB);
        meter.Report(At(1), 4 * MB, 67 * MB);

        Assert.EndsWith(" at 819 KB/s", meter.Describe(At(5)));     // already slower: the same bytes over a longer time
        Assert.Equal("5%, 4 of 67 MB, waiting for data", meter.Describe(At(6)));
    }

    [Fact]
    public void The_small_download_is_shown_to_one_place()
    {
        var meter = Started(MB + MB / 50);
        meter.Report(At(1), MB / 2, 0);

        Assert.Equal("49%, 0.5 of 1.0 MB at 512 KB/s", meter.Describe(At(1)));
    }

    [Fact]
    public void Without_a_known_size_it_still_says_what_has_arrived()
    {
        var meter = Started(0);
        meter.Report(At(2), 20 * MB, 0);

        Assert.Equal("20 MB at 10.0 MB/s", meter.Describe(At(2)));
    }

    [Fact]
    public void Once_everything_is_in_it_is_being_checked_not_waiting()
    {
        var meter = Started(67 * MB);
        meter.Report(At(2), 67 * MB, 67 * MB);

        Assert.Equal("67 MB received, checking it", meter.Describe(At(30)));
    }

    [Fact]
    public void A_new_download_forgets_the_last_one()
    {
        var meter = Started(67 * MB);
        meter.Report(At(2), 60 * MB, 67 * MB);
        meter.Start(At(50), 67 * MB);

        Assert.Equal("0%, 0 of 67 MB", meter.Describe(At(50)));
    }
}

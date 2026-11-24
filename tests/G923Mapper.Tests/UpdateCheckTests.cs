using G923Mapper;

namespace G923Mapper.Tests;

public class UpdateCheckTests
{
    [Theory]
    [InlineData("owner/name", "owner/name")]
    [InlineData("  owner/name  ", "owner/name")]
    [InlineData("https://github.com/owner/name", "owner/name")]
    [InlineData("https://github.com/owner/name/", "owner/name")]
    [InlineData("github.com/owner/name.git", "owner/name")]
    [InlineData("https://www.github.com/owner/name/releases/tag/v1", "owner/name")]
    [InlineData("my-org/my.repo_2", "my-org/my.repo_2")]
    public void A_repository_is_accepted_in_either_form(string text, string expected) =>
        Assert.Equal(expected, UpdateCheck.NormaliseRepo(text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("just-a-word")]
    [InlineData("a/b/c")]
    [InlineData("https://example.com/owner/name")]
    [InlineData("owner/na me")]
    public void Anything_else_is_not_a_repository(string? text) => Assert.Null(UpdateCheck.NormaliseRepo(text));

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.0", "1.0")]
    [InlineData("release-2.5.1-beta", "2.5.1")]
    [InlineData("V3", "3.0")]
    [InlineData("1.2.3.4", "1.2.3.4")]
    public void Version_numbers_are_read_from_tags(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateCheck.ParseVersion(tag));

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData(null)]
    public void A_tag_without_a_number_has_no_version(string? tag) => Assert.Null(UpdateCheck.ParseVersion(tag));

    [Theory]
    [InlineData("1.0.0", "1.0.1", true)]
    [InlineData("1.0.0", "1.1.0", true)]
    [InlineData("1.9.9", "2.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.2.0", "1.1.9", false)]
    [InlineData("1.0", "1.0.0", false)]        // 1.0 and 1.0.0 are the same release
    [InlineData("1.0.0.0", "1.0.0", false)]
    public void Newer_means_strictly_newer(string current, string latest, bool newer) =>
        Assert.Equal(newer, UpdateCheck.IsNewer(Version.Parse(current), Version.Parse(latest)));

    [Fact]
    public void A_newer_release_is_reported_with_its_page()
    {
        var info = UpdateCheck.Interpret("""{ "tag_name": "v1.4.0", "html_url": "https://github.com/o/n/releases/tag/v1.4.0" }""", new Version(1, 0, 0), "o/n");
        Assert.True(info.Newer);
        Assert.Equal("v1.4.0", info.LatestTag);
        Assert.Equal("https://github.com/o/n/releases/tag/v1.4.0", info.Url);
        Assert.Contains("newer version", info.Message);
    }

    [Fact]
    public void The_same_version_says_you_are_up_to_date()
    {
        var info = UpdateCheck.Interpret("""{ "tag_name": "v1.0.0", "html_url": "https://github.com/o/n/releases/tag/v1.0.0" }""", new Version(1, 0, 0), "o/n");
        Assert.False(info.Newer);
        Assert.Contains("up to date", info.Message);
    }

    [Fact]
    public void A_missing_or_odd_answer_never_crashes_and_says_so()
    {
        Assert.Contains("no releases", UpdateCheck.Interpret("{}", new Version(1, 0), "o/n").Message);
        Assert.Contains("no version number", UpdateCheck.Interpret("""{ "tag_name": "nightly" }""", new Version(1, 0), "o/n").Message);
        Assert.Contains("could not be read", UpdateCheck.Interpret("<html>rate limited</html>", new Version(1, 0), "o/n").Message);
        Assert.Equal("https://github.com/o/n/releases", UpdateCheck.Interpret("{}", new Version(1, 0), "o/n").Url);
    }

    [Fact]
    public void The_api_address_is_the_latest_release_of_the_repository() =>
        Assert.Equal("https://api.github.com/repos/o/n/releases/latest", UpdateCheck.ApiUrl("o/n"));
}

public class UpdateDueTests
{
    private static readonly TimeSpan Gap = TimeSpan.FromHours(20);
    private static readonly DateTime Now = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Never_looked_or_an_unreadable_time_means_it_is_due()
    {
        Assert.True(UpdateCheck.Due(null, Now, Gap));
        Assert.True(UpdateCheck.Due("", Now, Gap));
        Assert.True(UpdateCheck.Due("not a time", Now, Gap));
    }

    [Fact]
    public void It_is_due_again_only_after_the_gap()
    {
        Assert.False(UpdateCheck.Due(Now.AddHours(-1).ToString("o"), Now, Gap));
        Assert.False(UpdateCheck.Due(Now.AddHours(-19).ToString("o"), Now, Gap));
        Assert.True(UpdateCheck.Due(Now.AddHours(-20).ToString("o"), Now, Gap));
        Assert.True(UpdateCheck.Due(Now.AddDays(-3).ToString("o"), Now, Gap));
    }

    [Fact]
    public void A_last_look_in_the_future_after_a_clock_change_does_not_stop_it_looking()
    {
        Assert.True(UpdateCheck.Due(Now.AddDays(2).ToString("o"), Now, Gap));
    }
}

public class UpdateTimingTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan LaunchGap = TimeSpan.FromMinutes(10), RunningGap = TimeSpan.FromHours(6);

    [Fact]
    public void Every_start_looks_unless_a_check_was_made_in_the_last_few_minutes()
    {
        Assert.True(UpdateCheck.Due(Now.AddHours(-3).ToString("o"), Now, LaunchGap));      // reopened hours later: look
        Assert.True(UpdateCheck.Due(Now.AddMinutes(-11).ToString("o"), Now, LaunchGap));
        Assert.False(UpdateCheck.Due(Now.AddMinutes(-3).ToString("o"), Now, LaunchGap));   // closed and reopened straight away: do not ask again
        Assert.True(UpdateCheck.Due(null, Now, LaunchGap));                                // never checked
    }

    [Fact]
    public void While_running_it_looks_again_every_six_hours()
    {
        Assert.False(UpdateCheck.Due(Now.AddHours(-5).ToString("o"), Now, RunningGap));
        Assert.True(UpdateCheck.Due(Now.AddHours(-6).ToString("o"), Now, RunningGap));
    }
}

public class UpdateRepositoryDefaultTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_update_box_means_the_default_repository(string? text) =>
        Assert.Equal(UpdateCheck.DefaultRepo, UpdateCheck.RepoOrDefault(text));

    [Fact]
    public void Something_typed_is_used_as_it_is_and_the_default_is_a_valid_repository()
    {
        Assert.Equal("a/b", UpdateCheck.RepoOrDefault("  a/b "));
        Assert.Equal(UpdateCheck.DefaultRepo, UpdateCheck.NormaliseRepo(UpdateCheck.DefaultRepo));
        Assert.Equal("lngforgotten/g923-mapper", UpdateCheck.NormaliseRepo(UpdateCheck.RepoOrDefault("")));
    }

    [Fact]
    public void Something_that_is_not_a_repository_is_not_replaced_by_the_default()
    {
        Assert.Null(UpdateCheck.NormaliseRepo(UpdateCheck.RepoOrDefault("not a repo")));
    }
}

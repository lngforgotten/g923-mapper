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

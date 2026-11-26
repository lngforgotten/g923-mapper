namespace G923Mapper.Tests;

public class ProfileChecksTests
{
    [Theory]
    [InlineData(0, 0, "all good")]
    [InlineData(1, 0, "1 problem")]
    [InlineData(3, 0, "3 problems")]
    [InlineData(0, 1, "1 to check")]
    [InlineData(0, 4, "4 to check")]
    [InlineData(2, 5, "2 problems")]
    public void Summary_counts_the_most_serious_kind(int problems, int warnings, string expected) =>
        Assert.Equal(expected, ProfileChecks.Summary(problems, warnings));
}

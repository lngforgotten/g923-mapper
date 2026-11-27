using G923Mapper;

namespace G923Mapper.Tests;

public class RestartArgumentsTests
{
    [Fact]
    public void A_copy_started_with_Windows_comes_back_with_its_window_when_that_was_showing()
    {
        Assert.Empty(UpdatePackage.RestartArguments(["--minimized"], windowShowing: true));
    }

    [Fact]
    public void It_stays_in_the_tray_when_the_window_was_closed_to_it()
    {
        Assert.Equal(["--minimized"], UpdatePackage.RestartArguments([], windowShowing: false));
        Assert.Equal(["--minimized"], UpdatePackage.RestartArguments(["--minimized"], windowShowing: false));
    }

    [Fact]
    public void Other_arguments_are_kept()
    {
        Assert.Equal(["--instance", "2"], UpdatePackage.RestartArguments(["--instance", "2", "--minimized"], windowShowing: true));
    }
}

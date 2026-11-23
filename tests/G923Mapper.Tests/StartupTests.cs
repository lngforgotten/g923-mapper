using G923Mapper;

namespace G923Mapper.Tests;

public sealed class StartupTests : IDisposable
{
    // A unique value name so the test can never touch a real "G923Mapper" startup entry.
    private readonly string _name = "G923MapperTest-" + Guid.NewGuid().ToString("N");
    public void Dispose() => StartupRegistration.Set(false, "unused", _name);

    [Fact]
    public void The_command_starts_the_app_minimised_and_quotes_paths_with_spaces()
        => Assert.Equal(@"""C:\Program Files\G923 Mapper\G923Mapper.exe"" --minimized",
                        StartupRegistration.Command(@"C:\Program Files\G923 Mapper\G923Mapper.exe"));

    [Fact]
    public void Enabling_then_disabling_round_trips_through_the_registry()
    {
        Assert.False(StartupRegistration.IsEnabled(_name));
        StartupRegistration.Set(true, @"C:\Apps\G923Mapper.exe", _name);
        Assert.True(StartupRegistration.IsEnabled(_name));
        Assert.Equal(@"""C:\Apps\G923Mapper.exe"" --minimized", StartupRegistration.CurrentCommand(_name));
        StartupRegistration.Set(false, @"C:\Apps\G923Mapper.exe", _name);
        Assert.False(StartupRegistration.IsEnabled(_name));
    }

    [Fact]
    public void Disabling_when_it_was_never_enabled_is_fine()
        => StartupRegistration.Set(false, "x", _name);

    [Fact]
    public void Enabling_twice_just_updates_the_path()
    {
        StartupRegistration.Set(true, @"C:\Old\G923Mapper.exe", _name);
        StartupRegistration.Set(true, @"C:\New\G923Mapper.exe", _name);
        Assert.Contains(@"C:\New\", StartupRegistration.CurrentCommand(_name));
    }
}

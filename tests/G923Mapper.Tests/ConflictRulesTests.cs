using G923Mapper;

namespace G923Mapper.Tests;

public class ConflictRulesTests
{
    private static GameFolder Folder(params (string, long)[] files) => new("game.exe", @"D:\Games\Game", files);

    [Fact]
    public void A_clean_system_reports_nothing()
    {
        Assert.Empty(ConflictRules.Evaluate(["explorer", "chrome"], [Folder(("game.exe", 50_000_000), ("xinput1_3.dll", 110_000))]));
    }

    [Theory]
    [InlineData("x360ce.ini")]
    [InlineData("X360CE.EXE")]
    [InlineData("x360ce.dll")]
    public void An_x360ce_settings_file_or_program_in_the_game_folder_is_a_warning(string file)
    {
        var items = ConflictRules.Evaluate([], [Folder((file, 5000))]);
        var item = Assert.Single(items);
        Assert.Equal(HealthLevel.Warning, item.Level);
        Assert.Contains(file, item.Detail);
        Assert.Contains("game.exe", item.Title);
        Assert.Contains(@"D:\Games\Game", item.Detail);
    }

    [Fact]
    public void A_large_xinput_dll_is_x360ce_but_a_normal_sized_one_is_left_alone()
    {
        Assert.Single(ConflictRules.Evaluate([], [Folder(("xinput1_3.dll", 3_500_000))]));
        Assert.Empty(ConflictRules.Evaluate([], [Folder(("xinput1_3.dll", 110_000))]));
        Assert.Empty(ConflictRules.Evaluate([], [Folder(("xinput1_4.dll", 999_999))]));
        Assert.Single(ConflictRules.Evaluate([], [Folder(("XINPUT9_1_0.DLL", 1_000_000))]));
    }

    [Fact]
    public void Several_x360ce_files_in_one_folder_make_one_finding_naming_them_all()
    {
        var items = ConflictRules.Evaluate([], [Folder(("x360ce.ini", 5000), ("xinput1_3.dll", 4_000_000), ("readme.txt", 10))]);
        var item = Assert.Single(items);
        Assert.Contains("x360ce.ini", item.Detail);
        Assert.Contains("xinput1_3.dll", item.Detail);
        Assert.DoesNotContain("readme.txt", item.Detail);
    }

    [Theory]
    [InlineData("x360ce", "x360ce is running", HealthLevel.Warning)]
    [InlineData("X360CE.exe", "x360ce is running", HealthLevel.Warning)]
    [InlineData("DS4Windows", "DS4Windows is running", HealthLevel.Warning)]
    [InlineData("reWASD", "reWASD is running", HealthLevel.Warning)]
    [InlineData("JoyToKey", "JoyToKey is running", HealthLevel.Info)]
    [InlineData("xpadder", "Xpadder is running", HealthLevel.Info)]
    public void Other_programs_that_remap_controllers_are_reported(string process, string title, HealthLevel level)
    {
        var item = Assert.Single(ConflictRules.Evaluate([process], []));
        Assert.Equal(title, item.Title);
        Assert.Equal(level, item.Level);
    }

    [Fact]
    public void The_same_program_with_two_process_names_is_reported_once()
    {
        Assert.Single(ConflictRules.Evaluate(["rewasd", "rewasdservice"], []));
        Assert.Single(ConflictRules.Evaluate(["x360ce", "x360ce_x64"], []));
    }

    [Fact]
    public void Steam_gets_a_gentle_tip_only()
    {
        var item = Assert.Single(ConflictRules.Evaluate(["steam"], []));
        Assert.Equal(HealthLevel.Info, item.Level);
        Assert.Contains("Steam Input", item.Detail);
        Assert.Single(ConflictRules.Evaluate(["steam", "steamwebhelper"], []));   // not twice
    }

    [Fact]
    public void Findings_flow_into_the_health_check_and_make_the_badge_say_so()
    {
        var facts = new HealthFacts
        {
            ViGEmInstalled = true, ViGEmRunning = true, MapperRunning = true, WheelFound = true, WheelReporting = true,
            Conflicts = ConflictRules.Evaluate(["ds4windows"], []),
        };
        var items = HealthEvaluator.Evaluate(facts);
        Assert.Contains(items, i => i.Title == "DS4Windows is running" && i.Level == HealthLevel.Warning);
        Assert.Contains("to check", HealthEvaluator.Summary(items));
    }
}

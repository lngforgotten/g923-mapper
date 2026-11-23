using G923Mapper;

namespace G923Mapper.Tests;

public class HealthTests
{
    /// <summary>A healthy setup; each test changes one thing.</summary>
    private static HealthFacts Good(Action<Dictionary<string, object>>? change = null) => Make(new Dictionary<string, object>
    {
        ["ViGEmInstalled"] = true, ["ViGEmRunning"] = true, ["MapperRunning"] = true,
        ["WheelFound"] = true, ["WheelName"] = "G923", ["WheelReporting"] = true,
        ["HidHideInstalled"] = true, ["WheelHidden"] = true,
    }, change);

    private static HealthFacts Make(Dictionary<string, object> v, Action<Dictionary<string, object>>? change)
    {
        change?.Invoke(v);
        T Get<T>(string k, T d = default!) => v.TryGetValue(k, out var o) ? (T)o : d;
        return new HealthFacts
        {
            ViGEmInstalled = Get<bool>("ViGEmInstalled"), ViGEmRunning = Get<bool>("ViGEmRunning"),
            MapperRunning = Get<bool>("MapperRunning"), MapperError = Get<bool>("MapperError"), MapperStatus = Get("MapperStatus", ""),
            WheelFound = Get<bool>("WheelFound"), WheelName = Get("WheelName", ""), WheelReporting = Get<bool>("WheelReporting"),
            FfbEnabled = Get<bool>("FfbEnabled"), FfbSupported = Get<bool>("FfbSupported"), FfbActive = Get<bool>("FfbActive"), FfbMessage = Get("FfbMessage", ""),
            HidHideInstalled = Get<bool>("HidHideInstalled"), WheelHidden = Get<bool>("WheelHidden"),
            GHubRunning = Get<bool>("GHubRunning"), AntiCheatRunning = Get<bool>("AntiCheatRunning"), KeyboardOutputActive = Get<bool>("KeyboardOutputActive"),
            InvalidProfiles = Get<string[]>("InvalidProfiles", []), MissingExtraDevices = Get<string[]>("MissingExtraDevices", []),
        };
    }

    private static HealthItem? Find(IReadOnlyList<HealthItem> items, string titlePart) =>
        items.FirstOrDefault(i => i.Title.Contains(titlePart, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void A_healthy_setup_has_nothing_to_worry_about()
    {
        var items = HealthEvaluator.Evaluate(Good());
        Assert.Equal(HealthLevel.Ok, HealthEvaluator.Overall(items));
        Assert.Equal("All good", HealthEvaluator.Summary(items));
        Assert.All(items, i => Assert.Equal(HealthLevel.Ok, i.Level));
        Assert.Contains(items, i => i.Title.Contains("hidden"));
    }

    [Fact]
    public void Missing_ViGEmBus_is_a_problem_with_a_way_to_get_it()
    {
        var items = HealthEvaluator.Evaluate(Good(v => v["ViGEmInstalled"] = false));
        var item = Find(items, "driver is missing")!;
        Assert.Equal(HealthLevel.Problem, item.Level);
        Assert.Equal(HealthAction.GetViGEmBus, item.Action);
        Assert.Equal("1 problem", HealthEvaluator.Summary(items));
    }

    [Fact]
    public void A_stopped_ViGEmBus_service_is_a_problem()
        => Assert.Equal(HealthLevel.Problem, Find(HealthEvaluator.Evaluate(Good(v => v["ViGEmRunning"] = false)), "not running")!.Level);

    [Fact]
    public void Problems_sort_before_warnings_before_tips_before_ok()
    {
        var items = HealthEvaluator.Evaluate(Good(v =>
        {
            v["ViGEmInstalled"] = false; v["GHubRunning"] = true; v["InvalidProfiles"] = new[] { "gta-v" };
        }));
        var levels = items.Select(i => (int)i.Level).ToList();
        Assert.Equal(levels.OrderByDescending(x => x), levels);
        Assert.Equal(HealthLevel.Problem, items[0].Level);
    }

    [Fact]
    public void A_mapper_error_shows_its_message_and_offers_the_log()
    {
        var item = Find(HealthEvaluator.Evaluate(Good(v => { v["MapperError"] = true; v["MapperStatus"] = "boom"; })), "hit a problem")!;
        Assert.Equal("boom", item.Detail);
        Assert.Equal(HealthAction.OpenLogs, item.Action);
    }

    [Fact]
    public void Wheel_states_are_distinguished()
    {
        Assert.Equal(HealthLevel.Warning, Find(HealthEvaluator.Evaluate(Good(v => v["WheelFound"] = false)), "not found")!.Level);
        Assert.Equal(HealthLevel.Info, Find(HealthEvaluator.Evaluate(Good(v => v["WheelReporting"] = false)), "quiet")!.Level);
        Assert.Equal(HealthLevel.Ok, Find(HealthEvaluator.Evaluate(Good()), "Wheel is working")!.Level);
    }

    [Fact]
    public void A_stopped_mapper_says_so_and_does_not_complain_about_the_wheel()
    {
        var items = HealthEvaluator.Evaluate(Good(v => { v["MapperRunning"] = false; v["WheelFound"] = false; }));
        Assert.NotNull(Find(items, "stopped"));
        Assert.Null(Find(items, "Wheel not found"));
    }

    [Fact]
    public void Force_feedback_is_only_reported_when_it_is_switched_on()
    {
        Assert.Null(Find(HealthEvaluator.Evaluate(Good()), "force feedback"));
        Assert.Equal(HealthLevel.Ok, Find(HealthEvaluator.Evaluate(Good(v => { v["FfbEnabled"] = true; v["FfbSupported"] = true; v["FfbActive"] = true; })), "is on")!.Level);
    }

    [Fact]
    public void Force_feedback_that_cannot_start_explains_why()
    {
        var item = Find(HealthEvaluator.Evaluate(Good(v => { v["FfbEnabled"] = true; v["FfbSupported"] = true; v["FfbMessage"] = "held by G HUB"; })), "could not start")!;
        Assert.Equal(HealthLevel.Warning, item.Level);
        Assert.Equal("held by G HUB", item.Detail);
        // With no specific message it still points at the usual suspect.
        var generic = Find(HealthEvaluator.Evaluate(Good(v => { v["FfbEnabled"] = true; v["FfbSupported"] = true; })), "could not start")!;
        Assert.Contains("G HUB", generic.Detail);
    }

    [Fact]
    public void A_wheel_without_force_feedback_is_just_a_note()
        => Assert.Equal(HealthLevel.Info, Find(HealthEvaluator.Evaluate(Good(v => { v["FfbEnabled"] = true; v["FfbSupported"] = false; })), "No force feedback")!.Level);

    [Fact]
    public void Double_input_advice_depends_on_HidHide()
    {
        var notInstalled = Find(HealthEvaluator.Evaluate(Good(v => { v["HidHideInstalled"] = false; v["WheelHidden"] = false; })), "twice")!;
        Assert.Equal(HealthAction.GetHidHide, notInstalled.Action);
        Assert.Equal(HealthLevel.Info, notInstalled.Level);

        var visible = Find(HealthEvaluator.Evaluate(Good(v => v["WheelHidden"] = false)), "visible to games")!;
        Assert.Equal(HealthAction.OpenSettings, visible.Action);
    }

    [Fact]
    public void G_HUB_is_a_note_not_an_alarm()
        => Assert.Equal(HealthLevel.Info, Find(HealthEvaluator.Evaluate(Good(v => v["GHubRunning"] = true)), "G HUB")!.Level);

    [Fact]
    public void Anti_cheat_is_a_note_but_keyboard_output_with_anti_cheat_is_a_warning()
    {
        var plain = Find(HealthEvaluator.Evaluate(Good(v => v["AntiCheatRunning"] = true)), "BattlEye")!;
        Assert.Equal(HealthLevel.Info, plain.Level);
        Assert.Contains("Story Mode", plain.Detail);

        var keyboard = Find(HealthEvaluator.Evaluate(Good(v => { v["AntiCheatRunning"] = true; v["KeyboardOutputActive"] = true; })), "Keyboard output is on")!;
        Assert.Equal(HealthLevel.Warning, keyboard.Level);
        Assert.Contains("judgement call", keyboard.Detail);
    }

    [Fact]
    public void Invalid_profiles_and_missing_devices_are_listed_by_name()
    {
        var items = HealthEvaluator.Evaluate(Good(v =>
        {
            v["InvalidProfiles"] = new[] { "gta-v", "mine" }; v["MissingExtraDevices"] = new[] { "handbrake" };
        }));
        Assert.Contains("gta-v, mine", Find(items, "profiles have errors")!.Detail);
        Assert.Contains("handbrake", Find(items, "extra device")!.Detail);
    }

    [Theory]
    [InlineData(0, 0, 0, "All good")]
    [InlineData(0, 0, 1, "1 tip")]
    [InlineData(0, 0, 3, "3 tips")]
    [InlineData(0, 1, 2, "1 to check")]
    [InlineData(0, 2, 0, "2 to check")]
    [InlineData(1, 5, 5, "1 problem")]
    [InlineData(2, 0, 0, "2 problems")]
    public void Summary_wording_follows_the_most_serious_level(int problems, int warnings, int tips, string expected)
    {
        var items = new List<HealthItem>();
        items.AddRange(Enumerable.Range(0, problems).Select(i => new HealthItem("p" + i, HealthLevel.Problem, "")));
        items.AddRange(Enumerable.Range(0, warnings).Select(i => new HealthItem("w" + i, HealthLevel.Warning, "")));
        items.AddRange(Enumerable.Range(0, tips).Select(i => new HealthItem("t" + i, HealthLevel.Info, "")));
        items.Add(new HealthItem("ok", HealthLevel.Ok, ""));
        Assert.Equal(expected, HealthEvaluator.Summary(items));
    }
}

public sealed class AppLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "g923-log-" + Guid.NewGuid());
    public void Dispose() { AppLog.Init(Path.Combine(Path.GetTempPath(), "g923-log-unused-" + Guid.NewGuid())); try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Writes_timestamped_lines_to_a_daily_file()
    {
        AppLog.Init(_dir);
        AppLog.Info("hello"); AppLog.Warn("careful"); AppLog.Error("bad", new InvalidOperationException("why"));
        string text = File.ReadAllText(Directory.GetFiles(_dir, "g923mapper-*.log").Single());
        Assert.Contains("[INFO ] hello", text);
        Assert.Contains("[WARN ] careful", text);
        Assert.Contains("[ERROR] bad", text);
        Assert.Contains("InvalidOperationException: why", text);
    }

    [Fact]
    public void Old_logs_are_removed_and_recent_ones_kept()
    {
        Directory.CreateDirectory(_dir);
        string old = Path.Combine(_dir, "g923mapper-20200101.log"), recent = Path.Combine(_dir, "g923mapper-20990101.log");
        File.WriteAllText(old, "x"); File.WriteAllText(recent, "y");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-30));
        AppLog.Init(_dir, keepDays: 7);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void Tail_returns_only_the_last_lines_in_order()
    {
        AppLog.Init(_dir);
        for (int i = 1; i <= 10; i++) AppLog.Info("line " + i);
        var tail = AppLog.Tail(3);
        Assert.Equal(3, tail.Count);
        Assert.EndsWith("line 8", tail[0]);
        Assert.EndsWith("line 10", tail[2]);
        Assert.Empty(AppLog.Tail(0));
    }

    [Fact]
    public void Logging_without_a_folder_or_with_a_bad_one_never_throws()
    {
        AppLog.Init(Path.Combine(_dir, "con:"));   // not creatable
        AppLog.Info("still fine");
    }
}

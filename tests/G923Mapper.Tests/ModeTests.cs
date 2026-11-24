using G923Mapper;

namespace G923Mapper.Tests;

public class ModeTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();
    private static DeviceMap Wheel() => Devices.Fallback!;

    /// <summary>The Wardogs idea: pedals shoot and peek in one mode, and drive in another. One wheel button cycles.</summary>
    private const string Wardogs = """
        { "name": "Wardogs", "match": { "exe": ["wardogs.exe"] }, "safety": "standard", "outputs": { "keyboard": true },
          "force": { "rumble": 40 },
          "rules": [
            { "from": "wheel", "to": "LeftStickX", "deadzone": 0.04 },
            { "from": "button8", "to": "Mode:Next" },
            { "from": "button9", "to": "Button:Start" }
          ],
          "modes": [
            { "name": "Shooting", "force": { "friction": 30 },
              "rules": [ { "from": "clutch", "to": "Key:Q", "threshold": 0.35 }, { "from": "throttle", "to": "Key:E", "threshold": 0.5 }, { "from": "brake", "to": "Key:C", "mode": "toggle" } ] },
            { "name": "Driving", "force": { "rumble": 80, "spring": 15 },
              "rules": [ { "from": "throttle", "to": "RightTrigger" }, { "from": "brake", "to": "LeftTrigger" } ] },
            { "name": "Flying", "removedRules": ["Button:Start"],
              "rules": [ { "from": "wheel", "to": "LeftStickX", "curve": "expo", "exponent": 1.6 }, { "from": "throttle", "to": "RightStickY" } ] }
          ] }
        """;

    private static Profile Parse(string json)
    {
        var r = ProfileLoader.Parse(json, Devices);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        return r.Profile!;
    }

    private static Profile Load() => Parse(Wardogs);

    private static InputFrame Frame(params (string Name, double Value)[] values)
    {
        var f = new InputFrame();
        foreach (var (n, v) in values) f.Values[n] = v;
        return f;
    }

    // ---------------------------------------------------------------- the file

    [Fact]
    public void A_profile_with_modes_loads_with_each_modes_rules_and_feel()
    {
        Profile p = Load();
        Assert.Equal(["Shooting", "Driving", "Flying"], p.Modes.Select(m => m.Name));
        Assert.Equal(3, p.Modes[0].Rules.Count);
        Assert.Equal(30, p.Modes[0].Force!.Friction);
        Assert.Equal(["Button:Start"], p.Modes[2].RemovedRules);
        Assert.Equal(3, p.Rules.Count);   // the profile's own rules
    }

    [Fact]
    public void Modes_survive_saving_and_loading_without_loss()
    {
        Profile p = Load();
        string json = ProfileWriter.ToJson(p);
        Assert.Equal(json, ProfileWriter.ToJson(Parse(json)));
        Profile again = Parse(json);
        Assert.Equal(p.Modes.Count, again.Modes.Count);
        for (int i = 0; i < p.Modes.Count; i++)
        {
            Assert.Equal(p.Modes[i].Name, again.Modes[i].Name);
            Assert.Equal(p.Modes[i].Rules.Select(ProfileWriter.RuleText), again.Modes[i].Rules.Select(ProfileWriter.RuleText));
            Assert.Equal(p.Modes[i].RemovedRules, again.Modes[i].RemovedRules);
        }
    }

    [Fact]
    public void A_profile_without_modes_is_written_exactly_as_before()
    {
        Profile p = Parse("""{ "name": "t", "rules": [ { "from": "wheel", "to": "LeftStickX" } ] }""");
        Assert.DoesNotContain("modes", ProfileWriter.ToJson(p));
        Assert.Empty(p.Modes);
        Assert.Same(p, ProfileModes.Resolve(p, 0));   // nothing to merge
        Assert.Equal(-1, ProfileModes.Resolve(p, "next", 0));
    }

    // ---------------------------------------------------------------- what a mode does to the rules

    [Fact]
    public void A_mode_adds_its_rules_to_the_profiles_own_and_keeps_the_ones_it_does_not_touch()
    {
        Profile shooting = ProfileModes.Resolve(Load(), 0);
        Assert.Empty(shooting.Modes);
        var targets = shooting.Rules.Select(r => r.To).ToList();
        Assert.Contains("LeftStickX", targets);      // the wheel still steers
        Assert.Contains("Mode:Next", targets);       // and the switch button still works
        Assert.Contains("Button:Start", targets);
        Assert.Contains("Key:Q", targets);
        Assert.Contains("Key:E", targets);
        Assert.Contains("Key:C", targets);
        Assert.Equal(6, shooting.Rules.Count);
    }

    [Fact]
    public void A_mode_rule_replaces_the_profiles_rules_for_the_same_output_and_a_mode_can_turn_rules_off()
    {
        Profile flying = ProfileModes.Resolve(Load(), 2);
        var wheel = Assert.Single(flying.Rules, r => r.To == "LeftStickX");
        Assert.Equal("expo", wheel.Curve);                       // the mode's wheel rule won
        Assert.DoesNotContain(flying.Rules, r => r.To == "Button:Start");   // turned off in this mode
        Assert.Contains(flying.Rules, r => r.To == "RightStickY");
        Assert.Contains(flying.Rules, r => r.To == "Mode:Next");
    }

    [Fact]
    public void The_same_pedals_do_different_things_in_different_modes()
    {
        Profile p = Load();
        var shootEngine = new MappingEngine(ProfileModes.Resolve(p, 0), Wheel(), Devices);
        var driveEngine = new MappingEngine(ProfileModes.Resolve(p, 1), Wheel(), Devices);
        var frame = Frame(("throttle", 0.9), ("brake", 0));
        var shooting = shootEngine.Evaluate(frame, 0);
        var driving = driveEngine.Evaluate(frame, 0);
        Assert.Contains("E", shooting.Keys);
        Assert.Equal(0, shooting.RightTrigger);
        Assert.Empty(driving.Keys);
        Assert.True(driving.RightTrigger > 200);
    }

    [Fact]
    public void The_feel_of_a_mode_is_laid_over_the_profiles_feel_for_what_it_sets()
    {
        Profile p = Load();   // profile: rumble 40
        var shooting = ProfileModes.Resolve(p, 0).Force!;
        Assert.Equal(40, shooting.Rumble);      // from the profile
        Assert.Equal(30, shooting.Friction);    // the mode's own
        var driving = ProfileModes.Resolve(p, 1).Force!;
        Assert.Equal(80, driving.Rumble);       // the mode wins
        Assert.Equal(15, driving.Spring);
        Assert.Null(driving.Friction);
        Assert.Equal(40, ProfileModes.Resolve(p, 2).Force!.Rumble);   // a mode that sets nothing keeps the profile's
        // and the engine of the feel: the effective values the wheel is given
        var effective = ForceMath.Effective(new ForceSettings { RumblePercent = 10, SpringPercent = 5 }, ProfileModes.Resolve(p, 1).Force);
        Assert.Equal(80, effective.RumblePercent);
        Assert.Equal(15, effective.SpringPercent);
    }

    [Fact]
    public void Resolving_does_not_change_the_profile_itself()
    {
        Profile p = Load();
        string before = ProfileWriter.ToJson(p);
        ProfileModes.Resolve(p, 2);
        ProfileModes.Resolve(p, 0).Rules.Clear();
        Assert.Equal(before, ProfileWriter.ToJson(p));
    }

    // ---------------------------------------------------------------- switching

    [Fact]
    public void Next_and_previous_go_round_and_a_name_finds_its_mode()
    {
        Profile p = Load();
        Assert.Equal(1, ProfileModes.Resolve(p, "Next", 0));
        Assert.Equal(0, ProfileModes.Resolve(p, "next", 2));       // after the last comes the first
        Assert.Equal(2, ProfileModes.Resolve(p, "Previous", 0));   // and before the first, the last
        Assert.Equal(1, ProfileModes.Resolve(p, "prev", 2));
        Assert.Equal(1, ProfileModes.Resolve(p, "driving", 0));
        Assert.Equal(-1, ProfileModes.Resolve(p, "Sailing", 0));
    }

    [Fact]
    public void After_a_reload_the_same_mode_is_kept_if_it_is_still_there_else_the_first()
    {
        Profile p = Load();
        Assert.Equal(1, ProfileModes.Keep(p, "Driving"));
        Assert.Equal(0, ProfileModes.Keep(p, "Sailing"));
        Assert.Equal(0, ProfileModes.Keep(p, null));
        Assert.Equal(-1, ProfileModes.Keep(Parse("""{ "name": "t", "rules": [] }"""), "Driving"));
    }

    [Fact]
    public void A_press_of_the_switch_button_asks_for_the_next_mode_once_however_long_it_is_held()
    {
        var engine = new MappingEngine(ProfileModes.Resolve(Load(), 0), Wheel(), Devices);
        engine.Evaluate(Frame(("button8", 0)), 0);
        Assert.Null(engine.TakeModeRequest());
        engine.Evaluate(Frame(("button8", 1)), 10);
        Assert.Equal("Next", engine.TakeModeRequest());
        for (int i = 2; i < 20; i++) engine.Evaluate(Frame(("button8", 1)), i * 10);   // held
        Assert.Null(engine.TakeModeRequest());
        engine.Evaluate(Frame(("button8", 0)), 300);
        engine.Evaluate(Frame(("button8", 1)), 310);                                    // pressed again
        Assert.Equal("Next", engine.TakeModeRequest());
    }

    [Fact]
    public void A_switch_button_that_is_still_held_when_the_new_mode_starts_does_not_switch_again()
    {
        Profile p = Load();
        var held = Frame(("button8", 1), ("clutch", 1));
        var next = new MappingEngine(ProfileModes.Resolve(p, 1), Wheel(), Devices);
        next.Prime(held);
        next.Evaluate(held, 0);
        Assert.Null(next.TakeModeRequest());           // not a new press
        next.Evaluate(Frame(("button8", 0)), 10);
        next.Evaluate(Frame(("button8", 1)), 20);
        Assert.Equal("Next", next.TakeModeRequest());  // a real new press still works
    }

    [Fact]
    public void A_pedal_held_down_when_the_mode_changes_does_not_toggle_until_it_is_pressed_again()
    {
        Profile p = Load();
        var held = Frame(("brake", 1));
        var shooting = new MappingEngine(ProfileModes.Resolve(p, 0), Wheel(), Devices);   // brake toggles crouch here
        shooting.Prime(held);
        Assert.DoesNotContain("C", shooting.Evaluate(held, 0).Keys);
        shooting.Evaluate(Frame(("brake", 0)), 10);
        Assert.Contains("C", shooting.Evaluate(Frame(("brake", 1)), 20).Keys);   // a fresh press toggles it on
    }

    [Fact]
    public void A_mode_switch_is_never_sent_to_the_game_as_a_button_or_key()
    {
        var engine = new MappingEngine(ProfileModes.Resolve(Load(), 0), Wheel(), Devices);
        var state = engine.Evaluate(Frame(("button8", 1)), 0);
        Assert.Empty(state.Keys);
        Assert.False(state[PadButton.Start]);
        var reading = engine.Readings().Single(r => r.Rule.To == "Mode:Next");
        Assert.True(reading.IsButton);
        Assert.True(reading.On);
    }

    [Fact]
    public void A_named_mode_rule_switches_straight_to_that_mode()
    {
        Profile p = Parse("""
            { "name": "t", "rules": [ { "from": "button5", "to": "Mode:Driving" }, { "from": "button6", "to": "Mode:Previous" } ],
              "modes": [ { "name": "Shooting", "rules": [] }, { "name": "Driving", "rules": [] } ] }
            """);
        var engine = new MappingEngine(ProfileModes.Resolve(p, 0), Wheel(), Devices);
        engine.Evaluate(Frame(("button5", 1)), 0);
        string request = engine.TakeModeRequest()!;
        Assert.Equal("Driving", request);
        Assert.Equal(1, ProfileModes.Resolve(p, request, 0));
    }

    // ---------------------------------------------------------------- editing a mode

    [Fact]
    public void Changing_the_merged_profile_of_a_mode_and_capturing_it_keeps_only_the_modes_differences()
    {
        Profile p = Load();
        Profile shooting = ProfileModes.Resolve(p, 0);
        shooting.Rules.Single(r => r.To == "Key:Q").Threshold = 0.2;                       // change one of the mode's rules
        shooting.Rules.Add(new Rule { From = "button7", To = "Key:F", Curve = "linear" });  // add one
        shooting.Rules.RemoveAll(r => r.To == "Button:Start");                              // turn off a profile rule here
        shooting.Rules.Single(r => r.To == "LeftStickX").Deadzone = 0.2;                    // and change a profile rule here
        shooting.Force!.Friction = 55;
        ProfileModes.Capture(p, 0, shooting);

        Profile back = ProfileModes.Resolve(Parse(ProfileWriter.ToJson(p)), 0);
        Assert.Equal(0.2, back.Rules.Single(r => r.To == "Key:Q").Threshold);
        Assert.Contains(back.Rules, r => r.To == "Key:F");
        Assert.DoesNotContain(back.Rules, r => r.To == "Button:Start");
        Assert.Equal(0.2, back.Rules.Single(r => r.To == "LeftStickX").Deadzone);
        Assert.Equal(55, back.Force!.Friction);
        // the profile's own rules were not touched, and the other modes are as they were
        Assert.Equal(0.04, p.Rules.Single(r => r.To == "LeftStickX").Deadzone);
        Assert.Contains(p.Rules, r => r.To == "Button:Start");
        Assert.Equal(["Driving", "Flying"], p.Modes.Skip(1).Select(m => m.Name));
        Assert.Equal(2, p.Modes[1].Rules.Count);
    }

    [Fact]
    public void Capturing_an_unchanged_mode_leaves_it_exactly_as_it_was()
    {
        Profile p = Load();
        string before = ProfileWriter.ToJson(p);
        for (int i = 0; i < p.Modes.Count; i++) ProfileModes.Capture(p, i, ProfileModes.Resolve(p, i));
        Assert.Equal(before, ProfileWriter.ToJson(p));
    }

    [Fact]
    public void A_new_mode_starts_with_nothing_of_its_own_and_behaves_like_the_profile()
    {
        Profile p = Load();
        ProfileModes.Add(p, "Hacking");
        Profile merged = ProfileModes.Resolve(p, 3);
        Assert.Equal(p.Rules.Select(ProfileWriter.RuleText), merged.Rules.Select(ProfileWriter.RuleText));
        Assert.Equal("Mode 2", ProfileModes.FreeName(Parse("""{ "name": "t", "rules": [], "modes": [ { "name": "Mode", "rules": [] } ] }"""), "Mode"));
        Assert.Equal("Next 2", ProfileModes.FreeName(p, "Next"));   // a reserved word is not used as a name
    }

    // ---------------------------------------------------------------- bad files

    [Fact]
    public void A_rule_that_switches_to_a_mode_that_does_not_exist_is_an_error()
    {
        var r = ProfileLoader.Parse("""{ "name": "t", "rules": [ { "from": "button5", "to": "Mode:Sailing" } ], "modes": [ { "name": "Shooting", "rules": [] } ] }""", Devices);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Contains("no mode called \"Sailing\""));
    }

    [Theory]
    [InlineData("""[ { "name": "A", "rules": [] }, { "name": "a", "rules": [] } ]""", "same name")]
    [InlineData("""[ { "name": "", "rules": [] } ]""", "needs a name")]
    [InlineData("""[ { "name": "Next", "rules": [] } ]""", "used for switching")]
    [InlineData("""[ { "name": "A:B", "rules": [] } ]""", "colon")]
    [InlineData("""[ { "name": "A", "force": { "friction": 150 }, "rules": [] } ]""", "force.friction")]
    [InlineData("""[ { "name": "A", "rules": [ { "from": "nonsense", "to": "LeftStickX" } ] } ]""", "nonsense")]
    public void Bad_modes_are_refused_with_a_message(string modes, string expected)
    {
        var r = ProfileLoader.Parse("{ \"name\": \"t\", \"rules\": [], \"modes\": " + modes + " }", Devices);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Contains(expected));
    }

    [Fact]
    public void Too_many_modes_are_refused_and_a_removed_rule_that_does_not_exist_is_a_warning()
    {
        string many = string.Join(", ", Enumerable.Range(1, ProfileModes.MaxModes + 1).Select(i => $"{{ \"name\": \"M{i}\", \"rules\": [] }}"));
        Assert.False(ProfileLoader.Parse("{ \"name\": \"t\", \"rules\": [], \"modes\": [ " + many + " ] }", Devices).Ok);
        var warn = ProfileLoader.Parse("""{ "name": "t", "rules": [], "modes": [ { "name": "A", "removedRules": ["Button:A"], "rules": [] } ] }""", Devices);
        Assert.True(warn.Ok);
        Assert.Contains(warn.Warnings, w => w.Contains("Button:A"));
    }

    [Fact]
    public void Key_rules_inside_a_mode_get_the_same_warning_as_anywhere_when_keys_are_off()
    {
        var r = ProfileLoader.Parse("""{ "name": "t", "rules": [], "modes": [ { "name": "A", "rules": [ { "from": "clutch", "to": "Key:Q" } ] } ] }""", Devices);
        Assert.True(r.Ok);
        Assert.Contains(r.Warnings, w => w.Contains("Mode \"A\" rule 1") && w.Contains("keyboard output"));
    }

    [Fact]
    public void Existing_profiles_have_no_modes_and_everything_about_them_is_unchanged()
    {
        Profile p = Parse("""{ "name": "GTA V", "match": { "exe": ["GTA5.exe"] }, "safety": "strict", "rules": [ { "from": "wheel", "to": "LeftStickX", "deadzone": 0.04 } ] }""");
        Assert.Empty(p.Modes);
        Assert.DoesNotContain("modes", ProfileWriter.ToJson(p));
        Assert.Single(new MappingEngine(p, Wheel(), Devices).Readings());   // an engine over it builds as before
    }

    // ---------------------------------------------------------------- modes with templates, sharing and the pictures

    [Fact]
    public void A_profile_based_on_a_template_keeps_its_own_modes_through_a_save()
    {
        Profile template = Parse(StarterTemplates.All.Single(t => t.File == "template-driving.json").Json);
        Profile child = Templates.CreateChild(template, "Wardogs", ["wardogs.exe"]);
        ProfileModes.Add(child, "Shooting").Rules.Add(new Rule { From = "clutch", To = "Key:Q", Curve = "linear" });
        ProfileModes.Add(child, "Driving");
        Profile back = Parse(ProfileWriter.ToJson(child));
        Assert.NotNull(back.Parent);
        Assert.Equal(["Shooting", "Driving"], back.Modes.Select(m => m.Name));
        Assert.Equal("Key:Q", back.Modes[0].Rules.Single().To);
        Assert.Empty(Templates.Diff(back));   // modes are not "differences from the template"
    }

    [Fact]
    public void A_template_made_from_a_profile_with_modes_has_none()
    {
        Profile t = Templates.SaveAsTemplate(Load(), "Mine");
        Assert.Empty(t.Modes);
        Assert.Empty(Templates.Flatten(Load()).Modes);
    }

    [Fact]
    public void Sharing_a_profile_with_modes_keeps_the_modes_and_switches_off_keys_in_all_of_them()
    {
        var import = ProfileShare.Import(ProfileShare.Export(Load(), null), Devices);
        Assert.True(import.Ok, string.Join("; ", import.Errors));
        Profile p = import.Profile!;
        Assert.Equal(["Shooting", "Driving", "Flying"], p.Modes.Select(m => m.Name));
        Assert.False(p.Outputs.Keyboard);
        Assert.Equal("strict", ProfileSafety.Effective(p));
        Assert.Contains(import.TurnedOff, t => t.Contains("3 key or mouse rules"));   // Q, E, C in Shooting
        Assert.Contains("3 modes", import.Summary());
        Assert.Contains("Shooting, Driving, Flying", import.Summary());
    }

    [Fact]
    public void Turbo_inside_a_mode_does_not_survive_an_import()
    {
        var import = ProfileShare.Import("""{ "name": "t", "rules": [], "modes": [ { "name": "A", "rules": [ { "from": "paddleR", "to": "Button:A", "turbo": true } ] } ] }""", Devices);
        Assert.True(import.Ok, string.Join("; ", import.Errors));
        Assert.All(import.Profile!.Modes[0].Rules, r => Assert.False(r.Turbo));
        Assert.Contains("Turbo", import.TurnedOff);
    }

    [Fact]
    public void Unknown_fields_inside_a_mode_are_ignored_with_a_note()
    {
        var import = ProfileShare.Import("""{ "name": "t", "rules": [], "modes": [ { "name": "A", "autoRun": "calc.exe", "rules": [ { "from": "clutch", "to": "Button:A", "onPress": "x" } ] } ] }""", Devices);
        Assert.True(import.Ok);
        Assert.Contains(import.Warnings, w => w.Contains("mode 1.autoRun"));
        Assert.Contains(import.Warnings, w => w.Contains("mode 1 rule 1.onPress"));
    }

    [Fact]
    public void Switching_modes_does_not_appear_on_the_controller_picture()
    {
        Profile p = Parse("""{ "name": "t", "rules": [ { "from": "button8", "to": "Mode:Next" }, { "from": "wheel", "to": "LeftStickX" } ], "modes": [ { "name": "A", "rules": [] } ] }""");
        Assert.Equal(["LeftStick"], PadControls.Mappings(p).Keys);
        Assert.Equal([("wheel", "LeftStick")], PadControls.Flows(p));
    }
}

public class ModeSwitchButtonTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();
    private static DeviceMap Wheel() { Devices.TryGet("g923_xbox", out var d); return d; }

    private static Profile Load(string rules = "") => ProfileLoader.Parse("""
        { "name": "t", "rules": [ { "from": "wheel", "to": "LeftStickX" } ] , "modes": [ { "name": "A", "rules": [] }, { "name": "B", "rules": [] } ] }
        """.Replace("""[ { "from": "wheel", "to": "LeftStickX" } ]""", "[ { \"from\": \"wheel\", \"to\": \"LeftStickX\" }" + (rules.Length > 0 ? ", " + rules : "") + " ]"), Devices).Profile!;

    [Fact]
    public void The_switch_buttons_are_two_slots_that_send_to_next_and_previous_mode()
    {
        var slots = PadControls.Slots(PadControls.ModeSwitchKey, PadKind.Xbox360);
        Assert.Equal(["Mode:Next", "Mode:Previous"], slots.Select(s => s.Target));
        Assert.All(slots, s => Assert.True(Target.TryParse(s.Target, out var t, out _) && t is { Kind: TargetKind.Mode }));
        Assert.Equal("Mode switch buttons", PadControls.Title(PadControls.ModeSwitchKey, PadKind.Xbox360));
    }

    [Fact]
    public void Assigning_a_wheel_button_makes_a_rule_that_loads_cleanly_in_a_profile_with_modes()
    {
        Profile p = Load();
        PadControls.Assign(p, Wheel(), "button8", "Mode:Next");
        var rule = Assert.Single(PadControls.RulesFor(p, "Mode:Next"));
        Assert.Equal("button8", rule.From);
        Assert.Null(rule.Threshold);   // a button needs no trigger point
        var again = ProfileLoader.Parse(ProfileWriter.ToJson(p), Devices);
        Assert.True(again.Ok, string.Join("; ", again.Errors));
        Assert.Equal(2, again.Profile!.Rules.Count);
        Assert.Equal(2, again.Profile.Modes.Count);
    }

    [Fact]
    public void A_pedal_as_the_switch_gets_a_trigger_point_and_choosing_again_replaces_the_old_button()
    {
        Profile p = Load();
        PadControls.Assign(p, Wheel(), "clutch", "Mode:Next");
        Assert.Equal(0.5, PadControls.RulesFor(p, "Mode:Next").Single().Threshold);
        PadControls.Assign(p, Wheel(), "paddleR", "Mode:Next");
        Assert.Equal("paddleR", PadControls.RulesFor(p, "Mode:Next").Single().From);
        PadControls.Assign(p, Wheel(), "paddleL", "Mode:Previous");
        Assert.Equal(2, p.Rules.Count(r => r.To.StartsWith("Mode:")));
    }

    [Fact]
    public void Clearing_removes_the_switch_and_the_wheel_rule_stays()
    {
        Profile p = Load("""{ "from": "button8", "to": "Mode:Next" }""");
        Assert.Equal(1, PadControls.Clear(p, "Mode:Next"));
        Assert.Empty(PadControls.RulesFor(p, "Mode:Next"));
        Assert.Single(p.Rules);
    }

    [Fact]
    public void The_engine_asks_for_the_next_mode_when_the_assigned_button_is_pressed()
    {
        Profile p = Load();
        PadControls.Assign(p, Wheel(), "button8", "Mode:Next");
        var engine = new MappingEngine(ProfileModes.Resolve(p, 0), Wheel(), Devices);
        var f = new InputFrame(); f.Values["button8"] = 1;
        engine.Evaluate(f, 0);
        Assert.Equal("Next", engine.TakeModeRequest());
    }
}

public class ModePresetTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();
    private static DeviceMap Wheel() { Devices.TryGet("g923_xbox", out var d); return d; }
    private static Profile Parse(string json) { var r = ProfileLoader.Parse(json, Devices); Assert.True(r.Ok, string.Join("; ", r.Errors)); return r.Profile!; }

    private static InputFrame Frame(params (string Name, double Value)[] values)
    {
        var f = new InputFrame();
        foreach (var (n, v) in values) f.Values[n] = v;
        return f;
    }

    [Fact]
    public void A_driving_mode_comes_ready_made_with_the_usual_pedals_paddles_and_steering()
    {
        Profile p = Parse("""{ "name": "t", "rules": [] }""");
        ProfileMode m = ModePresets.AddTo(p, "Driving", ModePresets.Driving);
        var map = m.Rules.ToDictionary(r => r.To, r => r.From);
        Assert.Equal("throttle", map["RightTrigger"]);
        Assert.Equal("brake", map["LeftTrigger"]);
        Assert.Equal("paddleL", map["Button:LeftShoulder"]);
        Assert.Equal("paddleR", map["Button:RightShoulder"]);
        Assert.Equal("wheel", map["LeftStickX"]);   // the profile did not steer, so the mode does
        Assert.True(ProfileLoader.Parse(ProfileWriter.ToJson(p), Devices).Ok);
    }

    [Fact]
    public void A_driving_mode_does_not_add_steering_when_the_profile_already_steers()
    {
        Profile p = Parse("""{ "name": "t", "rules": [ { "from": "wheel", "to": "LeftStickX", "deadzone": 0.1 } ] }""");
        ProfileMode m = ModePresets.AddTo(p, "Driving", ModePresets.Driving);
        Assert.DoesNotContain(m.Rules, r => r.To == "LeftStickX");
        Assert.Equal(0.1, ProfileModes.Resolve(p, 0).Rules.Single(r => r.To == "LeftStickX").Deadzone);   // the profile's own steering still rules
    }

    [Fact]
    public void Adding_driving_to_the_wardogs_style_profile_turns_off_the_pedal_keys_in_that_mode_only()
    {
        Profile p = Parse("""
            { "name": "Wardogs", "safety": "standard", "outputs": { "keyboard": true },
              "rules": [ { "from": "wheel", "to": "LeftStickX" }, { "from": "clutch", "to": "Key:Q", "threshold": 0.35 }, { "from": "throttle", "to": "Key:E" }, { "from": "brake", "to": "Key:C" } ] }
            """);
        ModePresets.AddTo(p, "Driving", ModePresets.Driving);
        Assert.Equal(["Key:E", "Key:C"], p.Modes[0].RemovedRules);   // throttle and brake are busy driving (the clutch is not used by driving)
        Profile driving = ProfileModes.Resolve(p, 0);
        var engine = new MappingEngine(driving, Wheel(), Devices);
        var state = engine.Evaluate(Frame(("throttle", 1), ("brake", 1), ("clutch", 1)), 0);
        Assert.Contains("Q", state.Keys);      // the clutch still leans, which driving leaves alone
        Assert.DoesNotContain("E", state.Keys);
        Assert.DoesNotContain("C", state.Keys);
        Assert.True(state.RightTrigger > 200);
        Assert.True(state.LeftTrigger > 200);
    }

    [Fact]
    public void A_shooting_mode_turns_off_the_profiles_pedal_triggers_in_that_mode()
    {
        Profile p = Parse("""{ "name": "t", "rules": [ { "from": "throttle", "to": "RightTrigger" }, { "from": "brake", "to": "LeftTrigger" } ] }""");
        ModePresets.AddTo(p, "Shooting", ModePresets.Shooting);
        Assert.Equal(["RightTrigger", "LeftTrigger"], p.Modes[0].RemovedRules);
        Assert.DoesNotContain(ProfileModes.Resolve(p, 0).Rules, r => r.To == "RightTrigger");
    }

    [Fact]
    public void Rules_that_switch_modes_are_never_turned_off_by_a_preset()
    {
        Profile p = Parse("""{ "name": "t", "rules": [ { "from": "clutch", "to": "Mode:Next" } ], "modes": [ { "name": "A", "rules": [] } ] }""");
        ModePresets.AddTo(p, "Shooting", ModePresets.Shooting);   // uses the clutch for Q
        Assert.Null(p.Modes[1].RemovedRules);
        Assert.Contains(ProfileModes.Resolve(p, 1).Rules, r => r.To == "Mode:Next");
    }

    [Fact]
    public void The_flying_mode_pitches_both_ways_and_a_blank_mode_has_nothing()
    {
        Profile p = Parse("""{ "name": "t", "rules": [ { "from": "wheel", "to": "LeftStickX" } ] }""");
        ModePresets.AddTo(p, "Flying", ModePresets.Flying);
        ModePresets.AddTo(p, "Empty", ModePresets.Blank);
        var engine = new MappingEngine(ProfileModes.Resolve(p, 0), Wheel(), Devices);
        Assert.True(engine.Evaluate(Frame(("throttle", 1)), 0).RightY > 20000);
        Assert.True(engine.Evaluate(Frame(("brake", 1)), 10).RightY < -20000);
        Assert.Empty(p.Modes[1].Rules);
        Assert.Null(p.Modes[1].RemovedRules);
    }

    [Fact]
    public void Naming_a_mode_after_a_preset_finds_that_preset_and_other_names_do_not()
    {
        Assert.Equal(ModePresets.Driving, ModePresets.ForName("driving")?.Id);
        Assert.Equal(ModePresets.Shooting, ModePresets.ForName(" Shooting ")?.Id);
        Assert.Null(ModePresets.ForName("Hacking"));
        Assert.Null(ModePresets.ForName("Blank mode"));
    }

    [Fact]
    public void Only_the_shooting_preset_uses_keys_and_every_preset_loads_cleanly()
    {
        Assert.Equal([ModePresets.Shooting], ModePresets.All.Where(p => p.UsesKeys).Select(p => p.Id));
        foreach (var preset in ModePresets.All)
        {
            Profile p = Parse("""{ "name": "t", "rules": [] }""");
            ModePresets.AddTo(p, preset.Name, preset.Id);
            Assert.True(ProfileLoader.Parse(ProfileWriter.ToJson(p), Devices).Ok, preset.Id);
        }
    }
}

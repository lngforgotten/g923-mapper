using G923Mapper;

namespace G923Mapper.Tests;

public class TemplateTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();

    private static Profile Parse(string json)
    {
        var r = ProfileLoader.Parse(json, Devices);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        return r.Profile!;
    }

    private static Profile Starter(string file) => Parse(StarterTemplates.All.Single(t => t.File == file).Json);
    private static Profile Driving() => Starter("template-driving.json");

    /// <summary>Write a profile to text and read it back, as the app does when it saves and reloads a file.</summary>
    private static Profile Reload(Profile p) => Parse(ProfileWriter.ToJson(p));

    private static Rule RuleTo(Profile p, string to) => p.Rules.Single(r => r.To == to);

    // ---------------------------------------------------------------- the starter templates

    [Fact]
    public void Every_starter_template_loads_cleanly_and_is_marked_as_a_template_with_no_game()
    {
        Assert.Equal(4, StarterTemplates.All.Count);
        foreach (var (file, json) in StarterTemplates.All)
        {
            var r = ProfileLoader.Parse(json, Devices);
            Assert.True(r.Ok, file + ": " + string.Join("; ", r.Errors));
            Assert.Empty(r.Warnings);
            Assert.True(r.Profile!.IsTemplate, file);
            Assert.Empty(r.Profile.Match.Exe);
            Assert.False(string.IsNullOrWhiteSpace(r.Profile.Notes), file);   // every template says what it is for
        }
    }

    [Fact]
    public void The_driving_template_is_wheel_linear_to_the_left_stick_pedals_to_triggers_paddles_to_bumpers()
    {
        Profile t = Driving();
        Assert.Equal("strict", ProfileSafety.Effective(t));
        Assert.Equal("wheel", RuleTo(t, "LeftStickX").From);
        Assert.Equal("linear", RuleTo(t, "LeftStickX").Curve);
        Assert.Equal(0, RuleTo(t, "LeftStickX").AntiDeadzone);   // adjustable, off to begin with
        Assert.Equal("throttle", RuleTo(t, "RightTrigger").From);
        Assert.Equal("brake", RuleTo(t, "LeftTrigger").From);
        Assert.Equal("paddleL", RuleTo(t, "Button:LeftShoulder").From);
        Assert.Equal("paddleR", RuleTo(t, "Button:RightShoulder").From);
    }

    [Theory]
    [InlineData("template-fps-pedals.json", "Key:Q", "Key:E", "Key:C", "toggle")]
    [InlineData("template-rpg-pedals.json", "Mouse:Right", "Key:Shift", "Key:Space", "pulse")]
    public void The_pedal_first_templates_leave_the_wheel_unmapped_and_give_each_pedal_its_own_threshold_and_mode(string file, string clutch, string throttle, string brake, string brakeMode)
    {
        Profile t = Starter(file);
        Assert.DoesNotContain(t.Rules, r => r.From == "wheel");
        Assert.Equal(3, t.Rules.Count);
        Assert.Equal(clutch, t.Rules.Single(r => r.From == "clutch").To);
        Assert.Equal(throttle, t.Rules.Single(r => r.From == "throttle").To);
        Assert.Equal(brake, t.Rules.Single(r => r.From == "brake").To);
        Assert.Equal(brakeMode, t.Rules.Single(r => r.From == "brake").Mode);
        Assert.Equal(3, t.Rules.Select(r => r.Threshold).Distinct().Count());       // set independently
        Assert.All(t.Rules, r => Assert.NotNull(r.Threshold));
    }

    [Fact]
    public void The_flight_template_rolls_with_the_wheel_on_a_gentler_curve_and_the_two_pedals_pitch_both_ways()
    {
        Profile t = Starter("template-flight.json");
        Assert.Equal("expo", RuleTo(t, "LeftStickX").Curve);
        Assert.True(RuleTo(t, "LeftStickX").Exponent > 1);
        var engine = new MappingEngine(t, Devices.Fallback!, Devices);
        InputFrame Frame(double throttle, double brake) { var f = new InputFrame(); f.Values["throttle"] = throttle; f.Values["brake"] = brake; return f; }
        Assert.True(engine.Evaluate(Frame(1, 0), 0).RightY > 20000);     // throttle pitches up
        Assert.True(engine.Evaluate(Frame(0, 1), 10).RightY < -20000);   // brake pitches down
    }

    [Fact]
    public void Starter_templates_are_written_once_and_an_existing_file_is_never_touched()
    {
        string dir = Path.Combine(Path.GetTempPath(), "g923-tpl-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(Path.Combine(dir = Directory.CreateDirectory(dir).FullName, "template-driving.json"), "my own edit");
            var written = StarterTemplates.WriteMissing(dir);
            Assert.Equal(3, written.Count);
            Assert.DoesNotContain("template-driving.json", written);
            Assert.Equal("my own edit", File.ReadAllText(Path.Combine(dir, "template-driving.json")));
            Assert.Empty(StarterTemplates.WriteMissing(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---------------------------------------------------------------- a profile made from a template

    [Fact]
    public void A_new_profile_starts_out_behaving_exactly_like_its_template()
    {
        Profile t = Driving();
        Profile child = Reload(Templates.CreateChild(t, "Forza", ["forza.exe"]));
        Assert.Equal("Forza", child.Name);
        Assert.Equal(["forza.exe"], child.Match.Exe);
        Assert.Equal("Driving", child.Parent!.Name);
        Assert.False(child.IsTemplate);
        Assert.Equal(t.Rules.Select(r => ProfileWriter.RuleText(r)), child.Rules.Select(r => ProfileWriter.RuleText(r)));
        Assert.Empty(Templates.Diff(child));
    }

    [Fact]
    public void A_new_profile_never_starts_with_keys_and_mouse_on_even_when_its_template_uses_them()
    {
        Profile fps = Starter("template-fps-pedals.json");
        Assert.True(fps.Outputs.Keyboard);
        Profile child = Reload(Templates.CreateChild(fps, "Wardogs", ["wardogs.exe"]));
        Assert.False(child.Outputs.Keyboard);
        Assert.Equal("strict", ProfileSafety.Effective(child));
        Assert.Equal(3, child.Rules.Count);   // the rules are there, switched off
        Assert.Equal(KeyOutput.NeedsEnabling, KeyMappings.Status(child).State);
        // ...and that is the profile's own choice, so it shows as a difference that can be undone.
        Assert.Contains(Templates.Diff(child), d => d.Key == "safety");
        Assert.Contains(Templates.Diff(child), d => d.Key == "outputs.keyboard");
    }

    [Fact]
    public void The_profile_file_stores_only_what_differs_from_the_template_plus_the_pinned_copy()
    {
        Profile child = Templates.CreateChild(Driving(), "Forza", ["forza.exe"]);
        RuleTo(child, "RightTrigger").Deadzone = 0.1;
        string json = ProfileWriter.ToJson(child);

        // Only the one changed rule is listed in the file's own "rules"; the other four live in the template copy.
        string own = json[(json.LastIndexOf("\"rules\"", StringComparison.Ordinal))..];
        Assert.Contains("RightTrigger", own);
        Assert.DoesNotContain("LeftStickX", own);
        Assert.DoesNotContain("LeftTrigger", own);
        Assert.DoesNotContain("\"device\"", json[..json.IndexOf("\"parent\"", StringComparison.Ordinal)]);

        Profile back = Parse(json);
        Assert.Equal(0.1, RuleTo(back, "RightTrigger").Deadzone);
        Assert.Equal(5, back.Rules.Count);
        Assert.Equal(ProfileWriter.PlainJson(child), ProfileWriter.PlainJson(back));   // nothing lost in the round trip
    }

    [Fact]
    public void Editing_a_rule_makes_it_an_override_that_is_kept_and_listed_as_a_difference()
    {
        Profile child = Reload(Templates.CreateChild(Driving(), "Forza", ["forza.exe"]));
        RuleTo(child, "LeftStickX").AntiDeadzone = 0.08;
        child = Reload(child);

        var diff = Assert.Single(Templates.Diff(child));
        Assert.Equal(DiffKind.RuleChanged, diff.Kind);
        Assert.Equal("LeftStickX", diff.Key);
        Assert.Contains("8% anti-deadzone", diff.Current);
        Assert.Equal(0.08, RuleTo(child, "LeftStickX").AntiDeadzone);
    }

    [Fact]
    public void A_rule_the_profile_adds_and_a_template_rule_it_removes_both_survive_a_save()
    {
        Profile child = Reload(Templates.CreateChild(Driving(), "Forza", ["forza.exe"]));
        child.Rules.RemoveAll(r => r.To == "Button:RightShoulder");
        child.Rules.Add(new Rule { From = "button7", To = "Button:A", Curve = "linear" });
        child = Reload(child);

        Assert.DoesNotContain(child.Rules, r => r.To == "Button:RightShoulder");
        Assert.Contains(child.Rules, r => r.To == "Button:A" && r.From == "button7");
        Assert.Equal(5, child.Rules.Count);
        var kinds = Templates.Diff(child).ToDictionary(d => d.Key, d => d.Kind);
        Assert.Equal(DiffKind.RuleRemoved, kinds["Button:RightShoulder"]);
        Assert.Equal(DiffKind.RuleAdded, kinds["Button:A"]);
    }

    [Fact]
    public void A_setting_the_profile_changes_is_an_override_and_one_it_leaves_alone_follows_the_template()
    {
        Profile template = Driving();
        template.Force = new ProfileForce { Rumble = 40, Spring = 10 };
        Profile child = Templates.CreateChild(template, "Forza", ["forza.exe"]);
        child.Force!.Spring = 30;
        child = Reload(child);

        Assert.Equal(40, child.Force!.Rumble);   // from the template
        Assert.Equal(30, child.Force.Spring);    // its own
        var diff = Assert.Single(Templates.Diff(child));
        Assert.Equal("force.spring", diff.Key);
        Assert.Equal("10", diff.Template);
        Assert.Equal("30", diff.Current);
    }

    [Fact]
    public void Name_game_and_notes_are_always_the_profiles_own_and_never_the_templates()
    {
        Profile child = Reload(Templates.CreateChild(Driving(), "Forza", ["forza.exe"]));
        Assert.Null(child.Notes);   // the template's "what this is for" does not become the game profile's notes
        child.Notes = "anti-deadzone 0.08 felt right";
        child = Reload(child);
        Assert.Equal("anti-deadzone 0.08 felt right", child.Notes);
        Assert.Equal(["forza.exe"], child.Match.Exe);
        Assert.Empty(Templates.Diff(child));   // notes and game are not "differences from the template"
    }

    // ---------------------------------------------------------------- the template changes

    [Fact]
    public void Changing_a_template_flags_its_profiles_but_changes_nothing_until_apply()
    {
        Profile template = Driving();
        Profile child = Reload(Templates.CreateChild(template, "Forza", ["forza.exe"]));
        Assert.False(Templates.UpdateAvailable(child, template));

        string before = ProfileWriter.PlainJson(child);
        RuleTo(template, "RightTrigger").Deadzone = 0.2;             // the template is edited
        Assert.True(Templates.UpdateAvailable(child, template));      // the badge
        Assert.Equal(before, ProfileWriter.PlainJson(child));          // the profile is untouched
        Assert.Equal(0, RuleTo(Reload(child), "RightTrigger").Deadzone);
    }

    [Fact]
    public void Renaming_a_template_or_rewording_its_note_is_not_an_update()
    {
        Profile template = Driving();
        Profile child = Reload(Templates.CreateChild(template, "Forza", ["forza.exe"]));
        template.Name = "Driving v2";
        template.Notes = "something else";
        Assert.False(Templates.UpdateAvailable(child, template));
        Assert.Equal(Templates.Revision(Driving()), Templates.Revision(template));
    }

    [Fact]
    public void A_profile_with_no_template_or_whose_template_is_gone_is_never_flagged()
    {
        Assert.False(Templates.UpdateAvailable(Parse("""{ "name": "t", "rules": [] }"""), Driving()));
        Profile child = Reload(Templates.CreateChild(Driving(), "Forza", ["forza.exe"]));
        Assert.False(Templates.UpdateAvailable(child, null));
        Assert.False(Templates.UpdateAvailable(child, Parse("""{ "name": "Driving", "rules": [] }""")));   // not a template
    }

    [Fact]
    public void Apply_takes_the_new_template_and_keeps_every_override_the_profile_had()
    {
        Profile template = Driving();
        Profile child = Reload(Templates.CreateChild(template, "Forza", ["forza.exe"]));
        RuleTo(child, "LeftStickX").AntiDeadzone = 0.08;              // an override of the profile's own
        child = Reload(child);

        RuleTo(template, "RightTrigger").Deadzone = 0.2;             // the template changes this...
        RuleTo(template, "LeftStickX").AntiDeadzone = 0.5;           // ...and this, which the profile has overridden
        template.Rules.Add(new Rule { From = "button7", To = "Button:Y", Curve = "linear" });   // and gains a rule
        template.Force = new ProfileForce { Spring = 25 };

        var result = Templates.ApplyUpdate(child, template, Devices);
        Assert.True(result.Ok, string.Join("; ", result.Errors));
        Profile updated = result.Profile!;

        Assert.Equal(0.2, RuleTo(updated, "RightTrigger").Deadzone);       // the template's change arrives
        Assert.Equal(0.08, RuleTo(updated, "LeftStickX").AntiDeadzone);    // the override wins
        Assert.Equal("button7", RuleTo(updated, "Button:Y").From);          // new template rule arrives
        Assert.Equal(25, updated.Force!.Spring);
        Assert.False(Templates.UpdateAvailable(updated, template));         // and the badge goes
        Assert.Equal("Forza", updated.Name);
        Assert.Equal(["forza.exe"], updated.Match.Exe);
        Assert.Equal(["LeftStickX"], Templates.Diff(updated).Where(d => d.Kind == DiffKind.RuleChanged).Select(d => d.Key));
    }

    [Fact]
    public void Apply_keeps_a_rule_the_profile_removed_removed_and_a_rule_it_added_added()
    {
        Profile template = Driving();
        Profile child = Reload(Templates.CreateChild(template, "Forza", ["forza.exe"]));
        child.Rules.RemoveAll(r => r.To == "Button:LeftShoulder");
        child.Rules.Add(new Rule { From = "button9", To = "Button:Start", Curve = "linear" });
        child = Reload(child);
        RuleTo(template, "RightTrigger").Deadzone = 0.1;

        Profile updated = Templates.ApplyUpdate(child, template, Devices).Profile!;
        Assert.DoesNotContain(updated.Rules, r => r.To == "Button:LeftShoulder");
        Assert.Contains(updated.Rules, r => r.To == "Button:Start");
        Assert.Equal(0.1, RuleTo(updated, "RightTrigger").Deadzone);
    }

    [Fact]
    public void Several_profiles_on_one_template_update_independently()
    {
        Profile template = Driving();
        Profile a = Reload(Templates.CreateChild(template, "A", ["a.exe"]));
        Profile b = Reload(Templates.CreateChild(template, "B", ["b.exe"]));
        RuleTo(template, "RightTrigger").Deadzone = 0.3;
        Assert.True(Templates.UpdateAvailable(a, template));
        Assert.True(Templates.UpdateAvailable(b, template));

        Profile a2 = Templates.ApplyUpdate(a, template, Devices).Profile!;
        Assert.False(Templates.UpdateAvailable(a2, template));
        Assert.True(Templates.UpdateAvailable(b, template));                 // B waits until its own Apply
        Assert.Equal(0, RuleTo(b, "RightTrigger").Deadzone);
    }

    // ---------------------------------------------------------------- reset to template

    [Fact]
    public void Reset_puts_one_changed_rule_back_to_the_templates()
    {
        Profile child = Reload(Templates.CreateChild(Driving(), "Forza", ["forza.exe"]));
        RuleTo(child, "LeftStickX").AntiDeadzone = 0.08;
        RuleTo(child, "RightTrigger").Deadzone = 0.1;
        child = Reload(child);

        Templates.Reset(child, Templates.Diff(child).Single(d => d.Key == "LeftStickX"));
        Assert.Equal(0, RuleTo(child, "LeftStickX").AntiDeadzone);
        Assert.Equal(0.1, RuleTo(child, "RightTrigger").Deadzone);           // the other override stays
        Assert.Equal(["RightTrigger"], Templates.Diff(child).Select(d => d.Key));
    }

    [Fact]
    public void Reset_removes_an_added_rule_and_brings_back_a_removed_one_in_its_old_place()
    {
        Profile child = Reload(Templates.CreateChild(Driving(), "Forza", ["forza.exe"]));
        child.Rules.RemoveAll(r => r.To == "LeftTrigger");
        child.Rules.Add(new Rule { From = "button7", To = "Button:A", Curve = "linear" });
        child = Reload(child);

        foreach (var d in Templates.Diff(child).ToList()) Templates.Reset(child, d);
        Assert.Empty(Templates.Diff(child));
        Assert.DoesNotContain(child.Rules, r => r.To == "Button:A");
        Assert.Equal("brake", RuleTo(child, "LeftTrigger").From);
        Assert.Equal(5, child.Rules.Count);
    }

    [Fact]
    public void Reset_puts_a_setting_back_and_drops_an_emptied_force_block()
    {
        Profile template = Driving();
        Profile child = Templates.CreateChild(template, "Forza", ["forza.exe"]);
        child.Force = new ProfileForce { Friction = 40 };
        child.Outputs.SecondPad = true;
        child = Reload(child);

        Templates.Reset(child, Templates.Diff(child).Single(d => d.Key == "force.friction"));
        Assert.Null(child.Force);
        Templates.Reset(child, Templates.Diff(child).Single(d => d.Key == "outputs.secondPad"));
        Assert.False(child.Outputs.SecondPad);
        Assert.Empty(Templates.Diff(child));
    }

    [Fact]
    public void Reset_all_makes_the_profile_its_template_but_keeps_its_name_game_and_notes()
    {
        Profile child = Reload(Templates.CreateChild(Starter("template-fps-pedals.json"), "Wardogs", ["wardogs.exe"]));
        child.Notes = "lean works";
        RuleTo(child, "Key:Q").Threshold = 0.2;
        child.Rules.Add(new Rule { From = "button7", To = "Key:F", Curve = "linear" });
        child = Reload(child);

        Templates.ResetAll(child);
        Assert.Empty(Templates.Diff(child));
        Assert.Equal(0.35, RuleTo(child, "Key:Q").Threshold);
        Assert.Equal("Wardogs", child.Name);
        Assert.Equal(["wardogs.exe"], child.Match.Exe);
        Assert.Equal("lean works", child.Notes);
        Assert.True(child.Outputs.Keyboard);   // resetting the keyboard setting is the user's explicit choice
    }

    // ---------------------------------------------------------------- save as template, detach

    [Fact]
    public void Save_as_template_copies_the_settings_and_drops_the_game()
    {
        Profile game = Parse("""
            { "name": "GTA V", "match": { "exe": ["GTA5.exe"] }, "safety": "strict", "force": { "rumble": 30 },
              "rules": [ { "from": "wheel", "to": "LeftStickX", "deadzone": 0.04 }, { "from": "throttle", "to": "RightTrigger" } ] }
            """);
        Profile t = Reload(Templates.SaveAsTemplate(game, "My racing"));
        Assert.True(t.IsTemplate);
        Assert.Equal("My racing", t.Name);
        Assert.Empty(t.Match.Exe);
        Assert.Null(t.Parent);
        Assert.Equal(2, t.Rules.Count);
        Assert.Equal(30, t.Force!.Rumble);
        Assert.Equal("GTA V", game.Name);   // the original is not changed
        Assert.Equal(["GTA5.exe"], game.Match.Exe);
    }

    [Fact]
    public void Save_as_template_from_a_profile_that_has_a_template_gives_a_complete_standalone_template()
    {
        Profile child = Reload(Templates.CreateChild(Driving(), "Forza", ["forza.exe"]));
        RuleTo(child, "RightTrigger").Deadzone = 0.1;
        Profile t = Reload(Templates.SaveAsTemplate(child, "Forza feel"));
        Assert.Null(t.Parent);
        Assert.Equal(5, t.Rules.Count);
        Assert.Equal(0.1, RuleTo(t, "RightTrigger").Deadzone);
        Assert.Equal(0, RuleTo(t, "LeftStickX").AntiDeadzone);
    }

    [Fact]
    public void A_profile_with_its_template_taken_out_behaves_the_same_and_is_written_in_full()
    {
        Profile child = Reload(Templates.CreateChild(Driving(), "Forza", ["forza.exe"]));
        RuleTo(child, "RightTrigger").Deadzone = 0.1;
        Profile plain = Templates.Detach(child);
        Assert.Null(plain.Parent);
        string json = ProfileWriter.ToJson(plain);
        Assert.DoesNotContain("\"parent\"", json);
        Assert.Equal(ProfileWriter.PlainJson(child), json);
    }

    [Fact]
    public void Sharing_a_profile_that_has_a_template_shares_the_finished_profile()
    {
        Profile child = Reload(Templates.CreateChild(Driving(), "Forza", ["forza.exe"]));
        RuleTo(child, "RightTrigger").Deadzone = 0.1;
        var import = ProfileShare.Import(ProfileShare.Export(child, null), Devices);
        Assert.True(import.Ok, string.Join("; ", import.Errors));
        Assert.Null(import.Profile!.Parent);
        Assert.Equal(ProfileWriter.PlainJson(child), ProfileWriter.PlainJson(import.Profile));
    }

    // ---------------------------------------------------------------- migration: existing profiles do not change

    [Fact]
    public void A_profile_that_never_heard_of_templates_loads_and_saves_exactly_as_before()
    {
        const string old = """
            { "name": "GTA V", "match": { "exe": ["GTA5.exe", "GTA5_Enhanced.exe"] }, "device": "auto", "safety": "strict",
              "outputs": { "keyboard": false }, "force": { "rumble": 30, "spring": 10 },
              "rules": [
                { "from": "wheel", "to": "LeftStickX", "curve": "linear", "deadzone": 0.04, "range": 540 },
                { "from": "throttle", "to": "RightTrigger", "curve": "expo", "exponent": 1.5 },
                { "from": "paddleR", "to": "Button:A", "mode": "pulse", "pulseMs": 80 }
              ] }
            """;
        Profile p = Parse(old);
        Assert.Null(p.Parent);
        Assert.False(p.IsTemplate);
        Assert.Null(p.RemovedRules);
        string json = ProfileWriter.ToJson(p);
        Assert.DoesNotContain("parent", json);
        Assert.DoesNotContain("isTemplate", json);
        Assert.DoesNotContain("removedRules", json);
        Assert.Equal(json, ProfileWriter.ToJson(Parse(json)));   // stable
        Assert.Equal(json, ProfileWriter.PlainJson(p));           // the plain writer is the old writer
        Assert.Equal(3, p.Rules.Count);
    }

    [Fact]
    public void An_existing_profile_runs_the_same_engine_output_after_a_save_and_reload()
    {
        Profile p = Parse("""{ "name": "t", "rules": [ { "from": "wheel", "to": "LeftStickX", "deadzone": 0.04 }, { "from": "clutch", "to": "Button:A", "threshold": 0.3 } ] }""");
        Profile again = Reload(p);
        var f = new InputFrame(); f.Values["wheel"] = 0.37; f.Values["clutch"] = 0.5;
        Assert.True(new MappingEngine(p, Devices.Fallback!, Devices).Evaluate(f, 0).SameAs(new MappingEngine(again, Devices.Fallback!, Devices).Evaluate(f, 0)));
    }

    [Fact]
    public void A_profile_based_on_a_template_gives_the_engine_the_same_result_as_the_equivalent_plain_profile()
    {
        Profile child = Reload(Templates.CreateChild(Driving(), "Forza", ["forza.exe"]));
        RuleTo(child, "LeftStickX").AntiDeadzone = 0.08;
        child = Reload(child);
        Profile plain = Parse(ProfileWriter.PlainJson(child));
        var f = new InputFrame(); f.Values["wheel"] = 0.2; f.Values["throttle"] = 0.6; f.Values["paddleR"] = 1;
        Assert.True(new MappingEngine(child, Devices.Fallback!, Devices).Evaluate(f, 0).SameAs(new MappingEngine(plain, Devices.Fallback!, Devices).Evaluate(f, 0)));
    }

    // ---------------------------------------------------------------- history works for templates and for profiles made from them

    [Fact]
    public void History_keeps_and_restores_versions_of_both_a_template_and_a_profile_made_from_it()
    {
        string dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "g923-hist-" + Guid.NewGuid().ToString("N"))).FullName;
        string history = Path.Combine(dir, "history");
        try
        {
            Profile template = Driving();
            Profile child = Reload(Templates.CreateChild(template, "Forza", ["forza.exe"]));

            string childPath = Path.Combine(dir, "forza.json"), tplPath = Path.Combine(dir, "template-driving.json");
            File.WriteAllText(childPath, ProfileWriter.ToJson(child));
            File.WriteAllText(tplPath, ProfileWriter.ToJson(template));
            Assert.NotNull(ProfileHistory.Snapshot(childPath, history, new DateTime(2026, 1, 1, 10, 0, 0)));
            Assert.NotNull(ProfileHistory.Snapshot(tplPath, history, new DateTime(2026, 1, 1, 10, 0, 0)));

            RuleTo(child, "RightTrigger").Deadzone = 0.3;
            File.WriteAllText(childPath, ProfileWriter.ToJson(child));
            RuleTo(template, "RightTrigger").Deadzone = 0.3;
            File.WriteAllText(tplPath, ProfileWriter.ToJson(template));

            foreach (string path in new[] { childPath, tplPath })
            {
                var entry = Assert.Single(ProfileHistory.List(path, history));
                Assert.True(ProfileHistory.Restore(entry, path, history));
                var restored = ProfileLoader.LoadFile(path, Devices);
                Assert.True(restored.Ok, string.Join("; ", restored.Errors));
                Assert.Equal(0, RuleTo(restored.Profile!, "RightTrigger").Deadzone);
            }
            Assert.True(ProfileLoader.LoadFile(childPath, Devices).Profile!.Parent is not null);   // still a child after a restore
            Assert.True(ProfileLoader.LoadFile(tplPath, Devices).Profile!.IsTemplate);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---------------------------------------------------------------- bad files

    [Fact]
    public void A_template_cannot_be_based_on_another_template_and_a_missing_copy_is_reported()
    {
        var chained = ProfileLoader.Parse("""
            { "name": "x", "isTemplate": true, "parent": { "name": "Driving", "base": { "name": "Driving", "rules": [] } }, "rules": [] }
            """, Devices);
        Assert.False(chained.Ok);
        Assert.Contains(chained.Errors, e => e.Contains("template cannot be based on another"));

        var noBase = ProfileLoader.Parse("""{ "name": "x", "parent": { "name": "Driving" }, "rules": [] }""", Devices);
        Assert.False(noBase.Ok);
        Assert.Contains(noBase.Errors, e => e.Contains("copy"));

        var nested = ProfileLoader.Parse("""
            { "name": "x", "parent": { "name": "A", "base": { "name": "A", "parent": { "name": "B", "base": { "name": "B", "rules": [] } }, "rules": [] } }, "rules": [] }
            """, Devices);
        Assert.False(nested.Ok);
    }

    [Fact]
    public void An_override_that_is_not_valid_is_reported_like_any_other_profile_error()
    {
        Profile child = Templates.CreateChild(Driving(), "Forza", ["forza.exe"]);
        string json = ProfileWriter.ToJson(child).Replace("\"rules\": [\n  ]", "\"rules\": [ { \"from\": \"nonsense\", \"to\": \"LeftStickX\" } ]")
                                                  .Replace("\"rules\": [\r\n  ]", "\"rules\": [ { \"from\": \"nonsense\", \"to\": \"LeftStickX\" } ]");
        var r = ProfileLoader.Parse(json, Devices);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Contains("nonsense"));
    }

    [Fact]
    public void A_template_that_lists_games_gets_a_warning_that_they_are_ignored()
    {
        var r = ProfileLoader.Parse("""{ "name": "t", "isTemplate": true, "match": { "exe": ["a.exe"] }, "rules": [] }""", Devices);
        Assert.True(r.Ok);
        Assert.Contains(r.Warnings, w => w.Contains("not attached to a game"));
    }
}

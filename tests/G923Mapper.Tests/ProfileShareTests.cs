using System.Text;
using G923Mapper;

namespace G923Mapper.Tests;

public class ProfileShareTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();
    private static DeviceMap Wheel() { Devices.TryGet("g923_xbox", out var d); return d; }

    private const string Strict = """
        { "name": "Round trip", "notes": "felt right\nin \"GTA\"", "match": { "exe": ["game.exe"] }, "device": "auto", "safety": "strict",
          "outputs": { "keyboard": false, "pad": "ds4", "secondPad": true },
          "force": { "rumble": 40, "spring": 10, "friction": 20, "motors": "heavy" },
          "rules": [
            { "from": "wheel", "to": "LeftStickX", "curve": "expo", "exponent": 1.5, "deadzone": 0.04, "antiDeadzone": 0.08, "smooth": 30, "range": 540 },
            { "from": "throttle", "to": "RightTrigger", "curve": "custom", "points": [[0, 0], [0.5, 0.2], [1, 1]] },
            { "from": "clutch", "to": "Button:A", "threshold": 0.35, "mode": "toggle" },
            { "from": "brake", "to": "P2:LeftTrigger", "curve": "linear", "part": "positive", "invert": true }
          ] }
        """;

    private static Profile Load(string json) => ProfileLoader.Parse(json, Devices).Profile!;

    // ---------------------------------------------------------------- round trip

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Copy_then_paste_gives_back_the_same_profile_without_loss(bool encoded)
    {
        Profile original = Load(Strict);
        string text = ProfileShare.Export(original, null, encoded);
        var import = ProfileShare.Import(text, Devices);
        Assert.True(import.Ok, string.Join("; ", import.Errors));
        Assert.Equal(ProfileWriter.ToJson(original), ProfileWriter.ToJson(import.Profile!));
        Assert.Empty(import.TurnedOff);
    }

    [Fact]
    public void The_encoded_form_is_one_unbroken_line_behind_a_prefix()
    {
        string text = ProfileShare.Export(Load(Strict), null);
        Assert.StartsWith(ProfileShare.Prefix, text);
        Assert.DoesNotContain('\n', text);
        Assert.DoesNotContain(' ', text);
    }

    [Fact]
    public void An_encoded_block_still_reads_after_a_chat_app_wraps_it_onto_several_lines()
    {
        string text = ProfileShare.Export(Load(Strict), null);
        string wrapped = string.Join("\r\n", Enumerable.Range(0, (text.Length + 59) / 60).Select(i => text.Substring(i * 60, Math.Min(60, text.Length - i * 60))));
        Assert.True(ProfileShare.Import(wrapped, Devices).Ok);
    }

    [Fact]
    public void A_bare_profile_file_can_be_pasted_too()
    {
        var import = ProfileShare.Import(ProfileWriter.ToJson(Load(Strict)), Devices);
        Assert.True(import.Ok, string.Join("; ", import.Errors));
        Assert.Equal("Round trip", import.Profile!.Name);
    }

    // ---------------------------------------------------------------- calibration

    [Fact]
    public void The_calibration_travels_with_the_profile_and_is_labelled_with_the_edition()
    {
        var ps = new DeviceMap
        {
            Name = "g923_ps", Vid = "046D", Pid = "C267", RotationDegrees = 900,
            Axes = new(StringComparer.OrdinalIgnoreCase) { ["wheel"] = new AxisDef { Source = "X", Signed = true }, ["throttle"] = new AxisDef { Source = "Y", Invert = true } },
            Buttons = new(StringComparer.OrdinalIgnoreCase) { ["paddleL"] = 6, ["paddleR"] = 5 },
        };
        string text = ProfileShare.Export(Load(Strict), ps);
        var import = ProfileShare.Import(text, Devices);
        Assert.True(import.Ok, string.Join("; ", import.Errors));
        Assert.Equal(WheelEdition.PlayStation, import.Edition);
        Assert.Equal("g923_ps", import.Calibration!.Name);
        Assert.Equal(6, import.Calibration.Buttons["paddleL"]);
        Assert.True(import.Calibration.Axes["wheel"].Signed);
    }

    [Theory]
    [InlineData(0x046D, 0xC26E, WheelEdition.Xbox)]
    [InlineData(0x046D, 0xC266, WheelEdition.PlayStation)]
    [InlineData(0x046D, 0xC267, WheelEdition.PlayStation)]
    [InlineData(0x046D, 0xC24F, WheelEdition.Unknown)]
    [InlineData(0x045E, 0xC26E, WheelEdition.Unknown)]
    public void Editions_are_told_apart_by_usb_id(int vid, int pid, WheelEdition expected) =>
        Assert.Equal(expected, WheelEditions.Of(vid, pid));

    [Fact]
    public void A_calibration_from_a_different_edition_than_the_connected_wheel_is_flagged()
    {
        var ps = new DeviceMap { Name = "g923_ps", Vid = "046D", Pid = "C267", Axes = new(StringComparer.OrdinalIgnoreCase) { ["wheel"] = new AxisDef { Source = "X", Signed = true } } };
        var import = ProfileShare.Import(ProfileShare.Export(Load(Strict), ps), Devices);
        Assert.Equal(ProfileShare.CalibrationFit.Different, ProfileShare.Fit(import, WheelEdition.Xbox));
        Assert.Equal(ProfileShare.CalibrationFit.Same, ProfileShare.Fit(import, WheelEdition.PlayStation));
        Assert.Equal(ProfileShare.CalibrationFit.Unknown, ProfileShare.Fit(import, WheelEdition.Unknown));
        Assert.Equal(ProfileShare.CalibrationFit.None, ProfileShare.Fit(ProfileShare.Import(ProfileShare.Export(Load(Strict), null), Devices), WheelEdition.Xbox));
    }

    [Theory]
    [InlineData("\"rotationDegrees\": 5")]
    [InlineData("\"rotationDegrees\": 99999")]
    [InlineData("\"vid\": \"ZZZZ\"")]
    [InlineData("\"name\": \"..\\\\evil\"")]
    [InlineData("\"extra\": true")]
    public void An_impossible_calibration_is_rejected(string badField)
    {
        string cal = "{ \"name\": \"cal\", \"vid\": \"046D\", \"pid\": \"C26E\", \"rotationDegrees\": 900, " + badField + ", \"axes\": { \"wheel\": { \"source\": \"X\", \"signed\": true } } }";
        string text = "{ \"g923mapper\": 1, \"profile\": { \"name\": \"t\", \"rules\": [] }, \"calibration\": " + cal + " }";
        var import = ProfileShare.Import(text, Devices);
        Assert.False(import.Ok);
        Assert.Contains(import.Errors, e => e.Contains("calibration"));
    }

    [Fact]
    public void A_calibration_axis_with_an_impossible_range_is_rejected()
    {
        string text = """
            { "g923mapper": 1, "profile": { "name": "t", "rules": [] },
              "calibration": { "name": "cal", "vid": "046D", "pid": "C26E", "rotationDegrees": 900,
                               "axes": { "wheel": { "source": "X", "signed": true, "min": 60000, "max": 100 } } } }
            """;
        Assert.False(ProfileShare.Import(text, Devices).Ok);
    }

    // ---------------------------------------------------------------- safety: nothing risky arrives switched on

    [Fact]
    public void Importing_a_profile_with_keyboard_output_on_leaves_it_off_and_the_profile_strict()
    {
        const string risky = """
            { "name": "Lean", "safety": "standard", "hideWheel": true, "outputs": { "keyboard": true, "turbo": true },
              "rules": [ { "from": "clutch", "to": "Key:Q" }, { "from": "paddleR", "to": "Button:A", "turbo": true }, { "from": "brake", "to": "Mouse:Left" } ] }
            """;
        var import = ProfileShare.Import(ProfileShare.Export(Load(risky), null), Devices);
        Assert.True(import.Ok, string.Join("; ", import.Errors));
        Profile p = import.Profile!;
        Assert.False(p.Outputs.Keyboard);
        Assert.False(p.Outputs.Turbo);
        Assert.Equal("strict", ProfileSafety.Effective(p));
        Assert.Null(p.HideWheel);
        Assert.All(p.Rules, r => Assert.False(r.Turbo));
        Assert.Equal(3, p.Rules.Count);   // the rules are kept, just not active
        Assert.Equal(KeyOutput.NeedsEnabling, KeyMappings.Status(p).State);
        Assert.Equal(3, import.TurnedOff.Count);
        Assert.Contains(import.TurnedOff, t => t.StartsWith("Keyboard and mouse"));
    }

    [Fact]
    public void The_profile_written_to_disk_after_such_an_import_still_has_keyboard_off()
    {
        var import = ProfileShare.Import("""{ "name": "t", "safety": "standard", "outputs": { "keyboard": true }, "rules": [ { "from": "clutch", "to": "Key:Q" } ] }""", Devices);
        var reloaded = ProfileLoader.Parse(ProfileWriter.ToJson(import.Profile!), Devices).Profile!;
        Assert.False(reloaded.Outputs.Keyboard);
        Assert.Equal("strict", ProfileSafety.Effective(reloaded));
    }

    [Fact]
    public void The_summary_names_the_profile_counts_rules_and_lists_the_outputs()
    {
        var import = ProfileShare.Import(Strict, Devices);
        string s = import.Summary();
        Assert.Contains("Round trip", s);
        Assert.Contains("4 rules", s);
        Assert.Contains("DualShock 4", s);
        Assert.Contains("second controller", s);
        var keys = ProfileShare.Import("""{ "name": "k", "outputs": { "keyboard": true }, "rules": [ { "from": "clutch", "to": "Key:Q" } ] }""", Devices);
        Assert.Contains("1 key or mouse rule (switched off)", keys.Summary());
    }

    // ---------------------------------------------------------------- validation of the text itself

    [Fact]
    public void Unknown_fields_are_ignored_with_a_note_and_never_reach_the_profile()
    {
        const string text = """
            { "name": "t", "evil": "run me", "outputs": { "keyboard": false, "shell": "calc.exe" },
              "rules": [ { "from": "wheel", "to": "LeftStickX", "onPress": "cmd /c del *" } ] }
            """;
        var import = ProfileShare.Import(text, Devices);
        Assert.True(import.Ok, string.Join("; ", import.Errors));
        Assert.Contains(import.Warnings, w => w.Contains("\"evil\""));
        Assert.Contains(import.Warnings, w => w.Contains("\"outputs.shell\""));
        Assert.Contains(import.Warnings, w => w.Contains("\"rule 1.onPress\""));
        Assert.DoesNotContain("calc.exe", ProfileWriter.ToJson(import.Profile!));
        Assert.DoesNotContain("evil", ProfileWriter.ToJson(import.Profile!));
    }

    [Fact]
    public void Too_much_text_is_refused_before_it_is_read()
    {
        string huge = "{ \"name\": \"" + new string('a', ProfileShare.MaxChars) + "\", \"rules\": [] }";
        var import = ProfileShare.Import(huge, Devices);
        Assert.False(import.Ok);
        Assert.Contains("too big", import.Errors[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello world")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{ not json")]
    [InlineData("g923profile:!!!not base64!!!")]
    public void Text_that_is_not_a_profile_is_refused_with_a_message(string text)
    {
        var import = ProfileShare.Import(text, Devices);
        Assert.False(import.Ok);
        Assert.NotEmpty(import.Errors);
        Assert.Null(import.Profile);
    }

    [Fact]
    public void Deeply_nested_text_is_refused_rather_than_followed()
    {
        string deep = new string('[', 200) + new string(']', 200);
        Assert.False(ProfileShare.Import("{ \"profile\": " + deep + " }", Devices).Ok);
        Assert.False(ProfileShare.Import("{ \"name\": \"t\", \"rules\": " + deep + " }", Devices).Ok);
    }

    [Fact]
    public void A_profile_that_fails_the_normal_checks_is_refused_with_the_same_errors()
    {
        var import = ProfileShare.Import("""{ "name": "t", "rules": [ { "from": "nonsense", "to": "LeftStickX" }, { "from": "wheel", "to": "NoSuchTarget" } ] }""", Devices);
        Assert.False(import.Ok);
        Assert.Contains(import.Errors, e => e.Contains("nonsense"));
        Assert.Contains(import.Errors, e => e.Contains("NoSuchTarget"));
    }

    [Fact]
    public void A_profile_from_a_newer_format_is_refused()
    {
        var import = ProfileShare.Import("""{ "g923mapper": 99, "profile": { "name": "t", "rules": [] } }""", Devices);
        Assert.False(import.Ok);
        Assert.Contains("newer", import.Errors[0]);
    }

    [Fact]
    public void An_expression_is_only_ever_stored_as_text_and_checked_like_any_other()
    {
        var import = ProfileShare.Import("""{ "name": "t", "rules": [ { "from": "throttle", "to": "RightTrigger", "expr": "max(0, x)" } ] }""", Devices);
        Assert.True(import.Ok, string.Join("; ", import.Errors));
        Assert.Equal("max(0, x)", import.Profile!.Rules[0].Expr);
        var bad = ProfileShare.Import("""{ "name": "t", "rules": [ { "from": "throttle", "to": "RightTrigger", "expr": "System.Diagnostics.Process.Start(1)" } ] }""", Devices);
        Assert.False(bad.Ok);
    }

    [Fact]
    public void Base64_that_decodes_to_something_other_than_a_profile_is_refused()
    {
        string text = ProfileShare.Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes("MZ binary-ish \u0000\u0001 not json"));
        Assert.False(ProfileShare.Import(text, Devices).Ok);
    }
}

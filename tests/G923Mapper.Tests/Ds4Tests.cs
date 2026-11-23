using G923Mapper;

namespace G923Mapper.Tests;

public class Ds4MappingTests
{
    [Theory]
    [InlineData(PadButton.A, Ds4Button.Cross)]
    [InlineData(PadButton.B, Ds4Button.Circle)]
    [InlineData(PadButton.X, Ds4Button.Square)]
    [InlineData(PadButton.Y, Ds4Button.Triangle)]
    [InlineData(PadButton.LeftShoulder, Ds4Button.L1)]
    [InlineData(PadButton.RightShoulder, Ds4Button.R1)]
    [InlineData(PadButton.Back, Ds4Button.Share)]
    [InlineData(PadButton.Start, Ds4Button.Options)]
    [InlineData(PadButton.Guide, Ds4Button.Ps)]
    [InlineData(PadButton.LeftThumb, Ds4Button.L3)]
    [InlineData(PadButton.RightThumb, Ds4Button.R3)]
    public void Xbox_buttons_become_their_playstation_equivalents(PadButton xbox, Ds4Button ps) =>
        Assert.Equal(ps, Ds4Mapping.Button(xbox));

    [Theory]
    [InlineData(PadButton.DPadUp)]
    [InlineData(PadButton.DPadDown)]
    [InlineData(PadButton.DPadLeft)]
    [InlineData(PadButton.DPadRight)]
    public void The_dpad_is_handled_as_one_direction_not_as_buttons(PadButton b) => Assert.Null(Ds4Mapping.Button(b));

    [Fact]
    public void Every_xbox_button_is_either_mapped_or_a_dpad_direction()
    {
        foreach (PadButton b in Enum.GetValues<PadButton>())
        {
            bool dpad = b is PadButton.DPadUp or PadButton.DPadDown or PadButton.DPadLeft or PadButton.DPadRight;
            Assert.Equal(!dpad, Ds4Mapping.Button(b) is not null);
        }
    }

    [Theory]
    [InlineData(-32768, 0)]
    [InlineData(0, 128)]
    [InlineData(32767, 255)]
    [InlineData(-16384, 64)]
    [InlineData(16384, 192)]
    public void A_stick_axis_is_rescaled_to_a_byte_with_the_centre_at_128(int xbox, int expected) =>
        Assert.Equal(expected, Ds4Mapping.StickX((short)xbox));

    [Theory]
    [InlineData(32767, 0)]      // Xbox up = positive; DualShock 4 up = 0
    [InlineData(0, 128)]      // centred stays centred
    [InlineData(-32768, 255)]
    public void The_vertical_direction_is_flipped(int xbox, int expected) =>
        Assert.Equal(expected, Ds4Mapping.StickY((short)xbox));

    [Fact]
    public void Steering_is_symmetric_around_the_centre()
    {
        // The wheel drives left-stick X, so left and right must be equally far from 128.
        Assert.Equal(128 - Ds4Mapping.StickX(-32768), Ds4Mapping.StickX(32767) - 128 + 1);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(30, false)]
    [InlineData(31, true)]
    [InlineData(255, true)]
    public void A_pulled_trigger_also_counts_as_the_digital_l2_r2_button(int value, bool pressed) =>
        Assert.Equal(pressed, Ds4Mapping.TriggerPressed((byte)value));

    [Theory]
    [InlineData(false, false, false, false, Ds4DPad.Neutral)]
    [InlineData(true, false, false, false, Ds4DPad.North)]
    [InlineData(false, true, false, false, Ds4DPad.South)]
    [InlineData(false, false, true, false, Ds4DPad.West)]
    [InlineData(false, false, false, true, Ds4DPad.East)]
    [InlineData(true, false, true, false, Ds4DPad.NorthWest)]
    [InlineData(true, false, false, true, Ds4DPad.NorthEast)]
    [InlineData(false, true, true, false, Ds4DPad.SouthWest)]
    [InlineData(false, true, false, true, Ds4DPad.SouthEast)]
    [InlineData(true, true, false, false, Ds4DPad.Neutral)]      // opposite directions cancel
    [InlineData(true, true, true, false, Ds4DPad.West)]
    [InlineData(false, false, true, true, Ds4DPad.Neutral)]
    public void The_dpad_buttons_become_one_direction(bool up, bool down, bool left, bool right, Ds4DPad expected) =>
        Assert.Equal(expected, Ds4Mapping.DPad(up, down, left, right));
}

public class PadKindTests
{
    private static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();

    [Theory]
    [InlineData(null, PadKind.Xbox360)]
    [InlineData("", PadKind.Xbox360)]
    [InlineData("xbox360", PadKind.Xbox360)]
    [InlineData("XBOX", PadKind.Xbox360)]
    [InlineData("ds4", PadKind.DualShock4)]
    [InlineData("DualShock4", PadKind.DualShock4)]
    [InlineData("ps4", PadKind.DualShock4)]
    [InlineData(" PlayStation ", PadKind.DualShock4)]
    public void Pad_names_are_forgiving(string? text, PadKind expected)
    {
        Assert.True(PadKinds.TryParse(text, out var kind));
        Assert.Equal(expected, kind);
    }

    [Fact]
    public void An_unknown_pad_name_is_rejected()
    {
        Assert.False(PadKinds.TryParse("n64", out _));
        var r = ProfileLoader.Parse("""{ "name": "t", "outputs": { "pad": "n64" }, "rules": [] }""", Devices);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Contains("outputs.pad"));
    }

    [Fact]
    public void A_profile_asks_for_its_controller_and_the_default_is_xbox()
    {
        var plain = ProfileLoader.Parse("""{ "name": "t", "rules": [] }""", Devices).Profile!;
        var ps = ProfileLoader.Parse("""{ "name": "t", "outputs": { "pad": "ds4" }, "rules": [] }""", Devices).Profile!;
        Assert.Equal(PadKind.Xbox360, PadKinds.Of(plain));
        Assert.Equal(PadKind.DualShock4, PadKinds.Of(ps));
    }

    [Fact]
    public void The_controller_choice_survives_saving_and_the_default_is_not_written()
    {
        var ps = ProfileLoader.Parse("""{ "name": "t", "outputs": { "pad": "ds4" }, "rules": [] }""", Devices).Profile!;
        string json = ProfileWriter.ToJson(ps);
        Assert.Contains("\"pad\": \"ds4\"", json);
        Assert.Equal(PadKind.DualShock4, PadKinds.Of(ProfileLoader.Parse(json, Devices).Profile!));

        var plain = ProfileLoader.Parse("""{ "name": "t", "rules": [] }""", Devices).Profile!;
        Assert.DoesNotContain("\"pad\"", ProfileWriter.ToJson(plain));
    }

    [Fact]
    public void Rules_still_use_xbox_names_and_play_through_the_same_engine()
    {
        var p = ProfileLoader.Parse("""
            { "name": "t", "outputs": { "pad": "ds4" },
              "rules": [ { "from": "paddleR", "to": "Button:A" }, { "from": "wheel", "to": "LeftStickX" } ] }
            """, Devices).Profile!;
        Devices.TryGet("g923_xbox", out var d);
        var frame = new InputFrame();
        frame.Values["paddleR"] = 1; frame.Values["wheel"] = 1;
        var state = new MappingEngine(p, d, Devices).Evaluate(frame, 0);
        Assert.True(state[PadButton.A]);                 // becomes Cross when it reaches the DualShock 4 pad
        Assert.Equal(short.MaxValue, state.LeftX);
        Assert.Equal(255, Ds4Mapping.StickX(state.LeftX));
    }
}

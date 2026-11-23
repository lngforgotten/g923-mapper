namespace G923Mapper;

/// <summary>X360 output buttons. Independent of ViGEm so the output layer can be swapped.</summary>
public enum PadButton
{
    A, B, X, Y, LeftShoulder, RightShoulder, Back, Start, Guide,
    LeftThumb, RightThumb, DPadUp, DPadDown, DPadLeft, DPadRight,
}

public enum PadAxis { LeftStickX, LeftStickY, RightStickX, RightStickY, LeftTrigger, RightTrigger }

/// <summary>A virtual Xbox 360 pad. Sticks -32768..32767, triggers 0..255.</summary>
public interface IVirtualPad : IDisposable
{
    void Connect();
    void SetLeftStick(short x, short y);
    void SetRightStick(short x, short y);
    void SetTriggers(byte left, byte right);
    void SetButton(PadButton button, bool pressed);
    /// <summary>Send the accumulated state to the system.</summary>
    void Submit();
    /// <summary>The game's rumble request: (large motor, small motor), each 0..255. Raised on a driver thread.</summary>
    event Action<byte, byte>? Rumble;
}

/// <summary>Complete output state for one frame. Pure data, so the engine is testable without a driver.</summary>
public sealed class PadState
{
    private static readonly int ButtonCount = Enum.GetValues<PadButton>().Length;
    private readonly bool[] _buttons = new bool[ButtonCount];

    public short LeftX, LeftY, RightX, RightY;
    public byte LeftTrigger, RightTrigger;

    /// <summary>What the second virtual controller should show (null when the profile has none).</summary>
    public PadState? Second { get; set; }

    /// <summary>Keyboard keys the profile wants held down (canonical names). Empty unless keyboard output is on.</summary>
    public HashSet<string> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when every output is identical, so an unchanged frame need not be sent to the driver again.</summary>
    public bool SameAs(PadState o)
    {
        if (LeftX != o.LeftX || LeftY != o.LeftY || RightX != o.RightX || RightY != o.RightY) return false;
        if (LeftTrigger != o.LeftTrigger || RightTrigger != o.RightTrigger) return false;
        for (int i = 0; i < _buttons.Length; i++) if (_buttons[i] != o._buttons[i]) return false;
        if ((Second is null) != (o.Second is null)) return false;
        if (Second is not null && !Second.SameAs(o.Second!)) return false;
        return Keys.SetEquals(o.Keys);
    }

    public bool this[PadButton b]
    {
        get => _buttons[(int)b];
        set => _buttons[(int)b] = value;
    }

    public void WriteTo(IVirtualPad pad)
    {
        pad.SetLeftStick(LeftX, LeftY);
        pad.SetRightStick(RightX, RightY);
        pad.SetTriggers(LeftTrigger, RightTrigger);
        foreach (PadButton b in Enum.GetValues<PadButton>()) pad.SetButton(b, _buttons[(int)b]);
        pad.Submit();
    }
}

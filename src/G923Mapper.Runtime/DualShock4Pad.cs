using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace G923Mapper;

/// <summary>
/// A virtual DualShock 4 through ViGEmBus. It is fed the same Xbox-style state as the Xbox 360 pad; <see cref="Ds4Mapping"/>
/// translates it (Cross = A, Circle = B, Square = X, Triangle = Y, L1/R1 = shoulders, Share = Back, Options = Start,
/// PS = Guide), flips the stick's vertical direction, and turns the D-pad buttons into the one direction a DualShock 4 reports.
/// </summary>
public sealed class DualShock4Pad : IVirtualPad
{
    private readonly ViGEmClient _client;
    private readonly IDualShock4Controller _pad;
    private bool _up, _down, _left, _right;

    public DualShock4Pad()
    {
        _client = new ViGEmClient();   // throws if ViGEmBus is not installed
        _pad = _client.CreateDualShock4Controller();
        _pad.AutoSubmitReport = false;
        _pad.FeedbackReceived += (_, e) => Rumble?.Invoke(e.LargeMotor, e.SmallMotor);
    }

    public event Action<byte, byte>? Rumble;

    public void Connect() => _pad.Connect();

    public void SetLeftStick(short x, short y)
    {
        _pad.SetAxisValue(DualShock4Axis.LeftThumbX, Ds4Mapping.StickX(x));
        _pad.SetAxisValue(DualShock4Axis.LeftThumbY, Ds4Mapping.StickY(y));
    }

    public void SetRightStick(short x, short y)
    {
        _pad.SetAxisValue(DualShock4Axis.RightThumbX, Ds4Mapping.StickX(x));
        _pad.SetAxisValue(DualShock4Axis.RightThumbY, Ds4Mapping.StickY(y));
    }

    public void SetTriggers(byte left, byte right)
    {
        _pad.SetSliderValue(DualShock4Slider.LeftTrigger, left);
        _pad.SetSliderValue(DualShock4Slider.RightTrigger, right);
        // A real DualShock 4 also reports L2/R2 as buttons once the trigger is pulled a little.
        _pad.SetButtonState(DualShock4Button.TriggerLeft, Ds4Mapping.TriggerPressed(left));
        _pad.SetButtonState(DualShock4Button.TriggerRight, Ds4Mapping.TriggerPressed(right));
    }

    public void SetButton(PadButton button, bool pressed)
    {
        switch (button)
        {
            case PadButton.DPadUp: _up = pressed; return;
            case PadButton.DPadDown: _down = pressed; return;
            case PadButton.DPadLeft: _left = pressed; return;
            case PadButton.DPadRight: _right = pressed; return;
        }
        if (Ds4Mapping.Button(button) is not { } b) return;
        if (b == Ds4Button.Ps) _pad.SetButtonState(DualShock4SpecialButton.Ps, pressed);
        else _pad.SetButtonState(Map(b), pressed);
    }

    public void Submit()
    {
        _pad.SetDPadDirection(Direction(Ds4Mapping.DPad(_up, _down, _left, _right)));
        _pad.SubmitReport();
    }

    public void Dispose()
    {
        try { _pad.Disconnect(); } catch { }
        _client.Dispose();
    }

    private static DualShock4Button Map(Ds4Button b) => b switch
    {
        Ds4Button.Cross => DualShock4Button.Cross,
        Ds4Button.Circle => DualShock4Button.Circle,
        Ds4Button.Square => DualShock4Button.Square,
        Ds4Button.Triangle => DualShock4Button.Triangle,
        Ds4Button.L1 => DualShock4Button.ShoulderLeft,
        Ds4Button.R1 => DualShock4Button.ShoulderRight,
        Ds4Button.L2 => DualShock4Button.TriggerLeft,
        Ds4Button.R2 => DualShock4Button.TriggerRight,
        Ds4Button.Share => DualShock4Button.Share,
        Ds4Button.Options => DualShock4Button.Options,
        Ds4Button.L3 => DualShock4Button.ThumbLeft,
        Ds4Button.R3 => DualShock4Button.ThumbRight,
        _ => throw new ArgumentOutOfRangeException(nameof(b)),
    };

    private static DualShock4DPadDirection Direction(Ds4DPad d) => d switch
    {
        Ds4DPad.North => DualShock4DPadDirection.North,
        Ds4DPad.NorthEast => DualShock4DPadDirection.Northeast,
        Ds4DPad.East => DualShock4DPadDirection.East,
        Ds4DPad.SouthEast => DualShock4DPadDirection.Southeast,
        Ds4DPad.South => DualShock4DPadDirection.South,
        Ds4DPad.SouthWest => DualShock4DPadDirection.Southwest,
        Ds4DPad.West => DualShock4DPadDirection.West,
        Ds4DPad.NorthWest => DualShock4DPadDirection.Northwest,
        _ => DualShock4DPadDirection.None,
    };
}

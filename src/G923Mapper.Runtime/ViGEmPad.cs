using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace G923Mapper;

public sealed class ViGEmPad : IVirtualPad
{
    private readonly ViGEmClient _client;
    private readonly IXbox360Controller _pad;

    public ViGEmPad()
    {
        _client = new ViGEmClient();   // throws if ViGEmBus is not installed
        _pad = _client.CreateXbox360Controller();
        _pad.AutoSubmitReport = false;
        _pad.FeedbackReceived += (_, e) => Rumble?.Invoke(e.LargeMotor, e.SmallMotor);
    }

    public event Action<byte, byte>? Rumble;

    public void Connect() => _pad.Connect();

    public void SetLeftStick(short x, short y)
    {
        _pad.SetAxisValue(Xbox360Axis.LeftThumbX, x);
        _pad.SetAxisValue(Xbox360Axis.LeftThumbY, y);
    }

    public void SetRightStick(short x, short y)
    {
        _pad.SetAxisValue(Xbox360Axis.RightThumbX, x);
        _pad.SetAxisValue(Xbox360Axis.RightThumbY, y);
    }

    public void SetTriggers(byte left, byte right)
    {
        _pad.SetSliderValue(Xbox360Slider.LeftTrigger, left);
        _pad.SetSliderValue(Xbox360Slider.RightTrigger, right);
    }

    public void SetButton(PadButton button, bool pressed) => _pad.SetButtonState(Map(button), pressed);

    public void Submit() => _pad.SubmitReport();

    public void Dispose()
    {
        try { _pad.Disconnect(); } catch { }
        _client.Dispose();
    }

    private static Xbox360Button Map(PadButton b) => b switch
    {
        PadButton.A => Xbox360Button.A,
        PadButton.B => Xbox360Button.B,
        PadButton.X => Xbox360Button.X,
        PadButton.Y => Xbox360Button.Y,
        PadButton.LeftShoulder => Xbox360Button.LeftShoulder,
        PadButton.RightShoulder => Xbox360Button.RightShoulder,
        PadButton.Back => Xbox360Button.Back,
        PadButton.Start => Xbox360Button.Start,
        PadButton.Guide => Xbox360Button.Guide,
        PadButton.LeftThumb => Xbox360Button.LeftThumb,
        PadButton.RightThumb => Xbox360Button.RightThumb,
        PadButton.DPadUp => Xbox360Button.Up,
        PadButton.DPadDown => Xbox360Button.Down,
        PadButton.DPadLeft => Xbox360Button.Left,
        PadButton.DPadRight => Xbox360Button.Right,
        _ => throw new ArgumentOutOfRangeException(nameof(b)),
    };
}

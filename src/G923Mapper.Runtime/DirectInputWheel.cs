using Vortice.DirectInput;

namespace G923Mapper;

/// <summary>A DirectInput game controller read as <see cref="RawInput"/>.</summary>
public sealed class DirectInputWheel : IWheelSource, IDisposable
{
    private readonly IDirectInputDevice8 _dev;
    private RawInput _last = new() { Axes = [32768, 32768, 0, 0, 0, 32768, 32768, 0], Live = false };
    private int _failures;

    public DeviceInstance Instance { get; }
    public int Vid { get; }
    public int Pid { get; }

    /// <summary>True once reads have failed for long enough that the device has probably been unplugged.</summary>
    public bool Lost => _failures > 150;

    /// <summary>Force feedback on the steering axis; null unless it was requested and the device allowed it.</summary>
    public WheelForce? Force { get; private set; }
    /// <summary>The wheel reports force-feedback support.</summary>
    public bool ForceSupported { get; }
    /// <summary>Why force feedback is not running, when it was requested but could not start.</summary>
    public string ForceMessage { get; private set; } = "";

    private DirectInputWheel(IDirectInput8 di, DeviceInstance instance, IntPtr window, bool forceFeedback, string? forceStyle = null)
    {
        Instance = instance;
        (Vid, Pid) = Ids(instance);
        _dev = di.CreateDevice(instance.InstanceGuid);
        _dev.SetDataFormat<RawJoystickState>();
        ForceSupported = WheelForce.IsSupported(_dev);
        bool wantForce = forceFeedback && ForceSupported;
        if (forceFeedback && !ForceSupported) ForceMessage = "This wheel does not report force-feedback support.";

        // Background keeps reading while another window has focus. Force feedback needs exclusive access;
        // without it we stay non-exclusive so other programs are not blocked.
        var level = _dev.SetCooperativeLevel(window, CooperativeLevel.Background | (wantForce ? CooperativeLevel.Exclusive : CooperativeLevel.NonExclusive));
        if (level.Failure && wantForce)
        {
            // Exclusive access needs a real window handle; without one effects can look created but never play.
            ForceMessage = $"Windows refused exclusive access to the wheel ({level.Code:X8}), so force feedback was left off.";
            AppLog.Warn(ForceMessage);
            wantForce = false;
            _dev.SetCooperativeLevel(window, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
        }
        foreach (DeviceObjectInstance obj in _dev.GetObjects(DeviceObjectTypeFlags.Axis))
        {
            try { _dev.GetObjectPropertiesById(obj.ObjectId).Range = new InputRange(0, 65535); } catch { }
        }
        if (!TryAcquire() && wantForce)
        {
            // Someone else (often Logitech G HUB) holds the wheel exclusively. Carry on without force feedback.
            ForceMessage = "Another program has exclusive control of the wheel, so force feedback could not start. Closing Logitech G HUB often fixes this.";
            AppLog.Warn(ForceMessage);
            _dev.Unacquire();
            _dev.SetCooperativeLevel(window, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
            wantForce = false;
            _dev.Acquire();
        }
        _buttonCount = _dev.Capabilities.ButtonCount;

        if (wantForce)
        {
            try { Force = WheelForce.Create(_dev, forceStyle); AppLog.Info($"Force feedback effects created ({ForceMath.NormaliseStyle(forceStyle)} style)."); }
            catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or InvalidOperationException)
            {
                ForceMessage = "The wheel refused the force-feedback effects: " + ex.Message;
                AppLog.Warn(ForceMessage);
            }
        }
    }

    private bool TryAcquire()
    {
        try { return _dev.Acquire().Success; }
        catch (SharpGen.Runtime.SharpGenException) { return false; }
    }

    private readonly int _buttonCount;

    public static (int vid, int pid) Ids(DeviceInstance d)
    {
        // DirectInput product GUIDs carry the VID in the low word and the PID in the high word of Data1.
        uint d1 = BitConverter.ToUInt32(d.ProductGuid.ToByteArray(), 0);
        return ((int)(d1 & 0xFFFF), (int)(d1 >> 16));
    }

    /// <summary>
    /// Find the wheel at position <paramref name="index"/> (0 = the first): a device whose USB ids match <paramref name="preferred"/>,
    /// else any device with ids registered in <paramref name="registry"/>, else anything with "G923" in its name. Wheels are
    /// ordered by instance id, so each copy of the app gets the same wheel every time. Null when there is no such wheel.
    /// </summary>
    public static DeviceInstance? Find(IDirectInput8 di, DeviceRegistry registry, DeviceMap? preferred, int index = 0)
    {
        var all = di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
        var candidates = all.Select(d =>
        {
            var (vid, pid) = Ids(d);
            return new WheelPicker.Candidate(d.InstanceGuid, vid, pid, d.ProductName);
        }).ToList();
        Guid? id = WheelPicker.Pick(candidates, preferred?.UsbIds, (v, p) => registry.FindByUsb(v, p) is not null, index);
        return id is Guid g ? all.First(d => d.InstanceGuid == g) : null;
    }
    /// <param name="window">A window handle for the cooperative level (the console window, or the GUI's main window).</param>
    /// <param name="forceFeedback">Ask for exclusive access and set up force feedback. Falls back quietly (see <see cref="ForceMessage"/>).</param>
    public static DirectInputWheel Open(IDirectInput8 di, DeviceInstance instance, IntPtr window, bool forceFeedback = false, string? forceStyle = null) =>
        new(di, instance, window, forceFeedback, forceStyle);

    public RawInput Read()
    {
        try
        {
            _dev.Poll();
            JoystickState s = _dev.GetCurrentJoystickState();
            int[] axes = [s.X, s.Y, s.Z, s.RotationX, s.RotationY, s.RotationZ, s.Sliders[0], s.Sliders[1]];
            _last = new RawInput
            {
                Axes = axes,
                Buttons = s.Buttons,
                ButtonCount = _buttonCount,
                Pov = s.PointOfViewControllers[0],
                Live = !RawInput.LooksStale(axes),
            };
            _failures = 0;
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
            // Lost the device (unplugged, or acquisition dropped): try to get it back and repeat the last reading.
            _failures++;
            try { _dev.Acquire(); } catch { }
        }
        return _last;
    }

    public void Dispose()
    {
        Force?.Dispose();
        try { _dev.Unacquire(); } catch { }
        _dev.Dispose();
    }
}

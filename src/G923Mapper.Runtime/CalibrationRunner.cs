using Vortice.DirectInput;

namespace G923Mapper;

/// <summary>Finds the wheel, asks what to call it, and runs the calibration wizard through any <see cref="IWizardIO"/> (console or GUI).</summary>
public static class CalibrationRunner
{
    public sealed record Outcome(CalibrationResult? Result, string? Error, string DeviceName, string Ids);

    /// <param name="window">Window handle for DirectInput's cooperative level.</param>
    public static Outcome Run(DeviceRegistry devices, IntPtr window, IWizardIO io)
    {
        using IDirectInput8 di = DInput.DirectInput8Create();
        DeviceInstance? chosen = DirectInputWheel.Find(di, devices, null);
        if (chosen is null)
            return new Outcome(null, "No wheel found. Plug it in (and connect its power supply) and try again.", "", "");

        var (vid, pid) = DirectInputWheel.Ids(chosen);
        string ids = $"VID_{vid:X4} PID_{pid:X4}";
        io.Say($"Wheel found: {chosen.ProductName} ({ids}).");

        string defaultId = devices.FindByUsb(vid, pid)?.Name ?? (vid == 0x046D ? "g923_ps" : $"wheel_{pid:x4}");
        string id = io.Ask("Name to save this device under (g923_xbox for the Xbox edition, g923_ps for PlayStation)", defaultId);
        if (string.IsNullOrWhiteSpace(id)) id = defaultId;

        using DirectInputWheel wheel = DirectInputWheel.Open(di, chosen, window);
        var wizard = new CalibrationWizard(wheel, io, new CalibrationOptions
        {
            DeviceId = id,
            Vid = vid.ToString("X4"),
            Pid = pid.ToString("X4"),
            PlayStationLabels = CalibrationOptions.LooksPlayStation(id, pid.ToString("X4")),
        });
        CalibrationResult? result = wizard.Run();
        return new Outcome(result, result is null ? "Calibration did not finish." : null, chosen.ProductName, ids);
    }
}

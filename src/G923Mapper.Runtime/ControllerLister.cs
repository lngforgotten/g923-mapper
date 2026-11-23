using Vortice.DirectInput;

namespace G923Mapper;

/// <summary>A game controller Windows reports through DirectInput.</summary>
public sealed record ControllerInfo(string Name, int Vid, int Pid, Guid Instance)
{
    public string Ids => $"VID_{Vid:X4} PID_{Pid:X4}";
}

public static class ControllerLister
{
    /// <summary>Every attached game controller, wheel or not, in the order Windows lists them. Empty if DirectInput cannot be used.</summary>
    public static IReadOnlyList<ControllerInfo> List()
    {
        try
        {
            using IDirectInput8 di = DInput.DirectInput8Create();
            return di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly)
                .Select(d =>
                {
                    var (vid, pid) = DirectInputWheel.Ids(d);
                    return new ControllerInfo(d.ProductName, vid, pid, d.InstanceGuid);
                })
                .ToList();
        }
        catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or InvalidOperationException)
        {
            AppLog.Warn($"Could not list game controllers: {ex.Message}");
            return [];
        }
    }
}

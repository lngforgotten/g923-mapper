using G923Mapper;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]   // a few tests set PadKinds.AutoKind, which is shared

namespace G923Mapper.Tests;

internal static class T
{
    public static readonly DeviceRegistry Devices = DeviceRegistry.CreateDefault();

    /// <summary>Build an engine from a JSON rules array body, e.g. T.Engine("""{"from":"wheel","to":"LeftStickX"}""").</summary>
    public static MappingEngine Engine(params string[] rules)
    {
        string json = $$"""{ "name": "t", "device": "g923_xbox", "rules": [ {{string.Join(",", rules)}} ] }""";
        var result = ProfileLoader.Parse(json, Devices);
        Assert.True(result.Ok, string.Join("; ", result.Errors));
        return new MappingEngine(result.Profile!, DeviceRegistry.G923Xbox());
    }

    /// <summary>Named inputs for one frame. Unlisted inputs read 0.</summary>
    public static InputFrame Frame(params (string name, double value)[] values)
    {
        var f = new InputFrame();
        foreach (var (n, v) in values) f.Values[n] = v;
        return f;
    }

    /// <summary>Raw G923 reading: X wheel, Y throttle, RZ brake, Slider0 clutch (pedals read 65535 at rest).</summary>
    public static RawInput Raw(int wheel = 32768, int throttle = 65535, int brake = 65535, int clutch = 65535,
        bool[]? buttons = null, int pov = -1, bool live = true)
    {
        var axes = new int[8];
        axes[0] = wheel; axes[1] = throttle; axes[5] = brake; axes[6] = clutch;
        return new RawInput { Axes = axes, Buttons = buttons ?? new bool[23], Pov = pov, Live = live };
    }

    public static bool[] Buttons(params int[] down)
    {
        var b = new bool[23];
        foreach (int n in down) b[n - 1] = true;
        return b;
    }
}

internal sealed class FakePad : IVirtualPad
{
    public short LX, LY, RX, RY;
    public byte LT, RT;
    public HashSet<PadButton> Down = new();
    public int Submits;
    public event Action<byte, byte>? Rumble;
    public void RaiseRumble(byte large, byte small) => Rumble?.Invoke(large, small);
    public void Connect() { }
    public void SetLeftStick(short x, short y) { LX = x; LY = y; }
    public void SetRightStick(short x, short y) { RX = x; RY = y; }
    public void SetTriggers(byte l, byte r) { LT = l; RT = r; }
    public void SetButton(PadButton b, bool pressed) { if (pressed) Down.Add(b); else Down.Remove(b); }
    public void Submit() => Submits++;
    public void Dispose() { }
}

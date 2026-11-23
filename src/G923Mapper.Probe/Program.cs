using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Vortice.DirectInput;

// Milestone 1: read the wheel.
// Lists DirectInput game controllers with VID/PID, then prints live axis/button values
// and tracks which axis moved so you can work out the layout for this particular wheel.

[DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();

Console.OutputEncoding = Encoding.UTF8;

string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "G923Mapper");
Directory.CreateDirectory(appData);

using IDirectInput8 di = DInput.DirectInput8Create();
IList<DeviceInstance> devices = di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);

Console.WriteLine("=== DirectInput game controllers ===");
if (devices.Count == 0)
{
    Console.WriteLine("None found. Is the wheel plugged in (and powered, for the G923's pedal/motor supply)?");
    return 1;
}

var seen = new List<object>();
for (int i = 0; i < devices.Count; i++)
{
    DeviceInstance d = devices[i];
    (int vid, int pid) = SplitProductGuid(d.ProductGuid);
    Console.WriteLine($"[{i}] {d.InstanceName}");
    Console.WriteLine($"     product: {d.ProductName}   VID_{vid:X4} PID_{pid:X4}   type: {d.Type}/{d.Subtype}");
    seen.Add(new { index = i, instanceName = d.InstanceName, productName = d.ProductName, vid = $"{vid:X4}", pid = $"{pid:X4}", type = d.Type.ToString(), instanceGuid = d.InstanceGuid, productGuid = d.ProductGuid });
}

// Spec: log every device's VID/PID/name so devices.json can be built from real data.
string seenPath = Path.Combine(appData, "devices-seen.json");
File.WriteAllText(seenPath, JsonSerializer.Serialize(seen, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"\nLogged to {seenPath}");

// Choose device: CLI arg index, else the only one, else prefer something that looks like a Logitech wheel, else ask.
int pick = -1;
if (args.Length > 0 && int.TryParse(args[0], out int argIdx) && argIdx >= 0 && argIdx < devices.Count) pick = argIdx;
else if (devices.Count == 1) pick = 0;
else
{
    var wheels = Enumerable.Range(0, devices.Count)
        .Where(i => SplitProductGuid(devices[i].ProductGuid).vid == 0x046D || devices[i].Type == DeviceType.Driving)
        .ToList();
    if (wheels.Count == 1) pick = wheels[0];
}
while (pick < 0)
{
    Console.Write("Select device index: ");
    if (int.TryParse(Console.ReadLine(), out int n) && n >= 0 && n < devices.Count) pick = n;
}

DeviceInstance chosen = devices[pick];
Console.WriteLine($"\nUsing [{pick}] {chosen.InstanceName}");

using IDirectInputDevice8 dev = di.CreateDevice(chosen.InstanceGuid);
dev.SetDataFormat<RawJoystickState>();
// Background + non-exclusive so we keep reading when the console isn't focused and don't block other apps.
dev.SetCooperativeLevel(GetConsoleWindow(), CooperativeLevel.Background | CooperativeLevel.NonExclusive);

// Request a consistent 0..65535 range on every axis so values are comparable across axes.
foreach (DeviceObjectInstance obj in dev.GetObjects(DeviceObjectTypeFlags.Axis))
{
    try { dev.GetObjectPropertiesById(obj.ObjectId).Range = new InputRange(0, 65535); } catch { /* read-only axes */ }
}
dev.Acquire();

var caps = dev.Capabilities;
Console.WriteLine($"Axes: {caps.AxeCount}  Buttons: {caps.ButtonCount}  POV hats: {caps.PovCount}  ForceFeedback: {(caps.Flags.HasFlag(DeviceFlags.ForceFeedback) ? "yes" : "no")}");
Console.WriteLine("Axis objects reported by the device:");
foreach (DeviceObjectInstance obj in dev.GetObjects(DeviceObjectTypeFlags.Axis))
    Console.WriteLine($"   {obj.Name,-22} {obj.ObjectType}");

if (args.Contains("--dump") || Console.IsOutputRedirected)
{
    // Headless mode: one line per sample, no cursor control. Usage: --dump [seconds]
    int seconds = 5;
    int di2 = Array.IndexOf(args, "--dump");
    if (di2 >= 0 && di2 + 1 < args.Length && int.TryParse(args[di2 + 1], out int sec)) seconds = sec;
    var end = DateTime.UtcNow.AddSeconds(seconds);
    while (DateTime.UtcNow < end)
    {
        dev.Poll();
        JoystickState s = dev.GetCurrentJoystickState();
        string down = string.Join(",", s.Buttons.Select((b, i) => (b, i)).Where(t => t.b).Select(t => t.i + 1));
        Console.WriteLine($"X={s.X,5} Y={s.Y,5} Z={s.Z,5} RX={s.RotationX,5} RY={s.RotationY,5} RZ={s.RotationZ,5} S0={s.Sliders[0],5} S1={s.Sliders[1],5} POV={s.PointOfViewControllers[0]} btn=[{down}]");
        Thread.Sleep(250);
    }
    return 0;
}

Console.WriteLine("\nLive view. Move the wheel and press each pedal/button; the 'moved' column shows which axis changed.");
Console.WriteLine("Keys:  R = reset min/max   S = save axis report to disk   Q = quit\n");

string[] axisNames = ["X", "Y", "Z", "RotX", "RotY", "RotZ", "Slider0", "Slider1"];
int[] min = Enumerable.Repeat(int.MaxValue, 8).ToArray();
int[] max = Enumerable.Repeat(int.MinValue, 8).ToArray();
int[] first = new int[8];
bool haveFirst = false;
int top = Console.CursorTop;
int lines = axisNames.Length + 4;
for (int i = 0; i < lines; i++) Console.WriteLine();

while (true)
{
    if (Console.KeyAvailable)
    {
        ConsoleKey k = Console.ReadKey(true).Key;
        if (k == ConsoleKey.Q) break;
        if (k == ConsoleKey.R) { Array.Fill(min, int.MaxValue); Array.Fill(max, int.MinValue); haveFirst = false; }
        if (k == ConsoleKey.S) SaveReport();
    }

    try
    {
        dev.Poll();
        JoystickState s = dev.GetCurrentJoystickState();
        int[] v = [s.X, s.Y, s.Z, s.RotationX, s.RotationY, s.RotationZ, s.Sliders[0], s.Sliders[1]];
        if (!haveFirst) { Array.Copy(v, first, 8); haveFirst = true; }

        var sb = new StringBuilder();
        sb.AppendLine("Axis      value   min..max            span    moved");
        for (int i = 0; i < 8; i++)
        {
            min[i] = Math.Min(min[i], v[i]);
            max[i] = Math.Max(max[i], v[i]);
            int span = max[i] - min[i];
            string bar = new string('#', Math.Clamp(v[i] * 20 / 65535, 0, 20)).PadRight(20);
            sb.AppendLine($"{axisNames[i],-8} {v[i],6}   {min[i],6}..{max[i],-6}   {span,6}   {(span > 2000 ? "YES" : "   ")} {bar}");
        }
        string pressed = string.Join(" ", s.Buttons.Select((b, i) => (b, i)).Where(t => t.b).Select(t => t.i + 1));
        sb.AppendLine($"Buttons down: {(pressed.Length == 0 ? "-" : pressed)}");
        string pov = string.Join(" ", s.PointOfViewControllers.Take(Math.Max(caps.PovCount, 1)).Select(p => p < 0 ? "-" : (p / 100) + "deg"));
        sb.AppendLine($"POV: {pov}");
        sb.AppendLine("(R reset, S save report, Q quit)");

        Console.SetCursorPosition(0, top);
        foreach (string line in sb.ToString().Split(Environment.NewLine))
            Console.WriteLine(line.PadRight(Console.WindowWidth - 1));
    }
    catch (SharpGen.Runtime.SharpGenException)
    {
        // Lost the device (unplugged, or focus change); try to re-acquire.
        try { dev.Acquire(); } catch { }
    }
    Thread.Sleep(16);
}

dev.Unacquire();
return 0;

void SaveReport()
{
    var report = new
    {
        device = chosen.InstanceName,
        productName = chosen.ProductName,
        savedUtc = DateTime.UtcNow,
        axes = axisNames.Select((n, i) => new { name = n, min = min[i] == int.MaxValue ? (int?)null : min[i], max = max[i] == int.MinValue ? (int?)null : max[i], moved = max[i] - min[i] > 2000 }).ToArray(),
    };
    string path = Path.Combine(appData, "axis-report.json");
    File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
}

static (int vid, int pid) SplitProductGuid(Guid g)
{
    // DirectInput product GUIDs encode PID in the high word and VID in the low word of Data1.
    byte[] b = g.ToByteArray();
    uint data1 = BitConverter.ToUInt32(b, 0);
    return ((int)(data1 & 0xFFFF), (int)(data1 >> 16));
}

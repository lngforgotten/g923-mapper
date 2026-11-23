using System.Runtime.InteropServices;
using G923Mapper;
using Vortice.DirectInput;

// Console front end for the mapper (the GUI is G923Mapper.App; both run the same MapperService).
//   dotnet run -- [--profile <name>] [--centre <raw>] [--list]
//   dotnet run -- --calibrate
// Profiles live in %APPDATA%\G923Mapper\profiles\*.json and reload when you save them.
// Calibration is saved to %APPDATA%\G923Mapper\devices.json.
// Keys while running: C = set the current wheel position as centre, Q = quit.

[DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();

string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "G923Mapper");
string profileDir = Path.Combine(appData, "profiles");
string devicesPath = Path.Combine(appData, "devices.json");
string profileName = "default";
int? centreOverride = null;
bool calibrate = args.Contains("--calibrate");
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--profile") profileName = args[i + 1];
    if (args[i] == "--centre" && int.TryParse(args[i + 1], out int c)) centreOverride = c;
}

AppLog.Init(Path.Combine(appData, "logs"));
var devices = DeviceRegistry.CreateDefault();
devices.LoadFile(devicesPath);

if (calibrate) return RunCalibration();

DefaultProfiles.EnsureWritten(profileDir);
using var store = new ProfileStore(profileDir, devices);
store.Loaded += (file, result) =>
{
    if (result.Ok)
    {
        Console.WriteLine($"\n[reload] {file}: OK{(result.Warnings.Count > 0 ? " with warnings" : "")}");
        foreach (string w in result.Warnings) Console.WriteLine($"         warning: {w}");
    }
    else
    {
        Console.WriteLine($"\n[reload] {file}: REJECTED, keeping the previous version");
        foreach (string e in result.Errors) Console.WriteLine($"         {e}");
    }
};

Console.WriteLine($"Profiles in {profileDir}:");
foreach (string f in store.Files)
{
    var r = store.LastResult(f)!;
    Console.WriteLine($"  {f,-20} {(r.Ok ? "ok" : "INVALID: " + string.Join(" | ", r.Errors))}");
    foreach (string w in r.Warnings) Console.WriteLine($"  {"",-20} warning: {w}");
}
if (args.Contains("--list")) return 0;

if (store.Get(profileName) is null)
{
    Console.WriteLine($"\nNo valid profile called \"{profileName}\". Use --profile <name> with one of the files above.");
    return 3;
}

bool interactive = !Console.IsOutputRedirected && !Console.IsInputRedirected;
long lastPrint = 0;
var clock = System.Diagnostics.Stopwatch.StartNew();

using var service = new MapperService(devices, store, GetConsoleWindow) { ProfileName = profileName };
if (centreOverride is int co) service.SetCentre(co);
service.StatusChanged += (_, text) => Console.WriteLine($"\n[status] {text}");
service.Log += text => Console.WriteLine($"\n[info] {text}");
service.Frame += snap =>
{
    if (clock.ElapsedMilliseconds - lastPrint < 100) return;
    lastPrint = clock.ElapsedMilliseconds;
    PadState s = snap.Pad;
    string pressed = string.Join(",", Enum.GetValues<PadButton>().Where(b => s[b]));
    string line = $"steer {s.LeftX,6} (raw {snap.Raw.Axes[0],5})  LT {s.LeftTrigger,3}  RT {s.RightTrigger,3}  [{pressed}]  {(snap.Raw.Live ? "" : "(move the wheel to wake it)")}";
    if (interactive) Console.Write("\r" + line.PadRight(Console.WindowWidth - 1));
    else Console.WriteLine(line);
};

Console.WriteLine("\nStarting. Edit a profile JSON and save to see it reload. Keys: C = set wheel centre, Q = quit\n");
using var stop = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };
service.Start();

while (!stop.IsSet)
{
    if (interactive && Console.KeyAvailable)
    {
        ConsoleKey k = Console.ReadKey(true).Key;
        if (k == ConsoleKey.Q) break;
        if (k == ConsoleKey.C) service.Recentre();
    }
    stop.Wait(50);
}
service.Stop();
Console.WriteLine("\nPad removed.");
return 0;

int RunCalibration()
{
    using IDirectInput8 di = DInput.DirectInput8Create();
    var all = di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
    Console.WriteLine("Game controllers found:");
    for (int i = 0; i < all.Count; i++)
    {
        var (v, p) = DirectInputWheel.Ids(all[i]);
        Console.WriteLine($"  [{i}] {all[i].ProductName}  VID_{v:X4} PID_{p:X4}");
    }

    DeviceInstance? chosen = DirectInputWheel.Find(di, devices, null);
    if (chosen is null)
    {
        Console.WriteLine("\nNo wheel found. Plug it in (and connect its power supply) and try again.");
        return 1;
    }
    var (vid, pid) = DirectInputWheel.Ids(chosen);
    Console.WriteLine($"\nCalibrating: {chosen.ProductName} (VID_{vid:X4} PID_{pid:X4})");

    var io = new ConsoleWizardIO();
    string defaultId = devices.FindByUsb(vid, pid)?.Name ?? (vid == 0x046D ? "g923_ps" : $"wheel_{pid:x4}");
    string id = io.Ask("Name to save this device under (g923_xbox for the Xbox edition, g923_ps for PlayStation)", defaultId);

    Console.WriteLine("\nThe wizard asks you to move the wheel and pedals one step at a time.");
    Console.WriteLine("Each step waits for you to press Enter, so get into position first (use your other hand for Enter).");
    Console.WriteLine();

    using DirectInputWheel wheel = DirectInputWheel.Open(di, chosen, GetConsoleWindow());
    var wizard = new CalibrationWizard(wheel, io, new CalibrationOptions
    {
        DeviceId = id,
        Vid = vid.ToString("X4"),
        Pid = pid.ToString("X4"),
    });

    CalibrationResult? result = wizard.Run();
    if (result is null)
    {
        Console.WriteLine("\nCalibration did not finish. Nothing was saved.");
        return 4;
    }

    Console.WriteLine("\n=== Result ===");
    foreach (string line in result.Report) Console.WriteLine("  " + line);

    string save = io.Ask($"\nSave to {devicesPath}? (y/n)", "y");
    if (!save.StartsWith("y", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Not saved.");
        return 0;
    }
    DeviceRegistry.SaveEntry(devicesPath, result.Map);
    Console.WriteLine($"Saved. Entry for \"{id}\":\n");
    Console.WriteLine(DeviceRegistry.ToJson(result.Map));
    Console.WriteLine("\nIf this is a wheel edition that has no built-in entry (e.g. the PlayStation one), send that JSON back.");
    return 0;
}

using System.Diagnostics;
using Vortice.DirectInput;

namespace G923Mapper;

public enum MapperState { Stopped, WaitingForWheel, Running, Error }

/// <summary>Everything the UI wants to show about one moment of the mapping loop.</summary>
public sealed record MapperSnapshot(RawInput Raw, InputFrame Input, PadState Pad, string ProfileName, DeviceMap Device)
{
    /// <summary>The latest raw reading of each connected extra device, by device name.</summary>
    public IReadOnlyDictionary<string, RawInput> ExtraRaw { get; init; } = new Dictionary<string, RawInput>();
}

/// <summary>
/// Owns the read-map-write loop on a background thread: finds the wheel, creates the virtual pad,
/// runs the active profile, hot-reloads it, and reconnects if the wheel is unplugged. Shared by the
/// console app and the GUI.
/// </summary>
public sealed class MapperService : IDisposable
{
    private readonly DeviceRegistry _devices;
    private readonly ProfileStore _store;
    private readonly Func<IntPtr> _window;
    private readonly Func<PadKind, IVirtualPad> _padFactory;
    private readonly Func<IKeySink> _keySinkFactory;
    private volatile IKeySink? _liveKeys;   // so a crash or Stop can still let go of held keys

    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private volatile string _profileName = "default";
    private volatile bool _profileChanged;
    private volatile bool _recentre;
    private int? _centreOverride;
    private ForceSettings _force = new();
    private volatile ProfileForce? _profileForce;                // what the profile in use asks for (null = nothing)
    private volatile ForceSettings _effectiveForce = new();     // the two combined; what the loop actually uses

    private void RefreshEffectiveForce() => _effectiveForce = ForceMath.Effective(_force, _profileForce);
    private volatile bool _restart;
    private volatile int _rumbleLarge, _rumbleSmall;

    /// <summary>What the game is asking of the heavy and the light rumble motor right now (0 to 255), for the UI graph.</summary>
    public (byte Heavy, byte Light) RumbleNow => ((byte)_rumbleLarge, (byte)_rumbleSmall);
    private int _testRequested;
    private long _testUntil, _testStart;
    private bool _testLogged;
    private volatile IReadOnlyList<(string Name, bool Connected)> _extraStatus = [];

    private sealed class ExtraReader(DeviceMap map)
    {
        public DeviceMap Map { get; } = map;
        public DirectInputWheel? Wheel;
    }

    public MapperState State { get; private set; } = MapperState.Stopped;
    public string Status { get; private set; } = "Stopped";
    public string WheelName { get; private set; } = "";
    public string WheelIds { get; private set; } = "";
    /// <summary>The connected wheel's USB ids, once found.</summary>
    public (int Vid, int Pid)? WheelUsbIds { get; private set; }
    /// <summary>False when the connected wheel has no calibrated entry, so a built-in layout is only a guess for it.</summary>
    public bool WheelCalibrated { get; private set; } = true;
    public MapperSnapshot? Latest { get; private set; }

    /// <summary>Force feedback on the wheel is running (rather than merely requested).</summary>
    /// <summary>How many times a second the input loop is reading the wheel and updating the virtual controller.</summary>
    public int UpdatesPerSecond { get; private set; }

    public bool ForceActive { get; private set; }
    public bool ForceSupported { get; private set; }
    public string ForceMessage { get; private set; } = "";

    /// <summary>
    /// Force feedback options. Strengths apply immediately; turning it on or off restarts the connection
    /// to the wheel (exclusive access is chosen when the wheel is opened).
    /// </summary>
    public ForceSettings Force
    {
        get => _force;
        set
        {
            // Switching force feedback on or off, or changing the vibration style, means the wheel has to be opened again.
            bool toggled = value.Enabled != _force.Enabled || ForceMath.NormaliseStyle(value.Style) != ForceMath.NormaliseStyle(_force.Style);
            _force = value.Clone();
            RefreshEffectiveForce();
            if (toggled) _restart = true;
        }
    }

    private volatile bool _paused;
    /// <summary>While paused the virtual controller stays connected but every control is released (the "toggle emulation" shortcut).</summary>
    public bool Paused { get => _paused; set => _paused = value; }

    /// <summary>
    /// Release every control while the active profile's game is running but another program's window is in front, so a throttle
    /// held in the background cannot keep driving the game. Does nothing for a profile that is not tied to a game or when the game is closed.
    /// </summary>
    public volatile bool ReleaseWhenGameNotInFront;

    /// <summary>
    /// Let the centre follow where the wheel comes to rest, a few degrees at most (see <see cref="CentreTracker"/>). Useful because a
    /// self-centring wheel often settles a degree or two off. On by default.
    /// </summary>
    public volatile bool AutoCentre = true;

    /// <summary>Which program is in front. Replaceable so it can be tested without windows.</summary>
    public Func<string?> ForegroundExe { get; init; } = ForegroundWindow.ExeName;
    /// <summary>The programs that are running. Replaceable for tests.</summary>
    public Func<IEnumerable<string>> RunningPrograms { get; init; } = RunningProcesses.Names;

    /// <summary>Each configured extra device and whether it is plugged in right now.</summary>
    public IReadOnlyList<(string Name, bool Connected)> ExtraStatus => _extraStatus;

    /// <summary>Re-read the device list and reconnect everything (after extra devices or force feedback mode change).</summary>
    public void Restart() => _restart = true;

    /// <summary>Shake the wheel briefly so the user can feel that force feedback works.</summary>
    public void TestForce() => Interlocked.Exchange(ref _testRequested, 1);

    /// <summary>Raised on the service thread when state or status text changes.</summary>
    public event Action<MapperState, string>? StatusChanged;
    /// <summary>Raised on the service thread, about 30 times a second while running.</summary>
    public event Action<MapperSnapshot>? Frame;
    /// <summary>Human-readable events: profile reloads, wheel connect/disconnect.</summary>
    public event Action<string>? Log;

    public bool IsRunning => _thread is { IsAlive: true };

    /// <summary>Which wheel this copy of the app drives: 0 for the first, 1 for the second (when several copies run, one per wheel).</summary>
    public int WheelIndex { get; init; }

    public MapperService(DeviceRegistry devices, ProfileStore store, Func<IntPtr> window, Func<PadKind, IVirtualPad>? padFactory = null,
                         Func<IKeySink>? keySinkFactory = null)
    {
        _keySinkFactory = keySinkFactory ?? (() => new SendInputKeySink());
        _devices = devices;
        _store = store;
        _window = window;
        _padFactory = padFactory ?? (kind => kind == PadKind.DualShock4 ? new DualShock4Pad() : new ViGEmPad());
    }

    /// <summary>Profile to run (file name without .json, or the profile's own name). Takes effect immediately.</summary>
    public string ProfileName
    {
        get => _profileName;
        set { _profileName = value; _profileChanged = true; }
    }

    /// <summary>Use this raw value as the wheel centre when the next session starts.</summary>
    public void SetCentre(int raw) => _centreOverride = raw;

    /// <summary>Take the wheel's current position as its centre.</summary>
    public void Recentre() => _recentre = true;

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _thread = new Thread(() => Run(ct)) { IsBackground = true, Name = "G923Mapper loop" };
        _thread.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _thread?.Join(3000);
        _thread = null;
        _liveKeys?.ReleaseAll();
        Latest = null;
        Set(MapperState.Stopped, "Stopped");
    }

    public void Dispose() => Stop();

    private void Emit(string message)
    {
        AppLog.Info(message);
        Log?.Invoke(message);
    }

    private void Set(MapperState state, string status)
    {
        if (State == state && Status == status) return;
        AppLog.Info($"Status: {state}: {status}");
        State = state;
        Status = status;
        StatusChanged?.Invoke(state, status);
    }

    private void Run(CancellationToken ct)
    {
        using var timer = TimerResolution.Begin();     // 1 ms sleeps instead of ~15 ms
        Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        while (!ct.IsCancellationRequested)
        {
            try { Session(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _liveKeys?.ReleaseAll();
                AppLog.Error("Mapper session failed", ex);
                Set(MapperState.Error, ex.Message);
                ct.WaitHandle.WaitOne(3000);
            }
        }
    }

    /// <summary>Whether the controls should reach the game now (see <see cref="ReleaseWhenGameNotInFront"/>). The process list is refreshed about every 1.5 s.</summary>
    private bool GameInFront(Profile profile, long nowMs, ref HashSet<string>? names, ref long namesAt)
    {
        if (!ReleaseWhenGameNotInFront || profile.Match.Exe.Count == 0) return true;
        if (names is null || nowMs - namesAt > 1500)
        {
            try { names = RunningPrograms().Select(ProfileSelector.Normalize).ToHashSet(); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException) { return true; }
            namesAt = nowMs;
        }
        var running = names;
        return FocusGate.Allows(true, profile.Match.Exe, ForegroundExe(), exe => running.Contains(ProfileSelector.Normalize(exe)));
    }

    private bool ResolveProfile(out Profile profile, out DeviceMap device)
    {
        profile = null!; device = null!;
        if (_store.Get(_profileName) is not Profile p)
        {
            Set(MapperState.Error, $"No valid profile called \"{_profileName}\". Fix or select another.");
            return false;
        }
        if (_devices.Resolve(p.Device, null) is not DeviceMap d)
        {
            Set(MapperState.Error, $"Profile \"{p.Name}\" uses unknown device \"{p.Device}\".");
            return false;
        }
        profile = p; device = d;
        return true;
    }

    /// <summary>One connected period: wheel found, pad created, loop running until cancelled or the wheel goes away.</summary>
    private void Session(CancellationToken ct)
    {
        if (!ResolveProfile(out Profile profile, out DeviceMap device))
        {
            // Wait for the user to fix the profile; the store reload bumps its version.
            int v = _store.Version;
            for (int i = 0; i < 20 && !ct.IsCancellationRequested && v == _store.Version && !_profileChanged; i++) ct.WaitHandle.WaitOne(100);
            return;
        }

        using IDirectInput8 di = DInput.DirectInput8Create();
        DeviceInstance? instance = DirectInputWheel.Find(di, _devices, DeviceRegistry.IsAuto(profile.Device) ? null : device, WheelIndex);
        if (instance is null)
        {
            WheelName = ""; WheelIds = "";
            Set(MapperState.WaitingForWheel, WheelIndex == 0 ? "Waiting for the wheel. Plug it in and connect its power supply." : $"Waiting for wheel {WheelIndex + 1}. This copy drives the next wheel after the ones the other copies use.");
            ct.WaitHandle.WaitOne(1500);
            return;
        }

        using DirectInputWheel wheel = DirectInputWheel.Open(di, instance, _window(), _force.Enabled, _force.Style);
        WheelName = instance.ProductName;
        ForceSupported = wheel.ForceSupported;
        ForceActive = wheel.Force is not null;
        ForceMessage = wheel.ForceMessage;
        Emit(ForceActive ? "Force feedback is active."
            : _force.Enabled ? "Force feedback is not active: " + (ForceMessage.Length > 0 ? ForceMessage : "unknown reason") : "Force feedback is off.");
        WheelIds = $"VID_{wheel.Vid:X4} PID_{wheel.Pid:X4}";
        WheelUsbIds = (wheel.Vid, wheel.Pid);

        // A profile that says "auto" uses the calibrated entry for this very wheel (Xbox or PlayStation edition alike).
        if (DeviceRegistry.IsAuto(profile.Device)) device = _devices.Resolve(profile.Device, WheelUsbIds) ?? device;
        WheelCalibrated = _devices.IsKnownWheel(wheel.Vid, wheel.Pid);
        if (!WheelCalibrated)
            Emit($"This wheel ({WheelIds}) has no saved calibration, so the built-in Xbox edition layout is being used. Run Settings, Calibrate to match it to this wheel.");

        IVirtualPad pad;
        PadKind padKind = PadKinds.Of(profile);
        try { pad = _padFactory(padKind); pad.Connect(); }
        catch (Exception ex)
        {
            Set(MapperState.Error, $"Could not create the virtual pad ({ex.Message}). Is ViGEmBus installed?");
            ct.WaitHandle.WaitOne(3000);
            return;
        }

        // An optional second virtual controller, for profiles that send some rules to "P2:" targets.
        IVirtualPad? pad2 = null;
        if (profile.Outputs.SecondPad)
        {
            try { pad2 = _padFactory(padKind); pad2.Connect(); }
            catch (Exception ex) { Emit($"Could not create the second virtual controller ({ex.Message})."); pad2 = null; }
        }

        using (pad)
        using (pad2)
        {
            Emit($"Wheel connected: {WheelName} ({WheelIds}).");
            if (pad2 is not null) Emit("A second virtual controller is connected.");
            if (padKind == PadKind.DualShock4) Emit("The game sees a DualShock 4.");
            if (_centreOverride is int co && device.Axes.TryGetValue("wheel", out var wa0)) wa0.Centre = co;

            // Where the wheel settles: the centre follows it, a few degrees at most from the calibrated one.
            CentreTracker? centreTracker = null;
            if (device.Axes.TryGetValue("wheel", out var wheelAxis) && wheelAxis.Signed && wheelAxis.SourceIndex >= 0 && device.RotationDegrees > 0)
                centreTracker = new CentreTracker(wheelAxis.Centre, (wheelAxis.Max - wheelAxis.Min) / device.RotationDegrees);

            pad.Rumble += (large, small) => { _rumbleLarge = large; _rumbleSmall = small; };
            _restart = false;
            var extras = new List<ExtraReader>();
            IKeySink? keys = null;           // created the first time a profile with keyboard output runs
            bool keyboardWasOn = false;
            long nextScan = 0;
            var engine = new MappingEngine(profile, device, _devices);
            bool keyboardAllowed = ProfileSafety.KeyboardAllowed(profile);   // checked when a profile loads, not every frame
            _profileForce = profile.Force; RefreshEffectiveForce();
            int seenVersion = _store.Version;
            _profileChanged = false;
            var clock = Stopwatch.StartNew();
            long lastFrame = 0, rateAt = 0, frames = 0, lastSubmit = 0;
            int rateSamples = 0;
            PadState? lastSent = null;
            long nextGate = 0, runningAt = -10000;
            bool gateOpen = true;
            string? heldNotice = null;
            HashSet<string>? runningNames = null;
            Set(MapperState.Running, $"Running \"{profile.Name}\"");

            while (!ct.IsCancellationRequested && !wheel.Lost)
            {
                if (_restart) { Emit("Reconnecting to the wheel to change force feedback mode."); break; }
                if (_profileChanged || _store.Version != seenVersion)
                {
                    seenVersion = _store.Version;
                    _profileChanged = false;
                    if (_store.Get(_profileName) is Profile fresh && _devices.Resolve(fresh.Device, WheelUsbIds) is DeviceMap freshDevice)
                    {
                        // A different controller type needs a new virtual pad: reconnect (the game sees it replugged).
                        if (PadKinds.Of(fresh) != padKind || fresh.Outputs.SecondPad != (pad2 is not null))
                        {
                            Emit($"Switching the virtual controller to {(PadKinds.Of(fresh) == PadKind.DualShock4 ? "a DualShock 4" : "an Xbox 360 controller")}.");
                            break;
                        }
                        // Keep a centre that was set at runtime across reloads of the same device.
                        if (freshDevice.Axes.TryGetValue("wheel", out var w) && device.Axes.TryGetValue("wheel", out var old)) w.Centre = old.Centre;
                        profile = fresh;
                        device = freshDevice;
                        engine = new MappingEngine(profile, device, _devices);
                        keyboardAllowed = ProfileSafety.KeyboardAllowed(profile);
                        _profileForce = profile.Force; RefreshEffectiveForce();
                        heldNotice = null;
                        Set(MapperState.Running, $"Running \"{profile.Name}\"");
                        Emit($"Now running \"{profile.Name}\" ({profile.Rules.Count} rules).");
                    }
                    else
                    {
                        Emit($"Profile \"{_profileName}\" is not valid; keeping \"{profile.Name}\".");
                    }
                }

                RawInput raw = wheel.Read();

                if (_recentre)
                {
                    _recentre = false;
                    if (device.Axes.TryGetValue("wheel", out var wa) && wa.SourceIndex >= 0 && raw.Live)
                    {
                        wa.Centre = Math.Clamp(raw.Axes[wa.SourceIndex], wa.Min + 1, wa.Max - 1);
                        centreTracker?.Reset(wa.Centre);
                        Emit($"Wheel centre set to {wa.Centre}.");
                    }
                    else Emit("Move the wheel first so it starts reporting, then set the centre.");
                }

                ApplyForce(wheel, clock.ElapsedMilliseconds);

                if (clock.ElapsedMilliseconds >= nextScan)
                {
                    nextScan = clock.ElapsedMilliseconds + 2000;
                    ScanExtras(di, instance, extras);
                }

                InputFrame input = device.Normalize(raw);

                if (AutoCentre && centreTracker is not null && device.Axes.TryGetValue("wheel", out var liveWheel) && liveWheel.SourceIndex >= 0)
                {
                    bool pedalsIdle = input.Get("throttle") < 0.05 && input.Get("brake") < 0.05 && input.Get("clutch") < 0.05;
                    if (centreTracker.Update(raw.Axes[liveWheel.SourceIndex], clock.ElapsedMilliseconds, raw.Live && pedalsIdle) is int newCentre)
                    {
                        liveWheel.Centre = Math.Clamp(newCentre, liveWheel.Min + 1, liveWheel.Max - 1);
                        input = device.Normalize(raw);   // this very frame already uses the new centre
                        Emit($"Wheel centre followed the wheel to rest ({centreTracker.ShiftDegrees:+0.0;-0.0}° from the calibrated centre).");
                    }
                }
                Dictionary<string, RawInput>? extraRaw = null;
                foreach (ExtraReader x in extras)
                {
                    if (x.Wheel is null) continue;
                    RawInput xr = x.Wheel.Read();
                    if (x.Wheel.Lost)
                    {
                        x.Wheel.Dispose();
                        x.Wheel = null;
                        Emit($"Extra device \"{x.Map.Name}\" was unplugged.");
                        continue;
                    }
                    foreach (var kv in x.Map.Normalize(xr, x.Map.Name + ".").Values) input.Values[kv.Key] = kv.Value;
                    (extraRaw ??= new())[x.Map.Name] = xr;
                }
                PadState state = engine.Evaluate(input, clock.ElapsedMilliseconds);
                long nowMs = clock.ElapsedMilliseconds;

                // Paused, or the game is running but not in front: send a neutral controller (the rules keep tracking the wheel).
                if (nowMs >= nextGate)
                {
                    nextGate = nowMs + 150;
                    gateOpen = GameInFront(profile, nowMs, ref runningNames, ref runningAt);
                }
                string? held = _paused ? "Paused: the controller is released until you resume."
                    : !gateOpen ? "Controller released: the game is not in front."
                    : null;
                if (held != heldNotice)
                {
                    heldNotice = held;
                    Set(MapperState.Running, held ?? $"Running \"{profile.Name}\"");
                    Emit(held is null ? "Controller back on." : held);
                }
                if (held is not null) state = new PadState();

                // Only talk to the driver when something changed (plus a regular heartbeat).
                if (lastSent is null || !state.SameAs(lastSent) || nowMs - lastSubmit >= 8)
                {
                    state.WriteTo(pad);
                    if (pad2 is not null) (state.Second ?? new PadState()).WriteTo(pad2);   // paused or released: the second one is released too
                    lastSent = state;
                    lastSubmit = nowMs;
                }

                frames++;
                if (nowMs - rateAt >= 1000)
                {
                    UpdatesPerSecond = (int)frames;
                    frames = 0;
                    rateAt = nowMs;
                    if (++rateSamples == 3) Emit($"Input loop: about {UpdatesPerSecond} updates per second.");
                }

                // Keyboard output: only while the active profile allows it; released the moment it does not.
                if (keyboardAllowed)
                {
                    keys ??= _liveKeys = _keySinkFactory();
                    keys.Update(state.Keys);
                    keyboardWasOn = true;
                }
                else if (keyboardWasOn)
                {
                    keys?.ReleaseAll();
                    keyboardWasOn = false;
                }

                if (clock.ElapsedMilliseconds - lastFrame >= 33)
                {
                    lastFrame = clock.ElapsedMilliseconds;
                    var snap = new MapperSnapshot(raw, input, state, profile.Name, device) { ExtraRaw = extraRaw ?? new() };
                    Latest = snap;
                    Frame?.Invoke(snap);
                }
                Thread.Sleep(1);
            }

            if (wheel.Lost) Emit("Wheel disconnected; waiting for it to come back.");
            keys?.Dispose();                 // releases anything still held
            _liveKeys = null;
            foreach (ExtraReader x in extras) x.Wheel?.Dispose();
            _extraStatus = [];
            ForceActive = false;
        }
    }

    /// <summary>Connect extra devices that are plugged in and have been set up; forget ones that are no longer configured.</summary>
    private void ScanExtras(IDirectInput8 di, DeviceInstance primary, List<ExtraReader> readers)
    {
        var wanted = _devices.ExtraDevices.ToList();
        readers.RemoveAll(r =>
        {
            if (wanted.Contains(r.Map)) return false;
            r.Wheel?.Dispose();
            return true;
        });
        foreach (DeviceMap map in wanted)
            if (readers.All(r => r.Map != map)) readers.Add(new ExtraReader(map));

        IList<DeviceInstance>? attached = null;
        foreach (ExtraReader r in readers)
        {
            if (r.Wheel is not null || r.Map.UsbIds is not { } ids) continue;
            attached ??= di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
            DeviceInstance? inst = attached.FirstOrDefault(d => d.InstanceGuid != primary.InstanceGuid && DirectInputWheel.Ids(d) == ids);
            if (inst is null) continue;
            try
            {
                r.Wheel = DirectInputWheel.Open(di, inst, _window(), forceFeedback: false);
                Emit($"Extra device connected: {r.Map.Name} ({inst.ProductName}).");
            }
            catch (SharpGen.Runtime.SharpGenException ex) { AppLog.Warn($"Could not open extra device {r.Map.Name}: {ex.Message}"); }
        }
        _extraStatus = readers.Select(r => (r.Map.Name, r.Wheel is not null)).ToList();
    }

    /// <summary>Spring and rumble for this frame. Cheap: the effect layer only talks to the wheel when a value changes.</summary>
    private void ApplyForce(DirectInputWheel wheel, long nowMs)
    {
        WheelForce? f = wheel.Force;
        if (f is null) return;
        ForceSettings s = _effectiveForce;   // app-wide settings with the active profile's own feel laid over them

        if (Interlocked.Exchange(ref _testRequested, 0) == 1)
        {
            _testStart = nowMs;
            _testUntil = nowMs + 2200;
            _testLogged = false;
        }
        f.SetSpring(s.SpringPercent);
        if (nowMs < _testUntil)
        {
            // Heavy motor, then light motor, then both, so each can be felt on its own.
            long t = nowMs - _testStart;
            byte large = t < 800 || t >= 1600 ? (byte)255 : (byte)0;
            byte small = t < 800 ? (byte)0 : (byte)255;
            f.SetRumble(large, small, Math.Max(s.RumblePercent, 40));
            if (!_testLogged && t > 300)
            {
                _testLogged = true;
                Emit("Test vibration: " + f.Diagnose());
            }
        }
        else
        {
            var (large, small) = ForceMath.SelectMotors((byte)_rumbleLarge, (byte)_rumbleSmall, s.Motors);
            f.SetRumble(large, small, s.RumblePercent);
        }

        // If the wheel ever refuses to start an effect, say so instead of failing silently.
        if (f.Problem.Length > 0 && ForceMessage != f.Problem)
        {
            ForceMessage = f.Problem;
            ForceActive = false;
        }
    }
}

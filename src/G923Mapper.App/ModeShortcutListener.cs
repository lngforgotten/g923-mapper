using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace G923Mapper.App;

/// <summary>
/// Listens for the keys and mouse buttons the person chose for switching modes, whichever program is in front. It uses Windows' raw input, which
/// only lets a program watch: the key or click still goes to the game as normal, nothing is intercepted, and no hook is placed in any program.
/// It listens only while at least one shortcut is set; with none, it does not register for input at all. What it sees is checked against the list
/// and thrown away, never stored.
/// </summary>
public sealed class ModeShortcutListener : IDisposable
{
    private const int WmInput = 0x00FF;
    private const uint RidInput = 0x10000003;
    private const uint RidevInputSink = 0x00000100, RidevRemove = 0x00000001;
    private const uint RimTypeMouse = 0, RimTypeKeyboard = 1;
    private const ushort RiKeyBreak = 1;
    private const ushort MiddleDown = 0x10, MiddleUp = 0x20, Button4Down = 0x40, Button4Up = 0x80, Button5Down = 0x100, Button5Up = 0x200;

    private readonly IntPtr _hwnd;
    private readonly HwndSource? _source;
    private readonly Action<string> _action;
    private ModeShortcutMatcher _matcher = new(null);
    private bool _registered;

    /// <param name="action">What to do with a shortcut that fired: "Next", "Previous" or a mode name.</param>
    public ModeShortcutListener(IntPtr hwnd, Action<string> action)
    {
        _hwnd = hwnd;
        _action = action;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(Hook);
    }

    /// <summary>Use this list. An empty list stops listening altogether.</summary>
    public void Apply(IEnumerable<ModeShortcut>? shortcuts)
    {
        _matcher = new ModeShortcutMatcher(shortcuts);
        if (_matcher.IsEmpty) Unregister(); else Register();
    }

    /// <summary>True while the app is registered with Windows to be told about keys and mouse buttons.</summary>
    public bool Listening => _registered;

    private void Register()
    {
        if (_registered) return;
        var devices = new[]
        {
            new RawInputDevice { UsagePage = 1, Usage = 6, Flags = RidevInputSink, Target = _hwnd },   // keyboard
            new RawInputDevice { UsagePage = 1, Usage = 2, Flags = RidevInputSink, Target = _hwnd },   // mouse
        };
        _registered = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>());
        if (!_registered) AppLog.Warn("Mode shortcuts: Windows would not let the app watch the keyboard and mouse (error " + Marshal.GetLastWin32Error() + ").");
        else AppLog.Info("Mode shortcuts: watching for the keys and mouse buttons that were chosen.");
    }

    private void Unregister()
    {
        if (!_registered) return;
        var devices = new[]
        {
            new RawInputDevice { UsagePage = 1, Usage = 6, Flags = RidevRemove, Target = IntPtr.Zero },
            new RawInputDevice { UsagePage = 1, Usage = 2, Flags = RidevRemove, Target = IntPtr.Zero },
        };
        RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>());
        _registered = false;
        _matcher.Reset();
        AppLog.Info("Mode shortcuts: no longer watching the keyboard and mouse.");
    }

    private static HotkeyModifiers Held()
    {
        var m = HotkeyModifiers.None;
        if ((GetAsyncKeyState(0x11) & 0x8000) != 0) m |= HotkeyModifiers.Ctrl;
        if ((GetAsyncKeyState(0x12) & 0x8000) != 0) m |= HotkeyModifiers.Alt;
        if ((GetAsyncKeyState(0x10) & 0x8000) != 0) m |= HotkeyModifiers.Shift;
        if ((GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0) m |= HotkeyModifiers.Win;
        return m;
    }

    /// <summary>True while one of this program's own windows is in front: typing or capturing a shortcut there must not switch modes.</summary>
    private static bool OwnWindowInFront()
    {
        IntPtr front = GetForegroundWindow();
        if (front == IntPtr.Zero) return false;
        GetWindowThreadProcessId(front, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmInput || !_registered) return IntPtr.Zero;

        uint size = 0;
        uint header = (uint)Marshal.SizeOf<RawInputHeader>();
        if (GetRawInputData(lParam, RidInput, IntPtr.Zero, ref size, header) != 0 || size == 0) return IntPtr.Zero;
        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RidInput, buffer, ref size, header) != size) return IntPtr.Zero;
            var head = Marshal.PtrToStructure<RawInputHeader>(buffer);
            IntPtr data = buffer + (int)header;

            ModeShortcut? fired = null;
            if (head.Type == RimTypeKeyboard)
            {
                var key = Marshal.PtrToStructure<RawKeyboard>(data);
                int vk = key.VKey;
                if (vk is 0 or 0xFF) return IntPtr.Zero;
                if ((key.Flags & RiKeyBreak) != 0) _matcher.KeyUp(vk);
                else fired = _matcher.KeyDown(vk, Held());
            }
            else if (head.Type == RimTypeMouse)
            {
                var mouse = Marshal.PtrToStructure<RawMouse>(data);
                ushort b = mouse.ButtonFlags;
                if ((b & MiddleUp) != 0) _matcher.MouseUp(MouseTriggerButton.Middle);
                if ((b & Button4Up) != 0) _matcher.MouseUp(MouseTriggerButton.Back);
                if ((b & Button5Up) != 0) _matcher.MouseUp(MouseTriggerButton.Forward);
                if ((b & MiddleDown) != 0) fired ??= _matcher.MouseDown(MouseTriggerButton.Middle, Held());
                if ((b & Button4Down) != 0) fired ??= _matcher.MouseDown(MouseTriggerButton.Back, Held());
                if ((b & Button5Down) != 0) fired ??= _matcher.MouseDown(MouseTriggerButton.Forward, Held());
            }

            if (fired is not null && !OwnWindowInFront()) _action(fired.Action.Trim());
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(Hook);
    }

    // ---------------------------------------------------------------- Windows

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice { public ushort UsagePage; public ushort Usage; public uint Flags; public IntPtr Target; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader { public uint Type; public uint Size; public IntPtr Device; public IntPtr WParam; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboard { public ushort MakeCode; public ushort Flags; public ushort Reserved; public ushort VKey; public uint Message; public uint ExtraInformation; }

    [StructLayout(LayoutKind.Explicit)]
    private struct RawMouse
    {
        [FieldOffset(0)] public ushort Flags;
        [FieldOffset(4)] public uint Buttons;
        [FieldOffset(4)] public ushort ButtonFlags;
        [FieldOffset(6)] public ushort ButtonData;
        [FieldOffset(8)] public uint RawButtons;
        [FieldOffset(12)] public int LastX;
        [FieldOffset(16)] public int LastY;
        [FieldOffset(20)] public uint ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint count, uint size);
    [DllImport("user32.dll")] private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}

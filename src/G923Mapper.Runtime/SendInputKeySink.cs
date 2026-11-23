using System.Runtime.InteropServices;

namespace G923Mapper;

/// <summary>
/// Holds keys down with the Win32 SendInput call, using scan codes (what games that read the keyboard through
/// DirectInput or raw input expect). Everything held is released on <see cref="ReleaseAll"/> and on dispose.
/// </summary>
public sealed class SendInputKeySink : IKeySink
{
    private const uint InputMouse = 0, InputKeyboard = 1;
    private const uint KeyEventExtended = 0x0001, KeyEventKeyUp = 0x0002, KeyEventScanCode = 0x0008;

    private readonly KeyStateTracker _tracker = new();

    public void Update(IReadOnlyCollection<string> wanted)
    {
        var (press, release) = _tracker.Update(wanted);
        foreach (string key in release) Send(key, down: false);
        foreach (string key in press) Send(key, down: true);
    }

    public void ReleaseAll()
    {
        foreach (string key in _tracker.ReleaseAll()) Send(key, down: false);
    }

    public void Dispose() => ReleaseAll();

    private static void SendMouse(string key, bool down)
    {
        // (down flag, up flag, XBUTTON data)
        (uint Down, uint Up, uint Data)? b = KeyNames.Canonical(key) switch
        {
            "Mouse:Left" => (0x0002, 0x0004, 0),
            "Mouse:Right" => (0x0008, 0x0010, 0),
            "Mouse:Middle" => (0x0020, 0x0040, 0),
            "Mouse:X1" => (0x0080, 0x0100, 1),
            "Mouse:X2" => (0x0080, 0x0100, 2),
            _ => null,
        };
        if (b is not { } button) return;

        var input = new INPUT { type = InputMouse, u = new InputUnion { mi = new MOUSEINPUT { mouseData = button.Data, dwFlags = down ? button.Down : button.Up } } };
        uint sent = SendInput(1, [input], Marshal.SizeOf<INPUT>());
        if (sent == 0) AppLog.Warn($"SendInput failed for {key} ({(down ? "down" : "up")}), error {Marshal.GetLastWin32Error()}.");
    }

    private static void Send(string key, bool down)
    {
        if (KeyNames.IsMouse(key)) { SendMouse(key, down); return; }
        if (!KeyNames.TryGet(key, out KeyCode code)) return;
        ushort scan = (ushort)MapVirtualKey(code.Vk, 0 /* MAPVK_VK_TO_VSC */);
        uint flags = KeyEventScanCode | (down ? 0 : KeyEventKeyUp) | (code.Extended ? KeyEventExtended : 0);

        var input = new INPUT { type = InputKeyboard, u = new InputUnion { ki = new KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = flags } } };
        uint sent = SendInput(1, [input], Marshal.SizeOf<INPUT>());
        if (sent == 0) AppLog.Warn($"SendInput failed for key {key} ({(down ? "down" : "up")}), error {Marshal.GetLastWin32Error()}.");
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }

    // MOUSEINPUT is the largest member and fixes the union's size, so INPUT is the 40 bytes Windows expects on x64.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
}

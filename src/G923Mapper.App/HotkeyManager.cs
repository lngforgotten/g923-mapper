using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace G923Mapper.App;

/// <summary>Registers one system-wide shortcut with Windows and calls back when it is pressed, whichever program is in front.</summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;   // holding the keys does not fire again and again
    private readonly int _id;

    private readonly IntPtr _hwnd;
    private readonly HwndSource? _source;
    private readonly Action _pressed;
    private bool _registered;

    /// <param name="id">Which shortcut this is to Windows; every manager in the program needs its own.</param>
    public HotkeyManager(IntPtr hwnd, Action pressed, int id = 0x6923)
    {
        _id = id;
        _hwnd = hwnd;
        _pressed = pressed;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(Hook);
    }

    /// <summary>The shortcut now registered, or null.</summary>
    public Hotkey? Current { get; private set; }

    /// <summary>Replace the shortcut. Returns false (and registers nothing) if another program already uses it.</summary>
    public bool Register(Hotkey hotkey)
    {
        Unregister();
        // HotkeyModifiers uses the same bit values as Windows (Alt 1, Ctrl 2, Shift 4, Win 8).
        _registered = RegisterHotKey(_hwnd, _id, (uint)hotkey.Modifiers | ModNoRepeat, (uint)hotkey.VirtualKey);
        Current = _registered ? hotkey : null;
        return _registered;
    }

    public void Unregister()
    {
        if (_registered) UnregisterHotKey(_hwnd, _id);
        _registered = false;
        Current = null;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == _id)
        {
            handled = true;
            _pressed();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(Hook);
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

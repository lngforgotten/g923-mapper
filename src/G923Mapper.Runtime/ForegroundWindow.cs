using System.Diagnostics;
using System.Runtime.InteropServices;

namespace G923Mapper;

/// <summary>Which program owns the window that is in front right now.</summary>
public static class ForegroundWindow
{
    /// <summary>The program's name with .exe (for example "GTA5_Enhanced.exe"), or null if there is no foreground window or it cannot be read.</summary>
    public static string? ExeName()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            if (GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0) return null;
            using Process p = Process.GetProcessById((int)pid);
            return p.ProcessName + ".exe";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;     // the program closed between the two calls, or Windows would not say
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace G923Mapper;

/// <summary>
/// Makes Thread.Sleep(1) really sleep about 1 ms instead of about 15.6 ms, so the input loop can run at
/// several hundred updates a second. Two things are needed on Windows 11: ask for a 1 ms timer, and opt
/// this process out of power throttling, which otherwise ignores that request whenever the app has no
/// visible window (which is exactly the case for a tray app running behind a game).
/// </summary>
public sealed class TimerResolution : IDisposable
{
    private const uint ProcessPowerThrottling = 4;
    private const uint ThrottlingExecutionSpeed = 0x1, ThrottlingIgnoreTimerResolution = 0x4;

    private bool _active;

    private TimerResolution() { }

    public static TimerResolution Begin()
    {
        OptOutOfPowerThrottling();
        var t = new TimerResolution();
        try { t._active = timeBeginPeriod(1) == 0; }
        catch (DllNotFoundException) { }
        AppLog.Info(t._active ? "Timer resolution set to 1 ms." : "Could not set a 1 ms timer resolution.");
        return t;
    }

    public void Dispose()
    {
        if (!_active) return;
        _active = false;
        try { timeEndPeriod(1); } catch (DllNotFoundException) { }
    }

    private static void OptOutOfPowerThrottling()
    {
        try
        {
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = 1,
                ControlMask = ThrottlingExecutionSpeed | ThrottlingIgnoreTimerResolution,
                StateMask = 0,      // control these two settings, and turn throttling OFF for both
            };
            bool ok = SetProcessInformation(Process.GetCurrentProcess().Handle, (int)ProcessPowerThrottling, ref state, Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
            if (!ok) AppLog.Info($"Power-throttling opt-out not available (error {Marshal.GetLastWin32Error()}); fine on older Windows.");
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE { public uint Version, ControlMask, StateMask; }

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint milliseconds);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int informationClass, ref PROCESS_POWER_THROTTLING_STATE info, int size);
}

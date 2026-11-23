using System.ComponentModel;
using System.Diagnostics;

namespace G923Mapper;

/// <summary>
/// Lists the virtual Xbox 360 controllers Windows knows about and removes the leftover ones (entries that stay in Device Manager
/// after a crash). Uses Windows' own pnputil; removing devices needs administrator rights, so that runs through one permission prompt.
/// </summary>
public sealed class VirtualPadClient
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>The virtual controllers, or null if Windows could not be asked.</summary>
    public IReadOnlyList<VirtualPadInfo>? List()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("pnputil.exe", "/enum-devices /class XnaComposite")
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
            });
            if (p is null) return null;
            p.StandardInput.Close();
            var output = p.StandardOutput.ReadToEndAsync();
            var errors = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit((int)Timeout.TotalMilliseconds))
            {
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                AppLog.Warn("pnputil did not answer in time.");
                return null;
            }
            Task.WaitAll([output, errors], 2000);
            return output.IsCompletedSuccessfully ? VirtualPadText.Parse(output.Result) : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            AppLog.Warn($"Could not list virtual controllers: {ex.Message}");
            return null;
        }
    }

    /// <summary>Remove the given leftover entries (after one administrator prompt). Only virtual-controller ids are ever accepted.</summary>
    public HidHideResult Remove(IEnumerable<string> instanceIds)
    {
        string? args = VirtualPadText.RemoveBatch(instanceIds);
        if (args is null) return new(false, "Nothing to remove.");
        AppLog.Info("Removing leftover virtual controllers (elevated): " + args);
        try
        {
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", args)
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (p is null) return new(false, "Could not start the removal.");
            if (!p.WaitForExit(30000)) return new(false, "Windows did not finish in time.");
            return new(true, "");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new(false, "Cancelled. Administrator permission is needed to remove devices.");
        }
        catch (Win32Exception ex)
        {
            return new(false, "Could not run as administrator: " + ex.Message);
        }
    }
}

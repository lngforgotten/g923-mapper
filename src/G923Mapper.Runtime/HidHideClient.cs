using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace G923Mapper;

public sealed record HidHideResult(bool Success, string Message);

/// <summary>
/// Drives HidHide's command-line tool: finds it, lists devices, and hides or unhides the wheel. Changing
/// HidHide's configuration needs administrator rights, so those changes run through one UAC prompt.
/// </summary>
public sealed class HidHideClient
{
    public const string DownloadUrl = "https://github.com/nefarius/HidHide/releases";

    private readonly string? _cli;

    /// <summary>How long to wait for the command-line tool to answer a question before giving up.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(8);

    public HidHideClient(string? cliPath = null) => _cli = cliPath ?? Locate();

    public bool IsInstalled => _cli is not null;

    public static string? Locate()
    {
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] candidates =
        [
            Path.Combine(pf, "Nefarius Software Solutions", "HidHide", "x64", "HidHideCLI.exe"),
            Path.Combine(pf, "Nefarius Software Solutions", "HidHide", "HidHideCLI.exe"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Whether any interface of the wheel (by USB ids) is currently hidden. Null if HidHide cannot be asked.</summary>
    public bool? IsWheelHidden(int vid, int pid)
    {
        if (_cli is null) return null;
        string? output = Run("--dev-list");
        return output is null ? null : HidHideText.IsHidden(HidHideText.ParseHiddenList(output), vid, pid);
    }

    /// <summary>Hide the wheel from every program except this one, then switch hiding on. Shows one UAC prompt.</summary>
    public HidHideResult Hide(int vid, int pid, string appPath)
    {
        if (_cli is null) return new(false, "HidHide is not installed.");
        var devices = HidHideText.FindGaming(ListAll(), vid, pid);
        if (devices.Count == 0)
            return new(false, "HidHide did not list the wheel. Make sure it is plugged in, then try again.");

        var result = RunElevated(HidHideCommands.Hide(devices.Select(d => d.InstancePath), appPath));
        if (!result.Success) return result;

        bool? hidden = IsWheelHidden(vid, pid);
        return hidden == false
            ? new(false, "HidHide accepted the commands but the wheel is not listed as hidden. A restart of HidHide or Windows may be needed.")
            : new(true, "The wheel is now hidden from games. Only this app can see it.");
    }

    public HidHideResult Unhide(int vid, int pid, string appPath)
    {
        if (_cli is null) return new(false, "HidHide is not installed.");
        var hidden = (Run("--dev-list") is { } o ? HidHideText.ParseHiddenList(o) : [])
            .Where(p => p.Contains($"VID_{vid:X4}&PID_{pid:X4}", StringComparison.OrdinalIgnoreCase)).ToList();
        if (hidden.Count == 0) return new(true, "The wheel was not hidden.");
        var result = RunElevated(HidHideCommands.Unhide(hidden, appPath));
        return result.Success ? new(true, "The wheel is visible to games again.") : result;
    }

    private IReadOnlyList<HidDeviceInfo> ListAll()
    {
        string? all = Run("--dev-all");
        return all is null ? [] : HidHideText.ParseDevices(all);
    }

    /// <summary>Run the CLI without elevation and capture its output; null if it could not run or took too long.</summary>
    private string? Run(string arguments)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(_cli!, arguments)
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
            });
            if (p is null) return null;

            // The tool can sit waiting on its input when it has no console (it did in testing), so give it an
            // already-closed input, read both outputs without blocking, and enforce the timeout ourselves.
            p.StandardInput.Close();
            var output = p.StandardOutput.ReadToEndAsync();
            var errors = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit((int)Timeout.TotalMilliseconds))
            {
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                AppLog.Warn($"HidHide CLI did not answer \"{arguments}\" within {Timeout.TotalSeconds:0.#} seconds.");
                return null;
            }
            Task.WaitAll([output, errors], 2000);
            return p.ExitCode == 0 && output.IsCompletedSuccessfully ? output.Result : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            AppLog.Warn($"HidHide CLI failed: {ex.Message}");
            return null;
        }
    }

    private HidHideResult RunElevated(IReadOnlyList<string> commands)
    {
        string args = HidHideCommands.Batch(_cli!, commands);
        AppLog.Info("HidHide (elevated): " + args);
        try
        {
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", args)
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (p is null) return new(false, "Could not start HidHide.");
            if (!p.WaitForExit(20000)) return new(false, "HidHide did not finish in time.");
            return p.ExitCode == 0 ? new(true, "") : new(false, $"HidHide reported an error (exit code {p.ExitCode}).");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new(false, "Cancelled. Administrator permission is needed to change what HidHide hides.");
        }
        catch (Win32Exception ex)
        {
            return new(false, "Could not run HidHide as administrator: " + ex.Message);
        }
    }
}

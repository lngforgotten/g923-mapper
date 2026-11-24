using System;
using System.Net.Http;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace G923Mapper.App;

/// <summary>Asks GitHub whether a newer release exists. One small request; nothing about you or this PC is sent (the client is just called "G923Mapper").</summary>
internal static class UpdateChecker
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>The newest release compared with this version, or a sentence saying why that could not be found out.</summary>
    public static async Task<(UpdateInfo? Info, string? Problem)> CheckAsync(string? repoText)
    {
        string? repo = UpdateCheck.NormaliseRepo(UpdateCheck.RepoOrDefault(repoText));
        if (repo is null) return (null, "Enter a GitHub repository as owner/name (or its github.com address) first.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UpdateCheck.ApiUrl(repo));
            request.Headers.UserAgent.ParseAdd("G923Mapper");           // a generic name: nothing about you or this PC
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Http.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (null, "GitHub has no such repository, or it has no releases yet (or it is private).");
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync();
            Version current = typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(1, 0, 0);
            return (UpdateCheck.Interpret(json, current, repo), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (null, "Could not reach GitHub (" + (ex is TaskCanceledException ? "no answer in time" : ex.Message) + ").");
        }
    }
}

/// <summary>
/// Looks for a new version by itself, quietly: a few seconds after every start-up, and then about every six hours while the app keeps running in the tray. Switched on by default
/// and off with one tick in Settings. When one is found it is shown on Home (with a Download button) and in one Windows notification per version.
/// A check that could not reach GitHub is simply tried again later.
/// </summary>
internal sealed class AutoUpdate : IDisposable
{
    /// <summary>
    /// A start-up check happens every time the app starts, but not if one was made in the last few minutes (so closing and reopening, or a program
    /// that keeps restarting, cannot hammer GitHub). While running, it looks again every few hours.
    /// </summary>
    private static readonly TimeSpan LaunchGap = TimeSpan.FromMinutes(10), RunningGap = TimeSpan.FromHours(6);
    private readonly AppServices _s;
    private readonly Action<UpdateInfo> _found;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(1) };
    private readonly DispatcherTimer _first = new() { Interval = TimeSpan.FromSeconds(8) };
    private bool _busy;

    public AutoUpdate(AppServices services, Action<UpdateInfo> found)
    {
        _s = services;
        _found = found;
        _timer.Tick += async (_, _) => await TickAsync(RunningGap);
        _first.Tick += async (_, _) => { _first.Stop(); await TickAsync(LaunchGap); _timer.Start(); };
    }

    public void Start() => _first.Start();

    /// <summary>Whether enough time has passed since the last successful check.</summary>
    internal static bool Due(string? lastUtc, DateTime nowUtc, TimeSpan gap) => UpdateCheck.Due(lastUtc, nowUtc, gap);

    private async System.Threading.Tasks.Task TickAsync(TimeSpan gap)
    {
        if (_busy) return;
        if (!_s.Settings.AutoUpdateCheck) { AppLog.Info("Automatic update check skipped: it is switched off in Settings."); return; }
        if (!Due(_s.Settings.LastUpdateCheckUtc, DateTime.UtcNow, gap)) { AppLog.Info($"Automatic update check skipped: one was made at {_s.Settings.LastUpdateCheckUtc}."); return; }
        _busy = true;
        try
        {
            var (info, problem) = await UpdateChecker.CheckAsync(_s.Settings.UpdateRepo);
            if (info is null)
            {
                AppLog.Info("Automatic update check: " + problem);
                if (problem is not null && problem.StartsWith("GitHub has no such", StringComparison.Ordinal)) Remember();   // nothing to find; do not ask again today
                return;
            }
            Remember();
            AppLog.Info("Automatic update check: " + info.Message);
            if (info.Newer) _found(info);
        }
        finally { _busy = false; }
    }

    private void Remember()
    {
        _s.Settings.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o");
        _s.Settings.Save();
    }

    public void Dispose() { _timer.Stop(); _first.Stop(); }
}

/// <summary>Where a new version is in its way to being installed.</summary>
public enum UpdatePhase { None, Available, Downloading, Ready, Failed }

/// <summary>
/// Downloads a new version from the repository's GitHub release and gets it ready: the zip that matches how this copy was installed (the full build
/// or the small one), checked against the checksum GitHub publishes for it, with only G923Mapper.exe taken out of it and its version checked.
/// Nothing is installed here; the ready program waits in the app's data folder until it is swapped in.
/// </summary>
internal static class UpdateInstaller
{
    private static readonly HttpClient Http = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    /// <summary>Whether this copy is the full build, which carries .NET with it, rather than the small one.</summary>
    public static bool IsFullBuild()
    {
        string? exe = Environment.ProcessPath;
        return exe is not null && System.IO.File.Exists(exe) && UpdatePackage.LooksSelfContained(new System.IO.FileInfo(exe).Length);
    }

    public static async Task<(StagedUpdate? Staged, string Message)> StageAsync(UpdateInfo info, string? repoText, IProgress<int>? progress, System.Threading.CancellationToken ct = default)
    {
        string? repo = UpdateCheck.NormaliseRepo(UpdateCheck.RepoOrDefault(repoText));
        if (repo is null) return (null, "The update repository is not a valid GitHub address.");
        UpdateAsset? asset = UpdatePackage.Choose(info.Assets ?? [], IsFullBuild(), repo);
        if (asset is null) return (null, "That release has no download for this build of the app.");
        Version? version = UpdateCheck.ParseVersion(info.LatestTag);
        if (version is null) return (null, "That release has no version number.");

        string downloadDir = System.IO.Path.Combine(AppSettings.UpdateDir, "download");
        string zip = System.IO.Path.Combine(downloadDir, asset.Name);
        try
        {
            UpdatePackage.ClearStaged(AppSettings.UpdateDir);          // whatever was waiting before is replaced
            System.IO.Directory.CreateDirectory(downloadDir);

            using (var request = new HttpRequestMessage(HttpMethod.Get, asset.Url))
            {
                request.Headers.UserAgent.ParseAdd("G923Mapper");       // a generic name: nothing about you or this PC
                using var timeout = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromMinutes(20));
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps) return (null, "The download was not served over https, so it was not used.");
                long total = response.Content.Headers.ContentLength ?? asset.Size;
                await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
                await using var target = System.IO.File.Create(zip);
                var buffer = new byte[81920];
                long done = 0; int lastPercent = -1;
                int read;
                while ((read = await source.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                    done += read;
                    int percent = total > 0 ? (int)Math.Min(99, done * 100 / total) : 0;
                    if (percent != lastPercent) { lastPercent = percent; progress?.Report(percent); }
                }
            }

            switch (UpdatePackage.CheckDigest(zip, asset.Sha256))
            {
                case DigestCheck.Mismatch:
                    return (null, "The download does not match the checksum GitHub published for it, so it was thrown away.");
                case DigestCheck.NoDigest:
                    AppLog.Warn("GitHub gave no checksum for this download; relying on https from the repository's own release.");
                    break;
            }

            string? exe = UpdatePackage.ExtractExe(zip, AppSettings.StagedDir, out string problem);
            if (exe is null) return (null, problem);
            Version? inside = UpdatePackage.FileVersion(exe);
            if (inside is null || inside.Major != version.Major || inside.Minor != version.Minor || inside.Build != Math.Max(0, version.Build))
            {
                UpdatePackage.ClearStaged(AppSettings.UpdateDir);
                return (null, $"The program in the download says it is version {inside?.ToString(3) ?? "(unknown)"}, not {version.ToString(3)}, so it was not used.");
            }
            UpdatePackage.WriteStaged(AppSettings.StagedDir, info.LatestTag, version, exe);
            try { System.IO.Directory.Delete(downloadDir, recursive: true); } catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
            progress?.Report(100);
            return (UpdatePackage.ReadStaged(AppSettings.StagedDir), "");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.IO.IOException or UnauthorizedAccessException)
        {
            UpdatePackage.ClearStaged(AppSettings.UpdateDir);
            return (null, ex is TaskCanceledException ? "The download took too long." : "The download failed: " + ex.Message);
        }
    }
}



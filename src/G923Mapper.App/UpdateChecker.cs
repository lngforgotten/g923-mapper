using System;
using System.Net.Http;
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
        string? repo = UpdateCheck.NormaliseRepo(repoText);
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
/// Looks for a new version by itself, quietly: a little after start-up and then about once a day while the app is running. Switched on by default
/// and off with one tick in Settings. When one is found it is shown on Home (with a Download button) and in one Windows notification per version.
/// A check that could not reach GitHub is simply tried again later.
/// </summary>
internal sealed class AutoUpdate : IDisposable
{
    private static readonly TimeSpan MinGap = TimeSpan.FromHours(20);
    private readonly AppServices _s;
    private readonly Action<UpdateInfo> _found;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(1) };
    private readonly DispatcherTimer _first = new() { Interval = TimeSpan.FromSeconds(25) };
    private bool _busy;

    public AutoUpdate(AppServices services, Action<UpdateInfo> found)
    {
        _s = services;
        _found = found;
        _timer.Tick += async (_, _) => await TickAsync();
        _first.Tick += async (_, _) => { _first.Stop(); await TickAsync(); _timer.Start(); };
    }

    public void Start() => _first.Start();

    /// <summary>Whether enough time has passed since the last successful check.</summary>
    internal static bool Due(string? lastUtc, DateTime nowUtc) => UpdateCheck.Due(lastUtc, nowUtc, MinGap);
    private async System.Threading.Tasks.Task TickAsync()
    {
        if (_busy || !_s.Settings.AutoUpdateCheck || string.IsNullOrWhiteSpace(_s.Settings.UpdateRepo)) return;
        if (!Due(_s.Settings.LastUpdateCheckUtc, DateTime.UtcNow)) return;
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

using System.Text.Json;
using System.Text.RegularExpressions;

namespace G923Mapper;

/// <summary>The outcome of asking a GitHub repository whether there is a newer release.</summary>
public sealed record UpdateInfo(bool Newer, string LatestTag, string Url, string Message);

/// <summary>
/// The pure half of "check for updates": turning what the user typed into a repository, reading GitHub's "latest release" answer, and comparing
/// version numbers. Nothing here touches the network, and nothing is ever checked unless the user sets a repository and asks.
/// </summary>
public static partial class UpdateCheck
{
    [GeneratedRegex(@"^(?:https?://)?(?:www\.)?github\.com/([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+?)(?:\.git)?/?(?:/.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex RepoUrl();

    [GeneratedRegex(@"^([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+)$")]
    private static partial Regex RepoShort();

    [GeneratedRegex(@"\d+(\.\d+){0,3}")]
    private static partial Regex VersionNumber();

    /// <summary>"owner/name" from "owner/name" or a github.com address; null if it is neither.</summary>
    public static string? NormaliseRepo(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (RepoShort().Match(text) is { Success: true } s) return $"{s.Groups[1].Value}/{s.Groups[2].Value}";
        if (RepoUrl().Match(text) is { Success: true } u) return $"{u.Groups[1].Value}/{u.Groups[2].Value}";
        return null;
    }

    public static string ApiUrl(string repo) => $"https://api.github.com/repos/{repo}/releases/latest";

    /// <summary>The first version number in a release tag such as "v1.2.3" or "release-2.0", or null.</summary>
    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || VersionNumber().Match(tag) is not { Success: true } m) return null;
        string v = m.Value;
        if (!v.Contains('.')) v += ".0";
        return Version.TryParse(v, out var version) ? version : null;
    }

    public static bool IsNewer(Version current, Version latest) =>
        Normalise(latest).CompareTo(Normalise(current)) > 0;

    private static Version Normalise(Version v) => new(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build), Math.Max(0, v.Revision));

    /// <summary>Read GitHub's latest-release JSON and compare it with the running version.</summary>
    public static UpdateInfo Interpret(string json, Version current, string repo)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : $"https://github.com/{repo}/releases";
            if (url.Length == 0) url = $"https://github.com/{repo}/releases";

            Version? latest = ParseVersion(tag);
            if (latest is null)
                return new UpdateInfo(false, tag, url, tag.Length == 0 ? "That repository has no releases yet." : $"The latest release (\"{tag}\") has no version number I can compare.");
            return IsNewer(current, latest)
                ? new UpdateInfo(true, tag, url, $"A newer version is available: {tag} (you have {current.ToString(3)}).")
                : new UpdateInfo(false, tag, url, $"You are up to date ({current.ToString(3)}; the latest release is {tag}).");
        }
        catch (JsonException)
        {
            return new UpdateInfo(false, "", $"https://github.com/{repo}/releases", "GitHub's answer could not be read.");
        }
    }
}

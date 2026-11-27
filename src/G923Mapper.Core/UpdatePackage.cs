using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace G923Mapper;

/// <summary>One file attached to a GitHub release.</summary>
/// <param name="Sha256">GitHub's own SHA-256 of the file, as lower-case hex, when the release answer carries one.</param>
public sealed record UpdateAsset(string Name, string Url, long Size, string? Sha256);

/// <summary>A downloaded and checked update waiting to be swapped in.</summary>
public sealed record StagedUpdate(Version Version, string Tag, string ExePath);

/// <summary>How a download compared with the checksum GitHub published for it.</summary>
public enum DigestCheck { Match, NoDigest, Mismatch }

/// <summary>
/// The parts of updating the app that need no window and no network: picking the right download from a release, checking it, taking the
/// program out of the zip, remembering what is ready, and swapping the new program in for the old one. The program is one file, and Windows
/// lets a running program be renamed (though not overwritten or deleted), so the swap is: rename the old file aside, copy the new one in,
/// and put the old one back if anything goes wrong.
/// </summary>
public static class UpdatePackage
{
    public const string ExeName = "G923Mapper.exe";
    public const string FullZip = "G923Mapper-win-x64.zip";
    public const string SmallZip = "G923Mapper-small-win-x64.zip";

    /// <summary>The full build carries .NET with it and is far bigger than the small one (about 70 MB against 1 MB).</summary>
    private const long SelfContainedThreshold = 20_000_000;
    private const long MinExeBytes = 500_000, MaxExeBytes = 400_000_000;

    // ---------------------------------------------------------------- choosing the download

    /// <summary>The files attached to a release, from GitHub's "latest release" answer. Anything that cannot be read is left out.</summary>
    public static IReadOnlyList<UpdateAsset> ReadAssets(string json)
    {
        var list = new List<UpdateAsset>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return list;
            foreach (var a in assets.EnumerateArray())
            {
                string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                string url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                long size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out long sz) ? sz : 0;
                string? digest = a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? NormaliseDigest(d.GetString()) : null;
                if (name.Length > 0 && url.Length > 0) list.Add(new UpdateAsset(name, url, size, digest));
            }
        }
        catch (JsonException) { }
        return list;
    }

    /// <summary>"sha256:ABC..." or "ABC..." as lower-case hex; null for anything else.</summary>
    public static string? NormaliseDigest(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (text.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) text = text[7..];
        return text.Length == 64 && text.All(Uri.IsHexDigit) ? text.ToLowerInvariant() : null;
    }

    /// <summary>
    /// The zip that matches how this copy was installed (the full build or the small one), and only if it is a download from that repository's
    /// releases on GitHub over https. Null when the release has no such file.
    /// </summary>
    public static UpdateAsset? Choose(IEnumerable<UpdateAsset> assets, bool selfContained, string repo)
    {
        string wanted = selfContained ? FullZip : SmallZip;
        string prefix = $"https://github.com/{repo}/releases/download/";
        return assets.FirstOrDefault(a => string.Equals(a.Name, wanted, StringComparison.OrdinalIgnoreCase)
                                          && a.Url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether a program of this size is the full build.</summary>
    public static bool LooksSelfContained(long exeBytes) => exeBytes >= SelfContainedThreshold;

    // ---------------------------------------------------------------- checking and unpacking

    public static DigestCheck CheckDigest(string path, string? expectedSha256)
    {
        if (NormaliseDigest(expectedSha256) is not { } expected) return DigestCheck.NoDigest;
        using var stream = File.OpenRead(path);
        string actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return actual == expected ? DigestCheck.Match : DigestCheck.Mismatch;
    }

    /// <summary>
    /// Take G923Mapper.exe out of the zip into <paramref name="destDir"/>. Only that one entry is read, by its exact name and never by a path inside
    /// the zip, and it must look like a program of a sensible size. Null, with the reason, when it does not.
    /// </summary>
    public static string? ExtractExe(string zipPath, string destDir, out string problem)
    {
        problem = "";
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, ExeName, StringComparison.OrdinalIgnoreCase));
            if (entry is null) { problem = $"The download has no {ExeName} in it."; return null; }
            if (entry.Length is < MinExeBytes or > MaxExeBytes) { problem = $"{ExeName} in the download has an unexpected size."; return null; }
            Directory.CreateDirectory(destDir);
            string target = Path.Combine(destDir, ExeName);
            entry.ExtractToFile(target, overwrite: true);
            if (!LooksLikeProgram(target)) { File.Delete(target); problem = $"{ExeName} in the download is not a Windows program."; return null; }
            return target;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            problem = "The download could not be unpacked: " + ex.Message;
            return null;
        }
    }

    /// <summary>A Windows program starts with the two letters MZ.</summary>
    public static bool LooksLikeProgram(string path)
    {
        try
        {
            using var s = File.OpenRead(path);
            return s.Length >= MinExeBytes && s.ReadByte() == 'M' && s.ReadByte() == 'Z';
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>The version number inside a program file, or null when it has none.</summary>
    public static Version? FileVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return Version.TryParse(info.FileVersion ?? info.ProductVersion?.Split('+')[0], out var v) ? v : null;
        }
        catch (Exception ex) when (ex is IOException or FileNotFoundException) { return null; }
    }

    // ---------------------------------------------------------------- what is ready

    private const string StagedFile = "staged.json";

    /// <summary>Remember that this program file, for this version, has been downloaded and checked.</summary>
    public static void WriteStaged(string stageDir, string tag, Version version, string exePath)
    {
        Directory.CreateDirectory(stageDir);
        var json = JsonSerializer.Serialize(new { tag, version = version.ToString(), exe = Path.GetFileName(exePath) });
        File.WriteAllText(Path.Combine(stageDir, StagedFile), json);
    }

    /// <summary>The update waiting in <paramref name="stageDir"/>, or null when there is none (or what is there is damaged).</summary>
    public static StagedUpdate? ReadStaged(string stageDir)
    {
        try
        {
            string file = Path.Combine(stageDir, StagedFile);
            if (!File.Exists(file)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            string tag = doc.RootElement.GetProperty("tag").GetString() ?? "";
            string exe = Path.GetFileName(doc.RootElement.GetProperty("exe").GetString() ?? "");
            if (!Version.TryParse(doc.RootElement.GetProperty("version").GetString(), out var version) || exe.Length == 0) return null;
            string path = Path.Combine(stageDir, exe);
            return LooksLikeProgram(path) ? new StagedUpdate(version, tag, path) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    /// <summary>Forget (and delete) whatever is waiting.</summary>
    public static void ClearStaged(string stageDir)
    {
        try { if (Directory.Exists(stageDir)) Directory.Delete(stageDir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ---------------------------------------------------------------- telling the person afterwards

    /// <summary>Leave a note that the app has just updated itself to this version, for the new copy to say so once when it starts.</summary>
    public static void WriteUpdatedMarker(string path, string tag, Version version)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, JsonSerializer.Serialize(new { tag, version = version.ToString() }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The tag of the version the app has just updated itself to, if there is a note and it is for the version that is running now, and the note
    /// is removed either way so it is said once. Null when there is nothing to say (no note, or one left over from some other version).
    /// </summary>
    public static string? TakeUpdatedMarker(string path, Version running)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            string? tag = doc.RootElement.GetProperty("tag").GetString();
            bool ok = Version.TryParse(doc.RootElement.GetProperty("version").GetString(), out var v)
                      && v.Major == running.Major && v.Minor == running.Minor && Math.Max(0, v.Build) == Math.Max(0, running.Build);
            try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return ok && !string.IsNullOrWhiteSpace(tag) ? tag : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or KeyNotFoundException or InvalidOperationException or UnauthorizedAccessException)
        {
            try { File.Delete(path); } catch (Exception) { }
            return null;
        }
    }

    // ---------------------------------------------------------------- swapping it in

    /// <summary>
    /// The arguments for the copy started after "Restart now" or "Install now". It comes back the way it was just being used: with its window if
    /// that was showing, in the tray if not. Passing on the old ones as they were would hide a window the person was looking at, because a copy
    /// started with Windows was started "--minimized".
    /// </summary>
    public static IReadOnlyList<string> RestartArguments(IEnumerable<string> current, bool windowShowing)
    {
        var args = current.Where(a => !a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)).ToList();
        if (!windowShowing) args.Add("--minimized");
        return args;
    }

    /// <summary>Only a copy installed where the installer puts it is ever replaced, never a build someone is running from a folder.</summary>
    public static bool IsInstalledCopy(string? exePath, string installDir)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return false;
        string dir = Path.GetFullPath(installDir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return Path.GetFullPath(exePath).StartsWith(dir, StringComparison.OrdinalIgnoreCase)
               && string.Equals(Path.GetFileName(exePath), ExeName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether an update is worth swapping in: its version is higher than the one running.</summary>
    public static bool IsNewer(StagedUpdate? staged, Version running) => staged is not null && UpdateCheck.IsNewer(running, staged.Version);

    /// <summary>
    /// Replace the installed program with the staged one. The installed file (which may be running) is renamed to <c>.old</c>, the new one is copied
    /// in, and if that fails the old one is put back, so there is always a working program. Returns false, with the reason, when nothing changed.
    /// </summary>
    public static bool Swap(string stagedExe, string installedExe, out string problem)
    {
        problem = "";
        if (!LooksLikeProgram(stagedExe)) { problem = "The downloaded update is missing or damaged."; return false; }
        if (!File.Exists(installedExe)) { problem = "The installed program was not found."; return false; }

        string aside = installedExe + ".old";
        try
        {
            try { if (File.Exists(aside)) File.Delete(aside); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { aside = $"{installedExe}.{DateTime.UtcNow.Ticks}.old"; }   // the last one is still in use
            File.Move(installedExe, aside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = "The installed program could not be moved aside: " + ex.Message;
            return false;
        }

        try
        {
            File.Copy(stagedExe, installedExe, overwrite: false);
            if (!LooksLikeProgram(installedExe)) throw new IOException("the copy is not a program");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(installedExe)) File.Delete(installedExe); } catch (Exception) { }
            try { File.Move(aside, installedExe); } catch (Exception) { problem = "The update failed and the old program could not be put back: " + aside; return false; }
            problem = "The new program could not be put in place, so nothing was changed: " + ex.Message;
            return false;
        }
    }

    /// <summary>Delete the old programs left beside the installed one by earlier swaps (they cannot be deleted while running, so it is done on the next start).</summary>
    public static void CleanOld(string installedExe)
    {
        try
        {
            string dir = Path.GetDirectoryName(installedExe) ?? "";
            if (!Directory.Exists(dir)) return;
            foreach (string old in Directory.EnumerateFiles(dir, Path.GetFileName(installedExe) + "*.old"))
            {
                try { File.Delete(old); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}


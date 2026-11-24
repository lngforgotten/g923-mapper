using System.IO.Compression;
using System.Security.Cryptography;
using G923Mapper;

namespace G923Mapper.Tests;

public class UpdatePackageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "g923-update-" + Guid.NewGuid().ToString("N"));

    public UpdatePackageTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string P(string name) => Path.Combine(_dir, name);

    /// <summary>A file that passes for a program: starts with MZ and is large enough.</summary>
    private string FakeExe(string name, byte filler = 1)
    {
        var bytes = new byte[600_000];
        Array.Fill(bytes, filler);
        bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
        File.WriteAllBytes(P(name), bytes);
        return P(name);
    }

    private string MakeZip(string zipName, string entryName, byte[] content)
    {
        string zip = P(zipName);
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var e = z.CreateEntry(entryName);
            using var s = e.Open();
            s.Write(content);
        }
        return zip;
    }

    private static byte[] ProgramBytes(int size = 700_000)
    {
        var b = new byte[size];
        b[0] = (byte)'M'; b[1] = (byte)'Z';
        return b;
    }

    // ---------------------------------------------------------------- assets

    private const string Release = """
    {
      "tag_name": "v1.3.0", "html_url": "https://github.com/o/r/releases/tag/v1.3.0",
      "assets": [
        { "name": "G923Mapper-win-x64.zip", "size": 70000000, "browser_download_url": "https://github.com/o/r/releases/download/v1.3.0/G923Mapper-win-x64.zip",
          "digest": "sha256:ABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEF1234" },
        { "name": "G923Mapper-small-win-x64.zip", "size": 1000000, "browser_download_url": "https://github.com/o/r/releases/download/v1.3.0/G923Mapper-small-win-x64.zip" },
        { "name": "notes.txt", "size": 5, "browser_download_url": "https://github.com/o/r/releases/download/v1.3.0/notes.txt" }
      ]
    }
    """;

    [Fact]
    public void The_assets_of_a_release_are_read_with_their_size_and_checksum_when_there_is_one()
    {
        var assets = UpdatePackage.ReadAssets(Release);
        Assert.Equal(3, assets.Count);
        var full = assets.Single(a => a.Name == UpdatePackage.FullZip);
        Assert.Equal(70_000_000, full.Size);
        Assert.Equal("abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdef1234", full.Sha256);
        Assert.Null(assets.Single(a => a.Name == UpdatePackage.SmallZip).Sha256);
    }

    [Fact]
    public void Unreadable_or_empty_answers_give_no_assets()
    {
        Assert.Empty(UpdatePackage.ReadAssets("not json"));
        Assert.Empty(UpdatePackage.ReadAssets("{}"));
        Assert.Empty(UpdatePackage.ReadAssets("""{ "assets": "x" }"""));
        Assert.Empty(UpdatePackage.ReadAssets("""{ "assets": [ { "name": "a" } ] }"""));
    }

    [Theory]
    [InlineData("sha256:ABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEF1234", true)]
    [InlineData("abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdef1234", true)]
    [InlineData("sha256:short", false)]
    [InlineData("sha1:abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdef1234", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz", false)]
    public void A_digest_is_accepted_only_as_64_hex_digits(string? text, bool valid) =>
        Assert.Equal(valid, UpdatePackage.NormaliseDigest(text) is not null);

    [Fact]
    public void The_download_matches_how_the_app_was_installed_and_comes_only_from_that_repository_on_github()
    {
        var assets = UpdatePackage.ReadAssets(Release);
        Assert.Equal(UpdatePackage.FullZip, UpdatePackage.Choose(assets, selfContained: true, "o/r")!.Name);
        Assert.Equal(UpdatePackage.SmallZip, UpdatePackage.Choose(assets, selfContained: false, "o/r")!.Name);
        Assert.Null(UpdatePackage.Choose(assets, true, "someone/else"));               // a different repository's files are never used
        var elsewhere = new[] { new UpdateAsset(UpdatePackage.FullZip, "https://evil.example/o/r/releases/download/v1/G923Mapper-win-x64.zip", 1, null) };
        Assert.Null(UpdatePackage.Choose(elsewhere, true, "o/r"));
        var plain = new[] { new UpdateAsset(UpdatePackage.FullZip, "http://github.com/o/r/releases/download/v1/G923Mapper-win-x64.zip", 1, null) };
        Assert.Null(UpdatePackage.Choose(plain, true, "o/r"));                         // no plain http
    }

    [Fact]
    public void A_release_that_says_it_is_newer_carries_its_assets()
    {
        var info = UpdateCheck.Interpret(Release, new Version(1, 2, 1), "o/r");
        Assert.True(info.Newer);
        Assert.Equal(3, info.Assets!.Count);
        Assert.Null(UpdateCheck.Interpret(Release, new Version(1, 3, 0), "o/r").Assets);
    }

    [Theory]
    [InlineData(70_000_000, true)]
    [InlineData(1_000_000, false)]
    [InlineData(20_000_000, true)]
    [InlineData(19_999_999, false)]
    public void The_full_build_is_told_apart_from_the_small_one_by_size(long bytes, bool full) =>
        Assert.Equal(full, UpdatePackage.LooksSelfContained(bytes));

    // ---------------------------------------------------------------- checking and unpacking

    [Fact]
    public void A_download_is_checked_against_its_checksum()
    {
        string file = FakeExe("a.bin");
        string hex = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        Assert.Equal(DigestCheck.Match, UpdatePackage.CheckDigest(file, "sha256:" + hex));
        Assert.Equal(DigestCheck.Match, UpdatePackage.CheckDigest(file, hex.ToLowerInvariant()));
        Assert.Equal(DigestCheck.Mismatch, UpdatePackage.CheckDigest(file, "sha256:" + new string('0', 64)));
        Assert.Equal(DigestCheck.NoDigest, UpdatePackage.CheckDigest(file, null));
        Assert.Equal(DigestCheck.NoDigest, UpdatePackage.CheckDigest(file, "garbage"));
    }

    [Fact]
    public void The_program_is_taken_out_of_the_zip()
    {
        string zip = MakeZip("ok.zip", "G923Mapper.exe", ProgramBytes());
        string? exe = UpdatePackage.ExtractExe(zip, P("out"), out string problem);
        Assert.Equal("", problem);
        Assert.Equal(P("out\\G923Mapper.exe"), exe);
        Assert.True(UpdatePackage.LooksLikeProgram(exe!));
    }

    [Fact]
    public void A_zip_without_the_program_or_with_it_in_a_folder_is_refused()
    {
        Assert.Null(UpdatePackage.ExtractExe(MakeZip("a.zip", "readme.txt", ProgramBytes()), P("o1"), out string p1));
        Assert.Contains("no G923Mapper.exe", p1);
        Assert.Null(UpdatePackage.ExtractExe(MakeZip("b.zip", "sub/G923Mapper.exe", ProgramBytes()), P("o2"), out _));
        Assert.Null(UpdatePackage.ExtractExe(MakeZip("c.zip", "../G923Mapper.exe", ProgramBytes()), P("o3"), out _));
        Assert.False(File.Exists(P("G923Mapper.exe")));    // nothing escaped the folder
    }

    [Fact]
    public void Something_that_is_not_a_program_or_is_the_wrong_size_is_refused_and_not_kept()
    {
        var text = new byte[700_000]; Array.Fill(text, (byte)'a');
        Assert.Null(UpdatePackage.ExtractExe(MakeZip("t.zip", "G923Mapper.exe", text), P("o1"), out string notProgram));
        Assert.Contains("not a Windows program", notProgram);
        Assert.False(File.Exists(P("o1\\G923Mapper.exe")));

        Assert.Null(UpdatePackage.ExtractExe(MakeZip("s.zip", "G923Mapper.exe", ProgramBytes(1000)), P("o2"), out string tiny));
        Assert.Contains("unexpected size", tiny);
    }

    [Fact]
    public void A_file_that_is_not_a_zip_is_refused_without_an_exception()
    {
        File.WriteAllText(P("bad.zip"), "this is not a zip");
        Assert.Null(UpdatePackage.ExtractExe(P("bad.zip"), P("o"), out string problem));
        Assert.Contains("could not be unpacked", problem);
        Assert.Null(UpdatePackage.ExtractExe(P("missing.zip"), P("o"), out _));
    }

    [Fact]
    public void The_version_inside_a_program_is_read()
    {
        Version? v = UpdatePackage.FileVersion(typeof(UpdatePackage).Assembly.Location);
        Assert.NotNull(v);
        Assert.Null(UpdatePackage.FileVersion(P("nothing.exe")));
    }

    // ---------------------------------------------------------------- what is ready

    [Fact]
    public void A_staged_update_is_remembered_and_read_back()
    {
        string exe = FakeExe("stage\\G923Mapper.exe".Replace("stage\\", ""));
        Directory.CreateDirectory(P("stage"));
        File.Move(exe, P("stage\\G923Mapper.exe"));
        UpdatePackage.WriteStaged(P("stage"), "v1.3.0", new Version(1, 3, 0), P("stage\\G923Mapper.exe"));
        var staged = UpdatePackage.ReadStaged(P("stage"))!;
        Assert.Equal(new Version(1, 3, 0), staged.Version);
        Assert.Equal("v1.3.0", staged.Tag);
        Assert.Equal(P("stage\\G923Mapper.exe"), staged.ExePath);
    }

    [Fact]
    public void Nothing_staged_or_a_damaged_stage_reads_as_none()
    {
        Assert.Null(UpdatePackage.ReadStaged(P("none")));
        Directory.CreateDirectory(P("bad"));
        File.WriteAllText(P("bad\\staged.json"), "{ nope");
        Assert.Null(UpdatePackage.ReadStaged(P("bad")));

        Directory.CreateDirectory(P("gone"));
        UpdatePackage.WriteStaged(P("gone"), "v9", new Version(9, 0), P("gone\\G923Mapper.exe"));      // the program itself is missing
        Assert.Null(UpdatePackage.ReadStaged(P("gone")));
    }

    [Fact]
    public void A_staged_file_name_cannot_point_outside_its_folder()
    {
        Directory.CreateDirectory(P("stage2"));
        FakeExe("outside.exe");
        File.WriteAllText(P("stage2\\staged.json"), """{ "tag": "v9", "version": "9.0.0.0", "exe": "..\\outside.exe" }""");
        Assert.Null(UpdatePackage.ReadStaged(P("stage2")));       // only the file name is used, so "..\outside.exe" becomes "outside.exe" inside stage2, which is not there
    }

    [Fact]
    public void Clearing_the_stage_removes_it_and_is_harmless_when_there_is_none()
    {
        Directory.CreateDirectory(P("st"));
        File.WriteAllText(P("st\\x"), "x");
        UpdatePackage.ClearStaged(P("st"));
        Assert.False(Directory.Exists(P("st")));
        UpdatePackage.ClearStaged(P("never"));
    }

    [Fact]
    public void Only_a_newer_staged_version_is_worth_applying()
    {
        var staged = new StagedUpdate(new Version(1, 3, 0), "v1.3.0", "x");
        Assert.True(UpdatePackage.IsNewer(staged, new Version(1, 2, 1)));
        Assert.False(UpdatePackage.IsNewer(staged, new Version(1, 3, 0)));
        Assert.False(UpdatePackage.IsNewer(staged, new Version(2, 0, 0)));
        Assert.False(UpdatePackage.IsNewer(null, new Version(1, 0, 0)));
    }

    // ---------------------------------------------------------------- swapping

    [Fact]
    public void Only_a_copy_installed_in_the_install_folder_is_ever_replaced()
    {
        string install = P("Programs\\G923Mapper");
        Assert.True(UpdatePackage.IsInstalledCopy(Path.Combine(install, "G923Mapper.exe"), install));
        Assert.False(UpdatePackage.IsInstalledCopy(P("bin\\Release\\G923Mapper.exe"), install));     // a build someone runs from a folder
        Assert.False(UpdatePackage.IsInstalledCopy(Path.Combine(install, "Other.exe"), install));
        Assert.False(UpdatePackage.IsInstalledCopy(Path.Combine(install + "Evil", "G923Mapper.exe"), install));   // a folder that merely starts with the same letters
        Assert.False(UpdatePackage.IsInstalledCopy(null, install));
        Assert.False(UpdatePackage.IsInstalledCopy("", install));
    }

    [Fact]
    public void Swapping_puts_the_new_program_in_and_keeps_the_old_one_aside()
    {
        Directory.CreateDirectory(P("inst"));
        string installed = FakeExe("inst\\G923Mapper.exe".Replace("inst\\", ""), 1);
        File.Move(installed, P("inst\\G923Mapper.exe"));
        string staged = FakeExe("new.exe", 2);

        Assert.True(UpdatePackage.Swap(staged, P("inst\\G923Mapper.exe"), out string problem), problem);
        Assert.Equal(2, File.ReadAllBytes(P("inst\\G923Mapper.exe"))[100]);          // the new program is where the old one was
        Assert.Equal(1, File.ReadAllBytes(P("inst\\G923Mapper.exe.old"))[100]);      // the old one is beside it
    }

    [Fact]
    public void A_running_program_can_be_swapped_because_it_is_renamed_not_overwritten()
    {
        // Hold the installed file open for reading the way a running program is held, then swap.
        Directory.CreateDirectory(P("run"));
        string installed = P("run\\G923Mapper.exe");
        File.Move(FakeExe("tmp.exe", 1), installed);
        string staged = FakeExe("n.exe", 2);
        using var holding = new FileStream(installed, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        Assert.True(UpdatePackage.Swap(staged, installed, out string problem), problem);
        Assert.Equal(2, File.ReadAllBytes(installed)[100]);
    }

    [Fact]
    public void A_damaged_staged_file_changes_nothing()
    {
        Directory.CreateDirectory(P("keep"));
        string installed = P("keep\\G923Mapper.exe");
        File.Move(FakeExe("k.exe", 1), installed);
        File.WriteAllText(P("junk.exe"), "not a program");
        Assert.False(UpdatePackage.Swap(P("junk.exe"), installed, out string problem));
        Assert.Contains("damaged", problem);
        Assert.Equal(1, File.ReadAllBytes(installed)[100]);
        Assert.False(File.Exists(installed + ".old"));
        Assert.False(UpdatePackage.Swap(P("missing.exe"), installed, out _));
        Assert.False(UpdatePackage.Swap(FakeExe("x.exe"), P("keep\\NotInstalled.exe"), out _));
    }

    [Fact]
    public void A_staged_path_that_is_not_a_file_changes_nothing()
    {
        Directory.CreateDirectory(P("fail"));
        string installed = Path.Combine(P("fail"), "G923Mapper.exe");
        File.Move(FakeExe("f.exe", 1), installed);
        string folder = Path.Combine(P("fail"), "staged-dir");
        Directory.CreateDirectory(folder);
        Assert.False(UpdatePackage.Swap(folder, installed, out _));             // a folder is not a program
        Assert.Equal(1, File.ReadAllBytes(installed)[100]);
        Assert.False(File.Exists(installed + ".old"));
    }

    [Fact]
    public void Old_programs_left_by_earlier_swaps_are_cleaned_up()
    {
        Directory.CreateDirectory(P("clean"));
        string installed = P("clean\\G923Mapper.exe");
        File.Move(FakeExe("c.exe", 1), installed);
        File.WriteAllText(installed + ".old", "old");
        File.WriteAllText(installed + ".638000000000000000.old", "older");
        File.WriteAllText(P("clean\\other.txt"), "keep");
        UpdatePackage.CleanOld(installed);
        Assert.False(File.Exists(installed + ".old"));
        Assert.False(File.Exists(installed + ".638000000000000000.old"));
        Assert.True(File.Exists(installed));
        Assert.True(File.Exists(P("clean\\other.txt")));
        UpdatePackage.CleanOld(P("nowhere\\G923Mapper.exe"));       // harmless
    }
}

public class UpdatedMarkerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "g923-marker-" + Guid.NewGuid().ToString("N"));
    public UpdatedMarkerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string Marker => Path.Combine(_dir, "just-updated.json");

    [Fact]
    public void The_new_copy_says_so_once_for_the_version_it_is()
    {
        UpdatePackage.WriteUpdatedMarker(Marker, "v1.3.2", new Version(1, 3, 2));
        Assert.Equal("v1.3.2", UpdatePackage.TakeUpdatedMarker(Marker, new Version(1, 3, 2, 0)));
        Assert.False(File.Exists(Marker));
        Assert.Null(UpdatePackage.TakeUpdatedMarker(Marker, new Version(1, 3, 2)));      // not again
    }

    [Fact]
    public void A_note_left_over_from_another_version_is_dropped_without_being_said()
    {
        UpdatePackage.WriteUpdatedMarker(Marker, "v1.3.2", new Version(1, 3, 2));
        Assert.Null(UpdatePackage.TakeUpdatedMarker(Marker, new Version(1, 3, 0)));       // the swap did not happen, or it was rolled back
        Assert.False(File.Exists(Marker));
    }

    [Fact]
    public void No_note_or_a_damaged_one_says_nothing_and_does_not_throw()
    {
        Assert.Null(UpdatePackage.TakeUpdatedMarker(Marker, new Version(1, 3, 2)));
        File.WriteAllText(Marker, "{ nope");
        Assert.Null(UpdatePackage.TakeUpdatedMarker(Marker, new Version(1, 3, 2)));
        Assert.False(File.Exists(Marker));
        File.WriteAllText(Marker, """{ "tag": "v9", "version": "x" }""");
        Assert.Null(UpdatePackage.TakeUpdatedMarker(Marker, new Version(9, 0, 0)));
    }
}

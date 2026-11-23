using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace G923Mapper;

/// <summary>Finds the folders of games that are running and lists their files, for <see cref="ConflictRules"/>.</summary>
public static class GameFolderScanner
{
    /// <summary>
    /// For each program in <paramref name="exes"/> that is running, its folder and the files directly in it. Programs that cannot be inspected
    /// (protected by anti-cheat, or closing) are skipped. Never throws.
    /// </summary>
    public static IReadOnlyList<GameFolder> Scan(IEnumerable<string> exes)
    {
        var result = new List<GameFolder>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string exe in exes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string name = ProfileSelector.Normalize(exe);
            Process[] procs;
            try { procs = Process.GetProcessesByName(name); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { continue; }

            foreach (Process p in procs)
            {
                try
                {
                    if (PathOf(p) is not { } path || Path.GetDirectoryName(path) is not { } folder || !seen.Add(folder)) continue;
                    var files = new DirectoryInfo(folder).EnumerateFiles().Select(f => (f.Name, f.Length)).ToList();
                    result.Add(new GameFolder(exe, folder, files));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
                finally { p.Dispose(); }
            }
        }
        return result;
    }

    /// <summary>The program's file path. Works for processes that refuse <see cref="Process.MainModule"/> (anti-cheat protected games).</summary>
    private static string? PathOf(Process p)
    {
        IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, p.Id);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
        }
        finally { CloseHandle(h); }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder exeName, ref int size);
}

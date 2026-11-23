using System.Text;

namespace G923Mapper;

/// <summary>
/// A small daily log file in %APPDATA%\G923Mapper\logs. Logging never throws and never blocks the
/// mapping loop for long; if the disk is unavailable it silently does nothing.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _dir;
    private static string _name = "g923mapper";

    public static string? Directory => _dir;

    /// <summary>Set the folder (created if needed) and delete logs older than <paramref name="keepDays"/> days.</summary>
    /// <param name="name">File name prefix; each copy of the app running at once uses its own so they never fight over a file.</param>
    public static void Init(string directory, int keepDays = 7, string name = "g923mapper")
    {
        _name = name;
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            _dir = directory;
            foreach (string f in System.IO.Directory.EnumerateFiles(directory, "g923mapper-*.log"))
                if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-keepDays)) File.Delete(f);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _dir = null; }
    }

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}\n{ex}");

    /// <summary>The last <paramref name="count"/> lines of today's log (empty if there is none or it cannot be read).</summary>
    public static IReadOnlyList<string> Tail(int count)
    {
        string? dir = _dir;
        if (dir is null || count <= 0) return [];
        try
        {
            string path = Path.Combine(dir, $"{_name}-{DateTime.Now:yyyyMMdd}.log");
            if (!File.Exists(path)) return [];
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            var queue = new Queue<string>(count);
            while (reader.ReadLine() is { } line)
            {
                if (queue.Count == count) queue.Dequeue();
                queue.Enqueue(line);
            }
            return queue.ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static void Write(string level, string message)
    {
        string? dir = _dir;
        if (dir is null) return;
        try
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
            lock (Gate) File.AppendAllText(Path.Combine(dir, $"{_name}-{DateTime.Now:yyyyMMdd}.log"), line, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

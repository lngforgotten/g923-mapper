namespace G923Mapper;

/// <summary>
/// Loads every *.json profile in a folder and reloads on file save. A file that fails to parse or
/// validate never replaces the last good version of that profile; the error is reported instead.
/// </summary>
public sealed class ProfileStore : IDisposable
{
    private readonly string _dir;
    private readonly DeviceRegistry _devices;
    private readonly FileSystemWatcher _watcher;
    private readonly Dictionary<string, ProfileLoadResult> _byFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Timer> _debounce = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private int _version;
    private readonly Dictionary<string, string> _lastText = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Where to keep earlier versions of profiles (see <see cref="ProfileHistory"/>). Whenever a profile file changes, whoever changed it,
    /// the text it had before is saved there. Null = keep no history.
    /// </summary>
    public string? HistoryRoot { get; init; }

    /// <summary>Raised after a (re)load attempt of one file, on a thread-pool thread. Args: file name, result.</summary>
    public event Action<string, ProfileLoadResult>? Loaded;

    /// <summary>Raised once when a loaded file is dropped because it was deleted, moved or renamed away. Arg: file name. Any thread.</summary>
    public event Action<string>? Removed;

    /// <summary>Delay after the last write event before reloading (editors fire several events per save).</summary>
    public TimeSpan Debounce { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Increments whenever any profile's content changes successfully; poll this to know when to rebuild an engine.</summary>
    public int Version => Volatile.Read(ref _version);

    public ProfileStore(string directory, DeviceRegistry devices)
    {
        _dir = directory;
        _devices = devices;
        Directory.CreateDirectory(directory);

        foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
            Reload(Path.GetFileName(file));

        _watcher = new FileSystemWatcher(directory, "*.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, e) => Schedule(e.Name);
        _watcher.Created += (_, e) => Schedule(e.Name);
        _watcher.Deleted += (_, e) => Schedule(e.Name);
        _watcher.Renamed += (_, e) => { Schedule(e.OldName); Schedule(e.Name); };
    }

    /// <summary>Re-validate every profile, e.g. after the device map changed because of a calibration.</summary>
    public void ReloadAll()
    {
        foreach (string file in Files) Reload(file);
    }

    public IReadOnlyList<string> Files
    {
        get { lock (_lock) return _byFile.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList(); }
    }

    /// <summary>Last good profile for a file name ("gta-v.json" or "gta-v") or the profile's own name.</summary>
    public Profile? Get(string nameOrFile)
    {
        lock (_lock)
        {
            if (_byFile.TryGetValue(Ensure(nameOrFile), out var r) && r.Profile is not null) return r.Profile;
            return _byFile.Values.Select(v => v.Profile)
                .FirstOrDefault(p => p is not null && string.Equals(p.Name, nameOrFile, StringComparison.OrdinalIgnoreCase));
        }
    }

    public ProfileLoadResult? LastResult(string file)
    {
        lock (_lock) return _byFile.TryGetValue(Ensure(file), out var r) ? r : null;
    }

    private static string Ensure(string f) => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? f : f + ".json";

    private void Schedule(string? file)
    {
        if (file is null) return;
        lock (_lock)
        {
            if (_debounce.TryGetValue(file, out var t)) t.Dispose();
            _debounce[file] = new Timer(_ => Reload(file), null, Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Load (or drop, if deleted) one file now instead of waiting for the file watcher.</summary>
    public void Reload(string file)
    {
        string path = Path.Combine(_dir, file);
        ProfileLoadResult result;
        if (!File.Exists(path))
        {
            bool dropped;
            lock (_lock) { dropped = _byFile.Remove(file); _debounce.Remove(file); }
            Interlocked.Increment(ref _version);
            if (dropped) Removed?.Invoke(file);
            return;
        }

        RememberText(file, path);

        // The editor may still hold the file open; retry briefly.
        result = ProfileLoader.LoadFile(path, _devices);
        for (int i = 0; i < 5 && !result.Ok && result.Errors.Any(e => e.StartsWith("Could not read")); i++)
        {
            Thread.Sleep(50);
            result = ProfileLoader.LoadFile(path, _devices);
        }

        lock (_lock)
        {
            if (result.Ok)
            {
                _byFile[file] = result;
                Interlocked.Increment(ref _version);
            }
            else if (_byFile.TryGetValue(file, out var previous) && previous.Profile is not null)
            {
                // Keep serving the last good profile, but remember the latest errors for display.
                _byFile[file] = new ProfileLoadResult(previous.Profile, result.Errors, result.Warnings);
            }
            else
            {
                _byFile[file] = result;
            }
        }
        Loaded?.Invoke(file, result);
    }

    /// <summary>Keep the text a file had before it changed, so a bad edit can be undone later.</summary>
    private void RememberText(string file, string path)
    {
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }

        lock (_lock)
        {
            if (HistoryRoot is not null && _lastText.TryGetValue(file, out string? before) && before != text)
                ProfileHistory.SnapshotText(Path.GetFileNameWithoutExtension(file), before, HistoryRoot);
            _lastText[file] = text;
        }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        lock (_lock)
        {
            foreach (var t in _debounce.Values) t.Dispose();
            _debounce.Clear();
        }
    }
}

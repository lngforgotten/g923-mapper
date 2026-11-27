using System.Globalization;

namespace G923Mapper;

/// <summary>
/// How a download is going, in words: how much of how much, how fast over the last few seconds, and whether anything is arriving at all, so a slow
/// or stalled connection is plain to see. Times are passed in (any steadily rising clock), so it is tested without waiting.
/// </summary>
public sealed class DownloadMeter
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(4);      // the speed is the average over about this long
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(1);      // no speed is claimed from less than this
    private static readonly TimeSpan Stall = TimeSpan.FromSeconds(5);       // nothing new for this long is said to be waiting

    private readonly Queue<(TimeSpan When, long Done)> _samples = new();
    private TimeSpan _lastGrowth;

    public long Done { get; private set; }

    /// <summary>The size of the whole download, or 0 when it is not known.</summary>
    public long Total { get; private set; }

    /// <summary>0 to 99 while downloading (100 is for after the checks), or 0 when the size is not known.</summary>
    public int Percent => Total > 0 ? (int)Math.Min(99, Done * 100 / Total) : 0;

    /// <summary>A new download of this many bytes (0 = not known) is starting.</summary>
    public void Start(TimeSpan now, long total)
    {
        _samples.Clear();
        _samples.Enqueue((now, 0));
        _lastGrowth = now;
        Done = 0;
        Total = Math.Max(0, total);
    }

    /// <summary>This many bytes have arrived so far, of this many (0 = not known).</summary>
    public void Report(TimeSpan now, long done, long total)
    {
        if (total > 0) Total = total;
        if (done > Done) { Done = done; _lastGrowth = now; }
        _samples.Enqueue((now, Done));
        while (_samples.Count > 2 && now - _samples.Peek().When > Window) _samples.Dequeue();
    }

    /// <summary>The recent speed, or null while there is too little to go on.</summary>
    public double? BytesPerSecond(TimeSpan now)
    {
        if (_samples.Count == 0) return null;
        var (when, done) = _samples.Peek();
        double seconds = (now - when).TotalSeconds;
        return seconds >= Settle.TotalSeconds ? (Done - done) / seconds : null;
    }

    /// <summary>For example "34%, 23 of 67 MB at 4.2 MB/s".</summary>
    public string Describe(TimeSpan now)
    {
        if (Total > 0 && Done >= Total) return $"{Size(Total)} received, checking it";
        string amount = Total > 0 ? $"{Percent}%, {Amount(Done, Total)} of {Size(Total)}" : Size(Done);
        if (now - _lastGrowth >= Stall) return amount + ", waiting for data";
        return BytesPerSecond(now) is double speed ? amount + " at " + Speed(speed) : amount;
    }

    private const double KB = 1024, MB = 1024 * 1024;

    /// <summary>A size as Windows writes it: "67 MB", or "1.0 MB" under ten.</summary>
    public static string Size(long bytes) => Number(bytes, small: bytes < 10 * MB) + " MB";

    // The part done, written to the same number of places as the whole, without repeating the unit.
    private static string Amount(long done, long total) => Number(done, small: total < 10 * MB);

    private static string Number(long bytes, bool small) => (bytes / MB).ToString(small ? "0.0" : "0", CultureInfo.InvariantCulture);

    private static string Speed(double bytesPerSecond) => bytesPerSecond >= MB
        ? (bytesPerSecond / MB).ToString("0.0", CultureInfo.InvariantCulture) + " MB/s"
        : (bytesPerSecond / KB).ToString("0", CultureInfo.InvariantCulture) + " KB/s";
}

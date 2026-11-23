namespace G923Mapper;

/// <summary>The last few seconds of a pair of 0..255 levels (the game's heavy and light rumble motors), oldest first, for a history graph.</summary>
public sealed class SampleRing
{
    private readonly byte[] _a, _b;
    private int _next, _count;

    public SampleRing(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _a = new byte[capacity];
        _b = new byte[capacity];
    }

    public int Capacity => _a.Length;
    public int Count => _count;

    public void Add(byte first, byte second)
    {
        _a[_next] = first;
        _b[_next] = second;
        _next = (_next + 1) % _a.Length;
        if (_count < _a.Length) _count++;
    }

    public void Clear() { _next = 0; _count = 0; }

    /// <summary>The samples in time order, oldest first (shorter than the capacity until the ring has filled).</summary>
    public (byte First, byte Second)[] ToArray()
    {
        var result = new (byte, byte)[_count];
        int start = (_next - _count + _a.Length) % _a.Length;
        for (int i = 0; i < _count; i++)
        {
            int at = (start + i) % _a.Length;
            result[i] = (_a[at], _b[at]);
        }
        return result;
    }
}

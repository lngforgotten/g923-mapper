namespace G923Mapper;

/// <summary>
/// Follows where the wheel comes to rest so a self-centring wheel that settles a degree or two off still reads as centred.
/// When the wheel has stayed still for a moment, with no pedal pressed, and it is within a few degrees of the calibrated centre,
/// that spot becomes the new centre. It never moves more than <c>capDegrees</c> from the original centre, so holding the wheel off to
/// one side, or a long steady corner, can never be mistaken for "centre".
/// </summary>
public sealed class CentreTracker
{
    private readonly int _origin;
    private readonly double _countsPerDegree;
    private readonly int _holdMs;
    private readonly double _stillCounts, _minShiftCounts, _capCounts;
    private int _anchor;
    private long _since = -1;

    /// <param name="originCentre">The calibrated centre (raw axis counts) the wheel started from.</param>
    /// <param name="countsPerDegree">Raw counts per degree of wheel turn (axis span divided by lock-to-lock degrees).</param>
    /// <param name="holdMs">How long the wheel must be still before its position is trusted.</param>
    /// <param name="stillDegrees">How little it may move, in degrees, and still count as still.</param>
    /// <param name="minShiftDegrees">Smaller differences than this are ignored, so the centre does not creep for nothing.</param>
    /// <param name="capDegrees">The furthest the centre may move from the original.</param>
    public CentreTracker(int originCentre, double countsPerDegree, int holdMs = 700, double stillDegrees = 0.25,
                         double minShiftDegrees = 0.3, double capDegrees = 3)
    {
        _origin = originCentre;
        Centre = originCentre;
        _countsPerDegree = Math.Max(0.0001, countsPerDegree);
        _holdMs = holdMs;
        _stillCounts = stillDegrees * _countsPerDegree;
        _minShiftCounts = minShiftDegrees * _countsPerDegree;
        _capCounts = capDegrees * _countsPerDegree;
    }

    /// <summary>The centre in use now (raw counts).</summary>
    public int Centre { get; private set; }

    /// <summary>How far the centre has moved from the original, in degrees.</summary>
    public double ShiftDegrees => (Centre - _origin) / _countsPerDegree;

    /// <summary>Forget what was learnt and use this centre (for example after the user presses Set centre).</summary>
    public void Reset(int centre)
    {
        Centre = centre;
        _since = -1;
    }

    /// <param name="raw">The wheel's raw axis reading now.</param>
    /// <param name="nowMs">Monotonic milliseconds.</param>
    /// <param name="eligible">False while something makes the reading untrustworthy as a rest position (a pedal pressed, the wheel not live yet).</param>
    /// <returns>The new centre when it has just moved, otherwise null.</returns>
    public int? Update(int raw, long nowMs, bool eligible)
    {
        if (!eligible) { _since = -1; return null; }

        if (_since < 0 || Math.Abs(raw - _anchor) > _stillCounts)
        {
            _anchor = raw;      // it moved (or this is the first reading): start timing again from here
            _since = nowMs;
            return null;
        }
        if (nowMs - _since < _holdMs) return null;

        _since = nowMs;         // look again after another hold, whatever happens next
        if (Math.Abs(_anchor - _origin) > _capCounts) return null;           // too far from the calibrated centre: that is not the centre
        if (Math.Abs(_anchor - Centre) < _minShiftCounts) return null;       // already there

        Centre = _anchor;
        return Centre;
    }
}

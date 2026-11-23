namespace G923Mapper;

/// <summary>
/// The rules for editing the points of a custom curve by hand (adding, dragging, removing), kept free of any window code so they can be tested.
/// A curve is a list of [x, y] points, both 0..1, with x strictly ascending, and it always has at least two points.
/// </summary>
public static class CurvePoints
{
    /// <summary>Points must stay at least this far apart in x, so they stay strictly ascending even after rounding to 3 decimals.</summary>
    public const double MinGap = 0.01;

    public static double[][] StraightLine() => [[0, 0], [1, 1]];

    private static double[][] Copy(double[][] p) => p.Select(pt => new[] { pt[0], pt[1] }).ToArray();

    /// <summary>The list with a new point inserted in x order, or null if there is no room between its neighbours (or no curve).</summary>
    public static double[][]? Add(double[][]? points, double x, double y, out int index)
    {
        index = -1;
        if (points is null || points.Length < 2) return null;
        x = Math.Clamp(x, 0, 1); y = Math.Clamp(y, 0, 1);

        int at = 0;
        while (at < points.Length && points[at][0] < x) at++;
        if (at > 0 && x - points[at - 1][0] < MinGap) return null;
        if (at < points.Length && points[at][0] - x < MinGap) return null;

        var list = Copy(points).ToList();
        list.Insert(at, [x, y]);
        index = at;
        return list.ToArray();
    }

    /// <summary>
    /// The list with point <paramref name="index"/> moved to (x, y): y is kept in 0..1, and x is kept between its neighbours
    /// (the first point stays at the left edge side and the last at the right, so the curve always spans the whole range).
    /// </summary>
    public static double[][] Move(double[][] points, int index, double x, double y)
    {
        var result = Copy(points);
        if (index < 0 || index >= result.Length) return result;
        double lo = index == 0 ? 0 : result[index - 1][0] + MinGap;
        double hi = index == result.Length - 1 ? 1 : result[index + 1][0] - MinGap;
        result[index][0] = Math.Clamp(x, lo, Math.Max(lo, hi));
        result[index][1] = Math.Clamp(y, 0, 1);
        return result;
    }

    /// <summary>The list without point <paramref name="index"/>, or the same list if only two points remain.</summary>
    public static double[][] Remove(double[][] points, int index)
    {
        if (points.Length <= 2 || index < 0 || index >= points.Length) return Copy(points);
        return points.Where((_, i) => i != index).Select(pt => new[] { pt[0], pt[1] }).ToArray();
    }

    /// <summary>Round to the 3 decimals the text form keeps, so what is shown is exactly what is saved.</summary>
    public static double[][] Rounded(double[][] points) => points.Select(pt => new[] { Math.Round(pt[0], 3), Math.Round(pt[1], 3) }).ToArray();
}

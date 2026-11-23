using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace G923Mapper.App;

/// <summary>Plots a rule's response (deadzone, curve, anti-deadzone) so the effect of the numbers is visible.</summary>
public sealed class CurvePreview : FrameworkElement
{
    public CurvePreview()
    {
        Loaded += (_, _) => { ThemeManager.Changed += OnTheme; InvalidateVisual(); };
        Unloaded += (_, _) => ThemeManager.Changed -= OnTheme;
    }

    private void OnTheme() => Dispatcher.BeginInvoke(InvalidateVisual);

    private static Pen P(string key, double thickness, bool dash = false) =>
        new(ThemeManager.Brush(key), thickness) { DashStyle = dash ? DashStyles.Dash : DashStyles.Solid, LineJoin = PenLineJoin.Round };

    private double _deadzone, _anti, _exponent = 2;
    private string _curve = "linear";
    private double[][]? _points;
    private bool _signed;

    /// <summary>Raised while the user adds, drags or removes a point of a custom curve, with the whole new list (x ascending, both 0..1).</summary>
    public event Action<double[][]>? PointsEdited;

    private int _drag = -1;
    private const double HitRadius = 9;

    public void Set(string curve, double exponent, double[][]? points, double deadzone, double anti, bool signed)
    {
        _curve = curve; _exponent = exponent; _deadzone = deadzone; _anti = anti; _signed = signed;
        _points = points?.Select(p => new[] { p[0], p[1] }).ToArray();   // a private copy: dragging never edits the rule behind its back
        InvalidateVisual();
    }

    private (double X, double Y)? _live;

    /// <summary>Where the rule's input is on the curve right now (null = nothing to show); drawn as a dot with guide lines to both axes.</summary>
    public void SetLive((double X, double Y)? position)
    {
        if (_live == position) return;
        _live = position;
        InvalidateVisual();
    }

    private bool Editable => _curve.Equals("custom", StringComparison.OrdinalIgnoreCase);

    private Point ToPixel(double x, double y) =>
        _signed ? new Point((x + 1) / 2 * ActualWidth, ActualHeight - (y + 1) / 2 * ActualHeight) : new Point(x * ActualWidth, ActualHeight - y * ActualHeight);

    /// <summary>A pixel position as a point of the curve: input magnitude and output magnitude, each 0..1.</summary>
    private (double X, double Y) FromPixel(Point p)
    {
        double w = Math.Max(1, ActualWidth), h = Math.Max(1, ActualHeight);
        double x = _signed ? Math.Abs(p.X / w * 2 - 1) : p.X / w;
        double y = _signed ? (h - p.Y) / h * 2 - 1 : (h - p.Y) / h;
        return (Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));
    }

    private int HitPoint(Point mouse)
    {
        if (_points is null) return -1;
        for (int i = 0; i < _points.Length; i++)
        {
            Point p = ToPixel(_points[i][0], _points[i][1]);
            if ((p - mouse).Length <= HitRadius) return i;
        }
        return -1;
    }

    private void Raise() => PointsEdited?.Invoke(CurvePoints.Rounded(_points!));

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!Editable || _points is null || _points.Length < 2) return;
        Point m = e.GetPosition(this);
        int hit = HitPoint(m);
        if (hit < 0)
        {
            // A click on empty graph adds a point there, if there is room between its neighbours.
            var (x, y) = FromPixel(m);
            if (CurvePoints.Add(_points, x, y, out int at) is not { } added) return;
            _points = added;
            hit = at;
        }
        _drag = hit;
        CaptureMouse();
        Raise();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        if (_drag < 0 || _points is null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
        var (x, y) = FromPixel(e.GetPosition(this));
        _points = CurvePoints.Move(_points, _drag, x, y);
        Raise();
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_drag < 0) return;
        _drag = -1;
        ReleaseMouseCapture();
    }

    protected override void OnMouseRightButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!Editable || _points is null || _points.Length <= 2) return;   // a curve needs at least two points
        int hit = HitPoint(e.GetPosition(this));
        if (hit < 0) return;
        _points = CurvePoints.Remove(_points, hit);
        Raise();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 10 || h < 10) return;
        Pen border = P("PlotBorder", 1), grid = P("PlotGrid", 1), diagonal = P("PlotDiagonal", 1, dash: true), line = P("PlotLine", 2.2);
        dc.DrawRectangle(ThemeManager.Brush("PlotBack"), border, new Rect(0.5, 0.5, w - 1, h - 1));

        // Signed (wheel) plots -1..1 on both axes; pedals and buttons plot 0..1.
        double X(double v) => _signed ? (v + 1) / 2 * w : v * w;
        double Y(double v) => _signed ? h - (v + 1) / 2 * h : h - v * h;

        if (_signed)
        {
            dc.DrawLine(grid, new Point(w / 2, 0), new Point(w / 2, h));
            dc.DrawLine(grid, new Point(0, h / 2), new Point(w, h / 2));
            dc.DrawLine(diagonal, new Point(X(-1), Y(-1)), new Point(X(1), Y(1)));
        }
        else
        {
            dc.DrawLine(grid, new Point(w / 2, 0), new Point(w / 2, h));
            dc.DrawLine(grid, new Point(0, h / 2), new Point(w, h / 2));
            dc.DrawLine(diagonal, new Point(X(0), Y(0)), new Point(X(1), Y(1)));
        }

        double lo = _signed ? -1 : 0;
        var geo = new StreamGeometry();
        using (StreamGeometryContext ctx = geo.Open())
        {
            const int steps = 200;
            for (int i = 0; i <= steps; i++)
            {
                double x = lo + (1 - lo) * i / steps;
                double y;
                try { y = Shaping.Shape(x, _deadzone, _curve, _exponent, _points, _anti); }
                catch (Exception) { y = 0; }
                var p = new Point(X(x), Y(y));
                if (i == 0) ctx.BeginFigure(p, false, false); else ctx.LineTo(p, true, true);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, line, geo);

        // The live position: guide lines to both axes and a dot on the curve.
        if (_live is { } at)
        {
            double lx = Math.Clamp(at.X, lo, 1), ly = Math.Clamp(at.Y, lo, 1);
            var guide = P("Accent", 1, dash: true);
            dc.DrawLine(guide, new Point(X(lx), Y(lo)), new Point(X(lx), Y(ly)));
            dc.DrawLine(guide, new Point(X(lo), Y(ly)), new Point(X(lx), Y(ly)));
            dc.DrawEllipse(ThemeManager.Brush("Accent"), new Pen(ThemeManager.Brush("PlotBack"), 2), new Point(X(lx), Y(ly)), 6, 6);
        }

        // Handles for the points of a custom curve (drawn on the positive side, which the other side mirrors).
        if (Editable && _points is not null)
        {
            Brush fill = ThemeManager.Brush("PlotLine"), ring = ThemeManager.Brush("PlotBack");
            for (int i = 0; i < _points.Length; i++)
            {
                Point c = ToPixel(_points[i][0], _points[i][1]);
                dc.DrawEllipse(fill, new Pen(ring, 2), c, i == _drag ? 6.5 : 5, i == _drag ? 6.5 : 5);
            }
        }
    }

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
}


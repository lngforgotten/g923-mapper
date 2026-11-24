using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace G923Mapper.App;

/// <summary>
/// The centring curve drawn out: the force the wheel is pulled with at each angle, from full left to full right, with the wheel's own friction
/// floor as a band across the middle and a marker for where the wheel is right now. Above the line the force pushes right, below it left.
/// </summary>
public sealed class CentringGraph : FrameworkElement
{
    private ForceSettings _settings = new();
    private double _half = 450;
    private double? _angle;
    private double _force;

    public CentringGraph()
    {
        Loaded += (_, _) => { ThemeManager.Changed += OnTheme; InvalidateVisual(); };
        Unloaded += (_, _) => ThemeManager.Changed -= OnTheme;
    }

    private void OnTheme() => Dispatcher.BeginInvoke(InvalidateVisual);

    /// <summary>The settings to draw and half the wheel's total turn, in degrees.</summary>
    public void Set(ForceSettings settings, double halfRangeDegrees)
    {
        _settings = settings;
        _half = Math.Max(10, halfRangeDegrees);
        Refresh();
    }

    /// <summary>Where the wheel is right now (degrees from the middle), or null when it is not reporting.</summary>
    public void SetLive(double? angleDegrees)
    {
        _angle = angleDegrees;
        Refresh();
    }

    private void Refresh()
    {
        _force = _angle is double a ? CentringCurve.Force(a, 0, _settings, _half) : 0;
        InvalidateVisual();
    }

    /// <summary>What the marker reads, for the caption above the graph: the angle and the pull at it, or null with no live wheel.</summary>
    public string? Caption => _angle is double a ? $"Wheel {a:+0;-0;0}°   Pull {Math.Abs(_force) * 100:0}% {(_force > 0.0005 ? "to the right" : _force < -0.0005 ? "to the left" : "")}".TrimEnd() : null;

    private static Pen P(Brush b, double t) => new(b, t) { LineJoin = PenLineJoin.Round };

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 80 || h < 40) return;

        Brush line = ThemeManager.Brush("PlotLine"), ink = ThemeManager.Brush("TextMuted"), warn = ThemeManager.Brush("Warn");
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var face = new Typeface("Segoe UI");

        var frame = new Rect(0, 0, w, h);
        dc.DrawRoundedRectangle(ThemeManager.Brush("PlotBack"), P(ThemeManager.Brush("PlotBorder"), 1), frame, 3, 3);
        double midY = h / 2;
        dc.DrawLine(P(ThemeManager.Brush("PlotGrid"), 1), new Point(0, midY), new Point(w, midY));
        dc.DrawLine(P(ThemeManager.Brush("PlotGrid"), 1), new Point(w / 2, 0), new Point(w / 2, h));

        double[] values = CentringCurve.Sample(_settings, _half, Math.Clamp((int)w, 40, 400));
        double scale = Math.Max(0.12, values.Max(Math.Abs)) * 1.2;       // the curve fills most of the height whatever the strength
        double Y(double v) => midY - v / scale * (h / 2 - 4);
        double X(double angle) => (angle + _half) / (2 * _half) * w;

        // The friction floor: below this the wheel does not move, so it is the band the curve must stay outside.
        double floor = Math.Min(CentringCurve.FloorMax, Math.Clamp(_settings.FloorPercent, 0, 100) / 100.0);
        if (floor > 0)
        {
            var band = ThemeManager.Brush("TextMuted").Clone();
            band.Opacity = 0.16;
            dc.DrawRectangle(band, null, new Rect(0, Y(floor), w, Math.Max(1, Y(-floor) - Y(floor))));
        }

        var geo = new StreamGeometry();
        using (StreamGeometryContext c = geo.Open())
        {
            for (int i = 0; i < values.Length; i++)
            {
                var pt = new Point(i * w / (values.Length - 1), Y(values[i]));
                if (i == 0) c.BeginFigure(pt, false, false); else c.LineTo(pt, true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, P(line, 2), geo);

        if (_angle is double a)
        {
            double x = Math.Clamp(X(a), 0, w);
            dc.DrawLine(P(warn, 1), new Point(x, 0), new Point(x, h));
            dc.DrawEllipse(warn, null, new Point(x, Y(_force)), 4, 4);
        }

        void Label(string text, double x, double y, bool right = false)
        {
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 10, ink, dpi);
            dc.DrawText(ft, new Point(right ? x - ft.Width : x, y));
        }
        Label("pushes right", 6, 3);
        Label("pushes left", w - 6, h - 15, right: true);
        Label($"-{_half:0}°", 6, h - 15);
        Label("0°", w / 2 + 4, h - 15);
        Label($"+{_half:0}°", w - 6, 3, right: true);
    }
}

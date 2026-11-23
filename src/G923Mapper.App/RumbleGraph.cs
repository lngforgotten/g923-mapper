using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace G923Mapper.App;

/// <summary>
/// What the game asks of the rumble motors: two level meters for right now, and a graph of the last few seconds, so a bump or an engine
/// buzz can be seen as well as felt. The heavy motor is drawn in the accent colour and the light one in amber.
/// </summary>
public sealed class RumbleGraph : FrameworkElement
{
    private (byte First, byte Second)[] _samples = [];

    public RumbleGraph()
    {
        Loaded += (_, _) => { ThemeManager.Changed += OnTheme; InvalidateVisual(); };
        Unloaded += (_, _) => ThemeManager.Changed -= OnTheme;
    }

    private void OnTheme() => Dispatcher.BeginInvoke(InvalidateVisual);

    /// <summary>The samples to draw, oldest first; the last one is "now".</summary>
    public void Set((byte First, byte Second)[] samples)
    {
        _samples = samples;
        InvalidateVisual();
    }

    private static Pen P(Brush b, double t) => new(b, t) { LineJoin = PenLineJoin.Round };

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 60 || h < 20) return;

        Brush heavy = ThemeManager.Brush("PlotLine"), light = ThemeManager.Brush("Warn"), track = ThemeManager.Brush("BarTrack");
        Brush ink = ThemeManager.Brush("TextMuted");
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var face = new Typeface("Segoe UI");

        // Two meters for right now
        const double meterW = 12, gap = 6;
        byte nowA = _samples.Length > 0 ? _samples[^1].First : (byte)0, nowB = _samples.Length > 0 ? _samples[^1].Second : (byte)0;
        void Meter(double x, byte level, Brush fill, string caption)
        {
            var back = new Rect(x, 0, meterW, h - 14);
            dc.DrawRoundedRectangle(track, null, back, 3, 3);
            double filled = back.Height * level / 255.0;
            if (filled > 0.5) dc.DrawRoundedRectangle(fill, null, new Rect(x, back.Bottom - filled, meterW, filled), 3, 3);
            var ft = new FormattedText(caption, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 9, ink, dpi);
            dc.DrawText(ft, new Point(x + meterW / 2 - ft.Width / 2, h - 12));
        }
        Meter(0, nowA, heavy, "H");
        Meter(meterW + gap, nowB, light, "L");

        // The graph
        double gx = 2 * (meterW + gap) + 6, gw = w - gx, gh = h - 14;
        if (gw < 20) return;
        var frame = new Rect(gx, 0, gw, gh);
        dc.DrawRoundedRectangle(ThemeManager.Brush("PlotBack"), P(ThemeManager.Brush("PlotBorder"), 1), frame, 3, 3);
        dc.DrawLine(P(ThemeManager.Brush("PlotGrid"), 1), new Point(gx, gh / 2), new Point(gx + gw, gh / 2));

        if (_samples.Length >= 2)
        {
            // Newest on the right; the left edge is the oldest sample kept.
            void Area(Func<(byte First, byte Second), byte> pick, Brush colour, double opacity)
            {
                var geo = new StreamGeometry();
                using (StreamGeometryContext c = geo.Open())
                {
                    double step = gw / Math.Max(1, _samples.Length - 1);
                    double x0 = gx + gw - step * (_samples.Length - 1);
                    c.BeginFigure(new Point(x0, gh), true, true);
                    for (int i = 0; i < _samples.Length; i++)
                        c.LineTo(new Point(x0 + step * i, gh - gh * pick(_samples[i]) / 255.0 * 0.96), true, false);
                    c.LineTo(new Point(x0 + step * (_samples.Length - 1), gh), true, false);
                }
                geo.Freeze();
                var fill = colour.Clone();
                fill.Opacity = opacity;
                dc.DrawGeometry(fill, P(colour, 1.4), geo);
            }
            Area(s => s.Second, light, 0.22);
            Area(s => s.First, heavy, 0.28);
        }
        else
        {
            var ft = new FormattedText("Quiet. Nothing is asking for rumble.", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 11, ink, dpi);
            dc.DrawText(ft, new Point(gx + 10, gh / 2 - ft.Height / 2));
        }
    }
}

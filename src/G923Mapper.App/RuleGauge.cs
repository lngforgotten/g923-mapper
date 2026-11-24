using System;
using System.Windows;
using System.Windows.Media;

namespace G923Mapper.App;

/// <summary>
/// One rule on the test page. An axis rule shows its output as a bar (centred for a stick) with a tick where its input is; a button rule shows the
/// input as a bar with a tick at its trigger point, lit once the input is past it, and a dot for whether the button is down.
/// </summary>
public sealed class RuleGauge : FrameworkElement
{
    private RuleReading? _reading;

    public RuleGauge()
    {
        Height = 20;
        Loaded += (_, _) => { ThemeManager.Changed += OnTheme; InvalidateVisual(); };
        Unloaded += (_, _) => ThemeManager.Changed -= OnTheme;
    }

    private void OnTheme() => Dispatcher.BeginInvoke(InvalidateVisual);

    public void Set(RuleReading? reading)
    {
        if (_reading.Equals(reading)) return;
        _reading = reading;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 40 || _reading is not { } r) return;

        Brush track = ThemeManager.Brush("BarTrack"), tick = ThemeManager.Brush("TextPrimary");
        double dotSpace = 22;
        var bar = new Rect(0, 4, w - dotSpace, h - 8);
        dc.DrawRoundedRectangle(track, null, bar, 4, 4);

        // A position along the bar for a value: -1..1 for a signed input, 0..1 otherwise.
        double X(double v, bool signed) => bar.X + (signed ? (Math.Clamp(v, -1, 1) + 1) / 2 : Math.Clamp(v, 0, 1)) * bar.Width;

        if (r.IsButton)
        {
            bool over = r.Over;
            Brush fill = ThemeManager.Brush(over ? "Success" : "Accent");
            double from = r.Signed ? X(0, true) : bar.X;
            double to = X(r.Input, r.Signed);
            if (Math.Abs(to - from) > 0.5)
                dc.DrawRoundedRectangle(fill, null, new Rect(Math.Min(from, to), bar.Y, Math.Abs(to - from), bar.Height), 4, 4);
            // Where it triggers: a negative threshold on a wheel is on the left of centre.
            double tx = X(r.Threshold, r.Signed || r.Threshold < 0);
            dc.DrawRectangle(tick, null, new Rect(tx - 1, 0, 2, h));
            dc.DrawEllipse(r.On ? ThemeManager.Brush("Success") : track, new Pen(ThemeManager.Brush("PlotBorder"), 1), new Point(w - dotSpace / 2, h / 2), 6, 6);
        }
        else
        {
            bool stick = r.Rule.To.Contains("Stick", StringComparison.OrdinalIgnoreCase);
            Brush fill = ThemeManager.Brush("Accent");
            if (stick)
            {
                double mid = X(0, true), to = X(r.Output, true);
                if (Math.Abs(to - mid) > 0.5) dc.DrawRoundedRectangle(fill, null, new Rect(Math.Min(mid, to), bar.Y, Math.Abs(to - mid), bar.Height), 4, 4);
                dc.DrawRectangle(ThemeManager.Brush("BarTick"), null, new Rect(mid - 0.5, 2, 1, h - 4));
            }
            else
            {
                double to = X(r.Output, false);
                if (to - bar.X > 0.5) dc.DrawRoundedRectangle(fill, null, new Rect(bar.X, bar.Y, to - bar.X, bar.Height), 4, 4);
            }
            // Where the input is, so a deadzone or a curve shows as the bar lagging behind or running ahead of the tick.
            dc.DrawRectangle(tick, null, new Rect(X(r.Input, r.Signed) - 1, 0, 2, h));
        }
    }
}

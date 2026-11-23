using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace G923Mapper.App;

/// <summary>What <see cref="WheelView"/> shows for one moment: the wheel angle, the pedals and which buttons are down.</summary>
/// <param name="Angle">Degrees from centre, negative = left.</param>
/// <param name="MaxAngle">Half the lock-to-lock rotation, for the scale.</param>
/// <param name="RawWheel">The wheel axis' raw reading.</param>
/// <param name="Throttle">0..1, as are Brake and Clutch; the Raw values are the axis readings.</param>
public sealed record WheelFrame(double Angle, double MaxAngle, int RawWheel,
                                double Throttle, int RawThrottle, double Brake, int RawBrake, double Clutch, int RawClutch,
                                IReadOnlySet<int> ButtonsDown, int ButtonCount, int Pov, bool Live);

/// <summary>A picture of the wheel and pedals with their numbers: a steering wheel that turns, three pedal bars, and a grid of buttons.</summary>
public sealed class WheelView : FrameworkElement
{
    public WheelView()
    {
        Loaded += (_, _) => { ThemeManager.Changed += OnTheme; InvalidateVisual(); };
        Unloaded += (_, _) => ThemeManager.Changed -= OnTheme;
    }

    private void OnTheme() => Dispatcher.BeginInvoke(InvalidateVisual);

    private WheelFrame? _frame;

    public void Set(WheelFrame? frame)
    {
        _frame = frame;
        InvalidateVisual();
    }

    private const double W = 640, H = 230;

    protected override Size MeasureOverride(Size availableSize) => new(W, H);

    private static Typeface Face(bool bold = false) => new(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
    private static Typeface Mono => new("Consolas");

    private void Text(DrawingContext dc, string s, Point at, double size, Brush brush, bool centre = true, bool bold = false, bool mono = false)
    {
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, mono ? Mono : Face(bold), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(centre ? at.X - ft.Width / 2 : at.X, at.Y - ft.Height / 2));
    }

    protected override void OnRender(DrawingContext dc)
    {
        double scale = Math.Min(ActualWidth / W, ActualHeight / H);
        if (scale <= 0) return;
        dc.PushTransform(new ScaleTransform(scale, scale));

        WheelFrame f = _frame ?? new WheelFrame(0, 450, 0, 0, 0, 0, 0, 0, 0, new HashSet<int>(), 24, -1, false);
        Brush track = ThemeManager.Brush("BarTrack"), accent = ThemeManager.Brush("Accent"), line = ThemeManager.Brush("PlotBorder");
        Brush ink = ThemeManager.Brush("TextPrimary"), muted = ThemeManager.Brush("TextMuted");
        var pen = new Pen(line, 1.2);

        // ---- steering wheel
        var c = new Point(110, 108);
        const double R = 78;
        dc.DrawEllipse(null, new Pen(track, 2), c, R + 14, R + 14);                     // the lock-to-lock guide
        // A fixed marker at the top, and the wheel itself turned by the angle.
        dc.DrawGeometry(accent, null, Geo(new Point(c.X, c.Y - R - 18), new Point(c.X - 7, c.Y - R - 30), new Point(c.X + 7, c.Y - R - 30)));
        dc.PushTransform(new RotateTransform(f.Angle, c.X, c.Y));
        dc.DrawEllipse(null, new Pen(f.Live ? ThemeManager.Brush("TextSecondary") : track, 11), c, R, R);   // rim
        var spoke = new Pen(f.Live ? ThemeManager.Brush("TextSecondary") : track, 9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(spoke, new Point(c.X - R, c.Y), new Point(c.X + R, c.Y));            // left and right spokes
        dc.DrawLine(spoke, c, new Point(c.X, c.Y + R));                                  // the bottom spoke
        dc.DrawEllipse(accent, new Pen(ThemeManager.Brush("PlotBack"), 3), c, 16, 16);   // hub
        dc.DrawRectangle(accent, null, new Rect(c.X - 3, c.Y - R - 6, 6, 12));             // the 12 o'clock stripe on the rim
        dc.Pop();
        Text(dc, $"{f.Angle:+0.#;-0.#;0}°", new Point(c.X, 206), 18, ink, bold: true);
        Text(dc, $"raw {f.RawWheel}", new Point(c.X, 224), 10.5, muted, mono: true);

        // ---- pedals
        void Pedal(double x, string label, double v, int raw, Brush fill)
        {
            const double top = 56, h = 128, bw = 38;
            dc.DrawRoundedRectangle(track, pen, new Rect(x, top, bw, h), 6, 6);
            double filled = Math.Clamp(v, 0, 1) * h;
            if (filled > 0.5)
            {
                dc.PushClip(new RectangleGeometry(new Rect(x, top, bw, h), 6, 6));
                dc.DrawRectangle(fill, null, new Rect(x, top + h - filled, bw, filled));
                dc.Pop();
            }
            Text(dc, $"{v * 100:0}%", new Point(x + bw / 2, top - 14), 16, ink, bold: true);
            Text(dc, label, new Point(x + bw / 2, top + h + 14), 12, ink);
            Text(dc, raw.ToString(), new Point(x + bw / 2, top + h + 30), 10.5, muted, mono: true);
        }
        Pedal(236, "Clutch", f.Clutch, f.RawClutch, ThemeManager.Brush("Warn"));
        Pedal(292, "Brake", f.Brake, f.RawBrake, ThemeManager.Brush("Danger"));
        Pedal(348, "Throttle", f.Throttle, f.RawThrottle, ThemeManager.Brush("Success"));

        // ---- buttons
        Text(dc, "BUTTONS", new Point(430, 28), 10.5, muted, centre: false, bold: true);
        int count = Math.Max(f.ButtonCount, 12);
        const int cols = 6;
        const double cell = 26, gap = 5;
        for (int i = 0; i < count; i++)
        {
            double x = 430 + (i % cols) * (cell + gap), y = 44 + (i / cols) * (cell + gap);
            bool down = f.ButtonsDown.Contains(i + 1);
            dc.DrawRoundedRectangle(down ? accent : track, pen, new Rect(x, y, cell, cell), 6, 6);
            Text(dc, (i + 1).ToString(), new Point(x + cell / 2, y + cell / 2), 11, down ? Brushes.White : muted, bold: down);
        }

        // ---- D-pad / hat
        double hx = 540, hy = 205;
        Text(dc, "D-PAD", new Point(430, hy - 4), 10.5, muted, centre: false, bold: true);
        void Arm(double dx, double dy, bool on) => dc.DrawRoundedRectangle(on ? accent : track, pen, new Rect(hx + dx - 9, hy + dy - 9, 18, 18), 4, 4);
        var (up, down2, left, right) = DeviceMap.Hat(f.Pov);
        Arm(0, -18, up); Arm(18, 0, right); Arm(0, 18, down2); Arm(-18, 0, left);

        if (!f.Live)
            Text(dc, "Move the wheel to wake it", new Point(292, 24), 12, ThemeManager.Brush("Warn"), bold: true);

        dc.Pop();
    }

    private static Geometry Geo(Point a, Point b, Point c)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(a, true, true);
            ctx.LineTo(b, true, false);
            ctx.LineTo(c, true, false);
        }
        g.Freeze();
        return g;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace G923Mapper.App;

/// <summary>
/// Signal-flow lines laid over a controller picture: a chip for each wheel control the profile uses, in a column down each side of the
/// controller, and from each a line across to the control it feeds. A line lights up while its wheel control is being used.
/// Only the chips take the mouse (hovering one shows just its lines); everything else falls through to the picture.
/// </summary>
public sealed class FlowOverlay : FrameworkElement
{
    private PadDiagram? _pad;
    private PadKind _kind;
    private IReadOnlyList<(string From, string Key)> _flows = [];
    private Func<string, double> _level = _ => 0;
    private string? _hoverSource, _hoverControl;
    private Dictionary<string, Rect> _chipRects = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>With many lines the quiet ones are nearly hidden; hovering a chip here, or a control on the picture (see <see cref="SetHoverControl"/>), shows just its lines.</summary>
    private const int ManyLines = 7;

    public FlowOverlay()
    {
        Loaded += (_, _) => { ThemeManager.Changed += OnTheme; InvalidateVisual(); };
        Unloaded += (_, _) => ThemeManager.Changed -= OnTheme;
    }

    private void OnTheme() => Dispatcher.BeginInvoke(InvalidateVisual);

    /// <summary>The picture the lines end on. It must sit inside the same layout as this overlay.</summary>
    public void Attach(PadDiagram pad) => _pad = pad;

    /// <summary>The control of the picture the mouse is over (null = none), so its lines stand out.</summary>
    public void SetHoverControl(string? key)
    {
        if (string.Equals(_hoverControl, key, StringComparison.OrdinalIgnoreCase)) return;
        _hoverControl = key;
        InvalidateVisual();
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters p) =>
        _chipRects.Values.Any(r => r.Contains(p.HitPoint)) ? new PointHitTestResult(this, p.HitPoint) : null;

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point m = e.GetPosition(this);
        string? now = _chipRects.FirstOrDefault(kv => kv.Value.Contains(m)).Key;
        if (string.Equals(now, _hoverSource, StringComparison.OrdinalIgnoreCase)) return;
        _hoverSource = now;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverSource is null) return;
        _hoverSource = null;
        InvalidateVisual();
    }

    /// <summary>The pairs to draw, how strongly each wheel control is being used right now (0 to 1, absolute), and the kind of controller drawn.</summary>
    public void Set(IReadOnlyList<(string From, string Key)> flows, Func<string, double> level, PadKind kind)
    {
        _flows = flows;
        _level = level;
        _kind = kind;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        _chipRects = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        if (_pad is null || _flows.Count == 0 || ActualWidth < 100) return;
        double padScale = Math.Min(_pad.ActualWidth / PadLayout.Width, _pad.ActualHeight / PadLayout.Height);
        if (padScale <= 0) return;

        Point padTopLeft = _pad.TranslatePoint(new Point(0, 0), this);
        Rect padBox = new(padTopLeft.X, padTopLeft.Y, PadLayout.Width * padScale, PadLayout.Height * padScale);

        Point Centre(string key)
        {
            PadShape sh = PadLayout.Of(key, _kind) ?? new PadShape(150, 90, 0, 0, true);
            return new Point(padBox.X + sh.CentreX * padScale, padBox.Y + sh.CentreY * padScale);
        }

        // Where a line lands: the edge of the control that faces the chip's side, so lines do not run across the controls they are not for.
        Point Landing(string key, bool fromLeft)
        {
            PadShape sh = PadLayout.Of(key, _kind) ?? new PadShape(150, 90, 0, 0, true);
            double x = fromLeft ? sh.X - 1 : sh.X + sh.W + 1;
            return new Point(padBox.X + x * padScale, padBox.Y + sh.CentreY * padScale);
        }

        Color accent = ThemeManager.Brush("Accent") is SolidColorBrush a ? a.Color : Colors.DodgerBlue;
        Brush idleBrush = ThemeManager.Brush("TextMuted");
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var face = new Typeface("Segoe UI Semibold");

        // Each wheel control goes to the side its controls are on, ordered top to bottom by where they are, so lines cross as little as they can.
        var targets = _flows.GroupBy(f => f.From, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Source: g.Key, X: g.Average(f => Centre(f.Key).X), Y: g.Average(f => Centre(f.Key).Y)))
            .ToList();
        double midX = padBox.X + padBox.Width / 2;
        var left = targets.Where(t => t.X < midX).OrderBy(t => t.Y).Select(t => t.Source).ToList();
        var right = targets.Where(t => t.X >= midX).OrderBy(t => t.Y).Select(t => t.Source).ToList();
        // Keep the two columns about even: when one side has far more, move its lowest chips to the other side.
        while (left.Count > right.Count + 2) { right.Add(left[^1]); left.RemoveAt(left.Count - 1); }
        while (right.Count > left.Count + 2) { left.Add(right[^1]); right.RemoveAt(right.Count - 1); }
        double YOf(string s) => targets.First(t => t.Source == s).Y;
        left = left.OrderBy(YOf).ToList();
        right = right.OrderBy(YOf).ToList();

        const double gapToPad = 22, margin = 6;
        var sides = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);   // source -> on the left
        var chips = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        var texts = new Dictionary<string, FormattedText>(StringComparer.OrdinalIgnoreCase);

        void Column(List<string> column, bool onLeft)
        {
            if (column.Count == 0) return;
            double pitch = Math.Clamp(padBox.Height / column.Count, 18, 28);
            double chipH = pitch >= 24 ? 21 : 16;
            double fontSize = pitch >= 24 ? 11 : 9.5;
            double span = pitch * (column.Count - 1);
            double y0 = padBox.Y + padBox.Height / 2 - span / 2 - chipH / 2;
            for (int i = 0; i < column.Count; i++)
            {
                string src = column[i];
                var ft = new FormattedText(src, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, fontSize, Brushes.White, dpi);
                texts[src] = ft;
                double w = ft.Width + 16;
                double x = onLeft ? Math.Max(margin, padBox.X - gapToPad - w) : Math.Min(ActualWidth - margin - w, padBox.Right + gapToPad);
                chips[src] = new Rect(x, y0 + i * pitch, w, chipH);
                sides[src] = onLeft;
            }
        }
        Column(left, true);
        Column(right, false);
        _chipRects = chips;

        // Quiet lines first, then the ones under the mouse, then the lit ones, so a bright line is never hidden under a dull one.
        bool many = _flows.Count > ManyLines;
        bool Hovered(string from, string key) =>
            string.Equals(from, _hoverSource, StringComparison.OrdinalIgnoreCase) || string.Equals(key, _hoverControl, StringComparison.OrdinalIgnoreCase);
        foreach (int tier in new[] { 0, 1, 2 })
            foreach (var (from, key) in _flows)
            {
                if (!chips.TryGetValue(from, out Rect chip)) continue;
                double level = Math.Clamp(Math.Abs(_level(from)), 0, 1);
                bool lit = level > 0.04, hovered = Hovered(from, key);
                if ((lit ? 2 : hovered ? 1 : 0) != tier) continue;

                bool onLeft = sides[from];
                Point p0 = new(onLeft ? chip.Right : chip.Left, chip.Y + chip.Height / 2);
                Point p1 = Landing(key, onLeft);
                double dx = Math.Max(20, Math.Abs(p1.X - p0.X)) * (onLeft ? 1 : -1);
                var geo = new StreamGeometry();
                using (StreamGeometryContext c = geo.Open())
                {
                    c.BeginFigure(p0, false, false);
                    c.BezierTo(new Point(p0.X + dx * .55, p0.Y), new Point(p1.X - dx * .45, p1.Y), p1, true, true);
                }
                geo.Freeze();
                Brush stroke = lit ? new SolidColorBrush(Color.FromArgb((byte)(150 + level * 105), accent.R, accent.G, accent.B))
                    : hovered ? new SolidColorBrush(Color.FromArgb(0xE0, accent.R, accent.G, accent.B))
                    : Faded(idleBrush, many ? .22 : .45);
                dc.DrawGeometry(null, new Pen(stroke, lit ? 2.4 : hovered ? 1.8 : 1.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geo);
                if (lit || hovered || !many)
                    dc.DrawEllipse(lit || hovered ? new SolidColorBrush(accent) : Faded(idleBrush, .55), null, p1, lit ? 3.4 : 2.4, lit ? 3.4 : 2.4);
            }

        foreach (var (src, chip) in chips)
        {
            bool lit = Math.Abs(_level(src)) > 0.04;
            bool hov = string.Equals(src, _hoverSource, StringComparison.OrdinalIgnoreCase)
                || _flows.Any(f => f.From.Equals(src, StringComparison.OrdinalIgnoreCase) && string.Equals(f.Key, _hoverControl, StringComparison.OrdinalIgnoreCase));
            dc.DrawRoundedRectangle(lit ? new SolidColorBrush(accent) : ThemeManager.Brush("CellBack"),
                new Pen(lit || hov ? new SolidColorBrush(accent) : ThemeManager.Brush("CellBorder"), hov ? 2 : 1), chip, chip.Height / 2, chip.Height / 2);
            FormattedText ft = texts[src];
            ft.SetForegroundBrush(lit ? ((0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255 > 0.6 ? Brushes.Black : Brushes.White) : ThemeManager.Brush("TextSecondary"));
            dc.DrawText(ft, new Point(chip.X + 8, chip.Y + (chip.Height - ft.Height) / 2));
        }
    }

    private static Brush Faded(Brush b, double o)
    {
        var c = b.Clone();
        c.Opacity = o;
        return c;
    }
}

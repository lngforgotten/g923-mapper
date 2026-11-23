using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace G923Mapper.App;

/// <summary>
/// A picture of a game controller, Xbox or DualShock 4 shaped. It lights up as the game would see it (sticks move, triggers fill, buttons
/// light), and it can point at one control, with the ones already chosen marked, for the "Remap all" walk-through.
/// </summary>
public sealed class PadDiagram : FrameworkElement
{
    public PadDiagram()
    {
        Loaded += (_, _) => { ThemeManager.Changed += OnTheme; LabelsChanged += OnTheme; InvalidateVisual(); };
        Unloaded += (_, _) => { ThemeManager.Changed -= OnTheme; LabelsChanged -= OnTheme; };
    }

    private static bool _showLabels;

    /// <summary>When on, every mapped control of every picture is tagged with the wheel control that feeds it.</summary>
    public static bool ShowLabels
    {
        get => _showLabels;
        set { if (_showLabels == value) return; _showLabels = value; LabelsChanged?.Invoke(); }
    }

    /// <summary>Raised when <see cref="ShowLabels"/> changes, so the checkboxes and every picture follow.</summary>
    public static event Action? LabelsChanged;

    private IReadOnlyDictionary<string, string> _labels = new Dictionary<string, string>();

    /// <summary>The word to show on each control while <see cref="ShowLabels"/> is on (key to text).</summary>
    public void SetLabels(IReadOnlyDictionary<string, string> labels)
    {
        _labels = labels;
        if (ShowLabels) InvalidateVisual();
    }

    private void OnTheme() => Dispatcher.BeginInvoke(InvalidateVisual);

    private PadState? _state;
    private PadKind _kind;
    private string? _pointAt;
    private HashSet<string> _marked = new(StringComparer.OrdinalIgnoreCase);
    private string? _hover;
    private IReadOnlyDictionary<string, string> _tips = new Dictionary<string, string>();

    /// <summary>When true the controls react to the mouse: they light up under it, show what feeds them, and can be clicked.</summary>
    public bool Clickable { get; set; }

    /// <summary>Raised when a control is clicked, with its key in <see cref="PadLayout"/> (for example "Button:A" or "LeftStick").</summary>
    public event Action<string>? ControlClicked;

    /// <summary>Raised when the control under the mouse changes (its key, or null when the mouse is on none); only while <see cref="Clickable"/>.</summary>
    public event Action<string?>? HoverChanged;

    /// <summary>What to say about each control when the mouse rests on it (key to text).</summary>
    public void SetTips(IReadOnlyDictionary<string, string> tips) => _tips = tips;

    /// <summary>Show this state (null = a controller at rest), drawn as this kind of controller.</summary>
    public void Set(PadState? state, PadKind kind)
    {
        _state = state;
        _kind = kind;
        InvalidateVisual();
    }

    /// <summary>Point at the control for this rule target (null = none) and mark the targets already chosen ("Remap all").</summary>
    public void Point(string? target, IEnumerable<string> done)
    {
        _pointAt = target;
        _marked = new HashSet<string>(done, StringComparer.OrdinalIgnoreCase);
        InvalidateVisual();
    }

    private string? ControlAt(Point p)
    {
        double scale = Math.Min(ActualWidth / PadLayout.Width, ActualHeight / PadLayout.Height);
        return scale <= 0 ? null : PadControls.At(p.X / scale, p.Y / scale, _kind);
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!Clickable) return;
        string? now = ControlAt(e.GetPosition(this));
        if (now == _hover) return;
        _hover = now;
        HoverChanged?.Invoke(now);
        Cursor = now is null ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.Hand;
        ToolTip = now is not null && _tips.TryGetValue(now, out string? tip) ? tip : (now is null ? null : PadControls.Title(now, _kind) + "\nNot mapped yet. Click to choose a wheel control.");
        InvalidateVisual();
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover is null) return;
        _hover = null;
        HoverChanged?.Invoke(null);
        Cursor = System.Windows.Input.Cursors.Arrow;
        ToolTip = null;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!Clickable) return;
        if (ControlAt(e.GetPosition(this)) is string key) ControlClicked?.Invoke(key);
    }

    protected override Size MeasureOverride(Size availableSize) => new(PadLayout.Width, PadLayout.Height);

    private static Typeface Face => new("Segoe UI Semibold");

    private void Text(DrawingContext dc, string s, Point centre, double size, Brush brush)
    {
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(centre.X - ft.Width / 2, centre.Y - ft.Height / 2));
    }

    private static Rect Box(PadShape s) => new(s.X, s.Y, s.W, s.H);

    // ---------------------------------------------------------------- body outlines

    private readonly record struct Seg(bool Line, Point C1, Point C2, Point End);

    private static Point Mirror(Point p) => new(PadLayout.Width - p.X, p.Y);

    /// <summary>A closed outline drawn down the left side as given, then mirrored back up the right.</summary>
    private static Geometry Outline(Point start, Seg[] left)
    {
        var geo = new StreamGeometry();
        using (StreamGeometryContext c = geo.Open())
        {
            c.BeginFigure(start, true, true);
            foreach (Seg s in left)
            {
                if (s.Line) c.LineTo(s.End, true, true);
                else c.BezierTo(s.C1, s.C2, s.End, true, true);
            }
            for (int i = left.Length - 1; i >= 0; i--)
            {
                Point from = i == 0 ? start : left[i - 1].End;
                if (left[i].Line) c.LineTo(Mirror(from), true, true);
                else c.BezierTo(Mirror(left[i].C2), Mirror(left[i].C1), Mirror(from), true, true);
            }
        }
        geo.Freeze();
        return geo;
    }

    private static readonly Geometry XboxBody = Outline(new Point(150, 24), [
        new(true, default, default, new Point(86, 24)),
        new(false, new Point(52, 24), new Point(26, 48), new Point(21, 96)),
        new(false, new Point(16, 134), new Point(14, 168), new Point(38, 172)),
        new(false, new Point(60, 175), new Point(74, 160), new Point(98, 160)),
        new(true, default, default, new Point(150, 160)),
    ]);

    private static readonly Geometry DualShockBody = Outline(new Point(150, 28), [
        new(true, default, default, new Point(92, 28)),
        new(false, new Point(56, 28), new Point(30, 40), new Point(22, 78)),
        new(false, new Point(14, 118), new Point(10, 164), new Point(34, 172)),
        new(false, new Point(58, 180), new Point(74, 156), new Point(100, 158)),
        new(true, default, default, new Point(150, 158)),
    ]);

    // ---------------------------------------------------------------- palette: a dark controller with shaded, glossy parts, whatever the app theme

    private static readonly Dictionary<Color, Brush> Domes = new();
    private static readonly Dictionary<Color, Brush> Solids = new();

    private static Color H(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    private static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    private static T Fz<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private static Brush Solid(Color c)
    {
        if (!Solids.TryGetValue(c, out Brush? b)) Solids[c] = b = Fz(new SolidColorBrush(c));
        return b;
    }

    private static Brush Grad(double angle, params (double At, Color C)[] stops)
    {
        var c = new GradientStopCollection();
        foreach (var (at, col) in stops) c.Add(new GradientStop(col, at));
        return Fz(new LinearGradientBrush(c, angle));
    }

    /// <summary>A button top lit from the upper left: light spot, body colour, dark edge.</summary>
    private static Brush Dome(Color c)
    {
        if (Domes.TryGetValue(c, out Brush? b)) return b;
        var stops = new GradientStopCollection { new GradientStop(Mix(c, Colors.White, .35), 0), new GradientStop(c, .55), new GradientStop(Mix(c, Colors.Black, .5), 1) };
        return Domes[c] = Fz(new RadialGradientBrush(stops) { GradientOrigin = new Point(.36, .28), Center = new Point(.5, .5), RadiusX = .62, RadiusY = .62 });
    }

    private static Pen MakePen(Brush b, double w) => Fz(new Pen(b, w));

    private static readonly Brush BodyFill = Grad(90, (0, H("#555961")), (.45, H("#2c2f34")), (1, H("#131417")));
    private static readonly Brush Sheen = Grad(90, (0, Color.FromArgb(0x44, 255, 255, 255)), (.4, Color.FromArgb(0, 255, 255, 255)));
    private static readonly Pen BodyEdge = MakePen(Grad(60, (0, H("#8d939c")), (.5, H("#2a2d32")), (1, H("#060709"))), 1.6);
    private static readonly Brush ShadowBrush = Solid(Color.FromArgb(0x14, 0, 0, 0));
    private static readonly Pen[] ShadowPens = [MakePen(ShadowBrush, 12), MakePen(ShadowBrush, 8), MakePen(ShadowBrush, 4)];
    private static readonly Brush Lip = Grad(90, (0, H("#636870")), (1, H("#0a0b0d")));
    private static readonly Brush Recess = Fz(new RadialGradientBrush(H("#2b2e33"), H("#060708")) { GradientOrigin = new Point(.5, .4) });
    private static readonly Brush BumperFill = Grad(90, (0, H("#5d616a")), (1, H("#25272c")));
    private static readonly Brush TriggerFill = Grad(90, (0, H("#2d2f35")), (1, H("#141518")));
    private static readonly Brush ArmFill = Grad(90, (0, H("#4e5158")), (1, H("#1f2125")));
    private static readonly Brush Chrome = Grad(90, (0, H("#dde1e6")), (1, H("#6c7178")));
    private static readonly Brush Gloss = Solid(Color.FromArgb(0x55, 255, 255, 255));
    private static readonly Brush LabelInk = Solid(H("#aab0b9"));
    private static readonly Pen PartEdge = MakePen(Solid(H("#07080a")), 1);
    private static readonly Pen GroovePen = MakePen(Solid(Color.FromArgb(0x80, 0, 0, 0)), .8);
    private static readonly Pen RidgePen = MakePen(Solid(Color.FromArgb(0x60, 255, 255, 255)), .6);

    private static Color Accent => ThemeManager.Brush("Accent") is SolidColorBrush b ? b.Color : H("#3b82f6");

    // ---------------------------------------------------------------- drawing

    protected override void OnRender(DrawingContext dc)
    {
        var s = _state ?? new PadState();
        bool ps = _kind == PadKind.DualShock4;
        Color acc = Accent;
        Brush litBar = Grad(90, (0, Mix(acc, Colors.White, .4)), (1, acc));
        // Text on a lit part: dark on a light accent (high contrast gold), white otherwise.
        Brush onLit = (0.299 * acc.R + 0.587 * acc.G + 0.114 * acc.B) / 255 > 0.6 ? Brushes.Black : Brushes.White;

        double scale = Math.Min(ActualWidth / PadLayout.Width, ActualHeight / PadLayout.Height);
        if (scale <= 0) return;
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, PadLayout.Width, PadLayout.Height));   // so the whole picture hears the mouse

        PadShape Shape(string target) => PadLayout.Of(target, _kind)!.Value;
        Point Centre(PadShape sh) => new(sh.CentreX, sh.CentreY);
        void Glow(Point c, double r) => dc.DrawEllipse(null, new Pen(Solid(acc), 2), c, r + 3, r + 3);

        // Body: a soft shadow, the shaded shell, a sheen across the top and a lit rim
        Geometry bodyGeo = ps ? DualShockBody : XboxBody;
        dc.PushTransform(new TranslateTransform(0, 2.5));
        foreach (Pen sp in ShadowPens) dc.DrawGeometry(null, sp, bodyGeo);
        dc.Pop();
        dc.DrawGeometry(BodyFill, BodyEdge, bodyGeo);
        dc.DrawGeometry(Sheen, null, bodyGeo);

        if (ps)
        {
            dc.DrawRoundedRectangle(Grad(90, (0, H("#2c2f35")), (1, H("#111215"))), PartEdge, new Rect(118, 34, 64, 34), 6, 6);   // touchpad
            dc.DrawRoundedRectangle(Gloss, null, new Rect(121, 36, 58, 10), 4, 4);
            dc.DrawRoundedRectangle(Solid(Mix(acc, Colors.White, .2)), null, new Rect(124, 29.4, 52, 2.4), 1.2, 1.2);          // light bar
        }

        // Bumpers and triggers
        void Bumper(string target, bool pressed, string label)
        {
            PadShape sh = Shape(target);
            dc.DrawRoundedRectangle(pressed ? litBar : BumperFill, PartEdge, Box(sh), 5, 5);
            dc.DrawLine(RidgePen, new Point(sh.X + 6, sh.Y + 1.4), new Point(sh.X + sh.W - 6, sh.Y + 1.4));
            Text(dc, label, Centre(sh), 8, pressed ? onLit : LabelInk);
        }
        Bumper("Button:LeftShoulder", s[PadButton.LeftShoulder], ps ? "L1" : "LB");
        Bumper("Button:RightShoulder", s[PadButton.RightShoulder], ps ? "R1" : "RB");

        void Trigger(string target, byte v, string label)
        {
            PadShape sh = Shape(target);
            dc.DrawRoundedRectangle(TriggerFill, PartEdge, Box(sh), 5, 5);
            if (v > 0) dc.DrawRoundedRectangle(litBar, null, new Rect(sh.X, sh.Y, Math.Max(8, sh.W * v / 255), sh.H), 5, 5);
            Text(dc, label, Centre(sh), 7.5, v > 128 ? onLit : LabelInk);
        }
        Trigger("LeftTrigger", s.LeftTrigger, ps ? "L2" : "LT");
        Trigger("RightTrigger", s.RightTrigger, ps ? "R2" : "RT");

        // Sticks: a recessed well with a lipped rim, and a domed rubber cap that moves with the stick (up is up) and lights when clicked.
        void Stick(string target, short x, short y, bool click)
        {
            PadShape sh = Shape(target);
            Point c = Centre(sh);
            double r = sh.W / 2;
            dc.DrawEllipse(Lip, null, c, r + 1.5, r + 1.5);
            dc.DrawEllipse(Recess, null, c, r - .8, r - .8);
            var cap = new Point(c.X + x / 32768.0 * 11, c.Y - y / 32768.0 * 11);
            dc.DrawEllipse(Solid(Color.FromArgb(0x60, 0, 0, 0)), null, new Point(cap.X + 1, cap.Y + 2), 13.5, 13.5);
            dc.DrawEllipse(click ? Dome(acc) : Dome(H("#5c6068")), PartEdge, cap, 13, 13);
            foreach (double ring in new[] { 9.5, 6.5, 3.5 }) dc.DrawEllipse(null, GroovePen, cap, ring, ring);
            dc.DrawEllipse(Gloss, null, new Point(cap.X - 3, cap.Y - 6.5), 5, 2.6);
        }
        Stick("LeftStickX", s.LeftX, s.LeftY, s[PadButton.LeftThumb]);
        Stick("RightStickX", s.RightX, s.RightY, s[PadButton.RightThumb]);

        // D-pad: a cross whose arms light (a dished one on the Xbox, four separate arrows on the DualShock)
        PadShape up = Shape("Button:DPadUp");
        Point cross = new(up.CentreX, up.Y + up.H * 1.5);
        if (!ps)
        {
            dc.DrawEllipse(Lip, null, cross, 30, 30);
            dc.DrawEllipse(Recess, null, cross, 28.6, 28.6);
        }
        void Arm(string target, bool on, double dx, double dy)
        {
            PadShape sh = Shape(target);
            dc.DrawRoundedRectangle(on ? litBar : ArmFill, PartEdge, Box(sh), 3.5, 3.5);
            if (!ps) return;
            Point c = Centre(sh);
            var tri = new StreamGeometry();
            using (StreamGeometryContext g = tri.Open())
            {
                g.BeginFigure(new Point(c.X + dx * 4, c.Y + dy * 4), true, true);
                g.LineTo(new Point(c.X - dx * 3 - dy * 3.6, c.Y - dy * 3 + dx * 3.6), false, false);
                g.LineTo(new Point(c.X - dx * 3 + dy * 3.6, c.Y - dy * 3 - dx * 3.6), false, false);
            }
            tri.Freeze();
            dc.DrawGeometry(on ? onLit : LabelInk, null, tri);
        }
        Arm("Button:DPadUp", s[PadButton.DPadUp], 0, -1); Arm("Button:DPadDown", s[PadButton.DPadDown], 0, 1);
        Arm("Button:DPadLeft", s[PadButton.DPadLeft], -1, 0); Arm("Button:DPadRight", s[PadButton.DPadRight], 1, 0);
        if (!ps)
        {
            dc.DrawRoundedRectangle(ArmFill, null, new Rect(up.X, up.Y + up.H, up.W, up.H), 0, 0);   // the hub joins the arms
            dc.DrawEllipse(Recess, null, cross, 5.2, 5.2);
        }

        // Face buttons, in the familiar colours: glossy domes on the Xbox, dark domes with coloured symbols on the DualShock
        void Face4(string target, bool on, string label, Color col)
        {
            PadShape sh = Shape(target);
            Point c = Centre(sh);
            double r = sh.W / 2;
            dc.DrawEllipse(Lip, null, c, r + 1.2, r + 1.2);
            if (ps)
            {
                dc.DrawEllipse(Dome(on ? Mix(col, Colors.Black, .25) : H("#3b3e44")), null, c, r - .5, r - .5);
                Text(dc, label, c, 12, on ? Brushes.White : Solid(col));
            }
            else
            {
                dc.DrawEllipse(Dome(on ? Mix(col, Colors.White, .5) : col), null, c, r - .5, r - .5);
                dc.DrawEllipse(Gloss, null, new Point(c.X - 1.5, c.Y - r * .45), r * .55, r * .3);
                Text(dc, label, c, 11.5, Solid(Mix(col, Colors.Black, .62)));
            }
            if (on) Glow(c, r);
        }
        Face4("Button:Y", s[PadButton.Y], ps ? "△" : "Y", ps ? H("#3cc3a1") : H("#e6b422"));
        Face4("Button:A", s[PadButton.A], ps ? "✕" : "A", ps ? H("#5a8be0") : H("#58a832"));
        Face4("Button:X", s[PadButton.X], ps ? "□" : "X", ps ? H("#d87bb5") : H("#2e6bd1"));
        Face4("Button:B", s[PadButton.B], ps ? "○" : "B", ps ? H("#e4506a") : H("#d23c2a"));

        // Centre buttons
        void Small(string target, bool on, string label, bool labelBelow = true)
        {
            PadShape sh = Shape(target);
            Point c = Centre(sh);
            double r = sh.W / 2;
            dc.DrawEllipse(Lip, null, c, r + 1.2, r + 1.2);
            dc.DrawEllipse(Dome(on ? acc : H("#3d4046")), null, c, r - .3, r - .3);
            if (label.Length > 0) Text(dc, label, new Point(c.X, c.Y + (labelBelow ? 13 : -13)), 6.5, LabelInk);
        }
        Small("Button:Back", s[PadButton.Back], ps ? "SHARE" : "BACK", labelBelow: !ps);
        Small("Button:Start", s[PadButton.Start], ps ? "OPTIONS" : "START", labelBelow: !ps);

        // The big logo button: a chrome ring with a green glow on the Xbox, a dark dome with the PS mark on the DualShock
        {
            PadShape sh = Shape("Button:Guide");
            Point c = Centre(sh);
            bool on = s[PadButton.Guide];
            if (ps)
            {
                dc.DrawEllipse(Lip, null, c, 8.2, 8.2);
                dc.DrawEllipse(Dome(on ? acc : H("#3d4046")), null, c, 7, 7);
                Text(dc, "PS", c, 6.5, on ? onLit : LabelInk);
            }
            else
            {
                dc.DrawEllipse(Chrome, null, c, 9.4, 9.4);
                dc.DrawEllipse(Dome(on ? acc : H("#1b1d20")), null, c, 7.2, 7.2);
                dc.DrawEllipse(null, new Pen(Solid(on ? H("#c8ff90") : H("#79c93a")), 1.5), c, 6.4, 6.4);
                var x = new Pen(Solid(Color.FromArgb(0xC0, 255, 255, 255)), 1.3);
                dc.DrawLine(x, new Point(c.X - 3, c.Y - 3), new Point(c.X + 3, c.Y + 3));
                dc.DrawLine(x, new Point(c.X - 3, c.Y + 3), new Point(c.X + 3, c.Y - 3));
            }
        }

        // Tags: the wheel control behind each mapped control, on a dark pill so it reads on any colour.
        if (ShowLabels)
            foreach (var (key, text) in _labels)
            {
                if (PadLayout.Of(key, _kind) is not { } at) continue;
                string shown = ShortLabel(text);
                var ft = new FormattedText(shown, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, at.W <= 18 ? 5.6 : 6.8, Solid(Mix(acc, Colors.White, .7)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
                double w = ft.Width + 7, h = ft.Height + 2;
                var pill = new Rect(at.CentreX - w / 2, at.CentreY - h / 2, w, h);
                dc.DrawRoundedRectangle(Solid(Color.FromArgb(0xE0, 8, 9, 11)), MakePen(Solid(Mix(acc, Colors.Black, .2)), .8), pill, h / 2, h / 2);
                dc.DrawText(ft, new Point(pill.X + 3.5, pill.Y + 1));
            }

        // Under the mouse: a soft ring, so it is clear what a click will pick.
        if (Clickable && _hover is not null && PadLayout.Of(_hover, _kind) is { } over)
            Ring(dc, over, ThemeManager.Brush("Accent"), 2.5, 4);

        // "Remap all": controls already chosen get a green ring, and the one being asked about a bold amber one.
        foreach (string done in _marked)
            if (PadLayout.Of(done, _kind) is { } d) Ring(dc, d, ThemeManager.Brush("Success"), 2, 3);
        if (_pointAt is not null && PadLayout.Of(_pointAt, _kind) is { } now)
        {
            Ring(dc, now, ThemeManager.Brush("Warn"), 3.5, 6);
            Ring(dc, now, ThemeManager.Brush("Warn"), 1.2, 11);
        }

        dc.Pop();
    }

    /// <summary>A tag short enough to sit on a control: "dpadUp" is "Up", "buttonA" is "A", and anything long is cut.</summary>
    internal static string ShortLabel(string text)
    {
        string t = text;
        foreach (string prefix in new[] { "dpad", "button" })
            if (t.Length > prefix.Length && t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { t = char.ToUpperInvariant(t[prefix.Length]) + t[(prefix.Length + 1)..]; break; }
        return t.Length > 9 ? t[..8] + "..." : t;
    }

    /// <summary>A ring around a control, <paramref name="gap"/> units outside its edge.</summary>
    private static void Ring(DrawingContext dc, PadShape sh, Brush brush, double thickness, double gap)
    {
        var pen = new Pen(brush, thickness);
        if (sh.Round) dc.DrawEllipse(null, pen, new Point(sh.CentreX, sh.CentreY), sh.W / 2 + gap, sh.H / 2 + gap);
        else dc.DrawRoundedRectangle(null, pen, new Rect(sh.X - gap, sh.Y - gap, sh.W + 2 * gap, sh.H + 2 * gap), 6, 6);
    }
}

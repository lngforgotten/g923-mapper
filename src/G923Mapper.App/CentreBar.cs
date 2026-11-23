using System;
using System.Windows;
using System.Windows.Media;

namespace G923Mapper.App;

/// <summary>
/// A horizontal level bar. Bipolar (-1..1, fills from the centre outwards) for steering and sticks,
/// otherwise 0..1 filling from the left for pedals and triggers.
/// </summary>
public sealed class CentreBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(CentreBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BipolarProperty = DependencyProperty.Register(
        nameof(Bipolar), typeof(bool), typeof(CentreBar),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(CentreBar),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public CentreBar()
    {
        // Repaint when the theme changes; subscribe only while on screen so closed windows are not kept alive.
        Loaded += (_, _) => { ThemeManager.Changed += OnTheme; InvalidateVisual(); };
        Unloaded += (_, _) => ThemeManager.Changed -= OnTheme;
    }

    private void OnTheme() => Dispatcher.BeginInvoke(InvalidateVisual);

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool Bipolar { get => (bool)GetValue(BipolarProperty); set => SetValue(BipolarProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width, 14);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double radius = h / 2;
        dc.DrawRoundedRectangle(ThemeManager.Brush("BarTrack"), null, new Rect(0, 0, w, h), radius, radius);

        double v = double.IsNaN(Value) ? 0 : Value;
        Rect fill;
        if (Bipolar)
        {
            v = Math.Clamp(v, -1, 1);
            double mid = w / 2, x = mid + v * mid;
            fill = new Rect(Math.Min(mid, x), 0, Math.Abs(x - mid), h);
        }
        else
        {
            v = Math.Clamp(v, 0, 1);
            fill = new Rect(0, 0, v * w, h);
        }

        if (fill.Width >= 1)
        {
            // Clip to the rounded track so the fill never pokes out of the ends.
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h), radius, radius));
            dc.DrawRectangle(Fill, null, fill);
            dc.Pop();
        }
        if (Bipolar) dc.DrawLine(new Pen(ThemeManager.Brush("BarTick"), 1), new Point(w / 2, 1), new Point(w / 2, h - 1));
    }

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
}

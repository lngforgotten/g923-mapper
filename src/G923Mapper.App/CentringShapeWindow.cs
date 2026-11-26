using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace G923Mapper.App;

/// <summary>
/// "Adjust curve" for a profile's or a mode's own centring: the curve drawn out, with its strength and shape sliders. It only changes the numbers
/// the editor holds; nothing reaches the wheel until the profile is saved. Measuring the wheel stays in Settings, since it belongs to the wheel.
/// </summary>
internal sealed class CentringShapeWindow : Window
{
    private readonly ForceSettings _appWide;
    private readonly double _halfRange;
    private readonly CentringGraph _graph = new() { Height = 170 };
    private readonly Slider _strength, _centre, _peak, _lightening, _damping;

    /// <summary>The curve as set by the sliders.</summary>
    public CentringPreset Result => new("Custom", (int)_strength.Value, (int)_centre.Value, (int)_peak.Value, (int)_lightening.Value, (int)_damping.Value);

    public CentringShapeWindow(Window owner, string title, CentringPreset curve, ForceSettings appWide, double halfRangeDegrees)
    {
        Title = title;
        Owner = owner;
        _appWide = appWide;
        _halfRange = halfRangeDegrees;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        SetResourceReference(BackgroundProperty, "WindowBack");
        SetResourceReference(ForegroundProperty, "TextPrimary");
        FontFamily = owner.FontFamily; FontSize = owner.FontSize;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(_graph);

        Slider Row(string label, int min, int max, int step, int value, string unit, string help)
        {
            var slider = new Slider { Minimum = min, Maximum = max, TickFrequency = step, IsSnapToTickEnabled = false, SmallChange = 1, LargeChange = step, VerticalAlignment = VerticalAlignment.Center };
            slider.Value = value;
            AutomationProperties.SetName(slider, label);
            var shown = new TextBlock { Width = 48, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Text = value + unit };
            slider.ValueChanged += (_, e) =>
            {
                double whole = Math.Round(e.NewValue);
                if (whole != e.NewValue) { slider.Value = whole; return; }
                shown.Text = (int)whole + unit;
                Draw();
            };
            var grid = new Grid { Margin = new Thickness(0, 10, 0, 0), ToolTip = help, Background = System.Windows.Media.Brushes.Transparent };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(slider, 1); grid.Children.Add(slider);
            Grid.SetColumn(shown, 2); grid.Children.Add(shown);
            panel.Children.Add(grid);
            return slider;
        }

        _strength = Row("Strength", 0, 100, 10, curve.Strength, "%", "How strong the pull is at its strongest. The same value as the Centring slider in the editor.");
        _centre = Row("Centre zone", 1, 45, 1, curve.CentreDegrees, "°", "Degrees either side of straight ahead over which the pull ramps up from nothing, so the middle is not twitchy.");
        _peak = Row("Peak angle", 10, 180, 5, curve.PeakDegrees, "°", "The angle at which the pull is strongest.");
        _lightening = Row("Lightening", 0, 100, 5, curve.LighteningPercent, "%", "How much of the peak pull is left at full lock. 100% means none is lost.");
        _damping = Row("Damping", 0, 100, 5, curve.DampingPercent, "%", "Resists fast turns, so the wheel does not swing past the middle when you let go. Keep it small.");

        var hint = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 12, 0, 0),
            Text = "You feel these once the profile is saved. The least pull and which way your wheel pushes come from Measure wheel in Settings.",
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        panel.Children.Add(hint);

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Padding = new Thickness(10, 5, 10, 5) };
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), Children = { ok, cancel } });

        Content = panel;
        Draw();
    }

    private void Draw()
    {
        if (_damping is null) return;   // still being built
        ForceSettings s = _appWide.Clone();   // the wheel's own floor comes from Settings
        Result.ApplyTo(s);
        _graph.Set(s, _halfRange);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace G923Mapper.App;

/// <summary>
/// The Test tab: live gauges for the wheel, the pedals and every rule of the running profile, and a switch that pauses the output to the virtual
/// controller, so a profile can be checked without launching a game. The gauges read what the profile would send, so they keep working while paused.
/// </summary>
public partial class TestPanel : UserControl
{
    private AppServices? _s;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly List<(RuleGauge Gauge, TextBlock Text)> _rows = new();
    private string _rowsKey = "";

    public TestPanel()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) _timer.Start(); else _timer.Stop(); };   // only work while the tab is showing
    }

    public void Initialise(AppServices services) => _s = services;

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (_s is null) return;
        if (!_s.Service.IsRunning) { PauseBox.IsChecked = false; return; }
        _s.Service.Paused = PauseBox.IsChecked == true;
        AppLog.Info(_s.Service.Paused ? "Output paused from the Test tab." : "Output resumed from the Test tab.");
    }

    private void Tick()
    {
        if (_s is null) return;
        MapperSnapshot? snap = _s.Service.Latest;
        bool running = _s.Service.IsRunning;
        PauseBox.IsEnabled = running;
        if (PauseBox.IsChecked != _s.Service.Paused) PauseBox.IsChecked = _s.Service.Paused;   // the tray menu and shortcut change it too

        if (!running || snap is null)
        {
            TxtState.Text = running ? "Waiting for the wheel..." : "The mapper is stopped. Start it from the Home tab to test.";
            TxtState.SetResourceReference(TextBlock.ForegroundProperty, "Warn");
            BarSteer.Value = BarThrottle.Value = BarBrake.Value = BarClutch.Value = 0;
            TxtSteer.Text = TxtThrottle.Text = TxtBrake.Text = TxtClutch.Text = "";
            return;
        }

        bool paused = _s.Service.Paused;
        TxtState.Text = paused
            ? "Output is paused: the games see a neutral controller. The gauges below show what \"" + snap.ProfileName + "\" would send."
            : "Output is live: the games see what the profile sends to the virtual controller.";
        TxtState.SetResourceReference(TextBlock.ForegroundProperty, paused ? "Success" : "TextMuted");

        TxtMode.Text = snap.ModeName is { } mode ? $"Mode: {mode}" : "";
        TxtMode.Visibility = snap.ModeName is null ? Visibility.Collapsed : Visibility.Visible;

        double steer = snap.Input.Get("wheel");
        BarSteer.Value = steer;
        TxtSteer.Text = $"{steer * snap.Device.RotationDegrees / 2:+0;-0;0}°  ({steer * 100:+0;-0;0}%)";
        Pedal(BarThrottle, TxtThrottle, snap, "throttle");
        Pedal(BarBrake, TxtBrake, snap, "brake");
        Pedal(BarClutch, TxtClutch, snap, "clutch");

        ShowRules(snap);
    }

    private static void Pedal(CentreBar bar, TextBlock text, MapperSnapshot snap, string name)
    {
        double v = snap.Input.Get(name);
        bar.Value = v;
        int raw = snap.Device.Axes.TryGetValue(name, out var a) && a.SourceIndex >= 0 && a.SourceIndex < snap.Raw.Axes.Length ? snap.Raw.Axes[a.SourceIndex] : 0;
        text.Text = $"{v * 100:0}%  (raw {raw})";
    }

    private void ShowRules(MapperSnapshot snap)
    {
        IReadOnlyList<RuleReading> readings = snap.Readings;
        string key = snap.ProfileName + "|" + snap.ModeName + "|" + _s!.Store.Version + "|" + readings.Count;
        if (key != _rowsKey) Rebuild(snap, readings, key);
        for (int i = 0; i < _rows.Count && i < readings.Count; i++)
        {
            RuleReading r = readings[i];
            _rows[i].Gauge.Set(r);
            _rows[i].Text.Text = ReadingText.Describe(r);
        }
    }

    private void Rebuild(MapperSnapshot snap, IReadOnlyList<RuleReading> readings, string key)
    {
        _rowsKey = key;
        _rows.Clear();
        RuleGrid.Children.Clear();
        RuleGrid.RowDefinitions.Clear();
        RuleGrid.ColumnDefinitions.Clear();
        RuleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        RuleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RuleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });

        for (int i = 0; i < readings.Count; i++)
        {
            Rule rule = readings[i].Rule;
            RuleGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new TextBlock { Text = $"{rule.From}  →  {rule.To}", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 10, 3), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = $"{rule.From} to {rule.To}" };
            var gauge = new RuleGauge { Margin = new Thickness(0, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center };
            var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12 };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            Grid.SetRow(name, i); Grid.SetRow(gauge, i); Grid.SetColumn(gauge, 1); Grid.SetRow(text, i); Grid.SetColumn(text, 2);
            RuleGrid.Children.Add(name); RuleGrid.Children.Add(gauge); RuleGrid.Children.Add(text);
            _rows.Add((gauge, text));
        }

        // Rules that exist in the profile but are not running, and why.
        Profile? profile = _s!.Store.Get(snap.ProfileName);
        var inactive = new List<string>();
        if (profile is not null)
        {
            KeyOutputStatus keys = KeyMappings.Status(profile);
            var active = new HashSet<Rule>(readings.Select(r => r.Rule));
            foreach (Rule r in profile.Rules.Where(r => !active.Contains(r)))
            {
                bool isKey = Target.TryParse(r.To, out Target? t, out _) && t is { Kind: TargetKind.Key };
                string why = isKey && keys.State != KeyOutput.Active ? "keys and mouse are off for this profile (" + keys.Reason + ")"
                    : r.To.TrimStart().StartsWith(Target.SecondPadPrefix, StringComparison.OrdinalIgnoreCase) ? "the second controller is off"
                    : "not in use";
                inactive.Add($"{r.From} → {r.To}: {why}");
            }
        }
        TxtInactive.Text = inactive.Count == 0 ? "" : "NOT ACTIVE\n" + string.Join("\n", inactive);
        TxtRulesTitle.Text = readings.Count == 0 ? "WHAT EACH RULE DOES (NO ACTIVE RULES)" : $"WHAT EACH RULE DOES ({readings.Count})";
    }

}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace G923Mapper.App;

/// <summary>What the pop-out window shows.</summary>
public enum MiniView { Full, Compact, Mode, Controller, Rules }

public static class MiniViews
{
    public static string Label(MiniView v) => v switch
    {
        MiniView.Full => "Full",
        MiniView.Compact => "Compact",
        MiniView.Mode => "Mode only",
        MiniView.Controller => "Controller",
        _ => "Rules",
    };

    /// <summary>A view from its saved name; anything not recognised means Full.</summary>
    public static MiniView Parse(string? text) => Enum.TryParse(text, true, out MiniView v) && Enum.IsDefined(v) ? v : MiniView.Full;

    /// <summary>The size a window takes when it switches to this view (width, height), so each one is a sensible shape to start with.</summary>
    public static (double Width, double Height) PreferredSize(MiniView v) => v switch
    {
        MiniView.Full => (340, 470),
        MiniView.Compact => (320, 210),
        MiniView.Mode => (340, 150),
        MiniView.Controller => (340, 300),
        _ => (440, 420),
    };
}

/// <summary>
/// A small always-on-top window for keeping an eye on the wheel while a game is in front (a borderless windowed game lets it sit on top), in
/// five views: everything, a compact one (mode and bars), the mode name alone in large letters, just the controller, or a gauge for each rule.
/// The last view used is remembered.
/// </summary>
public partial class MiniWindow : Window
{
    private readonly AppServices _s;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private MiniView _view = MiniView.Full;
    private readonly List<(RuleGauge Gauge, TextBlock Name)> _rules = new();
    private string _rulesKey = "";

    public MiniWindow(AppServices services)
    {
        InitializeComponent();
        _s = services;

        ViewBox.ItemsSource = Enum.GetValues<MiniView>().Select(MiniViews.Label).ToList();
        _view = MiniViews.Parse(_s.Settings.MiniView);
        ViewBox.SelectedIndex = (int)_view;
        ApplyView(resize: true);

        // Top right of the main screen, out of the way.
        Left = SystemParameters.WorkArea.Right - Width - 24;
        Top = SystemParameters.WorkArea.Top + 24;
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private void OnTop_Click(object sender, RoutedEventArgs e) => Topmost = OnTopBox.IsChecked == true;

    private void View_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ViewBox.SelectedIndex < 0 || (MiniView)ViewBox.SelectedIndex == _view) return;
        _view = (MiniView)ViewBox.SelectedIndex;
        _s.Settings.MiniView = _view.ToString();
        _s.Settings.Save();
        ApplyView(resize: true);
    }

    /// <summary>Show the parts this view is made of and give the room to the one that should have it.</summary>
    private void ApplyView(bool resize)
    {
        bool mode = _view == MiniView.Mode, controller = _view == MiniView.Controller, rules = _view == MiniView.Rules;
        bool compact = _view == MiniView.Compact, full = _view == MiniView.Full;

        PadBox.Visibility = full || controller ? Visibility.Visible : Visibility.Collapsed;
        SteerRow.Visibility = PedalRow.Visibility = full || compact ? Visibility.Visible : Visibility.Collapsed;
        RuleScroll.Visibility = rules ? Visibility.Visible : Visibility.Collapsed;
        // The mode is shown in every view but the controller one (which has the room for it only as a small label).
        ModeBox.Visibility = Visibility.Visible;

        // Which row takes up the spare room: the mode name alone, the picture alone, or the rules.
        RowMode.Height = mode ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        RowPad.Height = controller ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        RowRules.Height = rules ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        RowSteer.Height = RowPedals.Height = GridLength.Auto;

        TxtMode.FontSize = mode ? 64 : compact ? 30 : full ? 22 : 18;
        ModeBox.MaxHeight = mode ? double.PositiveInfinity : compact ? 48 : full ? 34 : 28;
        ModeBox.StretchDirection = mode ? System.Windows.Controls.StretchDirection.Both : System.Windows.Controls.StretchDirection.DownOnly;
        PadBox.MaxWidth = full ? 316 : double.PositiveInfinity;
        Pad.AlwaysLabels = controller;
        _tipsKey = "";

        if (resize)
        {
            var (w, h) = MiniViews.PreferredSize(_view);
            Width = w;
            Height = h;
        }
        _rulesKey = "";   // the rule rows are rebuilt when the view is shown
    }

    private void Tick()
    {
        MapperSnapshot? snap = _s.Service.Latest;
        if (snap is null)
        {
            Dot.Fill = ThemeManager.Neutral;
            TxtProfile.Text = _s.Service.IsRunning ? "Waiting for the wheel" : "Stopped";
            TxtMode.Text = _view == MiniView.Mode ? TxtProfile.Text : "";
            ModeBox.Visibility = _view == MiniView.Mode ? Visibility.Visible : Visibility.Collapsed;
            Pad.Set(null, PadKind.Xbox360);
            BarSteer.Value = BarThrottle.Value = BarBrake.Value = BarClutch.Value = 0;
            TxtSteer.Text = "0°";
            return;
        }

        Dot.Fill = _s.Service.State == MapperState.Running ? ThemeManager.Success : ThemeManager.Warn;
        TxtProfile.Text = snap.ProfileName;

        // The mode name, or (in the mode-only view, where it is the whole point) the profile's name if there are no modes.
        string? modeText = snap.ModeName ?? (_view == MiniView.Mode ? snap.ProfileName : null);
        TxtMode.Text = modeText ?? "";
        ModeBox.Visibility = modeText is null ? Visibility.Collapsed : Visibility.Visible;

        switch (_view)
        {
            case MiniView.Full:
            case MiniView.Controller:
                PadKind kind = _s.Store.Get(snap.ProfileName) is { } p ? PadKinds.Of(p) : PadKind.Xbox360;
                Pad.Set(snap.Pad, kind);
                if (_view == MiniView.Controller) ShowTips(snap, kind);
                if (_view == MiniView.Full) ShowBars(snap);
                break;
            case MiniView.Compact:
                ShowBars(snap);
                break;
            case MiniView.Rules:
                ShowRules(snap);
                break;
        }
    }

    private void ShowBars(MapperSnapshot snap)
    {
        double steer = snap.Input.Get("wheel");
        BarSteer.Value = steer;
        TxtSteer.Text = $"{steer * snap.Device.RotationDegrees / 2:+0;-0;0}°";
        BarThrottle.Value = snap.Input.Get("throttle");
        BarBrake.Value = snap.Input.Get("brake");
        BarClutch.Value = snap.Input.Get("clutch");
    }

    private string _tipsKey = "";

    /// <summary>The Controller view tags every control with the wheel control behind it, in the active mode, so it can be read without the main window.</summary>
    private void ShowTips(MapperSnapshot snap, PadKind kind)
    {
        string key = snap.ProfileName + "|" + snap.ModeName + "|" + _s.Store.Version;
        if (key == _tipsKey) return;
        _tipsKey = key;
        Profile? running = RunningProfile.Of(_s.Store, snap);
        Pad.SetLabels(running is null ? new Dictionary<string, string>() : PadControls.Labels(running));
    }

    private void ShowRules(MapperSnapshot snap)
    {
        IReadOnlyList<RuleReading> readings = snap.Readings;
        string key = snap.ProfileName + "|" + snap.ModeName + "|" + _s.Store.Version + "|" + readings.Count;
        if (key != _rulesKey)
        {
            _rulesKey = key;
            _rules.Clear();
            RuleGrid.Children.Clear();
            RuleGrid.RowDefinitions.Clear();
            RuleGrid.ColumnDefinitions.Clear();
            RuleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            RuleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < readings.Count; i++)
            {
                Rule rule = readings[i].Rule;
                RuleGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var name = new TextBlock { Text = $"{rule.From} → {rule.To}", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 8, 2), FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
                var gauge = new RuleGauge { Margin = new Thickness(0, 2, 0, 2), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(name, i); Grid.SetRow(gauge, i); Grid.SetColumn(gauge, 1);
                RuleGrid.Children.Add(name); RuleGrid.Children.Add(gauge);
                _rules.Add((gauge, name));
            }
        }
        for (int i = 0; i < _rules.Count && i < readings.Count; i++)
        {
            _rules[i].Gauge.Set(readings[i]);
            string tip = ReadingText.Describe(readings[i]);
            _rules[i].Gauge.ToolTip = tip;
            _rules[i].Name.ToolTip = tip;
        }
    }
}

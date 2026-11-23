using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace G923Mapper.App;

/// <summary>
/// The "Get started" checklist: driver, wheel, calibration, a profile, and the optional extras. Every row updates by itself from what is
/// really true (so it works as a first-run guide and as a health check), and each has a button that does the step.
/// </summary>
public partial class SetupWindow : Window
{
    private sealed class Row
    {
        public required Border Card;
        public required TextBlock Icon, Title, Detail;
        public required StackPanel Buttons;
        public Button? Primary, Secondary;
        public ComboBox? Profiles;
    }

    private readonly AppServices _s;
    private readonly MainWindow _main;
    private readonly Dictionary<string, Row> _rows = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _loadingProfiles;

    public SetupWindow(AppServices services, MainWindow main)
    {
        InitializeComponent();
        _s = services;
        _main = main;
        BuildRows();
        Refresh();
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    // ---------------------------------------------------------------- building the rows (once)

    private void BuildRows()
    {
        AddRow(SetupChecklist.Driver, "Get driver", Open("https://github.com/nefarius/ViGEmBus/releases"));
        AddRow(SetupChecklist.Wheel, "Open Devices", () => _main.SelectTab("Devices"));
        AddRow(SetupChecklist.Calibrate, "Calibrate...", () => { _s.RunCalibration(this); Refresh(); });

        Row profile = AddRow(SetupChecklist.Profile, null, null);
        profile.Profiles = new ComboBox { MinWidth = 170 };
        profile.Profiles.SelectionChanged += (_, _) =>
        {
            if (_loadingProfiles || profile.Profiles.SelectedItem is not string key) return;
            _main.SelectProfile(key);
            Refresh();
        };
        profile.Buttons.Children.Add(profile.Profiles);

        AddRow(SetupChecklist.Hide, "Hide wheel", HideOrShow);
        Row test = AddRow(SetupChecklist.Test, "Open Windows test", () =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("control.exe", "joy.cpl") { UseShellExecute = true }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        });
        test.Secondary = new Button { Content = "Test vibration", Style = (Style)FindResource("Secondary"), Margin = new Thickness(8, 0, 0, 0) };
        test.Secondary.Click += (_, _) => _s.Service.TestForce();
        test.Buttons.Children.Add(test.Secondary);
    }

    private static Action Open(string url) => () =>
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    };

    private Row AddRow(string id, string? buttonText, Action? click)
    {
        var icon = new TextBlock { FontSize = 18, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0) };
        var title = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var detail = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(title);
        text.Children.Add(detail);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(icon);
        grid.Children.Add(text);
        grid.Children.Add(buttons);

        var card = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 10), Child = grid };
        Rows.Children.Add(card);

        var row = new Row { Card = card, Icon = icon, Title = title, Detail = detail, Buttons = buttons };
        if (buttonText is not null && click is not null)
        {
            row.Primary = new Button { Content = buttonText, Style = (Style)FindResource("Secondary"), MinWidth = 110 };
            row.Primary.Click += (_, _) => click();
            buttons.Children.Add(row.Primary);
        }
        _rows[id] = row;
        return row;
    }

    private async void HideOrShow()
    {
        if (!_s.HidHide.IsInstalled) { Open(HidHideClient.DownloadUrl)(); return; }
        if (_rows[SetupChecklist.Hide].Primary is { } b) b.IsEnabled = false;
        await _s.SetWheelHiddenAsync(!_s.WheelHidden);
        Refresh();
    }

    // ---------------------------------------------------------------- keeping them true

    private SetupFacts Gather()
    {
        var svc = _s.Service;
        var (installed, running) = HealthProbe.Service("ViGEmBus");
        var ids = svc.WheelUsbIds;
        bool ps = ids is { Vid: 0x046D, Pid: 0xC266 or 0xC267 } || svc.WheelName.Contains("PlayStation", StringComparison.OrdinalIgnoreCase);
        var profileName = _s.Settings.Profile;
        return new SetupFacts
        {
            DriverInstalled = installed, DriverRunning = running,
            WheelFound = svc.State == MapperState.Running, WheelName = svc.WheelName,
            WheelReporting = svc.Latest?.Raw.Live == true,
            WheelCalibrated = svc.WheelCalibrated, PlayStationWheel = ps,
            ProfileValid = _s.Store.Get(profileName) is not null, ProfileName = profileName,
            HidHideInstalled = _s.HidHide.IsInstalled, WheelHidden = _s.WheelHidden,
        };
    }

    private void Refresh()
    {
        var steps = SetupChecklist.Evaluate(Gather());
        TxtSummary.Text = SetupChecklist.Summary(steps);
        var required = steps.Where(s => s.Required).ToList();
        Progress.Value = required.Count == 0 ? 1 : (double)required.Count(s => s.State == SetupState.Done) / required.Count;

        foreach (SetupStep step in steps)
        {
            if (!_rows.TryGetValue(step.Id, out Row? row)) continue;
            row.Title.Text = step.Title;
            row.Detail.Text = step.Detail;
            (row.Icon.Text, string brush) = step.State switch
            {
                SetupState.Done => ("✓", "Success"),
                SetupState.ToDo => ("●", "Warn"),
                SetupState.Waiting => ("…", "TextMuted"),
                _ => ("○", "TextMuted"),
            };
            row.Icon.SetResourceReference(TextBlock.ForegroundProperty, brush);
            row.Card.Opacity = step.State == SetupState.Waiting ? 0.6 : 1;
        }

        // Buttons whose label depends on the situation.
        if (_rows[SetupChecklist.Driver].Primary is { } driver) driver.Visibility = steps[0].State == SetupState.Done ? Visibility.Collapsed : Visibility.Visible;
        if (_rows[SetupChecklist.Wheel].Primary is { } wheel) wheel.Visibility = steps[1].State == SetupState.Done ? Visibility.Collapsed : Visibility.Visible;
        if (_rows[SetupChecklist.Calibrate].Primary is { } cal)
        {
            cal.IsEnabled = steps[2].State != SetupState.Waiting;
            cal.Content = steps[2].State == SetupState.Done ? "Calibrate again" : "Calibrate...";
        }
        if (_rows[SetupChecklist.Hide].Primary is { } hide)
        {
            hide.IsEnabled = true;
            hide.Content = !_s.HidHide.IsInstalled ? "Get HidHide" : _s.WheelHidden ? "Show wheel again" : "Hide wheel";
        }
        RefreshProfileList();
    }

    private void RefreshProfileList()
    {
        if (_rows[SetupChecklist.Profile].Profiles is not { } box || box.IsDropDownOpen) return;
        var keys = _s.Store.Files
            .Select(f => System.IO.Path.GetFileNameWithoutExtension(f))
            .Where(k => _s.Store.Get(k) is not null)
            .ToList();
        if (!keys.SequenceEqual(box.Items.Cast<string>()))
        {
            _loadingProfiles = true;
            box.ItemsSource = keys;
            _loadingProfiles = false;
        }
        string current = _s.Settings.Profile;
        if (!string.Equals(box.SelectedItem as string, current, StringComparison.OrdinalIgnoreCase))
        {
            _loadingProfiles = true;
            box.SelectedItem = keys.FirstOrDefault(k => string.Equals(k, current, StringComparison.OrdinalIgnoreCase));
            _loadingProfiles = false;
        }
    }

    private void Check_Click(object sender, RoutedEventArgs e)
    {
        _ = _s.RefreshHiddenStateAsync().ContinueWith(_ => Dispatcher.BeginInvoke(Refresh));
        Refresh();
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        _s.Settings.SetupDone = true;
        _s.Settings.Save();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace G923Mapper.App;

/// <summary>The Devices tab: the game controllers Windows sees, whether the wheel is hidden, and a live picture of the wheel with its numbers.</summary>
public partial class DevicesPanel : UserControl
{
    public sealed record Row(string Symbol, Brush Brush, string Title, string Detail);

    private AppServices? _s;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private IReadOnlyList<Row> _rows = [];
    private readonly SampleRing _rumble = new(200);   // 10 seconds at one sample per 50 ms

    private void ShowLabels_Click(object sender, RoutedEventArgs e)
    {
        if (_s is null) return;
        PadDiagram.ShowLabels = ShowLabelsBox.IsChecked == true;
        _s.Settings.ShowPadLabels = PadDiagram.ShowLabels;
        _s.Settings.Save();
    }

    private IReadOnlyList<(string From, string Key)> _flows = [];
    private int _tipsVersion = -1;
    private string? _tipsProfile;

    public DevicesPanel()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => ShowLive();
        Flow.Attach(Pad);
        Pad.HoverChanged += Flow.SetHoverControl;
        KeyChips.ChipClicked += rule => (Window.GetWindow(this) as MainWindow)?.EditKeyMapping(rule);
        PadDiagram.LabelsChanged += () => ShowLabelsBox.IsChecked = PadDiagram.ShowLabels;
        ShowLabelsBox.IsChecked = PadDiagram.ShowLabels;
        Pad.Clickable = true;
        Pad.ControlClicked += key => (Window.GetWindow(this) as MainWindow)?.AssignControl(key);
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) { _timer.Start(); Refresh(); } else _timer.Stop();   // only work while the tab is showing
        };
    }

    public void Initialise(AppServices services)
    {
        _s = services;
        RefreshHide();
    }

    private async void Refresh()
    {
        if (_s is null) return;
        var found = await Task.Run(ControllerLister.List);
        var inUse = _s.Service.WheelUsbIds;
        _rows = found.Select(c =>
        {
            bool mine = inUse is { } ids && ids.Vid == c.Vid && ids.Pid == c.Pid;
            bool calibrated = _s.Devices.IsKnownWheel(c.Vid, c.Pid);
            bool virtualPad = c.Vid == 0x045E && c.Pid == 0x028E;
            var notes = new List<string> { c.Ids };
            if (mine) notes.Add("in use by this app");
            if (virtualPad) notes.Add("the virtual controller games see");
            if (calibrated && !virtualPad) notes.Add($"calibrated as {_s.Devices.FindByUsb(c.Vid, c.Pid)!.Name}");
            else if (c.Vid == 0x046D) notes.Add("Logitech device with no saved calibration");
            if (mine && _s.WheelHidden) notes.Add("hidden from games by HidHide");
            string symbol = mine ? "●" : virtualPad ? "◆" : "○";
            Brush brush = mine ? ThemeManager.Success : virtualPad ? ThemeManager.Muted : ThemeManager.Muted;
            return new Row(symbol, brush, c.Name, string.Join("  ·  ", notes));
        }).ToList();
        DeviceList.ItemsSource = _rows;
        NoDevices.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshHide();
    }

    private void ShowLive()
    {
        if (_s is null) return;
        var snap = _s.Service.Latest;
        if (snap is null)
        {
            Pad.Set(null, PadKind.Xbox360);
            Flow.Set([], _ => 0, PadKind.Xbox360);
            _rumble.Clear();
            Rumble.Set([]);
            View.Set(null);
            LiveText.Text = _s.Service.IsRunning ? "Waiting for the wheel…" : "The mapper is stopped, so there is nothing to read. Start it from the Home tab.";
            return;
        }

        Profile? shown = RunningProfile.Of(_s.Store, snap);   // the profile with its active mode laid over it
        PadKind padKind = shown is not null ? PadKinds.Of(shown) : PadKind.Xbox360;
        Pad.Set(snap.Pad, padKind);
        if (_tipsVersion != _s.Store.Version || _tipsProfile != snap.ProfileName + "|" + snap.ModeName)
        {
            _tipsVersion = _s.Store.Version;
            _tipsProfile = snap.ProfileName + "|" + snap.ModeName;
            Pad.SetTips(PadTips.For(shown, padKind));
            Pad.SetLabels(shown is null ? new Dictionary<string, string>() : PadControls.Labels(shown));
            _flows = shown is null ? [] : PadControls.Flows(shown);
            KeyChips.Set(shown is null ? [] : KeyMappings.Of(shown));
            string note = KeyStrip.NoteFor(shown);
            KeyNote.Text = note;
            KeyNote.Visibility = note.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        Flow.Set(_flows, name => snap.Input.Get(name), padKind);
        KeyChips.Update(name => snap.Input.Get(name));

        var (heavy, lightLevel) = _s.Service.RumbleNow;
        _rumble.Add(heavy, lightLevel);
        Rumble.Set(_rumble.ToArray());

        var raw = snap.Raw;
        int Raw(string axis) => snap.Device.Axes.TryGetValue(axis, out var a) && a.SourceIndex >= 0 && a.SourceIndex < raw.Axes.Length ? raw.Axes[a.SourceIndex] : 0;
        var down = new HashSet<int>();
        for (int i = 0; i < raw.Buttons.Length; i++) if (raw.Buttons[i]) down.Add(i + 1);

        double max = snap.Device.RotationDegrees / 2;
        double angle = Math.Clamp(snap.Input.Get("wheel") * max, -max, max);
        View.Set(new WheelFrame(angle, max, Raw("wheel"),
            snap.Input.Get("throttle"), Raw("throttle"), snap.Input.Get("brake"), Raw("brake"), snap.Input.Get("clutch"), Raw("clutch"),
            down, Math.Max(raw.ButtonCount, 12), raw.Pov, raw.Live));

        var names = ButtonNames.Down(snap.Input, snap.Device);
        var sb = new StringBuilder();
        sb.Append("Buttons down: ").Append(names.Count == 0 ? "none" : string.Join(", ", names));
        sb.Append($"   ·   centre {snap.Device.Axes.GetValueOrDefault("wheel")?.Centre}   ·   lock to lock {snap.Device.RotationDegrees:0.#}°");
        LiveText.Text = sb.ToString();
    }

    // ---------------------------------------------------------------- hide / show the wheel

    private void RefreshHide()
    {
        if (_s is null) return;
        if (!_s.HidHide.IsInstalled) { HideButton.Content = "Get HidHide"; return; }
        HideButton.Content = _s.WheelHidden ? "Show wheel again" : "Hide wheel from games";
    }

    private async void Hide_Click(object sender, RoutedEventArgs e)
    {
        if (_s is null) return;
        if (!_s.HidHide.IsInstalled)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(HidHideClient.DownloadUrl) { UseShellExecute = true }); } catch { }
            return;
        }
        HideButton.IsEnabled = false;
        HideMessage.Text = "Waiting for permission…";
        var result = await _s.SetWheelHiddenAsync(!_s.WheelHidden);
        HideMessage.Text = result.Message;
        HideButton.IsEnabled = true;
        Refresh();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder("Game controllers\n");
        foreach (var r in _rows) sb.AppendLine($"- {r.Title}: {r.Detail}");
        try { Clipboard.SetText(sb.ToString()); } catch (System.Runtime.InteropServices.COMException) { }
    }

    private void Test_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("control.exe", "joy.cpl") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ThemedMessageBox.Show(Window.GetWindow(this), "Could not open the Windows game controller test:\n" + ex.Message, "Test in Windows", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}


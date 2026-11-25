using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace G923Mapper.App;

/// <summary>
/// "Add a wheel control": choose what it does (next mode, previous mode, a mode by name), then press a wheel button or a pedal. What was pressed is
/// found the same way as the Learn button in Edit rules. The result is an ordinary rule sending to Mode:..., exactly as a wheel button has always
/// switched modes.
/// </summary>
internal sealed class WheelControlWindow : Window
{
    private readonly Func<MapperSnapshot?> _latest;
    private readonly DeviceMap? _device;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly ComboBox _action = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _name = new() { Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(6, 4, 6, 4), Visibility = Visibility.Collapsed };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button _ok = new() { Content = "Add", IsDefault = true, MinWidth = 80, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
    private readonly Button _again = new() { Content = "Listen again", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    private RawInput? _baseline;
    private string? _control;
    private DateTime _until;

    /// <summary>The rule that was made, or null when the window was cancelled.</summary>
    public Rule? Result { get; private set; }

    public WheelControlWindow(Window owner, Func<MapperSnapshot?> latest, DeviceMap? device)
    {
        Title = "Add a wheel control";
        Owner = owner;
        _latest = latest;
        _device = device;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        SetResourceReference(BackgroundProperty, "WindowBack");
        SetResourceReference(ForegroundProperty, "TextPrimary");
        FontFamily = owner.FontFamily; FontSize = owner.FontSize;

        _action.ItemsSource = new[] { "Next mode", "Previous mode", "A mode by name" };
        _action.SelectedIndex = 0;
        _action.SelectionChanged += (_, _) => { _name.Visibility = _action.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed; UpdateOk(); };
        _name.TextChanged += (_, _) => UpdateOk();
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        _again.Click += (_, _) => Listen();

        _ok.Click += (_, _) =>
        {
            string target = _action.SelectedIndex == 0 ? "Mode:Next" : _action.SelectedIndex == 1 ? "Mode:Previous" : "Mode:" + _name.Text.Trim();
            bool axis = _device is not null && _control is not null && _device.Axes.ContainsKey(_control);
            Result = new Rule { From = _control!, To = target, Curve = "linear", Threshold = axis ? 0.5 : null };
            DialogResult = true;
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Padding = new Thickness(10, 5, 10, 5) };

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = "What should it do?" }, _action, _name,
                new TextBlock { Text = "Which wheel control?", Margin = new Thickness(0, 12, 0, 0) }, _status, _hint, _again,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), Children = { _ok, cancel } },
            },
        };
        _timer.Tick += (_, _) => Poll();
        Loaded += (_, _) => Listen();
        Closed += (_, _) => _timer.Stop();
    }

    private void Listen()
    {
        _control = null;
        UpdateOk();
        MapperSnapshot? snap = _latest();
        if (snap is null || _device is null)
        {
            _status.Text = "No wheel is being read";
            _hint.Text = "Start the mapper with the wheel connected, then open this window again.";
            return;
        }
        _baseline = snap.Raw;
        _until = DateTime.UtcNow.AddSeconds(15);
        _status.Text = "Press a wheel button, or press a pedal...";
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        _hint.Text = "A pedal switches modes once it goes about halfway down. Turning the wheel itself cannot be used.";
        _timer.Start();
    }

    private void Poll()
    {
        MapperSnapshot? now = _latest();
        if (now is null || _baseline is null || _device is null) { Stop("Lost contact with the wheel."); return; }
        DetectedInput? hit = InputDetector.Detect(_baseline, now.Raw, _device);
        if (hit is not null)
        {
            if (hit.Name is null) { Stop(hit.Description); return; }
            if (string.Equals(hit.Name, "wheel", StringComparison.OrdinalIgnoreCase))
            {
                _baseline = now.Raw;                                  // keep listening: that was the steering, not a control
                _hint.Text = "Turning the wheel cannot be used. Press a button or a pedal instead.";
                return;
            }
            _timer.Stop();
            _control = hit.Name;
            _status.Text = hit.Description;
            _status.SetResourceReference(TextBlock.ForegroundProperty, "Success");
            _hint.Text = "Press Listen again to pick a different one.";
            UpdateOk();
            return;
        }
        if (DateTime.UtcNow > _until) Stop("Nothing was pressed. Press Listen again and try again.");
    }

    private void Stop(string message)
    {
        _timer.Stop();
        _status.Text = message;
        _status.SetResourceReference(TextBlock.ForegroundProperty, "Warn");
    }

    private void UpdateOk() => _ok.IsEnabled = _control is not null && (_action.SelectedIndex != 2 || _name.Text.Trim().Length > 0);
}

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace G923Mapper.App;

/// <summary>Walks through the virtual controller's controls, asking which wheel control to use for each, and builds a profile from the answers.</summary>
public partial class RemapAllWindow : Window
{
    private enum Phase { Waiting, Released, Naming }

    private readonly AppServices _s;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly ObservableCollection<string> _chosen = new();
    private readonly PadKind _kind;
    private RemapBuilder? _builder;
    private DeviceMap? _device;
    private RawInput? _baseline, _releaseBaseline;
    private int _index;
    private Phase _phase = Phase.Waiting;
    private DateTime? _quietSince;
    private readonly System.Collections.Generic.List<string> _doneTargets = new();   // targets that were given a control, for the green rings

    /// <summary>The profile that was built, once the user pressed Create profile.</summary>
    public Profile? Result { get; private set; }

    public RemapAllWindow(AppServices services, PadKind kind)
    {
        InitializeComponent();
        _s = services;
        _kind = kind;
        ChosenList.ItemsSource = _chosen;
        _timer.Tick += (_, _) => Tick();
        Closed += (_, _) => _timer.Stop();
        Loaded += (_, _) => Begin();
    }

    private void Begin()
    {
        var snap = _s.Service.Latest;
        if (snap is null)
        {
            TxtStep.Text = "NOT READY";
            TxtPrompt.Text = "The mapper is not running, so there is no wheel to listen to. Close this, start the mapper from the main window, and try again.";
            SkipButton.IsEnabled = FinishButton.IsEnabled = false;
            return;
        }
        _device = snap.Device;
        _builder = new RemapBuilder(_device);
        ShowStep();
        _timer.Start();
    }

    private RemapStep Current => RemapSteps.All[_index];

    private void ShowStep()
    {
        _phase = Phase.Waiting;
        _baseline = _s.Service.Latest?.Raw;
        TxtStep.Text = $"CONTROL {_index + 1} OF {RemapSteps.All.Count}";
        TxtPrompt.Text = Current.Kind switch
        {
            RemapKind.Steering => $"Turn the wheel a little: this is the {Current.Label(_kind)}.",
            RemapKind.Trigger => $"Press the pedal you want for the {Current.Label(_kind)}.",
            _ => $"Press the button or pedal you want for the {Current.Label(_kind)}.",
        };
        TxtHint.Text = Current.Hint;
        TxtGot.Text = "";
        Progress.Value = (double)_index / RemapSteps.All.Count;
        PadView.Set(null, _kind);
        PadView.Point(Current.Target, _doneTargets);
    }

    private void Tick()
    {
        var snap = _s.Service.Latest;
        if (snap is null || _device is null || _builder is null) return;

        if (_baseline is null || !_baseline.Live)
        {
            _baseline = snap.Raw;
            if (!snap.Raw.Live) TxtGot.Text = "The wheel is not reporting yet. Move it a little.";
            return;
        }

        if (_phase == Phase.Waiting)
        {
            if (InputDetector.Detect(_baseline, snap.Raw, _device) is { Name: not null } hit)
            {
                _builder.Assign(Current, hit.Name);
                _doneTargets.Add(Current.Target);
                PadView.Point(Current.Target, _doneTargets);
                _chosen.Add($"{Current.Label(_kind),-26} <- {hit.Name}");
                ChosenList.ScrollIntoView(_chosen[^1]);
                TxtGot.Text = $"Got {hit.Description}. Let go of it…";
                _releaseBaseline = _baseline;
                _quietSince = null;
                _phase = Phase.Released;
            }
        }
        else if (_phase == Phase.Released)
        {
            // Move on only once everything has gone back to rest, so one press is never read as two.
            bool still = _releaseBaseline is not null && InputDetector.Detect(_releaseBaseline, snap.Raw, _device) is not null;
            if (still) { _quietSince = null; return; }
            _quietSince ??= DateTime.UtcNow;
            if ((DateTime.UtcNow - _quietSince.Value).TotalMilliseconds >= 400) Advance();
        }
    }

    private void Advance()
    {
        _index++;
        if (_index >= RemapSteps.All.Count) { EnterNaming(); return; }
        ShowStep();
    }

    private void EnterNaming()
    {
        _timer.Stop();
        _phase = Phase.Naming;
        int n = _builder?.Assigned ?? 0;
        TxtStep.Text = "DONE";
        TxtHint.Text = "";
        PadView.Point(null, _doneTargets);
        TxtPrompt.Text = n == 0 ? "Nothing was chosen, so there is nothing to make." : $"{n} control{(n == 1 ? "" : "s")} chosen. Name the new profile and create it.";
        TxtGot.Text = "";
        Progress.Value = 1;
        NamePanel.Visibility = n == 0 ? Visibility.Collapsed : Visibility.Visible;
        CreateButton.Visibility = n == 0 ? Visibility.Collapsed : Visibility.Visible;
        SkipButton.Visibility = FinishButton.Visibility = Visibility.Collapsed;
        if (n > 0) { NameBox.Focus(); NameBox.SelectAll(); }
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        if (_phase == Phase.Naming || _builder is null) return;
        _builder.Assign(Current, null);
        _chosen.Add($"{Current.Label(_kind),-26} (skipped)");
        Advance();
    }

    private void Finish_Click(object sender, RoutedEventArgs e) => EnterNaming();

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        if (name.Length == 0 || _builder is null) { NameBox.Focus(); return; }
        Result = _builder.Build(name);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

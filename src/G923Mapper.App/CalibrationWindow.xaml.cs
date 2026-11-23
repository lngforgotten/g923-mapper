using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace G923Mapper.App;

public partial class CalibrationWindow : Window
{
    private enum Mode { Starting, Waiting, Step, Recording, Question, Result, Failed }

    private readonly DeviceRegistry _devices;
    private readonly string _devicesPath;
    private readonly IntPtr _window;
    private readonly CancellationTokenSource _cts = new();

    private TaskCompletionSource<bool>? _stepTcs;
    private TaskCompletionSource<string>? _askTcs;
    private CalibrationResult? _result;
    private Mode _mode = Mode.Starting;
    private int _accept;   // set by "Use this position", consumed by the wizard thread

    /// <summary>The map that was saved to devices.json, if the user chose Save.</summary>
    public DeviceMap? SavedMap { get; private set; }

    public CalibrationWindow(DeviceRegistry devices, string devicesPath, IntPtr window)
    {
        InitializeComponent();
        _devices = devices;
        _devicesPath = devicesPath;
        _window = window;
        Loaded += (_, _) => Task.Run(RunWizard);
        Closed += (_, _) => _cts.Cancel();
        SetMode(Mode.Starting);
    }

    // ---------------------------------------------------------------- wizard thread

    private void RunWizard()
    {
        try
        {
            var io = new GuiWizardIO(this, _cts.Token);
            var outcome = CalibrationRunner.Run(_devices, _window, io);
            Dispatcher.Invoke(() =>
            {
                if (outcome.Result is null) ShowFailure(outcome.Error ?? "Calibration did not finish.");
                else ShowResult(outcome.Result);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            try { Dispatcher.Invoke(() => ShowFailure(ex.Message)); } catch (TaskCanceledException) { }
        }
    }

    /// <summary>Bridges the wizard's blocking calls to the window.</summary>
    private sealed class GuiWizardIO(CalibrationWindow w, CancellationToken ct) : IWizardIO
    {
        public void Say(string text)
        {
            ct.ThrowIfCancellationRequested();
            // The first wizard step has no button: it completes when the wheel moves.
            if (text.StartsWith("Step ", StringComparison.Ordinal)) w.Dispatcher.Invoke(() => w.ShowWaiting(text));
            else w.Dispatcher.Invoke(() => w.AddNote(text));
        }

        public bool Step(string prompt, bool allowSkip)
        {
            ct.ThrowIfCancellationRequested();
            var tcs = new TaskCompletionSource<bool>();
            w.Dispatcher.Invoke(() => w.ShowStep(prompt, allowSkip, tcs));
            tcs.Task.Wait(ct);
            if (tcs.Task.Result) w.Dispatcher.Invoke(w.ShowRecording);
            return tcs.Task.Result;
        }

        public string Ask(string prompt, string defaultValue)
        {
            ct.ThrowIfCancellationRequested();
            var tcs = new TaskCompletionSource<string>();
            w.Dispatcher.Invoke(() => w.ShowQuestion(prompt, defaultValue, tcs));
            tcs.Task.Wait(ct);
            return tcs.Task.Result;
        }

        public void Pause(int milliseconds)
        {
            if (ct.WaitHandle.WaitOne(milliseconds)) ct.ThrowIfCancellationRequested();
        }

        public void Progress(WizardProgress progress)
        {
            ct.ThrowIfCancellationRequested();
            w.Dispatcher.BeginInvoke(() => w.ShowProgress(progress));
        }

        public bool AcceptNow() => Interlocked.Exchange(ref w._accept, 0) == 1;
    }

    // ---------------------------------------------------------------- UI states (UI thread)

    private void SetMode(Mode mode)
    {
        _mode = mode;
        Progress.Visibility = mode is Mode.Starting or Mode.Waiting or Mode.Recording ? Visibility.Visible : Visibility.Collapsed;
        LivePanel.Visibility = Visibility.Collapsed;   // shown again by the first live reading of a recording
        UseButton.Visibility = Visibility.Collapsed;
        UseButton.IsEnabled = false;
        AskPanel.Visibility = mode == Mode.Question ? Visibility.Visible : Visibility.Collapsed;
        ReportList.Visibility = mode == Mode.Result ? Visibility.Visible : Visibility.Collapsed;
        SkipButton.Visibility = Visibility.Collapsed;
        DiscardButton.Visibility = mode == Mode.Result ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Content = mode is Mode.Result or Mode.Failed ? "Close" : "Cancel";
        CancelButton.Visibility = mode == Mode.Result ? Visibility.Collapsed : Visibility.Visible;
        PrimaryButton.IsEnabled = mode is Mode.Step or Mode.Question or Mode.Result;
        PrimaryButton.Visibility = mode == Mode.Failed ? Visibility.Collapsed : Visibility.Visible;
        PrimaryButton.Content = mode switch { Mode.Question => "Continue", Mode.Result => "Save", _ => "Record" };
        TxtPhase.Text = mode switch
        {
            Mode.Starting => "STARTING",
            Mode.Waiting => "WAITING FOR THE WHEEL",
            Mode.Step => "GET INTO POSITION, THEN RECORD",
            Mode.Recording => "RECORDING, KEEP HOLDING",
            Mode.Question => "QUESTION",
            Mode.Result => "RESULT",
            _ => "PROBLEM",
        };
    }

    private void AddNote(string text)
    {
        Notes.Items.Add(text);
        Notes.ScrollIntoView(Notes.Items[^1]);
    }

    private void ShowWaiting(string text)
    {
        TxtPrompt.Text = text;
        SetMode(Mode.Waiting);
    }

    private void ShowStep(string prompt, bool allowSkip, TaskCompletionSource<bool> tcs)
    {
        _stepTcs = tcs;
        TxtPrompt.Text = prompt;
        SetMode(Mode.Step);
        SkipButton.Visibility = allowSkip ? Visibility.Visible : Visibility.Collapsed;
        PrimaryButton.Focus();
    }

    private void ShowRecording()
    {
        Interlocked.Exchange(ref _accept, 0);
        SetMode(Mode.Recording);
    }

    /// <summary>Live reading while a stop or rest is being measured.</summary>
    private void ShowProgress(WizardProgress p)
    {
        if (_mode != Mode.Recording) return;   // a reading that arrived after the step ended
        LivePanel.Visibility = Visibility.Visible;
        Progress.Visibility = Visibility.Collapsed;
        bool rest = p.Label == "Resting";

        // -1..1 with +/-1 at the very end of the 0..65535 axis, so the bar shows how close to the end it is.
        double room = p.Value < p.Rest ? p.Rest : 65535 - p.Rest;
        LiveBar.Value = room <= 0 ? 0 : Math.Clamp((p.Value - p.Rest) / room, -1, 1);
        TxtLiveValue.Text = p.Value.ToString();
        HoldBar.Value = p.Hold;
        HoldBar.Foreground = (System.Windows.Media.Brush)FindResource(p.Locked ? "Success" : "Accent");
        TxtHoldPct.Text = p.Locked ? "locked" : $"{p.Hold:P0}";
        TxtLive.Text = p.Message;
        TxtPhase.Text = rest ? "RESTING, KEEP STILL" : p.Locked ? "LOCKED IN" : "HOLD IT AT THE STOP";

        UseButton.Visibility = rest ? Visibility.Collapsed : Visibility.Visible;
        UseButton.IsEnabled = !rest && !p.Locked && Math.Abs(p.Value - p.Rest) >= CalibrationAnalyzer.MinChange;
    }

    private void UseButton_Click(object sender, RoutedEventArgs e)
    {
        Interlocked.Exchange(ref _accept, 1);
        UseButton.IsEnabled = false;
    }

    private void ShowQuestion(string prompt, string defaultValue, TaskCompletionSource<string> tcs)
    {
        _askTcs = tcs;
        TxtPrompt.Text = prompt;
        AskBox.Text = defaultValue;
        SetMode(Mode.Question);
        AskBox.Focus();
        AskBox.SelectAll();
    }

    private void ShowResult(CalibrationResult result)
    {
        _result = result;
        TxtPrompt.Text = $"Done. Save this as \"{result.Map.Name}\"?";
        ReportList.ItemsSource = result.Report;
        SetMode(Mode.Result);
    }

    private void ShowFailure(string message)
    {
        TxtPrompt.Text = message;
        SetMode(Mode.Failed);
    }

    // ---------------------------------------------------------------- buttons

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        switch (_mode)
        {
            case Mode.Step: _stepTcs?.TrySetResult(true); break;
            case Mode.Question: _askTcs?.TrySetResult(AskBox.Text); break;
            case Mode.Result: Save(); break;
        }
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mode != Mode.Step) return;
        AddNote($"Skipped: {TxtPrompt.Text}");
        _stepTcs?.TrySetResult(false);
        SetMode(Mode.Waiting);
        TxtPhase.Text = "WORKING";
    }

    private void AskBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _mode == Mode.Question) _askTcs?.TrySetResult(AskBox.Text);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        DialogResult = false;
    }

    private void DiscardButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Save()
    {
        if (_result is null) return;
        try
        {
            DeviceRegistry.SaveEntry(_devicesPath, _result.Map);
            SavedMap = _result.Map;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not save {_devicesPath}:\n{ex.Message}", "Calibration", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace G923Mapper.App;

/// <summary>Asks the user to hold some wheel buttons together and reports which ones, for a button combination.</summary>
public partial class ComboCaptureWindow : Window
{
    private readonly AppServices _s;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private ComboCapture _capture = new();
    private DateTime _started = DateTime.UtcNow;

    /// <summary>The buttons chosen, once the user pressed Use this.</summary>
    public IReadOnlyList<string> Inputs { get; private set; } = [];

    public ComboCaptureWindow(AppServices services, string what)
    {
        InitializeComponent();
        _s = services;
        TxtTitle.Text = $"Set the combination for: {what}";
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Closed += (_, _) => _timer.Stop();

        if (!_s.Service.IsRunning || _s.Service.Latest is null)
            TxtResult.Text = "The mapper is not running or the wheel is not connected, so nothing can be read. Start it and move the wheel first.";
    }

    private void Tick()
    {
        if (_capture.Finished) return;
        var snap = _s.Service.Latest;
        if (snap is null) return;

        var down = ButtonNames.Down(snap.Input, snap.Device);
        _capture.Add(down);
        TxtHeld.Text = down.Count == 0 ? "nothing" : string.Join(" + ", down);

        if (_capture.Finished) Finish();
        else if ((DateTime.UtcNow - _started).TotalSeconds > 30)
        {
            _timer.Stop();
            TxtResult.Text = "Nothing was pressed. Try again.";
            RetryButton.Visibility = Visibility.Visible;
        }
    }

    private void Finish()
    {
        _timer.Stop();
        var best = _capture.Best;
        TxtResult.Text = best.Count == 1
            ? $"Captured: {ComboText.Format(best)}. A single button can be pressed by accident while driving; two or more together is safer."
            : $"Captured: {ComboText.Format(best)}.";
        UseButton.IsEnabled = best.Count > 0;
        RetryButton.Visibility = Visibility.Visible;
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        _capture = new ComboCapture();
        _started = DateTime.UtcNow;
        TxtResult.Text = "";
        TxtHeld.Text = "nothing";
        UseButton.IsEnabled = false;
        RetryButton.Visibility = Visibility.Collapsed;
        _timer.Start();
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        Inputs = _capture.Best;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

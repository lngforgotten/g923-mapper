using System;
using System.Windows;
using System.Windows.Threading;

namespace G923Mapper.App;

/// <summary>
/// A small always-on-top window: the controller as the game sees it, the steering and the three pedals. For keeping an eye on the wheel
/// while a game is in front (borderless windowed games let it sit on top), without opening the whole app.
/// </summary>
public partial class MiniWindow : Window
{
    private readonly AppServices _s;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };

    public MiniWindow(AppServices services)
    {
        InitializeComponent();
        _s = services;
        // Top right of the main screen, out of the way.
        Left = SystemParameters.WorkArea.Right - Width - 24;
        Top = SystemParameters.WorkArea.Top + 24;
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private void OnTop_Click(object sender, RoutedEventArgs e) => Topmost = OnTopBox.IsChecked == true;

    private void Tick()
    {
        MapperSnapshot? snap = _s.Service.Latest;
        if (snap is null)
        {
            Dot.Fill = ThemeManager.Neutral;
            TxtProfile.Text = _s.Service.IsRunning ? "Waiting for the wheel" : "Stopped";
            Pad.Set(null, PadKind.Xbox360);
            BarSteer.Value = BarThrottle.Value = BarBrake.Value = BarClutch.Value = 0;
            TxtSteer.Text = "0°";
            return;
        }

        Dot.Fill = _s.Service.State == MapperState.Running ? ThemeManager.Success : ThemeManager.Warn;
        TxtProfile.Text = snap.ProfileName;
        PadKind kind = _s.Store.Get(snap.ProfileName) is { } p ? PadKinds.Of(p) : PadKind.Xbox360;
        Pad.Set(snap.Pad, kind);

        double steer = snap.Input.Get("wheel");
        BarSteer.Value = steer;
        TxtSteer.Text = $"{steer * snap.Device.RotationDegrees / 2:+0;-0;0}°";
        BarThrottle.Value = snap.Input.Get("throttle");
        BarBrake.Value = snap.Input.Get("brake");
        BarClutch.Value = snap.Input.Get("clutch");
    }
}

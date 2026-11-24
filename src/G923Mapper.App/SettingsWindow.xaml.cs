using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace G923Mapper.App;

public partial class SettingsWindow : Window
{
    private readonly AppServices _s;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _loading = true;

    public SettingsWindow(AppServices services)
    {
        InitializeComponent();
        _s = services;

        ThemeAuto.IsChecked = ThemeManager.Setting == ThemeSetting.System;
        ThemeLight.IsChecked = ThemeManager.Setting == ThemeSetting.Light;
        ThemeDark.IsChecked = ThemeManager.Setting == ThemeSetting.Dark;
        ThemeContrast.IsChecked = ThemeManager.Setting == ThemeSetting.HighContrast;

        StartupBox.IsChecked = StartupRegistration.IsEnabled();
        UnhideBox.IsChecked = _s.Settings.UnhideWheelOnExit;
        AutoCentreBox.IsChecked = _s.Settings.AutoCentre;
        ReleaseBox.IsChecked = _s.Settings.ReleaseWhenGameNotInFront;
        ModeHotkeyBox.IsChecked = _s.Settings.ModeHotkey.Enabled;
        ModeHotkeyCombo.ItemsSource = Hotkey.Presets;
        ModeHotkeyCombo.Text = _s.Settings.ModeHotkey.Combo;
        ModeHotkeyMessage.Text = !_s.Settings.ModeHotkey.Enabled ? "" : _s.ModeHotkeyProblem.Length > 0 ? _s.ModeHotkeyProblem : $"Active: {_s.Settings.ModeHotkey.Combo}";
        ModeBuzzBox.IsChecked = _s.Settings.ModeBuzz;
        ModeToastBox.IsChecked = _s.Settings.ModeToast;
        HotkeyBox.IsChecked = _s.Settings.Hotkey.Enabled;
        CombosBox.IsChecked = _s.Settings.Combos.Enabled;
        ShowCombos();
        HotkeyCombo.ItemsSource = Hotkey.Presets;
        HotkeyCombo.Text = _s.Settings.Hotkey.Combo;
        HotkeyMessage.Text = !_s.Settings.Hotkey.Enabled ? "" : _s.HotkeyProblem.Length > 0 ? _s.HotkeyProblem : $"Active: {_s.Settings.Hotkey.Combo}";

        // This setting lives in the first copy's file, so it is only offered there.
        MultiBox.IsChecked = _s.Settings.AllowMultipleInstances;
        if (_s.Instance > 1) MultiBox.Visibility = MultiHint.Visibility = Visibility.Collapsed;
        string exe = Environment.ProcessPath ?? "";
        StartupHint.Text = exe.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) || exe.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase)
            ? "This copy is running from a build folder. Install the app first (see the README) so Windows starts it from a permanent location."
            : "";
        StartupHint.Visibility = StartupHint.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        ForceSettings f = _s.Settings.Force;
        FfbBox.IsChecked = f.Enabled;
        RumbleSlider.Value = f.RumblePercent;
        SpringSlider.Value = f.SpringPercent;
        FrictionSlider.Value = f.FrictionPercent;
        StyleBox.ItemsSource = new[] { "Smooth", "Sharp", "Rough" };
        StyleBox.SelectedIndex = ForceMath.NormaliseStyle(f.Style) == ForceMath.StyleSharp ? 1 : ForceMath.NormaliseStyle(f.Style) == ForceMath.StyleRough ? 2 : 0;
        MotorsBox.ItemsSource = new[] { "Both (heavy and light)", "Heavy only", "Light only" };
        MotorsBox.SelectedIndex = f.Motors == ForceMath.MotorsHeavy ? 1 : f.Motors == ForceMath.MotorsLight ? 2 : 0;
        CentringStyleBox.ItemsSource = new[] { "Straight spring", "Curve (like a car)" };
        CentringPresetBox.ItemsSource = new[] { "Custom" }.Concat(CentringCurve.Presets.Select(p => p.Name)).ToList();
        ShowCurveSettings(f);
        UpdateForceTexts();

        UpdateRepoBox.Text = _s.Settings.UpdateRepo;
        AutoUpdateBox.IsChecked = _s.Settings.AutoUpdateCheck;
        AutoInstallBox.IsChecked = _s.Settings.AutoInstallUpdates;
        AboutText.Text = $"G923 Mapper {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? ""}  ·  {RuntimeInformation.FrameworkDescription}";

        _loading = false;
        RefreshAll();

        _s.Service.FloorMeasured += OnFloorMeasured;
        _statusTimer.Tick += (_, _) => RefreshLive();
        _statusTimer.Start();
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); _s.Settings.Save(); };
        Closed += (_, _) =>
        {
            _statusTimer.Stop();
            _s.Service.FloorMeasured -= OnFloorMeasured;
            if (_saveTimer.IsEnabled) { _saveTimer.Stop(); _s.Settings.Save(); }
        };
    }

    private void RefreshAll()
    {
        RefreshLive();
        RefreshHide();
        RefreshExtras();
        RefreshPads();
    }

    /// <summary>Things that change while the window is open.</summary>
    private void RefreshLive()
    {
        var svc = _s.Service;
        WheelText.Text = svc.State == MapperState.Running
            ? $"{svc.WheelName}  ({svc.WheelIds})"
            : "No wheel connected right now. Start the mapper and plug the wheel in.";

        ForceSettings f = _s.Settings.Force;
        FfbStatus.Text = !f.Enabled ? "Off"
            : svc.ForceActive ? "Working"
            : svc.State != MapperState.Running ? "Starts with the mapper"
            : svc.ForceMessage.Length > 0 ? svc.ForceMessage
            : "Not active";
        TestForceButton.IsEnabled = svc.ForceActive;
        RefreshCurve();
    }

    // ---------------------------------------------------------------- appearance and general

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        ThemeSetting t = ThemeLight.IsChecked == true ? ThemeSetting.Light : ThemeDark.IsChecked == true ? ThemeSetting.Dark : ThemeContrast.IsChecked == true ? ThemeSetting.HighContrast : ThemeSetting.System;
        ThemeManager.Apply(t);
        _s.Settings.Theme = t.ToString();
        _s.Settings.Save();
    }

    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try { StartupRegistration.Set(StartupBox.IsChecked == true, Environment.ProcessPath ?? ""); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            MessageBox.Show(this, "Windows would not let this setting be changed: " + ex.Message, "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            _loading = true; StartupBox.IsChecked = StartupRegistration.IsEnabled(); _loading = false;
        }
    }

    private void Multi_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.Settings.AllowMultipleInstances = MultiBox.IsChecked == true;
        _s.Settings.Save();
    }

    // ---------------------------------------------------------------- wheel button combinations

    private System.Collections.Generic.List<string> ComboList(string which) => which switch
    {
        "next" => _s.Settings.Combos.NextProfile,
        "previous" => _s.Settings.Combos.PreviousProfile,
        _ => _s.Settings.Combos.Pause,
    };

    private static string ComboName(string which) => which switch
    {
        "next" => "next profile",
        "previous" => "previous profile",
        _ => "pause or resume",
    };

    private void ShowCombos()
    {
        ComboPauseText.Text = ComboText.Format(_s.Settings.Combos.Pause);
        ComboNextText.Text = ComboText.Format(_s.Settings.Combos.NextProfile);
        ComboPrevText.Text = ComboText.Format(_s.Settings.Combos.PreviousProfile);
    }

    private void Combos_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.Settings.Combos.Enabled = CombosBox.IsChecked == true;
        _s.Settings.Save();
        _s.ApplyInputOptions?.Invoke();
    }

    private void ComboSet_Click(object sender, RoutedEventArgs e)
    {
        string which = (sender as FrameworkElement)?.Tag as string ?? "pause";
        var dlg = new ComboCaptureWindow(_s, ComboName(which)) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Inputs.Count == 0) return;

        // The same buttons for two different jobs would do both at once: take them off the other jobs.
        foreach (string other in new[] { "pause", "next", "previous" }.Where(o => o != which))
            if (ComboList(other).Count > 0 && ComboList(other).OrderBy(x => x).SequenceEqual(dlg.Inputs.OrderBy(x => x), StringComparer.OrdinalIgnoreCase))
                ComboList(other).Clear();

        var list = ComboList(which);
        list.Clear();
        list.AddRange(dlg.Inputs);
        _s.Settings.Save();
        ShowCombos();
        _s.ApplyInputOptions?.Invoke();
    }

    private void ComboClear_Click(object sender, RoutedEventArgs e)
    {
        string which = (sender as FrameworkElement)?.Tag as string ?? "pause";
        ComboList(which).Clear();
        _s.Settings.Save();
        ShowCombos();
        _s.ApplyInputOptions?.Invoke();
    }

    private void Release_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.Settings.ReleaseWhenGameNotInFront = ReleaseBox.IsChecked == true;
        _s.Settings.Save();
        _s.ApplyInputOptions?.Invoke();
    }

    private void Hotkey_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.Settings.Hotkey.Enabled = HotkeyBox.IsChecked == true;
        ApplyHotkeyText();
    }

    private void HotkeyCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || HotkeyCombo.SelectedItem is not string) return;
        ApplyHotkeyText(HotkeyCombo.SelectedItem as string);
    }

    private void HotkeyCombo_LostFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (!_loading) ApplyHotkeyText();
    }

    private void ApplyHotkeyText(string? chosen = null)
    {
        string text = (chosen ?? HotkeyCombo.Text).Trim();
        if (Hotkey.TryParse(text, out Hotkey? key, out string? why))
            _s.Settings.Hotkey.Combo = key!.ToString();
        else if (_s.Settings.Hotkey.Enabled)
        {
            HotkeyMessage.Text = "That shortcut cannot be used: " + why;
            return;
        }
        _s.Settings.Save();
        _s.ApplyInputOptions?.Invoke();
        HotkeyMessage.Text = !_s.Settings.Hotkey.Enabled ? "" : _s.HotkeyProblem.Length > 0 ? _s.HotkeyProblem : $"Active: {_s.Settings.Hotkey.Combo}";
    }

    private void ModeHotkey_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.Settings.ModeHotkey.Enabled = ModeHotkeyBox.IsChecked == true;
        ApplyModeHotkeyText();
    }

    private void ModeHotkeyCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ModeHotkeyCombo.SelectedItem is not string) return;
        ApplyModeHotkeyText(ModeHotkeyCombo.SelectedItem as string);
    }

    private void ModeHotkeyCombo_LostFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (!_loading) ApplyModeHotkeyText();
    }

    private void ApplyModeHotkeyText(string? chosen = null)
    {
        string text = (chosen ?? ModeHotkeyCombo.Text).Trim();
        if (Hotkey.TryParse(text, out Hotkey? key, out string? why))
            _s.Settings.ModeHotkey.Combo = key!.ToString();
        else if (_s.Settings.ModeHotkey.Enabled)
        {
            ModeHotkeyMessage.Text = "That shortcut cannot be used: " + why;
            return;
        }
        _s.Settings.Save();
        _s.ApplyInputOptions?.Invoke();
        ModeHotkeyMessage.Text = !_s.Settings.ModeHotkey.Enabled ? "" : _s.ModeHotkeyProblem.Length > 0 ? _s.ModeHotkeyProblem : $"Active: {_s.Settings.ModeHotkey.Combo}";
    }

    private void ModeOptions_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.Settings.ModeBuzz = ModeBuzzBox.IsChecked == true;
        _s.Settings.ModeToast = ModeToastBox.IsChecked == true;
        _s.Settings.Save();
        _s.ApplyInputOptions?.Invoke();
    }

    // ---------------------------------------------------------------- update check (only when asked)

    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private string _updateUrl = "";

    private void AutoUpdate_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.Settings.AutoUpdateCheck = AutoUpdateBox.IsChecked == true;
        _s.Settings.Save();
    }

    private void AutoInstall_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.Settings.AutoInstallUpdates = AutoInstallBox.IsChecked == true;
        _s.Settings.Save();
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        UpdateInstallButton.IsEnabled = false;
        UpdateMessage.Text = "Downloading the new version...";
        if (_s.InstallUpdateNow is { } install) await install();
        UpdateInstallButton.IsEnabled = true;
        UpdateMessage.Text = _s.UpdatePhase == UpdatePhase.Failed ? "It could not be installed: " + _s.UpdateProblem : "";
    }

    private void UpdateRepo_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _s.Settings.UpdateRepo = UpdateRepoBox.Text.Trim();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        UpdateOpenButton.Visibility = Visibility.Collapsed;
        UpdateButton.IsEnabled = false;
        UpdateMessage.Text = "Asking GitHub...";
        try
        {
            var (info, problem) = await UpdateChecker.CheckAsync(UpdateRepoBox.Text);
            if (info is null) { UpdateMessage.Text = problem ?? ""; return; }
            UpdateMessage.Text = info.Message;
            _updateUrl = info.Url;
            UpdateOpenButton.Visibility = UpdateInstallButton.Visibility = info.Newer ? Visibility.Visible : Visibility.Collapsed;
            AppLog.Info("Update check: " + info.Message);
            if (info.Newer)
            {
                _s.AvailableUpdate = info;                                    // Home shows it too, and Install now works from either place
                if (_s.UpdatePhase is UpdatePhase.None or UpdatePhase.Failed) _s.UpdatePhase = UpdatePhase.Available;
                _s.UpdateChanged?.Invoke();
            }
        }
        finally { UpdateButton.IsEnabled = true; }
    }

    private void OpenUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_updateUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            try { Process.Start(new ProcessStartInfo(_updateUrl) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { }
    }

    private void AutoCentre_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.Settings.AutoCentre = AutoCentreBox.IsChecked == true;
        _s.Settings.Save();
        _s.ApplyInputOptions?.Invoke();
    }

    private void Unhide_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.Settings.UnhideWheelOnExit = UnhideBox.IsChecked == true;
        _s.Settings.Save();
    }

    private void AntiCheat_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AntiCheatList.Load(AppSettings.AntiCheatPath);   // creates the file with the defaults if it is not there yet
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{AppSettings.AntiCheatPath}\"") { UseShellExecute = true });
            MessageBox.Show(this, "Add one program name per line (for example GTA5.exe), save, then restart G923 Mapper. " +
                                  "Profiles for these games can only use the plain virtual controller: keyboard output is switched off for them.",
                "Anti-cheat games", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { AppLog.Warn($"Could not open the anti-cheat list: {ex.Message}"); }
    }

    private void OpenProfiles_Click(object sender, RoutedEventArgs e) => OpenFolder(_s.ProfileDir);
    private void OpenLogs_Click(object sender, RoutedEventArgs e) { if (AppLog.Directory is string d) OpenFolder(d); }

    private static void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { AppLog.Warn($"Could not open {path}: {ex.Message}"); }
    }

    // ---------------------------------------------------------------- wheel

    private void Calibrate_Click(object sender, RoutedEventArgs e)
    {
        _s.RunCalibration(this);
    }

    private void NameButtons_Click(object sender, RoutedEventArgs e)
    {
        DeviceMap? device = _s.Service.Latest?.Device;
        if (device is null) _s.Devices.TryGet("g923_xbox", out device);
        if (device is null) return;

        var dlg = new ButtonMapWindow(device, () => _s.Service.Latest?.Raw, AutoProfileSwitcher.FromStore(_s.Store), _s.DevicesPath) { Owner = this };
        if (dlg.ShowDialog() == true) _s.Store.ReloadAll();
    }

    // ---------------------------------------------------------------- force feedback

    private void UpdateForceTexts()
    {
        RumbleText.Text = $"{(int)RumbleSlider.Value}%";
        SpringText.Text = $"{(int)SpringSlider.Value}%";
        FrictionText.Text = $"{(int)FrictionSlider.Value}%";
        CentreText.Text = $"{(int)CentreSlider.Value}°";
        PeakText.Text = $"{(int)PeakSlider.Value}°";
        LightenText.Text = $"{(int)LightenSlider.Value}%";
        DampText.Text = $"{(int)DampSlider.Value}%";
        FloorText.Text = $"{(int)FloorSlider.Value}%";
    }

    private void Ffb_Changed(object sender, RoutedEventArgs e) => PushForce();
    private void Motors_Changed(object sender, SelectionChangedEventArgs e) => PushForce();
    private void Style_Changed(object sender, SelectionChangedEventArgs e) => PushForce();
    private void Force_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        UpdateForceTexts();
        PushForce();
    }

    /// <summary>Apply to the running mapper at once; the file is written shortly after the last change.</summary>
    private void PushForce()
    {
        if (_loading) return;
        var f = new ForceSettings
        {
            CentringReversed = _s.Settings.Force.CentringReversed,
            CentringWheel = _s.Settings.Force.CentringWheel,
            CentringStyle = CentringStyleBox.SelectedIndex == 1 ? CentringCurve.StyleCurve : CentringCurve.StyleSpring,
            CentreDegrees = (int)CentreSlider.Value,
            PeakDegrees = (int)PeakSlider.Value,
            LighteningPercent = (int)LightenSlider.Value,
            DampingPercent = (int)DampSlider.Value,
            FloorPercent = (int)FloorSlider.Value,
            Enabled = FfbBox.IsChecked == true,
            RumblePercent = (int)RumbleSlider.Value,
            SpringPercent = (int)SpringSlider.Value,
            FrictionPercent = (int)FrictionSlider.Value,
            Style = StyleBox.SelectedIndex switch { 1 => ForceMath.StyleSharp, 2 => ForceMath.StyleRough, _ => ForceMath.StyleSmooth },
            Motors = MotorsBox.SelectedIndex switch { 1 => ForceMath.MotorsHeavy, 2 => ForceMath.MotorsLight, _ => ForceMath.MotorsBoth },
        };
        _s.Settings.Force = f;
        _s.Service.Force = f.Clone();
        _saveTimer.Stop();
        _saveTimer.Start();
        RefreshLive();
    }

    private void TestForce_Click(object sender, RoutedEventArgs e) => _s.Service.TestForce();

    // ---------------------------------------------------------------- the centring curve

    /// <summary>Put the curve settings into the controls without that counting as the user changing them.</summary>
    private void ShowCurveSettings(ForceSettings f)
    {
        bool was = _loading;
        _loading = true;
        CentringStyleBox.SelectedIndex = CentringCurve.NormaliseStyle(f.CentringStyle) == CentringCurve.StyleCurve ? 1 : 0;
        CentreSlider.Value = Math.Clamp(f.CentreDegrees, CentreSlider.Minimum, CentreSlider.Maximum);
        PeakSlider.Value = Math.Clamp(f.PeakDegrees, PeakSlider.Minimum, PeakSlider.Maximum);
        LightenSlider.Value = Math.Clamp(f.LighteningPercent, 0, 100);
        DampSlider.Value = Math.Clamp(f.DampingPercent, 0, 100);
        FloorSlider.Value = Math.Clamp(f.FloorPercent, 0, FloorSlider.Maximum);
        SpringSlider.Value = Math.Clamp(f.SpringPercent, 0, 100);
        CentringPresetBox.SelectedIndex = CentringCurve.Presets.ToList().FindIndex(p => p.Matches(f)) + 1;   // 0 = Custom
        CurvePanel.Visibility = CentringStyleBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        _loading = was;
    }

    private void CentringStyle_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        CurvePanel.Visibility = CentringStyleBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        PushForce();
    }

    private void CentringPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CentringPresetBox.SelectedIndex <= 0) return;
        var f = _s.Settings.Force.Clone();
        CentringCurve.Presets[CentringPresetBox.SelectedIndex - 1].ApplyTo(f);
        f.CentringStyle = CentringCurve.StyleCurve;
        ShowCurveSettings(f);
        UpdateForceTexts();
        PushForce();
    }

    private void Measure_Click(object sender, RoutedEventArgs e)
    {
        if (!_s.Service.ForceActive)
        {
            MeasureStatus.Text = "Force feedback is not running, so the wheel cannot be measured. Start the mapper first.";
            return;
        }
        MeasureStatus.Text = "Measuring: keep your hands off the wheel...";
        _showingResult = false;
        MeasureButton.IsEnabled = false;
        _s.Service.MeasureFloor();
    }

    /// <summary>The measurement ended (on the service thread): keep what it found and say so.</summary>
    private void OnFloorMeasured(FloorMeasurement m) => Dispatcher.BeginInvoke(() =>
    {
        MeasureButton.IsEnabled = true;
        MeasureStatus.Text = m.Message;
        _showingResult = true;
        if (!m.Ok) return;
        var f = _s.Settings.Force.Clone();
        f.FloorPercent = Math.Clamp(m.FloorPercent, 0, (int)FloorSlider.Maximum);
        f.CentringReversed = m.Reversed;
        f.CentringWheel = m.Wheel;
        _s.Settings.Force = f;
        _s.Service.Force = f.Clone();
        ShowCurveSettings(f);
        UpdateForceTexts();
        _saveTimer.Stop();
        _saveTimer.Start();
    });

    private bool _showingResult;

    /// <summary>The live part of the curve section: the graph and what it says about this wheel.</summary>
    private void RefreshCurve()
    {
        if (CurvePanel.Visibility != Visibility.Visible) return;
        var svc = _s.Service;
        ForceSettings f = _s.Settings.Force;
        double half = (svc.Latest?.Device.RotationDegrees ?? 900) / 2;
        CurveGraph.Set(f, half);

        double? angle = null;
        if (svc.Latest is { } snap && snap.Raw.Live && snap.Device.Axes.TryGetValue("wheel", out var wa) && wa.SourceIndex >= 0 && wa.Max > wa.Min)
            angle = (snap.Raw.Axes[wa.SourceIndex] - wa.Centre) * snap.Device.RotationDegrees / (wa.Max - wa.Min);
        CurveGraph.SetLive(angle);
        CurveCaption.Text = CurveGraph.Caption ?? "The graph shows the pull at each angle. Start the mapper to see where the wheel is.";

        if (svc.Measuring) { MeasureButton.IsEnabled = false; _showingResult = false; return; }
        MeasureButton.IsEnabled = svc.ForceActive && svc.CurveSupported;
        if (_showingResult) return;
        bool measured = svc.WheelIds.Length > 0 && f.CentringWheel == svc.WheelIds;
        MeasureStatus.Text = svc.State != MapperState.Running ? "Start the mapper with the wheel plugged in to measure it."
            : !svc.CurveSupported ? "This wheel does not accept the force the curve needs, so the plain spring is used."
            : measured ? "The curve is running: this wheel has been measured."
            : "The curve is not running yet. Take your hands off the wheel and press Measure wheel; until then the plain spring is used.";
    }

    // ---------------------------------------------------------------- hide the wheel

    private void RefreshHide()
    {
        if (!_s.HidHide.IsInstalled)
        {
            HideStatus.Text = "HidHide is not installed. It is optional, but it stops games from seeing your real wheel alongside the virtual controller.";
            HideButton.Content = "Get HidHide";
            HideButton.IsEnabled = true;
        }
        else if (_s.WheelHidden)
        {
            HideStatus.Text = "The wheel is hidden from games. Only this app can see it.";
            HideButton.Content = "Show wheel again";
            HideButton.IsEnabled = true;
        }
        else
        {
            HideStatus.Text = "The wheel is currently visible to games.";
            HideButton.Content = "Hide wheel";
            HideButton.IsEnabled = true;
        }
    }

    private async void Hide_Click(object sender, RoutedEventArgs e)
    {
        if (!_s.HidHide.IsInstalled)
        {
            try { Process.Start(new ProcessStartInfo(HidHideClient.DownloadUrl) { UseShellExecute = true }); } catch { }
            return;
        }
        if (_s.PrimaryIds is not { } ids)
        {
            HideMessage.Text = "Start the mapper with the wheel plugged in first.";
            return;
        }

        string app = Environment.ProcessPath ?? "";
        bool hide = !_s.WheelHidden;
        HideButton.IsEnabled = false;
        HideMessage.Text = "Waiting for permission…";
        HidHideResult result = await Task.Run(() => hide ? _s.HidHide.Hide(ids.Vid, ids.Pid, app) : _s.HidHide.Unhide(ids.Vid, ids.Pid, app));
        if (result.Success) _s.SetHiddenState(hide);
        await _s.RefreshHiddenStateAsync();
        HideMessage.Text = result.Message;
        RefreshHide();
    }

    // ---------------------------------------------------------------- virtual controllers

    private IReadOnlyList<VirtualPadInfo> _pads = [];

    private async void RefreshPads()
    {
        PadsRefreshButton.IsEnabled = false;
        var found = await Task.Run(() => new VirtualPadClient().List());
        PadsRefreshButton.IsEnabled = true;
        if (found is null)
        {
            PadsStatus.Text = "Windows would not list them (this needs an English-language Windows and the pnputil tool).";
            PadsList.Text = "";
            PadsRemoveButton.IsEnabled = false;
            return;
        }
        _pads = found;
        int connected = found.Count(p => p.Connected);
        var leftovers = VirtualPadText.Leftovers(found);
        PadsStatus.Text = $"{connected} connected, {leftovers.Count} leftover." +
                          (connected > Math.Max(1, _s.Service.IsRunning ? 1 : 0) && _s.Instance == 1 && !_s.Settings.AllowMultipleInstances
                              ? "  More than this app's one: another program is making a virtual controller too." : "");
        PadsList.Text = found.Count == 0 ? "None." : string.Join("\n", found.Select(p => $"{p.InstanceId.Split('\\').Last(),-6} {(p.Connected ? "connected" : "leftover")}"));
        PadsRemoveButton.IsEnabled = leftovers.Count > 0;
    }

    private void PadsRefresh_Click(object sender, RoutedEventArgs e) => RefreshPads();

    private async void PadsRemove_Click(object sender, RoutedEventArgs e)
    {
        var leftovers = VirtualPadText.Leftovers(_pads);
        if (leftovers.Count == 0) return;
        if (MessageBox.Show(this, $"Remove {leftovers.Count} leftover virtual controller{(leftovers.Count == 1 ? "" : "s")} from Device Manager?\n\n" +
                                  "They are not plugged in. Windows will ask for administrator permission.",
                "Remove leftovers", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;

        PadsRemoveButton.IsEnabled = false;
        PadsMessage.Text = "Waiting for permission…";
        var ids = leftovers.Select(p => p.InstanceId).ToList();
        HidHideResult result = await Task.Run(() => new VirtualPadClient().Remove(ids));
        PadsMessage.Text = result.Success ? "Done." : result.Message;
        HealthProbe.RefreshVirtualPads();
        RefreshPads();
    }

    // ---------------------------------------------------------------- extra devices

    private void RefreshExtras()
    {
        var status = _s.Service.ExtraStatus;
        var rows = _s.Devices.ExtraDevices.Select(d =>
        {
            bool? connected = status.Any(x => x.Name == d.Name) ? status.First(x => x.Name == d.Name).Connected : null;
            return $"{d.Name}   ·   {(connected == true ? "connected" : connected == false ? "not plugged in" : "set up")}";
        }).ToList();
        ExtraList.ItemsSource = rows;
        ExtraList.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ExtraEmpty.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        RemoveDeviceButton.IsEnabled = rows.Count > 0;
    }

    private void AddDevice_Click(object sender, RoutedEventArgs e)
    {
        bool wasRunning = _s.Service.IsRunning;
        _s.Service.Stop();     // the device list is only changed while the mapper is stopped
        var dlg = new ExtraDeviceWindow(_s) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Saved is not null)
        {
            _s.Devices.Add(dlg.Saved);
            _s.Store.ReloadAll();
        }
        if (wasRunning) _s.Service.Start();
        RefreshExtras();
    }

    private void RemoveDevice_Click(object sender, RoutedEventArgs e)
    {
        var all = _s.Devices.ExtraDevices.ToList();
        if (all.Count == 0) return;
        DeviceMap target = ExtraList.SelectedIndex >= 0 && ExtraList.SelectedIndex < all.Count ? all[ExtraList.SelectedIndex] : all[^1];
        if (MessageBox.Show(this, $"Remove \"{target.Name}\"? Rules in profiles that use {target.Name}.… will stop working.", "Remove device",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;

        bool wasRunning = _s.Service.IsRunning;
        _s.Service.Stop();
        DeviceRegistry.RemoveEntry(_s.DevicesPath, target.Name);
        _s.Devices.Remove(target.Name);
        _s.Store.ReloadAll();
        if (wasRunning) _s.Service.Start();
        RefreshExtras();
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}

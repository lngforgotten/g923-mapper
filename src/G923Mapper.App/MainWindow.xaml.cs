using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace G923Mapper.App;

public sealed record ProfileItem(string Key, string File, bool Usable, string Display)
{
    public override string ToString() => Display;
}

public sealed record Issue(string Text, Brush Brush);

public partial class MainWindow : Window
{
    private static Brush Green => ThemeManager.Success;
    private static Brush Amber => ThemeManager.Warn;
    private static Brush Red => ThemeManager.Danger;
    private static Brush Grey => ThemeManager.Neutral;

    private readonly AppServices _s;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _healthTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _loadingList;
    private bool _initialising = true;
    private string? _selectAfterRefresh;
    private string _lastLog = "";

    private MapperService Service => _s.Service;
    private ProfileStore Store => _s.Store;
    private AppSettings Settings => _s.Settings;

    /// <summary>Set by the app when the user chose Exit, so closing the window really closes it.</summary>
    public bool Exiting { get; set; }
    public event Action? HiddenToTray;
    /// <summary>The user chose Exit from the menu (as opposed to closing the window, which keeps the app in the tray).</summary>
    public event Action? ExitRequested;
    public event Action<string>? StatusTextChanged;
    /// <summary>Raised when a game starts or stops and the profile changes by itself: (profile name, program), or (null, null) when it ends.</summary>
    public event Action<string?, string?>? ProfileAutoSwitched;

    public MainWindow(AppServices services)
    {
        InitializeComponent();
        _s = services;
        _s.UpdateChanged = () => Dispatcher.BeginInvoke(ShowUpdate);
        PadView.Clickable = true;
        PadView.ControlClicked += AssignControl;
        KeyChips.ChipClicked += EditKeyMapping;
        PadDiagram.ShowLabels = _s.Settings.ShowPadLabels;
        ShowLabelsBox.IsChecked = PadDiagram.ShowLabels;
        PadDiagram.LabelsChanged += () => ShowLabelsBox.IsChecked = PadDiagram.ShowLabels;

        AutoSwitchBox.IsChecked = Settings.AutoSwitch;
        FocusModeBox.IsChecked = Settings.AutoSwitchWhenFocused;
        FocusModeBox.IsEnabled = Settings.AutoSwitch;
        _s.Switcher.Enabled = Settings.AutoSwitch;
        _s.Switcher.Mode = Settings.AutoSwitchWhenFocused ? AutoSwitchMode.WhenFocused : AutoSwitchMode.WhenRunning;
        _initialising = false;

        _s.Switcher.Changed += (key, exe) => Dispatcher.BeginInvoke(() =>
        {
            AppendLog(key is null
                ? $"No known game running; back to \"{Settings.Profile}\"."
                : $"{exe} is {(Focused ? "in front" : "running")}: using profile \"{key}\".");
            ApplyProfile();
            UpdateAutoInfo();
            ProfileAutoSwitched?.Invoke(key is null ? null : Store.Get(key)?.Name ?? key, exe);
        });
        Service.Log += AppendLog;
        Store.Loaded += (file, r) => Dispatcher.BeginInvoke(() =>
        {
            AppendLog(r.Ok ? $"{file} reloaded." : $"{file} rejected: {r.Errors.FirstOrDefault()}");
            RefreshProfiles();
        });
        ThemeManager.Changed += () => Dispatcher.BeginInvoke(() => { ShowIssues(); UpdateHealth(); });

        RefreshProfiles();
        UpdateAutoInfo();
        DevicesTab.Initialise(_s);
        TestTab.Initialise(_s);

        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        _healthTimer.Tick += (_, _) => UpdateHealth();
        _healthTimer.Start();
        UpdateHealth();
    }

    // ---------------------------------------------------------------- live updates

    private void Tick()
    {
        MapperState state = Service.State;
        MapperSnapshot? snap = Service.Latest;

        (string title, Brush dot) = state switch
        {
            MapperState.Running => ($"Running: {Service.WheelName}", Green),
            MapperState.WaitingForWheel => ("Waiting for the wheel", Amber),
            MapperState.Error => ("Problem", Red),
            _ => ("Stopped", Grey),
        };
        StatusDot.Fill = dot;
        StatusTitle.Text = title;

        string detail = Service.Status;
        if (state == MapperState.Running)
        {
            detail = $"Profile \"{snap?.ProfileName ?? "…"}\"" + (snap?.ModeName is { } modeName ? $"  ·  Mode: {modeName}" : "") +
                     (_s.Switcher.ActiveKey is not null ? $" (switched automatically: {_s.Switcher.ActiveExe} is {(Focused ? "in front" : "running")})" : "") +
                     (snap is not null && PadKindOf(snap.ProfileName) == PadKind.DualShock4 ? "  ·  virtual DualShock 4 connected" : "  ·  virtual Xbox 360 controller connected");
            if (snap is { Raw.Live: false }) detail += "\nMove the wheel a little to wake it. Until then it reads as centred.";
        }
        StatusDetail.Text = detail;
        ToggleButton.Content = Service.IsRunning ? "Stop" : "Start";
        StatusTextChanged?.Invoke(state == MapperState.Running && snap is not null && Service.ModeName is { } modeNow
            ? $"G923 Mapper: {snap.ProfileName} / {modeNow}" : $"G923 Mapper: {title}");
        ShowModes();

        if (snap is null)
        {
            BarSteer.Value = BarThrottle.Value = BarBrake.Value = BarClutch.Value = 0;
            TxtSteer.Text = "0°";
            TxtThrottle.Text = TxtBrake.Text = TxtClutch.Text = "0%";
            TxtGameSees.Text = "Game sees: nothing (the mapper is stopped)";
            PadView.Set(null, PadKind.Xbox360);
            TxtWheelButtons.Text = "Wheel buttons down: none";
            CentreButton.IsEnabled = false;
            return;
        }

        double steer = snap.Input.Get("wheel");
        BarSteer.Value = steer;
        TxtSteer.Text = $"{steer * snap.Device.RotationDegrees / 2:+0;-0;0}°";
        SetBar(BarThrottle, TxtThrottle, snap.Input.Get("throttle"));
        SetBar(BarBrake, TxtBrake, snap.Input.Get("brake"));
        SetBar(BarClutch, TxtClutch, snap.Input.Get("clutch"));

        PadState pad = snap.Pad;
        PadView.Set(pad, PadKindOf(snap.ProfileName));
        KeyChips.Update(name => snap.Input.Get(name));
        RefreshTips(snap);
        var down = Enum.GetValues<PadButton>().Where(b => pad[b]).Select(b => b.ToString()).ToList();
        string seen = $"Game sees: left stick {pad.LeftX:+0;-0;0}  ·  right trigger {pad.RightTrigger}  ·  left trigger {pad.LeftTrigger}";
        if (down.Count > 0) seen += "  ·  " + string.Join(", ", down);
        if (pad.Keys.Count > 0) seen += "  ·  keys: " + string.Join(", ", pad.Keys);
        TxtGameSees.Text = seen;

        var wheelDown = new List<string>();
        if (snap.Raw.Live)
        {
            for (int i = 0; i < snap.Raw.Buttons.Length; i++)
            {
                if (!snap.Raw.Buttons[i]) continue;
                string n = InputDetector.ButtonName(snap.Device, i + 1);
                wheelDown.Add(n.StartsWith("button", StringComparison.Ordinal) ? n : $"{n} (button {i + 1})");
            }
            var (hu, hd, hl, hr) = DeviceMap.Hat(snap.Raw.Pov);
            if (hu) wheelDown.Add("dpadUp");
            if (hd) wheelDown.Add("dpadDown");
            if (hl) wheelDown.Add("dpadLeft");
            if (hr) wheelDown.Add("dpadRight");
        }
        TxtWheelButtons.Text = wheelDown.Count == 0 ? "Wheel buttons down: none" : "Wheel buttons down: " + string.Join(", ", wheelDown);

        if (snap.Device.Axes.TryGetValue("wheel", out var wa))
        {
            int raw = wa.SourceIndex >= 0 ? snap.Raw.Axes[wa.SourceIndex] : 0;
            CentreButton.ToolTip = $"The centre is {wa.Centre} and the wheel reads {raw}. Let go of the wheel, then click to make this position the centre.";
        }
        CentreButton.IsEnabled = true;
    }

    private static void SetBar(CentreBar bar, TextBlock text, double v)
    {
        bar.Value = v;
        text.Text = $"{v * 100:0}%";
    }

    private void AppendLog(string text)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => AppendLog(text)); return; }
        string line = $"{DateTime.Now:HH:mm:ss}  {text}";
        LogList.Items.Add(line);
        while (LogList.Items.Count > 200) LogList.Items.RemoveAt(0);
        LogList.ScrollIntoView(LogList.Items[^1]);
        _lastLog = line;
        // Collapsed, the header carries the latest message so nothing needs opening to see what just happened.
        LogExpander.Header = LogExpander.IsExpanded ? "Activity" : $"Activity   ·   {text}";
    }

    private void ShowLabels_Click(object sender, RoutedEventArgs e)
    {
        PadDiagram.ShowLabels = ShowLabelsBox.IsChecked == true;
        _s.Settings.ShowPadLabels = PadDiagram.ShowLabels;
        _s.Settings.Save();
    }

    /// <summary>The "a new version is available" line in the status card: shown while there is one the user has not put off.</summary>
    private void ShowUpdate()
    {
        UpdateInfo? info = _s.AvailableUpdate;
        bool show = info is { Newer: true } && info.LatestTag != Settings.UpdateDismissedTag;
        UpdatePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) UpdateText.Text = $"Version {info!.LatestTag} is available.";
    }

    private void UpdateDownload_Click(object sender, RoutedEventArgs e)
    {
        string url = _s.AvailableUpdate?.Url ?? "";
        if (url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { }
    }

    private void UpdateLater_Click(object sender, RoutedEventArgs e)
    {
        if (_s.AvailableUpdate is { } info) { Settings.UpdateDismissedTag = info.LatestTag; Settings.Save(); }
        ShowUpdate();
    }

    private string _modeKey = "";

    /// <summary>The Mode strip on Home: a button for each mode of the running profile, the active one lit, and how to switch. Click one to switch to it.</summary>
    private void ShowModes()
    {
        var names = Service.IsRunning ? Service.ModeNames : [];
        string key = string.Join("|", names) + "#" + Service.ModeIndex + "#" + Store.Version + "#" + (Service.Latest?.ProfileName ?? "");
        if (key == _modeKey) return;
        _modeKey = key;
        ModePanel.Visibility = names.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ModeChips.Children.Clear();
        if (names.Count == 0) return;
        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i];
            bool active = i == Service.ModeIndex;
            var b = new Button { Content = name, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 6, 6), FontSize = 13, FontWeight = active ? FontWeights.Bold : FontWeights.Normal, ToolTip = active ? "The mode in use now." : "Switch to " + name + "." };
            b.SetResourceReference(StyleProperty, "Secondary");
            if (active)
            {
                b.SetResourceReference(BackgroundProperty, "Accent");
                b.SetResourceReference(ForegroundProperty, "OnAccent");
            }
            b.Click += (_, _) => Service.RequestMode(name);
            ModeChips.Children.Add(b);
        }

        var how = new List<string>();
        if (Store.Get(Service.Latest?.ProfileName ?? "") is { } running)
        {
            var wheel = running.Rules.Concat(running.Modes.SelectMany(x => x.Rules))
                .Where(r => Target.TryParse(r.To, out Target? t, out _) && t is { Kind: TargetKind.Mode }).Select(r => $"{r.From} ({r.To})").Distinct().ToList();
            if (wheel.Count > 0) how.Add("wheel: " + string.Join(", ", wheel));
        }
        if (Settings.ModeHotkey.Enabled) how.Add("shortcut: " + Settings.ModeHotkey.Combo);
        ModeHint.Text = how.Count > 0 ? "Switch with a click, or " + string.Join("; ", how) + "."
            : "Switch with a click, or press Set switch button to choose a wheel button for it (or turn on the shortcut in Settings).";
        SwitchButtonButton.Content = how.Any(h => h.StartsWith("wheel")) ? "Change switch button..." : "Set switch button...";
    }

    private int _tipsVersion = -1;
    private string? _tipsProfile;

    /// <summary>The hover text of the controller picture: what feeds each control in the running profile. Rebuilt only when the profile or its file changed.</summary>
    private void RefreshTips(MapperSnapshot snap)
    {
        string key = snap.ProfileName + "|" + snap.ModeName;
        if (_tipsVersion == Store.Version && _tipsProfile == key) return;
        _tipsVersion = Store.Version;
        _tipsProfile = key;
        PadKind kind = PadKindOf(snap.ProfileName);
        Profile? running = RunningProfile.Of(Store, snap);   // what the game gets now: the profile with its active mode
        PadView.SetTips(PadTips.For(running, kind));
        PadView.SetLabels(running is { } shown ? PadControls.Labels(shown) : new Dictionary<string, string>());
        KeyChips.Set(running is { } withKeys ? KeyMappings.Of(withKeys) : []);
        string note = KeyStrip.NoteFor(running);
        KeyNote.Text = note;
        KeyNote.Visibility = note.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Which mode of the profile is active (-1 for a profile with no modes).</summary>
    private static int ModeIndexOf(Profile profile, MapperSnapshot snap) =>
        snap.ModeName is null ? -1 : profile.Modes.FindIndex(m => string.Equals(m.Name, snap.ModeName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The running profile and its file, or false after telling the user why not.</summary>
    private bool TryRunningProfile(string title, out MapperSnapshot snap, out Profile profile, out string file)
    {
        snap = Service.Latest!; profile = null!; file = "";
        if (!Service.IsRunning || Service.Latest is null)
        {
            MessageBox.Show(this, "Start the mapper with the wheel connected first, so there is a wheel to listen to.", title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (Store.Get(snap.ProfileName) is not { } p || Store.Files.FirstOrDefault(f => ReferenceEquals(Store.Get(f), p)) is not { } f2)
        {
            MessageBox.Show(this, "The running profile cannot be changed from here. Choose one on the Profiles tab first.", title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        profile = p; file = f2;
        return true;
    }

    /// <summary>A chip of the "Keys and mouse" strip was clicked (its rule), or the "+" chip (null): open the mapping window.</summary>
    public void EditKeyMapping(Rule? rule)
    {
        if (!TryRunningProfile("Keys and mouse", out MapperSnapshot snap, out Profile profile, out string file)) return;
        var dlg = new KeyMapWindow(_s, profile, System.IO.Path.Combine(_s.ProfileDir, file), rule, () => Service.Latest, ModeIndexOf(profile, snap)) { Owner = this };
        dlg.ShowDialog();
        if (dlg.Changed) { Store.Reload(file); AppendLog($"Changed the key and mouse mappings in {file}."); }
    }

    private void SwitchButton_Click(object sender, RoutedEventArgs e) => AssignControl(PadControls.ModeSwitchKey);

    /// <summary>A control of the controller picture was clicked (here or in the Devices tab): open the assign window for it.</summary>
    public void AssignControl(string key)
    {
        if (!TryRunningProfile("Assign", out MapperSnapshot snap, out Profile profile, out string file)) return;
        // The switch buttons belong to the profile itself, so they work in every mode; everything else is the active mode's.
        int mode = key == PadControls.ModeSwitchKey ? -1 : ModeIndexOf(profile, snap);
        var dlg = new AssignWindow(_s, profile, System.IO.Path.Combine(_s.ProfileDir, file), key, PadKindOf(snap.ProfileName), () => Service.Latest, mode) { Owner = this };
        bool ok = dlg.ShowDialog() == true;
        if (dlg.Changed) { Store.Reload(file); AppendLog($"Changed {PadControls.Title(key, PadKindOf(snap.ProfileName))} in {file}."); }
        if (ok && dlg.WantsEditor) { SelectTab("Profiles"); EditRules_Click(this, new RoutedEventArgs()); }
    }

    private PadKind PadKindOf(string profileName) => Store.Get(profileName) is { } p ? PadKinds.Of(p) : PadKind.Xbox360;

    private void LogExpander_Toggled(object sender, RoutedEventArgs e)
    {
        // Give the log room instead of squeezing the live view.
        if (IsLoaded && WindowState == WindowState.Normal) Height = Math.Max(MinHeight, Height + (LogExpander.IsExpanded ? 170 : -170));
        LogExpander.Header = LogExpander.IsExpanded || _lastLog.Length == 0 ? "Activity" : "Activity   ·   " + _lastLog[10..].Trim();
    }

    // ---------------------------------------------------------------- health badge

    private void UpdateHealth()
    {
        var missing = Service.IsRunning ? Service.ExtraStatus.Where(x => !x.Connected).Select(x => x.Name).ToList() : new List<string>();
        var items = HealthEvaluator.Evaluate(HealthProbe.Gather(Service, Store, _s.HidHide, _s.WheelHidden, missing));
        HealthLevel level = HealthEvaluator.Overall(items);
        Brush colour = level switch { HealthLevel.Problem => Red, HealthLevel.Warning => Amber, HealthLevel.Info => ThemeManager.Muted, _ => Green };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = colour, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = HealthEvaluator.Summary(items), VerticalAlignment = VerticalAlignment.Center });
        HealthButton.Content = row;
    }

    private void Health_Click(object sender, RoutedEventArgs e)
    {
        new HealthWindow(_s) { Owner = this }.ShowDialog();
        UpdateHealth();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        new SettingsWindow(_s) { Owner = this }.ShowDialog();
        RefreshProfiles();
        UpdateHealth();
    }

    // ---------------------------------------------------------------- profiles

    private void RefreshProfiles()
    {
        string? selectedKey = _selectAfterRefresh ?? Settings.Profile;
        _selectAfterRefresh = null;
        var items = Store.Files.Select(f =>
        {
            string key = System.IO.Path.GetFileNameWithoutExtension(f);
            var last = Store.LastResult(f);
            bool usable = Store.Get(key) is not null;
            string mark = last is { Ok: true } ? "" : usable ? "  (last save has errors)" : "  (invalid)";
            if (Store.Get(key) is { } shown)
            {
                if (shown.IsTemplate) mark += "  (template)";
                else if (shown.Parent is not null && Templates.UpdateAvailable(shown, FindTemplate(shown.Parent.Name))) mark += "  (template update available)";
            }
            return new ProfileItem(key, f, usable, key + mark);
        }).ToList();

        _loadingList = true;
        ProfileBox.ItemsSource = items;
        ProfileBox.SelectedItem = items.FirstOrDefault(i => string.Equals(i.Key, selectedKey, StringComparison.OrdinalIgnoreCase))
                                  ?? items.FirstOrDefault(i => i.Usable);
        _loadingList = false;

        // The chosen profile may have been deleted or become invalid: fall back to one that works so the
        // mapper (and the next launch) never sits on a profile that does not exist.
        if (ProfileBox.SelectedItem is ProfileItem sel && sel.Usable
            && !string.Equals(sel.Key, Settings.Profile, StringComparison.OrdinalIgnoreCase))
        {
            AppendLog($"Profile \"{Settings.Profile}\" is not available; using \"{sel.Key}\" instead.");
            Settings.Profile = sel.Key;
            Settings.Save();
        }
        ApplyProfile();
        ShowIssues();
        UpdateAutoInfo();
    }

    /// <summary>Choose a profile by its file name (without .json), for the tray menu.</summary>
    public void SelectProfile(string key)
    {
        if (ProfileBox.Items.OfType<ProfileItem>().FirstOrDefault(i => i.Usable && string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase)) is { } item)
            ProfileBox.SelectedItem = item;
    }

    /// <summary>Choose the next (+1) or previous (-1) usable profile, wrapping round. Returns its name, or null if there is no other one.</summary>
    public string? CycleProfile(int direction)
    {
        var usable = ProfileBox.Items.OfType<ProfileItem>().Where(i => i.Usable).ToList();
        if (usable.Count < 2) return null;
        int current = usable.FindIndex(i => ReferenceEquals(i, ProfileBox.SelectedItem));
        int next = (Math.Max(0, current) + direction + usable.Count) % usable.Count;
        ProfileBox.SelectedItem = usable[next];
        return usable[next].Key;
    }

    private void ProfileBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingList || ProfileBox.SelectedItem is not ProfileItem item) return;
        Settings.Profile = item.Key;
        Settings.Save();
        if (item.Usable)
        {
            ApplyProfile();
            AppendLog(_s.Switcher.ActiveKey is null
                ? $"Switched to profile \"{item.Key}\"."
                : $"Chosen \"{item.Key}\". It is used when {_s.Switcher.ActiveExe} closes (auto-switching is active).");
        }
        ShowIssues();
        UpdateAutoInfo();
    }

    /// <summary>A running game's profile wins while it runs; otherwise the one picked in the list.</summary>
    /// <summary>Raised whenever a profile is put into use (chosen, switched to for a game, or reloaded), with its name.</summary>
    public event Action<string>? ProfileInUse;

    /// <summary>
    /// Make the profile list show the profile that is actually running, which while a game is detected is the game's profile and not the one
    /// the user picked. This is display only: the user's own pick stays what is remembered and returns when the game closes.
    /// </summary>
    private void ShowInList(string key)
    {
        if (ProfileBox.SelectedItem is ProfileItem current && string.Equals(current.Key, key, StringComparison.OrdinalIgnoreCase)) return;
        if (ProfileBox.Items.OfType<ProfileItem>().FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase)) is not { } item) return;
        bool was = _loadingList;
        _loadingList = true;
        ProfileBox.SelectedItem = item;
        _loadingList = was;
        ShowIssues();
    }

    private void ApplyProfile()
    {
        string wanted = _s.Switcher.ActiveKey ?? Settings.Profile;
        if (Store.Get(wanted) is not null)
        {
            Service.ProfileName = wanted;
            ShowInList(wanted);
            ProfileInUse?.Invoke(wanted);
        }
    }

    private MiniWindow? _mini;

    /// <summary>Open the small always-on-top view (one at a time).</summary>
    public void ShowMini()
    {
        if (_mini is { IsLoaded: true }) { _mini.Activate(); return; }
        _mini = new MiniWindow(_s);
        _mini.Closed += (_, _) => _mini = null;
        _mini.Show();
    }

    private SetupWindow? _setup;

    /// <summary>Show the "Get started" checklist (one at a time).</summary>
    public void ShowSetup()
    {
        if (_setup is { IsLoaded: true }) { _setup.Activate(); return; }
        _setup = new SetupWindow(_s, this) { Owner = this };
        _setup.Closed += (_, _) => _setup = null;
        _setup.Show();
    }

    /// <summary>Switch to the tab with this header (Home, Profiles, Devices).</summary>
    public void SelectTab(string header)
    {
        foreach (TabItem tab in Tabs.Items.OfType<TabItem>())
            if (string.Equals(tab.Header as string, header, StringComparison.OrdinalIgnoreCase)) { Tabs.SelectedItem = tab; Activate(); return; }
    }

    private void UpdateAutoInfo()
    {
        AutoInfo.Text = !_s.Switcher.Enabled
            ? ""
            : _s.Switcher.ActiveKey is not null
                ? (Focused
                    ? $"Using \"{_s.Switcher.ActiveKey}\" because {_s.Switcher.ActiveExe} is in front. Your pick (\"{Settings.Profile}\") returns when you switch away."
                    : $"Using \"{_s.Switcher.ActiveKey}\" because {_s.Switcher.ActiveExe} is running. Your pick (\"{Settings.Profile}\") returns when it closes.")
                : (Focused ? "On. Waiting for a game window to come to the front." : "On. No profile's game is running right now.");
    }

    /// <summary>True when profiles follow the window in front rather than what is running.</summary>
    private bool Focused => _s.Switcher.Mode == AutoSwitchMode.WhenFocused;

    private void FocusModeBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_initialising) return;
        Settings.AutoSwitchWhenFocused = FocusModeBox.IsChecked == true;
        Settings.Save();
        _s.Switcher.Mode = Settings.AutoSwitchWhenFocused ? AutoSwitchMode.WhenFocused : AutoSwitchMode.WhenRunning;
        _s.Switcher.Poll();
        ApplyProfile();
        UpdateAutoInfo();
    }

    private void AutoSwitchBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_initialising) return;
        Settings.AutoSwitch = AutoSwitchBox.IsChecked == true;
        Settings.Save();
        FocusModeBox.IsEnabled = Settings.AutoSwitch;
        _s.Switcher.Enabled = Settings.AutoSwitch;
        _s.Switcher.Poll();          // reacts at once instead of waiting for the next 2 second poll
        ApplyProfile();
        UpdateAutoInfo();
    }

    private void ShowIssues()
    {
        var issues = new List<Issue>();
        if (ProfileBox.SelectedItem is not ProfileItem item)
        {
            ProfileInfo.Text = "No profiles found.";
            IssueList.ItemsSource = issues;
            return;
        }

        var result = Store.LastResult(item.File);
        Profile? served = Store.Get(item.Key);
        ProfileInfo.Text = served is null
            ? "This file has errors and cannot run yet."
            : $"{served.Rules.Count} rules" + (served.Modes.Count > 0 ? $", {served.Modes.Count} modes" : "") + (served.Match.Exe.Count > 0 ? $"  ·  for {string.Join(", ", served.Match.Exe)}" : "")
              + (string.IsNullOrWhiteSpace(served.Notes) ? "" : "\n" + served.Notes.Trim());

        if (result is not null)
        {
            foreach (string e in result.Errors) issues.Add(new Issue("✗ " + e, Red));
            foreach (string w in result.Warnings) issues.Add(new Issue("⚠ " + w, Amber));
            if (result.Errors.Count > 0 && served is not null)
                issues.Insert(0, new Issue("The last save was rejected. The previous version is still running.", Amber));
        }
        IssueList.ItemsSource = issues;     // empty when all is well: no "no problems" noise
        ShowTemplateInfo(served);
    }

    // ---------------------------------------------------------------- profile actions

    private void EditRules_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not ProfileItem item) return;
        if (Store.Get(item.Key) is not Profile profile)
        {
            MessageBox.Show(this, "This profile has errors and cannot be opened in the editor. Use More, then Edit JSON file, to fix it first.",
                "Edit rules", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        string path = System.IO.Path.Combine(_s.ProfileDir, item.File);
        var dlg = new RuleEditorWindow(profile, _s.Devices, path, () => Service.Latest, _s.Settings, FindTemplate) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Saved)
        {
            Store.Reload(item.File);
            AppendLog($"Saved {item.File}.");
        }
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not ProfileItem item) return;
        var dlg = new RestoreWindow(_s, item.File) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Restored)
        {
            Store.Reload(item.File);
            AppendLog($"Restored an earlier version of {item.File}.");
        }
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e) => NewProfile();
    private void RemapAll_Click(object sender, RoutedEventArgs e) => RemapAll();
    private void Import_Click(object sender, RoutedEventArgs e) => Import();
    private void Export_Click(object sender, RoutedEventArgs e) => Export();
    private void EditFile_Click(object sender, RoutedEventArgs e) => EditFile();
    private void OpenFolder_Click(object sender, RoutedEventArgs e) => OpenFolder();

    private void More_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = MoreButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        MenuItem Item(string header, RoutedEventHandler click)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += click;
            return mi;
        }
        menu.Items.Add(Item("Pop-out view", (_, _) => ShowMini()));
        menu.Items.Add(Item("Setup assistant…",(_, _) => ShowSetup()));
        menu.Items.Add(Item("Open log folder", (_, _) => { if (AppLog.Directory is string d) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + d + "\"") { UseShellExecute = true }); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Exit G923 Mapper", (_, _) => ExitRequested?.Invoke()));   // closing the window only hides it to the tray
        menu.IsOpen = true;
    }

    private void NewProfile()
    {
        var dlg = new NewProfileWindow(_s.Devices) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;
        SaveNewProfile(dlg.Result);
    }

    /// <summary>Write a new profile under a free file name, then select it.</summary>
    private void SaveNewProfile(Profile profile)
    {
        // A new profile follows the wheel: a PlayStation edition wheel gets a DualShock 4, any other an Xbox 360 controller.
        if (string.IsNullOrWhiteSpace(profile.Outputs.Pad)) profile.Outputs.Pad = PadKinds.AutoText;
        string key = Slug(profile.Name);
        string file = key + ".json";
        for (int n = 2; File.Exists(System.IO.Path.Combine(_s.ProfileDir, file)); n++)
        {
            key = $"{Slug(profile.Name)}-{n}";
            file = key + ".json";
        }
        File.WriteAllText(System.IO.Path.Combine(_s.ProfileDir, file), ProfileWriter.ToJson(profile));
        _selectAfterRefresh = key;
        Store.Reload(file);
        AppendLog($"Created {file}.");
    }

    /// <summary>Open Windows' own game controller test, the quickest way to see what a game would see from the virtual controller.</summary>
    private void TestInWindows()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("control.exe", "joy.cpl") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, "Could not open the Windows game controller test:\n" + ex.Message, "Test in Windows", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RemapAll()
    {
        if (!Service.IsRunning || Service.Latest is null)
        {
            MessageBox.Show(this, "Start the mapper with the wheel connected first, so there is a wheel to listen to.", "Remap all", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        string current = Service.Latest.ProfileName;
        var dlg = new RemapAllWindow(_s, PadKindOf(current)) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;
        SaveNewProfile(dlg.Result);
    }

    private void Import()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Profile (*.json)|*.json", Title = "Import a profile" };
        if (dlg.ShowDialog(this) != true) return;

        string text;
        try { text = File.ReadAllText(dlg.FileName); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Import", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        var result = ProfileLoader.Parse(text, _s.Devices);
        if (!result.Ok)
        {
            MessageBox.Show(this, "That file is not a valid profile:\n\n" + string.Join("\n", result.Errors.Take(8)),
                "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string key = Slug(System.IO.Path.GetFileNameWithoutExtension(dlg.FileName));
        string file = key + ".json";
        string path = System.IO.Path.Combine(_s.ProfileDir, file);
        if (File.Exists(path) && MessageBox.Show(this, $"{file} already exists. Replace it?", "Import",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;

        File.WriteAllText(path, text);   // the original text is kept, comments included
        _selectAfterRefresh = key;
        Store.Reload(file);
        AppendLog($"Imported {file}" + (result.Warnings.Count > 0 ? $" ({result.Warnings.Count} warnings)." : "."));
    }

    // ---------------------------------------------------------------- templates

    private Profile? FindTemplate(string name) =>
        Store.Files.Select(f => Store.Get(f)).FirstOrDefault(p => p is { IsTemplate: true } && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Under the profile on Home: which template it is based on, whether that template has changed, and the buttons for that.</summary>
    private void ShowTemplateInfo(Profile? served)
    {
        if (served is null || (!served.IsTemplate && served.Parent is null)) { TemplatePanel.Visibility = Visibility.Collapsed; return; }
        TemplatePanel.Visibility = Visibility.Visible;
        if (served.IsTemplate)
        {
            int users = Store.Files.Select(f => Store.Get(f)).Count(p => p?.Parent is { } pl && string.Equals(pl.Name, served.Name, StringComparison.OrdinalIgnoreCase));
            TemplateText.Text = users == 0 ? "A template: not used by any profile yet. Start one with From template on the Profiles tab."
                : $"A template: {users} profile{(users == 1 ? "" : "s")} {(users == 1 ? "is" : "are")} based on it. They are offered the changes you make here, and take them only when you apply them.";
            TemplateDiffButton.Visibility = TemplateUpdateButton.Visibility = Visibility.Collapsed;
            return;
        }
        Profile? template = FindTemplate(served.Parent!.Name);
        bool update = Templates.UpdateAvailable(served, template);
        int diffs = Templates.Diff(served).Count;
        TemplateText.Text = $"Based on the template \"{served.Parent.Name}\"" + (template is null ? " (that template is no longer in the profiles folder; this profile keeps its own copy)." : ".") +
            $" {(diffs == 0 ? "It does everything the template does." : $"{diffs} thing{(diffs == 1 ? "" : "s")} {(diffs == 1 ? "is" : "are")} its own.")}" +
            (update ? " The template has changed: an update is available." : "");
        TemplateDiffButton.Visibility = Visibility.Visible;
        TemplateUpdateButton.Visibility = update ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NewFromTemplate_Click(object sender, RoutedEventArgs e)
    {
        var templates = Store.Files.Select(f => Store.Get(f)).Where(p => p is { IsTemplate: true }).Select(p => p!).ToList();
        if (templates.Count == 0)
        {
            MessageBox.Show(this, "There are no templates yet. Use Save as template on a profile you like to make one.", "From template", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        bool Exists(string name) => Store.Get(name) is not null || File.Exists(System.IO.Path.Combine(_s.ProfileDir, Slug(name) + ".json"));
        var dlg = new TemplatePickerWindow(templates, Exists) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;
        SaveNewProfile(dlg.Result);
        AppendLog($"Created \"{dlg.Result.Name}\" from the template \"{dlg.Result.Parent!.Name}\".");
    }

    private void SaveAsTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not ProfileItem item || Store.Get(item.Key) is not Profile profile)
        {
            MessageBox.Show(this, "Choose a profile that loads without errors first.", "Save as template", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        string? name = Prompt.Ask(this, "Save as template", "Name for the new template. It keeps this profile's settings and rules, with no game attached.", profile.Name + " template")?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        if (Store.Get(name) is not null || File.Exists(System.IO.Path.Combine(_s.ProfileDir, Slug(name) + ".json")))
        {
            MessageBox.Show(this, "A profile or template with that name already exists. Choose another name.", "Save as template", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SaveNewProfile(Templates.SaveAsTemplate(profile, name));
        AppendLog($"Saved \"{profile.Name}\" as the template \"{name}\".");
    }

    private void TemplateDiff_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not ProfileItem item || Store.Get(item.Key) is not { Parent: not null } profile) return;
        string path = System.IO.Path.Combine(_s.ProfileDir, item.File);
        Profile working = ProfileLoader.Parse(ProfileWriter.ToJson(profile), _s.Devices).Profile ?? profile;   // a private copy, changed one reset at a time
        bool Reset(TemplateDiff diff)
        {
            // Putting the safety mode back to Standard is the same decision as choosing it, so it gets the same one-time warning.
            bool toStandard = (diff.Key == "safety" && diff.Template == ProfileSafety.Standard) || (diff.Key == "outputs.keyboard" && diff.Template == "on");
            if (toStandard && !SafetyPrompts.ConfirmStandard(this, _s.Settings)) return false;
            Templates.Reset(working, diff);
            File.WriteAllText(path, ProfileWriter.ToJson(working));
            Store.Reload(item.File);
            AppendLog($"Put \"{diff.Label}\" back to the template's in {item.File}.");
            return true;
        }
        new TemplateDiffWindow(working.Name, working.Parent!.Name, () => Templates.Diff(working), Reset) { Owner = this }.ShowDialog();
    }

    private void TemplateUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not ProfileItem item || Store.Get(item.Key) is not { Parent: not null } profile) return;
        if (FindTemplate(profile.Parent!.Name) is not { } template)
        {
            MessageBox.Show(this, $"The template \"{profile.Parent.Name}\" is not in the profiles folder any more, so there is nothing to update from.", "Update from template", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        int own = Templates.Diff(profile).Count;
        if (MessageBox.Show(this,
                $"Update \"{profile.Name}\" from the template \"{template.Name}\"?\n\nThe template has changed. The changes are applied to this profile; the {own} thing{(own == 1 ? "" : "s")} it does its own way {(own == 1 ? "is" : "are")} kept. The old version stays in Earlier versions.",
                "Update from template", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.OK) != MessageBoxResult.OK) return;
        ProfileLoadResult result = Templates.ApplyUpdate(profile, template, _s.Devices);
        if (!result.Ok)
        {
            MessageBox.Show(this, "The updated profile would not be valid, so nothing was changed:\n\n" + string.Join("\n", result.Errors.Take(6)), "Update from template", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        File.WriteAllText(System.IO.Path.Combine(_s.ProfileDir, item.File), ProfileWriter.ToJson(result.Profile!));
        Store.Reload(item.File);
        AppendLog($"Updated \"{profile.Name}\" from the template \"{template.Name}\".");
    }

    // ---------------------------------------------------------------- copy and paste a profile

    private void CopyProfile_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = CopyButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        MenuItem Item(string header, bool calibration, bool encode)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => CopyProfileAs(calibration, encode);
            return mi;
        }
        menu.Items.Add(Item("As text, with my wheel calibration", true, true));
        menu.Items.Add(Item("As text, profile only", false, true));
        menu.Items.Add(Item("As plain JSON (readable)", true, false));
        menu.IsOpen = true;
    }

    private void CopyProfileAs(bool withCalibration, bool encode)
    {
        if (ProfileBox.SelectedItem is not ProfileItem item || Store.Get(item.Key) is not Profile profile)
        {
            MessageBox.Show(this, "Choose a profile that loads without errors first.", "Copy profile", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DeviceMap? calibration = withCalibration ? _s.Devices.Resolve(profile.Device, Service.WheelUsbIds) : null;
        string text = ProfileShare.Export(profile, calibration, encode);
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            MessageBox.Show(this, "Windows would not let the clipboard be used just now: " + ex.Message, "Copy profile", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        string edition = calibration is null ? "" : $", with the {WheelEditions.Label(WheelEditions.Of(calibration))} calibration";
        ShareNote.Text = $"Copied \"{profile.Name}\"{edition} ({text.Length:N0} characters). Paste it into a message.";
        ShareNote.Visibility = Visibility.Visible;
        AppendLog($"Copied profile \"{profile.Name}\" to the clipboard.");
    }

    private void PasteProfile_Click(object sender, RoutedEventArgs e)
    {
        string text;
        try { text = Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            MessageBox.Show(this, "Windows would not let the clipboard be used just now: " + ex.Message, "Paste profile", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ShareImport import = ProfileShare.Import(text, _s.Devices);
        if (!import.Ok)
        {
            MessageBox.Show(this, "Nothing was imported.\n\n" + string.Join("\n", import.Errors.Take(6)), "Paste profile", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        WheelEdition connected = Service.WheelUsbIds is { } ids ? WheelEditions.Of(ids.Vid, ids.Pid) : WheelEdition.Unknown;
        bool Exists(string name) => Store.Get(name) is not null || File.Exists(System.IO.Path.Combine(_s.ProfileDir, Slug(name) + ".json"));
        var dlg = new ShareImportWindow(import, connected, _s.Devices, Exists) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        Profile profile = import.Profile!;
        profile.Name = dlg.ChosenName;
        if (dlg.ApplyCalibration && import.Calibration is { } cal)
        {
            DeviceRegistry.SaveEntry(_s.DevicesPath, cal);
            _s.Devices.Add(cal);
            Store.ReloadAll();   // profiles are re-checked against the new device map
            AppendLog($"Added the wheel calibration \"{cal.Name}\" that came with the profile.");
        }
        SaveNewProfile(profile);
        AppendLog($"Imported \"{profile.Name}\" from the clipboard" + (import.TurnedOff.Count > 0 ? " (keyboard and mouse output left off)." : "."));
    }

    private void Export()
    {
        if (ProfileBox.SelectedItem is not ProfileItem item) return;
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Profile (*.json)|*.json", FileName = item.File, Title = "Export this profile" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.Copy(System.IO.Path.Combine(_s.ProfileDir, item.File), dlg.FileName, overwrite: true);
            AppendLog($"Exported {item.File}.");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Export", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void OpenFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_s.ProfileDir}\"") { UseShellExecute = true }); }
        catch (Exception ex) { AppendLog("Could not open the folder: " + ex.Message); }
    }

    private void EditFile()
    {
        if (ProfileBox.SelectedItem is not ProfileItem item) return;
        string path = System.IO.Path.Combine(_s.ProfileDir, item.File);
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch
        {
            try { Process.Start("notepad.exe", $"\"{path}\""); }
            catch (Exception ex) { AppendLog("Could not open the file: " + ex.Message); }
        }
    }

    /// <summary>A safe file name: lower case, letters/digits, everything else becomes a hyphen.</summary>
    public static string Slug(string name)
    {
        var chars = name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        string s = string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return s.Length == 0 ? "profile" : s;
    }

    // ---------------------------------------------------------------- buttons and window

    private void ToggleButton_Click(object sender, RoutedEventArgs e) => ToggleRunning();

    public void ToggleRunning()
    {
        if (Service.IsRunning) { Service.Stop(); AppendLog("Stopped. The virtual controller has been removed."); }
        else { Service.Start(); AppendLog("Started."); }
    }

    private void CentreButton_Click(object sender, RoutedEventArgs e) => Service.Recentre();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!Exiting)
        {
            // Closing the window keeps the mapper running in the tray.
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke();
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _healthTimer.Stop();
        base.OnClosed(e);
    }
}

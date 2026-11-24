using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace G923Mapper.App;

/// <summary>The profile as the game is getting it right now: the profile with its active mode laid over it. The pictures and strips show this.</summary>
public static class RunningProfile
{
    public static Profile? Of(ProfileStore store, MapperSnapshot snap)
    {
        if (store.Get(snap.ProfileName) is not { } profile) return null;
        int mode = snap.ModeName is null ? -1 : profile.Modes.FindIndex(m => string.Equals(m.Name, snap.ModeName, StringComparison.OrdinalIgnoreCase));
        return ProfileModes.Resolve(profile, mode);
    }
}

/// <summary>The hover text of the controller picture: what feeds each control in a profile.</summary>
public static class PadTips
{
    public static IReadOnlyDictionary<string, string> For(Profile? profile, PadKind kind)
    {
        var tips = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (profile is not null)
            foreach (var (key, rules) in PadControls.Mappings(profile))
                tips[key] = PadControls.Title(key, kind) + "\n" + string.Join("\n", rules.Select(r => PadControls.Describe(r) + "  ->  " + r.To)) + "\nClick to change.";
        return tips;
    }
}

/// <summary>
/// Opened by clicking a control on the controller picture: shows what feeds that control (one row per slot, so a stick has three) and lets the
/// user record a wheel control for it or clear it. Every change is written to the profile file at once.
/// </summary>
public partial class AssignWindow : Window
{
    private sealed class Row
    {
        public required PadSlot Slot { get; init; }
        public required TextBlock Current { get; init; }
        public required Button Record { get; init; }
        public required Button Clear { get; init; }
        public required WrapPanel Options { get; init; }
    }

    private readonly AppServices _s;
    private readonly PadKind _kind;
    private readonly string _path;
    private readonly Func<MapperSnapshot?> _latest;
    private readonly Profile _full;      // the profile as it is in the file, modes and all
    private readonly Profile _profile;   // the rules in force in the mode being shown: what is being changed
    private readonly int _mode;
    private readonly List<Row> _rows = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private Row? _recording;
    private RawInput? _baseline;

    /// <summary>True once something was saved.</summary>
    public bool Changed { get; private set; }

    /// <summary>True when the user asked for the full rule editor instead.</summary>
    public bool WantsEditor { get; private set; }

    public AssignWindow(AppServices services, Profile profile, string path, string controlKey, PadKind kind, Func<MapperSnapshot?> latest, int mode = -1)
    {
        InitializeComponent();
        _s = services;
        _kind = kind;
        _path = path;
        _latest = latest;
        // Work on a copy, so nothing changes in the running profile until a save is written and reloaded.
        _full = ProfileLoader.Parse(ProfileWriter.ToJson(profile), services.Devices).Profile ?? profile;
        _mode = mode;
        _profile = ProfileModes.Resolve(_full, mode);   // inside a mode, a change is a change to that mode only

        TxtTitle.Text = PadControls.Title(controlKey, kind);
        TxtProfile.Text = $"Profile \"{profile.Name}\"" + (mode >= 0 ? $", mode \"{ProfileModes.NameAt(profile, mode)}\". A change here applies to this mode only." : "");
        foreach (PadSlot slot in PadControls.Slots(controlKey, kind)) AddRow(slot);
        RefreshRows();

        _timer.Tick += (_, _) => Tick();
        Closed += (_, _) => _timer.Stop();
    }

    private void AddRow(PadSlot slot)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = slot.Label, FontWeight = FontWeights.SemiBold });
        var current = new TextBlock { TextWrapping = TextWrapping.Wrap };
        current.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        text.Children.Add(current);
        grid.Children.Add(text);

        var record = new Button { Content = "Record", MinWidth = 78, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        record.SetResourceReference(StyleProperty, "Secondary");
        Grid.SetColumn(record, 1);
        grid.Children.Add(record);

        var clear = new Button { Content = "Clear", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        clear.SetResourceReference(StyleProperty, "Secondary");
        Grid.SetColumn(clear, 2);
        grid.Children.Add(clear);

        // Under the control: the options that make sense for what feeds it (invert, half of the axis, deadzone...), once something does.
        var options = new WrapPanel { Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        Grid.SetRow(options, 1);
        Grid.SetColumnSpan(options, 3);
        grid.Children.Add(options);

        var row = new Row { Slot = slot, Current = current, Record = record, Clear = clear, Options = options };
        record.Click += (_, _) => ToggleRecord(row);
        clear.Click += (_, _) => ClearRow(row);
        _rows.Add(row);
        Rows.Children.Add(grid);
    }

    private void RefreshRows()
    {
        foreach (Row r in _rows)
        {
            var rules = PadControls.RulesFor(_profile, r.Slot.Target);
            r.Current.Text = rules.Count == 0 ? "Not mapped" : string.Join("  +  ", rules.Select(PadControls.Describe));
            r.Clear.IsEnabled = rules.Count > 0;
            r.Record.Content = r == _recording ? "Cancel" : "Record";
            BuildOptions(r, rules);
        }
    }

    private DeviceMap Device => _latest()?.Device ?? _s.Devices.Fallback!;

    private TextBlock OptionLabel(string text)
    {
        var t = new TextBlock { Text = text, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        return t;
    }

    private static ComboBox Choice(IReadOnlyList<string> items, int selected, double width) =>
        new() { ItemsSource = items, SelectedIndex = Math.Max(0, selected), Width = width, Margin = new Thickness(0, 0, 14, 4), FontSize = 12 };

    private TextBox Percent(double fraction, Action<string, TextBox> commit)
    {
        var box = new TextBox { Text = AssignOptions.PercentText(fraction), Width = 46, Margin = new Thickness(0, 0, 14, 4), FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center };
        box.LostFocus += (_, _) => commit(box.Text, box);
        box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) commit(box.Text, box); };
        return box;
    }

    /// <summary>The options under one control, for the rule that feeds it. Changes are applied to the rule and saved straight away.</summary>
    private void BuildOptions(Row row, IReadOnlyList<Rule> rules)
    {
        row.Options.Children.Clear();
        row.Options.Visibility = Visibility.Collapsed;
        if (rules.Count == 0) return;
        if (rules.Count > 1)
        {
            var note = OptionLabel("Several rules feed this control; change their options in Edit all rules.");
            row.Options.Children.Add(note);
            row.Options.Visibility = Visibility.Visible;
            return;
        }

        Rule rule = rules[0];
        AssignOptionSet o = AssignOptions.For(rule, Device);
        if (!o.Any) return;
        row.Options.Visibility = Visibility.Visible;

        void Apply(string saved)
        {
            TxtStatus.Text = Save() ? saved : "Could not save that. The profile was left as it was.";
            row.Current.Text = string.Join("  +  ", PadControls.RulesFor(_profile, row.Slot.Target).Select(PadControls.Describe));
        }

        if (o.Invert)
        {
            var invert = new CheckBox { Content = "Invert", IsChecked = rule.Invert, Margin = new Thickness(0, 0, 14, 4), VerticalAlignment = VerticalAlignment.Center,
                                        ToolTip = "Flip the control: fully pressed counts as released and the other way round." };
            invert.Click += (_, _) => { rule.Invert = invert.IsChecked == true; Apply("Invert " + (rule.Invert ? "on." : "off.")); };
            row.Options.Children.Add(invert);
        }
        if (o.Half)
        {
            row.Options.Children.Add(OptionLabel("Use"));
            var half = Choice(AssignOptions.HalfChoices, AssignOptions.HalfIndex(rule), 118);
            half.ToolTip = "Use only one half of the axis, stretched to full range (for example to split a combined pedal axis in two, or to steer only to the right).";
            half.SelectionChanged += (_, _) => { AssignOptions.SetHalf(rule, half.SelectedIndex); Apply("Saved."); };
            row.Options.Children.Add(half);
        }
        if (o.Deadzone)
        {
            row.Options.Children.Add(OptionLabel("Deadzone %"));
            row.Options.Children.Add(Percent(rule.Deadzone, (text, box) =>
            {
                if (AssignOptions.TryPercent(text, 0, 99, true, out double f)) { if (Math.Abs(f - rule.Deadzone) > 1e-9) { rule.Deadzone = f; Apply("Deadzone saved."); } }
                else { box.Text = AssignOptions.PercentText(rule.Deadzone); TxtStatus.Text = "A deadzone is a percentage from 0 to 99."; }
            }));
        }
        if (o.Curve)
        {
            int curve = AssignOptions.CurveIndex(rule);
            if (curve >= 0)
            {
                row.Options.Children.Add(OptionLabel("Curve"));
                var box = Choice(AssignOptions.CurveChoices, curve, 82);
                box.ToolTip = "Linear is 1:1. Expo is softer around the middle, so small movements are finer.";
                box.SelectionChanged += (_, _) => { AssignOptions.SetCurve(rule, box.SelectedIndex); Apply("Saved."); };
                row.Options.Children.Add(box);
            }
            else row.Options.Children.Add(OptionLabel("Custom curve (draw it in Edit all rules)"));
        }
        if (o.Threshold)
        {
            row.Options.Children.Add(OptionLabel("Presses at %"));
            row.Options.Children.Add(Percent(rule.Threshold ?? 0.5, (text, box) =>
            {
                if (AssignOptions.TryPercent(text, -100, 100, false, out double f)) { if (rule.Threshold is not double old || Math.Abs(f - old) > 1e-9) { rule.Threshold = f; Apply("Trigger point saved."); } }
                else { box.Text = AssignOptions.PercentText(rule.Threshold ?? 0.5); TxtStatus.Text = "A trigger point is a percentage from -100 to 100, not 0."; }
            }));
        }
        if (o.Mode)
        {
            row.Options.Children.Add(OptionLabel("Mode"));
            var mode = Choice(AssignOptions.ModeChoices, AssignOptions.ModeIndex(rule), 82);
            mode.ToolTip = "Hold: pressed while you hold it. Toggle: each press switches it on or off. Pulse: one short press for each press.";
            mode.SelectionChanged += (_, _) => { AssignOptions.SetMode(rule, mode.SelectedIndex); Apply("Saved."); };
            row.Options.Children.Add(mode);
        }
    }

    private void ToggleRecord(Row row)
    {
        if (_recording == row) { StopRecording("Cancelled."); return; }
        MapperSnapshot? snap = _latest();
        if (snap is null) { TxtStatus.Text = "The mapper is not running, so there is no wheel to listen to. Start it from the main window."; return; }
        _recording = row;
        _baseline = snap.Raw;
        TxtStatus.Text = row.Slot.Target is "LeftStickX" or "RightStickX" or "LeftStickY" or "RightStickY"
            ? "Listening: turn the wheel a little, or move the control you want."
            : "Listening: press the wheel button or pedal you want.";
        RefreshRows();
        _timer.Start();
    }

    private void StopRecording(string message)
    {
        _timer.Stop();
        _recording = null;
        TxtStatus.Text = message;
        RefreshRows();
    }

    private void Tick()
    {
        MapperSnapshot? snap = _latest();
        if (_recording is null || snap is null) return;
        if (_baseline is null || !_baseline.Live) { _baseline = snap.Raw; return; }
        if (InputDetector.Detect(_baseline, snap.Raw, snap.Device) is not { Name: not null } hit) return;

        Row row = _recording;
        PadControls.Assign(_profile, snap.Device, hit.Name, row.Slot.Target);
        if (Save()) StopRecording($"Got {hit.Description}. {row.Slot.Label} now follows it.");
        else StopRecording("Could not save that. The profile was left as it was.");
    }

    private void ClearRow(Row row)
    {
        if (_recording is not null) StopRecording("");
        PadControls.Clear(_profile, row.Slot.Target);
        TxtStatus.Text = Save() ? $"{row.Slot.Label} is no longer mapped." : "Could not save that.";
        RefreshRows();
    }

    private bool Save()
    {
        if (_mode >= 0) ProfileModes.Capture(_full, _mode, _profile);   // keep only what differs from the profile's own rules
        string json = ProfileWriter.ToJson(_full);
        if (!ProfileLoader.Parse(json, _s.Devices).Ok) return false;
        try { File.WriteAllText(_path, json); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        Changed = true;
        return true;
    }

    private void EditAll_Click(object sender, RoutedEventArgs e)
    {
        WantsEditor = true;
        DialogResult = true;
    }

    private void Done_Click(object sender, RoutedEventArgs e) => DialogResult = Changed;
}

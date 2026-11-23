using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace G923Mapper.App;

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
    }

    private readonly AppServices _s;
    private readonly PadKind _kind;
    private readonly string _path;
    private readonly Func<MapperSnapshot?> _latest;
    private readonly Profile _profile;
    private readonly List<Row> _rows = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private Row? _recording;
    private RawInput? _baseline;

    /// <summary>True once something was saved.</summary>
    public bool Changed { get; private set; }

    /// <summary>True when the user asked for the full rule editor instead.</summary>
    public bool WantsEditor { get; private set; }

    public AssignWindow(AppServices services, Profile profile, string path, string controlKey, PadKind kind, Func<MapperSnapshot?> latest)
    {
        InitializeComponent();
        _s = services;
        _kind = kind;
        _path = path;
        _latest = latest;
        // Work on a copy, so nothing changes in the running profile until a save is written and reloaded.
        _profile = ProfileLoader.Parse(ProfileWriter.ToJson(profile), services.Devices).Profile ?? profile;

        TxtTitle.Text = PadControls.Title(controlKey, kind);
        TxtProfile.Text = $"Profile \"{profile.Name}\"";
        foreach (PadSlot slot in PadControls.Slots(controlKey, kind)) AddRow(slot);
        RefreshRows();

        _timer.Tick += (_, _) => Tick();
        Closed += (_, _) => _timer.Stop();
    }

    private void AddRow(PadSlot slot)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
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

        var row = new Row { Slot = slot, Current = current, Record = record, Clear = clear };
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
        string json = ProfileWriter.ToJson(_profile);
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

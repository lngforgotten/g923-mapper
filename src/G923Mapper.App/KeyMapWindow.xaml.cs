using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace G923Mapper.App;

/// <summary>
/// Adds, changes or removes one key or mouse mapping: pick the wheel control (or press it with Record), pick the key or mouse button (or press
/// it with "Press a key"), Save. Writes the profile file; the caller reloads it.
/// </summary>
public partial class KeyMapWindow : Window
{
    private readonly AppServices _s;
    private readonly string _path;
    private readonly Profile _original;
    private readonly Rule? _existing;
    private readonly Func<MapperSnapshot?> _latest;
    private readonly int _mode;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private RawInput? _baseline;
    private bool _recording, _capturing;

    /// <summary>True once the profile file was written (saved or removed).</summary>
    public bool Changed { get; private set; }

    /// <param name="existing">The rule to change or remove; null to add a new mapping.</param>
    public KeyMapWindow(AppServices services, Profile profile, string path, Rule? existing, Func<MapperSnapshot?> latest, int mode = -1)
    {
        InitializeComponent();
        _s = services;
        _path = path;
        _original = profile;
        _existing = existing;
        _latest = latest;
        _mode = mode;

        TxtTitle.Text = existing is null ? "Add a key or mouse mapping" : "Change a key or mouse mapping";
        TxtProfile.Text = $"Profile \"{profile.Name}\"" + (mode >= 0 ? $", mode \"{ProfileModes.NameAt(profile, mode)}\". A change here applies to this mode only." : "");
        KeyBox.ItemsSource = KeyNames.AllKeyNames.Concat(KeyNames.MouseButtonNames).ToList();
        RemoveButton.Visibility = existing is null ? Visibility.Collapsed : Visibility.Visible;
        if (existing is not null)
        {
            FromBox.Text = existing.From;
            KeyBox.Text = KeyNames.MouseButtonNames.FirstOrDefault(m => m.Equals(existing.To, StringComparison.OrdinalIgnoreCase)) ?? KeyMappings.Label(existing.To);
        }
        ShowSafety();

        _timer.Tick += (_, _) => Tick();
        Closed += (_, _) => _timer.Stop();
    }

    private void ShowSafety()
    {
        KeyOutputStatus status = KeyMappings.Status(_original);
        switch (status.State)
        {
            case KeyOutput.Active:
                TxtSafety.Text = "";
                break;
            case KeyOutput.NeedsEnabling:
                TxtSafety.Text = $"Keys and mouse are switched off for this profile ({status.Reason}). Saving will ask whether to turn them on. Only do that for offline and single-player games.";
                TxtSafety.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Warn");
                break;
            default:
                TxtSafety.Text = $"Keys and mouse can never be sent for this profile: {status.Reason}. Anything saved here would do nothing.";
                TxtSafety.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Danger");
                break;
        }
    }

    // ---------------------------------------------------------------- recording the wheel control

    private void Record_Click(object sender, RoutedEventArgs e)
    {
        if (_recording) { StopRecording("Cancelled."); return; }
        MapperSnapshot? snap = _latest();
        if (snap is null) { TxtStatus.Text = "The mapper is not running, so there is no wheel to listen to. Start it from the main window, or type the control's name."; return; }
        StopCapture();
        _recording = true;
        _baseline = snap.Raw;
        RecordButton.Content = "Cancel";
        TxtStatus.Text = "Listening: press the wheel button or pedal you want.";
        _timer.Start();
    }

    private void StopRecording(string message)
    {
        _timer.Stop();
        _recording = false;
        RecordButton.Content = "Record";
        TxtStatus.Text = message;
    }

    private void Tick()
    {
        MapperSnapshot? snap = _latest();
        if (!_recording || snap is null) return;
        if (_baseline is null || !_baseline.Live) { _baseline = snap.Raw; return; }
        if (InputDetector.Detect(_baseline, snap.Raw, snap.Device) is not { Name: not null } hit) return;
        FromBox.Text = hit.Name;
        StopRecording($"Got {hit.Description}.");
    }

    // ---------------------------------------------------------------- pressing the key

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (_capturing) { StopCapture(); return; }
        if (_recording) StopRecording("");
        _capturing = true;
        CaptureButton.Content = "Cancel";
        TxtStatus.Text = "Press the key you want (any key, including Escape). Click Cancel to stop.";
    }

    private void StopCapture()
    {
        _capturing = false;
        CaptureButton.Content = "Press a key";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk <= 0 || vk > 0xFF || KeyNames.NameForVk((ushort)vk) is not { } name)
        {
            TxtStatus.Text = "That key has no name this app can send. Pick another, or choose one from the list.";
            return;
        }
        KeyBox.Text = name;
        StopCapture();
        TxtStatus.Text = $"Got {name}.";
    }

    // ---------------------------------------------------------------- saving

    private bool Write(Profile profile)
    {
        string json = ProfileWriter.ToJson(profile);
        if (!ProfileLoader.Parse(json, _s.Devices).Ok)
        {
            MessageBox.Show(this, "That would make the profile invalid, so it was not saved.", "Key or mouse mapping", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        try { File.WriteAllText(_path, json); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Could not save: " + ex.Message, "Key or mouse mapping", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        Changed = true;
        return true;
    }

    /// <summary>
    /// Private copies to edit: the whole profile, the rules in force in the mode being shown (the same thing for a profile with no modes), and the
    /// rule being changed in them (found by what it connects).
    /// </summary>
    private (Profile Full, Profile Copy, Rule? Rule) Working()
    {
        Profile full = ProfileLoader.Parse(ProfileWriter.ToJson(_original), _s.Devices).Profile ?? _original;
        Profile copy = ProfileModes.Resolve(full, _mode);
        Rule? rule = _existing is null ? null : copy.Rules.FirstOrDefault(r => r.From == _existing.From && r.To == _existing.To);
        return (full, copy, rule);
    }

    /// <summary>Write the profile, first keeping a change made inside a mode as that mode's own.</summary>
    private bool WriteWorking(Profile full, Profile copy)
    {
        if (_mode >= 0) ProfileModes.Capture(full, _mode, copy);
        return Write(full);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string from = FromBox.Text.Trim();
        if (from.Length == 0) { TxtStatus.Text = "Choose the wheel control first (press Record, or type its name)."; return; }
        if (KeyMappings.TargetFor(KeyBox.Text) is null) { TxtStatus.Text = "That is not a key or mouse button this app knows. Pick one from the list, or press Press a key."; return; }

        var (full, copy, rule) = Working();
        KeyOutputStatus status = KeyMappings.Status(copy);
        if (status.State == KeyOutput.Blocked)
        {
            MessageBox.Show(this, $"Keys and mouse can never be sent for this profile: {status.Reason}.\n\nNothing was saved.", "Key or mouse mapping", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (status.State == KeyOutput.NeedsEnabling)
        {
            var answer = MessageBox.Show(this,
                $"Keys and mouse are switched off for this profile ({status.Reason}).\n\nTurn them on (the profile's safety becomes Standard)? Only do this for offline and single-player games: some games treat simulated keys as cheating.",
                "Key or mouse mapping", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
            KeyMappings.Enable(copy);
            KeyMappings.Enable(full);   // the profile-level switches live on the whole profile, not on the mode
        }

        MapperSnapshot? snap = _latest();
        DeviceMap device = snap?.Device ?? _s.Devices.Fallback!;
        if (KeyMappings.Set(copy, device, rule, from, KeyBox.Text) is null) { TxtStatus.Text = "Could not make that mapping."; return; }
        if (WriteWorking(full, copy)) DialogResult = true;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        var (full, copy, rule) = Working();
        if (rule is null || !KeyMappings.Remove(copy, rule)) { DialogResult = false; return; }
        if (WriteWorking(full, copy)) DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

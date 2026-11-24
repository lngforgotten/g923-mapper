using System;
using System.Linq;
using System.Windows;

namespace G923Mapper.App;

/// <summary>
/// The confirmation step of "Paste profile": what is in the text (name, number of rules, kinds of output), what was switched off on the way in,
/// whether to add the wheel calibration that came with it (with a warning when it is for the other edition), and the name to save it under.
/// Nothing is saved until the user presses Save.
/// </summary>
public partial class ShareImportWindow : Window
{
    private readonly ShareImport _import;
    private readonly Func<string, bool> _exists;
    private readonly DeviceRegistry _devices;

    /// <summary>The name to save the profile under.</summary>
    public string ChosenName { get; private set; } = "";

    /// <summary>Whether to add the calibration that came with the profile.</summary>
    public bool ApplyCalibration { get; private set; }

    /// <param name="connected">The edition of the wheel that is connected now (unknown when none is).</param>
    /// <param name="exists">Whether a profile already has this name.</param>
    public ShareImportWindow(ShareImport import, WheelEdition connected, DeviceRegistry devices, Func<string, bool> exists)
    {
        InitializeComponent();
        _import = import;
        _exists = exists;
        _devices = devices;

        TxtSummary.Text = import.Summary();
        string name = import.Profile!.Name;
        for (int n = 2; exists(name); n++) name = $"{import.Profile.Name} ({n})";
        NameBox.Text = name;

        OffPanel.Visibility = import.TurnedOff.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        TxtOff.Text = string.Join("\n", import.TurnedOff.Select(t => "• " + t));

        ShowCalibration(connected);

        var notes = import.Warnings.Take(8).ToList();
        if (!string.IsNullOrWhiteSpace(import.Profile.Notes)) notes.Insert(0, "The author's note: " + import.Profile.Notes.Trim());
        NotePanel.Visibility = notes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        TxtNotes.Text = string.Join("\n", notes);
        UpdateName();
    }

    private void ShowCalibration(WheelEdition connected)
    {
        if (_import.Calibration is not { } cal) { CalPanel.Visibility = Visibility.Collapsed; return; }
        var fit = ProfileShare.Fit(_import, connected);
        string edition = WheelEditions.Label(_import.Edition);
        CalBox.Content = new System.Windows.Controls.TextBlock { Text = $"Also add the wheel calibration \"{cal.Name}\" ({edition})", TextWrapping = TextWrapping.Wrap };

        bool replaces = _devices.All.Any(d => string.Equals(d.Name, cal.Name, StringComparison.OrdinalIgnoreCase));
        switch (fit)
        {
            case ProfileShare.CalibrationFit.Different:
                CalBox.IsChecked = false;
                TxtCal.Text = $"This calibration is for the {edition}, but the wheel connected now is the {WheelEditions.Label(connected)}. " +
                              "Applying it would make the wrong buttons and axes line up. Leave it unticked unless you know what you are doing.";
                TxtCal.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Danger");
                break;
            case ProfileShare.CalibrationFit.Same:
                CalBox.IsChecked = !replaces;
                TxtCal.Text = replaces ? "You already have a calibration with this name; ticking this replaces it." : "It matches the wheel you have connected.";
                TxtCal.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, replaces ? "Warn" : "TextMuted");
                break;
            default:
                CalBox.IsChecked = false;
                TxtCal.Text = "No wheel is connected (or its edition is not known), so it cannot be checked against yours. " +
                              "Your own calibration is usually the better choice: run Calibrate once instead." + (replaces ? " Ticking this would replace your calibration with the same name." : "");
                TxtCal.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Warn");
                break;
        }
    }

    private void NameBox_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateName();

    private void UpdateName()
    {
        if (TxtNameNote is null || SaveButton is null) return;
        string n = NameBox.Text.Trim();
        bool empty = n.Length == 0;
        bool taken = !empty && _exists(n);
        SaveButton.IsEnabled = !empty && !taken;
        TxtNameNote.Text = empty ? "Give it a name." : taken ? "You already have a profile with this name. Choose another." : "";
        TxtNameNote.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Danger");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ChosenName = NameBox.Text.Trim();
        ApplyCalibration = _import.Calibration is not null && CalBox.IsChecked == true;
        DialogResult = true;
    }
}

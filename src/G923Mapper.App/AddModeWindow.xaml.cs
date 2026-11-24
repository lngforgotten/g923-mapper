using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace G923Mapper.App;

/// <summary>
/// "Add mode": pick a ready-made mode (Driving, Shooting, Flying or a blank one) and give it a name. The ready-made ones come with the usual
/// rules, so adding Driving needs no set-up.
/// </summary>
public partial class AddModeWindow : Window
{
    private readonly Func<string, bool> _taken;
    private readonly IReadOnlyList<Rule> _profileRules;
    private string _autoName = "";

    public string ModeName { get; private set; } = "";
    public string PresetId { get; private set; } = ModePresets.Blank;

    /// <param name="taken">Whether a mode already has this name.</param>
    /// <param name="profileRules">The profile's own rules, to say which of them a ready-made mode would turn off.</param>
    public AddModeWindow(Func<string, bool> taken, IReadOnlyList<Rule> profileRules, bool firstMode)
    {
        InitializeComponent();
        _taken = taken;
        _profileRules = profileRules;
        PresetList.ItemsSource = ModePresets.All;
        // A profile with no modes yet most likely wants Shooting first (the pedals' own job), then Driving.
        PresetList.SelectedItem = ModePresets.All.First(p => p.Id == (firstMode ? ModePresets.Shooting : ModePresets.Driving));
    }

    private ModePreset? Chosen => PresetList.SelectedItem as ModePreset;

    private void Preset_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (Chosen is not { } preset) return;
        TxtDescription.Text = preset.Description;
        TxtKeys.Visibility = preset.UsesKeys ? Visibility.Visible : Visibility.Collapsed;

        // Say plainly which of the profile's own rules this mode would put aside, so nothing changes without being seen.
        var probe = new Profile { Name = "x", Rules = _profileRules.ToList() };
        ProfileMode mode = ModePresets.AddTo(probe, preset.Name, preset.Id);
        TxtDisplaced.Text = mode.RemovedRules is { Count: > 0 } removed
            ? "In this mode the profile's rules for " + string.Join(", ", removed) + " are turned off, because the same wheel controls are used for something else here."
            : "";

        // Keep the name in step with the choice until the user types their own.
        if (NameBox.Text.Length == 0 || NameBox.Text == _autoName)
        {
            _autoName = preset.Id == ModePresets.Blank ? "" : preset.Name;
            NameBox.Text = _autoName;
        }
        Name_Changed(sender, e);
    }

    private void Name_Changed(object sender, RoutedEventArgs e)
    {
        if (AddButton is null || TxtProblem is null) return;
        string name = NameBox.Text.Trim().Replace(":", "");
        string? problem = name.Length == 0 ? "Give the mode a name."
            : ProfileModes.IsReservedName(name) ? "Next and Previous are used for switching; choose another name."
            : _taken(name) ? "Another mode already has this name."
            : null;
        TxtProblem.Text = problem ?? "";
        AddButton.IsEnabled = problem is null;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        ModeName = NameBox.Text.Trim().Replace(":", "");
        // A blank mode named after a ready-made one (Driving, Shooting, Flying) gets that one's rules: naming it is enough.
        PresetId = Chosen is { Id: ModePresets.Blank } && ModePresets.ForName(ModeName) is { } named ? named.Id : (Chosen?.Id ?? ModePresets.Blank);
        DialogResult = true;
    }
}

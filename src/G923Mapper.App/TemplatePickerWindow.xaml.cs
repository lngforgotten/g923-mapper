using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace G923Mapper.App;

/// <summary>"New from template": pick a template, name the profile and say which game it is for. Nothing is written until Create.</summary>
public partial class TemplatePickerWindow : Window
{
    private readonly Func<string, bool> _exists;

    /// <summary>The profile that was built, once the user pressed Create profile.</summary>
    public Profile? Result { get; private set; }

    /// <param name="exists">Whether a profile with this name already exists.</param>
    public TemplatePickerWindow(IReadOnlyList<Profile> templates, Func<string, bool> exists)
    {
        InitializeComponent();
        _exists = exists;
        TemplateList.ItemsSource = templates.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        TemplateList.SelectedIndex = 0;
    }

    private Profile? Chosen => TemplateList.SelectedItem as Profile;

    private void Template_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (Chosen is not { } t) return;
        string notes = string.IsNullOrWhiteSpace(t.Notes) ? "No description." : t.Notes.Trim();
        bool keys = t.Outputs.Keyboard || KeyMappings.Of(t).Count > 0;
        TxtNotes.Text = notes + (keys
            ? "\n\nThis template presses keys or mouse buttons. A new profile starts with those switched off; turn them on in Edit rules (Safety: Standard) for an offline or single-player game."
            : "");
        if (string.IsNullOrWhiteSpace(NameBox.Text) || TemplateList.Items.OfType<Profile>().Any(p => p.Name == NameBox.Text)) NameBox.Text = "";
        Fields_Changed(sender, e);
    }

    private void Fields_Changed(object sender, RoutedEventArgs e)
    {
        if (CreateButton is null || TxtProblem is null) return;
        string name = NameBox.Text.Trim();
        string? problem = Chosen is null ? "Choose a template." : name.Length == 0 ? "Give the profile a name." : _exists(name) ? "A profile with this name already exists." : null;
        TxtProblem.Text = problem ?? "";
        CreateButton.IsEnabled = problem is null;
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (Chosen is not { } t) return;
        var exes = ExeBox.Text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Result = Templates.CreateChild(t, NameBox.Text.Trim(), exes);
        DialogResult = true;
    }
}

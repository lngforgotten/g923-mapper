using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace G923Mapper.App;

/// <summary>
/// Every way a profile differs from its template, one row each, with a button to put that one back to what the template has. The caller does the
/// resetting (in the editor's unsaved state, or in the saved file), and says whether it worked.
/// </summary>
public partial class TemplateDiffWindow : Window
{
    private readonly Func<TemplateDiff, bool> _reset;
    private readonly Func<IReadOnlyList<TemplateDiff>> _current;
    private readonly string _template;

    /// <param name="reset">Put one difference back; returns false if it was not done (the row stays).</param>
    /// <param name="current">The differences as they are now, asked again after each reset.</param>
    public TemplateDiffWindow(string profileName, string templateName, Func<IReadOnlyList<TemplateDiff>> current, Func<TemplateDiff, bool> reset)
    {
        InitializeComponent();
        _reset = reset;
        _current = current;
        _template = templateName;
        TxtTitle.Text = $"\"{profileName}\" and the template \"{templateName}\"";
        Refresh();
    }

    private void Refresh()
    {
        var diffs = _current();
        Rows.Children.Clear();
        TxtHelp.Text = diffs.Count == 0
            ? $"This profile does everything the template \"{_template}\" does. Anything you change from now on shows up here."
            : "These are the things this profile does its own way. Everything else follows the template. Reset puts one back to what the template has; changes you make in a template later are offered to this profile as an update, and these differences are kept.";
        ResetAllButton.IsEnabled = diffs.Count > 0;
        foreach (TemplateDiff d in diffs)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel();
            string kind = d.Kind switch { DiffKind.RuleAdded => "added by this profile", DiffKind.RuleRemoved => "removed by this profile", DiffKind.RuleChanged => "changed", _ => "changed" };
            text.Children.Add(new TextBlock { Text = $"{d.Label}  ({kind})", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var detail = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            detail.Text = d.Kind switch
            {
                DiffKind.RuleAdded => "Template: nothing   |   This profile: " + d.Current,
                DiffKind.RuleRemoved => "Template: " + d.Template + "   |   This profile: nothing",
                _ => "Template: " + d.Template + "   |   This profile: " + d.Current,
            };
            text.Children.Add(detail);
            row.Children.Add(text);

            var button = new Button { Content = d.Kind == DiffKind.RuleRemoved ? "Restore" : d.Kind == DiffKind.RuleAdded ? "Remove" : "Reset", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MinWidth = 76 };
            button.SetResourceReference(StyleProperty, "Secondary");
            TemplateDiff diff = d;
            button.Click += (_, _) => { if (_reset(diff)) Refresh(); };
            Grid.SetColumn(button, 1);
            row.Children.Add(button);
            Rows.Children.Add(row);
        }
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        if (ThemedMessageBox.Show(this, "Put everything back to what the template has? The profile keeps its name, game and notes.", "Reset everything",
                MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        foreach (TemplateDiff d in _current().ToList()) if (!_reset(d)) break;
        Refresh();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}


using System;
using System.Collections.Generic;
using System.Windows;

namespace G923Mapper.App;

/// <summary>The list behind the rule editor's Checks button.</summary>
public partial class ChecksWindow : Window
{
    /// <param name="issues">Asked again after a theme change, so the list takes the new theme's colours.</param>
    public ChecksWindow(string summary, Func<IReadOnlyList<Issue>> issues)
    {
        InitializeComponent();
        Summary.Text = summary;
        Rows.ItemsSource = issues();
        void OnTheme() => Dispatcher.BeginInvoke(() => Rows.ItemsSource = issues());
        ThemeManager.Changed += OnTheme;
        Closed += (_, _) => ThemeManager.Changed -= OnTheme;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

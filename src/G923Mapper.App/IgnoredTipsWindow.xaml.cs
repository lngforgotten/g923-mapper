using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace G923Mapper.App;

public sealed record IgnoredRow(string Title, string Detail, Action<IgnoredTips> Restore)
{
    public string ButtonName => $"Turn back on: {Title}";
    public override string ToString() => $"{Title}. {Detail}";
}

public partial class IgnoredTipsWindow : Window
{
    private readonly IgnoredTipsFile _file;

    public IgnoredTipsWindow(IgnoredTipsFile file)
    {
        InitializeComponent();
        _file = file;
        Refresh();
    }

    private void Refresh()
    {
        IgnoredTips tips = _file.Read();
        var rows = new List<IgnoredRow>();
        if (tips.AllLeftovers)
            rows.Add(new("All leftover virtual controllers", "Never mentioned, including new ones.", t => t.AllLeftovers = false));
        rows.AddRange(tips.LeftoverIds.Select(id => new IgnoredRow(IgnoredTips.Label(id),
            tips.AllLeftovers ? "Also ignored on its own." : "Not mentioned. A different leftover still is.",
            t => t.Restore([id]))));

        Rows.ItemsSource = rows;
        Empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ResetButton.IsEnabled = rows.Count > 0;
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: IgnoredRow row }) Save(row.Restore);
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => Save(t => t.Reset());

    private void Save(Action<IgnoredTips> change)
    {
        if (!_file.Change(change))
            ThemedMessageBox.Show(this, "That could not be saved. See the log.", "Ignored tips", MessageBoxButton.OK, MessageBoxImage.Warning);
        Refresh();
    }
}

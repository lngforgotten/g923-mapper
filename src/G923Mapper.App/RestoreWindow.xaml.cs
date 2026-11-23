using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace G923Mapper.App;

/// <summary>Lists the earlier versions of one profile and puts the chosen one back.</summary>
public partial class RestoreWindow : Window
{
    private sealed record Item(HistoryEntry Entry, string When, string Detail);

    private readonly AppServices _s;
    private readonly string _profilePath;

    /// <summary>True when a version was restored.</summary>
    public bool Restored { get; private set; }

    public RestoreWindow(AppServices services, string profileFile)
    {
        InitializeComponent();
        _s = services;
        _profilePath = Path.Combine(_s.ProfileDir, profileFile);
        TxtTitle.Text = $"Earlier versions of \"{Path.GetFileNameWithoutExtension(profileFile)}\"";

        var items = ProfileHistory.List(_profilePath, AppSettings.HistoryDir).Select(Describe).ToList();
        Versions.ItemsSource = items;
        TxtNone.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (items.Count > 0) Versions.SelectedIndex = 0;
    }

    private Item Describe(HistoryEntry e)
    {
        string detail;
        try
        {
            var r = ProfileLoader.Parse(File.ReadAllText(e.Path), _s.Devices);
            detail = r.Profile is null ? "has errors (it did not load)" : $"{r.Profile.Rules.Count} rule{(r.Profile.Rules.Count == 1 ? "" : "s")}" +
                     (r.Errors.Count > 0 ? ", has errors" : "") + $"  ·  {e.Size} bytes";
        }
        catch (IOException) { detail = "could not be read"; }

        string day = e.When.Date == DateTime.Today ? "Today" : e.When.Date == DateTime.Today.AddDays(-1) ? "Yesterday" : e.When.ToString("d MMM yyyy");
        return new Item(e, $"{day} {e.When:HH:mm:ss}", detail);
    }

    private void Versions_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        RestoreButton.IsEnabled = Versions.SelectedItem is Item;

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (Versions.SelectedItem is not Item item) return;
        if (!ProfileHistory.Restore(item.Entry, _profilePath, AppSettings.HistoryDir))
        {
            MessageBox.Show(this, "That version could not be restored.", "Restore", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Restored = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

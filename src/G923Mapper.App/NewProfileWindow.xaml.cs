using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace G923Mapper.App;

public partial class NewProfileWindow : Window
{
    private readonly DeviceRegistry _devices;
    private string _autoName = "";

    /// <summary>The profile to create, when the user pressed Create.</summary>
    public Profile? Result { get; private set; }

    public NewProfileWindow(DeviceRegistry devices)
    {
        InitializeComponent();
        _devices = devices;
        PresetList.ItemsSource = Presets.All.Concat(Presets.Gallery).ToList();
        PresetList.SelectedIndex = 0;
    }

    private void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetList.SelectedItem is not Preset p) return;
        // Follow the preset's name until the user types their own.
        if (NameBox.Text == _autoName || NameBox.Text.Length == 0) NameBox.Text = _autoName = p.Title;
        UpdateCreate();
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateCreate();

    private void UpdateCreate() => CreateButton.IsEnabled = PresetList.SelectedItem is not null && NameBox.Text.Trim().Length > 0;

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (PresetList.SelectedItem is not Preset p) return;
        Result = Presets.Instantiate(p, NameBox.Text, _devices);
        if (Result is null)
        {
            MessageBox.Show(this, "That template could not be loaded with the current device settings.", "New profile",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

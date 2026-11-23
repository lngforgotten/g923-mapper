using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace G923Mapper.App;

/// <summary>A tiny "type a value" dialog, built in code to avoid another XAML file.</summary>
internal static class Prompt
{
    public static string? Ask(Window owner, string title, string label, string defaultValue)
    {
        var box = new TextBox { Text = defaultValue, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 8, 0, 14) };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Padding = new Thickness(10, 5, 10, 5) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } };
        var panel = new StackPanel { Margin = new Thickness(16), Children = { new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, box, buttons } };

        var win = new Window
        {
            Title = title, Content = panel, Width = 420, SizeToContent = SizeToContent.Height, Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            FontFamily = owner.FontFamily, FontSize = owner.FontSize,
        };
        ok.Click += (_, _) => win.DialogResult = true;
        win.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return win.ShowDialog() == true ? box.Text : null;
    }
}

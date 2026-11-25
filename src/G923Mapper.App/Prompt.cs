using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace G923Mapper.App;

/// <summary>The one-time warning before a profile is switched to Standard safety, shared by everything that can do it.</summary>
internal static class SafetyPrompts
{
    /// <summary>
    /// True when switching a profile to Standard may go ahead: the user has already accepted the warning once, or accepts it now (and is not
    /// asked again). False if they decline.
    /// </summary>
    public static bool ConfirmStandard(Window owner, AppSettings? settings)
    {
        if (settings?.WarnedAboutStandard == true) return true;
        var answer = ThemedMessageBox.Show(owner,
            "Standard mode allows extras such as keyboard output, which synthetic key presses can look like automation to anti-cheat. " +
            "It is meant for offline and single-player games. Do not use it for a game with online play or anti-cheat (for example GTA Online).\n\n" +
            "Switch this profile to Standard?",
            "Switch to Standard", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return false;
        if (settings is not null) { settings.WarnedAboutStandard = true; settings.Save(); }
        return true;
    }
}

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


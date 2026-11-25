using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace G923Mapper.App;

/// <summary>
/// A profile's own mode shortcuts: either the app-wide ones from Settings, or a list of its own. Games bind different keys, so a profile can
/// have shortcuts that do not clash with the game's. Built in code, like the other small question windows.
/// </summary>
internal sealed class ProfileShortcutsWindow : Window
{
    private readonly RadioButton _appWide, _own;
    private readonly ListBox _list = new() { MinHeight = 80, MaxHeight = 150, Margin = new Thickness(22, 8, 0, 0) };
    private readonly Button _add = new() { Content = "Add...", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _remove = new() { Content = "Remove", Padding = new Thickness(12, 5, 12, 5), IsEnabled = false };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(22, 6, 0, 0) };
    private readonly List<ModeShortcut> _items;

    /// <summary>The profile's own list, or null when it uses the app-wide one. Read after the window returned true.</summary>
    public List<ModeShortcut>? Result { get; private set; }

    public ProfileShortcutsWindow(Window owner, List<ModeShortcut>? current, int appWideCount)
    {
        Title = "Mode shortcuts for this profile";
        Owner = owner;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        SetResourceReference(BackgroundProperty, "WindowBack");
        SetResourceReference(ForegroundProperty, "TextPrimary");
        FontFamily = owner.FontFamily; FontSize = owner.FontSize;

        _items = current?.Select(m => new ModeShortcut { Trigger = m.Trigger, Action = m.Action }).ToList() ?? [];
        _appWide = new RadioButton
        {
            Content = $"Use the app-wide shortcuts from Settings ({(appWideCount == 0 ? "none are set" : appWideCount + " set")})",
            IsChecked = current is null, GroupName = "which", Margin = new Thickness(0, 0, 0, 6),
        };
        _own = new RadioButton { Content = "Use this profile's own shortcuts", IsChecked = current is not null, GroupName = "which" };
        _appWide.Checked += (_, _) => UpdateEnabled();
        _own.Checked += (_, _) => UpdateEnabled();

        _list.SelectionChanged += (_, _) => _remove.IsEnabled = _own.IsChecked == true && _list.SelectedIndex >= 0;
        _add.Click += (_, _) => AddOne();
        _remove.Click += (_, _) => { if (_list.SelectedIndex >= 0 && _list.SelectedIndex < _items.Count) { _items.RemoveAt(_list.SelectedIndex); Refresh(); } };

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => { Result = _own.IsChecked == true ? _items : null; DialogResult = true; };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Padding = new Thickness(10, 5, 10, 5) };

        var note = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 12, 0, 0),
            Text = "Games bind different keys, so pick ones this game does not use. They work only while this profile is running. An empty list means no shortcuts for this profile at all.",
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        _message.SetResourceReference(TextBlock.ForegroundProperty, "Warn");

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                _appWide, _own, _list,
                new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 8, 0, 0), Children = { _add, _remove } },
                _message, note,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), Children = { ok, cancel } },
            },
        };
        Refresh();
        UpdateEnabled();
    }

    private void Refresh()
    {
        _list.ItemsSource = _items.Select(m => m.Describe()).ToList();
        _remove.IsEnabled = false;
    }

    private void UpdateEnabled()
    {
        bool own = _own.IsChecked == true;
        _list.IsEnabled = _add.IsEnabled = own;
        _remove.IsEnabled = own && _list.SelectedIndex >= 0;
        _message.Text = "";
    }

    private void AddOne()
    {
        var dlg = new ModeShortcutWindow(this);
        if (dlg.ShowDialog() != true || dlg.Result is not { } made) return;
        InputTrigger.TryParse(made.Trigger, out var trigger, out _);
        if (_items.Count >= ModeShortcutList.MaxShortcuts) { _message.Text = $"At most {ModeShortcutList.MaxShortcuts} shortcuts."; return; }
        if (_items.Any(m => InputTrigger.TryParse(m.Trigger, out var other, out _) && other == trigger)) { _message.Text = $"{trigger} is already used by another shortcut here."; return; }
        _items.Add(made);
        _message.Text = "";
        Refresh();
    }
}


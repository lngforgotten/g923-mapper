using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace G923Mapper.App;

/// <summary>
/// A profile's mode shortcuts. Keyboard keys and mouse buttons: either the app-wide ones from Settings, or a list of its own (games bind
/// different keys, so a profile can have shortcuts that do not clash with the game's). Wheel controls: wheel buttons and pedals that switch modes,
/// which are ordinary rules sending to Mode:..., as they have always been. Built in code, like the other small question windows.
/// </summary>
internal sealed class ProfileShortcutsWindow : Window
{
    private readonly RadioButton _appWide, _own;
    private readonly ListBox _list = new() { MinHeight = 70, MaxHeight = 130, Margin = new Thickness(22, 8, 0, 0) };
    private readonly Button _add = new() { Content = "Add key or mouse button...", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _remove = new() { Content = "Remove", Padding = new Thickness(12, 5, 12, 5), IsEnabled = false };
    private readonly ListBox _wheelList = new() { MinHeight = 56, MaxHeight = 110, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _addWheel = new() { Content = "Add wheel control...", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _removeWheel = new() { Content = "Remove", Padding = new Thickness(12, 5, 12, 5), IsEnabled = false };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly List<ModeShortcut> _items;
    private readonly List<Rule> _wheel;
    private readonly Func<MapperSnapshot?> _latest;
    private readonly DeviceMap? _device;

    /// <summary>The profile's own keyboard and mouse list, or null when it uses the app-wide one. Read after the window returned true.</summary>
    public List<ModeShortcut>? Result { get; private set; }

    /// <summary>The wheel controls that switch modes, as rules. Read after the window returned true.</summary>
    public List<Rule> WheelRules { get; private set; } = [];

    public ProfileShortcutsWindow(Window owner, List<ModeShortcut>? current, int appWideCount, IEnumerable<Rule> wheelRules, Func<MapperSnapshot?> latest, DeviceMap? device)
    {
        Title = "Mode shortcuts for this profile";
        Owner = owner;
        Width = 500;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        SetResourceReference(BackgroundProperty, "WindowBack");
        SetResourceReference(ForegroundProperty, "TextPrimary");
        FontFamily = owner.FontFamily; FontSize = owner.FontSize;

        _latest = latest;
        _device = device;
        _items = current?.Select(m => new ModeShortcut { Trigger = m.Trigger, Action = m.Action }).ToList() ?? [];
        _wheel = wheelRules.ToList();

        var keyTitle = new TextBlock { Text = "Keyboard and mouse", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
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

        var wheelTitle = new TextBlock { Text = "Wheel buttons and pedals", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 0) };
        _wheelList.SelectionChanged += (_, _) => _removeWheel.IsEnabled = _wheelList.SelectedIndex >= 0;
        _addWheel.Click += (_, _) => AddWheel();
        _removeWheel.Click += (_, _) => { if (_wheelList.SelectedIndex >= 0 && _wheelList.SelectedIndex < _wheel.Count) { _wheel.RemoveAt(_wheelList.SelectedIndex); Refresh(); } };

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => { Result = _own.IsChecked == true ? _items : null; WheelRules = _wheel; DialogResult = true; };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Padding = new Thickness(10, 5, 10, 5) };

        var note = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 12, 0, 0),
            Text = "Games bind different keys, so pick ones this game does not use. Keyboard and mouse shortcuts work only while this profile is running; an empty list means none. "
                 + "Wheel controls are rules of this profile (they show in its rule list as sending to Mode:...), so they work in every mode.",
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        _message.SetResourceReference(TextBlock.ForegroundProperty, "Warn");

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                keyTitle, _appWide, _own, _list,
                new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 8, 0, 0), Children = { _add, _remove } },
                wheelTitle, _wheelList,
                new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Children = { _addWheel, _removeWheel } },
                _message, note,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), Children = { ok, cancel } },
            },
        };
        Refresh();
        UpdateEnabled();
    }

    /// <summary>Words for a rule that sends to Mode:..., such as "button19: next mode".</summary>
    internal static string DescribeWheelRule(Rule r)
    {
        string target = r.To.Trim();
        string what = target.Equals("Mode:Next", StringComparison.OrdinalIgnoreCase) ? "next mode"
            : target.Equals("Mode:Previous", StringComparison.OrdinalIgnoreCase) ? "previous mode"
            : $"the mode \"{(target.Length > 5 ? target[5..] : target)}\"";
        return $"{r.From}: {what}";
    }

    private void Refresh()
    {
        _list.ItemsSource = _items.Select(m => m.Describe()).ToList();
        _wheelList.ItemsSource = _wheel.Select(DescribeWheelRule).ToList();
        _remove.IsEnabled = _removeWheel.IsEnabled = false;
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

    private void AddWheel()
    {
        var dlg = new WheelControlWindow(this, _latest, _device);
        if (dlg.ShowDialog() != true || dlg.Result is not { } rule) return;
        if (_wheel.Any(r => string.Equals(r.From, rule.From, StringComparison.OrdinalIgnoreCase)))
        {
            _message.Text = $"{rule.From} already switches modes here. Remove it first to change what it does.";
            return;
        }
        _wheel.Add(rule);
        _message.Text = "";
        Refresh();
    }
}

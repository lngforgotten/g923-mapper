using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace G923Mapper.App;

/// <summary>
/// "Add a mode shortcut": choose what it does (next mode, previous mode, or a mode by name), then press the key or click the mouse button to use.
/// Built in code, like the other small question windows. What is pressed is read here with the window's own key and mouse events, so nothing is
/// watched system-wide while it is open.
/// </summary>
internal sealed class ModeShortcutWindow : Window
{
    private readonly ComboBox _action = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _name = new() { Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(6, 4, 6, 4), Visibility = Visibility.Collapsed };
    private readonly Border _capture;
    private readonly TextBlock _captured = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _problem = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button _ok = new() { Content = "Add", IsDefault = true, MinWidth = 80, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
    private string _trigger = "";

    /// <summary>The shortcut that was made, or null when the window was cancelled.</summary>
    public ModeShortcut? Result { get; private set; }

    public ModeShortcutWindow(Window owner)
    {
        Title = "Add a mode shortcut";
        Owner = owner;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        SetResourceReference(BackgroundProperty, "WindowBack");
        SetResourceReference(ForegroundProperty, "TextPrimary");
        FontFamily = owner.FontFamily; FontSize = owner.FontSize;

        _action.ItemsSource = new[] { "Next mode", "Previous mode", "A mode by name" };
        _action.SelectedIndex = 0;
        _action.SelectionChanged += (_, _) => { _name.Visibility = _action.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed; UpdateOk(); };
        _name.TextChanged += (_, _) => UpdateOk();

        _captured.Text = "Click here, then press a key or a mouse button";
        _captured.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        _capture = new Border
        {
            Height = 52, Margin = new Thickness(0, 4, 0, 0), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Focusable = true,
            Child = _captured, Cursor = Cursors.Hand,
        };
        _capture.SetResourceReference(Border.BorderBrushProperty, "PlotBorder");
        _capture.SetResourceReference(Border.BackgroundProperty, "PlotBack");
        _capture.PreviewKeyDown += Capture_KeyDown;
        _capture.PreviewMouseDown += Capture_MouseDown;
        _capture.GotKeyboardFocus += (_, _) => { if (_trigger.Length == 0) _captured.Text = "Now press a key or a mouse button"; };

        _ok.Click += (_, _) =>
        {
            Result = new ModeShortcut { Trigger = _trigger, Action = _action.SelectedIndex == 0 ? ModeShortcut.Next : _action.SelectedIndex == 1 ? ModeShortcut.Previous : _name.Text.Trim() };
            DialogResult = true;
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Padding = new Thickness(10, 5, 10, 5) };

        var note = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), FontSize = 12,
            Text = "Function keys, the number pad, the arrows, Insert/Delete/Home/End, and any key with Ctrl, Alt or Shift held work. The middle, back and forward mouse buttons work. "
                 + "Plain letters, digits and the left and right mouse buttons do not, because they would switch modes whenever you typed or clicked.",
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        _problem.SetResourceReference(TextBlock.ForegroundProperty, "Warn");

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = "What should it do?" }, _action, _name,
                new TextBlock { Text = "Which key or mouse button?", Margin = new Thickness(0, 12, 0, 0) }, _capture, _problem, note,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), Children = { _ok, cancel } },
            },
        };
        Loaded += (_, _) => _capture.Focus();
    }

    private void Capture_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _capture.Focus();
        MouseTriggerButton? button = e.ChangedButton switch
        {
            MouseButton.Middle => MouseTriggerButton.Middle,
            MouseButton.XButton1 => MouseTriggerButton.Back,
            MouseButton.XButton2 => MouseTriggerButton.Forward,
            _ => null,
        };
        if (button is null) { if (e.ChangedButton == MouseButton.Right) Reject("The right mouse button cannot be used: it would stop clicking working."); return; }
        Accept(InputTrigger.ForMouse(CurrentModifiers(), button.Value).ToString());
        e.Handled = true;
    }

    private void Capture_KeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key switch { Key.System => e.SystemKey, Key.ImeProcessed => e.ImeProcessedKey, _ => e.Key };
        e.Handled = true;                                                         // keep Alt from opening a menu, Tab from moving on
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None) return;   // wait for the key itself
        int vk = KeyInterop.VirtualKeyFromKey(key);
        string text = InputTrigger.ForKey(CurrentModifiers(), vk).ToString();
        if (InputTrigger.KeyName(vk) is null) { Reject($"{key} is not a key that can be used. Try a function key, the number pad, or a key with Ctrl, Alt or Shift."); return; }
        Accept(text);
    }

    private static HotkeyModifiers CurrentModifiers()
    {
        var m = HotkeyModifiers.None;
        ModifierKeys held = Keyboard.Modifiers;
        if (held.HasFlag(ModifierKeys.Control)) m |= HotkeyModifiers.Ctrl;
        if (held.HasFlag(ModifierKeys.Alt)) m |= HotkeyModifiers.Alt;
        if (held.HasFlag(ModifierKeys.Shift)) m |= HotkeyModifiers.Shift;
        if (held.HasFlag(ModifierKeys.Windows)) m |= HotkeyModifiers.Win;
        return m;
    }

    private void Accept(string text)
    {
        if (!InputTrigger.TryParse(text, out _, out string? why)) { Reject(why ?? "That cannot be used."); return; }
        _trigger = text;
        _captured.Text = text;
        _captured.SetResourceReference(TextBlock.ForegroundProperty, "Success");
        _problem.Text = "";
        UpdateOk();
    }

    private void Reject(string why)
    {
        _trigger = "";
        _captured.Text = "Press a key or a mouse button";
        _captured.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        _problem.Text = why;
        UpdateOk();
    }

    private void UpdateOk() => _ok.IsEnabled = _trigger.Length > 0 && (_action.SelectedIndex != 2 || _name.Text.Trim().Length > 0);
}


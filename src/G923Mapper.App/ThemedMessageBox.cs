using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace G923Mapper.App;

/// <summary>
/// The app's own pop-up message: the same calls as <see cref="MessageBox"/> (OK, OK and Cancel, Yes and No, Yes No and Cancel, an icon, a default
/// answer), drawn in the app's theme instead of plain Windows colours. Windows' message box cannot be themed. Where the theme is not available (the
/// very first moments of start-up, or a crash handler with no working window), it falls back to the Windows one rather than show nothing.
/// </summary>
internal static class ThemedMessageBox
{
    public static MessageBoxResult Show(string text) => Show(null, text, "", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);
    public static MessageBoxResult Show(string text, string caption) => Show(null, text, caption, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);
    public static MessageBoxResult Show(string text, string caption, MessageBoxButton buttons) => Show(null, text, caption, buttons, MessageBoxImage.None, MessageBoxResult.None);
    public static MessageBoxResult Show(string text, string caption, MessageBoxButton buttons, MessageBoxImage icon) => Show(null, text, caption, buttons, icon, MessageBoxResult.None);
    public static MessageBoxResult Show(string text, string caption, MessageBoxButton buttons, MessageBoxImage icon, MessageBoxResult defaultResult) => Show(null, text, caption, buttons, icon, defaultResult);
    public static MessageBoxResult Show(Window? owner, string text) => Show(owner, text, "", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);
    public static MessageBoxResult Show(Window? owner, string text, string caption) => Show(owner, text, caption, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);
    public static MessageBoxResult Show(Window? owner, string text, string caption, MessageBoxButton buttons) => Show(owner, text, caption, buttons, MessageBoxImage.None, MessageBoxResult.None);
    public static MessageBoxResult Show(Window? owner, string text, string caption, MessageBoxButton buttons, MessageBoxImage icon) => Show(owner, text, caption, buttons, icon, MessageBoxResult.None);

    public static MessageBoxResult Show(Window? owner, string text, string caption, MessageBoxButton buttons, MessageBoxImage icon, MessageBoxResult defaultResult)
    {
        Application? app = Application.Current;
        if (app is null || app.Dispatcher.HasShutdownStarted) return Native(owner, text, caption, buttons, icon, defaultResult);
        if (!app.Dispatcher.CheckAccess()) return app.Dispatcher.Invoke(() => Show(owner, text, caption, buttons, icon, defaultResult));
        if (app.TryFindResource("WindowBack") is null) return Native(owner, text, caption, buttons, icon, defaultResult);   // the theme is not loaded yet

        try
        {
            Window box = Build(owner, text, caption, buttons, icon, defaultResult, out Func<MessageBoxResult> result);
            box.ShowDialog();
            return result();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Native(owner, text, caption, buttons, icon, defaultResult);
        }
    }

    private static MessageBoxResult Native(Window? owner, string text, string caption, MessageBoxButton buttons, MessageBoxImage icon, MessageBoxResult defaultResult) =>
        owner is { IsLoaded: true }
            ? MessageBox.Show(owner, text, caption, buttons, icon, defaultResult)
            : MessageBox.Show(text, caption, buttons, icon, defaultResult);

    /// <summary>The answers a set of buttons offers, left to right, and which one Escape and the close box mean.</summary>
    internal static (MessageBoxResult[] Answers, MessageBoxResult Dismiss) AnswersFor(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.OKCancel => ([MessageBoxResult.OK, MessageBoxResult.Cancel], MessageBoxResult.Cancel),
        MessageBoxButton.YesNo => ([MessageBoxResult.Yes, MessageBoxResult.No], MessageBoxResult.No),
        MessageBoxButton.YesNoCancel => ([MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel], MessageBoxResult.Cancel),
        _ => ([MessageBoxResult.OK], MessageBoxResult.OK),
    };

    /// <summary>The answer that starts selected: the one asked for if the buttons include it, otherwise the first.</summary>
    internal static MessageBoxResult DefaultFor(MessageBoxResult[] answers, MessageBoxResult wanted) =>
        wanted != MessageBoxResult.None && answers.Contains(wanted) ? wanted : answers[0];

    private static string Label(MessageBoxResult r) => r switch { MessageBoxResult.OK => "OK", MessageBoxResult.Yes => "Yes", MessageBoxResult.No => "No", _ => "Cancel" };

    /// <summary>Builds the window without showing it, so it can be looked at and tested.</summary>
    internal static Window Build(Window? owner, string text, string caption, MessageBoxButton buttons, MessageBoxImage icon, MessageBoxResult defaultResult, out Func<MessageBoxResult> result)
    {
        var (answers, dismiss) = AnswersFor(buttons);
        MessageBoxResult chosen = dismiss;
        MessageBoxResult initial = DefaultFor(answers, defaultResult);

        bool hasOwner = owner is { IsLoaded: true, IsVisible: true };
        var window = new Window
        {
            Title = string.IsNullOrEmpty(caption) ? "G923 Mapper" : caption,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 360, MaxWidth = 560,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = hasOwner ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ShowInTaskbar = !hasOwner,
            Topmost = !hasOwner,                                   // with no window to sit on (the app is in the tray) it must not hide behind others
        };
        if (hasOwner) { window.Owner = owner; window.FontFamily = owner!.FontFamily; window.FontSize = owner.FontSize; }
        else { window.FontFamily = new FontFamily("Segoe UI"); window.FontSize = 13; }
        window.SetResourceReference(Control.BackgroundProperty, "WindowBack");
        window.SetResourceReference(Control.ForegroundProperty, "TextPrimary");

        // The icon: a coloured disc with a letter, which needs no pictures and follows the theme.
        (string glyph, string brush)? mark = icon switch
        {
            MessageBoxImage.Error => ("x", "Danger"),
            MessageBoxImage.Warning => ("!", "Warn"),
            MessageBoxImage.Question => ("?", "Accent"),
            MessageBoxImage.Information => ("i", "Accent"),
            _ => null,
        };

        var body = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 440, VerticalAlignment = VerticalAlignment.Center };
        body.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 20, 20, 8) };
        if (mark is { } m)
        {
            var disc = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Top };
            disc.SetResourceReference(Border.BackgroundProperty, m.brush);
            var letter = new TextBlock { Text = m.glyph, FontWeight = FontWeights.Bold, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White };
            disc.Child = letter;
            row.Children.Add(disc);
        }
        row.Children.Add(body);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 12, 20, 18) };
        foreach (MessageBoxResult answer in answers)
        {
            bool isDefault = answer == initial;
            var button = new Button { Content = Label(answer), MinWidth = 84, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0), IsDefault = isDefault, IsCancel = answer == dismiss, Tag = answer };
            button.SetResourceReference(FrameworkElement.StyleProperty, isDefault ? "Primary" : "Secondary");
            button.Click += (_, _) => { chosen = answer; window.DialogResult = true; };
            bar.Children.Add(button);
        }

        window.Content = new StackPanel { Children = { row, bar } };
        window.Loaded += (_, _) =>
        {
            window.Activate();
            (bar.Children.OfType<Button>().FirstOrDefault(b => b.IsDefault) ?? bar.Children.OfType<Button>().First()).Focus();
        };
        result = () => chosen;
        return window;
    }
}

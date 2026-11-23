using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace G923Mapper.App;

public partial class ButtonMapWindow : Window
{
    private static Brush Idle => ThemeManager.Brush("CellBack");
    private static Brush IdleBorder => ThemeManager.Brush("CellBorder");
    private static Brush Lit => ThemeManager.Brush("CellLit");
    private static Brush LitBorder => ThemeManager.Brush("CellLitBorder");
    private static Brush Bad => ThemeManager.Danger;
    private static Brush Muted => ThemeManager.Muted;

    private sealed class Cell
    {
        public int Number;
        public Border Border = null!;
        public TextBox Box = null!;
        public bool WasDown;
        public bool HasError;
    }

    private readonly DeviceMap _device;
    private readonly Func<RawInput?> _latest;
    private readonly Func<IEnumerable<(string Key, Profile Profile)>> _profiles;
    private readonly string _devicesPath;
    private readonly List<Cell> _cells = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(30) };
    private bool _building = true;

    public ButtonMapWindow(DeviceMap device, Func<RawInput?> latest, Func<IEnumerable<(string Key, Profile Profile)>> profiles, string devicesPath)
    {
        InitializeComponent();
        _device = device;
        _latest = latest;
        _profiles = profiles;
        _devicesPath = devicesPath;
        Title = $"Name wheel buttons ({device.Name})";

        int count = _latest() is { ButtonCount: > 0 } raw ? raw.ButtonCount : 23;
        var byNumber = device.Buttons.GroupBy(b => b.Value).ToDictionary(g => g.Key, g => g.First().Key);

        for (int n = 1; n <= count; n++)
        {
            var number = new TextBlock { Text = n.ToString(), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            number.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            var box = new TextBox { Text = byNumber.TryGetValue(n, out string? name) ? name : "", Padding = new Thickness(5, 3, 5, 3), Width = 108 };
            var border = new Border
            {
                Child = new StackPanel { Children = { number, box } },
                Background = Idle, BorderBrush = IdleBorder, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8), Margin = new Thickness(0, 0, 10, 10),
            };
            var cell = new Cell { Number = n, Border = border, Box = box };
            box.TextChanged += (_, _) => { if (!_building) Validate(); };
            _cells.Add(cell);
        }
        Cells.ItemsSource = _cells.Select(c => c.Border).ToList();

        _building = false;
        Validate();
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
        Closed += (_, _) => { _timer.Stop(); ThemeManager.Changed -= OnTheme; };
        ThemeManager.Changed += OnTheme;
    }

    private void OnTheme() => Dispatcher.BeginInvoke(() => Validate());

    private static Brush Frozen(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    /// <summary>Light up pressed buttons; a fresh press selects that button's name box.</summary>
    private void Poll()
    {
        RawInput? raw = _latest();
        foreach (Cell c in _cells)
        {
            bool down = raw is { Live: true } && c.Number - 1 < raw.Buttons.Length && raw.Buttons[c.Number - 1];
            if (down != c.WasDown)
            {
                if (down)
                {
                    c.Box.Focus();
                    c.Box.SelectAll();
                    BringIntoView(c);
                }
                c.WasDown = down;
            }
            Paint(c, down);
        }
    }

    private void BringIntoView(Cell c) => c.Border.BringIntoView();

    private static void Paint(Cell c, bool down)
    {
        c.Border.Background = down ? Lit : Idle;
        c.Border.BorderBrush = c.HasError ? Bad : down ? LitBorder : IdleBorder;
    }

    private Dictionary<string, int> Proposed() =>
        _cells.Where(c => c.Box.Text.Trim().Length > 0)
              .GroupBy(c => c.Box.Text.Trim(), StringComparer.OrdinalIgnoreCase)
              .ToDictionary(g => g.Key, g => g.First().Number, StringComparer.OrdinalIgnoreCase);

    private bool Validate()
    {
        var proposed = Proposed();
        string? first = null;
        foreach (Cell c in _cells)
        {
            string name = c.Box.Text.Trim();
            string? error = name.Length == 0 ? null : ButtonNaming.Validate(name, _device, proposed, c.Number);

            // Two boxes with the same name collapse to one entry in <proposed>; flag the later ones.
            if (error is null && name.Length > 0 && proposed.TryGetValue(name, out int owner) && owner != c.Number)
                error = $"\"{name}\" is already used for button {owner}.";

            c.HasError = error is not null;
            c.Box.ToolTip = error;
            first ??= error is null ? null : $"Button {c.Number}: {error}";
            Paint(c, c.WasDown);
        }
        Message.Text = first ?? "Leave a box empty for no name.";
        Message.Foreground = first is null ? Muted : Bad;
        SaveButton.IsEnabled = first is null;
        return first is null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate()) return;
        var newNames = new Dictionary<string, int>(Proposed(), StringComparer.OrdinalIgnoreCase);

        // Warn before breaking rules that use a name that is going away.
        var removed = ButtonNaming.Removed(_device.Buttons, newNames);
        var affected = _profiles()
            .Where(p => string.Equals(p.Profile.Device, _device.Name, StringComparison.OrdinalIgnoreCase))
            .Select(p => (p.Key, Names: p.Profile.Rules.Select(r => r.From).Where(f => removed.Contains(f, StringComparer.OrdinalIgnoreCase)).Distinct().ToList()))
            .Where(p => p.Names.Count > 0).ToList();
        if (affected.Count > 0)
        {
            string list = string.Join("\n", affected.Select(a => $"  {a.Key}: {string.Join(", ", a.Names)}"));
            var answer = MessageBox.Show(this,
                $"These profiles use button names you are removing or changing:\n\n{list}\n\nThose rules will stop working until you update them. Continue?",
                "Name buttons", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
        }

        try
        {
            // Swap the whole dictionary so the running mapper never sees a half-edited one.
            _device.Buttons = newNames;
            DeviceRegistry.SaveEntry(_devicesPath, _device);
            DialogResult = true;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not save {_devicesPath}:\n{ex.Message}", "Name buttons", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

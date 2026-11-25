using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Vortice.DirectInput;

namespace G923Mapper.App;

public partial class ExtraDeviceWindow : Window
{
    private sealed class AxisRow
    {
        public int Index;
        public int Min, Max;
        public CentreBar Bar = null!;
        public TextBlock Range = null!;
        public TextBox Name = null!;
        public CheckBox Invert = null!, Centred = null!;
    }

    private sealed class ButtonCell
    {
        public int Number;
        public Border Border = null!;
        public TextBox Box = null!;
        public bool WasDown, EverDown;
    }

    private const int MovedThreshold = 8000;

    private readonly AppServices _s;
    private readonly IDirectInput8 _di = DInput.DirectInput8Create();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly List<DeviceInstance> _candidates = new();
    private readonly Dictionary<int, AxisRow> _axes = new();
    private readonly List<ButtonCell> _cells = new();
    private DirectInputWheel? _wheel;
    private RawInput? _baseline;
    private int[] _min = new int[8], _max = new int[8];
    private bool _building;

    /// <summary>The device that was saved, if the user pressed Save.</summary>
    public DeviceMap? Saved { get; private set; }

    public ExtraDeviceWindow(AppServices services)
    {
        InitializeComponent();
        _s = services;

        // Everything attached except the wheel itself and the virtual Xbox 360 pad this app creates.
        var primaryIds = _s.Devices.All.Where(d => !d.Extra && d.UsbIds is not null).Select(d => d.UsbIds!.Value).ToHashSet();
        foreach (DeviceInstance d in _di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
        {
            var ids = DirectInputWheel.Ids(d);
            if (primaryIds.Contains(ids) || ids == (0x045E, 0x028E)) continue;
            _candidates.Add(d);
        }

        DeviceBox.ItemsSource = _candidates.Select(d => d.ProductName).ToList();
        _timer.Tick += (_, _) => Poll();
        Closed += (_, _) => { _timer.Stop(); _wheel?.Dispose(); _di.Dispose(); };
        ThemeManager.Changed += OnTheme;
        Closed += (_, _) => ThemeManager.Changed -= OnTheme;

        if (_candidates.Count == 0)
        {
            WakeHint.Text = "No other game controllers are plugged in. Connect the device (a handbrake, shifter or pedals) and open this window again.";
            SaveButton.IsEnabled = false;
        }
        else DeviceBox.SelectedIndex = 0;
    }

    private void OnTheme() => Dispatcher.BeginInvoke(() => { foreach (var c in _cells) Paint(c, c.WasDown); Validate(); });

    // ---------------------------------------------------------------- choosing the device

    private void DeviceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceBox.SelectedIndex < 0 || DeviceBox.SelectedIndex >= _candidates.Count) return;
        DeviceInstance inst = _candidates[DeviceBox.SelectedIndex];

        _timer.Stop();
        _wheel?.Dispose();
        _wheel = null;
        _baseline = null;
        _axes.Clear();
        _cells.Clear();
        AxisPanel.Children.Clear();
        ButtonPanel.Children.Clear();
        _min = new int[8]; _max = new int[8];

        // Re-opening a device that is already set up keeps its name.
        var ids = DirectInputWheel.Ids(inst);
        DeviceMap? existing = _s.Devices.FindByUsb(ids.vid, ids.pid, includeExtra: true);
        _building = true;
        NameBox.Text = existing is { Extra: true } ? existing.Name : SuggestName(inst.ProductName);
        _building = false;

        try { _wheel = DirectInputWheel.Open(_di, inst, _s.Hwnd()); }
        catch (SharpGen.Runtime.SharpGenException ex)
        {
            WakeHint.Text = "Could not open that device: " + ex.Message;
            return;
        }
        WakeHint.Text = "Move or press something on the device to wake it.";
        AxesEmpty.Visibility = Visibility.Visible;
        _timer.Start();
        Validate();
    }

    /// <summary>A short, valid starting name from the product name ("USB Handbrake" gives handbrake).</summary>
    public static string SuggestName(string productName)
    {
        string last = Regex.Matches(productName, "[A-Za-z][A-Za-z0-9]*").Select(m => m.Value).LastOrDefault() ?? "";
        string name = last.Length > 0 ? last.ToLowerInvariant() : "device";
        return name.Length > 24 ? name[..24] : name;
    }

    // ---------------------------------------------------------------- live reading

    private void Poll()
    {
        if (_wheel is null) return;
        RawInput raw = _wheel.Read();
        if (!raw.Live) return;

        if (_baseline is null)
        {
            _baseline = new RawInput { Axes = (int[])raw.Axes.Clone(), Buttons = (bool[])raw.Buttons.Clone(), Live = true };
            Array.Copy(raw.Axes, _min, 8);
            Array.Copy(raw.Axes, _max, 8);
            BuildButtonCells(raw.ButtonCount > 0 ? raw.ButtonCount : 0);
            WakeHint.Text = "Now move each control through its full range, and press its buttons.";
        }

        for (int a = 0; a < 8; a++)
        {
            int v = raw.Axes[a];
            _min[a] = Math.Min(_min[a], v);
            _max[a] = Math.Max(_max[a], v);
            if (!_axes.ContainsKey(a) && Math.Abs(v - _baseline.Axes[a]) >= MovedThreshold) AddAxisRow(a);
            if (_axes.TryGetValue(a, out AxisRow? row))
            {
                row.Min = _min[a]; row.Max = _max[a];
                row.Bar.Value = v / 65535.0;
                row.Range.Text = $"{row.Min} to {row.Max}";
            }
        }

        foreach (ButtonCell c in _cells)
        {
            bool down = c.Number - 1 < raw.Buttons.Length && raw.Buttons[c.Number - 1];
            if (down && !c.WasDown) { c.Box.Focus(); c.Box.SelectAll(); c.EverDown = true; }
            c.WasDown = down;
            Paint(c, down);
        }
    }

    private void AddAxisRow(int index)
    {
        AxesEmpty.Visibility = Visibility.Collapsed;
        var row = new AxisRow { Index = index, Min = _min[index], Max = _max[index] };
        row.Bar = new CentreBar { Width = 170, Height = 14, VerticalAlignment = VerticalAlignment.Center };
        row.Range = new TextBlock { Width = 100, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas"), FontSize = 12 };
        row.Range.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        row.Name = new TextBox { Width = 120, Padding = new Thickness(5, 3, 5, 3), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Name this control to use it in profiles. Leave empty to ignore it." };
        row.Invert = new CheckBox { Content = "Invert", Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Tick if the value falls when you pull or press." };
        row.Centred = new CheckBox { Content = "Centred", Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Tick for a control with a middle position (like a steering wheel), so it reads -1 to 1 instead of 0 to 1." };
        row.Name.TextChanged += (_, _) => { if (!_building) Validate(); };

        var label = new TextBlock { Text = AxisDef.SourceNames[index], Width = 62, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        foreach (UIElement el in new UIElement[] { label, row.Bar, row.Range, row.Name, row.Invert, row.Centred }) panel.Children.Add(el);
        AxisPanel.Children.Add(panel);
        _axes[index] = row;
        Validate();
    }

    private void BuildButtonCells(int count)
    {
        _building = true;
        ButtonsEmpty.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        for (int n = 1; n <= count; n++)
        {
            var number = new TextBlock { Text = n.ToString(), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            number.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            var box = new TextBox { Padding = new Thickness(5, 3, 5, 3), Width = 96 };
            var border = new Border
            {
                Child = new StackPanel { Children = { number, box } }, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8), Margin = new Thickness(0, 0, 8, 8),
            };
            var cell = new ButtonCell { Number = n, Border = border, Box = box };
            box.TextChanged += (_, _) => { if (!_building) Validate(); };
            Paint(cell, false);
            _cells.Add(cell);
            ButtonPanel.Children.Add(border);
        }
        _building = false;
    }

    private static void Paint(ButtonCell c, bool down)
    {
        c.Border.Background = down ? ThemeManager.Brush("CellLit") : ThemeManager.Brush("CellBack");
        c.Border.BorderBrush = down ? ThemeManager.Brush("CellLitBorder") : ThemeManager.Brush("CellBorder");
    }

    // ---------------------------------------------------------------- checking and saving

    private void Anything_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_building) Validate();
    }

    private static bool Reserved(string name) =>
        DeviceMap.TryParseButtonN(name, out _) || DeviceMap.HatNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Why saving is not possible yet (null when it is).</summary>
    private string? Problem()
    {
        if (_wheel is null) return "Choose a device.";

        string name = NameBox.Text.Trim();
        DeviceInstance inst = _candidates[DeviceBox.SelectedIndex];
        var ids = DirectInputWheel.Ids(inst);
        string? existing = _s.Devices.FindByUsb(ids.vid, ids.pid, includeExtra: true) is { Extra: true } e ? e.Name : null;
        if (ExtraDeviceNaming.Validate(name, _s.Devices, existing) is string nameProblem) return "Name: " + nameProblem;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AxisRow r in _axes.Values)
        {
            string n = r.Name.Text.Trim();
            if (n.Length == 0) continue;
            if (ExtraDeviceNaming.ValidateInputName(n) is string p) return $"{AxisDef.SourceNames[r.Index]}: {p}";
            if (Reserved(n)) return $"\"{n}\" is reserved; choose another name.";
            if (!used.Add(n)) return $"\"{n}\" is used twice.";
            if (r.Max - r.Min < 2000) return $"Move {AxisDef.SourceNames[r.Index]} through its whole range first.";
        }
        foreach (ButtonCell c in _cells)
        {
            string n = c.Box.Text.Trim();
            if (n.Length == 0) continue;
            if (ExtraDeviceNaming.ValidateInputName(n) is string p) return $"Button {c.Number}: {p}";
            if (Reserved(n)) return $"\"{n}\" is reserved; choose another name.";
            if (!used.Add(n)) return $"\"{n}\" is used twice.";
        }

        bool anything = _axes.Values.Any(r => r.Name.Text.Trim().Length > 0) || _cells.Any(c => c.Box.Text.Trim().Length > 0 || c.EverDown);
        return anything ? null : "Move or press something on the device first.";
    }

    private void Validate()
    {
        if (_candidates.Count == 0) return;
        string? problem = Problem();
        Message.Text = problem ?? "Ready to save. Unnamed buttons still work as button1, button2 and so on.";
        Message.Foreground = problem is null ? ThemeManager.Muted : ThemeManager.Warn;
        SaveButton.IsEnabled = problem is null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Problem() is not null || _wheel is null) { Validate(); return; }

        var ids = DirectInputWheel.Ids(_candidates[DeviceBox.SelectedIndex]);
        var map = new DeviceMap { Name = NameBox.Text.Trim(), Vid = ids.vid.ToString("X4"), Pid = ids.pid.ToString("X4"), Extra = true };
        foreach (AxisRow r in _axes.Values)
        {
            string n = r.Name.Text.Trim();
            if (n.Length == 0) continue;
            map.Axes[n] = new AxisDef
            {
                Source = AxisDef.SourceNames[r.Index], Signed = r.Centred.IsChecked == true, Invert = r.Invert.IsChecked == true,
                Min = r.Min, Max = r.Max, Centre = (r.Min + r.Max) / 2,
            };
        }
        foreach (ButtonCell c in _cells)
        {
            string n = c.Box.Text.Trim();
            if (n.Length > 0) map.Buttons[n] = c.Number;
        }

        try
        {
            DeviceRegistry.SaveEntry(_s.DevicesPath, map);
            Saved = map;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            ThemedMessageBox.Show(this, $"Could not save {_s.DevicesPath}:\n{ex.Message}", "Add a device", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}


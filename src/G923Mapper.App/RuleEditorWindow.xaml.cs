using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace G923Mapper.App;

public partial class RuleEditorWindow : Window
{
    private static Brush Red => ThemeManager.Danger;
    private static Brush Amber => ThemeManager.Warn;
    private static Brush Green => ThemeManager.Success;

    private sealed class RuleRow(Rule rule) : INotifyPropertyChanged
    {
        public Rule Rule { get; } = rule;
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));

        public string Summary
        {
            get
            {
                var r = Rule;
                string head = $"{(r.From.Length == 0 ? "(input)" : r.From)}  →  {(r.To.Length == 0 ? "(output)" : r.To)}";
                var bits = new List<string>();
                if (!r.Curve.Equals("linear", StringComparison.OrdinalIgnoreCase)) bits.Add(r.Curve.ToLowerInvariant());
                if (r.Deadzone != 0) bits.Add($"dz {r.Deadzone * 100:0.##}%");
                if (r.AntiDeadzone != 0) bits.Add($"anti {r.AntiDeadzone * 100:0.##}%");
                if (r.Range is double range) bits.Add($"{range:0.#}°");
                if (r.Invert) bits.Add("inverted");
                if (r.Smooth > 0) bits.Add($"smooth {r.Smooth} ms");
                if (r.Threshold is double t) bits.Add($"at {t * 100:0.##}%");
                if (!r.Mode.Equals("hold", StringComparison.OrdinalIgnoreCase)) bits.Add(r.Mode.ToLowerInvariant());
                if (!string.IsNullOrWhiteSpace(r.Part) && !r.Part.Equals("full", StringComparison.OrdinalIgnoreCase)) bits.Add(r.Part.ToLowerInvariant() + " half");
                if (!string.IsNullOrWhiteSpace(r.Expr)) bits.Add($"= {r.Expr}");
                if (r.Turbo) bits.Add($"turbo {r.TurboHz:0.#}/s");
                return bits.Count == 0 ? head : head + "\n" + string.Join(" · ", bits);
            }
        }
    }

    private readonly DeviceRegistry _devices;
    private readonly string _path;
    private readonly Func<MapperSnapshot?> _latest;
    private readonly System.Windows.Threading.DispatcherTimer _learnTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private MapperSnapshot? _learnBaseline;
    private DateTime _learnUntil;
    private readonly ObservableCollection<RuleRow> _rows = new();   // the profile's own rules

    /// <summary>One mode as it is being edited: its own rules, the profile rules it turns off, and its feel.</summary>
    private sealed class EditorMode
    {
        public string Name = "";
        public ObservableCollection<RuleRow> Rows = new();
        public List<string> Removed = new();
        public ProfileForce? Force;
    }

    private readonly List<EditorMode> _modes = new();
    private int _view = -1;               // -1 = the profile's own rules and feel; otherwise which mode is shown
    private ProfileForce? _profileForce;  // the profile's feel, kept while a mode is shown
    private ObservableCollection<RuleRow> Rows => _view >= 0 && _view < _modes.Count ? _modes[_view].Rows : _rows;
    private readonly List<string> _fieldErrors = new();
    private bool _populating;
    private string _lastJson = "";
    private bool _valid;
    private readonly AppSettings? _settings;
    private readonly Func<string, Profile?>? _findTemplate;
    private bool _isTemplate;
    private ParentLink? _parent;

    public bool Saved { get; private set; }

    /// <param name="latest">The mapper's latest snapshot, used by the Learn button; may return null when the mapper is stopped.</param>
    public RuleEditorWindow(Profile profile, DeviceRegistry devices, string path, Func<MapperSnapshot?> latest, AppSettings? settings = null, Func<string, Profile?>? findTemplate = null)
    {
        InitializeComponent();
        _settings = settings;
        _findTemplate = findTemplate;
        _devices = devices;
        _path = path;
        _latest = latest;
        _learnTimer.Tick += (_, _) => LearnTick();
        _liveTimer.Tick += (_, _) => LiveTick();
        _liveTimer.Start();
        Closed += (_, _) => { _learnTimer.Stop(); _liveTimer.Stop(); ThemeManager.Changed -= OnTheme; };
        ThemeManager.Changed += OnTheme;

        // Work on a copy so Cancel leaves the running profile untouched.
        Profile copy = ProfileLoader.Parse(ProfileWriter.ToJson(profile), devices).Profile ?? profile;
        Title = $"Edit rules: {copy.Name}";
        _isTemplate = copy.IsTemplate;
        _parent = copy.Parent;

        CurveBox.ItemsSource = ProfileLoader.Curves;
        ModeBox.ItemsSource = ProfileLoader.Modes;
        PartBox.ItemsSource = new[] { "Whole axis", "Positive half", "Negative half" };
        ToBox.ItemsSource = ToChoices(copy.Outputs.SecondPad);
        DeviceBox.ItemsSource = new[] { DeviceRegistry.AutoDevice }.Concat(devices.Names.Where(n => !DeviceRegistry.IsAuto(n))).ToList();   // "auto" = whichever wheel is connected
        // Each percentage has a slider and a box that follow each other; the box still takes an exact value.
        Preview.PointsEdited += pts => { if (Current is not null) PointsBox.Text = CurveText.Format(pts); };   // dragging a point rewrites the points text
        LinkSlider(DeadzoneBox, DeadzoneSlider);
        LinkSlider(AntiBox, AntiSlider);
        LinkSlider(SmoothBox, SmoothSlider);
        LinkSlider(ThresholdBox, ThresholdSlider);
        FromBox.AddHandler(TextBoxBase.TextChangedEvent, new RoutedEventHandler(Form_Changed));
        ToBox.AddHandler(TextBoxBase.TextChangedEvent, new RoutedEventHandler(Form_Changed));

        _populating = true;
        NameBox.Text = copy.Name;
        NotesBox.Text = copy.Notes ?? "";
        DeviceBox.SelectedItem = ((IEnumerable<string>)DeviceBox.ItemsSource).FirstOrDefault(n => n.Equals(copy.Device, StringComparison.OrdinalIgnoreCase)) ?? copy.Device;
        ExeBox.Text = string.Join(", ", copy.Match.Exe);
        HideWheelBox.ItemsSource = new[] { "Leave the wheel as it is", "Hide the wheel while its game runs", "Show the wheel while its game runs" };
        HideWheelBox.SelectedIndex = copy.HideWheel is true ? 1 : copy.HideWheel is false ? 2 : 0;
        FeelMotors.ItemsSource = new[] { "Both motors", "Heavy only", "Light only" };
        FeelBox.IsChecked = copy.Force is { IsEmpty: false };
        FeelRumble.Value = copy.Force?.Rumble ?? 50;
        FeelSpring.Value = copy.Force?.Spring ?? 0;
        FeelFriction.Value = copy.Force?.Friction ?? 0;
        FeelMotors.SelectedIndex = string.Equals(copy.Force?.Motors, ForceMath.MotorsHeavy, StringComparison.OrdinalIgnoreCase) ? 1
            : string.Equals(copy.Force?.Motors, ForceMath.MotorsLight, StringComparison.OrdinalIgnoreCase) ? 2 : 0;
        PadBox.ItemsSource = new[] { "Xbox 360", "DualShock 4" };
        PadBox.SelectedItem = PadKinds.Of(copy) == PadKind.DualShock4 ? "DualShock 4" : "Xbox 360";
        SecondPadBox.IsChecked = copy.Outputs.SecondPad;
        SafetyBox.ItemsSource = new[] { "Strict", "Standard" };
        SafetyBox.SelectedItem = ProfileSafety.Effective(copy) == ProfileSafety.Standard ? "Standard" : "Strict";
        _safetyBefore = SafetyBox.SelectedItem as string ?? "Strict";
        KeyboardBox.IsChecked = copy.Outputs.Keyboard;
        UpdateSafetyUi();
        _populating = false;
        RefreshFromList();

        foreach (Rule r in copy.Rules) _rows.Add(new RuleRow(r));
        _profileForce = copy.Force;
        foreach (ProfileMode m in copy.Modes)
        {
            var em = new EditorMode { Name = m.Name, Removed = (m.RemovedRules ?? []).ToList(), Force = m.Force };
            foreach (Rule r in m.Rules) em.Rows.Add(new RuleRow(r));
            _modes.Add(em);
        }
        ToBox.ItemsSource = ToChoices(copy.Outputs.SecondPad, ModeNames);
        RuleList.ItemsSource = _rows;
        if (_rows.Count > 0) RuleList.SelectedIndex = 0; else LoadRule(null);
        RefreshViewBox(-1);
        Revalidate();
    }

    private bool _syncing;

    /// <summary>Keeps a percentage slider and its text box in step, in both directions, without either one re-triggering the other.</summary>
    private void LinkSlider(TextBox box, Slider slider)
    {
        box.TextChanged += (_, _) =>
        {
            if (_syncing) return;
            string text = box.Text.Trim().TrimEnd('%').Trim();
            _syncing = true;
            slider.Value = TryNumber(text, out double v) ? Math.Clamp(Math.Round(v), slider.Minimum, slider.Maximum) : (text.Length == 0 ? 0 : slider.Value);
            _syncing = false;
        };
        slider.ValueChanged += (_, e) =>
        {
            if (_syncing || _populating) return;
            _syncing = true;
            box.Text = e.NewValue == 0 && slider.Minimum == 0 && string.IsNullOrEmpty(box.Text) ? "" : e.NewValue.ToString("0", CultureInfo.InvariantCulture);
            _syncing = false;
        };
    }

    /// <summary>Everything the "To output" box offers; the P2: ones (the second virtual controller) only when that is switched on.</summary>
    private IEnumerable<string> ModeNames => _modes.Select(m => m.Name);

    private static List<string> ToChoices(bool secondPad, IEnumerable<string>? modeNames = null)
    {
        var list = Enum.GetNames<PadAxis>()
            .Concat(Enum.GetNames<PadButton>().Select(b => "Button:" + b))
            .Concat(KeyNames.AllKeyNames.Select(k => "Key:" + k))
            .Concat(KeyNames.MouseButtonNames).ToList();
        if (secondPad)
            list.AddRange(Enum.GetNames<PadAxis>().Select(a => "P2:" + a).Concat(Enum.GetNames<PadButton>().Select(b => "P2:Button:" + b)));
        list.Add("Mode:Next");
        list.Add("Mode:Previous");
        list.AddRange((modeNames ?? []).Select(n => "Mode:" + n));
        return list;
    }

    private void SecondPad_Changed(object sender, RoutedEventArgs e)
    {
        if (_populating) return;
        string text = ToBox.Text;
        _populating = true;
        ToBox.ItemsSource = ToChoices(SecondPadBox.IsChecked == true, ModeNames);
        ToBox.Text = text;
        _populating = false;
        Revalidate();
    }

    private void OnTheme() => Dispatcher.BeginInvoke(Revalidate);

    private static Brush Frozen(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    private RuleRow? Current => RuleList.SelectedItem as RuleRow;
    private DeviceMap? Device => DeviceBox.SelectedItem is string name ? _devices.Resolve(name, null) : null;

    // ---------------------------------------------------------------- lists

    private void RefreshFromList()
    {
        var names = new List<string>();
        if (Device is { } d)
        {
            names.AddRange(d.Axes.Keys);
            names.AddRange(d.Buttons.Keys);
            names.AddRange(DeviceMap.HatNames);
            for (int i = 1; i <= 24; i++) names.Add("button" + i);
        }
        foreach (DeviceMap x in _devices.ExtraDevices)
        {
            names.AddRange(x.Axes.Keys.Select(k => $"{x.Name}.{k}"));
            names.AddRange(x.Buttons.Keys.Select(k => $"{x.Name}.{k}"));
            for (int i = 1; i <= 16; i++) names.Add($"{x.Name}.button{i}");
        }
        string current = FromBox.Text;
        _populating = true;
        FromBox.ItemsSource = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        FromBox.Text = current;
        _populating = false;
    }

    private void RuleList_SelectionChanged(object sender, SelectionChangedEventArgs e) => LoadRule(Current);

    private void LoadRule(RuleRow? row)
    {
        bool has = row is not null;
        DuplicateButton.IsEnabled = DeleteButton.IsEnabled = has;
        FormRoot.IsEnabled = has;
        _populating = true;
        Rule r = row?.Rule ?? new Rule();
        FromBox.Text = r.From;
        ToBox.Text = r.To;
        CurveBox.SelectedItem = ProfileLoader.Curves.FirstOrDefault(c => c.Equals(r.Curve, StringComparison.OrdinalIgnoreCase)) ?? "linear";
        ExpBox.Text = Fmt(r.Exponent);
        PointsBox.Text = CurveText.Format(r.Points);
        DeadzoneBox.Text = r.Deadzone == 0 ? "" : Fmt(r.Deadzone * 100);
        AntiBox.Text = r.AntiDeadzone == 0 ? "" : Fmt(r.AntiDeadzone * 100);
        RangeBox.Text = r.Range is double range ? Fmt(range) : "";
        InvertBox.IsChecked = r.Invert;
        ThresholdBox.Text = r.Threshold is double t ? Fmt(t * 100) : "";
        ModeBox.SelectedItem = ProfileLoader.Modes.FirstOrDefault(m => m.Equals(r.Mode, StringComparison.OrdinalIgnoreCase)) ?? "hold";
        PulseBox.Text = r.PulseMs.ToString(CultureInfo.InvariantCulture);
        PartBox.SelectedIndex = string.Equals(r.Part, "positive", StringComparison.OrdinalIgnoreCase) ? 1 : string.Equals(r.Part, "negative", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
        SmoothBox.Text = r.Smooth == 0 ? "" : r.Smooth.ToString(CultureInfo.InvariantCulture);
        ExprBox.Text = r.Expr ?? "";
        TurboBox.IsChecked = r.Turbo;
        TurboHzBox.Text = Fmt(r.TurboHz);
        _populating = false;
        _fieldErrors.Clear();
        UpdateVisibility();
        UpdatePreview();
        Revalidate();
    }

    private static string Fmt(double d) => d.ToString("0.######", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- reading the form

    private void Form_Changed(object sender, RoutedEventArgs e)
    {
        if (_populating || Current is null) return;
        // Choosing "custom" starts from a straight line so there is something to drag.
        if ((CurveBox.SelectedItem as string) == "custom" && string.IsNullOrWhiteSpace(PointsBox.Text)) PointsBox.Text = CurveText.Format(CurvePoints.StraightLine());
        ReadForm(Current.Rule);
        Current.Refresh();
        UpdateVisibility();
        UpdatePreview();
        Revalidate();
    }

    private void ReadForm(Rule r)
    {
        _fieldErrors.Clear();
        r.From = FromBox.Text.Trim();
        r.To = ToBox.Text.Trim();
        r.Curve = CurveBox.SelectedItem as string ?? "linear";
        r.Mode = ModeBox.SelectedItem as string ?? "hold";
        r.Invert = InvertBox.IsChecked == true;

        r.Exponent = ReadNumber(ExpBox, "Exponent", r.Exponent, blank: 2.0) ?? r.Exponent;
        r.Deadzone = ReadPercent(DeadzoneBox, "Deadzone", r.Deadzone, blank: 0) ?? r.Deadzone;
        r.AntiDeadzone = ReadPercent(AntiBox, "Anti-deadzone", r.AntiDeadzone, blank: 0) ?? r.AntiDeadzone;

        r.Range = ReadOptional(RangeBox, "Wheel range", r.Range);
        r.Threshold = ReadOptionalPercent(ThresholdBox, "Press at", r.Threshold);

        r.Smooth = (int)Math.Round(ReadNumber(SmoothBox, "Smoothing", r.Smooth, blank: 0) ?? r.Smooth);
        r.Part = PartBox.SelectedIndex switch { 1 => "positive", 2 => "negative", _ => null };
        r.Expr = ExprBox.Text.Trim().Length == 0 ? null : ExprBox.Text.Trim();
        r.Turbo = TurboBox.IsChecked == true;
        r.TurboHz = ReadNumber(TurboHzBox, "Turbo rate", r.TurboHz, blank: 10) ?? r.TurboHz;

        string pulse = PulseBox.Text.Trim();
        if (pulse.Length == 0) r.PulseMs = 50;
        else if (int.TryParse(pulse, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ms)) { r.PulseMs = ms; Mark(PulseBox, true); }
        else { _fieldErrors.Add("Pulse length must be a whole number of milliseconds."); Mark(PulseBox, false); }

        if (CurveText.TryParse(PointsBox.Text, out var pts, out string? err)) { r.Points = pts; Mark(PointsBox, true); }
        else { _fieldErrors.Add("Points: " + err); Mark(PointsBox, false); }
    }

    private double? ReadNumber(TextBox box, string label, double current, double blank)
    {
        string text = box.Text.Trim();
        if (text.Length == 0) { Mark(box, true); return blank; }
        if (TryNumber(text, out double v)) { Mark(box, true); return v; }
        _fieldErrors.Add($"{label} must be a number.");
        Mark(box, false);
        return null;
    }

    /// <summary>A percentage as typed ("4", "4%", "4,5") turned into the 0..1 fraction the profile stores.</summary>
    private double? ReadPercent(TextBox box, string label, double current, double blank)
    {
        string text = box.Text.Trim().TrimEnd('%').Trim();
        if (text.Length == 0) { Mark(box, true); return blank; }
        if (TryNumber(text, out double v)) { Mark(box, true); return v / 100; }
        _fieldErrors.Add($"{label} must be a percentage, for example 4.");
        Mark(box, false);
        return null;
    }

    private double? ReadOptionalPercent(TextBox box, string label, double? current)
    {
        string text = box.Text.Trim().TrimEnd('%').Trim();
        if (text.Length == 0) { Mark(box, true); return null; }
        if (TryNumber(text, out double v)) { Mark(box, true); return v / 100; }
        _fieldErrors.Add($"{label} must be a percentage or empty, for example 35.");
        Mark(box, false);
        return current;
    }

    private double? ReadOptional(TextBox box, string label, double? current)
    {
        string text = box.Text.Trim();
        if (text.Length == 0) { Mark(box, true); return null; }
        if (TryNumber(text, out double v)) { Mark(box, true); return v; }
        _fieldErrors.Add($"{label} must be a number or empty.");
        Mark(box, false);
        return current;
    }

    /// <summary>Accepts "0.04" and also "0,04" so a European keyboard does not trip over the decimal separator.</summary>
    private static bool TryNumber(string text, out double v) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    /// <summary>Red border when the text is invalid; otherwise back to the control's own (themed) border.</summary>
    private static void Mark(TextBox box, bool ok)
    {
        if (ok) box.ClearValue(Control.BorderBrushProperty);
        else box.BorderBrush = Red;
    }

    private void UpdateVisibility()
    {
        string curve = CurveBox.SelectedItem as string ?? "linear";
        SetVisible(curve == "expo", ExpLabel, ExpPanel);
        SetVisible(curve == "custom", PointsLabel, PointsPanel);

        string to = ToBox.Text.Trim();
        bool button = to.StartsWith("Button:", StringComparison.OrdinalIgnoreCase);
        // Pad buttons, keys and mouse buttons are all on/off outputs, so they all have a trigger point and a button mode.
        bool onOff = button || to.StartsWith("Key:", StringComparison.OrdinalIgnoreCase) || to.StartsWith("Mouse:", StringComparison.OrdinalIgnoreCase);
        SetVisible(to.Length > 0 && !onOff, SmoothLabel, SmoothPanel);   // smoothing is for sticks and triggers
        SetVisible(onOff, ThresholdLabel, ThresholdPanel);
        SetVisible(onOff, ModeLabel, ModePanel);
        PulsePanel.Visibility = (ModeBox.SelectedItem as string) == "pulse" ? Visibility.Visible : Visibility.Collapsed;

        // Turbo is a standard-mode extra for button outputs, never for a game on the anti-cheat list.
        SetVisible(button, TurboLabel, TurboPanel);
        string? blocked = AntiCheatList.Current.FirstProtected(ExeBox.Text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        bool turboOk = IsStandard && blocked is null;
        TurboBox.IsEnabled = TurboHzBox.IsEnabled = turboOk;
        TurboHint.Text = blocked is not null ? $"{blocked} is on the anti-cheat list, so turbo is not available."
            : !IsStandard ? "Turbo needs this profile's Safety to be Standard (offline and single-player games only)."
            : "Repeats the button while you hold it. For offline and single-player games only.";
        if (!turboOk && TurboBox.IsChecked == true && Current is not null)
        {
            _populating = true; TurboBox.IsChecked = false; _populating = false;
            Current.Rule.Turbo = false;
        }
    }

    private static void SetVisible(bool on, params UIElement[] items)
    {
        foreach (var i in items) i.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private readonly System.Windows.Threading.DispatcherTimer _liveTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };

    /// <summary>Show where the selected rule's control is on its curve right now, so moving the wheel or a pedal moves a dot on the graph.</summary>
    private void LiveTick()
    {
        MapperSnapshot? snap = _latest();
        if (Current is null || snap is null || !snap.Raw.Live) { Preview.SetLive(null); return; }
        Rule r = Current.Rule;
        if (string.IsNullOrWhiteSpace(r.From) || !snap.Device.Axes.ContainsKey(r.From)) { Preview.SetLive(null); return; }   // buttons have no curve to sit on
        bool signed = snap.Device.IsSigned(r.From);
        Preview.SetLive(LiveCurve.Position(snap.Input.Get(r.From), r, signed, snap.Device.RotationDegrees));
    }

    private void UpdatePreview()
    {
        if (Current is null) { Preview.Set("linear", 2, null, 0, 0, false); return; }
        Rule r = Current.Rule;
        bool signed = Device?.IsSigned(r.From) == true;
        Preview.Set(r.Curve, r.Exponent, r.Points, r.Deadzone, r.AntiDeadzone, signed);
        if (r.Curve.Equals("custom", StringComparison.OrdinalIgnoreCase))
        {
            PreviewNote.Text = "Custom curve: click the graph to add a point, drag a point to move it, right-click a point to remove it. The left half mirrors the right.";
            return;
        }
        PreviewNote.Text = signed
            ? "Wheel: left lock to right lock across, stick output up. The dashed line is a straight 1:1 response."
            : "Left to right is the input (released to fully pressed), bottom to top is the output. The dashed line is 1:1.";
    }

    // ---------------------------------------------------------------- profile-level and validation

    private void Profile_Changed(object sender, RoutedEventArgs e)
    {
        if (_populating) return;
        UpdateSafetyUi();
        Revalidate();
    }

    private void Feel_Slider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FeelRumbleText is null || FeelSpringText is null || FeelFrictionText is null) return;   // during start-up
        FeelRumbleText.Text = $"{(int)FeelRumble.Value}%";
        FeelSpringText.Text = $"{(int)FeelSpring.Value}%";
        FeelFrictionText.Text = $"{(int)FeelFriction.Value}%";
        if (!_populating) Revalidate();
    }

    /// <summary>The feel controls only mean something while "Own force feedback" is ticked.</summary>
    private void UpdateFeelUi()
    {
        bool on = FeelBox.IsChecked == true;
        FeelRumble.IsEnabled = FeelSpring.IsEnabled = FeelFriction.IsEnabled = FeelMotors.IsEnabled = on;
        FeelRumbleText.Text = $"{(int)FeelRumble.Value}%";
        FeelSpringText.Text = $"{(int)FeelSpring.Value}%";
        FeelFrictionText.Text = $"{(int)FeelFriction.Value}%";
    }

    private string _safetyBefore = "Strict";

    private bool IsStandard => SafetyBox.SelectedItem as string == "Standard";

    /// <summary>Keyboard output only makes sense in standard mode and never for a game on the anti-cheat list.</summary>
    private void UpdateSafetyUi()
    {
        string? blocked = AntiCheatList.Current.FirstProtected(ExeBox.Text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        KeyboardBox.IsEnabled = IsStandard && blocked is null;
        if (!KeyboardBox.IsEnabled && KeyboardBox.IsChecked == true)
        {
            _populating = true; KeyboardBox.IsChecked = false; _populating = false;
        }
        SafetyHint.Text = blocked is not null ? $"{blocked} is on the anti-cheat list, so this profile can only use the plain virtual controller."
            : IsStandard ? "Standard: extras are allowed. Do not use this for games with online play or anti-cheat."
            : "Strict: controller mapping only, the safe choice for online games and anti-cheat.";
        PadHint.Text = PadBox.SelectedItem as string == "DualShock 4"
            ? "Rules keep the Xbox names: A = Cross, B = Circle, X = Square, Y = Triangle, shoulders = L1/R1, triggers = L2/R2, Back = Share, Start = Options, Guide = PS button."
            : "Works with nearly every PC game.";
        UpdateFeelUi();
        HideWheelHint.Text = HideWheelBox.SelectedIndex == 1 ? "Only when this profile switches on because its game was detected (see Games above); shown again when the game ends."
            : HideWheelBox.SelectedIndex == 2 ? "Only when this profile switches on because its game was detected: makes sure the wheel is visible then." : "";
        UpdateVisibility();   // turbo depends on the same checks
    }

    private void Safety_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_populating) return;
        // The warning is shown once; after it has been accepted the profile can be switched freely.
        if (IsStandard && _safetyBefore == "Strict" && !SafetyPrompts.ConfirmStandard(this, _settings))
        {
            _populating = true; SafetyBox.SelectedItem = "Strict"; _populating = false;
        }
        _safetyBefore = SafetyBox.SelectedItem as string ?? "Strict";
        UpdateSafetyUi();
        Revalidate();
    }

    private void DeviceBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_populating) return;
        RefreshFromList();
        UpdatePreview();
        Revalidate();
    }

    private void Keyboard_Changed(object sender, RoutedEventArgs e)
    {
        if (_populating) return;
        if (KeyboardBox.IsChecked == true)
        {
            var answer = MessageBox.Show(this,
                "Keyboard output sends simulated key presses. In games with anti-cheat (for example GTA Online with BattlEye) " +
                "synthetic keyboard input is more likely to look like automation than a gamepad does. That is a judgement call, " +
                "not a verified rule, but the safe choice is to leave it off for online games.\n\n" +
                "Turn it on anyway?",
                "Keyboard output", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
            if (answer != MessageBoxResult.OK)
            {
                _populating = true; KeyboardBox.IsChecked = false; _populating = false;
            }
        }
        Revalidate();
    }

    private Profile BuildProfile() => new()
    {
        Name = NameBox.Text.Trim(),
        Notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
        IsTemplate = _isTemplate,
        Parent = _parent,
        Device = DeviceBox.SelectedItem as string ?? "",
        Match = new ProfileMatch
        {
            Exe = ExeBox.Text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        },
        Safety = IsStandard ? ProfileSafety.Standard : ProfileSafety.Strict,
        HideWheel = HideWheelBox.SelectedIndex switch { 1 => true, 2 => false, _ => null },
        Force = _view < 0 ? ForceFromUi() : _profileForce,
        Modes = _modes.Select((m, i) => new ProfileMode
        {
            Name = m.Name,
            Force = i == _view ? ForceFromUi() : m.Force,
            RemovedRules = m.Removed.Count == 0 ? null : m.Removed.ToList(),
            Rules = m.Rows.Select(r => r.Rule).ToList(),
        }).ToList(),
        Outputs = new ProfileOutputs
        {
            Keyboard = KeyboardBox.IsChecked == true, Turbo = _rows.Concat(_modes.SelectMany(m => m.Rows)).Any(r => r.Rule.Turbo),
            Pad = PadBox.SelectedItem as string == "DualShock 4" ? PadKinds.DualShock4Text : null,
            SecondPad = SecondPadBox.IsChecked == true,
        },
        Rules = _rows.Select(r => r.Rule).ToList(),
    };

    // ---------------------------------------------------------------- modes

    private ProfileForce? ForceFromUi() => FeelBox.IsChecked == true
        ? new ProfileForce
        {
            Rumble = (int)FeelRumble.Value, Spring = (int)FeelSpring.Value, Friction = (int)FeelFriction.Value,
            Motors = FeelMotors.SelectedIndex switch { 1 => ForceMath.MotorsHeavy, 2 => ForceMath.MotorsLight, _ => ForceMath.MotorsBoth },
        }
        : null;

    /// <summary>Show the feel belonging to the view being edited (the profile's, or the mode's) in the feel controls.</summary>
    private void ShowFeel()
    {
        ProfileForce? own = _view < 0 ? _profileForce : _modes[_view].Force;
        ProfileForce? basis = own ?? _profileForce;   // a mode that has no feel of its own starts from the profile's when it is switched on
        _populating = true;
        FeelBox.Content = _view < 0 ? "Own force feedback" : "Own feel in this mode";
        FeelBox.IsChecked = own is { IsEmpty: false };
        FeelRumble.Value = basis?.Rumble ?? 50;
        FeelSpring.Value = basis?.Spring ?? 0;
        FeelFriction.Value = basis?.Friction ?? 0;
        FeelMotors.SelectedIndex = string.Equals(basis?.Motors, ForceMath.MotorsHeavy, StringComparison.OrdinalIgnoreCase) ? 1
            : string.Equals(basis?.Motors, ForceMath.MotorsLight, StringComparison.OrdinalIgnoreCase) ? 2 : 0;
        _populating = false;
        UpdateFeelUi();
    }

    private void RefreshViewBox(int select)
    {
        _populating = true;
        var items = new List<string> { "The profile (rules for every mode)" };
        items.AddRange(_modes.Select(m => "Mode: " + m.Name));
        ViewBox.ItemsSource = items;
        ViewBox.SelectedIndex = Math.Clamp(select + 1, 0, items.Count - 1);
        _populating = false;
        UpdateModeUi();
    }

    /// <summary>Switch which rules (the profile's own or one mode's) and which feel the form is editing, keeping what was typed in the one being left.</summary>
    private void SelectView(int view)
    {
        view = Math.Clamp(view, -1, _modes.Count - 1);
        if (view == _view && ViewBox.SelectedIndex == view + 1) return;
        // What the feel controls show belongs to the view being left; keep it there.
        if (_view < 0) _profileForce = ForceFromUi(); else if (_view < _modes.Count) _modes[_view].Force = ForceFromUi();
        _view = view;
        RefreshViewBox(view);
        RuleList.ItemsSource = Rows;
        if (Rows.Count > 0) RuleList.SelectedIndex = 0; else LoadRule(null);
        ShowFeel();
        Revalidate();
    }

    private void View_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_populating || ViewBox.SelectedIndex < 0) return;
        SelectView(ViewBox.SelectedIndex - 1);
    }

    private void UpdateModeUi()
    {
        bool inMode = _view >= 0 && _view < _modes.Count;
        RenameModeButton.IsEnabled = DeleteModeButton.IsEnabled = inMode;
        EarlierModeButton.IsEnabled = inMode && _view > 0;
        LaterModeButton.IsEnabled = inMode && _view < _modes.Count - 1;
        TurnedOffButton.Visibility = inMode ? Visibility.Visible : Visibility.Collapsed;
        bool anySwitch = _rows.Concat(_modes.SelectMany(m => m.Rows)).Any(r => r.Rule.To.TrimStart().StartsWith("Mode:", StringComparison.OrdinalIgnoreCase));
        SwitchRuleButton.Visibility = _modes.Count > 0 && !anySwitch ? Visibility.Visible : Visibility.Collapsed;
        ModeNote.Text = _modes.Count == 0
            ? "A profile can have modes, such as Shooting, Driving and Flying: the same wheel and pedals doing different jobs, switched with a wheel button or a shortcut. Add one to start."
            : inMode
                ? $"Editing the mode \"{_modes[_view].Name}\": its rules apply only while it is active, and each replaces the profile's rule for the same output. The feel below is this mode's own. The first mode ({_modes[0].Name}) is where the profile starts."
                : $"The profile's own rules apply in every mode ({string.Join(", ", _modes.Select(m => m.Name))}). Rules that switch modes belong here, so they work in all of them."
                  + (anySwitch ? "" : " No wheel button switches modes yet: use Add a switch button, or the shortcut in Settings.");
    }

    private void AddMode_Click(object sender, RoutedEventArgs e)
    {
        if (_modes.Count >= ProfileModes.MaxModes)
        {
            MessageBox.Show(this, $"A profile can have at most {ProfileModes.MaxModes} modes.", "Add mode", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        string? wanted = Prompt.Ask(this, "Add mode", "Name for the new mode, for example Shooting, Driving or Flying.", _modes.Count == 0 ? "Shooting" : "Driving")?.Trim();
        if (string.IsNullOrEmpty(wanted)) return;
        wanted = wanted.Replace(":", "");
        string name = FreeModeName(wanted, null);
        _modes.Add(new EditorMode { Name = name });
        ToBox.ItemsSource = ToChoices(SecondPadBox.IsChecked == true, ModeNames);
        SelectView(_modes.Count - 1);
    }

    private string FreeModeName(string wanted, EditorMode? except)
    {
        bool Taken(string n) => ProfileModes.IsReservedName(n) || _modes.Any(m => !ReferenceEquals(m, except) && string.Equals(m.Name, n, StringComparison.OrdinalIgnoreCase));
        if (!Taken(wanted)) return wanted;
        for (int n = 2; ; n++) if (!Taken($"{wanted} {n}")) return $"{wanted} {n}";
    }

    private void RenameMode_Click(object sender, RoutedEventArgs e)
    {
        if (_view < 0 || _view >= _modes.Count) return;
        EditorMode m = _modes[_view];
        string? wanted = Prompt.Ask(this, "Rename mode", "New name for this mode.", m.Name)?.Trim().Replace(":", "");
        if (string.IsNullOrEmpty(wanted) || wanted == m.Name) return;
        string old = m.Name;
        m.Name = FreeModeName(wanted, m);
        // Rules that switch to this mode by name follow it.
        foreach (RuleRow row in _rows.Concat(_modes.SelectMany(x => x.Rows)))
            if (row.Rule.To.Trim().Equals("Mode:" + old, StringComparison.OrdinalIgnoreCase)) { row.Rule.To = "Mode:" + m.Name; row.Refresh(); }
        ToBox.ItemsSource = ToChoices(SecondPadBox.IsChecked == true, ModeNames);
        RefreshViewBox(_view);
        LoadRule(Current);
        Revalidate();
    }

    private void DeleteMode_Click(object sender, RoutedEventArgs e)
    {
        if (_view < 0 || _view >= _modes.Count) return;
        EditorMode m = _modes[_view];
        var switching = _rows.Concat(_modes.SelectMany(x => x.Rows)).Where(r => r.Rule.To.Trim().Equals("Mode:" + m.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        string extra = switching.Count > 0 ? $"\n\n{switching.Count} rule{(switching.Count == 1 ? "" : "s")} that switch{(switching.Count == 1 ? "es" : "")} to it will be removed too." : "";
        if (MessageBox.Show(this, $"Delete the mode \"{m.Name}\" and its {m.Rows.Count} rule{(m.Rows.Count == 1 ? "" : "s")}?{extra}", "Delete mode",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        foreach (RuleRow row in switching)
        {
            _rows.Remove(row);
            foreach (EditorMode other in _modes) other.Rows.Remove(row);
        }
        int removedAt = _view;
        _view = -1;           // the mode's feel is going with it, so nothing is kept from the controls
        _modes.RemoveAt(removedAt);
        ToBox.ItemsSource = ToChoices(SecondPadBox.IsChecked == true, ModeNames);
        RefreshViewBox(-1);
        RuleList.ItemsSource = Rows;
        if (Rows.Count > 0) RuleList.SelectedIndex = 0; else LoadRule(null);
        ShowFeel();
        Revalidate();
    }

    private void MoveMode_Click(object sender, RoutedEventArgs e)
    {
        if (_view < 0 || _view >= _modes.Count || sender is not Button { Tag: string tag } || !int.TryParse(tag, out int step)) return;
        int to = _view + step;
        if (to < 0 || to >= _modes.Count) return;
        // Keep the feel on screen with its mode before the order changes.
        _modes[_view].Force = ForceFromUi();
        (_modes[_view], _modes[to]) = (_modes[to], _modes[_view]);
        _view = to;
        RefreshViewBox(to);
        Revalidate();
    }

    /// <summary>Tick the profile rules (by what they send to) that should not apply while this mode is active.</summary>
    private void TurnedOff_Click(object sender, RoutedEventArgs e)
    {
        if (_view < 0 || _view >= _modes.Count) return;
        EditorMode m = _modes[_view];
        var targets = _rows.Select(r => r.Rule.To.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var menu = new ContextMenu { PlacementTarget = TurnedOffButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        if (targets.Count == 0) menu.Items.Add(new MenuItem { Header = "The profile has no rules yet", IsEnabled = false });
        foreach (string target in targets)
        {
            var item = new MenuItem { Header = "Off here: " + target, IsCheckable = true, StaysOpenOnClick = true, IsChecked = m.Removed.Any(x => x.Equals(target, StringComparison.OrdinalIgnoreCase)) };
            item.Click += (_, _) =>
            {
                m.Removed.RemoveAll(x => x.Equals(target, StringComparison.OrdinalIgnoreCase));
                if (item.IsChecked) m.Removed.Add(target);
                Revalidate();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    /// <summary>A starting point for switching from the wheel: a profile rule that sends a button to the next mode, ready for Learn.</summary>
    private void SwitchRule_Click(object sender, RoutedEventArgs e)
    {
        SelectView(-1);
        var row = new RuleRow(new Rule { From = "", To = "Mode:Next", Curve = "linear" });
        _rows.Add(row);
        RuleList.SelectedItem = row;
        RuleList.ScrollIntoView(row);
        Revalidate();
    }

    // ---------------------------------------------------------------- templates

    private Profile? TemplateNow => _parent is null ? null : _findTemplate?.Invoke(_parent.Name);

    /// <summary>The "Template" line of the header: which template this profile follows, how it differs, and whether the template has changed.</summary>
    private void UpdateTemplateRow(Profile? resolved)
    {
        if (!_isTemplate && _parent is null) { TemplateRow.Visibility = Visibility.Collapsed; return; }
        TemplateRow.Visibility = Visibility.Visible;
        if (_isTemplate)
        {
            TemplateInfo.Text = "This is a template. Profiles made from it are offered your changes as an update.";
            TemplateMenuButton.Visibility = Visibility.Collapsed;
            return;
        }
        int diffs = resolved is { Parent: not null } ? Templates.Diff(resolved).Count : 0;
        bool update = Templates.UpdateAvailable(resolved ?? BuildProfile(), TemplateNow);
        TemplateInfo.Text = $"Follows \"{_parent!.Name}\"; {(diffs == 0 ? "nothing of its own yet" : $"{diffs} thing{(diffs == 1 ? "" : "s")} of its own")}" + (update ? "; the template has changed (update available)" : "");
        TemplateMenuButton.Visibility = Visibility.Visible;
    }

    /// <summary>Fill the settings and the rule list from a profile (after a reset or an update), leaving name, notes and game as typed.</summary>
    private void ReloadFrom(Profile p)
    {
        SelectView(-1);   // a template reset or update is about the profile's own rules and settings
        _profileForce = p.Force;
        _populating = true;
        DeviceBox.SelectedItem = ((IEnumerable<string>)DeviceBox.ItemsSource).FirstOrDefault(n => n.Equals(p.Device, StringComparison.OrdinalIgnoreCase)) ?? p.Device;
        HideWheelBox.SelectedIndex = p.HideWheel is true ? 1 : p.HideWheel is false ? 2 : 0;
        FeelBox.IsChecked = p.Force is { IsEmpty: false };
        FeelRumble.Value = p.Force?.Rumble ?? 50;
        FeelSpring.Value = p.Force?.Spring ?? 0;
        FeelFriction.Value = p.Force?.Friction ?? 0;
        FeelMotors.SelectedIndex = string.Equals(p.Force?.Motors, ForceMath.MotorsHeavy, StringComparison.OrdinalIgnoreCase) ? 1
            : string.Equals(p.Force?.Motors, ForceMath.MotorsLight, StringComparison.OrdinalIgnoreCase) ? 2 : 0;
        PadBox.SelectedItem = PadKinds.Of(p) == PadKind.DualShock4 ? "DualShock 4" : "Xbox 360";
        SecondPadBox.IsChecked = p.Outputs.SecondPad;
        ToBox.ItemsSource = ToChoices(p.Outputs.SecondPad, ModeNames);
        SafetyBox.SelectedItem = ProfileSafety.Effective(p) == ProfileSafety.Standard ? "Standard" : "Strict";
        _safetyBefore = SafetyBox.SelectedItem as string ?? "Strict";
        KeyboardBox.IsChecked = p.Outputs.Keyboard;
        UpdateSafetyUi();
        UpdateFeelUi();
        _rows.Clear();
        foreach (Rule r in p.Rules) _rows.Add(new RuleRow(r));
        _populating = false;
        if (_rows.Count > 0) RuleList.SelectedIndex = 0; else LoadRule(null);
        RefreshFromList();
        UpdatePreview();
        Revalidate();
    }

    private bool ResetInEditor(TemplateDiff diff)
    {
        // Putting the safety mode back to Standard is the same decision as choosing it, so it gets the same one-time warning.
        bool toStandard = (diff.Key == "safety" && diff.Template == ProfileSafety.Standard) || (diff.Key == "outputs.keyboard" && diff.Template == "on");
        if (toStandard && !SafetyPrompts.ConfirmStandard(this, _settings)) return false;
        Profile p = BuildProfile();
        Templates.Reset(p, diff);
        ReloadFrom(p);
        return true;
    }

    private void TemplateMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_parent is null) return;
        Profile now = BuildProfile();
        var diffs = Templates.Diff(now);
        Profile? template = TemplateNow;
        bool update = Templates.UpdateAvailable(now, template);
        var menu = new ContextMenu { PlacementTarget = TemplateMenuButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        MenuItem Item(string header, RoutedEventHandler click, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            mi.Click += click;
            return mi;
        }

        menu.Items.Add(Item($"Differences from the template ({diffs.Count})...", (_, _) =>
            new TemplateDiffWindow(NameBox.Text.Trim(), _parent!.Name, () => Templates.Diff(BuildProfile()), ResetInEditor) { Owner = this }.ShowDialog()));

        string? selected = Current?.Rule.To.Trim();
        var selectedDiff = selected is null ? null : diffs.FirstOrDefault(d => d.Kind != DiffKind.Setting && string.Equals(d.Key, selected, StringComparison.OrdinalIgnoreCase));
        menu.Items.Add(Item("Reset the selected rule to the template", (_, _) => ResetInEditor(selectedDiff!), selectedDiff is not null));

        if (update)
            menu.Items.Add(Item("Update from the template (it has changed)...", (_, _) => UpdateFromTemplate(template!)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Stop following the template (make this profile standalone)", (_, _) =>
        {
            if (MessageBox.Show(this, "Make this profile standalone? It keeps all its current rules and settings, but no longer follows the template or is offered its changes.",
                    "Stop following the template", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            _parent = null;
            Revalidate();
        }));
        menu.IsOpen = true;
    }

    private void UpdateFromTemplate(Profile template)
    {
        ProfileLoadResult result = Templates.ApplyUpdate(BuildProfile(), template, _devices);
        if (!result.Ok)
        {
            MessageBox.Show(this, "The updated profile would not be valid, so nothing was changed:\n\n" + string.Join("\n", result.Errors.Take(6)), "Update from template", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _parent = result.Profile!.Parent;
        ReloadFrom(result.Profile);
    }

    private void Revalidate()
    {
        var issues = new List<Issue>();
        foreach (string e in _fieldErrors) issues.Add(new Issue("✗ " + e, Red));

        _lastJson = ProfileWriter.ToJson(BuildProfile());
        var result = ProfileLoader.Parse(_lastJson, _devices);
        foreach (string e in result.Errors) issues.Add(new Issue("✗ " + e, Red));
        foreach (string w in result.Warnings) issues.Add(new Issue("⚠ " + w, Amber));

        _valid = result.Ok && _fieldErrors.Count == 0;
        UpdateTemplateRow(result.Profile);
        UpdateModeUi();
        if (issues.Count == 0) issues.Add(new Issue("✓ Looks good.", Green));
        IssueList.ItemsSource = issues;
        SaveButton.IsEnabled = _valid;
    }

    // ---------------------------------------------------------------- learn an input from the wheel

    private void Learn_Click(object sender, RoutedEventArgs e)
    {
        if (_learnTimer.IsEnabled) { StopLearning("Cancelled."); return; }

        MapperSnapshot? snap = _latest();
        if (snap is null)
        {
            ShowLearnHint("The mapper is not running, so there is no wheel to listen to. Start it from the main window.");
            return;
        }
        if (!snap.Raw.Live)
        {
            ShowLearnHint("The wheel is not reporting yet. Move it a little, then click Learn again.");
            return;
        }
        if (Device is null) { ShowLearnHint("Choose a device first."); return; }

        _learnBaseline = snap;
        _learnUntil = DateTime.UtcNow.AddSeconds(15);
        LearnButton.Content = "Cancel";
        ShowLearnHint("Now press a button, press a pedal, or turn the wheel…");
        _learnTimer.Start();
    }

    private void LearnTick()
    {
        MapperSnapshot? now = _latest();
        if (now is null || _learnBaseline is null || Device is null) { StopLearning("Lost contact with the wheel."); return; }

        // The wheel first, then any extra devices (their inputs are written "device.input").
        DetectedInput? hit = InputDetector.Detect(_learnBaseline.Raw, now.Raw, Device);
        string prefix = "";
        if (hit is null)
        {
            foreach (var (deviceName, raw) in now.ExtraRaw)
            {
                if (!_learnBaseline.ExtraRaw.TryGetValue(deviceName, out RawInput? before) || !_devices.TryGet(deviceName, out DeviceMap? map)) continue;
                hit = InputDetector.Detect(before, raw, map);
                if (hit is not null) { prefix = deviceName + "."; break; }
            }
        }

        if (hit is not null)
        {
            if (hit.Name is not null) FromBox.Text = prefix + hit.Name;
            StopLearning(hit.Name is not null ? $"Learned: {(prefix.Length > 0 ? prefix : "")}{hit.Description}." : hit.Description);
            return;
        }
        if (DateTime.UtcNow > _learnUntil) StopLearning("Nothing detected. Click Learn and try again.");
    }

    private void StopLearning(string message)
    {
        _learnTimer.Stop();
        _learnBaseline = null;
        LearnButton.Content = "Learn";
        ShowLearnHint(message);
    }

    private void ShowLearnHint(string text)
    {
        LearnHint.Text = text;
        LearnHint.Visibility = Visibility.Visible;
    }
    // ---------------------------------------------------------------- buttons

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var row = new RuleRow(new Rule { From = "throttle", To = "RightTrigger" });
        int at = RuleList.SelectedIndex >= 0 ? RuleList.SelectedIndex + 1 : Rows.Count;
        Rows.Insert(at, row);
        RuleList.SelectedItem = row;
        RuleList.ScrollIntoView(row);
        Revalidate();
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (Current is null) return;
        Rule s = Current.Rule;
        var copy = new Rule
        {
            From = s.From, To = s.To, Curve = s.Curve, Exponent = s.Exponent,
            Points = s.Points?.Select(p => (double[])p.Clone()).ToArray(),
            Deadzone = s.Deadzone, AntiDeadzone = s.AntiDeadzone, Invert = s.Invert,
            Threshold = s.Threshold, Mode = s.Mode, PulseMs = s.PulseMs, Range = s.Range,
        };
        var row = new RuleRow(copy);
        Rows.Insert(RuleList.SelectedIndex + 1, row);
        RuleList.SelectedItem = row;
        Revalidate();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Current is null) return;
        int i = RuleList.SelectedIndex;
        Rows.RemoveAt(i);
        if (Rows.Count > 0) RuleList.SelectedIndex = Math.Min(i, Rows.Count - 1);
        else LoadRule(null);
        Revalidate();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Revalidate();
        if (!_valid) return;
        try
        {
            if (File.Exists(_path)) File.Copy(_path, _path + ".bak", overwrite: true);
            File.WriteAllText(_path, _lastJson);
            Saved = true;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not save {_path}:\n{ex.Message}", "Edit rules", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

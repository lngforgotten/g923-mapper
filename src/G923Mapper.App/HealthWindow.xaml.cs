using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace G923Mapper.App;

public sealed record HealthRow(string Symbol, Brush Brush, string Title, string Detail, string ActionText,
                               Visibility ActionVisibility, HealthAction Action)
{
    public override string ToString() => $"{Title}. {Detail}";
}

public partial class HealthWindow : Window
{
    private readonly AppServices _s;

    public HealthWindow(AppServices services)
    {
        InitializeComponent();
        _s = services;
        Refresh();
        ThemeManager.Changed += OnTheme;
        Closed += (_, _) => ThemeManager.Changed -= OnTheme;
    }

    private void OnTheme() => Dispatcher.BeginInvoke(Refresh);

    private void Refresh()
    {
        var svc = _s.Service;
        var missing = svc.IsRunning ? svc.ExtraStatus.Where(x => !x.Connected).Select(x => x.Name).ToList() : new List<string>();
        var items = HealthEvaluator.Evaluate(HealthProbe.Gather(svc, _s.Store, _s.HidHide, _s.WheelHidden, missing));

        Summary.Text = HealthEvaluator.Summary(items) switch
        {
            "All good" => "Everything looks good",
            var s => s,
        };
        Rows.ItemsSource = items.Select(i => new HealthRow(
            i.Level switch { HealthLevel.Ok => "✓", HealthLevel.Info => "i", HealthLevel.Warning => "!", _ => "✗" },
            i.Level switch { HealthLevel.Ok => ThemeManager.Success, HealthLevel.Info => ThemeManager.Muted, HealthLevel.Warning => ThemeManager.Warn, _ => ThemeManager.Danger },
            i.Title, i.Detail,
            i.Action switch
            {
                HealthAction.GetViGEmBus => "Get driver",
                HealthAction.GetHidHide => "Get HidHide",
                HealthAction.OpenSettings => "Open settings",
                HealthAction.OpenLogs => "Open log",
                _ => "",
            },
            i.Action == HealthAction.None ? Visibility.Collapsed : Visibility.Visible,
            i.Action)).ToList();
    }

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not HealthRow row) return;
        switch (row.Action)
        {
            case HealthAction.GetViGEmBus: Open("https://github.com/nefarius/ViGEmBus/releases"); break;
            case HealthAction.GetHidHide: Open(HidHideClient.DownloadUrl); break;
            case HealthAction.OpenLogs: if (AppLog.Directory is string d) Open(d); break;
            case HealthAction.OpenSettings:
                Close();
                if (Owner is not null)
                {
                    var settings = new SettingsWindow(_s) { Owner = Owner };
                    settings.ShowDialog();
                    if (settings.OpenSetupRequested && Owner is MainWindow main) main.ShowSetup();
                }
                break;
        }
    }

    private static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { AppLog.Warn($"Could not open {target}: {ex.Message}"); }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _ = _s.RefreshHiddenStateAsync().ContinueWith(_ => Dispatcher.BeginInvoke(Refresh));
        Refresh();
    }

    private void SaveReport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text file (*.txt)|*.txt", Title = "Save health report",
            FileName = $"G923Mapper-report-{DateTime.Now:yyyyMMdd-HHmm}.txt",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            System.IO.File.WriteAllText(dlg.FileName, BuildReport(), System.Text.Encoding.UTF8);
            AppLog.Info("Health report saved.");
            ThemedMessageBox.Show(this, "Report saved. It lists the findings above and the recent log lines, and contains no personal files.",
                "Health report", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            ThemedMessageBox.Show(this, $"Could not save the report:\n{ex.Message}", "Health report", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string BuildReport()
    {
        var svc = _s.Service;
        var missing = svc.IsRunning ? svc.ExtraStatus.Where(x => !x.Connected).Select(x => x.Name).ToList() : new List<string>();
        var items = HealthEvaluator.Evaluate(HealthProbe.Gather(svc, _s.Store, _s.HidHide, _s.WheelHidden, missing));
        string[] header =
        [
            $"App: G923 Mapper {typeof(HealthWindow).Assembly.GetName().Version?.ToString(3)} (copy {_s.Instance})",
            $"System: {System.Runtime.InteropServices.RuntimeInformation.OSDescription} · {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}",
            $"Mapper: {svc.Status}",
            $"Wheel: {(string.IsNullOrWhiteSpace(svc.WheelName) ? "(none)" : $"{svc.WheelName} ({svc.WheelIds})")}",
            $"Profile: {svc.ProfileName}",
            $"Show wheel again on exit: {(_s.Settings.UnhideWheelOnExit ? "yes" : "no")}",
            $"Anti-cheat list: {string.Join(", ", AntiCheatList.Current.Names)}",
        ];
        return HealthReport.Build(DateTime.Now, header, items, AppLog.Tail(60));
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}


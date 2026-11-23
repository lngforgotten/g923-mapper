using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using WinForms = System.Windows.Forms;

namespace G923Mapper.App;

internal static class AppIcon
{
    /// <summary>
    /// The app icon at the size the system tray wants (so it stays sharp on high-DPI screens). Taken from the embedded
    /// app.ico; if that cannot be read it falls back to a simple wheel drawn at runtime.
    /// </summary>
    public static Icon Create(int size = 0)
    {
        if (size <= 0) size = WinForms.SystemInformation.SmallIconSize.Width;
        try
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (resource is not null)
            {
                using var stream = resource.Stream;
                return new Icon(stream, new Size(size, size));
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or ArgumentException or UriFormatException) { }

        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float s = size;
            using var rim = new Pen(Color.FromArgb(0x3B, 0x82, 0xF6), s * 0.14f);
            using var spoke = new Pen(Color.FromArgb(0x3B, 0x82, 0xF6), s * 0.11f);
            using var hub = new SolidBrush(Color.FromArgb(0x3B, 0x82, 0xF6));
            float m = s * 0.12f;
            g.DrawEllipse(rim, m, m, s - 2 * m, s - 2 * m);
            float cx = s / 2, cy = s / 2;
            g.DrawLine(spoke, m + s * 0.04f, cy, s - m - s * 0.04f, cy);   // horizontal bar
            g.DrawLine(spoke, cx, cy, cx, s - m - s * 0.04f);              // down spoke
            g.FillEllipse(hub, cx - s * 0.12f, cy - s * 0.12f, s * 0.24f, s * 0.24f);
        }
        // Clone so the returned Icon owns its data and the bitmap's HICON can be released.
        IntPtr h = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { NativeMethods.DestroyIcon(h); }
    }

    public static BitmapSource ToBitmapSource(Icon icon) =>
        Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr handle);
    }
}

/// <summary>System tray icon with Open / Start-Stop / Exit.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _toggle;
    private readonly Icon _glyph;

    /// <param name="profiles">The profiles that can be chosen (name, whether it is the current one), asked each time the menu opens.</param>
    /// <param name="chooseProfile">Called with the name of the profile picked from the menu.</param>
    /// <param name="isPaused">Whether the controller is paused right now.</param>
    /// <param name="togglePause">Pause or resume the controller.</param>
    public TrayIcon(Action open, Action toggleRun, Action exit, Func<bool> isRunning,
                    Func<System.Collections.Generic.IReadOnlyList<(string Name, bool Current)>>? profiles = null, Action<string>? chooseProfile = null,
                    Func<bool>? isPaused = null, Action? togglePause = null,
                    Func<(bool Installed, bool Hidden)>? hideState = null, Action? toggleHidden = null, Action? popOut = null)
    {
        _glyph = AppIcon.Create();
        var menu = new WinForms.ContextMenuStrip();
        var openItem = new WinForms.ToolStripMenuItem("Open G923 Mapper") { Font = new Font(WinForms.Control.DefaultFont, FontStyle.Bold) };
        openItem.Click += (_, _) => open();
        _toggle = new WinForms.ToolStripMenuItem("Stop");
        _toggle.Click += (_, _) => toggleRun();
        var exitItem = new WinForms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => exit();
        var profileMenu = new WinForms.ToolStripMenuItem("Profile");
        var pauseItem = new WinForms.ToolStripMenuItem("Pause controller");
        pauseItem.Click += (_, _) => togglePause?.Invoke();
        var hideItem = new WinForms.ToolStripMenuItem("Hide wheel from games");
        hideItem.Click += (_, _) => toggleHidden?.Invoke();
        var popItem = new WinForms.ToolStripMenuItem("Pop-out view");
        popItem.Click += (_, _) => popOut?.Invoke();
        popItem.Visible = popOut is not null;
        menu.Items.AddRange([openItem, popItem, new WinForms.ToolStripSeparator(), profileMenu, pauseItem, _toggle, new WinForms.ToolStripSeparator(), hideItem, new WinForms.ToolStripSeparator(), exitItem]);
        menu.Opening += (_, _) =>
        {
            bool running = isRunning();
            _toggle.Text = running ? "Stop (removes the virtual pad)" : "Start";

            pauseItem.Visible = togglePause is not null;
            pauseItem.Enabled = running;
            pauseItem.Text = isPaused?.Invoke() == true ? "Resume controller" : "Pause controller";

            // Hide or show the real wheel (HidHide); offered only when HidHide is installed.
            var hs = hideState?.Invoke();
            hideItem.Visible = toggleHidden is not null && hs is { Installed: true };
            hideItem.Text = hs is { Hidden: true } ? "Show wheel again (unhide from games)" : "Hide wheel from games";

            // The profile list is read fresh each time, so new and deleted profiles show up without a restart.
            profileMenu.DropDownItems.Clear();
            var list = profiles?.Invoke();
            profileMenu.Visible = list is { Count: > 0 };
            if (list is null) return;
            foreach (var (name, current) in list)
            {
                var item = new WinForms.ToolStripMenuItem(name) { Checked = current };
                string chosen = name;
                item.Click += (_, _) => chooseProfile?.Invoke(chosen);
                profileMenu.DropDownItems.Add(item);
            }
        };

        _icon = new WinForms.NotifyIcon { Icon = _glyph, Text = "G923 Mapper", ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => open();
    }

    public Icon Glyph => _glyph;

    /// <summary>NotifyIcon.Text is limited to 63 characters.</summary>
    public void SetTooltip(string text) => _icon.Text = text.Length <= 63 ? text : text[..62] + "…";

    public void ShowBalloon(string title, string text) => _icon.ShowBalloonTip(4000, title, text, WinForms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _glyph.Dispose();
    }
}

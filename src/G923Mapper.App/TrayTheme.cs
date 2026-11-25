using System;
using System.Drawing;
using System.Windows.Media;
using Color = System.Drawing.Color;
using Pen = System.Drawing.Pen;
using Brush = System.Drawing.Brush;
using WinForms = System.Windows.Forms;

namespace G923Mapper.App;

/// <summary>
/// Makes the tray icon's menu follow the app's theme. That menu is a Windows Forms one (the notification icon has no WPF equivalent), which
/// otherwise always draws in plain Windows light colours. The colours are read from the app's own palette each time something is painted, so a
/// switch between Light, Dark and High contrast shows the next time the menu opens.
/// </summary>
internal static class TrayTheme
{
    /// <summary>Use the app's colours for this menu and every sub-menu under it.</summary>
    public static void Apply(WinForms.ContextMenuStrip menu)
    {
        menu.Renderer = new Renderer();
        menu.ShowCheckMargin = true;
        menu.ShowImageMargin = false;
    }

    private static Color C(string key) =>
        ThemeManager.Brush(key) is SolidColorBrush b ? Color.FromArgb(b.Color.A, b.Color.R, b.Color.G, b.Color.B) : Color.Gray;

    private sealed class Colors : WinForms.ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => C("CardBack");
        public override Color MenuBorder => C("CardBorder");
        public override Color MenuItemBorder => C("SecBorder");
        public override Color MenuItemSelected => C("SecHover");
        public override Color MenuItemSelectedGradientBegin => C("SecHover");
        public override Color MenuItemSelectedGradientEnd => C("SecHover");
        public override Color MenuItemPressedGradientBegin => C("SecPressed");
        public override Color MenuItemPressedGradientEnd => C("SecPressed");
        public override Color ImageMarginGradientBegin => C("CardBack");
        public override Color ImageMarginGradientMiddle => C("CardBack");
        public override Color ImageMarginGradientEnd => C("CardBack");
        public override Color SeparatorDark => C("CardBorder");
        public override Color SeparatorLight => C("CardBack");
        public override Color CheckBackground => C("SecHover");
        public override Color CheckSelectedBackground => C("SecPressed");
        public override Color CheckPressedBackground => C("SecPressed");
    }

    private sealed class Renderer : WinForms.ToolStripProfessionalRenderer
    {
        public Renderer() : base(new Colors()) { RoundedEdges = false; }

        protected override void OnRenderToolStripBackground(WinForms.ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(C("CardBack"));
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(WinForms.ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(C("CardBorder"));
            var r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            e.Graphics.DrawRectangle(pen, r);
        }

        protected override void OnRenderImageMargin(WinForms.ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(C("CardBack"));
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderMenuItemBackground(WinForms.ToolStripItemRenderEventArgs e)
        {
            var item = e.Item;
            if (!item.Enabled || !item.Selected) return;
            using var brush = new SolidBrush(item.Pressed ? C("SecPressed") : C("SecHover"));
            e.Graphics.FillRectangle(brush, new Rectangle(2, 1, item.Width - 4, item.Height - 2));
        }

        protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? C("TextPrimary") : C("TextMuted");
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(WinForms.ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item?.Enabled == false ? C("TextMuted") : C("TextPrimary");
            base.OnRenderArrow(e);
        }

        protected override void OnRenderSeparator(WinForms.ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(C("CardBorder"));
            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
        }

        protected override void OnRenderItemCheck(WinForms.ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            using var pen = new Pen(C("Accent"), 2f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            var old = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
            e.Graphics.DrawLines(pen, [new PointF(cx - 4.5f, cy), new PointF(cx - 1.5f, cy + 3.5f), new PointF(cx + 4.5f, cy - 4f)]);
            e.Graphics.SmoothingMode = old;
        }
    }
}


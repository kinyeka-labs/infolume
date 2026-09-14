using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Infolume.Ui;

/// <summary>
/// Paints the tray menu in the flyout's palette.
///
/// A stock ContextMenuStrip renders in Windows' own light chrome, which sits
/// badly against a dark flyout opened from the same icon: the two read as
/// different applications.
/// </summary>
internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    private readonly bool _dark;

    internal DarkMenuRenderer(bool dark) : base(new Colors(dark))
    {
        _dark = dark;
        RoundedEdges = false;
    }

    private Color Bg => _dark ? Color.FromArgb(0x24, 0x2A, 0x31) : Color.FromArgb(0xF7, 0xF9, 0xFB);
    private Color Fg => _dark ? Color.FromArgb(0xF0, 0xF3, 0xF6) : Color.FromArgb(0x12, 0x16, 0x1B);
    private Color Dim => _dark ? Color.FromArgb(0x8A, 0x94, 0x9F) : Color.FromArgb(0x6B, 0x76, 0x81);
    private Color Rule => _dark ? Color.FromArgb(0x33, 0x3B, 0x45) : Color.FromArgb(0xE4, 0xEA, 0xF0);
    private Color Accent => _dark ? Color.FromArgb(0xF0, 0xA9, 0x3B) : Color.FromArgb(0xA8, 0x57, 0x08);

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var b = new SolidBrush(Bg);
        e.Graphics.FillRectangle(b, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var p = new Pen(Rule);
        var r = e.AffectedBounds;
        e.Graphics.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var r = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height);
        if (e.Item.Selected && e.Item.Enabled)
        {
            using var b = new SolidBrush(Color.FromArgb(_dark ? 30 : 22, Accent));
            e.Graphics.FillRectangle(b, r);
        }
        else
        {
            using var b = new SolidBrush(Bg);
            e.Graphics.FillRectangle(b, r);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Fg : Dim;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var p = new Pen(Rule);
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        using var pen = new Pen(Accent, Math.Max(1.6f, r.Width * 0.14f))
        { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(pen,
        [
            new PointF(r.X + r.Width * 0.22f, r.Y + r.Height * 0.52f),
            new PointF(r.X + r.Width * 0.44f, r.Y + r.Height * 0.74f),
            new PointF(r.X + r.Width * 0.80f, r.Y + r.Height * 0.28f)
        ]);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled == true ? Fg : Dim;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var b = new SolidBrush(Bg);
        e.Graphics.FillRectangle(b, e.AffectedBounds);
    }

    /// <summary>The few colours ToolStripProfessionalRenderer reads directly.</summary>
    private sealed class Colors(bool dark) : ProfessionalColorTable
    {
        private readonly bool _dark = dark;

        private Color Bg => _dark ? Color.FromArgb(0x24, 0x2A, 0x31) : Color.FromArgb(0xF7, 0xF9, 0xFB);
        private Color Rule => _dark ? Color.FromArgb(0x33, 0x3B, 0x45) : Color.FromArgb(0xE4, 0xEA, 0xF0);

        public override Color ToolStripDropDownBackground => Bg;
        public override Color ImageMarginGradientBegin => Bg;
        public override Color ImageMarginGradientMiddle => Bg;
        public override Color ImageMarginGradientEnd => Bg;
        public override Color MenuBorder => Rule;
        public override Color MenuItemBorder => Rule;
        public override Color SeparatorDark => Rule;
        public override Color SeparatorLight => Rule;
    }
}

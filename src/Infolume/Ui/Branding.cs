using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Infolume.Icons;

namespace Infolume.Ui;

/// <summary>
/// The app's mark and name, as one control reused by every window.
///
/// Until now the name only appeared in a window title nobody had reason to open,
/// so the panel was anonymous: nothing on screen said what it belonged to.
/// </summary>
internal sealed class Branding : Control
{
    private readonly bool _dark;
    private readonly float _scale;
    private readonly Bitmap _mark;

    internal static Branding Create(int x, int y, int width, bool dark, float scale, Font font) =>
        new(dark, scale)
        {
            Location = new Point(x, y),
            Size = new Size(width, (int)Math.Round(20 * scale)),
            Font = font
        };

    private Branding(bool dark, float scale)
    {
        _dark = dark;
        _scale = scale;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

        // The same mark the shell shows, so the panel and the Start menu entry are
        // visibly the same product.
        int s = Math.Max(16, (int)Math.Round(18 * scale));
        _mark = new Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(_mark);
        g.Clear(Color.Transparent);
        AppMarkPainter.Draw(g, AppMark.MonitorTile, s);
    }

    private Color Dim => _dark ? Color.FromArgb(0x7C, 0x87, 0x92) : Color.FromArgb(0x78, 0x82, 0x8C);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Color.Transparent);

        g.DrawImage(_mark, 0, (Height - _mark.Height) / 2, _mark.Width, _mark.Height);

        using var brush = new SolidBrush(Dim);
        g.DrawString("Infolume", Font, brush,
            new RectangleF(_mark.Width + (int)(6 * _scale), 0, Width - _mark.Width, Height),
            new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _mark.Dispose();
        base.Dispose(disposing);
    }
}

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Infolume.Ui;

/// <summary>
/// A section label that folds its contents away. The chevron is the only affordance
/// that a plain label would lack, so the whole row is the hit target rather than
/// the arrow alone.
/// </summary>
internal sealed class SectionHeader : Control
{
    private readonly string _text;
    private readonly bool _collapsed;
    private readonly bool _dark;
    private readonly float _scale;
    private bool _hover;

    internal event Action? Toggled;

    internal SectionHeader(string text, bool collapsed, bool dark, float scale)
    {
        _text = text;
        _collapsed = collapsed;
        _dark = dark;
        _scale = scale;

        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Cursor = Cursors.Hand;
        Click += (_, _) => Toggled?.Invoke();
    }

    private int S(int logical) => Math.Max(1, (int)Math.Round(logical * _scale));

    private Color Dim => _dark ? Color.FromArgb(0x98, 0xA3, 0xAE) : Color.FromArgb(0x66, 0x70, 0x7B);
    private Color Fg => _dark ? Color.FromArgb(0xF0, 0xF3, 0xF6) : Color.FromArgb(0x12, 0x16, 0x1B);

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Color.Transparent);

        Color ink = _hover ? Fg : Dim;

        using var font = new Font("Segoe UI", Math.Max(1f, 10f * _scale), FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(ink);

        int chev = S(10);
        int left = S(8);
        DrawChevron(g, new Rectangle(left, (Height - chev) / 2, chev, chev), ink, _collapsed);

        g.DrawString(_text.ToUpperInvariant(), font, brush,
            new RectangleF(left + chev + S(7), 0, Width - left - chev - S(14), Height),
            new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap });
    }

    /// <summary>Points right when folded, down when open.</summary>
    private static void DrawChevron(Graphics g, Rectangle r, Color c, bool collapsed)
    {
        using var pen = new Pen(c, Math.Max(1.2f, r.Width * 0.16f))
        { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

        float x = r.X, y = r.Y, s = r.Width;
        if (collapsed)
        {
            g.DrawLines(pen,
            [
                new PointF(x + s * 0.34f, y + s * 0.16f),
                new PointF(x + s * 0.70f, y + s * 0.50f),
                new PointF(x + s * 0.34f, y + s * 0.84f)
            ]);
        }
        else
        {
            g.DrawLines(pen,
            [
                new PointF(x + s * 0.16f, y + s * 0.36f),
                new PointF(x + s * 0.50f, y + s * 0.70f),
                new PointF(x + s * 0.84f, y + s * 0.36f)
            ]);
        }
    }
}

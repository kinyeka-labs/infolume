using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Infolume.Audio;
using Infolume.Icons;

namespace Infolume.Ui;

/// <summary>
/// One device in the flyout list.
///
/// Both lines are always drawn: the description alone is ambiguous on machines
/// where two active endpoints share a name (two here are both "Speakers"), and
/// the adapter is the only thing telling them apart.
/// </summary>
internal sealed class DeviceRow : Control
{
    private readonly EndpointInfo _ep;
    private readonly string _name;
    private readonly bool _active;
    private readonly bool _dark;
    private readonly Bitmap _glyph;
    private readonly float _scale;

    private bool _hover;
    private bool _overPencil;

    internal event Action<string>? Activate;
    internal event Action<string>? EditRequested;

    internal DeviceRow(EndpointInfo ep, string name, bool active, bool dark, Bitmap glyph, float scale)
    {
        _ep = ep;
        _name = name;
        _active = active;
        _dark = dark;
        _glyph = glyph;
        _scale = scale;

        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Cursor = Cursors.Hand;
    }

    private int S(int logical) => Math.Max(1, (int)Math.Round(logical * _scale));

    private Color Fg => _dark ? Color.FromArgb(0xF0, 0xF3, 0xF6) : Color.FromArgb(0x12, 0x16, 0x1B);
    private Color Dim => _dark ? Color.FromArgb(0x8A, 0x94, 0x9F) : Color.FromArgb(0x6B, 0x76, 0x81);
    private Color Accent => Palette.AccentFor(_ep.Id);

    private int IconSize => S(16);
    private Rectangle PencilRect => new(Width - S(44), (Height - IconSize) / 2, IconSize, IconSize);
    private Rectangle CheckRect => new(Width - S(22), (Height - IconSize) / 2, IconSize, IconSize);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool over = PencilRect.Contains(e.Location);
        if (over != _overPencil) { _overPencil = over; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _overPencil = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        Diagnostics.Log.Write("devicerow",
            $"click={e.Button} at {e.Location} gear={PencilRect} active={_active} dev={_name}");

        // Right-click anywhere on the row opens settings for it, so the gear is a
        // shortcut rather than the only way in.
        if (e.Button == MouseButtons.Right) { EditRequested?.Invoke(_ep.Id); return; }
        if (e.Button != MouseButtons.Left) return;

        if (PencilRect.Contains(e.Location)) EditRequested?.Invoke(_ep.Id);
        else if (!_active) Activate?.Invoke(_ep.Id);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Color.Transparent);

        int radius = S(6);

        // Three cues for "this is the one": rail, tint, check. Redundant on
        // purpose - it is the single question the whole app exists to answer.
        if (_active)
        {
            using var tint = new SolidBrush(Color.FromArgb(_dark ? 34 : 26, Accent));
            using var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), radius);
            g.FillPath(tint, path);
            using var rail = new SolidBrush(Accent);
            g.FillRectangle(rail, 0, S(3), S(3), Height - S(6));
        }
        else if (_hover)
        {
            using var hover = new SolidBrush(Color.FromArgb(_dark ? 16 : 13, _dark ? Color.White : Color.Black));
            using var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), radius);
            g.FillPath(hover, path);
        }

        int glyphX = S(10);
        g.DrawImage(_glyph, glyphX, (Height - _glyph.Height) / 2, _glyph.Width, _glyph.Height);

        int textLeft = glyphX + _glyph.Width + S(9);
        int textRight = Width - S(50);
        int wide = Math.Max(S(20), textRight - textLeft);

        using var nameFont = new Font("Segoe UI", Math.Max(1f, 12.5f * _scale),
            _active ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        using var subFont = new Font("Segoe UI", Math.Max(1f, 10.5f * _scale), GraphicsUnit.Pixel);
        using var fg = new SolidBrush(Fg);
        using var dim = new SolidBrush(Dim);

        var fmt = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
            LineAlignment = StringAlignment.Center
        };

        int half = Height / 2;
        g.DrawString(_name, nameFont, fg, new RectangleF(textLeft, S(2), wide, half), fmt);
        g.DrawString(_ep.Adapter, subFont, dim, new RectangleF(textLeft, half - S(2), wide, half), fmt);

        if (_hover) DrawGear(g, PencilRect, _overPencil ? Fg : Dim);
        if (_active) DrawCheck(g, CheckRect, Accent);
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = Math.Max(2, radius * 2);
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>
    /// A gear, not a pencil. The pencil implied "rename", but this opens glyph,
    /// colour, name and visibility, which is settings rather than editing text.
    /// </summary>
    private static void DrawGear(Graphics g, Rectangle r, Color c)
    {
        const int teeth = 7;
        float cx = r.X + r.Width / 2f;
        float cy = r.Y + r.Height / 2f;
        float outer = r.Width * 0.48f;
        float inner = r.Width * 0.30f;

        using var brush = new SolidBrush(c);
        using var pen = new Pen(c, Math.Max(1.1f, r.Width * 0.085f))
        { StartCap = LineCap.Round, EndCap = LineCap.Round };

        for (int i = 0; i < teeth; i++)
        {
            double a = i * (Math.PI * 2 / teeth);
            g.DrawLine(pen,
                cx + (float)Math.Cos(a) * inner, cy + (float)Math.Sin(a) * inner,
                cx + (float)Math.Cos(a) * outer, cy + (float)Math.Sin(a) * outer);
        }

        g.DrawEllipse(pen, cx - inner, cy - inner, inner * 2, inner * 2);

        // Punch the hub out rather than painting it: the row tint behind varies.
        float hub = r.Width * 0.13f;
        var old = g.CompositingMode;
        g.CompositingMode = CompositingMode.SourceCopy;
        using (var cut = new SolidBrush(Color.Transparent))
            g.FillEllipse(cut, cx - hub, cy - hub, hub * 2, hub * 2);
        g.CompositingMode = old;
    }

    private static void DrawCheck(Graphics g, Rectangle r, Color c)
    {
        using var pen = new Pen(c, Math.Max(1.5f, r.Width * 0.13f))
        { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen,
        [
            new PointF(r.X + r.Width * 0.18f, r.Y + r.Height * 0.52f),
            new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.76f),
            new PointF(r.X + r.Width * 0.84f, r.Y + r.Height * 0.26f)
        ]);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _glyph.Dispose();
        base.Dispose(disposing);
    }
}

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Infolume.Ui;

/// <summary>
/// A mute button, a drag-anywhere level track, and a percentage readout.
///
/// Hand-drawn rather than a TrackBar because TrackBar's chrome is a fixed size
/// that neither scales with DPI nor matches the rest of the panel.
/// </summary>
internal sealed class VolumeRow : Control
{
    private readonly bool _dark;
    private readonly float _scale;
    private readonly bool _big;

    private float _level;
    private bool _muted;
    private bool _dragging;

    internal event Action<float>? LevelChanged;
    internal event Action? MuteToggled;

    internal VolumeRow(float level, bool muted, bool dark, float scale, bool big = false)
    {
        _level = Math.Clamp(level, 0f, 1f);
        _muted = muted;
        _dark = dark;
        _scale = scale;
        _big = big;

        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    private int S(int logical) => Math.Max(1, (int)Math.Round(logical * _scale));

    private Color Fg => _dark ? Color.FromArgb(0xF0, 0xF3, 0xF6) : Color.FromArgb(0x12, 0x16, 0x1B);
    private Color Dim => _dark ? Color.FromArgb(0x98, 0xA3, 0xAE) : Color.FromArgb(0x66, 0x70, 0x7B);
    private Color Track => _dark ? Color.FromArgb(0x3B, 0x44, 0x4E) : Color.FromArgb(0xDC, 0xE3, 0xEA);
    private Color Fill => _dark ? Color.FromArgb(0xF0, 0xA9, 0x3B) : Color.FromArgb(0xA8, 0x57, 0x08);

    private int MuteSize => S(_big ? 18 : 15);
    private Rectangle MuteRect => new(0, (Height - MuteSize) / 2, MuteSize, MuteSize);
    private int ReadoutWidth => S(_big ? 40 : 34);

    private Rectangle TrackRect
    {
        get
        {
            int left = MuteRect.Right + S(10);
            int right = Width - ReadoutWidth - S(6);
            int h = S(_big ? 6 : 5);
            return Rectangle.FromLTRB(left, (Height - h) / 2, Math.Max(left + h, right), (Height + h) / 2);
        }
    }

    internal void Set(float level, bool muted)
    {
        _level = Math.Clamp(level, 0f, 1f);
        _muted = muted;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (MuteRect.Contains(e.Location)) { _muted = !_muted; MuteToggled?.Invoke(); Invalidate(); return; }

        // Generous hit area: the whole row height, not just the thin bar.
        var grab = TrackRect with { Y = 0, Height = Height };
        if (grab.Contains(e.Location)) { _dragging = true; Capture = true; SetFromX(e.X); }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) SetFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        float step = e.Delta > 0 ? 0.02f : -0.02f;
        float v = Math.Clamp(_level + step, 0f, 1f);
        if (Math.Abs(v - _level) < 0.001f) return;
        _level = v;
        LevelChanged?.Invoke(v);
        Invalidate();
    }

    private void SetFromX(int x)
    {
        var t = TrackRect;
        if (t.Width <= 0) return;
        float v = Math.Clamp((x - t.Left) / (float)t.Width, 0f, 1f);
        if (Math.Abs(v - _level) < 0.004f) return;
        _level = v;
        if (_muted) { _muted = false; MuteToggled?.Invoke(); }
        LevelChanged?.Invoke(v);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Color.Transparent);

        DrawSpeaker(g, MuteRect, _muted ? Icons.Palette.Muted : Fg, _muted);

        var t = TrackRect;
        using (var tb = new SolidBrush(Track)) FillPill(g, tb, t);
        if (!_muted && _level > 0f)
        {
            var f = t with { Width = Math.Max(t.Height, (int)(t.Width * _level)) };
            using var fb = new SolidBrush(Fill);
            FillPill(g, fb, f);

            int knob = S(_big ? 12 : 10);
            using var kb = new SolidBrush(Color.White);
            using var kp = new Pen(Color.FromArgb(90, 0, 0, 0));
            int kx = t.Left + (int)(t.Width * _level) - knob / 2;
            int ky = t.Top + t.Height / 2 - knob / 2;
            g.FillEllipse(kb, kx, ky, knob, knob);
            g.DrawEllipse(kp, kx, ky, knob, knob);
        }

        using var font = new Font("Consolas", Math.Max(1f, (_big ? 12f : 11f) * _scale), FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(_muted ? Dim : Fg);
        g.DrawString(_muted ? "mute" : $"{(int)Math.Round(_level * 100)}%", font, brush,
            new RectangleF(Width - ReadoutWidth, 0, ReadoutWidth, Height),
            new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center });
    }

    internal static void FillPill(Graphics g, Brush b, Rectangle r)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        int d = r.Height;
        if (r.Width <= d) { g.FillEllipse(b, r.X, r.Y, d, d); return; }
        using var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 90, 180);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
        p.CloseFigure();
        g.FillPath(b, p);
    }

    internal static void DrawSpeaker(Graphics g, Rectangle r, Color c, bool muted)
    {
        using var brush = new SolidBrush(c);
        using var pen = new Pen(c, Math.Max(1.2f, r.Width * 0.09f))
        { StartCap = LineCap.Round, EndCap = LineCap.Round };

        float x = r.X, y = r.Y, s = r.Width;
        using var cone = new GraphicsPath();
        cone.AddPolygon(new[]
        {
            new PointF(x + s * 0.06f, y + s * 0.36f),
            new PointF(x + s * 0.28f, y + s * 0.36f),
            new PointF(x + s * 0.52f, y + s * 0.10f),
            new PointF(x + s * 0.52f, y + s * 0.90f),
            new PointF(x + s * 0.28f, y + s * 0.64f),
            new PointF(x + s * 0.06f, y + s * 0.64f)
        });
        g.FillPath(brush, cone);

        if (muted)
        {
            g.DrawLine(pen, x + s * 0.64f, y + s * 0.32f, x + s * 0.94f, y + s * 0.68f);
            g.DrawLine(pen, x + s * 0.94f, y + s * 0.32f, x + s * 0.64f, y + s * 0.68f);
        }
        else
        {
            g.DrawArc(pen, x + s * 0.40f, y + s * 0.24f, s * 0.52f, s * 0.52f, -55, 110);
        }
    }
}

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Infolume.Ui;

/// <summary>
/// A vertical scroller with a thin drawn thumb instead of a native scrollbar.
///
/// WinForms' AutoScroll bar is a fixed-width system control that ignores the
/// panel's palette and, at high DPI, is wide enough to sit on top of the row
/// content beside it. This scrolls children by offsetting them and paints a
/// two-pixel-ish thumb over the right edge, which costs no layout width at all.
/// </summary>
internal sealed class ScrollHost : Panel
{
    private readonly bool _dark;
    private readonly float _scale;

    private int _offset;
    private int _contentHeight;
    private bool _dragging;
    private int _dragStartY;
    private int _dragStartOffset;

    internal ScrollHost(bool dark, float scale)
    {
        _dark = dark;
        _scale = scale;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    private int S(int logical) => Math.Max(1, (int)Math.Round(logical * _scale));

    private Color Thumb => _dark ? Color.FromArgb(0x5A, 0x65, 0x71) : Color.FromArgb(0xB6, 0xBF, 0xC9);
    private Color Trough => _dark ? Color.FromArgb(0x2C, 0x33, 0x3B) : Color.FromArgb(0xE8, 0xED, 0xF2);

    private int TrackWidth => S(4);
    private int TrackRight => Width - S(3);

    /// <summary>Total height of the laid-out children; set after adding them.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int ContentHeight
    {
        get => _contentHeight;
        set { _contentHeight = value; ClampOffset(); Invalidate(); }
    }

    internal bool Scrollable => _contentHeight > Height;

    /// <summary>Width children should occupy, leaving room for the thumb.</summary>
    internal int RowWidth => Width - (Scrollable ? S(9) : 0);

    private int MaxOffset => Math.Max(0, _contentHeight - Height);

    private void ClampOffset() => _offset = Math.Clamp(_offset, 0, MaxOffset);

    private void ApplyOffset()
    {
        SuspendLayout();
        foreach (Control c in Controls)
            c.Top = (int)c.Tag! - _offset;
        ResumeLayout();
        Invalidate();
    }

    /// <summary>
    /// Adds a row at its natural position. The position is stashed in Tag so
    /// scrolling can recompute Top without needing a second layout pass.
    /// </summary>
    internal void AddRow(Control c, int top)
    {
        c.Tag = top;
        c.Top = top - _offset;
        Controls.Add(c);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!Scrollable) return;
        int before = _offset;
        _offset -= Math.Sign(e.Delta) * S(30);
        ClampOffset();
        if (_offset != before) ApplyOffset();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Scrollable || e.X < TrackRight - TrackWidth * 2) return;
        _dragging = true;
        _dragStartY = e.Y;
        _dragStartOffset = _offset;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        float ratio = _contentHeight / (float)Height;
        _offset = _dragStartOffset + (int)((e.Y - _dragStartY) * ratio);
        ClampOffset();
        ApplyOffset();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!Scrollable) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int w = TrackWidth;
        int x = TrackRight - w;
        int pad = S(2);
        int trackH = Height - pad * 2;

        using (var tb = new SolidBrush(Trough))
            Pill(g, tb, new Rectangle(x, pad, w, trackH));

        int thumbH = Math.Max(S(18), (int)(trackH * (Height / (float)_contentHeight)));
        int travel = trackH - thumbH;
        int thumbY = pad + (MaxOffset == 0 ? 0 : (int)(travel * (_offset / (float)MaxOffset)));

        using var b = new SolidBrush(Thumb);
        Pill(g, b, new Rectangle(x, thumbY, w, thumbH));
    }

    private static void Pill(Graphics g, Brush b, Rectangle r)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        int d = r.Width;
        if (r.Height <= d) { g.FillEllipse(b, r); return; }
        using var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 180);
        p.AddArc(r.X, r.Bottom - d, d, d, 0, 180);
        p.CloseFigure();
        g.FillPath(b, p);
    }
}

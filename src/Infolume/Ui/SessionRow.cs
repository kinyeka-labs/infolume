using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Infolume.Audio;

namespace Infolume.Ui;

/// <summary>
/// One app playing on the current device: its real icon, its name, a mute
/// toggle, and a drag-anywhere volume track.
/// </summary>
internal sealed class SessionRow : Control
{
    private readonly SessionInfo _s;
    private readonly bool _dark;
    private readonly AudioEngine _engine;
    private readonly float _scale;

    private float _volume;
    private bool _muted;
    private bool _dragging;
    private bool _hover;

    internal SessionRow(SessionInfo s, bool dark, AudioEngine engine, float scale)
    {
        _s = s;
        _dark = dark;
        _engine = engine;
        _scale = scale;
        _volume = s.Volume;
        _muted = s.Muted;

        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    private int S(int logical) => Math.Max(1, (int)Math.Round(logical * _scale));

    private Color Fg => _dark ? Color.FromArgb(0xE6, 0xEA, 0xEF) : Color.FromArgb(0x1A, 0x1F, 0x25);
    private Color Dim => _dark ? Color.FromArgb(0x8A, 0x94, 0x9F) : Color.FromArgb(0x6B, 0x76, 0x81);
    private Color Track => _dark ? Color.FromArgb(0x3B, 0x44, 0x4E) : Color.FromArgb(0xDC, 0xE3, 0xEA);
    private Color Fill => _dark ? Color.FromArgb(0xF0, 0xA9, 0x3B) : Color.FromArgb(0xA8, 0x57, 0x08);

    private int IconSize => S(16);
    private Rectangle IconRect => new(S(8), (Height - IconSize) / 2, IconSize, IconSize);
    private Rectangle MuteRect => new(Width - S(122), (Height - S(14)) / 2, S(14), S(14));
    private int ReadoutWidth => S(32);

    private Rectangle TrackRect
    {
        get
        {
            int left = MuteRect.Right + S(8);
            int right = Width - ReadoutWidth - S(4);
            int h = S(5);
            return Rectangle.FromLTRB(left, (Height - h) / 2, Math.Max(left + h, right), (Height + h) / 2);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (MuteRect.Contains(e.Location))
        {
            _muted = !_muted;
            _engine.ToggleSessionMute(_s.Id);
            Invalidate();
            return;
        }
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

    private void SetFromX(int x)
    {
        var t = TrackRect;
        if (t.Width <= 0) return;
        float v = Math.Clamp((x - t.Left) / (float)t.Width, 0f, 1f);
        if (Math.Abs(v - _volume) < 0.005f) return;
        _volume = v;
        _engine.SetSessionVolume(_s.Id, v);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Color.Transparent);

        if (_hover)
        {
            using var hover = new SolidBrush(Color.FromArgb(_dark ? 13 : 11, _dark ? Color.White : Color.Black));
            g.FillRectangle(hover, 0, 0, Width, Height);
        }

        // The app's real icon, extracted from its executable.
        var ir = IconRect;
        if (_s.Icon is not null) g.DrawIcon(_s.Icon, ir);
        else
        {
            using var b = new SolidBrush(Dim);
            g.FillEllipse(b, ir);
        }

        using var font = new Font("Segoe UI", Math.Max(1f, 11.5f * _scale), GraphicsUnit.Pixel);
        using var fg = new SolidBrush(_muted ? Dim : Fg);
        var fmt = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
            LineAlignment = StringAlignment.Center
        };

        int textLeft = ir.Right + S(8);
        g.DrawString(_s.Name, font, fg,
            new RectangleF(textLeft, 0, Math.Max(S(20), MuteRect.Left - textLeft - S(6)), Height), fmt);

        VolumeRow.DrawSpeaker(g, MuteRect, _muted ? Icons.Palette.Muted : Dim, _muted);

        var t = TrackRect;
        using (var tb = new SolidBrush(Track)) VolumeRow.FillPill(g, tb, t);
        if (!_muted && _volume > 0f)
        {
            var f = t with { Width = Math.Max(t.Height, (int)(t.Width * _volume)) };
            using var fb = new SolidBrush(Fill);
            VolumeRow.FillPill(g, fb, f);
        }

        using var mono = new Font("Consolas", Math.Max(1f, 10.5f * _scale), GraphicsUnit.Pixel);
        using var dim = new SolidBrush(Dim);
        g.DrawString(_muted ? "off" : $"{(int)Math.Round(_volume * 100)}%", mono, dim,
            new RectangleF(Width - ReadoutWidth, 0, ReadoutWidth, Height),
            new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center });
    }
}

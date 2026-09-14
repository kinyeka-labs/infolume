using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Infolume.Icons;

namespace Infolume.Ui;

/// <summary>
/// The transient level readout shown while scrolling over the tray icon.
///
/// Three window styles matter more than the drawing here:
///
/// * <c>WS_EX_NOACTIVATE</c> - scrolling must never move focus. Without it a
///   scroll while typing would yank the caret out of the text box.
/// * <c>WS_EX_TRANSPARENT</c> - the OSD sits over the taskbar corner; clicks and
///   further scrolls have to pass straight through to whatever is underneath,
///   including the tray icon that raised it.
/// * <c>WS_EX_TOOLWINDOW</c> - keeps it out of Alt-Tab.
///
/// It is also TopMost, for the same reason everything else here is: otherwise it
/// can be painted while another window owns the z-order.
/// </summary>
internal sealed class OsdForm : Form
{
    private const int HoldMs = 900;
    private const int FadeStepMs = 25;
    private const double FadeStep = 0.12;

    private readonly System.Windows.Forms.Timer _hold = new();
    private readonly System.Windows.Forms.Timer _fade = new();

    private float _scale = 1f;
    private bool _dark = true;
    private GlyphKind _glyph = GlyphKind.Speakers;
    private Color _accent = Palette.Accents[0];
    private float _level;
    private bool _muted;
    private string _device = "";

    internal OsdForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        Opacity = 0;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

        _hold.Tick += (_, _) => { _hold.Stop(); _fade.Start(); };
        _fade.Interval = FadeStepMs;
        _fade.Tick += (_, _) =>
        {
            Opacity -= FadeStep;
            if (Opacity <= 0.01) { _fade.Stop(); Hide(); Dismissed?.Invoke(); }
        };
    }

    /// <summary>
    /// Raised once the readout is fully gone. The tray tooltip is suppressed while
    /// this is up, since the shell draws it in the same corner and the two overlap;
    /// this is the cue to put the tooltip back.
    /// </summary>
    internal event Action? Dismissed;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOPMOST = 0x00000008;
            const int WS_EX_TRANSPARENT = 0x00000020;
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_NOACTIVATE = 0x08000000;

            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    private int S(int logical) => Math.Max(1, (int)Math.Round(logical * _scale));

    private Color Bg => _dark ? Color.FromArgb(0x24, 0x2A, 0x31) : Color.FromArgb(0xF7, 0xF9, 0xFB);
    private Color Fg => _dark ? Color.FromArgb(0xF0, 0xF3, 0xF6) : Color.FromArgb(0x12, 0x16, 0x1B);
    private Color Dim => _dark ? Color.FromArgb(0x98, 0xA3, 0xAE) : Color.FromArgb(0x66, 0x70, 0x7B);
    private Color Track => _dark ? Color.FromArgb(0x3B, 0x44, 0x4E) : Color.FromArgb(0xDC, 0xE3, 0xEA);

    /// <summary>
    /// Shows (or refreshes) the readout. Called on every notch, so it restarts the
    /// hold timer rather than restarting the whole animation - a continuous scroll
    /// keeps it up, and it fades once you stop.
    /// </summary>
    internal void Show(Rectangle? iconRect, float scale, bool dark,
                       GlyphKind glyph, Color accent, float level, bool muted, string device)
    {
        _scale = scale <= 0 ? 1f : scale;
        _dark = dark;
        _glyph = glyph;
        _accent = accent;
        _level = Math.Clamp(level, 0f, 1f);
        _muted = muted;
        _device = device;

        Size = new Size(S(210), S(64));
        Location = TrayPositioning.Place(iconRect, Size, margin: S(10));

        _fade.Stop();
        Opacity = 1;
        _hold.Stop();
        _hold.Interval = HoldMs;
        _hold.Start();

        if (!Visible) ShowInactive();
        Invalidate();
    }

    /// <summary>Shows without taking focus. Show() alone would activate the window.</summary>
    private void ShowInactive()
    {
        const int SW_SHOWNOACTIVATE = 4;
        if (!IsHandleCreated) CreateControl();
        ShowWindow(Handle, SW_SHOWNOACTIVATE);
        Visible = true;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    internal void HideNow()
    {
        bool wasUp = Visible;
        _hold.Stop();
        _fade.Stop();
        Opacity = 0;
        Hide();
        if (wasUp) Dismissed?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Bg);

        int pad = S(12);
        int glyphSize = S(28);

        Glyphs.Draw(g, _glyph, pad, (Height - glyphSize) / 2f, glyphSize, _muted ? Palette.Muted : Fg);

        int textLeft = pad + glyphSize + S(12);
        int right = Width - pad;

        using var big = new Font("Segoe UI", Math.Max(1f, 22f * _scale), FontStyle.Bold, GraphicsUnit.Pixel);
        using var small = new Font("Segoe UI", Math.Max(1f, 10.5f * _scale), GraphicsUnit.Pixel);
        using var fg = new SolidBrush(_muted ? Palette.Muted : Fg);
        using var dim = new SolidBrush(Dim);

        string value = _muted ? "Muted" : $"{(int)Math.Round(_level * 100)}%";
        g.DrawString(value, big, fg, new PointF(textLeft, S(8)));

        var fmt = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
            Alignment = StringAlignment.Far
        };
        g.DrawString(_device, small, dim,
            new RectangleF(textLeft, S(10), right - textLeft, S(16)), fmt);

        // Level bar along the bottom, full width of the text column.
        int barY = Height - pad - S(6);
        var track = new Rectangle(textLeft, barY, right - textLeft, S(6));
        using (var tb = new SolidBrush(Track)) VolumeRow.FillPill(g, tb, track);
        if (!_muted && _level > 0f)
        {
            var fill = track with { Width = Math.Max(track.Height, (int)(track.Width * _level)) };
            using var fb = new SolidBrush(_accent);
            VolumeRow.FillPill(g, fb, fill);
        }

        using var border = new Pen(_dark ? Color.FromArgb(0x3A, 0x42, 0x4C) : Color.FromArgb(0xDD, 0xE3, 0xEA));
        g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _hold.Dispose(); _fade.Dispose(); }
        base.Dispose(disposing);
    }
}

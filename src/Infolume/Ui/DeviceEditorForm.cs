using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Infolume.Audio;
using Infolume.Config;
using Infolume.Icons;

namespace Infolume.Ui;

/// <summary>
/// Edits how each device is shown: its name, glyph, colour, and whether it is
/// listed at all.
///
/// It carries its own device picker rather than being bound to whichever row was
/// clicked, so all the devices can be set up in one sitting instead of opening
/// the window once per device.
/// </summary>
internal sealed class DeviceEditorForm : Form
{
    private readonly Settings _settings;
    private readonly bool _dark;
    private readonly float _scale;
    private readonly List<Font> _fonts = [];

    private readonly ComboBox _device;
    private readonly PictureBox _preview;
    private readonly TextBox _name;
    private readonly Label _adapter;
    private readonly Label _detected;
    private readonly List<GlyphCell> _cells = [];
    private readonly Button[] _modeButtons;
    private readonly CheckBox _hidden;

    private EndpointInfo _ep;
    private DeviceOverride _ov;
    private bool _loading;

    private static readonly GlyphKind[] AllGlyphs = Enum.GetValues<GlyphKind>();
    private const int Cols = 6;

    private int S(int logical) => Math.Max(1, (int)Math.Round(logical * _scale));

    private Font F(float px, FontStyle style = FontStyle.Regular)
    {
        var f = new Font("Segoe UI", Math.Max(1f, px * _scale), style, GraphicsUnit.Pixel);
        _fonts.Add(f);
        return f;
    }

    private Color Dim => _dark ? Color.FromArgb(0x8F, 0x99, 0xA4) : Color.FromArgb(0x69, 0x73, 0x7E);
    private Color Field => _dark ? Color.FromArgb(0x1E, 0x23, 0x29) : Color.White;
    private Color Rule => _dark ? Color.FromArgb(0x3A, 0x42, 0x4C) : Color.FromArgb(0xE4, 0xEA, 0xF0);
    private Color Accent => _dark ? Color.FromArgb(0xF0, 0xA9, 0x3B) : Color.FromArgb(0xA8, 0x57, 0x08);

    internal DeviceEditorForm(AudioEngine engine, Settings settings, string deviceId, bool dark, float scale)
    {
        _settings = settings;
        _dark = dark;
        _scale = scale;

        var devices = engine.Endpoints();
        _ep = devices.FirstOrDefault(d => d.Id == deviceId) ?? devices.First();
        _ov = settings.For(_ep.Id);

        Text = "Edit Infolume icons";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        TopMost = true;

        BackColor = dark ? Color.FromArgb(0x24, 0x2A, 0x31) : Color.FromArgb(0xF7, 0xF9, 0xFB);
        ForeColor = dark ? Color.FromArgb(0xF0, 0xF3, 0xF6) : Color.FromArgb(0x12, 0x16, 0x1B);
        Font = F(13f);

        int pad = S(14);
        int cell = S(44);
        int gap = S(6);
        int gridW = Cols * cell + (Cols - 1) * gap;
        int rows = (int)Math.Ceiling(AllGlyphs.Length / (double)Cols);
        int width = gridW + pad * 2;

        int y = pad;

        // Device picker first: this window edits any device, not just the one that
        // opened it.
        Controls.Add(new Label
        {
            Text = "DEVICE",
            Location = new Point(pad, y),
            Size = new Size(width - pad * 2, S(15)),
            ForeColor = Dim,
            Font = F(10f, FontStyle.Bold)
        });
        y += S(19);

        _device = new ComboBox
        {
            Location = new Point(pad, y),
            Size = new Size(width - pad * 2, S(24)),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Field,
            ForeColor = ForeColor,
            FlatStyle = FlatStyle.Flat,
            Font = F(13f)
        };
        foreach (var d in devices) _device.Items.Add(new Row(d, settings));
        _device.SelectedIndex = Math.Max(0, devices.ToList().FindIndex(d => d.Id == _ep.Id));
        _device.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _device.SelectedItem is not Row r) return;
            _ep = r.Endpoint;
            _ov = _settings.For(_ep.Id);
            LoadDevice();
        };
        Controls.Add(_device);
        y += S(34);

        Controls.Add(Divider(pad, y, width - pad * 2));
        y += S(12);

        int previewSize = S(64);
        _preview = new PictureBox
        {
            Location = new Point(pad, y),
            Size = new Size(previewSize, previewSize),
            SizeMode = PictureBoxSizeMode.StretchImage,
            BackColor = dark ? Color.FromArgb(0x1E, 0x22, 0x27) : Color.FromArgb(0xEE, 0xF1, 0xF4)
        };
        Controls.Add(_preview);

        int infoLeft = pad + previewSize + S(12);
        int infoWidth = width - pad - infoLeft;

        _name = new TextBox
        {
            Location = new Point(infoLeft, y + S(2)),
            Size = new Size(infoWidth, S(24)),
            Font = F(15f, FontStyle.Bold),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Field,
            ForeColor = ForeColor
        };
        _name.GotFocus += (_, _) => _name.SelectAll();
        _name.TextChanged += (_, _) =>
        {
            if (_loading) return;
            var v = _name.Text.Trim();
            // Storing the original as an alias would be noise, and would outlive a
            // later driver rename that should have shown through.
            _ov.Alias = (v.Length == 0 || v == _ep.Description) ? null : v;
            RefreshUi();
        };
        Controls.Add(_name);

        _adapter = new Label
        {
            Location = new Point(infoLeft, y + S(30)),
            Size = new Size(infoWidth, S(16)),
            ForeColor = Dim,
            Font = F(11.5f),
            AutoEllipsis = true
        };
        Controls.Add(_adapter);

        _detected = new Label
        {
            Location = new Point(infoLeft, y + S(48)),
            Size = new Size(infoWidth, S(16)),
            ForeColor = Dim,
            Font = F(10.5f),
            AutoEllipsis = true
        };
        Controls.Add(_detected);

        y += previewSize + S(16);
        Controls.Add(Divider(pad, y, width - pad * 2));
        y += S(12);

        y = AddSection("ICON", y, width, pad);

        for (int i = 0; i < AllGlyphs.Length; i++)
        {
            var c = new GlyphCell(AllGlyphs[i], dark, _scale)
            {
                Location = new Point(pad + (i % Cols) * (cell + gap), y + (i / Cols) * (cell + gap)),
                Size = new Size(cell, cell)
            };
            c.Picked += k => { _ov.Glyph = k; RefreshUi(); };
            _cells.Add(c);
            Controls.Add(c);
        }
        y += rows * (cell + gap) + S(2);

        y = AddSection("COLOUR", y, width, pad);

        int modeW = (width - pad * 2 - gap * 2) / 3;
        _modeButtons =
        [
            ModeButton("Theme",  ColorMode.Theme,  pad,                     y, modeW),
            ModeButton("Device", ColorMode.Device, pad + modeW + gap,       y, modeW),
            ModeButton("Custom", ColorMode.Custom, pad + (modeW + gap) * 2, y, modeW)
        ];
        foreach (var b in _modeButtons) Controls.Add(b);
        y += S(28) + S(8);

        int sw = S(24);
        int swGap = S(6);
        for (int i = 0; i < Palette.Accents.Length; i++)
        {
            var c = Palette.Accents[i];
            var b = new Button
            {
                Location = new Point(pad + i * (sw + swGap), y),
                Size = new Size(sw, sw),
                BackColor = c,
                FlatStyle = FlatStyle.Flat,
                TabStop = false
            };
            b.FlatAppearance.BorderSize = 1;
            b.Click += (_, _) =>
            {
                _ov.ColorMode = ColorMode.Custom;
                _ov.CustomColor = Settings.ToHex(c);
                RefreshUi();
            };
            Controls.Add(b);
        }

        var pick = new Button
        {
            Text = "...",
            Location = new Point(pad + Palette.Accents.Length * (sw + swGap), y),
            Size = new Size(sw, sw),
            FlatStyle = FlatStyle.Flat,
            Font = F(12f),
            TabStop = false
        };
        pick.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = Settings.ParseHex(_ov.CustomColor) ?? Palette.Accents[0] };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            _ov.ColorMode = ColorMode.Custom;
            _ov.CustomColor = Settings.ToHex(dlg.Color);
            RefreshUi();
        };
        Controls.Add(pick);
        y += sw + S(14);

        _hidden = new CheckBox
        {
            Text = "Hide from list",
            Location = new Point(pad, y),
            Size = new Size(S(130), S(24)),
            ForeColor = ForeColor,
            Font = F(12f)
        };
        _hidden.CheckedChanged += (_, _) => { if (!_loading) _ov.Hidden = _hidden.Checked; };
        Controls.Add(_hidden);

        var reset = new Button
        {
            Text = "Reset",
            Location = new Point(width - pad - S(160), y - S(2)),
            Size = new Size(S(74), S(26)),
            Font = F(12f)
        };
        reset.Click += (_, _) =>
        {
            _ov.Alias = null;
            _ov.Glyph = null;
            _ov.ColorMode = ColorMode.Theme;
            _ov.CustomColor = null;
            _ov.Hidden = false;
            LoadDevice();
        };
        Controls.Add(reset);

        var ok = new Button
        {
            Text = "Done",
            Location = new Point(width - pad - S(78), y - S(2)),
            Size = new Size(S(78), S(26)),
            DialogResult = DialogResult.OK,
            Font = F(12f)
        };
        Controls.Add(ok);
        AcceptButton = ok;
        y += S(34);

        Controls.Add(Divider(pad, y, width - pad * 2));
        y += S(8);
        Controls.Add(Branding.Create(pad, y, width - pad * 2, _dark, _scale, F(11f)));

        ClientSize = new Size(width, y + S(28));
        LoadDevice();
    }

    /// <summary>A device row in the picker, showing the alias and the adapter.</summary>
    private sealed record Row(EndpointInfo Endpoint, Settings Settings)
    {
        public override string ToString() =>
            $"{Settings.NameFor(Endpoint.Id, Endpoint.Description)}  ({Endpoint.Adapter})";
    }

    /// <summary>Points every control at the currently selected device.</summary>
    private void LoadDevice()
    {
        _loading = true;
        _name.Text = _settings.NameFor(_ep.Id, _ep.Description);
        _adapter.Text = _ep.Adapter;
        _detected.Text = $"Auto: {GlyphResolver.Resolve(_ep, new Settings())}  ·  "
                       + $"{_ep.FormFactor}  ·  {(string.IsNullOrEmpty(_ep.Enumerator) ? "unknown" : _ep.Enumerator)}";
        _hidden.Checked = _ov.Hidden;
        _loading = false;
        RefreshUi();
    }

    private Panel Divider(int x, int y, int w) => new()
    {
        Location = new Point(x, y),
        Size = new Size(w, 1),
        BackColor = Rule
    };

    private int AddSection(string text, int y, int width, int pad)
    {
        Controls.Add(new Label
        {
            Text = text,
            Location = new Point(pad, y),
            Size = new Size(width - pad * 2, S(15)),
            ForeColor = Dim,
            Font = F(10f, FontStyle.Bold)
        });
        return y + S(19);
    }

    private Button ModeButton(string text, ColorMode mode, int x, int y, int w)
    {
        var b = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, S(28)),
            FlatStyle = FlatStyle.Flat,
            Tag = mode,
            ForeColor = ForeColor,
            Font = F(12f)
        };
        b.Click += (_, _) => { _ov.ColorMode = mode; RefreshUi(); };
        return b;
    }

    private void RefreshUi()
    {
        var kind = GlyphResolver.Resolve(_ep, _settings);
        var colour = _settings.GlyphColorFor(_ep.Id, _dark);

        _preview.Image?.Dispose();
        _preview.Image = IconPreview(kind, colour);

        foreach (var c in _cells) c.Selected = _ov.Glyph == c.Kind;

        foreach (var b in _modeButtons)
        {
            bool on = (ColorMode)b.Tag! == _ov.ColorMode;
            b.FlatAppearance.BorderSize = on ? 2 : 1;
            b.FlatAppearance.BorderColor = on ? Accent : Rule;
            b.ForeColor = on ? Accent : ForeColor;
        }
    }

    /// <summary>Preview through the real painter, so it cannot drift from the tray.</summary>
    private Bitmap IconPreview(GlyphKind kind, Color colour)
    {
        var icon = IconPainter.Render(new IconState(
            Glyph: kind,
            GlyphColor: colour,
            AccentColor: Palette.AccentFor(_ep.Id),
            Volume: _ep.Volume,
            Muted: _ep.Muted,
            DarkTaskbar: _dark,
            Size: 48,
            Cue: _settings.Cue,
            Style: _settings.Style,
            InclineBars: _settings.InclineBars));

        try { return icon.ToBitmap(); }
        finally { IconPainter.DisposeIcon(icon); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _preview.Image?.Dispose();
            foreach (var f in _fonts) f.Dispose();
            _fonts.Clear();
        }
        base.Dispose(disposing);
    }
}

/// <summary>A single glyph swatch in the editor grid.</summary>
internal sealed class GlyphCell : Control
{
    internal GlyphKind Kind { get; }
    internal event Action<GlyphKind>? Picked;

    private readonly bool _dark;
    private readonly float _scale;
    private bool _selected;

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Selected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; Invalidate(); } }
    }

    internal GlyphCell(GlyphKind kind, bool dark, float scale)
    {
        Kind = kind;
        _dark = dark;
        _scale = scale;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Cursor = Cursors.Hand;
        Click += (_, _) => Picked?.Invoke(Kind);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(_dark ? Color.FromArgb(0x22, 0x28, 0x31) : Color.FromArgb(0xF4, 0xF7, 0xFA));

        var accent = _dark ? Color.FromArgb(0xF0, 0xA9, 0x3B) : Color.FromArgb(0xA8, 0x57, 0x08);
        float bw = Math.Max(1f, (Selected ? 2f : 1f) * _scale);
        using var border = new Pen(Selected ? accent
            : (_dark ? Color.FromArgb(0x41, 0x4A, 0x55) : Color.FromArgb(0xDF, 0xE5, 0xEC)), bw);
        g.DrawRectangle(border, bw / 2, bw / 2, Width - bw, Height - bw);

        var ink = _dark ? Color.FromArgb(0xE6, 0xEA, 0xEF) : Color.FromArgb(0x1A, 0x1F, 0x25);
        float s = Width * 0.60f;
        Glyphs.Draw(g, Kind, (Width - s) / 2f, (Height - s) / 2f, s, ink);
    }
}

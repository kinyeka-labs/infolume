using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Infolume.Config;
using Infolume.Icons;

namespace Infolume.Ui;

/// <summary>
/// Everything that used to be nested inside the tray menu.
///
/// A tray menu is for a handful of actions; it had grown four config submenus,
/// which is a settings panel wearing a menu's clothes. Here the choices sit
/// beside a live preview, so the effect of each one is visible while choosing.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly Settings _settings;
    private readonly bool _dark;
    private readonly float _scale;
    private readonly List<Font> _fonts = [];

    private readonly PictureBox _preview;
    private readonly ComboBox _bars;
    private readonly CheckBox _scrollToAdjust;
    private readonly NumericUpDown _step;
    private readonly CheckBox _autoStart;
    private readonly TrackBar _demo;

    internal event Action? Changed;

    private int S(int logical) => Math.Max(1, (int)Math.Round(logical * _scale));

    private Font F(float px, FontStyle style = FontStyle.Regular)
    {
        var f = new Font("Segoe UI", Math.Max(1f, px * _scale), style, GraphicsUnit.Pixel);
        _fonts.Add(f);
        return f;
    }

    private Color Bg => _dark ? Color.FromArgb(0x24, 0x2A, 0x31) : Color.FromArgb(0xF7, 0xF9, 0xFB);
    private Color Fg => _dark ? Color.FromArgb(0xF0, 0xF3, 0xF6) : Color.FromArgb(0x12, 0x16, 0x1B);
    private Color Dim => _dark ? Color.FromArgb(0x8A, 0x94, 0x9F) : Color.FromArgb(0x6B, 0x76, 0x81);
    private Color Field => _dark ? Color.FromArgb(0x1E, 0x23, 0x29) : Color.White;
    private Color Rule => _dark ? Color.FromArgb(0x3A, 0x42, 0x4C) : Color.FromArgb(0xE4, 0xEA, 0xF0);

    internal SettingsForm(Settings settings, bool dark, float scale)
    {
        _settings = settings;
        _dark = dark;
        _scale = scale;

        Text = "Infolume settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        TopMost = true;                      // same z-order lesson as everywhere else
        BackColor = Bg;
        ForeColor = Fg;
        Font = F(13f);

        int pad = S(16);
        int labelW = S(112);
        int fieldW = S(190);
        int rowH = S(34);
        int width = pad * 2 + labelW + fieldW;

        int y = pad;

        // Live preview: the icon exactly as the tray will paint it.
        _preview = new PictureBox
        {
            Location = new Point(pad, y),
            Size = new Size(S(64), S(64)),
            SizeMode = PictureBoxSizeMode.StretchImage,
            BackColor = _dark ? Color.FromArgb(0x1E, 0x22, 0x27) : Color.FromArgb(0xEE, 0xF1, 0xF4)
        };
        Controls.Add(_preview);

        _demo = new TrackBar
        {
            Location = new Point(pad + S(76), y + S(16)),
            Size = new Size(width - pad * 2 - S(76), S(32)),
            Minimum = 0,
            Maximum = 100,
            Value = 65,
            TickStyle = TickStyle.None
        };
        _demo.ValueChanged += (_, _) => RefreshPreview();
        Controls.Add(_demo);

        Controls.Add(new Label
        {
            Text = "Drag to preview the level",
            Location = new Point(pad + S(78), y + S(2)),
            Size = new Size(width - pad * 2 - S(78), S(16)),
            ForeColor = Dim,
            Font = F(11f)
        });

        y += S(76);
        Controls.Add(Divider(pad, y, width - pad * 2));
        y += S(12);

        AddRow("Level indicator", pad, ref y, labelW, fieldW, rowH,
        [
            ("Incline, wide", VolumeStyle.InclineWide),
            ("Incline, corner", VolumeStyle.Incline),
            ("Level meter", VolumeStyle.Equalizer),
            ("Segments", VolumeStyle.Segments),
            ("Solid bar", VolumeStyle.Bar),
            ("Side column", VolumeStyle.Column),
            ("Pips", VolumeStyle.Pips),
            ("Dial arc", VolumeStyle.Arc)
        ], _settings.Style, v => { _settings.Style = (VolumeStyle)v!; Apply(); });

        _bars = AddRow("Incline steps", pad, ref y, labelW, fieldW, rowH,
        [("4", 4), ("5", 5), ("6", 6), ("7", 7), ("8", 8)],
            _settings.InclineBars, v => { _settings.InclineBars = (int)v!; Apply(); });

        AddRow("Glyph cue", pad, ref y, labelW, fieldW, rowH,
        [
            ("None", AudioCue.None),
            ("Sound waves", AudioCue.Waves),
            ("Speaker + badge", AudioCue.SpeakerBadge)
        ], _settings.Cue, v => { _settings.Cue = (AudioCue)v!; Apply(); });

        AddRow("Icon theme", pad, ref y, labelW, fieldW, rowH,
        [
            ("Auto (follow Windows)", ThemeMode.Auto),
            ("Light taskbar", ThemeMode.ForceLightTaskbar),
            ("Dark taskbar", ThemeMode.ForceDarkTaskbar)
        ], _settings.Theme, v => { _settings.Theme = (ThemeMode)v!; Apply(); });

        AddRow("Icon size", pad, ref y, labelW, fieldW, rowH,
        [("Auto", 0), ("16 px", 16), ("20 px", 20), ("24 px", 24), ("28 px", 28),
         ("32 px", 32), ("36 px", 36), ("40 px", 40), ("48 px", 48)],
            _settings.IconSize, v => { _settings.IconSize = (int)v!; Apply(); });

        // Ahead of the step, because it governs it: with this off there is no
        // gesture for a step to size. Turning it off is what stops Infolume
        // registering for mouse input at all.
        _scrollToAdjust = new CheckBox
        {
            Text = "Scroll over the icon to change volume",
            Location = new Point(pad, y + S(2)),
            Size = new Size(width - pad * 2, S(24)),
            Checked = _settings.ScrollToAdjust,
            ForeColor = Fg,
            Font = F(13f)
        };
        _scrollToAdjust.CheckedChanged += (_, _) =>
        {
            _settings.ScrollToAdjust = _scrollToAdjust.Checked;
            Apply();
        };
        Controls.Add(_scrollToAdjust);
        y += S(30);

        Controls.Add(Label("Scroll step", pad, y, labelW));
        _step = new NumericUpDown
        {
            Location = new Point(pad + labelW, y + S(3)),
            Size = new Size(S(70), S(24)),
            Minimum = 1,
            Maximum = 25,
            Value = Math.Clamp(_settings.ScrollStep, 1, 25),
            BackColor = Field,
            ForeColor = Fg,
            BorderStyle = BorderStyle.FixedSingle,
            Font = F(13f)
        };
        _step.ValueChanged += (_, _) => { _settings.ScrollStep = (int)_step.Value; Apply(); };
        Controls.Add(_step);
        Controls.Add(new Label
        {
            Text = "% per notch",
            Location = new Point(pad + labelW + S(78), y + S(7)),
            Size = new Size(S(100), S(18)),
            ForeColor = Dim,
            Font = F(11f)
        });
        y += rowH;

        y += S(6);
        Controls.Add(Divider(pad, y, width - pad * 2));
        y += S(12);

        _autoStart = new CheckBox
        {
            Text = "Start with Windows",
            Location = new Point(pad, y),
            Size = new Size(width - pad * 2, S(24)),
            Checked = AutoStart.IsEnabled(),
            ForeColor = Fg,
            Font = F(13f)
        };
        _autoStart.CheckedChanged += (_, _) => AutoStart.Set(_autoStart.Checked);
        Controls.Add(_autoStart);
        y += S(34);

        var sound = new LinkLabel
        {
            Text = "Open Windows sound settings",
            Location = new Point(pad, y),
            Size = new Size(width - pad * 2, S(20)),
            LinkColor = Dim,
            ActiveLinkColor = Fg,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Font = F(12f),
            BackColor = Bg
        };
        sound.LinkClicked += (_, _) => OpenSoundSettings();
        Controls.Add(sound);
        y += S(30);

        var close = new Button
        {
            Text = "Close",
            Location = new Point(width - pad - S(90), y),
            Size = new Size(S(90), S(28)),
            DialogResult = DialogResult.OK,
            Font = F(13f)
        };
        Controls.Add(close);
        AcceptButton = close;

        Controls.Add(Branding.Create(pad, y + S(5), S(140), _dark, _scale, F(11f)));

        ClientSize = new Size(width, y + S(40));
        SyncEnabled();
        RefreshPreview();
    }

    private Label Label(string text, int x, int y, int w) => new()
    {
        Text = text,
        Location = new Point(x, y + S(6)),
        Size = new Size(w, S(20)),
        ForeColor = Fg,
        Font = F(13f)
    };

    private Panel Divider(int x, int y, int w) => new()
    {
        Location = new Point(x, y),
        Size = new Size(w, 1),
        BackColor = Rule
    };

    private ComboBox AddRow(string label, int pad, ref int y, int labelW, int fieldW, int rowH,
                            (string text, object value)[] items, object selected, Action<object?> onPick)
    {
        Controls.Add(Label(label, pad, y, labelW));

        var combo = new ComboBox
        {
            Location = new Point(pad + labelW, y + S(3)),
            Size = new Size(fieldW, S(24)),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Field,
            ForeColor = Fg,
            FlatStyle = FlatStyle.Flat,
            Font = F(13f)
        };
        foreach (var (text, value) in items) combo.Items.Add(new Item(text, value));
        combo.SelectedIndex = Math.Max(0, Array.FindIndex(items, i => Equals(i.value, selected)));
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (combo.SelectedItem is Item it) onPick(it.Value);
        };
        Controls.Add(combo);
        y += rowH;
        return combo;
    }

    private sealed record Item(string Text, object Value)
    {
        public override string ToString() => Text;
    }

    private void Apply()
    {
        _settings.Save();
        SyncEnabled();
        RefreshPreview();
        Changed?.Invoke();
    }

    /// <summary>
    /// Step count only means anything for the two incline styles, and scroll step
    /// only means anything while scroll-to-adjust is on.
    /// </summary>
    private void SyncEnabled()
    {
        _bars.Enabled = _settings.Style is VolumeStyle.Incline or VolumeStyle.InclineWide;
        _step.Enabled = _settings.ScrollToAdjust;
    }

    private void RefreshPreview()
    {
        _preview.Image?.Dispose();

        var icon = IconPainter.Render(new IconState(
            Glyph: GlyphKind.Speakers,
            GlyphColor: Palette.InkOn(_dark),
            AccentColor: Palette.Accents[0],
            Volume: _demo.Value / 100f,
            Muted: false,
            DarkTaskbar: _dark,
            Size: 48,
            Cue: _settings.Cue,
            Style: _settings.Style,
            InclineBars: _settings.InclineBars));

        try { _preview.Image = icon.ToBitmap(); }
        finally { IconPainter.DisposeIcon(icon); }
    }

    private static void OpenSoundSettings()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ms-settings:sound",
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Diagnostics.Log.Error("settings", ex);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
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

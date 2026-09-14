using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Infolume.Audio;
using Infolume.Config;
using Infolume.Icons;

namespace Infolume.Ui;

/// <summary>
/// The left-click panel: active device stated by name, a master slider, the
/// device list, then the apps playing on the current device.
///
/// Layout rules, both of which exist because this has to behave on any monitor at
/// any resolution and scaling:
///
/// 1. Every dimension AND every font is authored in logical units and multiplied
///    by one scale factor taken from the DPI of the monitor the flyout is about
///    to appear on. Fonts are created in <see cref="GraphicsUnit.Pixel"/> so the
///    framework never scales them a second time. Mixing point-sized fonts (which
///    scale themselves) with hardcoded pixel positions (which do not) is what
///    makes a panel look right at 100% and collapse into overlapping rows at 200%.
///
/// 2. The device list has priority and never scrolls - it is the reason the app
///    exists. The app mixer is secondary: it takes only the height left over,
///    caps after a few rows, and scrolls inside itself.
/// </summary>
internal sealed class FlyoutForm : Form
{
    // Logical units at 96 DPI. Nothing below is used raw - everything goes
    // through S() for sizes or F() for fonts.
    private const int PanelWidthL = 320;
    private const int PadL = 12;
    private const int HeaderHeightL = 92;
    private const int DeviceRowL = 44;
    private const int SessionRowL = 30;
    private const int SectionLabelL = 22;
    private const int FooterL = 32;

    /// <summary>App rows shown before the mixer starts scrolling.</summary>
    private const int MaxVisibleSessions = 4;

    /// <summary>The flyout never occupies more than this share of the work area.</summary>
    private const double MaxWorkAreaShare = 0.80;

    private readonly AudioEngine _engine;
    private readonly Settings _settings;
    private readonly Panel _content;
    private readonly List<Font> _fonts = [];

    private float _scale = 1f;
    private bool _dark = true;
    private bool _suppressEvents;

    /// <summary>The header's master slider, kept so level changes repaint just it.</summary>
    private VolumeRow? _master;

    private DateTime _ignoreDeactivateUntil = DateTime.MinValue;
    private DateTime _hiddenAt = DateTime.MinValue;
    private bool _editorOpen;

    internal event Action? SettingsChanged;

    /// <summary>Raised when the footer's Settings link is used.</summary>
    internal event Action? SettingsRequested;

    internal FlyoutForm(AudioEngine engine, Settings settings)
    {
        _engine = engine;
        _settings = settings;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        MinimizeBox = MaximizeBox = false;
        KeyPreview = true;
        DoubleBuffered = true;

        // A tray flyout has to sit above ordinary windows, both to be seen and to
        // be clicked: without this it can be painted on top while another window
        // still owns the z-order at those coordinates, so it receives no mouse
        // input at all and every click inside it silently does nothing.
        TopMost = true;

        // Scaling is done explicitly against the target monitor's DPI, so the
        // framework must not apply its own on top.
        AutoScaleMode = AutoScaleMode.None;

        _content = new Panel { Dock = DockStyle.Fill, AutoScroll = false };
        Controls.Add(_content);

        Deactivate += OnDeactivate;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) HideFlyout(); };
    }

    /// <summary>Logical units to physical pixels on the monitor being laid out for.</summary>
    private int S(int logical) => Math.Max(1, (int)Math.Round(logical * _scale));

    /// <summary>
    /// A font sized in physical pixels. Pixel units keep this independent of the
    /// form's own DeviceDpi, which can still be reporting the previous monitor's
    /// value at the moment layout runs.
    /// </summary>
    private Font F(float logicalPx, FontStyle style = FontStyle.Regular)
    {
        var f = new Font("Segoe UI", Math.Max(1f, logicalPx * _scale), style, GraphicsUnit.Pixel);
        _fonts.Add(f);
        return f;
    }

    private int PanelWidth => S(PanelWidthL);

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000;   // CS_DROPSHADOW
            return cp;
        }
    }

    /// <summary>
    /// Auto-hide when focus leaves - but not for the first moments after showing.
    /// Showing a window from a tray click briefly leaves focus with the shell, so
    /// an unguarded handler hides the flyout in the same gesture that opened it,
    /// and the click looks like it did nothing at all.
    /// </summary>
    private void OnDeactivate(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow < _ignoreDeactivateUntil)
        {
            Diagnostics.Log.Write("flyout", "deactivate ignored (within grace window)");
            return;
        }
        HideFlyout();
    }

    // --------------------------------------------------------------- theming

    private Color Bg => _dark ? Color.FromArgb(0x24, 0x2A, 0x31) : Color.FromArgb(0xF7, 0xF9, 0xFB);
    private Color Fg => _dark ? Color.FromArgb(0xF0, 0xF3, 0xF6) : Color.FromArgb(0x12, 0x16, 0x1B);
    private Color Dim => _dark ? Color.FromArgb(0x98, 0xA3, 0xAE) : Color.FromArgb(0x66, 0x70, 0x7B);
    private Color Rule => _dark ? Color.FromArgb(0x33, 0x3B, 0x45) : Color.FromArgb(0xE4, 0xEA, 0xF0);

    // ------------------------------------------------------------- lifecycle

    internal void Toggle(Rectangle? iconRect, bool darkTaskbar)
    {
        if (_editorOpen) return;
        if (Visible) { HideFlyout(); return; }

        // Clicking the icon while the flyout is open fires Deactivate first, which
        // hides it, and only then MouseUp - which would immediately reopen it. A
        // click just after a hide is therefore the tail of a close, not a new open.
        if ((DateTime.UtcNow - _hiddenAt).TotalMilliseconds < 250) return;

        _dark = darkTaskbar;
        _ignoreDeactivateUntil = DateTime.UtcNow.AddMilliseconds(400);

        // Resolve the destination monitor BEFORE measuring: its DPI drives every
        // dimension, and its work area caps the height.
        var screen = TrayPositioning.AnchorScreen(iconRect);
        _scale = TrayPositioning.AnchorDpi(iconRect) / 96f;

        Rebuild(screen.WorkingArea.Height);
        Location = TrayPositioning.Place(iconRect, Size);

        Diagnostics.Log.Write("flyout",
            $"showing at {Location} size={Size} scale={_scale:0.00} work={screen.WorkingArea}");

        Show();
        Activate();
        Native.NativeMethods.SetForegroundWindow(Handle);
    }

    private void HideFlyout()
    {
        Hide();
        _hiddenAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Repaints just the master slider from the engine. Used when the level moves
    /// while the panel is open, from the wheel or from anywhere else in Windows,
    /// so the panel stays truthful without a rebuild.
    /// </summary>
    internal void SyncLevel()
    {
        if (!Visible || _master is null) return;
        var ep = _engine.Current;
        if (ep is null) return;
        _master.Set(ep.Volume, ep.Muted);
    }

    /// <summary>
    /// Closes the panel and suppresses tray toggling while a modal window is up.
    /// Without this, tray clicks keep arriving during the dialog's nested message
    /// loop and the flyout reopens on top of its own modal.
    /// </summary>
    internal void HideForDialog()
    {
        HideFlyout();
        _editorOpen = true;
    }

    internal void DialogClosed()
    {
        _editorOpen = false;
        _hiddenAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Re-lays out while open - after a device switch or a mute toggle.
    ///
    /// Switching the default endpoint hands focus around (the shell reacts to the
    /// change), which can drop this window behind others and start the auto-hide
    /// timer, leaving it Visible but not actually on screen. So the z-order and
    /// the deactivate grace window are both re-asserted afterwards.
    /// </summary>
    /// <summary>
    /// Queues a relayout for the next message-loop turn.
    ///
    /// A rebuild disposes every row, including the one whose click handler is
    /// currently on the stack. Destroying a control from inside its own event
    /// unwinds into a disposed object and the rebuild dies half-finished, leaving
    /// an empty panel. Deferring lets the click return first.
    /// </summary>
    private void QueueRebuild()
    {
        if (!Visible || !IsHandleCreated) return;
        _ignoreDeactivateUntil = DateTime.UtcNow.AddMilliseconds(600);
        BeginInvoke(() => Diagnostics.Log.Guard("flyout", RebuildInPlace));
    }

    /// <summary>
    /// The display configuration changed under an open flyout. Its scale was
    /// taken from the DPI at the moment it was opened, so it is now wrong;
    /// rebuilding re-derives it. Closed flyouts need nothing - <see cref="Toggle"/>
    /// recomputes the scale on every open.
    /// </summary>
    internal void DisplayChanged() => QueueRebuild();

    private void RebuildInPlace()
    {
        if (!Visible) return;

        var anchor = Location;
        var wa = Screen.FromPoint(anchor).WorkingArea;

        // Re-derive the scale rather than reusing the one Toggle set. A rebuild
        // can happen a long way from the open - a device appearing, an icon
        // edited, or the display reconfigured under it - and by then the monitor
        // under the flyout may be at a different DPI. Reusing the old value lays
        // the panel out for a monitor it is no longer on.
        _scale = TrayPositioning.AnchorDpi(Bounds) / 96f;

        _ignoreDeactivateUntil = DateTime.UtcNow.AddMilliseconds(400);
        Rebuild(wa.Height);

        // Stay where it was opened, and only move if the rebuilt panel would now
        // run off the bottom of the work area.
        var placed = new Point(anchor.X, Math.Min(anchor.Y, wa.Bottom - Height - 8));
        Location = ClampTo(placed, Size, wa);

        TopMost = true;
        BringToFront();
        Activate();

        Diagnostics.Log.Write("flyout",
            $"rebuilt in place -> {Bounds} scale={_scale:0.00} visible={Visible}");
    }

    private static Point ClampTo(Point p, Size size, Rectangle work) => new(
        Math.Clamp(p.X, work.Left, Math.Max(work.Left, work.Right - size.Width)),
        Math.Clamp(p.Y, work.Top, Math.Max(work.Top, work.Bottom - size.Height)));

    // --------------------------------------------------------------- content

    private void Rebuild(int availableHeight)
    {
        _suppressEvents = true;
        _content.SuspendLayout();

        // Copy first: Control.Dispose removes the control from this very
        // collection, so disposing while enumerating it skips half the children.
        var stale = _content.Controls.Cast<Control>().ToArray();
        _content.Controls.Clear();
        foreach (var c in stale) c.Dispose();
        foreach (var f in _fonts) f.Dispose();
        _fonts.Clear();

        BackColor = Bg;
        _content.BackColor = Bg;
        Width = PanelWidth;

        var current = _engine.Current;
        var devices = VisibleDevices();
        var sessions = _engine.Sessions();

        // Budget: the device list is never sacrificed, so work out what it needs
        // first and give the app mixer only what is genuinely left over. On a
        // short screen with many devices this correctly yields zero.
        int fixedHeight = S(HeaderHeightL)
                        + S(SectionLabelL) + devices.Count * S(DeviceRowL)
                        + S(FooterL) + S(PadL);

        int budget = (int)(availableHeight * MaxWorkAreaShare);
        int sessionsHeight = 0;
        if (sessions.Count > 0)
        {
            int ceiling = budget - fixedHeight - S(SectionLabelL);
            int capped = Math.Min(sessions.Count, MaxVisibleSessions) * S(SessionRowL);
            sessionsHeight = Math.Max(0, Math.Min(capped, ceiling));
            // A scroller shorter than one row is useless; drop the section instead.
            if (sessionsHeight < S(SessionRowL)) sessionsHeight = 0;
        }

        int y = 0;
        y = BuildHeader(current, y);
        y = BuildSectionLabel("Output device", y);
        y = BuildDeviceList(devices, current, y);

        if (sessions.Count > 0)
        {
            // The header stays whatever happens, carrying the count, so a folded
            // section still tells you what is playing.
            y = BuildAppsHeader(sessions.Count, y);
            if (!_settings.AppsCollapsed && sessionsHeight > 0)
                y = BuildSessionList(sessions, y, sessionsHeight);
        }

        y = BuildFooter(y);

        Height = Math.Min(y, budget);
        _content.ResumeLayout();
        _suppressEvents = false;
    }

    private List<EndpointInfo> VisibleDevices()
    {
        var list = new List<EndpointInfo>();
        foreach (var ep in _engine.Endpoints())
        {
            // A hidden device is still shown while it is the active one -
            // otherwise the panel would claim you are on nothing.
            if (_settings.Overrides.TryGetValue(ep.Id, out var ov) && ov.Hidden && !ep.IsDefault)
                continue;
            list.Add(ep);
        }
        return list;
    }

    private int BuildHeader(EndpointInfo? ep, int y)
    {
        int pad = S(PadL);
        int glyph = S(30);

        var head = new Panel
        {
            Location = new Point(0, y),
            Size = new Size(PanelWidth, S(HeaderHeightL)),
            BackColor = Bg
        };

        var glyphBox = new PictureBox
        {
            Location = new Point(pad, S(12)),
            Size = new Size(glyph, glyph),
            SizeMode = PictureBoxSizeMode.StretchImage,
            BackColor = Color.Transparent
        };
        if (ep is not null) glyphBox.Image = RenderGlyphBitmap(ep, glyph);
        head.Controls.Add(glyphBox);

        int textLeft = pad + glyph + S(10);
        int textWidth = PanelWidth - textLeft - pad;

        head.Controls.Add(new Label
        {
            Text = ep is null ? "No output device" : _settings.NameFor(ep.Id, ep.Description),
            Location = new Point(textLeft, S(11)),
            Size = new Size(textWidth, S(18)),
            Font = F(14f, FontStyle.Bold),
            ForeColor = Fg,
            AutoEllipsis = true,
            BackColor = Color.Transparent
        });

        head.Controls.Add(new Label
        {
            Text = ep?.Adapter ?? "",
            Location = new Point(textLeft, S(30)),
            Size = new Size(textWidth, S(15)),
            Font = F(11f),
            ForeColor = Dim,
            AutoEllipsis = true,
            BackColor = Color.Transparent
        });

        // A custom row rather than a TrackBar: TrackBar's chrome is a fixed size
        // that does not scale cleanly and dwarfs everything else at high DPI.
        var master = new VolumeRow(ep?.Volume ?? 0f, ep?.Muted ?? false, _dark, _scale, big: true)
        {
            Location = new Point(pad, S(56)),
            Size = new Size(PanelWidth - pad * 2, S(28))
        };
        master.LevelChanged += v => { if (!_suppressEvents) _engine.SetVolume(v); };
        master.MuteToggled += () => { _engine.ToggleMute(); SyncLevel(); };
        head.Controls.Add(master);
        _master = master;

        _content.Controls.Add(head);
        return y + head.Height;
    }

    private int BuildSectionLabel(string text, int y)
    {
        _content.Controls.Add(new Label
        {
            Text = text.ToUpperInvariant(),
            Location = new Point(S(PadL), y + S(4)),
            Size = new Size(PanelWidth - S(PadL) * 2, S(14)),
            ForeColor = Dim,
            Font = F(10f, FontStyle.Bold),
            BackColor = Color.Transparent
        });
        return y + S(SectionLabelL);
    }

    /// <summary>A section label that folds the app mixer away, and remembers it.</summary>
    private int BuildAppsHeader(int count, int y)
    {
        var header = new SectionHeader(
            text: $"Apps on this device  ({count})",
            collapsed: _settings.AppsCollapsed,
            dark: _dark,
            scale: _scale)
        {
            Location = new Point(S(4), y),
            Size = new Size(PanelWidth - S(8), S(SectionLabelL))
        };
        header.Toggled += () =>
        {
            _settings.AppsCollapsed = !_settings.AppsCollapsed;
            _settings.Save();
            QueueRebuild();
        };
        _content.Controls.Add(header);
        return y + S(SectionLabelL);
    }

    private int BuildDeviceList(List<EndpointInfo> devices, EndpointInfo? current, int y)
    {
        int rowH = S(DeviceRowL);
        foreach (var ep in devices)
        {
            var row = new DeviceRow(ep, _settings.NameFor(ep.Id, ep.Description),
                                    ep.Id == current?.Id, _dark, RenderGlyphBitmap(ep, S(20)), _scale)
            {
                Location = new Point(S(4), y),
                Size = new Size(PanelWidth - S(8), rowH - S(2))
            };
            row.Activate += id =>
            {
                bool ok = _engine.SetDefault(id);
                Diagnostics.Log.Write("flyout", $"switch to {id} -> {(ok ? "ok" : "FAILED")}");
                if (ok) QueueRebuild();
            };
            row.EditRequested += EditDevice;
            _content.Controls.Add(row);
            y += rowH;
        }
        return y;
    }

    /// <summary>
    /// The app mixer, inside its own scroller. Secondary to the device list: it
    /// never grows the panel past its budget, it just scrolls.
    /// </summary>
    private int BuildSessionList(IReadOnlyList<SessionInfo> sessions, int y, int height)
    {
        int rowH = S(SessionRowL);

        var host = new ScrollHost(_dark, _scale)
        {
            Location = new Point(S(4), y),
            Size = new Size(PanelWidth - S(8), height),
            BackColor = Bg
        };
        host.ContentHeight = sessions.Count * rowH;

        int rowWidth = host.RowWidth;
        for (int i = 0; i < sessions.Count; i++)
        {
            host.AddRow(new SessionRow(sessions[i], _dark, _engine, _scale)
            {
                Left = 0,
                Size = new Size(rowWidth, rowH - S(2))
            }, i * rowH);
        }

        _content.Controls.Add(host);
        return y + height + S(4);
    }

    private int BuildFooter(int y)
    {
        int pad = S(PadL);
        int hidden = _settings.Overrides.Count(kv => kv.Value.Hidden);

        _content.Controls.Add(new Panel
        {
            Location = new Point(0, y + S(2)),
            Size = new Size(PanelWidth, Math.Max(1, S(1))),
            BackColor = Rule
        });

        _content.Controls.Add(new Label
        {
            Text = hidden == 1 ? "1 device hidden" : $"{hidden} devices hidden",
            Location = new Point(pad, y + S(9)),
            Size = new Size(PanelWidth - pad * 2 - S(150), S(16)),
            ForeColor = Dim,
            Font = F(11f),
            BackColor = Color.Transparent
        });

        AddFooterLink("Edit icons", PanelWidth - pad - S(166), y, S(66), () =>
        {
            var cur = _engine.Current;
            if (cur is not null) EditDevice(cur.Id);
        });

        AddFooterLink("Infolume settings", PanelWidth - pad - S(96), y, S(96), () =>
        {
            HideFlyout();
            SettingsRequested?.Invoke();
        });

        y += S(FooterL);

        // A second line for the app's own identity, plus the escape hatch to
        // Windows' sound page. Without this the panel says nothing about what it
        // belongs to.
        _content.Controls.Add(Branding.Create(pad, y - S(4), S(120), _dark, _scale, F(11f)));

        AddFooterLink("Windows sound settings", PanelWidth - pad - S(140), y - S(13), S(140), () =>
        {
            HideFlyout();
            OpenWindowsSoundSettings();
        });

        return y + S(20);
    }

    private void AddFooterLink(string text, int x, int y, int width, Action onClick)
    {
        var link = new LinkLabel
        {
            Text = text,
            Location = new Point(x, y + S(9)),
            Size = new Size(width, S(16)),
            TextAlign = ContentAlignment.MiddleRight,
            LinkColor = Dim,
            ActiveLinkColor = Fg,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Font = F(11f),
            BackColor = Bg
        };
        link.LinkClicked += (_, _) => onClick();
        _content.Controls.Add(link);
    }

    /// <summary>
    /// Opens the Windows sound page. The ms-settings URI needs ShellExecute; it is
    /// not a file path and will not start as a process.
    /// </summary>
    private static void OpenWindowsSoundSettings()
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
            Diagnostics.Log.Error("flyout", ex);
        }
    }

    // ---------------------------------------------------------------- editor

    private void EditDevice(string deviceId)
    {
        var ep = _engine.Endpoints().FirstOrDefault(e => e.Id == deviceId);
        if (ep is null) return;

        HideFlyout();

        // ShowDialog runs a nested message loop, but tray clicks keep arriving -
        // without this the flyout can be opened on top of its own modal editor.
        _editorOpen = true;
        try
        {
            Diagnostics.Log.Write("editor", $"opening for {ep.Description}");
            using var dlg = new DeviceEditorForm(_engine, _settings, ep.Id, _dark, _scale);
            var result = dlg.ShowDialog();
            Diagnostics.Log.Write("editor", $"closed result={result}");
            if (result == DialogResult.OK)
            {
                _settings.Save();
                SettingsChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error("editor", ex);
        }
        finally
        {
            _editorOpen = false;
            _hiddenAt = DateTime.UtcNow;
            Diagnostics.Log.Write("editor", "editor closed, tray re-armed");
        }
    }

    private Bitmap RenderGlyphBitmap(EndpointInfo ep, int size)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        var kind = GlyphResolver.Resolve(ep, _settings);
        var color = _settings.Overrides.TryGetValue(ep.Id, out var ov) && ov.ColorMode != ColorMode.Theme
            ? _settings.GlyphColorFor(ep.Id, _dark)
            : (ep.IsDefault ? Palette.AccentFor(ep.Id) : Dim);
        Glyphs.Draw(g, kind, size * 0.06f, size * 0.06f, size * 0.88f, color);
        return bmp;
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_LBUTTONDOWN = 0x0201;
        const int WM_MOUSEACTIVATE = 0x0021;
        const int WM_MOUSEWHEEL = 0x020A;

        if (m.Msg == WM_MOUSEWHEEL && HandleWheel(m)) return;

        // Diagnostic only: proves whether mouse input reaches this window at all.
        if (Diagnostics.Log.Enabled && (m.Msg == WM_LBUTTONDOWN || m.Msg == WM_MOUSEACTIVATE))
            Diagnostics.Log.Write("flyout", $"wndproc msg=0x{m.Msg:X4} lparam={m.LParam}");

        base.WndProc(ref m);
    }

    /// <summary>
    /// Scrolling anywhere on the panel changes the master volume, except over the
    /// app list, which scrolls itself, and over a slider, which takes its own.
    /// Returns true when the wheel was consumed here.
    /// </summary>
    private bool HandleWheel(Message m)
    {
        int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
        if (delta == 0) return false;

        var under = FindTarget(PointToClient(Cursor.Position));
        if (under is ScrollHost or SessionRow or VolumeRow) return false;

        var ep = _engine.Current;
        if (ep is null) return false;

        int notches = Math.Max(1, Math.Abs(delta) / 120);
        int step = Math.Max(1, _settings.ScrollStep) * notches;
        int pct = Math.Clamp((int)Math.Round(ep.Volume * 100) + (delta > 0 ? step : -step), 0, 100);
        _engine.SetVolume(pct / 100f);

        // Repaint the one control that changed. Rebuilding the panel per notch
        // tore down and recreated every row, which reads as the whole window
        // flickering rather than a slider moving.
        _master?.Set(pct / 100f, muted: false);

        // No readout: the panel is already showing the level.
        return true;
    }

    private Control? FindTarget(Point clientPoint)
    {
        Control? c = _content.GetChildAtPoint(clientPoint);
        while (c is not null)
        {
            if (c is ScrollHost or SessionRow or VolumeRow) return c;
            var inner = c.GetChildAtPoint(new Point(clientPoint.X - c.Left, clientPoint.Y - c.Top));
            if (inner is null || ReferenceEquals(inner, c)) return c;
            clientPoint = new Point(clientPoint.X - c.Left, clientPoint.Y - c.Top);
            c = inner;
        }
        return null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Rule);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var f in _fonts) f.Dispose();
            _fonts.Clear();
        }
        base.Dispose(disposing);
    }
}

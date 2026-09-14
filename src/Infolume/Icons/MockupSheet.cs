using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Infolume.Audio;
using Infolume.Config;
using Infolume.Ui;

namespace Infolume.Icons;

/// <summary>
/// Renders the real flyout against invented-but-plausible hardware, for the
/// README.
///
/// The machine this was built on has four endpoints and three of them are
/// virtual - two Steam Streaming sinks and an AudioRelay cable. A screenshot of
/// that is accurate and useless: it tells a reader nothing about what the app
/// looks like on their own machine, and the one physical device is an HDMI TV.
///
/// So this substitutes a set of devices people actually own and shows the real
/// panel rendering them. Nothing about the UI is faked - it is the shipping
/// <see cref="FlyoutForm"/>, laid out by the shipping code, at the DPI of the
/// monitor it appears on. Only the device list is invented, and even that only
/// supplies the properties Windows would supply: form factor, enumerator, jack
/// type. The glyphs are then chosen by the real <see cref="GlyphResolver"/>, so
/// the picture cannot show a pairing the app would not actually produce.
/// </summary>
internal static class MockupSheet
{
    internal static void Write(string panelPath, string trayPath)
    {
        var endpoints = MockEndpoints();
        WritePanel(panelPath, endpoints);
        WriteTrayStrip(trayPath, endpoints);
    }

    /// <summary>
    /// The one device list both images are built from, so the tray strip and the
    /// panel can never drift into showing different hardware.
    /// </summary>
    private static List<EndpointInfo> MockEndpoints()
    {
        // Real MMDevice IDs are "{0.0.0.00000000}.{guid}". Palette.AccentFor hashes
        // the ID for the per-device accent, so these GUIDs are chosen to land on four
        // DIFFERENT accents - the point of the image is that devices are
        // distinguishable at a glance, and two greens undercut it. Any real device
        // has an arbitrary ID too; picking arbitrary ones that read well is not
        // misrepresenting the hash, and the colours are still whatever it returns.
        return new List<EndpointInfo>
        {
            new()
            {
                Id = "{0.0.0.00000000}.{0005b2c3-0000-4000-8000-000000000000}",
                Description = "AirPods Pro",
                Adapter = "Bluetooth Audio",
                FormFactor = FormFactor.Headphones,
                Enumerator = "BTHENUM",          // -> BluetoothHeadphones
                Volume = 0.58f,
                IsDefault = true
            },
            new()
            {
                Id = "{0.0.0.00000000}.{0004b2c3-0000-4000-8000-000000000000}",
                Description = "Speakers",
                Adapter = "Realtek(R) Audio",
                FormFactor = FormFactor.Speakers,
                Enumerator = "HDAUDIO",          // -> Speakers
                Volume = 0.34f
            },
            new()
            {
                Id = "{0.0.0.00000000}.{0006b2c3-0000-4000-8000-000000000000}",
                Description = "Arctis Nova Pro",
                Adapter = "SteelSeries USB Audio",
                FormFactor = FormFactor.Headset,
                Enumerator = "USB",              // -> Headset
                Volume = 0.72f
            },
            new()
            {
                Id = "{0.0.0.00000000}.{0002b2c3-0000-4000-8000-000000000000}",
                Description = "LG C3 OLED",
                Adapter = "NVIDIA High Definition Audio",
                FormFactor = FormFactor.DigitalAudioDisplayDevice,
                Enumerator = "PCI",              // -> Tv
                Volume = 1.0f
            }
        };
    }

    // ------------------------------------------------------------- the panel

    private static void WritePanel(string path, List<EndpointInfo> endpoints)
    {
        var settings = new Settings { AppsCollapsed = false };

        // No icons: SessionInfo.Icon is nullable and the row draws a placeholder,
        // which is what an app whose executable cannot be read looks like anyway.
        // Extracting real ones would put whatever is installed here into the image.
        var sessions = new List<SessionInfo>
        {
            new() { Id = "mock:1", Name = "Spotify",       Volume = 0.80f, Muted = false },
            new() { Id = "mock:2", Name = "Firefox",       Volume = 0.55f, Muted = false },
            new() { Id = "mock:3", Name = "Discord",       Volume = 0.40f, Muted = true  },
            new() { Id = "mock:4", Name = "System sounds", Volume = 0.65f, Muted = false, IsSystemSounds = true }
        };

        using var engine = new AudioEngine();
        engine.UseMockData(endpoints, sessions);

        using var flyout = new FlyoutForm(engine, settings);

        // Anchor to a synthetic tray icon in the corner of the working area, so
        // placement runs through the same TrayPositioning path a real click does
        // rather than being hand-positioned.
        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        var iconRect = new Rectangle(work.Right - 220, work.Bottom - 20, 24, 24);

        flyout.Toggle(iconRect, darkTaskbar: true);

        // The form is shown but nothing is pumping messages, so it has not painted
        // yet. Pump until it has.
        for (int i = 0; i < 20; i++)
        {
            Application.DoEvents();
            Thread.Sleep(25);
        }

        using var bmp = new Bitmap(flyout.Width, flyout.Height, PixelFormat.Format32bppArgb);
        flyout.DrawToBitmap(bmp, new Rectangle(0, 0, flyout.Width, flyout.Height));
        bmp.Save(path, ImageFormat.Png);

        Diagnostics.Log.Write("mockup", $"wrote {path} ({bmp.Width}x{bmp.Height})");
        Console.WriteLine($"{path}  {bmp.Width}x{bmp.Height}");
    }

    // --------------------------------------------------------- the tray strip

    /// <summary>
    /// The thing the app is actually for: one tray slot, and what it looks like
    /// for each device. The README can assert that the icon tells you the device
    /// and the level; only this shows it.
    ///
    /// Rendered larger than a real tray icon. It is the same painter and the same
    /// design - <see cref="IconPainter"/> draws to whatever size it is given - but
    /// a literal 32px row would be unreadable at README scale. The reasoning for
    /// six steps at 32px is in "The icon" above.
    /// </summary>
    private static void WriteTrayStrip(string path, List<EndpointInfo> endpoints)
    {
        var settings = new Settings();
        const int iconPx = 128;
        const int gap = 44;
        const int pad = 40;
        const int labelGap = 16;
        const int nameH = 26;
        const int levelH = 24;

        // Each device, then the active one again muted - silence is a state the
        // icon has to communicate too, and it is not obvious it can.
        var cells = new List<(EndpointInfo Ep, bool Muted)>();
        foreach (var ep in endpoints) cells.Add((ep, false));
        cells.Add((endpoints[0], true));

        int w = pad * 2 + cells.Count * iconPx + (cells.Count - 1) * gap;
        int h = pad * 2 + iconPx + labelGap + nameH + levelH;

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Windows 11's dark taskbar, so the icons sit on the ground they were
            // designed against rather than an invented one.
            using (var back = new SolidBrush(Color.FromArgb(0x20, 0x20, 0x20)))
                g.FillRectangle(back, 0, 0, w, h);

            // GraphicsUnit.Pixel throughout, per the rule in "Scaling and
            // resolution": a point-sized font would rescale itself against the
            // screen DPI and break the fixed layout below.
            using var nameFont = new Font("Segoe UI", 19f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var levelFont = new Font("Segoe UI", 17f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var nameBrush = new SolidBrush(Color.FromArgb(0xF2, 0xF4, 0xF6));
            using var dimBrush = new SolidBrush(Color.FromArgb(0x9A, 0xA3, 0xAD));
            using var fmt = new StringFormat { Alignment = StringAlignment.Center };

            int x = pad;
            foreach (var (ep, muted) in cells)
            {
                // Exactly what TrayApp.Repaint builds, so the strip cannot show a
                // combination the tray would not.
                var state = new IconState(
                    Glyph: GlyphResolver.Resolve(ep, settings),
                    GlyphColor: settings.GlyphColorFor(ep.Id, darkTaskbar: true),
                    AccentColor: Palette.AccentFor(ep.Id),
                    Volume: ep.Volume,
                    Muted: muted,
                    DarkTaskbar: true,
                    Size: iconPx,
                    Cue: settings.Cue,
                    Style: settings.Style,
                    InclineBars: settings.InclineBars);

                var icon = IconPainter.Render(state);
                try { g.DrawIcon(icon, new Rectangle(x, pad, iconPx, iconPx)); }
                finally { IconPainter.DisposeIcon(icon); }

                var centre = new RectangleF(x - gap / 2f, pad + iconPx + labelGap, iconPx + gap, nameH);
                g.DrawString(ep.Description, nameFont, nameBrush, centre, fmt);

                string level = muted ? "muted" : $"{(int)Math.Round(ep.Volume * 100)}%";
                g.DrawString(level, levelFont, dimBrush,
                    new RectangleF(centre.X, centre.Y + nameH, centre.Width, levelH), fmt);

                x += iconPx + gap;
            }
        }

        bmp.Save(path, ImageFormat.Png);
        Diagnostics.Log.Write("mockup", $"wrote {path} ({bmp.Width}x{bmp.Height})");
        Console.WriteLine($"{path}  {bmp.Width}x{bmp.Height}");
    }
}

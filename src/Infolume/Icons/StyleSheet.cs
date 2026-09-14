using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Infolume.Icons;

/// <summary>
/// Renders the device set under each <see cref="AudioCue"/> so the treatments can
/// be compared at the size they will actually be seen.
/// </summary>
internal static class StyleSheet
{
    private static readonly GlyphKind[] Shown =
        [GlyphKind.Speakers, GlyphKind.Headphones, GlyphKind.BluetoothHeadphones,
         GlyphKind.Tv, GlyphKind.Monitor, GlyphKind.Virtual];

    private static readonly (AudioCue cue, string name, string note)[] Treatments =
    [
        (AudioCue.None,         "A — plain device glyph",  "current: reads as display / network"),
        (AudioCue.Waves,        "B — device + waves",       "device stays primary, waves say audio"),
        (AudioCue.SpeakerBadge, "C — speaker + badge",      "always reads as sound; device is secondary")
    ];

    internal static void Write(string path, int trayPx)
    {
        const int mag = 4;
        int cell = trayPx * mag;
        int pad = 16;
        int label = 20;
        int head = 34;
        int cols = Shown.Length;

        int w = pad * 2 + cols * cell + (cols - 1) * pad;
        int h = pad * 2 + Treatments.Length * (head + cell + label + pad + trayPx + 14);

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.FromArgb(0x18, 0x1C, 0x21));

        using var hFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var nFont = new Font("Segoe UI", 8f);
        using var sFont = new Font("Segoe UI", 7.5f);
        using var white = new SolidBrush(Color.FromArgb(0xE8, 0xEC, 0xF0));
        using var dim = new SolidBrush(Color.FromArgb(0x8A, 0x94, 0x9F));

        int y = pad;

        foreach (var (cue, name, note) in Treatments)
        {
            g.DrawString(name, hFont, white, pad, y);
            g.DrawString(note, nFont, dim, pad + 200, y + 2);
            y += head;

            for (int i = 0; i < cols; i++)
            {
                int cx = pad + i * (cell + pad);

                using (var cellBg = new SolidBrush(Color.FromArgb(0x22, 0x26, 0x2B)))
                    g.FillRectangle(cellBg, cx, y, cell, cell);

                using var icon = Render(Shown[i], cue, trayPx, 0.62f);
                var oldI = g.InterpolationMode;
                var oldP = g.PixelOffsetMode;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using (var b = icon.ToBitmap()) g.DrawImage(b, new Rectangle(cx, y, cell, cell));
                g.InterpolationMode = oldI;
                g.PixelOffsetMode = oldP;
                IconPainter.DisposeIcon(icon);

                g.DrawString(Shown[i].ToString(), sFont, dim,
                    new RectangleF(cx - pad / 2f, y + cell + 2, cell + pad, label),
                    new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter });
            }
            y += cell + label + 6;

            // True-size strip on a taskbar-coloured ground.
            using (var barBg = new SolidBrush(Color.FromArgb(0x1F, 0x22, 0x26)))
                g.FillRectangle(barBg, pad, y, w - pad * 2, trayPx + 8);
            int x = pad + 8;
            foreach (var kind in Shown)
            {
                using var icon = Render(kind, cue, trayPx, 0.62f);
                g.DrawIcon(icon, new Rectangle(x, y + 4, trayPx, trayPx));
                IconPainter.DisposeIcon(icon);
                x += trayPx + 14;
            }
            y += trayPx + 8 + pad;
        }

        bmp.Save(path, ImageFormat.Png);
    }

    private static Icon Render(GlyphKind kind, AudioCue cue, int size, float level) =>
        IconPainter.Render(new IconState(
            Glyph: kind,
            GlyphColor: Palette.InkOn(true),
            AccentColor: Palette.AccentFor(kind.ToString()),
            Volume: level,
            Muted: false,
            DarkTaskbar: true,
            Size: size,
            Cue: cue));
}

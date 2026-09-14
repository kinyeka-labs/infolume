using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Infolume.Icons;

/// <summary>
/// Compares the volume indicators at the size they will actually be seen, across
/// the levels that matter - including the two that are easy to get wrong: silent
/// and full.
/// </summary>
internal static class VolumeSheet
{
    private static readonly float[] Levels = [0f, 0.15f, 0.35f, 0.60f, 0.85f, 1.0f];

    private static readonly (VolumeStyle style, string name, string note)[] Styles =
    [
        (VolumeStyle.Incline,     "A — incline, corner", "staircase over the glyph; glyph keeps full size"),
        (VolumeStyle.InclineWide, "B — incline, wide",   "same staircase spanning the width"),
        (VolumeStyle.Equalizer,   "C — level meter",     "for comparison"),
        (VolumeStyle.Bar,         "D — solid bar",       "current")
    ];

    internal static void Write(string path, int trayPx, GlyphKind glyph, AudioCue cue)
    {
        const int mag = 5;
        int cell = trayPx * mag;
        int pad = 16;
        int head = 30;
        int cols = Levels.Length;

        int w = pad * 2 + cols * cell + (cols - 1) * pad;
        int rowH = head + cell + pad + trayPx + 20;
        int h = pad * 2 + Styles.Length * rowH;

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.FromArgb(0x18, 0x1C, 0x21));

        using var hFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        using var nFont = new Font("Segoe UI", 8f);
        using var sFont = new Font("Segoe UI", 7.5f);
        using var white = new SolidBrush(Color.FromArgb(0xE8, 0xEC, 0xF0));
        using var dim = new SolidBrush(Color.FromArgb(0x8A, 0x94, 0x9F));

        int y = pad;

        foreach (var (style, name, note) in Styles)
        {
            g.DrawString(name, hFont, white, pad, y);
            g.DrawString(note, nFont, dim, pad + 150, y + 3);
            y += head;

            for (int i = 0; i < cols; i++)
            {
                int cx = pad + i * (cell + pad);
                using (var bg = new SolidBrush(Color.FromArgb(0x22, 0x26, 0x2B)))
                    g.FillRectangle(bg, cx, y, cell, cell);

                using var icon = Make(glyph, cue, style, trayPx, Levels[i]);
                var oi = g.InterpolationMode; var op = g.PixelOffsetMode;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using (var b = icon.ToBitmap()) g.DrawImage(b, new Rectangle(cx, y, cell, cell));
                g.InterpolationMode = oi; g.PixelOffsetMode = op;
                IconPainter.DisposeIcon(icon);

                g.DrawString($"{(int)(Levels[i] * 100)}%", sFont, dim,
                    new RectangleF(cx, y + cell + 2, cell, 18),
                    new StringFormat { Alignment = StringAlignment.Center });
            }
            y += cell + 20;

            using (var barBg = new SolidBrush(Color.FromArgb(0x1F, 0x22, 0x26)))
                g.FillRectangle(barBg, pad, y, w - pad * 2, trayPx + 8);
            int x = pad + 10;
            foreach (var lv in Levels)
            {
                using var icon = Make(glyph, cue, style, trayPx, lv);
                g.DrawIcon(icon, new Rectangle(x, y + 4, trayPx, trayPx));
                IconPainter.DisposeIcon(icon);
                x += trayPx + 16;
            }
            // Muted, for comparison, at the end of each strip.
            using (var muted = IconPainter.Render(new IconState(
                       glyph, Palette.InkOn(true), Palette.Accents[0], 0.5f, true, true, trayPx, cue, style)))
            {
                g.DrawIcon(muted, new Rectangle(x + 14, y + 4, trayPx, trayPx));
                IconPainter.DisposeIcon(muted);
            }
            g.DrawString("muted", sFont, dim, x + 6, y + trayPx + 8);

            y += trayPx + 8 + pad;
        }

        bmp.Save(path, ImageFormat.Png);
    }

    private static Icon Make(GlyphKind glyph, AudioCue cue, VolumeStyle style, int size, float level) =>
        IconPainter.Render(new IconState(
            Glyph: glyph,
            GlyphColor: Palette.InkOn(true),
            AccentColor: Palette.Accents[0],
            Volume: level,
            Muted: false,
            DarkTaskbar: true,
            Size: size,
            Cue: cue,
            Style: style));
}

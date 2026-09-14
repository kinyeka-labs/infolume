using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Infolume.Icons;

/// <summary>
/// Sweeps the incline staircase across bar counts, full width, so the point where
/// the steps stop resolving at real tray size is a measured result rather than a
/// guess.
/// </summary>
internal static class InclineSheet
{
    private static readonly float[] Levels = [0f, 0.25f, 0.5f, 0.75f, 1.0f];
    private static readonly int[] Counts = [4, 5, 6, 7, 8];

    internal static void Write(string path, int trayPx, GlyphKind glyph)
    {
        const int mag = 6;
        int cell = trayPx * mag;
        int pad = 16;
        int head = 28;
        int cols = Levels.Length;

        int w = pad * 2 + cols * cell + (cols - 1) * pad;
        int rowH = head + cell + 18 + pad + trayPx + 18;
        int h = pad * 2 + Counts.Length * rowH;

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

        foreach (int count in Counts)
        {
            float barPx = (trayPx * 0.92f - Math.Max(1.2f, trayPx * 0.045f) * (count - 1)) / count;
            g.DrawString($"{count} bars", hFont, white, pad, y);
            g.DrawString($"each bar ≈ {barPx:0.0} px at {trayPx}px", nFont, dim, pad + 90, y + 3);
            y += head;

            for (int i = 0; i < cols; i++)
            {
                int cx = pad + i * (cell + pad);
                using (var bg = new SolidBrush(Color.FromArgb(0x22, 0x26, 0x2B)))
                    g.FillRectangle(bg, cx, y, cell, cell);

                using var icon = Make(glyph, count, trayPx, Levels[i]);
                var oi = g.InterpolationMode; var op = g.PixelOffsetMode;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using (var b = icon.ToBitmap()) g.DrawImage(b, new Rectangle(cx, y, cell, cell));
                g.InterpolationMode = oi; g.PixelOffsetMode = op;
                IconPainter.DisposeIcon(icon);

                g.DrawString($"{(int)(Levels[i] * 100)}%", sFont, dim,
                    new RectangleF(cx, y + cell + 2, cell, 16),
                    new StringFormat { Alignment = StringAlignment.Center });
            }
            y += cell + 18;

            using (var barBg = new SolidBrush(Color.FromArgb(0x1F, 0x22, 0x26)))
                g.FillRectangle(barBg, pad, y, w - pad * 2, trayPx + 8);
            int x = pad + 10;
            for (int i = 0; i <= 8; i++)
            {
                using var icon = Make(glyph, count, trayPx, i / 8f);
                g.DrawIcon(icon, new Rectangle(x, y + 4, trayPx, trayPx));
                IconPainter.DisposeIcon(icon);
                x += trayPx + 12;
            }
            y += trayPx + 8 + pad;
        }

        bmp.Save(path, ImageFormat.Png);
    }

    private static Icon Make(GlyphKind glyph, int count, int size, float level) =>
        IconPainter.Render(new IconState(
            Glyph: glyph,
            GlyphColor: Palette.InkOn(true),
            AccentColor: Palette.Accents[0],
            Volume: level,
            Muted: false,
            DarkTaskbar: true,
            Size: size,
            Cue: AudioCue.None,
            Style: VolumeStyle.InclineWide,
            InclineBars: count));
}

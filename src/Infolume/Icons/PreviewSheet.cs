using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Infolume.Icons;

/// <summary>
/// Renders every glyph through the real <see cref="IconPainter"/> onto one sheet.
/// A development aid: the tray cannot be screenshotted on this machine, so this is
/// how the shipped painting code gets eyes on it.
/// </summary>
internal static class PreviewSheet
{
    internal static void Write(string path, int trayPx)
    {
        var kinds = Enum.GetValues<GlyphKind>();
        const int mag = 4;
        int cell = trayPx * mag;
        int cols = 6;
        int rows = (int)Math.Ceiling(kinds.Length / (double)cols);
        int labelH = 20;
        int padding = 14;
        int headH = 22;

        // Each strip is: heading + the icon band + room for the "muted" caption.
        int stripH = headH + trayPx + 8 + 18;
        int w = padding * 2 + cols * cell + (cols - 1) * padding;
        int h = padding * 2
              + stripH * 2 + padding
              + headH + rows * (cell + labelH) + (rows - 1) * padding
              + padding * 2;

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.FromArgb(0x18, 0x1C, 0x21));

        using var label = new Font("Segoe UI", 7.5f);
        using var head = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var white = new SolidBrush(Color.FromArgb(0xD8, 0xDE, 0xE5));
        using var dim = new SolidBrush(Color.FromArgb(0x8A, 0x94, 0x9F));

        int y = padding * 2;

        // Row 1: a volume ramp on a dark taskbar, at true size.
        g.DrawString($"Volume ramp — true size, {trayPx}px, dark taskbar", head, white, padding, y);
        y += headH;
        using (var barBg = new SolidBrush(Color.FromArgb(0x1F, 0x22, 0x26)))
            g.FillRectangle(barBg, padding, y, w - padding * 2, trayPx + 8);
        int x = padding + 6;
        foreach (int v in new[] { 0, 15, 33, 50, 68, 85, 100 })
        {
            using var ic = IconPainter.Render(new IconState(
                GlyphKind.Tv, Palette.InkOn(true), Palette.Accents[0],
                v / 100f, false, true, trayPx));
            g.DrawIcon(ic, new Rectangle(x, y + 4, trayPx, trayPx));
            IconPainter.DisposeIcon(ic);
            x += trayPx + 10;
        }
        // Muted, at the end of the ramp.
        using (var ic = IconPainter.Render(new IconState(
            GlyphKind.Tv, Palette.InkOn(true), Palette.Accents[0], 0.42f, true, true, trayPx)))
        {
            g.DrawIcon(ic, new Rectangle(x + 14, y + 4, trayPx, trayPx));
            IconPainter.DisposeIcon(ic);
        }
        g.DrawString("muted", label, dim, x + 10, y + trayPx + 6);
        y += stripH + padding;

        // Row 2: the same on a light taskbar.
        g.DrawString("Same, light taskbar", head, white, padding, y);
        y += headH;
        using (var barBg = new SolidBrush(Color.FromArgb(0xF3, 0xF3, 0xF3)))
            g.FillRectangle(barBg, padding, y, w - padding * 2, trayPx + 8);
        x = padding + 6;
        foreach (int v in new[] { 0, 15, 33, 50, 68, 85, 100 })
        {
            using var ic = IconPainter.Render(new IconState(
                GlyphKind.Tv, Palette.InkOn(false), Palette.Accents[0],
                v / 100f, false, false, trayPx));
            g.DrawIcon(ic, new Rectangle(x, y + 4, trayPx, trayPx));
            IconPainter.DisposeIcon(ic);
            x += trayPx + 10;
        }
        using (var ic = IconPainter.Render(new IconState(
            GlyphKind.Tv, Palette.InkOn(false), Palette.Accents[0], 0.42f, true, false, trayPx)))
        {
            g.DrawIcon(ic, new Rectangle(x + 14, y + 4, trayPx, trayPx));
            IconPainter.DisposeIcon(ic);
        }
        y += stripH + padding * 2;

        // The glyph set, magnified.
        g.DrawString($"Base glyph set — {mag}x magnified from the real {trayPx}px render", head, white, padding, y);
        y += headH;

        var cellFmt = new StringFormat
        {
            Alignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        for (int i = 0; i < kinds.Length; i++)
        {
            int cx = padding + (i % cols) * (cell + padding);
            int cy = y + (i / cols) * (cell + labelH + padding);

            using (var cellBg = new SolidBrush(Color.FromArgb(0x22, 0x26, 0x2B)))
                g.FillRectangle(cellBg, cx, cy, cell, cell);

            using var ic = IconPainter.Render(new IconState(
                kinds[i], Palette.InkOn(true), Palette.AccentFor(kinds[i].ToString()),
                0.62f, false, true, trayPx));

            // Nearest-neighbour, so this shows the actual pixels rather than a
            // smoothed idea of them.
            var old = g.InterpolationMode;
            var oldPix = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var asBmp = ic.ToBitmap())
                g.DrawImage(asBmp, new Rectangle(cx, cy, cell, cell));
            g.InterpolationMode = old;
            g.PixelOffsetMode = oldPix;
            IconPainter.DisposeIcon(ic);

            g.DrawString(kinds[i].ToString(), label, dim,
                new RectangleF(cx - padding / 2f, cy + cell + 3, cell + padding, labelH), cellFmt);
        }

        bmp.Save(path, ImageFormat.Png);
    }
}

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Infolume.Icons;

/// <summary>
/// Renders every app-icon candidate at the sizes Windows actually asks for, on
/// both a light and a dark ground, since an app icon is seen in the Start menu,
/// the taskbar, Alt-Tab and File Explorer rather than only on one background.
/// </summary>
internal static class AppMarkSheet
{
    private static readonly int[] Sizes = [16, 24, 32, 48, 64];

    private static readonly (AppMark mark, string name, string note)[] Marks =
    [
        (AppMark.MonitorRing,   "1  monitor, ring gauge", "woofer is the roundel; tweeter is the tittle"),
        (AppMark.MonitorFill,   "2  monitor, filled",     "level rises inside the cabinet"),
        (AppMark.WooferRoundel, "3  woofer roundel",      "driver alone; strongest silhouette"),
        (AppMark.MonitorTile,   "4  monitor on tile",     "no.1 in shell chrome"),
        (AppMark.MonitorGrille, "5  grille as meter",     "slats double as the level"),
        (AppMark.MonitorPair,   "6  monitor pair",        "nearfield pair, near one gauged")
    ];

    internal static void Write(string path)
    {
        const int big = 128;
        int pad = 18;
        int rowLabel = 22;
        int rowH = big + pad + rowLabel;

        int stripW = Sizes.Sum(s => s + 14) + 40;
        int w = pad * 2 + big + pad + stripW * 2 + pad;
        int h = pad + Marks.Length * rowH + pad;

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.FromArgb(0x14, 0x18, 0x1C));

        using var hFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        using var nFont = new Font("Segoe UI", 8f);
        using var sFont = new Font("Segoe UI", 7f);
        using var white = new SolidBrush(Color.FromArgb(0xE8, 0xEC, 0xF0));
        using var dim = new SolidBrush(Color.FromArgb(0x8A, 0x94, 0x9F));

        int y = pad;

        foreach (var (mark, name, note) in Marks)
        {
            // Large rendering.
            using (var large = new Bitmap(big, big, PixelFormat.Format32bppArgb))
            {
                using (var lg = Graphics.FromImage(large))
                {
                    lg.Clear(Color.Transparent);
                    AppMarkPainter.Draw(lg, mark, big);
                }
                g.DrawImage(large, pad, y);
            }

            int tx = pad + big + pad;
            g.DrawString(name, hFont, white, tx, y);
            g.DrawString(note, nFont, dim, tx, y + 18);

            // Two strips: dark ground (taskbar, dark Start menu) and light ground.
            DrawStrip(g, tx, y + 42, "on dark", Color.FromArgb(0x20, 0x24, 0x29), mark, sFont, dim);
            DrawStrip(g, tx + stripW, y + 42, "on light", Color.FromArgb(0xF2, 0xF3, 0xF5), mark, sFont, dim);

            y += rowH;
        }

        bmp.Save(path, ImageFormat.Png);
    }

    private static void DrawStrip(Graphics g, int x, int y, string label,
                                  Color ground, AppMark mark, Font font, Brush dim)
    {
        int width = Sizes.Sum(s => s + 14) + 12;
        using (var bg = new SolidBrush(ground))
            g.FillRectangle(bg, x, y, width, 74);

        g.DrawString(label, font, dim, x + 4, y + 58);

        int cx = x + 10;
        foreach (int s in Sizes)
        {
            using var icon = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var ig = Graphics.FromImage(icon))
            {
                ig.Clear(Color.Transparent);
                AppMarkPainter.Draw(ig, mark, s);
            }
            g.DrawImage(icon, cx, y + 8 + (48 - s) / 2, s, s);
            cx += s + 14;
        }
    }
}

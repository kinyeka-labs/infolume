using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Infolume.Icons;

/// <summary>
/// Writes the application's .ico from the same painter the shell mark uses, so the
/// icon on disk can never drift from the design in code.
///
/// Entries below 256px are written as uncompressed DIBs, not PNGs. PNG-compressed
/// entries are legal and the Windows shell reads them, but GDI+ cannot decode
/// them: System.Drawing.Icon reports the right size from the directory header and
/// then throws on draw. That breaks anything .NET-based that loads the file,
/// including this project's own tooling, so only the 256px entry is PNG - which is
/// the convention real icon files follow anyway.
/// </summary>
internal static class IconFile
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    internal static void Write(string path)
    {
        var entries = new List<(int size, byte[] data, bool png)>();

        foreach (int size in Sizes)
        {
            using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);

                // The brand mark, not the tray icon. The tray icon is a live
                // readout that changes constantly; this is fixed, and it needs a
                // container so it survives a light Start menu as well as a dark one.
                AppMarkPainter.Draw(g, AppMark.MonitorTile, size);
            }

            if (size >= 256)
            {
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                entries.Add((size, ms.ToArray(), true));
            }
            else
            {
                entries.Add((size, Dib(bmp), false));
            }
        }

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);

        w.Write((ushort)0);                      // reserved
        w.Write((ushort)1);                      // type: icon
        w.Write((ushort)entries.Count);

        int offset = 6 + entries.Count * 16;
        foreach (var (size, data, _) in entries)
        {
            w.Write((byte)(size >= 256 ? 0 : size));   // 0 means 256
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);                    // palette count
            w.Write((byte)0);                    // reserved
            w.Write((ushort)1);                  // colour planes
            w.Write((ushort)32);                 // bits per pixel
            w.Write(data.Length);
            w.Write(offset);
            offset += data.Length;
        }

        foreach (var (_, data, _) in entries) w.Write(data);
    }

    /// <summary>
    /// A 32bpp bottom-up DIB with the AND mask an icon entry requires.
    ///
    /// Two details that silently corrupt the icon if missed: the header's height is
    /// doubled to cover the colour data plus the mask, and each mask row is padded
    /// to a 4-byte boundary.
    /// </summary>
    private static byte[] Dib(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;

        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly,
                                PixelFormat.Format32bppArgb);
        var pixels = new byte[w * h * 4];
        try
        {
            for (int row = 0; row < h; row++)
            {
                // Bottom-up: the last source row lands first in the DIB.
                IntPtr src = data.Scan0 + (h - 1 - row) * data.Stride;
                Marshal.Copy(src, pixels, row * w * 4, w * 4);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        int maskStride = ((w + 31) / 32) * 4;      // 1bpp, rows padded to 4 bytes
        var mask = new byte[maskStride * h];       // all zero: alpha does the work

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(40);                 // biSize
        bw.Write(w);                  // biWidth
        bw.Write(h * 2);              // biHeight: colour data + mask
        bw.Write((ushort)1);          // biPlanes
        bw.Write((ushort)32);         // biBitCount
        bw.Write(0);                  // biCompression: BI_RGB
        bw.Write(pixels.Length + mask.Length);
        bw.Write(0);                  // biXPelsPerMeter
        bw.Write(0);                  // biYPelsPerMeter
        bw.Write(0);                  // biClrUsed
        bw.Write(0);                  // biClrImportant

        bw.Write(pixels);
        bw.Write(mask);

        return ms.ToArray();
    }
}

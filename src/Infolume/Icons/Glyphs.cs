using System.Drawing;
using System.Drawing.Drawing2D;

namespace Infolume.Icons;

/// <summary>
/// The base glyph set, drawn as paths rather than shipped as bitmaps: they stay
/// sharp at any DPI and are recoloured at paint time, so glyph and colour are
/// independent settings rather than a combinatorial set of assets.
///
/// Every glyph draws inside a square box of side <c>s</c> at <c>(x, y)</c>, using
/// the same proportions as the design mockup.
/// </summary>
internal static class Glyphs
{
    internal static void Draw(Graphics g, GlyphKind kind, float x, float y, float s, Color color)
    {
        using var pen = new Pen(color, Math.Max(2f, s * 0.09f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var brush = new SolidBrush(color);

        switch (kind)
        {
            case GlyphKind.Speakers: Speakers(g, pen, brush, x, y, s); break;
            case GlyphKind.Headphones: Headphones(g, pen, brush, x, y, s); break;
            case GlyphKind.BluetoothHeadphones: BluetoothHeadphones(g, pen, brush, x, y, s, color); break;
            case GlyphKind.Headset: Headset(g, pen, brush, x, y, s); break;
            case GlyphKind.Earbuds: Earbuds(g, pen, brush, x, y, s); break;
            case GlyphKind.Glasses: Glasses(g, pen, x, y, s); break;
            case GlyphKind.Tv: Tv(g, pen, x, y, s); break;
            case GlyphKind.Monitor: Monitor(g, pen, brush, x, y, s); break;
            case GlyphKind.Line: Line(g, pen, brush, x, y, s); break;
            case GlyphKind.Spdif: Spdif(g, pen, brush, x, y, s); break;
            case GlyphKind.Network: Network(g, pen, brush, x, y, s); break;
            case GlyphKind.Virtual: Virtual(g, pen, x, y, s); break;
            default: Unknown(g, pen, brush, x, y, s); break;
        }
    }

    // ------------------------------------------------------------- helpers

    private static void RoundRect(GraphicsPath p, float x, float y, float w, float h, float r)
    {
        r = Math.Min(r, Math.Min(w, h) / 2f);
        float d = r * 2f;
        p.StartFigure();
        if (r <= 0.01f)
        {
            p.AddRectangle(new RectangleF(x, y, w, h));
        }
        else
        {
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
        }
        p.CloseFigure();
    }

    private static void FillRound(Graphics g, Brush b, float x, float y, float w, float h, float r)
    {
        using var p = new GraphicsPath();
        RoundRect(p, x, y, w, h, r);
        g.FillPath(b, p);
    }

    private static void DrawRound(Graphics g, Pen pen, float x, float y, float w, float h, float r)
    {
        using var p = new GraphicsPath();
        RoundRect(p, x, y, w, h, r);
        g.DrawPath(pen, p);
    }

    private static void FillCircle(Graphics g, Brush b, float cx, float cy, float r) =>
        g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);

    // -------------------------------------------------------------- glyphs

    private static void Speakers(Graphics g, Pen pen, Brush br, float x, float y, float s)
    {
        DrawRound(g, pen, x + s * 0.20f, y + s * 0.06f, s * 0.60f, s * 0.88f, s * 0.10f);
        FillCircle(g, br, x + s * 0.50f, y + s * 0.63f, s * 0.17f);
        FillCircle(g, br, x + s * 0.50f, y + s * 0.26f, s * 0.075f);
    }

    private static void Headphones(Graphics g, Pen pen, Brush br, float x, float y, float s)
    {
        float r = s * 0.36f;
        g.DrawArc(pen, x + s * 0.5f - r, y + s * 0.50f - r, r * 2, r * 2, 184, 172);
        FillRound(g, br, x + s * 0.06f, y + s * 0.46f, s * 0.20f, s * 0.40f, s * 0.07f);
        FillRound(g, br, x + s * 0.74f, y + s * 0.46f, s * 0.20f, s * 0.40f, s * 0.07f);
    }

    private static void BluetoothHeadphones(Graphics g, Pen pen, Brush br, float x, float y, float s, Color color)
    {
        // The headphones shrink slightly to make room for the badge, which sits
        // proud of the top-right rather than overlapping the band.
        Headphones(g, pen, br, x, y + s * 0.06f, s * 0.88f);
        BtRune(g, x + s * 0.80f, y + s * 0.17f, s * 0.42f, color);
    }

    /// <summary>
    /// A Bluetooth badge. The rune is CUT out of the disc rather than painted in a
    /// background colour: the taskbar is translucent, so there is no colour to match.
    /// </summary>
    private static void BtRune(Graphics g, float cx, float cy, float s, Color color)
    {
        using var disc = new SolidBrush(color);
        FillCircle(g, disc, cx, cy, s * 0.5f);

        var oldMode = g.CompositingMode;
        g.CompositingMode = CompositingMode.SourceCopy;
        using var cut = new Pen(Color.Transparent, Math.Max(1.4f, s * 0.13f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        float h = s * 0.30f, w = s * 0.17f;
        g.DrawLines(cut,
        [
            new PointF(cx - w, cy - h * 0.45f),
            new PointF(cx + w, cy + h * 0.45f),
            new PointF(cx,     cy + h),
            new PointF(cx,     cy - h),
            new PointF(cx + w, cy - h * 0.45f),
            new PointF(cx - w, cy + h * 0.45f)
        ]);
        g.CompositingMode = oldMode;
    }

    private static void Headset(Graphics g, Pen pen, Brush br, float x, float y, float s)
    {
        float r = s * 0.34f;
        g.DrawArc(pen, x + s * 0.46f - r, y + s * 0.46f - r, r * 2, r * 2, 184, 172);
        FillRound(g, br, x + s * 0.03f, y + s * 0.42f, s * 0.19f, s * 0.38f, s * 0.07f);
        FillRound(g, br, x + s * 0.70f, y + s * 0.42f, s * 0.19f, s * 0.38f, s * 0.07f);

        using var boom = new GraphicsPath();
        boom.AddBezier(
            x + s * 0.795f, y + s * 0.80f,
            x + s * 0.795f, y + s * 0.96f,
            x + s * 0.68f,  y + s * 0.96f,
            x + s * 0.55f,  y + s * 0.96f);
        g.DrawPath(pen, boom);
        FillCircle(g, br, x + s * 0.47f, y + s * 0.96f, s * 0.10f);
    }

    private static void Earbuds(Graphics g, Pen pen, Brush br, float x, float y, float s)
    {
        FillCircle(g, br, x + s * 0.26f, y + s * 0.30f, s * 0.19f);
        FillCircle(g, br, x + s * 0.74f, y + s * 0.30f, s * 0.19f);
        g.DrawLine(pen, x + s * 0.26f, y + s * 0.49f, x + s * 0.26f, y + s * 0.92f);
        g.DrawLine(pen, x + s * 0.74f, y + s * 0.49f, x + s * 0.74f, y + s * 0.92f);
    }

    /// <summary>
    /// Display glasses - XREAL, Viture and the like. They arrive as
    /// DisplayPort audio, so without this they draw as a television, which is
    /// exactly the confusion this app exists to remove.
    ///
    /// Drawn front-on. It is the only angle that survives a 32px box, and the
    /// swept temples are what separate it from a pair of goggles or a mask.
    /// </summary>
    private static void Glasses(Graphics g, Pen pen, float x, float y, float s)
    {
        const float top = 0.29f;
        const float h = 0.27f;
        const float lw = 0.36f;

        // One continuous brow across the whole width, with two square lenses hung
        // from it. That single unbroken line is what reads as a moulded AR unit
        // rather than a pair of spectacles, and it is also the part that survives
        // longest as the icon shrinks. It overhangs the lenses at both ends, which
        // is what the temples would have contributed without needing strokes thin
        // enough to disappear.
        g.DrawLine(pen, x + s * 0.04f, y + s * top, x + s * 0.96f, y + s * top);

        // Square corners: these are display glasses, not eyewear. The small radius
        // is what carries "modern" at this size.
        DrawRound(g, pen, x + s * 0.08f, y + s * top, s * lw, s * h, s * 0.04f);
        DrawRound(g, pen, x + s * 0.56f, y + s * top, s * lw, s * h, s * 0.04f);
    }

    private static void Tv(Graphics g, Pen pen, float x, float y, float s)
    {
        DrawRound(g, pen, x + s * 0.04f, y + s * 0.14f, s * 0.92f, s * 0.60f, s * 0.08f);
        g.DrawLine(pen, x + s * 0.50f, y + s * 0.74f, x + s * 0.50f, y + s * 0.92f);
        g.DrawLine(pen, x + s * 0.30f, y + s * 0.92f, x + s * 0.70f, y + s * 0.92f);
    }

    private static void Monitor(Graphics g, Pen pen, Brush br, float x, float y, float s)
    {
        DrawRound(g, pen, x + s * 0.04f, y + s * 0.10f, s * 0.92f, s * 0.58f, s * 0.06f);
        g.FillRectangle(br, x + s * 0.42f, y + s * 0.68f, s * 0.16f, s * 0.16f);
        g.DrawLine(pen, x + s * 0.22f, y + s * 0.92f, x + s * 0.78f, y + s * 0.92f);
    }

    private static void Line(Graphics g, Pen pen, Brush br, float x, float y, float s)
    {
        FillRound(g, br, x + s * 0.36f, y + s * 0.04f, s * 0.28f, s * 0.40f, s * 0.06f);
        g.DrawLine(pen, x + s * 0.50f, y + s * 0.44f, x + s * 0.50f, y + s * 0.62f);
        float r = s * 0.20f;
        g.DrawEllipse(pen, x + s * 0.50f - r, y + s * 0.78f - r, r * 2, r * 2);
    }

    private static void Spdif(Graphics g, Pen pen, Brush br, float x, float y, float s)
    {
        DrawRound(g, pen, x + s * 0.22f, y + s * 0.06f, s * 0.56f, s * 0.44f, s * 0.08f);
        FillCircle(g, br, x + s * 0.50f, y + s * 0.28f, s * 0.11f);
        using var thin = new Pen(pen.Color, Math.Max(1.8f, s * 0.075f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        for (int i = 0; i < 3; i++)
        {
            float r = s * (0.16f + i * 0.15f);
            g.DrawArc(thin, x + s * 0.50f - r, y + s * 0.62f - r, r * 2, r * 2, 36, 108);
        }
    }

    private static void Network(Graphics g, Pen pen, Brush br, float x, float y, float s)
    {
        for (int i = 0; i < 3; i++)
        {
            float r = s * (0.22f + i * 0.24f);
            g.DrawArc(pen, x + s * 0.50f - r, y + s * 0.86f - r, r * 2, r * 2, 220, 100);
        }
        FillCircle(g, br, x + s * 0.50f, y + s * 0.86f, s * 0.09f);
    }

    private static void Virtual(Graphics g, Pen pen, float x, float y, float s)
    {
        using var dashed = new Pen(pen.Color, pen.Width)
        {
            DashStyle = DashStyle.Custom,
            DashPattern = [2.1f, 1.7f],
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        float r = s * 0.42f;
        g.DrawEllipse(dashed, x + s * 0.50f - r, y + s * 0.50f - r, r * 2, r * 2);
        g.DrawLines(pen,
        [
            new PointF(x + s * 0.30f, y + s * 0.38f),
            new PointF(x + s * 0.50f, y + s * 0.38f),
            new PointF(x + s * 0.50f, y + s * 0.62f),
            new PointF(x + s * 0.70f, y + s * 0.62f)
        ]);
    }

    private static void Unknown(Graphics g, Pen pen, Brush br, float x, float y, float s)
    {
        using var cone = new GraphicsPath();
        cone.AddPolygon(new[]
        {
            new PointF(x + s * 0.04f, y + s * 0.34f),
            new PointF(x + s * 0.24f, y + s * 0.34f),
            new PointF(x + s * 0.52f, y + s * 0.08f),
            new PointF(x + s * 0.52f, y + s * 0.92f),
            new PointF(x + s * 0.24f, y + s * 0.66f),
            new PointF(x + s * 0.04f, y + s * 0.66f)
        });
        g.FillPath(br, cone);
        float r = s * 0.28f;
        g.DrawArc(pen, x + s * 0.52f - r, y + s * 0.50f - r, r * 2, r * 2, -49, 98);
    }
}

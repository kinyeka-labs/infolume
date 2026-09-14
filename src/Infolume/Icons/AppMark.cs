using System.Drawing;
using System.Drawing.Drawing2D;

namespace Infolume.Icons;

/// <summary>
/// Candidate application marks, built on the studio monitor.
///
/// The fusion this direction exploits: a monitor cabinet carries a small tweeter
/// above a large woofer, which is already the arrangement of a lowercase "i" -
/// tittle over stem. So the speaker IS the information mark, and the woofer, being
/// a circle, can carry the level as a ring gauge without any added ornament.
/// </summary>
public enum AppMark
{
    /// <summary>Cabinet, tweeter, and a woofer drawn as a ring gauge.</summary>
    MonitorRing,

    /// <summary>Cabinet filled to the level from the bottom; drivers knocked out of it.</summary>
    MonitorFill,

    /// <summary>The woofer alone, as a driver-shaped info roundel, tweeter above.</summary>
    WooferRoundel,

    /// <summary>MonitorRing on a graphite tile, for shell chrome.</summary>
    MonitorTile,

    /// <summary>Cabinet whose grille is a stack of level bars.</summary>
    MonitorGrille,

    /// <summary>A pair of monitors, the near one carrying the gauge.</summary>
    MonitorPair
}

internal static class AppMarkPainter
{
    private static readonly Color Graphite = Color.FromArgb(0x1E, 0x24, 0x2B);
    private static readonly Color GraphiteLift = Color.FromArgb(0x2C, 0x34, 0x3D);
    private static readonly Color Amber = Color.FromArgb(0xF0, 0xA9, 0x3B);
    private static readonly Color AmberDeep = Color.FromArgb(0xD2, 0x86, 0x1B);
    private static readonly Color Bone = Color.FromArgb(0xF4, 0xF6, 0xF8);

    /// <summary>Where the gauge sits, so the mark reads as a reading and not a shape.</summary>
    private const float Level = 0.68f;

    internal static void Draw(Graphics g, AppMark mark, int n)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        switch (mark)
        {
            case AppMark.MonitorRing: MonitorRing(g, n, Bone); break;
            case AppMark.MonitorFill: MonitorFill(g, n); break;
            case AppMark.WooferRoundel: WooferRoundel(g, n); break;
            // Inset the cabinet inside the tile, and re-centre it: Layout centres
            // within the size it is given, so a smaller cabinet must be nudged by
            // half the difference or it rides up and to the left.
            case AppMark.MonitorTile:
                Tile(g, n);
                MonitorRing(g, n * 0.84f, Bone, n * 0.065f, n * 0.08f, n * 0.08f);
                break;
            case AppMark.MonitorGrille: MonitorGrille(g, n); break;
            case AppMark.MonitorPair: MonitorPair(g, n); break;
        }
    }

    // ------------------------------------------------------------- geometry

    private readonly record struct Cabinet(RectangleF Box, PointF Tweeter, float TweeterD,
                                           PointF Woofer, float WooferD);

    /// <summary>
    /// Cabinet proportions. Deliberately taller than wide, like a real nearfield
    /// monitor, which is also what makes the tweeter-over-woofer read as an "i".
    /// </summary>
    private static Cabinet Layout(float n, float ox = 0, float oy = 0)
    {
        float w = n * 0.56f, h = n * 0.84f;
        var box = new RectangleF(ox + (n - w) / 2f, oy + (n - h) / 2f, w, h);
        return new Cabinet(
            box,
            new PointF(box.X + w / 2f, box.Y + h * 0.24f), n * 0.115f,
            new PointF(box.X + w / 2f, box.Y + h * 0.65f), n * 0.30f);
    }

    // ---------------------------------------------------------------- marks

    private static void MonitorRing(Graphics g, float n, Color ink, float stroke = 0,
                                    float ox = 0, float oy = 0)
    {
        var c = Layout(n, ox, oy);
        stroke = stroke <= 0 ? n * 0.075f : stroke;

        using (var pen = new Pen(ink, stroke) { LineJoin = LineJoin.Round })
        using (var path = Rounded(c.Box, n * 0.13f))
            g.DrawPath(pen, path);

        using (var brush = new SolidBrush(ink))
            g.FillEllipse(brush, c.Tweeter.X - c.TweeterD / 2, c.Tweeter.Y - c.TweeterD / 2,
                          c.TweeterD, c.TweeterD);

        // The woofer as a gauge: a full track ring with an amber arc over it,
        // sweeping from the bottom of the driver.
        float r = c.WooferD / 2f;
        var wr = new RectangleF(c.Woofer.X - r, c.Woofer.Y - r, c.WooferD, c.WooferD);

        // A 270-degree dial rather than a full circle: a closed ring at any level
        // above about two thirds reads as "a ring", while an open dial with a
        // visible gap reads as a reading.
        const float start = 135f, span = 270f;

        using (var track = new Pen(Color.FromArgb(70, ink), stroke * 0.95f)
               { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(track, wr, start, span);

        using (var fill = new Pen(Amber, stroke * 0.95f)
               { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(fill, wr, start, span * Level);

        // Dust cap in bone, not amber: an amber cap inside an amber dial merges
        // into one blob once the icon is small.
        using (var cap = new SolidBrush(ink))
            g.FillEllipse(cap, c.Woofer.X - r * 0.34f, c.Woofer.Y - r * 0.34f, r * 0.68f, r * 0.68f);
    }

    private static void MonitorFill(Graphics g, float n)
    {
        var c = Layout(n);
        float radius = n * 0.13f;

        using (var path = Rounded(c.Box, radius))
        using (var ghost = new SolidBrush(Color.FromArgb(60, Bone)))
            g.FillPath(ghost, path);

        // Level rises inside the cabinet, clipped to its shape.
        var lit = new RectangleF(c.Box.X, c.Box.Bottom - c.Box.Height * Level,
                                 c.Box.Width, c.Box.Height * Level);
        var state = g.Save();
        using (var clip = Rounded(c.Box, radius))
        {
            g.SetClip(clip);
            using var brush = new LinearGradientBrush(
                new RectangleF(lit.X, lit.Y, lit.Width, Math.Max(1f, lit.Height)),
                Amber, AmberDeep, LinearGradientMode.Vertical);
            g.FillRectangle(brush, lit);
        }
        g.Restore(state);

        // Drivers cut out of whatever is behind, so they read on fill and ghost alike.
        var old = g.CompositingMode;
        g.CompositingMode = CompositingMode.SourceCopy;
        using (var cut = new SolidBrush(Color.Transparent))
        {
            g.FillEllipse(cut, c.Tweeter.X - c.TweeterD / 2, c.Tweeter.Y - c.TweeterD / 2,
                          c.TweeterD, c.TweeterD);
            g.FillEllipse(cut, c.Woofer.X - c.WooferD / 2, c.Woofer.Y - c.WooferD / 2,
                          c.WooferD, c.WooferD);
        }
        g.CompositingMode = old;

        using var outline = new Pen(Bone, n * 0.05f);
        using var p = Rounded(c.Box, radius);
        g.DrawPath(outline, p);
    }

    /// <summary>
    /// No cabinet: the driver alone, which is both a speaker and the information
    /// roundel. Strongest silhouette of the set at small sizes.
    /// </summary>
    private static void WooferRoundel(Graphics g, float n)
    {
        float d = n * 0.62f;
        var wr = new RectangleF((n - d) / 2f, n * 0.30f, d, d);
        float stroke = n * 0.085f;

        using (var track = new Pen(Color.FromArgb(70, Bone), stroke))
            g.DrawEllipse(track, wr);

        using (var fill = new Pen(Amber, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(fill, wr, 90f, 360f * Level);

        // Surround and dust cap: what makes the circle read as a driver.
        using (var surround = new Pen(Color.FromArgb(120, Bone), stroke * 0.55f))
            g.DrawEllipse(surround, wr.X + stroke, wr.Y + stroke,
                          wr.Width - stroke * 2, wr.Height - stroke * 2);

        using (var cap = new SolidBrush(Bone))
            g.FillEllipse(cap, wr.X + wr.Width * 0.34f, wr.Y + wr.Height * 0.34f,
                          wr.Width * 0.32f, wr.Height * 0.32f);

        // Tweeter above: the tittle of the "i".
        float td = n * 0.15f;
        using var tw = new SolidBrush(Bone);
        g.FillEllipse(tw, (n - td) / 2f, n * 0.09f, td, td);
    }

    private static void MonitorGrille(Graphics g, float n)
    {
        var c = Layout(n);
        float radius = n * 0.13f;

        using (var pen = new Pen(Bone, n * 0.06f) { LineJoin = LineJoin.Round })
        using (var path = Rounded(c.Box, radius))
            g.DrawPath(pen, path);

        // Grille slats doubling as a level: lit from the bottom up.
        const int slats = 5;
        float inner = c.Box.Width * 0.62f;
        float x = c.Box.X + (c.Box.Width - inner) / 2f;
        float top = c.Box.Y + c.Box.Height * 0.16f;
        float bottom = c.Box.Bottom - c.Box.Height * 0.12f;
        float gap = (bottom - top) * 0.07f;
        float sh = ((bottom - top) - gap * (slats - 1)) / slats;
        int lit = (int)Math.Round(slats * Level);

        for (int i = 0; i < slats; i++)
        {
            float sy = bottom - (i + 1) * sh - i * gap;
            Color col = i < lit ? Blend(AmberDeep, Amber, i / (float)(slats - 1))
                                : Color.FromArgb(60, Bone);
            using var brush = new SolidBrush(col);
            using var p = Rounded(new RectangleF(x, sy, inner, sh), sh * 0.4f);
            g.FillPath(brush, p);
        }
    }

    private static void MonitorPair(Graphics g, float n)
    {
        // Far cabinet, set back and dimmed.
        var far = Layout(n * 0.78f, n * 0.30f, n * 0.14f);
        using (var pen = new Pen(Color.FromArgb(110, Bone), n * 0.055f) { LineJoin = LineJoin.Round })
        using (var path = Rounded(far.Box, n * 0.10f))
            g.DrawPath(pen, path);

        // Near cabinet carries the gauge.
        var near = Layout(n * 0.78f, -n * 0.06f, n * 0.14f);
        float stroke = n * 0.065f;

        using (var bg = new SolidBrush(Graphite))
        using (var path = Rounded(near.Box, n * 0.10f))
            g.FillPath(bg, path);

        using (var pen = new Pen(Bone, stroke) { LineJoin = LineJoin.Round })
        using (var path = Rounded(near.Box, n * 0.10f))
            g.DrawPath(pen, path);

        using (var brush = new SolidBrush(Bone))
            g.FillEllipse(brush, near.Tweeter.X - near.TweeterD / 2, near.Tweeter.Y - near.TweeterD / 2,
                          near.TweeterD, near.TweeterD);

        float r = near.WooferD / 2f;
        var wr = new RectangleF(near.Woofer.X - r, near.Woofer.Y - r, near.WooferD, near.WooferD);
        using (var track = new Pen(Color.FromArgb(70, Bone), stroke * 0.9f))
            g.DrawEllipse(track, wr);
        using (var fill = new Pen(Amber, stroke * 0.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(fill, wr, 90f, 360f * Level);
    }

    // --------------------------------------------------------------- pieces

    private static void Tile(Graphics g, float n)
    {
        float inset = n * 0.05f;
        var r = new RectangleF(inset, inset, n - inset * 2, n - inset * 2);
        using var path = Rounded(r, n * 0.22f);
        using var brush = new LinearGradientBrush(r, GraphiteLift, Graphite, LinearGradientMode.Vertical);
        g.FillPath(brush, path);
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2f);
        if (radius <= 0.05f || r.Width <= 0 || r.Height <= 0) { p.AddRectangle(r); return p; }

        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        255,
        (int)(a.R + (b.R - a.R) * Math.Clamp(t, 0f, 1f)),
        (int)(a.G + (b.G - a.G) * Math.Clamp(t, 0f, 1f)),
        (int)(a.B + (b.B - a.B) * Math.Clamp(t, 0f, 1f)));
}

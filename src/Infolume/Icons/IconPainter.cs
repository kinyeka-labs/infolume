using System.Drawing;
using System.Drawing.Drawing2D;
using Infolume.Native;

namespace Infolume.Icons;

/// <summary>The state one tray icon frame is painted from.</summary>
public readonly record struct IconState(
    GlyphKind Glyph,
    Color GlyphColor,
    Color AccentColor,
    float Volume,
    bool Muted,
    bool DarkTaskbar,
    int Size,
    AudioCue Cue = AudioCue.Waves,
    VolumeStyle Style = VolumeStyle.Bar,
    /// <summary>Steps in the incline staircase. Below ~32px, more than 5 stops resolving.</summary>
    int InclineBars = 4);

/// <summary>
/// Paints the tray icon: glyph in the top band, solid level bar along the bottom.
///
/// Proportions are taken from the 36px design and expressed as fractions, so the
/// same layout holds at 16px (100% scaling) and 48px alike.
/// </summary>
public static class IconPainter
{
    // Proportions as fractions of the icon's edge, so the same layout holds at
    // 16px and 48px alike.
    //
    // The glyph gets 78% of the height and is centred on a square of that side -
    // an earlier 75%/full-width-bar split left the glyph visibly small against a
    // chunky bar at 32px, which read as cramped rather than balanced.
    private const float GlyphBand = 0.78f;
    private const float BarTop    = 0.86f;
    private const float BarHeight = 0.14f;
    private const float BarInset  = 0.06f;

    /// <summary>
    /// Renders one frame. The caller owns the returned <see cref="Icon"/> and must
    /// dispose it via <see cref="DisposeIcon"/> - not Icon.Dispose alone, which
    /// leaves the underlying HICON alive.
    /// </summary>
    public static Icon Render(IconState st)
    {
        int n = Math.Max(16, st.Size);
        var bmp = new Bitmap(n, n, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                float unit = n / 36f;
                DrawMark(g, n, st);

                DrawBar(g, n, st);

                // Silence looks the same whichever way you got there, so zero gets
                // the slash too. The tooltip and OSD still say "Muted" vs "0%",
                // where there is room for the distinction to matter.
                if (st.Muted || st.Volume <= 0f) DrawSlash(g, n);
            }

            IntPtr hIcon = bmp.GetHicon();
            // Icon.FromHandle does not own the handle - clone so the managed Icon
            // is self-contained, then release the temporary handle immediately.
            using var tmp = Icon.FromHandle(hIcon);
            var icon = (Icon)tmp.Clone();
            NativeMethods.DestroyIcon(hIcon);
            return icon;
        }
        finally
        {
            bmp.Dispose();
        }
    }

    /// <summary>
    /// Paints the glyph band.
    ///
    /// A bare device outline reads as a display or a network icon, not a sound
    /// control - which is the whole job of a tray audio app. The cue is what makes
    /// the icon assert "audio" before it asserts "which device".
    /// </summary>
    private static void DrawMark(Graphics g, int n, IconState st)
    {
        // Each indicator claims a different part of the icon, so the glyph's
        // available band depends on the style rather than being fixed.
        float band = st.Style switch
        {
            VolumeStyle.Equalizer => n * 0.66f,
            VolumeStyle.Column => n * 0.86f,
            VolumeStyle.Arc => n * 0.56f,
            // The staircase is drawn over the glyph, so the glyph keeps the icon.
            VolumeStyle.Incline or VolumeStyle.InclineWide => n * 0.92f,
            _ => n * GlyphBand
        };

        Color ink = st.GlyphColor;

        // Column runs up the right edge; Arc sits under a centred glyph.
        float xShift = st.Style == VolumeStyle.Column ? -n * 0.10f : 0f;
        float yShift = st.Style == VolumeStyle.Arc ? -n * 0.04f : 0f;

        if (st.Style is VolumeStyle.Column or VolumeStyle.Arc)
        {
            Glyphs.Draw(g, st.Glyph, (n - band) / 2f + xShift, yShift, band, ink);
            if (st.Cue == AudioCue.Waves && !AlreadySpeaks(st.Glyph) && st.Style == VolumeStyle.Arc)
                DrawWaves(g, n, band, ink);
            return;
        }

        switch (st.Cue)
        {
            case AudioCue.None:
                Glyphs.Draw(g, st.Glyph, (n - band) / 2f, 0f, band, ink);
                break;

            case AudioCue.SpeakerBadge:
            {
                // Speaker is the mark; the device is a corner badge. Reads as sound
                // instantly, but the badge is small and gets tight below ~24px.
                Glyphs.Draw(g, GlyphKind.Unknown, (n - band) / 2f, 0f, band * 0.94f, ink);

                float badge = band * 0.46f;
                float bx = n - badge - n * 0.02f;
                float by = band - badge;

                var old = g.CompositingMode;
                g.CompositingMode = CompositingMode.SourceCopy;
                using (var cut = new SolidBrush(Color.Transparent))
                    g.FillEllipse(cut, bx - badge * 0.16f, by - badge * 0.16f,
                                  badge * 1.32f, badge * 1.32f);
                g.CompositingMode = old;

                Glyphs.Draw(g, st.Glyph, bx, by, badge, ink);
                break;
            }

            default:
            {
                // Glyphs that already carry a wave motif get no cue: a second set
                // of arcs beside their own reads as clutter, not as emphasis.
                if (AlreadySpeaks(st.Glyph))
                {
                    Glyphs.Draw(g, st.Glyph, (n - band) / 2f, 0f, band, ink);
                    break;
                }

                // Device stays the primary shape; waves radiate off its right edge,
                // so identity survives and the audio reading comes for free.
                float shrunk = band * 0.80f;
                Glyphs.Draw(g, st.Glyph, n * 0.02f, (band - shrunk) / 2f, shrunk, ink);
                DrawWaves(g, n, band, ink);
                break;
            }
        }
    }

    /// <summary>
    /// The ascending staircase, drawn IN FRONT of the glyph.
    ///
    /// Overlaying rather than sitting below is what lets the device art keep its
    /// full size. Each bar is knocked out of whatever is behind it first - with
    /// destination-out rather than a painted halo, because the taskbar is
    /// translucent and there is no background colour to match - so the steps read
    /// cleanly even where they cross the glyph's own strokes.
    /// </summary>
    private static void DrawIncline(Graphics g, int n, IconState st, float v, Color track, bool wide)
    {
        int count = Math.Clamp(st.InclineBars, 3, 8);

        // Wide starts hard left and spans the icon; corner hugs the right edge.
        float w = wide ? n * 0.92f : n * 0.56f;
        float x0 = wide ? n * 0.04f : n - w - n * 0.04f;
        float bottom = n * (wide ? 0.95f : 0.94f);
        float maxH = n * (wide ? 0.44f : 0.50f);
        float minH = Math.Max(2.5f, n * 0.12f);

        float gap = Math.Max(1.2f, n * (wide ? 0.045f : 0.04f));
        float bw = (w - gap * (count - 1)) / count;
        float radius = Math.Min(bw, minH) * 0.35f;

        // Knock a channel out for the whole staircase in one pass, so the gaps
        // between bars stay even where they overlap the glyph.
        var old = g.CompositingMode;
        g.CompositingMode = CompositingMode.SourceCopy;
        using (var cut = new SolidBrush(Color.Transparent))
        {
            // Kept tight: a generous channel severs thin glyph strokes that pass
            // behind the staircase (a TV stand, an earbud stem) and leaves stray
            // floating fragments beside it.
            float pad = Math.Max(0.75f, n * 0.028f);
            for (int i = 0; i < count; i++)
            {
                float bh = minH + (maxH - minH) * (i / (float)(count - 1));
                float x = x0 + i * (bw + gap);
                FillPill(g, cut, x - pad, bottom - bh - pad, bw + pad * 2, bh + pad * 2, radius + pad);
            }
        }
        g.CompositingMode = old;

        using var tb = new SolidBrush(track);
        using var fb = new SolidBrush(st.AccentColor);
        for (int i = 0; i < count; i++)
        {
            float bh = minH + (maxH - minH) * (i / (float)(count - 1));
            float x = x0 + i * (bw + gap);
            bool lit = v > i / (float)count;
            FillPill(g, lit ? fb : tb, x, bottom - bh, bw, bh, radius);
        }
    }

    /// <summary>
    /// Glyphs whose own shape already radiates: the optical port, the network
    /// device, and the fallback speaker all draw arcs of their own.
    /// </summary>
    private static bool AlreadySpeaks(GlyphKind kind) =>
        kind is GlyphKind.Spdif or GlyphKind.Network or GlyphKind.Unknown;

    /// <summary>Two arcs off the right edge - the universal "this makes sound" mark.</summary>
    private static void DrawWaves(Graphics g, int n, float band, Color ink)
    {
        float cx = n * 0.70f;
        float cy = band * 0.5f;

        using var pen = new Pen(ink, Math.Max(1.6f, n * 0.075f))
        { StartCap = LineCap.Round, EndCap = LineCap.Round };

        for (int i = 0; i < 2; i++)
        {
            float r = band * (0.20f + i * 0.17f);
            g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, -52, 104);
        }
    }

    private static void DrawBar(Graphics g, int n, IconState st)
    {
        float v = st.Muted ? 0f : Math.Clamp(st.Volume, 0f, 1f);
        Color track = Palette.TrackOn(st.DarkTaskbar);

        switch (st.Style)
        {
            case VolumeStyle.Segments: DrawSegments(g, n, st, v, track); break;
            case VolumeStyle.Equalizer: DrawEqualizer(g, n, st, v, track); break;
            case VolumeStyle.Column: DrawColumn(g, n, st, v, track); break;
            case VolumeStyle.Arc: DrawArcGauge(g, n, st, v, track); break;
            case VolumeStyle.Pips: DrawPips(g, n, st, v, track); break;
            case VolumeStyle.Incline: DrawIncline(g, n, st, v, track, wide: false); break;
            case VolumeStyle.InclineWide: DrawIncline(g, n, st, v, track, wide: true); break;
            default: DrawSolidBar(g, n, st, v, track); break;
        }
    }

    private static void DrawSolidBar(Graphics g, int n, IconState st, float v, Color track)
    {
        float y = n * BarTop;
        float h = Math.Max(2f, n * BarHeight);
        float inset = n * BarInset;
        float full = n - inset * 2f;
        float r = h / 2f;

        using (var tb = new SolidBrush(track)) FillPill(g, tb, inset, y, full, h, r);
        if (v <= 0f) return;
        using var fb = new SolidBrush(st.AccentColor);
        FillPill(g, fb, inset, y, Math.Max(h, full * v), h, r);
    }

    private static void DrawSegments(Graphics g, int n, IconState st, float v, Color track)
    {
        const int count = 6;
        float y = n * BarTop;
        float h = Math.Max(2f, n * BarHeight);
        float inset = n * BarInset;
        float full = n - inset * 2f;
        float gap = Math.Max(1f, n * 0.035f);
        float w = (full - gap * (count - 1)) / count;

        using var tb = new SolidBrush(track);
        using var fb = new SolidBrush(st.AccentColor);
        for (int i = 0; i < count; i++)
        {
            bool lit = v > i / (float)count;
            FillPill(g, lit ? fb : tb, inset + i * (w + gap), y, w, h, Math.Min(w, h) / 2f);
        }
    }

    /// <summary>
    /// Stepped vertical bars. The shape itself reads as a level meter, so the icon
    /// says "audio" without needing a separate cue on the glyph.
    /// </summary>
    private static void DrawEqualizer(Graphics g, int n, IconState st, float v, Color track)
    {
        const int count = 5;
        float bottom = n * 0.99f;
        float maxH = n * 0.30f;
        float minH = Math.Max(2f, n * 0.07f);
        float inset = n * BarInset;
        float full = n - inset * 2f;
        float gap = Math.Max(1f, n * 0.04f);
        float w = (full - gap * (count - 1)) / count;

        // A fixed silhouette (tall in the middle) so the mark stays recognisable at
        // any level; the level decides how many bars are lit, not their heights.
        float[] profile = [0.45f, 0.75f, 1.0f, 0.70f, 0.40f];

        using var tb = new SolidBrush(track);
        using var fb = new SolidBrush(st.AccentColor);
        for (int i = 0; i < count; i++)
        {
            float bh = minH + (maxH - minH) * profile[i];
            bool lit = v > i / (float)count;
            float x = inset + i * (w + gap);
            FillPill(g, lit ? fb : tb, x, bottom - bh, w, bh, Math.Min(w, bh) / 2f);
        }
    }

    private static void DrawColumn(Graphics g, int n, IconState st, float v, Color track)
    {
        float w = Math.Max(2f, n * 0.16f);
        float x = n - w - n * 0.04f;
        float top = n * 0.08f;
        float bottom = n * 0.92f;
        float h = bottom - top;
        float r = w / 2f;

        using (var tb = new SolidBrush(track)) FillPill(g, tb, x, top, w, h, r);
        if (v <= 0f) return;
        float lit = Math.Max(w, h * v);
        using var fb = new SolidBrush(st.AccentColor);
        FillPill(g, fb, x, bottom - lit, w, lit, r);
    }

    private static void DrawArcGauge(Graphics g, int n, IconState st, float v, Color track)
    {
        float r = n * 0.40f;
        float cx = n * 0.5f, cy = n * 0.56f;
        float thickness = Math.Max(2f, n * 0.11f);
        const float start = 145f, sweep = 250f;

        using var tp = new Pen(track, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(tp, cx - r, cy - r, r * 2, r * 2, start, sweep);

        if (v <= 0f) return;
        using var fp = new Pen(st.AccentColor, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(fp, cx - r, cy - r, r * 2, r * 2, start, sweep * v);
    }

    private static void DrawPips(Graphics g, int n, IconState st, float v, Color track)
    {
        const int count = 5;
        float d = Math.Max(2f, n * 0.13f);
        float y = n * 0.87f;
        float inset = n * BarInset;
        float full = n - inset * 2f;
        float gap = (full - d * count) / (count - 1);

        using var tb = new SolidBrush(track);
        using var fb = new SolidBrush(st.AccentColor);
        for (int i = 0; i < count; i++)
        {
            bool lit = v > i / (float)count;
            g.FillEllipse(lit ? fb : tb, inset + i * (d + gap), y, d, d);
        }
    }

    private static void FillPill(Graphics g, Brush b, float x, float y, float w, float h, float r)
    {
        r = Math.Min(r, Math.Min(w, h) / 2f);
        using var p = new GraphicsPath();
        if (r <= 0.01f)
        {
            p.AddRectangle(new RectangleF(x, y, w, h));
        }
        else
        {
            float d = r * 2f;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
        }
        g.FillPath(b, p);
    }

    /// <summary>
    /// Mute: a red slash across the glyph band. The channel underneath is CUT with
    /// SourceCopy against a transparent pen rather than painted in a background
    /// colour - the taskbar is translucent, so there is no colour to match, and a
    /// guessed one would show as a grey smear over whatever is behind it.
    /// </summary>
    private static void DrawSlash(Graphics g, int n)
    {
        // Spans the glyph band's diagonal, expressed as fractions of the icon so
        // it tracks the glyph if those proportions change.
        var a = new PointF(n * 0.12f, n * GlyphBand - n * 0.04f);
        var b = new PointF(n * 0.88f, n * 0.04f);

        var old = g.CompositingMode;
        g.CompositingMode = CompositingMode.SourceCopy;
        // Only a thin clearance either side of the slash: any wider and the
        // channel eats the glyph, which defeats the point of keeping the glyph
        // visible so you can still read WHICH device is muted.
        using (var cut = new Pen(Color.Transparent, Math.Max(3f, n * 0.19f))
               { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(cut, a, b);
        g.CompositingMode = old;

        using var slash = new Pen(Palette.Muted, Math.Max(2f, n * 0.12f))
        { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(slash, a, b);
    }

    /// <summary>
    /// Releases both the managed Icon and its underlying HICON.
    /// Tolerates an already-disposed Icon: NotifyIcon disposes the Icon it was
    /// handed when it is itself disposed, so on shutdown this can be reached with
    /// a corpse. Reading .Handle in that state throws.
    /// </summary>
    public static void DisposeIcon(Icon? icon)
    {
        if (icon is null) return;

        IntPtr h;
        try { h = icon.Handle; }
        catch (ObjectDisposedException) { return; }

        try { icon.Dispose(); }
        catch (ObjectDisposedException) { }

        if (h != IntPtr.Zero) NativeMethods.DestroyIcon(h);
    }
}

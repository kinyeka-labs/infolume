using System.Drawing;

namespace Infolume.Ui;

/// <summary>
/// Works out where the tray icon is when the shell refuses to say.
///
/// <c>Shell_NotifyIconGetRect</c> returns nothing under a replaced taskbar
/// (StartAllBack's legacy tray), which breaks anything needing the icon's
/// position - scroll-to-adjust most of all, since it has to know whether the
/// cursor is over the icon before claiming the wheel.
///
/// But <see cref="System.Windows.Forms.NotifyIcon.MouseMove"/> still fires, and
/// it fires ONLY while the cursor is over this icon. So every hover point is by
/// definition inside the icon: collect them and the bounding box converges on the
/// real rect within a single pass of the cursor.
/// </summary>
internal sealed class TrayIconLocator
{
    private Rectangle? _observed;
    private DateTime _lastHover = DateTime.MinValue;

    /// <summary>An estimate older than this is not trusted for positioning.</summary>
    private static readonly TimeSpan Stale = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Records a point known to be over the icon.
    ///
    /// Unioning blindly is wrong: the icon moves whenever the tray re-lays out or
    /// the resolution changes, and points from before and after then average into
    /// a box sitting between the two positions - matching neither, so the wheel
    /// hit test fails while the estimate still looks plausible. A point that would
    /// stretch the box beyond one icon is treated as a relocation, not as growth.
    /// </summary>
    internal void NoteHover(Point p, int iconSize)
    {
        _lastHover = DateTime.UtcNow;
        var point = new Rectangle(p.X, p.Y, 1, 1);

        if (_observed is not { } r) { _observed = point; return; }

        var merged = Rectangle.Union(r, point);
        int limit = Math.Max(8, (int)(iconSize * 1.5));
        _observed = (merged.Width > limit || merged.Height > limit) ? point : merged;
    }

    /// <summary>
    /// Forget the estimate. Must be called when the icon could have moved: an
    /// explorer restart re-lays out the tray, and a resolution or DPI change moves
    /// it outright. A stale rect is worse than none - it would claim scroll
    /// events over whatever now occupies that spot, moving the volume when the
    /// user meant to scroll something else.
    /// </summary>
    internal void Reset()
    {
        _observed = null;
        _lastHover = DateTime.MinValue;
    }

    /// <summary>
    /// Best guess at the icon rect, or null if the cursor has never been over it.
    /// Grown to at least one icon square around the observed centre, and capped so
    /// a long sweep can never inflate it across neighbouring icons.
    /// </summary>
    internal Rectangle? Estimate(int iconSize)
    {
        if (_observed is not { } r) return null;
        if (DateTime.UtcNow - _lastHover > Stale) return null;

        int w = Math.Clamp(r.Width, iconSize, (int)(iconSize * 1.5));
        int h = Math.Clamp(r.Height, iconSize, (int)(iconSize * 1.5));
        int cx = r.Left + r.Width / 2;
        int cy = r.Top + r.Height / 2;
        return new Rectangle(cx - w / 2, cy - h / 2, w, h);
    }
}

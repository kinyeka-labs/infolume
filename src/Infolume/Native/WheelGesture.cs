using System.Drawing;

namespace Infolume.Native;

/// <summary>
/// The scroll-over-the-icon gesture, as arithmetic.
///
/// Split out of <see cref="TrayWheelRawInput"/> and <see cref="TrayApp"/> for two
/// reasons. The obvious one is that it is the only part of the gesture that can
/// be tested without a tray, a wheel or an audio device.
///
/// The other is that <see cref="Claims"/> runs on the raw-input thread, which is
/// handed a WM_INPUT for every mouse movement on the desktop. Keeping the
/// decision here - pure, allocation-free, no call into anything that could
/// block - makes it obvious at a glance that nothing expensive has crept onto
/// that path.
/// </summary>
internal static class WheelGesture
{
    /// <summary>One notch, as WM_MOUSEWHEEL reports it.</summary>
    internal const int WheelDelta = 120;

    /// <summary>
    /// Whether the wheel at this screen point belongs to the tray icon.
    ///
    /// This used to be the decision to <i>swallow</i> the event as well. It is not
    /// any more: raw input cannot block an event, and it was measured that nothing
    /// else acts on a wheel over the icon, so there is nothing to block.
    ///
    /// A null rect means the icon's position is not known (the shell would not say
    /// and the cursor has never hovered it). Claiming nothing is the right answer
    /// there: the wheel then behaves normally instead of misfiring somewhere else.
    /// A degenerate rect is treated the same way. Note this is deliberately not
    /// <c>Rectangle.IsEmpty</c>, which is only true for a rect at the origin and
    /// would let a zero-sized one through.
    ///
    /// <c>Contains</c> is half-open - left and top inclusive, right and bottom
    /// exclusive - so adjacent icons tile without ever both claiming a pixel.
    /// </summary>
    internal static bool Claims(Rectangle? iconRect, int x, int y) =>
        iconRect is { Width: > 0, Height: > 0 } r && r.Contains(x, y);

    /// <summary>
    /// The signed wheel delta out of <c>MSLLHOOKSTRUCT.mouseData</c>, whose high
    /// word carries it. Signed: scroll-down arrives as -120.
    ///
    /// The <c>unchecked</c> is load-bearing, not decoration. Scroll-down puts
    /// <c>0xFF88</c> in that high word, and reinterpreting it as a negative
    /// <c>short</c> is the whole point - but that conversion is only legal in an
    /// unchecked context. It compiles today because unchecked is the C# default;
    /// state it explicitly so that turning on <c>CheckForOverflowUnderflow</c>
    /// cannot silently make every scroll-down throw. That exception would be
    /// thrown inside the hook callback, which is the last place in this codebase
    /// that can afford one.
    /// </summary>
    internal static int DeltaFrom(uint mouseData) =>
        unchecked((short)((mouseData >> 16) & 0xFFFF));

    /// <summary>
    /// Notches carried by one message. A fast flick coalesces several into a
    /// single WM_MOUSEWHEEL, so a delta of 360 has to move the volume three steps
    /// or a hard scroll feels unresponsive. Never less than one: a device
    /// reporting a sub-notch delta should still move the volume.
    /// </summary>
    internal static int Notches(int delta) => Math.Max(1, Math.Abs(delta) / WheelDelta);

    /// <summary>
    /// The new volume percentage. Clamped to 0-100 rather than wrapping, and the
    /// step floored at 1 so a zero or negative setting cannot make scrolling a
    /// no-op.
    /// </summary>
    internal static int Adjust(int currentPercent, int delta, int scrollStep)
    {
        int step = Math.Max(1, scrollStep) * Notches(delta);
        int pct = currentPercent + (delta > 0 ? step : -step);
        return Math.Clamp(pct, 0, 100);
    }

    /// <summary>
    /// Scrolling up on a muted device unmutes it, rather than silently moving a
    /// level nobody can hear. Scrolling down leaves it muted - that direction is
    /// unambiguous about wanting less sound, not more.
    /// </summary>
    internal static bool Unmutes(bool muted, int delta) => muted && delta > 0;
}

using System.Drawing;
using Infolume.Native;
using Xunit;

namespace Infolume.Tests;

/// <summary>
/// The scroll-over-the-icon gesture, tested where it is now testable.
///
/// <see cref="WheelGesture.Claims"/> is the decision the low-level hook callback
/// makes, and the only one it cannot defer - the callback's return value either
/// swallows the wheel event or passes it on, so it has to be answered inline. It
/// is worth pinning down precisely: claiming too widely eats scrolls that belong
/// to the taskbar, and claiming too narrowly is the "scroll does nothing" bug.
///
/// The rest is the arithmetic that used to sit inline in TrayApp.OnTrayWheel.
/// </summary>
public class WheelGestureTests
{
    /// <summary>A plausible tray icon: 32px square near the bottom-right corner.</summary>
    private static readonly Rectangle Icon = new(1850, 1040, 32, 32);

    // ---------------------------------------------------------------- claims

    [Fact]
    public void AnUnknownRectClaimsNothing()
    {
        // Not a failure case. The shell refuses to report the rect under a
        // replaced taskbar, and the cursor may never have hovered the icon. The
        // wheel must then behave completely normally rather than misfire.
        Assert.False(WheelGesture.Claims(null, 1860, 1050));
    }

    [Fact]
    public void ADegenerateRectClaimsNothing()
    {
        // Shell_NotifyIconGetRect can succeed and still hand back a zero rect. Note
        // the second case is not Rectangle.IsEmpty - that is only true at the
        // origin - so this is guarded on the dimensions, not on that property.
        Assert.False(WheelGesture.Claims(Rectangle.Empty, 0, 0));
        Assert.False(WheelGesture.Claims(new Rectangle(1850, 1040, 0, 0), 1850, 1040));
        Assert.False(WheelGesture.Claims(new Rectangle(1850, 1040, 32, 0), 1866, 1040));
    }

    [Fact]
    public void APointInsideTheIconIsClaimed()
        => Assert.True(WheelGesture.Claims(Icon, 1866, 1056));

    [Theory]
    [InlineData(1849, 1056)]   // one pixel left
    [InlineData(1882, 1056)]   // one pixel right of the exclusive edge
    [InlineData(1866, 1039)]   // one pixel above
    [InlineData(1866, 1072)]   // one pixel below
    public void APointOutsideTheIconIsNotClaimed(int x, int y)
        => Assert.False(WheelGesture.Claims(Icon, x, y));

    [Fact]
    public void TheRectIsHalfOpen()
    {
        // Rectangle.Contains includes left and top and excludes right and bottom.
        // Adjacent icons therefore tile without overlapping, which is the property
        // that matters: two hit tests can never both claim the same pixel.
        Assert.True(WheelGesture.Claims(Icon, Icon.Left, Icon.Top));
        Assert.False(WheelGesture.Claims(Icon, Icon.Right, Icon.Top));
        Assert.False(WheelGesture.Claims(Icon, Icon.Left, Icon.Bottom));
    }

    [Fact]
    public void ARectOnANegativeCoordinateMonitorStillClaims()
    {
        // A second monitor above or left of the primary one has negative screen
        // coordinates, and the taskbar can live there.
        var icon = new Rectangle(-1200, -300, 32, 32);
        Assert.True(WheelGesture.Claims(icon, -1190, -290));
        Assert.False(WheelGesture.Claims(icon, -1210, -290));
    }

    // ----------------------------------------------------------------- delta

    [Fact]
    public void ScrollUpReadsAsAPositiveDelta()
        => Assert.Equal(120, WheelGesture.DeltaFrom(120u << 16));

    [Fact]
    public void ScrollDownReadsAsANegativeDelta()
    {
        // mouseData carries the delta in the high word as a signed short, so
        // scroll-down arrives as 0xFF88 there and must come back as -120 rather
        // than 65416.
        Assert.Equal(-120, WheelGesture.DeltaFrom(0xFF88_0000u));
    }

    [Fact]
    public void TheLowWordOfMouseDataIsIgnored()
    {
        // The low word is reserved and not guaranteed zero.
        Assert.Equal(120, WheelGesture.DeltaFrom((120u << 16) | 0xFFFF));
    }

    [Fact]
    public void ACoalescedFlickReadsAsAMultipleOfOneNotch()
    {
        Assert.Equal(360, WheelGesture.DeltaFrom(360u << 16));
        Assert.Equal(-360, WheelGesture.DeltaFrom(0xFE98_0000u));
    }

    [Fact]
    public void TheWholeNegativeHalfOfTheHighWordSurvivesTheConversion()
    {
        // The reinterpret-as-signed is only legal in an unchecked context, which
        // DeltaFrom states explicitly rather than inheriting from the compiler
        // default. If that ever regressed, every one of these would throw
        // OverflowException rather than return - and it would throw inside the
        // hook callback, which is the worst place in this codebase for it.
        Assert.Equal(-1, WheelGesture.DeltaFrom(0xFFFF_0000u));
        Assert.Equal(short.MinValue, WheelGesture.DeltaFrom(0x8000_0000u));
        Assert.Equal(short.MaxValue, WheelGesture.DeltaFrom(0x7FFF_0000u));
    }

    // --------------------------------------------------------------- notches

    [Theory]
    [InlineData(120, 1)]
    [InlineData(-120, 1)]
    [InlineData(240, 2)]
    [InlineData(-360, 3)]
    [InlineData(1200, 10)]
    public void NotchesScaleWithTheDelta(int delta, int expected)
        => Assert.Equal(expected, WheelGesture.Notches(delta));

    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(-40)]
    [InlineData(119)]
    public void ASubNotchDeltaStillCountsAsOne(int delta)
    {
        // A high-resolution wheel or a precision touchpad can report less than a
        // full notch. Flooring at one keeps such a device from being inert.
        Assert.Equal(1, WheelGesture.Notches(delta));
    }

    // ---------------------------------------------------------------- adjust

    [Fact]
    public void ScrollingUpAddsOneStep()
        => Assert.Equal(45, WheelGesture.Adjust(40, 120, 5));

    [Fact]
    public void ScrollingDownSubtractsOneStep()
        => Assert.Equal(35, WheelGesture.Adjust(40, -120, 5));

    [Fact]
    public void AFlickAppliesEveryNotchItCarries()
    {
        // The behaviour a single message carrying three notches must produce, or a
        // hard scroll moves the volume one step and feels broken.
        Assert.Equal(55, WheelGesture.Adjust(40, 360, 5));
        Assert.Equal(25, WheelGesture.Adjust(40, -360, 5));
    }

    [Fact]
    public void TheResultIsClampedToTheVolumeRange()
    {
        Assert.Equal(100, WheelGesture.Adjust(98, 120, 5));
        Assert.Equal(0, WheelGesture.Adjust(2, -120, 5));
        Assert.Equal(100, WheelGesture.Adjust(100, 1200, 5));
        Assert.Equal(0, WheelGesture.Adjust(0, -1200, 5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ADegenerateStepStillMovesTheVolume(int step)
    {
        // ScrollStep is user-editable. A zero would make scrolling a silent no-op
        // and a negative one would invert the gesture; both are floored to 1.
        Assert.Equal(41, WheelGesture.Adjust(40, 120, step));
        Assert.Equal(39, WheelGesture.Adjust(40, -120, step));
    }

    [Fact]
    public void ADeltaOfZeroIsTreatedAsScrollDown()
    {
        // Not a case any real device produces, but the sign test is `delta > 0`
        // and this records which way that falls rather than leaving it to be
        // rediscovered.
        Assert.Equal(35, WheelGesture.Adjust(40, 0, 5));
    }

    // ---------------------------------------------------------------- unmute

    [Fact]
    public void ScrollingUpOnAMutedDeviceUnmutesIt()
        => Assert.True(WheelGesture.Unmutes(muted: true, delta: 120));

    [Fact]
    public void ScrollingDownOnAMutedDeviceLeavesItMuted()
        => Assert.False(WheelGesture.Unmutes(muted: true, delta: -120));

    [Fact]
    public void ScrollingOnAnUnmutedDeviceNeverTouchesMute()
    {
        Assert.False(WheelGesture.Unmutes(muted: false, delta: 120));
        Assert.False(WheelGesture.Unmutes(muted: false, delta: -120));
    }
}

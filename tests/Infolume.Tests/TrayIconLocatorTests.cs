using System.Drawing;
using System.Reflection;
using Infolume.Ui;
using Xunit;

namespace Infolume.Tests;

/// <summary>
/// The locator only ever sees points it already knows are inside the icon, so
/// the interesting behaviour is entirely about what it does when those points
/// stop being consistent with each other - which is what a tray re-layout or a
/// resolution change produces.
/// </summary>
public class TrayIconLocatorTests
{
    private const int IconSize = 32;

    /// <summary>Beyond this the box is treated as spanning more than one icon.</summary>
    private const int Limit = 48;   // (int)(32 * 1.5)

    private static TrayIconLocator Sweep(int iconSize, params Point[] points)
    {
        var loc = new TrayIconLocator();
        foreach (var p in points) loc.NoteHover(p, iconSize);
        return loc;
    }

    // ------------------------------------------------------------ empty case

    [Fact]
    public void NoHoverYetMeansNoEstimate()
        => Assert.Null(new TrayIconLocator().Estimate(IconSize));

    [Fact]
    public void ResetReturnsItToTheEmptyState()
    {
        var loc = Sweep(IconSize, new Point(1000, 1050), new Point(1008, 1052));
        Assert.NotNull(loc.Estimate(IconSize));

        loc.Reset();
        Assert.Null(loc.Estimate(IconSize));
    }

    // ---------------------------------------------------------------- growth

    [Fact]
    public void OneHoverGivesAFullIconSquareAroundThatPoint()
    {
        var est = Sweep(IconSize, new Point(1000, 1050)).Estimate(IconSize);

        Assert.NotNull(est);
        Assert.Equal(IconSize, est!.Value.Width);
        Assert.Equal(IconSize, est.Value.Height);
        Assert.True(est.Value.Contains(1000, 1050));
    }

    [Fact]
    public void NearbyHoversAccumulateIntoOneBox()
    {
        var points = new[] { new Point(1000, 1050), new Point(1010, 1055), new Point(1020, 1052) };
        var est = Sweep(IconSize, points).Estimate(IconSize);

        Assert.NotNull(est);
        foreach (var p in points)
            Assert.True(est!.Value.Contains(p), $"estimate {est} lost the hover at {p}");
    }

    [Fact]
    public void APointExactlyOnTheLimitIsStillGrowth()
    {
        // Union width lands on the limit itself, which the check does not exceed.
        var loc = Sweep(IconSize, new Point(1000, 1050), new Point(1000 + Limit - 1, 1050));
        var est = loc.Estimate(IconSize);

        Assert.NotNull(est);
        Assert.True(est!.Value.Contains(1000, 1050));
        Assert.True(est.Value.Contains(1000 + Limit - 1, 1050));
    }

    // ------------------------------------------------------------ relocation

    [Fact]
    public void APointBeyondOneIconRelocatesInsteadOfStretching()
    {
        // The bug this exists to prevent: unioning across a move averages the two
        // positions into a box matching neither, and the wheel silently stops
        // working because its hit test never lands on the icon again.
        var loc = Sweep(IconSize, new Point(1000, 1050), new Point(200, 1050));
        var est = loc.Estimate(IconSize);

        Assert.NotNull(est);
        Assert.True(est!.Value.Contains(200, 1050), "the new position was not adopted");
        Assert.False(est.Value.Contains(1000, 1050), "the old position was kept");
        Assert.False(est.Value.Contains(600, 1050), "the box averaged the two positions");
    }

    [Fact]
    public void RelocationAppliesVerticallyToo()
    {
        // A taskbar moving from the bottom edge to the top is a Y-axis move.
        var loc = Sweep(IconSize, new Point(1000, 1050), new Point(1000, 20));
        var est = loc.Estimate(IconSize);

        Assert.NotNull(est);
        Assert.True(est!.Value.Contains(1000, 20));
        Assert.False(est.Value.Contains(1000, 1050));
    }

    [Fact]
    public void OnePixelPastTheLimitIsAlreadyARelocation()
    {
        var loc = Sweep(IconSize, new Point(1000, 1050), new Point(1000 + Limit, 1050));
        var est = loc.Estimate(IconSize);

        Assert.NotNull(est);
        Assert.False(est!.Value.Contains(1000, 1050));
    }

    [Fact]
    public void AResolutionChangeRecoversOnTheNextHoverEvenWithoutAReset()
    {
        // Reset is called on DisplaySettingsChanged, but a tray re-layout that
        // raises no event has to heal itself from the hover points alone.
        var loc = Sweep(IconSize,
            new Point(1850, 1050), new Point(1858, 1052), new Point(1866, 1049));

        loc.NoteHover(new Point(1210, 700), IconSize);   // same icon, new screen geometry

        var est = loc.Estimate(IconSize);
        Assert.NotNull(est);
        Assert.True(est!.Value.Contains(1210, 700));
        Assert.False(est.Value.Contains(1858, 1052));
    }

    [Fact]
    public void ALongSweepNeverInflatesTheBoxAcrossNeighbouringIcons()
    {
        var loc = new TrayIconLocator();
        for (int x = 800; x <= 1200; x += 4)
        {
            loc.NoteHover(new Point(x, 1050), IconSize);
            var est = loc.Estimate(IconSize);

            Assert.NotNull(est);
            Assert.True(est!.Value.Width <= Limit, $"box grew to {est.Value.Width}px at x={x}");
            Assert.True(est.Value.Height <= Limit, $"box grew to {est.Value.Height}px at x={x}");
        }
    }

    [Fact]
    public void ADegenerateIconSizeStillBoundsTheBox()
    {
        // iconSize can be 0 before the first repaint has measured it. The floor of
        // 8px is what stops the limit collapsing to zero and relocating on every
        // single point, which would make the estimate useless. Read the box back
        // at 8px too: asking at 32 would re-inflate it and hide the difference.
        var near = Sweep(0, new Point(1000, 1050), new Point(1005, 1050)).Estimate(8);
        Assert.NotNull(near);
        Assert.True(near!.Value.Contains(1005, 1050));
        Assert.True(near.Value.Contains(1000, 1050));

        var far = Sweep(0, new Point(1000, 1050), new Point(1010, 1050)).Estimate(8);
        Assert.NotNull(far);
        Assert.True(far!.Value.Contains(1010, 1050));
        Assert.False(far.Value.Contains(1000, 1050));
    }

    // -------------------------------------------------------------- estimate

    [Fact]
    public void EstimateIsSizedAgainstTheIconSizeItIsAskedAbout()
    {
        // The box is learned at one icon size and may be read back at another:
        // the icon is repainted at the DPI of whichever monitor it is on.
        var loc = Sweep(IconSize, new Point(1000, 1050), new Point(1040, 1050));

        var small = loc.Estimate(16);
        Assert.NotNull(small);
        Assert.InRange(small!.Value.Width, 16, 24);
        Assert.InRange(small.Value.Height, 16, 24);

        var large = loc.Estimate(48);
        Assert.NotNull(large);
        Assert.InRange(large!.Value.Width, 48, 72);
        Assert.InRange(large.Value.Height, 48, 72);
    }

    [Fact]
    public void EstimateIsNeverSmallerThanOneIcon()
    {
        var est = Sweep(IconSize, new Point(1000, 1050)).Estimate(IconSize);

        Assert.NotNull(est);
        Assert.True(est!.Value.Width >= IconSize);
        Assert.True(est.Value.Height >= IconSize);
    }

    // ------------------------------------------------------------- staleness

    [Fact]
    public void AnEstimateOlderThanThirtyMinutesIsNotTrusted()
    {
        var loc = Sweep(IconSize, new Point(1000, 1050));
        Assert.NotNull(loc.Estimate(IconSize));

        Age(loc, TimeSpan.FromMinutes(31));
        Assert.Null(loc.Estimate(IconSize));
    }

    [Fact]
    public void AnEstimateInsideTheStaleWindowIsStillTrusted()
    {
        var loc = Sweep(IconSize, new Point(1000, 1050));

        Age(loc, TimeSpan.FromMinutes(29));
        Assert.NotNull(loc.Estimate(IconSize));
    }

    [Fact]
    public void AHoverRefreshesAStaleEstimate()
    {
        var loc = Sweep(IconSize, new Point(1000, 1050));
        Age(loc, TimeSpan.FromMinutes(31));
        Assert.Null(loc.Estimate(IconSize));

        loc.NoteHover(new Point(1004, 1052), IconSize);
        Assert.NotNull(loc.Estimate(IconSize));
    }

    /// <summary>
    /// Backdates the last-hover stamp. Reaching for the private field keeps the
    /// production type free of a clock seam it would otherwise only have for the
    /// sake of these three tests; a rename fails the assert rather than silently
    /// skipping them.
    /// </summary>
    private static void Age(TrayIconLocator loc, TimeSpan by)
    {
        var field = typeof(TrayIconLocator).GetField("_lastHover",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);

        var current = (DateTime)field!.GetValue(loc)!;
        field.SetValue(loc, current - by);
    }
}

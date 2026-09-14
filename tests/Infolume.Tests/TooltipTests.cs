using Infolume;
using Infolume.Audio;
using Infolume.Config;
using Xunit;
using static Infolume.Tests.Fixtures;

namespace Infolume.Tests;

/// <summary>
/// The hover tooltip. <c>NotifyIcon.Text</c> throws above 63 characters, so the
/// length cap is a crash guard rather than a formatting preference - and it is
/// reachable from a user-supplied alias, which has no length limit of its own.
/// </summary>
public class TooltipTests
{
    private const int Cap = 63;

    private static string Tip(EndpointInfo? ep, Settings? settings = null)
        => TrayApp.Tooltip(ep, settings ?? new Settings());

    // ------------------------------------------------------------- the cap

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(200)]
    [InlineData(5000)]
    public void TheResultNeverExceedsWhatNotifyIconAccepts(int aliasLength)
    {
        var s = new Settings();
        s.For(DeviceId).Alias = new string('W', aliasLength);

        var tip = Tip(Ep(adapter: new string('A', 120)), s);

        Assert.True(tip.Length <= Cap, $"tooltip was {tip.Length} chars: {tip}");
    }

    [Fact]
    public void AnAbsurdAliasIsTruncatedToExactlyTheCap()
    {
        var s = new Settings();
        s.For(DeviceId).Alias = new string('W', 500);

        Assert.Equal(Cap, Tip(Ep(), s).Length);
    }

    // -------------------------------------------------- what gets dropped first

    [Fact]
    public void EverythingFitsWhenThereIsRoom()
    {
        var tip = Tip(Ep(description: "Speakers", adapter: "Realtek HD Audio", volume: 0.5f));

        Assert.Equal("Infolume\nSpeakers  50%\nRealtek HD Audio", tip);
    }

    [Fact]
    public void TheAdapterIsTheFirstThingDropped()
    {
        var tip = Tip(Ep(description: "Speakers",
                         adapter: "Some Extremely Long Adapter Name From A Driver",
                         volume: 0.5f));

        Assert.Equal("Infolume\nSpeakers  50%", tip);
        Assert.DoesNotContain("Adapter", tip);
    }

    [Fact]
    public void TheAppNameGoesNextAndTheDeviceLineAlwaysSurvives()
    {
        // A device line long enough that prefixing "Infolume\n" would breach the
        // cap, but short enough to stand on its own.
        var s = new Settings();
        s.For(DeviceId).Alias = new string('W', 55);

        var tip = Tip(Ep(volume: 0.5f), s);

        Assert.DoesNotContain("Infolume", tip);
        Assert.StartsWith("WWWWW", tip);
        Assert.EndsWith("50%", tip);
        Assert.True(tip.Length <= Cap);
    }

    [Fact]
    public void TheLevelSurvivesEvenWhenTheNameIsTruncated()
    {
        // Truncation cuts from the end, so a name long enough to overrun the cap
        // takes the percentage with it. Worth pinning: the level is the whole
        // point of the tooltip, and this is the one case where it is lost.
        var s = new Settings();
        s.For(DeviceId).Alias = new string('W', 100);

        var tip = Tip(Ep(volume: 0.5f), s);

        Assert.Equal(new string('W', Cap), tip);
    }

    // ------------------------------------------------------------- content

    [Fact]
    public void NoDeviceSaysSoInsteadOfRenderingAnEmptyTooltip()
        => Assert.Equal("Infolume: no output device", Tip(null));

    [Fact]
    public void MutedReadsAsMutedNotAsAPercentage()
    {
        // The icon draws the same slash at 0% and when muted; the tooltip is where
        // the two are actually distinguished.
        var tip = Tip(Ep(description: "Speakers", volume: 0.42f, muted: true));

        Assert.Contains("Speakers  muted", tip);
        Assert.DoesNotContain("42%", tip);
    }

    [Fact]
    public void SilenceAtZeroReadsAsAPercentageNotAsMuted()
    {
        var tip = Tip(Ep(description: "Speakers", volume: 0f, muted: false));

        Assert.Contains("Speakers  0%", tip);
        Assert.DoesNotContain("muted", tip);
    }

    [Theory]
    [InlineData(0f, "0%")]
    [InlineData(0.5f, "50%")]
    [InlineData(1f, "100%")]
    [InlineData(0.07f, "7%")]
    [InlineData(0.999f, "100%")]
    public void TheLevelIsRenderedAsAWholePercentage(float volume, string expected)
        => Assert.Contains($"Speakers  {expected}", Tip(Ep(description: "Speakers", volume: volume)));

    [Fact]
    public void TheAliasIsUsedWhenOneIsSet()
    {
        var s = new Settings();
        s.For(DeviceId).Alias = "Desk speakers";

        var tip = Tip(Ep(description: "Speakers", adapter: "Realtek", volume: 0.3f), s);

        Assert.Contains("Desk speakers  30%", tip);
        Assert.DoesNotContain("Speakers  30%", tip.Replace("Desk speakers  30%", ""));
    }

    [Fact]
    public void TheAdapterLineIsNeverReplacedByTheAlias()
    {
        // The adapter is what tells two endpoints both called "Speakers" apart, so
        // an alias must never stand in for it.
        var s = new Settings();
        s.For(DeviceId).Alias = "Desk";

        var tip = Tip(Ep(description: "Speakers", adapter: "Steam Streaming", volume: 0.3f), s);

        Assert.EndsWith("\nSteam Streaming", tip);
    }

    [Fact]
    public void TheAppNameLeadsSoTheIconIsNotAnonymous()
        => Assert.StartsWith("Infolume", Tip(Ep(description: "Speakers", adapter: "Realtek")));
}

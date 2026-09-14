using Infolume.Audio;
using Infolume.Config;
using Infolume.Icons;
using Xunit;
using static Infolume.Tests.Fixtures;

namespace Infolume.Tests;

/// <summary>
/// The four-tier fallback chain. What matters is not only that each tier maps
/// correctly, but that a tier wins only when the ones above it had nothing
/// useful to say - two of them deliberately decline rather than short-circuit.
/// </summary>
public class GlyphResolverTests
{
    private static Settings Empty() => new();

    // ------------------------------------------------------- display glasses

    // XREAL, Viture and the rest arrive over DisplayPort, so Windows describes
    // them exactly as it describes a television. The name is the only signal, and
    // it is consulted only where the answer would otherwise have been Tv.

    [Theory]
    [InlineData("XREAL Air 2")]
    [InlineData("VITURE One")]
    [InlineData("Rokid Max")]
    [InlineData("RayNeo Air 2s")]
    [InlineData("Legion Glasses")]
    [InlineData("nreal air")]          // pre-rename, and lower case
    public void DisplayGlassesGetTheGlassesGlyph(string name)
    {
        var ep = Ep(description: name,
                    adapter: "NVIDIA High Definition Audio",
                    formFactor: FormFactor.DigitalAudioDisplayDevice);

        Assert.Equal(GlyphKind.Glasses, GlyphResolver.Resolve(ep, Empty()));
    }

    [Fact]
    public void TheBrandCanBeOnTheAdapterRatherThanTheDevice()
    {
        var ep = Ep(description: "Digital Output",
                    adapter: "XREAL One Pro",
                    formFactor: FormFactor.DigitalAudioDisplayDevice);

        Assert.Equal(GlyphKind.Glasses, GlyphResolver.Resolve(ep, Empty()));
    }

    [Theory]
    [InlineData("LG TV", "NVIDIA High Definition Audio")]
    [InlineData("SAMSUNG", "NVIDIA High Definition Audio")]
    [InlineData("DELL U2723QE", "Intel(R) Display Audio")]
    public void OrdinaryDisplaysStillGetTheTelevisionGlyph(string name, string adapter)
    {
        var ep = Ep(description: name, adapter: adapter,
                    formFactor: FormFactor.DigitalAudioDisplayDevice);

        Assert.Equal(GlyphKind.Tv, GlyphResolver.Resolve(ep, Empty()));
    }

    [Fact]
    public void TheGlassesRuleOnlyAppliesToDisplayEndpoints()
    {
        // A named-alike that is not on a display connector must not be diverted:
        // headphones called "Rokid" are still headphones.
        var ep = Ep(description: "Rokid Headphones", formFactor: FormFactor.Headphones);

        Assert.Equal(GlyphKind.Headphones, GlyphResolver.Resolve(ep, Empty()));
    }

    [Fact]
    public void TheDisplayJackAlsoRoutesThroughTheGlassesCheck()
    {
        // Tier 2 short-circuits on the observed DisplayPort jack GUID, so the
        // check has to happen there too or a device with that jack would reach
        // tier 3 already decided.
        var ep = Ep(description: "VITURE Pro XR",
                    jackSubType: KsDisplayObserved,
                    formFactor: FormFactor.DigitalAudioDisplayDevice);

        Assert.Equal(GlyphKind.Glasses, GlyphResolver.Resolve(ep, Empty()));
    }

    [Fact]
    public void AUserOverrideStillBeatsTheGlassesRule()
    {
        var s = Empty();
        s.For(DeviceId).Glyph = GlyphKind.Tv;

        var ep = Ep(description: "XREAL Air 2",
                    formFactor: FormFactor.DigitalAudioDisplayDevice);

        Assert.Equal(GlyphKind.Tv, GlyphResolver.Resolve(ep, s));
    }

    // ---------------------------------------------------------------- tier 1

    [Fact]
    public void UserOverrideBeatsEverySignalBelowIt()
    {
        var s = Empty();
        s.For(DeviceId).Glyph = GlyphKind.Earbuds;

        // Every lower tier points somewhere else, and none of them get a vote.
        var ep = Ep(jackSubType: KsHeadphones,
                    formFactor: FormFactor.DigitalAudioDisplayDevice,
                    iconPath: @"%windir%\system32\mmres.dll,-3016");

        Assert.Equal(GlyphKind.Earbuds, GlyphResolver.Resolve(ep, s));
    }

    [Fact]
    public void OverrideForAnotherDeviceIsIgnored()
    {
        var s = Empty();
        s.For("{0.0.0.00000000}.{some-other-device}").Glyph = GlyphKind.Earbuds;

        Assert.Equal(GlyphKind.Headphones,
            GlyphResolver.Resolve(Ep(formFactor: FormFactor.Headphones), s));
    }

    [Fact]
    public void OverrideWithoutAGlyphFallsThrough()
    {
        // A row edited only to rename or recolour it still has an override entry;
        // that must not pin the glyph to anything.
        var s = Empty();
        s.For(DeviceId).Alias = "Desk speakers";
        s.For(DeviceId).ColorMode = ColorMode.Device;

        Assert.Equal(GlyphKind.Tv,
            GlyphResolver.Resolve(Ep(formFactor: FormFactor.DigitalAudioDisplayDevice), s));
    }

    [Fact]
    public void OverrideLookupIgnoresDeviceIdCase()
    {
        var s = Empty();
        s.For(DeviceId.ToUpperInvariant()).Glyph = GlyphKind.Monitor;

        Assert.Equal(GlyphKind.Monitor, GlyphResolver.Resolve(Ep(), s));
    }

    // ---------------------------------------------------------------- tier 2

    [Fact]
    public void HeadphoneJackWinsOverFormFactor()
    {
        var ep = Ep(jackSubType: KsHeadphones, formFactor: FormFactor.Speakers);
        Assert.Equal(GlyphKind.Headphones, GlyphResolver.Resolve(ep, Empty()));
    }

    [Fact]
    public void HeadphoneJackOnABluetoothEndpointGivesTheBluetoothGlyph()
    {
        var ep = Ep(jackSubType: KsHeadphones, enumerator: "BTHENUM");
        Assert.Equal(GlyphKind.BluetoothHeadphones, GlyphResolver.Resolve(ep, Empty()));
    }

    [Fact]
    public void ObservedDisplayJackGivesTv()
    {
        var ep = Ep(jackSubType: KsDisplayObserved, formFactor: FormFactor.Speakers);
        Assert.Equal(GlyphKind.Tv, GlyphResolver.Resolve(ep, Empty()));
    }

    [Theory]
    [InlineData(false)]   // KsSpeaker
    [InlineData(true)]    // KsLineConnector
    public void GenericJackTypesDeclineSoFormFactorStillDecides(bool lineConnector)
    {
        // Three of four endpoints on the dev machine report the generic speaker
        // GUID, so treating it as authoritative would flatten the whole icon set.
        var ep = Ep(jackSubType: lineConnector ? KsLineConnector : KsSpeaker,
                    formFactor: FormFactor.Headphones);

        Assert.Equal(GlyphKind.Headphones, GlyphResolver.Resolve(ep, Empty()));
    }

    [Fact]
    public void UnrecognisedJackTypeFallsThroughRatherThanGivingUp()
    {
        var ep = Ep(jackSubType: KsUnknown, formFactor: FormFactor.SPDIF);
        Assert.Equal(GlyphKind.Spdif, GlyphResolver.Resolve(ep, Empty()));
    }

    [Fact]
    public void UnrecognisedJackTypeStillReachesTheIconPathTier()
    {
        var ep = Ep(jackSubType: KsUnknown, iconPath: @"mmres.dll,-3016");
        Assert.Equal(GlyphKind.Network, GlyphResolver.Resolve(ep, Empty()));
    }

    // ---------------------------------------------------------------- tier 3

    [Theory]
    [InlineData(FormFactor.Headphones, GlyphKind.Headphones)]
    [InlineData(FormFactor.Headset, GlyphKind.Headset)]
    [InlineData(FormFactor.Handset, GlyphKind.Headset)]
    [InlineData(FormFactor.DigitalAudioDisplayDevice, GlyphKind.Tv)]
    [InlineData(FormFactor.SPDIF, GlyphKind.Spdif)]
    [InlineData(FormFactor.UnknownDigitalPassthrough, GlyphKind.Spdif)]
    [InlineData(FormFactor.RemoteNetworkDevice, GlyphKind.Network)]
    [InlineData(FormFactor.LineLevel, GlyphKind.Line)]
    [InlineData(FormFactor.Speakers, GlyphKind.Speakers)]
    public void FormFactorMapsToItsGlyph(FormFactor ff, GlyphKind expected)
        => Assert.Equal(expected, GlyphResolver.Resolve(Ep(formFactor: ff, enumerator: "PCI"), Empty()));

    [Theory]
    [InlineData(FormFactor.Microphone)]
    [InlineData(FormFactor.UnknownFormFactor)]
    public void FormFactorsWithNoGlyphFallThroughToTheIconPath(FormFactor ff)
    {
        var ep = Ep(formFactor: ff, iconPath: @"%windir%\system32\mmres.dll,-3011");
        Assert.Equal(GlyphKind.Headphones, GlyphResolver.Resolve(ep, Empty()));
    }

    // ------------------------------------------------------- bluetooth detection

    [Theory]
    [InlineData("BTHENUM")]
    [InlineData("BTHA2DP")]
    [InlineData("bthenum")]         // the comparison is case-insensitive
    [InlineData("BTHLEDEVICE")]
    public void BluetoothEnumeratorsTurnSpeakersIntoBluetoothHeadphones(string enumerator)
    {
        var ep = Ep(formFactor: FormFactor.Speakers, enumerator: enumerator);
        Assert.Equal(GlyphKind.BluetoothHeadphones, GlyphResolver.Resolve(ep, Empty()));
    }

    [Theory]
    [InlineData("USB")]
    [InlineData("HDAUDIO")]
    [InlineData("PCI")]
    [InlineData("")]
    public void NonBluetoothEnumeratorsLeaveSpeakersAlone(string enumerator)
    {
        var ep = Ep(formFactor: FormFactor.Speakers, enumerator: enumerator);
        Assert.Equal(GlyphKind.Speakers, GlyphResolver.Resolve(ep, Empty()));
    }

    [Fact]
    public void BluetoothIsMatchedOnThePrefixOnly()
    {
        // The test is a prefix, not equality, because the enumerator carries a
        // suffix on some stacks. A name merely CONTAINING it must not match.
        Assert.Equal(GlyphKind.BluetoothHeadphones,
            GlyphResolver.Resolve(Ep(formFactor: FormFactor.Speakers, enumerator: "BTHX"), Empty()));
        Assert.Equal(GlyphKind.Speakers,
            GlyphResolver.Resolve(Ep(formFactor: FormFactor.Speakers, enumerator: "XBTHENUM"), Empty()));
    }

    // -------------------------------------------------------- virtual heuristics

    [Fact]
    public void SwdEnumeratorIsAlwaysVirtual()
    {
        var ep = Ep(formFactor: FormFactor.Speakers, enumerator: "SWD",
                    description: "Speakers", adapter: "Realtek High Definition Audio");
        Assert.Equal(GlyphKind.Virtual, GlyphResolver.Resolve(ep, Empty()));
    }

    [Fact]
    public void RootAloneIsNotEnoughToCallSomethingVirtual()
    {
        // ROOT covers real drivers too, so it needs corroboration from the name.
        var ep = Ep(formFactor: FormFactor.Speakers, enumerator: "ROOT",
                    description: "Speakers", adapter: "High Definition Audio Device");
        Assert.Equal(GlyphKind.Speakers, GlyphResolver.Resolve(ep, Empty()));
    }

    [Theory]
    [InlineData("Virtual Audio Device", "Some Vendor")]
    [InlineData("CABLE Input", "VB-Audio Virtual Cable")]
    [InlineData("Speakers", "Steam Streaming Speakers")]
    [InlineData("Speakers", "AudioRelay Relay Device")]
    [InlineData("Loopback", "Whatever")]
    [InlineData("Speakers", "VoiceMeeter Aux Input")]
    [InlineData("SPEAKERS", "STEAM STREAMING SPEAKERS")]   // tells are case-insensitive
    public void RootPlusATellInTheNameIsVirtual(string description, string adapter)
    {
        var ep = Ep(formFactor: FormFactor.Speakers, enumerator: "ROOT",
                    description: description, adapter: adapter);
        Assert.Equal(GlyphKind.Virtual, GlyphResolver.Resolve(ep, Empty()));
    }

    [Fact]
    public void LineLevelUsesTheSameVirtualTest()
    {
        Assert.Equal(GlyphKind.Virtual,
            GlyphResolver.Resolve(Ep(formFactor: FormFactor.LineLevel, enumerator: "SWD"), Empty()));
        Assert.Equal(GlyphKind.Line,
            GlyphResolver.Resolve(Ep(formFactor: FormFactor.LineLevel, enumerator: "HDAUDIO"), Empty()));
    }

    [Fact]
    public void BluetoothWinsOverTheVirtualTestForSpeakers()
    {
        // Ordering inside the Speakers arm: a Bluetooth speaker whose adapter
        // happens to contain a tell is still Bluetooth.
        var ep = Ep(formFactor: FormFactor.Speakers, enumerator: "BTHENUM",
                    adapter: "Streaming Speaker");
        Assert.Equal(GlyphKind.BluetoothHeadphones, GlyphResolver.Resolve(ep, Empty()));
    }

    // ---------------------------------------------------------------- tier 4

    [Theory]
    [InlineData(3010, GlyphKind.Speakers)]
    [InlineData(3011, GlyphKind.Headphones)]
    [InlineData(3012, GlyphKind.Line)]
    [InlineData(3013, GlyphKind.Spdif)]
    [InlineData(3014, GlyphKind.Headset)]
    [InlineData(3015, GlyphKind.Headset)]
    [InlineData(3016, GlyphKind.Network)]
    [InlineData(3017, GlyphKind.Tv)]
    [InlineData(3018, GlyphKind.Monitor)]
    public void MmresResourceIdsMapToGlyphs(int id, GlyphKind expected)
    {
        var ep = Ep(iconPath: $@"%windir%\system32\mmres.dll,-{id}");
        Assert.Equal(expected, GlyphResolver.Resolve(ep, Empty()));
    }

    [Fact]
    public void ResourceIdSignIsIgnored()
    {
        // Windows writes these negative; the sign is a resource-ordinal convention.
        Assert.Equal(GlyphKind.Tv, GlyphResolver.Resolve(Ep(iconPath: "mmres.dll,3017"), Empty()));
        Assert.Equal(GlyphKind.Tv, GlyphResolver.Resolve(Ep(iconPath: "mmres.dll,-3017"), Empty()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"mmres.dll")]                  // no comma at all
    [InlineData(@"mmres.dll,")]                 // comma with nothing after it
    [InlineData(@"mmres.dll,-abc")]             // not a number
    [InlineData(@"mmres.dll,-99999")]           // a number, but not one we map
    [InlineData(@"C:\path,with,commas\x.dll")]  // last comma wins, still not a number
    public void UnusableIconPathsEndAtUnknown(string iconPath)
        => Assert.Equal(GlyphKind.Unknown, GlyphResolver.Resolve(Ep(iconPath: iconPath), Empty()));

    [Fact]
    public void APathWhoseLastCommaCarriesTheIdStillResolves()
        => Assert.Equal(GlyphKind.Speakers,
            GlyphResolver.Resolve(Ep(iconPath: @"C:\a,b\mmres.dll,-3010"), Empty()));

    // ------------------------------------------------------------- exhaustion

    [Fact]
    public void AnEndpointWindowsSaysNothingAboutIsUnknown()
        => Assert.Equal(GlyphKind.Unknown, GlyphResolver.Resolve(Ep(), Empty()));

    [Fact]
    public void FormFactorIsPreferredOverTheIconPath()
    {
        // Tier 3 before tier 4: the icon path always resolves, so if it were
        // consulted first the form factor tier would never run.
        var ep = Ep(formFactor: FormFactor.Headphones, iconPath: "mmres.dll,-3010");
        Assert.Equal(GlyphKind.Headphones, GlyphResolver.Resolve(ep, Empty()));
    }
}

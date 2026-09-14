using System.Drawing;
using Infolume.Config;
using Infolume.Icons;
using Xunit;

namespace Infolume.Tests;

/// <summary>
/// Settings persistence and resolution. The load path matters more than it looks:
/// it runs before the tray icon exists, so anything it throws leaves the user with
/// no icon and no way to reach the settings that would fix it.
/// </summary>
public class SettingsTests : IDisposable
{
    private readonly string _dir = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "infolume-tests", Guid.NewGuid().ToString("N"));

    private string File_ => System.IO.Path.Combine(_dir, "settings.json");

    public SettingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private Settings RoundTrip(Settings s)
    {
        s.SaveTo(File_);
        return Settings.LoadFrom(File_);
    }

    private Settings LoadRaw(string json)
    {
        System.IO.File.WriteAllText(File_, json);
        return Settings.LoadFrom(File_);
    }

    // --------------------------------------------------------------- defaults

    [Fact]
    public void DefaultsAreTheDocumentedOnes()
    {
        var s = new Settings();

        Assert.Equal(ThemeMode.Auto, s.Theme);
        Assert.True(s.ScrollToAdjust);
        Assert.Equal(2, s.ScrollStep);
        Assert.Equal(0, s.IconSize);              // 0 means "derive from SM_CXSMICON"
        Assert.Equal(AudioCue.None, s.Cue);
        Assert.Equal(VolumeStyle.InclineWide, s.Style);
        Assert.Equal(6, s.InclineBars);           // finest that resolves at 32px
        Assert.False(s.AppsCollapsed);
        Assert.Empty(s.Overrides);
    }

    // ------------------------------------------------------- scroll-to-adjust

    [Fact]
    public void ScrollToAdjustSurvivesARoundTrip()
    {
        var s = new Settings { ScrollToAdjust = false };
        Assert.False(RoundTrip(s).ScrollToAdjust);

        s.ScrollToAdjust = true;
        Assert.True(RoundTrip(s).ScrollToAdjust);
    }

    [Fact]
    public void AConfigWrittenBeforeTheSettingExistedKeepsScrollToAdjustOn()
    {
        // Every settings.json in the wild predates this property. Absent means the
        // property keeps its initialiser, and it has to be the ON one: a silent
        // upgrade that turned scroll-to-adjust off would read as the feature
        // breaking, with nothing in the UI to explain it.
        var s = LoadRaw("""{ "Theme": "Auto", "ScrollStep": 4, "InclineBars": 6 }""");

        Assert.True(s.ScrollToAdjust);
        Assert.Equal(4, s.ScrollStep);
    }

    [Fact]
    public void ScrollToAdjustOffLeavesTheStepAlone()
    {
        // The step is not reset or zeroed when the gesture is switched off, so
        // switching it back on restores what the user had rather than a default.
        var s = RoundTrip(new Settings { ScrollToAdjust = false, ScrollStep = 9 });

        Assert.False(s.ScrollToAdjust);
        Assert.Equal(9, s.ScrollStep);
    }

    [Fact]
    public void ThePathLivesUnderRoamingAppData()
    {
        // Asserted rather than exercised: nothing in this suite may touch the real
        // config file.
        var expected = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Infolume", "settings.json");

        Assert.Equal(expected, Settings.Path);
    }

    // ------------------------------------------------------------- round trip

    [Fact]
    public void EveryPreferenceSurvivesARoundTrip()
    {
        var s = new Settings
        {
            Theme = ThemeMode.ForceDarkTaskbar,
            ScrollStep = 7,
            IconSize = 40,
            Cue = AudioCue.SpeakerBadge,
            Style = VolumeStyle.Arc,
            InclineBars = 8,
            AppsCollapsed = true
        };

        var back = RoundTrip(s);

        Assert.Equal(ThemeMode.ForceDarkTaskbar, back.Theme);
        Assert.Equal(7, back.ScrollStep);
        Assert.Equal(40, back.IconSize);
        Assert.Equal(AudioCue.SpeakerBadge, back.Cue);
        Assert.Equal(VolumeStyle.Arc, back.Style);
        Assert.Equal(8, back.InclineBars);
        Assert.True(back.AppsCollapsed);
    }

    [Fact]
    public void EveryOverrideFieldSurvivesARoundTrip()
    {
        var s = new Settings();
        var ov = s.For(Fixtures.DeviceId);
        ov.Alias = "Desk speakers";
        ov.Glyph = GlyphKind.Monitor;
        ov.ColorMode = ColorMode.Custom;
        ov.CustomColor = "#4C9BE0";
        ov.Hidden = true;

        var back = RoundTrip(s).For(Fixtures.DeviceId);

        Assert.Equal("Desk speakers", back.Alias);
        Assert.Equal(GlyphKind.Monitor, back.Glyph);
        Assert.Equal(ColorMode.Custom, back.ColorMode);
        Assert.Equal("#4C9BE0", back.CustomColor);
        Assert.True(back.Hidden);
    }

    [Fact]
    public void AnUnsetGlyphStaysNullRatherThanBecomingTheFirstEnumMember()
    {
        // GlyphKind? null is what tells the resolver to fall through; a default of
        // Speakers would pin every renamed device to the speaker glyph.
        var s = new Settings();
        s.For(Fixtures.DeviceId).Alias = "Renamed";

        Assert.Null(RoundTrip(s).For(Fixtures.DeviceId).Glyph);
    }

    [Fact]
    public void EnumsAreWrittenAsNamesNotNumbers()
    {
        // The numbers are positional. Writing them would mean any reordering of
        // VolumeStyle silently reinterprets everyone's saved config.
        var s = new Settings { Style = VolumeStyle.Column, Theme = ThemeMode.ForceLightTaskbar };
        s.SaveTo(File_);

        var json = System.IO.File.ReadAllText(File_);
        Assert.Contains("\"Column\"", json);
        Assert.Contains("\"ForceLightTaskbar\"", json);
    }

    [Fact]
    public void OverrideLookupStaysCaseInsensitiveAcrossALoad()
    {
        // The dictionary is built with OrdinalIgnoreCase, but System.Text.Json
        // constructs a fresh one with the default comparer on deserialize, so the
        // comparer has to be reapplied after loading. Without that, a device ID
        // reported in different casing than it was saved in silently loses its
        // alias, glyph and colour.
        var s = new Settings();
        s.For(Fixtures.DeviceId).Alias = "Desk speakers";

        var back = RoundTrip(s);

        Assert.True(back.Overrides.ContainsKey(Fixtures.DeviceId.ToUpperInvariant()));
        Assert.Equal("Desk speakers", back.NameFor(Fixtures.DeviceId.ToUpperInvariant(), "Speakers"));
    }

    [Fact]
    public void SaveCreatesTheDirectoryItNeeds()
    {
        var nested = System.IO.Path.Combine(_dir, "a", "b", "settings.json");
        new Settings().SaveTo(nested);

        Assert.True(System.IO.File.Exists(nested));
    }

    // ------------------------------------------------------- damaged config

    [Fact]
    public void AMissingFileYieldsDefaultsAndWritesNothing()
    {
        var missing = System.IO.Path.Combine(_dir, "does-not-exist.json");

        Assert.Equal(2, Settings.LoadFrom(missing).ScrollStep);
        Assert.False(System.IO.File.Exists(missing));
    }

    [Theory]
    [InlineData("")]                                     // empty file
    [InlineData("   ")]                                  // whitespace only
    [InlineData("{ not json at all")]                    // truncated mid-object
    [InlineData("null")]                                 // valid JSON, no object
    [InlineData("[]")]                                   // valid JSON, wrong shape
    [InlineData("{\"ScrollStep\": \"not a number\"}")]   // right key, wrong type
    [InlineData("{\"Theme\": \"NoSuchTheme\"}")]         // enum name we do not know
    [InlineData("{\"Overrides\": 42}")]                  // dictionary replaced by a scalar
    [InlineData("\0\0\0\0")]                             // the classic half-written file
    public void ACorruptConfigYieldsDefaultsRatherThanThrowing(string json)
    {
        var s = LoadRaw(json);

        Assert.Equal(2, s.ScrollStep);
        Assert.Equal(VolumeStyle.InclineWide, s.Style);
        Assert.NotNull(s.Overrides);
    }

    [Fact]
    public void AnUnknownPropertyIsIgnoredWithoutLosingTheRest()
    {
        // Forward compatibility: a config written by a newer build must not reset
        // everything the current build does understand.
        var s = LoadRaw("{\"ScrollStep\": 5, \"SomeFutureSetting\": {\"nested\": true}}");

        Assert.Equal(5, s.ScrollStep);
    }

    [Fact]
    public void AnExplicitlyNullOverridesMapLoadsAsEmptyRatherThanNull()
    {
        var s = LoadRaw("{\"ScrollStep\": 5, \"Overrides\": null}");

        Assert.NotNull(s.Overrides);
        Assert.Empty(s.Overrides);
        Assert.Equal(5, s.ScrollStep);
    }

    // ----------------------------------------------------------------- For()

    [Fact]
    public void ForCreatesAnEntryAndKeepsReturningTheSameOne()
    {
        var s = new Settings();

        var first = s.For(Fixtures.DeviceId);
        first.Alias = "Desk";

        Assert.Same(first, s.For(Fixtures.DeviceId));
        Assert.Same(first, s.For(Fixtures.DeviceId.ToUpperInvariant()));
        Assert.Single(s.Overrides);
    }

    // --------------------------------------------------------------- NameFor

    [Fact]
    public void NameForFallsBackWhenThereIsNoOverride()
        => Assert.Equal("Speakers", new Settings().NameFor(Fixtures.DeviceId, "Speakers"));

    [Fact]
    public void NameForUsesTheAliasWhenOneIsSet()
    {
        var s = new Settings();
        s.For(Fixtures.DeviceId).Alias = "Desk speakers";

        Assert.Equal("Desk speakers", s.NameFor(Fixtures.DeviceId, "Speakers"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void ABlankAliasFallsBackRatherThanRenderingAnEmptyRow(string? alias)
    {
        var s = new Settings();
        s.For(Fixtures.DeviceId).Alias = alias;

        Assert.Equal("Speakers", s.NameFor(Fixtures.DeviceId, "Speakers"));
    }

    [Fact]
    public void AnAliasIsTrimmed()
    {
        var s = new Settings();
        s.For(Fixtures.DeviceId).Alias = "  Desk speakers  ";

        Assert.Equal("Desk speakers", s.NameFor(Fixtures.DeviceId, "Speakers"));
    }

    [Fact]
    public void AnOverrideForAnotherDeviceDoesNotRenameThisOne()
    {
        var s = new Settings();
        s.For("{0.0.0.00000000}.{other}").Alias = "Desk speakers";

        Assert.Equal("Speakers", s.NameFor(Fixtures.DeviceId, "Speakers"));
    }

    // --------------------------------------------------------------- ParseHex

    [Theory]
    [InlineData("#4C9BE0", 0x4C, 0x9B, 0xE0)]
    [InlineData("4C9BE0", 0x4C, 0x9B, 0xE0)]     // the hash is optional
    [InlineData("4c9be0", 0x4C, 0x9B, 0xE0)]     // and the case is
    [InlineData("#000000", 0x00, 0x00, 0x00)]
    [InlineData("#FFFFFF", 0xFF, 0xFF, 0xFF)]
    public void ParseHexReadsSixDigitColours(string hex, int r, int g, int b)
    {
        var c = Settings.ParseHex(hex);

        Assert.NotNull(c);
        Assert.Equal(Color.FromArgb(255, r, g, b), c!.Value);
        Assert.Equal(255, c.Value.A);   // always opaque
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    [InlineData("#12345")]        // five digits
    [InlineData("#1234567")]      // seven
    [InlineData("#GGGGGG")]       // not hex
    [InlineData("#-4C9BE")]       // a sign is not a digit
    [InlineData("rebeccapurple")]
    [InlineData("rgb(1,2,3)")]
    [InlineData(" #4C9BE0")]      // TrimStart only strips the hash, not the space
    public void ParseHexRejectsAnythingElse(string? hex)
        => Assert.Null(Settings.ParseHex(hex));

    [Fact]
    public void ToHexRoundTripsThroughParseHex()
    {
        foreach (var original in Palette.Accents.Append(Palette.Muted))
        {
            var text = Settings.ToHex(original);
            var back = Settings.ParseHex(text);

            Assert.NotNull(back);
            Assert.Equal(original.ToArgb(), back!.Value.ToArgb());
        }
    }

    [Fact]
    public void ToHexIsUppercaseAndHashPrefixed()
        => Assert.Equal("#4C9BE0", Settings.ToHex(Color.FromArgb(0x4C, 0x9B, 0xE0)));

    [Fact]
    public void ToHexDropsAlpha()
        => Assert.Equal("#4C9BE0", Settings.ToHex(Color.FromArgb(0x80, 0x4C, 0x9B, 0xE0)));

    // ---------------------------------------------------------- GlyphColorFor

    [Fact]
    public void WithNoOverrideTheGlyphFollowsTheTaskbar()
    {
        var s = new Settings();

        Assert.Equal(Palette.InkOn(true), s.GlyphColorFor(Fixtures.DeviceId, darkTaskbar: true));
        Assert.Equal(Palette.InkOn(false), s.GlyphColorFor(Fixtures.DeviceId, darkTaskbar: false));
    }

    [Fact]
    public void ThemeModeFollowsTheTaskbarToo()
    {
        var s = new Settings();
        s.For(Fixtures.DeviceId).ColorMode = ColorMode.Theme;

        Assert.Equal(Palette.InkOn(true), s.GlyphColorFor(Fixtures.DeviceId, darkTaskbar: true));
        Assert.Equal(Palette.InkOn(false), s.GlyphColorFor(Fixtures.DeviceId, darkTaskbar: false));
    }

    [Fact]
    public void DeviceModeIgnoresTheTaskbarAndUsesTheEndpointAccent()
    {
        var s = new Settings();
        s.For(Fixtures.DeviceId).ColorMode = ColorMode.Device;

        var accent = Palette.AccentFor(Fixtures.DeviceId);
        Assert.Equal(accent, s.GlyphColorFor(Fixtures.DeviceId, darkTaskbar: true));
        Assert.Equal(accent, s.GlyphColorFor(Fixtures.DeviceId, darkTaskbar: false));
    }

    [Fact]
    public void CustomModeUsesTheStoredColour()
    {
        var s = new Settings();
        var ov = s.For(Fixtures.DeviceId);
        ov.ColorMode = ColorMode.Custom;
        ov.CustomColor = "#3FB07C";

        Assert.Equal(Color.FromArgb(255, 0x3F, 0xB0, 0x7C),
            s.GlyphColorFor(Fixtures.DeviceId, darkTaskbar: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a colour")]
    public void CustomModeWithAnUnusableColourFallsBackToTheThemeInk(string? stored)
    {
        // Custom is selectable before a colour has been picked, so this path is
        // reachable in normal use, not only from a hand-edited config.
        var s = new Settings();
        var ov = s.For(Fixtures.DeviceId);
        ov.ColorMode = ColorMode.Custom;
        ov.CustomColor = stored;

        Assert.Equal(Palette.InkOn(true), s.GlyphColorFor(Fixtures.DeviceId, darkTaskbar: true));
    }

    [Fact]
    public void GlyphColourSurvivesARoundTrip()
    {
        var s = new Settings();
        var ov = s.For(Fixtures.DeviceId);
        ov.ColorMode = ColorMode.Custom;
        ov.CustomColor = "#E0685C";

        Assert.Equal(Color.FromArgb(255, 0xE0, 0x68, 0x5C),
            RoundTrip(s).GlyphColorFor(Fixtures.DeviceId, darkTaskbar: false));
    }

    // ---------------------------------------------------------------- palette

    [Fact]
    public void AccentForIsStableAndAlwaysInRange()
    {
        var ids = new[]
        {
            Fixtures.DeviceId,
            "{0.0.0.00000000}.{11111111-1111-1111-1111-111111111111}",
            "{0.0.0.00000000}.{22222222-2222-2222-2222-222222222222}",
            ""
        };

        foreach (var id in ids)
        {
            var first = Palette.AccentFor(id);
            Assert.Equal(first, Palette.AccentFor(id));       // no per-run randomness
            Assert.Contains(first, Palette.Accents);
        }
    }

    [Fact]
    public void AnEmptyDeviceIdStillGetsAColour()
        => Assert.Equal(Palette.Accents[0], Palette.AccentFor(""));
}

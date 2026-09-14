using Infolume.Audio;
using Infolume.Config;

namespace Infolume.Icons;

/// <summary>
/// Decides which glyph an endpoint gets, four fallbacks deep. Every property read
/// is one Windows already publishes per endpoint.
///
/// One exception, and it is deliberately the narrowest possible: display glasses
/// cannot be told from a television by any property, so <see cref="DisplayGlyph"/>
/// looks at the name. It runs only where the answer was already going to be
/// <see cref="GlyphKind.Tv"/>, so it can never turn a correct answer into a wrong
/// one, only a generic answer into a specific one.
/// </summary>
public static class GlyphResolver
{
    // KSNODETYPE GUIDs. The first three are the documented well-known values;
    // the DisplayPort/HDMI one was observed on this machine's NVIDIA HDMI
    // endpoint, so it is treated as a hint rather than a documented constant.
    private static readonly Guid KsSpeaker = new("DFF21CE1-F70F-11D0-B917-00A0C9223196");
    private static readonly Guid KsHeadphones = new("DFF21CE2-F70F-11D0-B917-00A0C9223196");
    private static readonly Guid KsLineConnector = new("DFF21FE3-F70F-11D0-B917-00A0C9223196");
    private static readonly Guid KsDisplayObserved = new("D1B9CC2A-F519-417F-91C9-55FA65481001");

    /// <summary>
    /// mmres.dll resource IDs, from PKEY_Device_IconPath. Mapping these to our own
    /// glyphs keeps the icon set visually consistent; extracting the actual Windows
    /// artwork with SHDefExtractIcon is a settings option, not the default.
    /// </summary>
    private static readonly Dictionary<int, GlyphKind> MmresMap = new()
    {
        [3010] = GlyphKind.Speakers,
        [3011] = GlyphKind.Headphones,
        [3012] = GlyphKind.Line,
        [3013] = GlyphKind.Spdif,
        [3014] = GlyphKind.Headset,
        [3015] = GlyphKind.Headset,
        [3016] = GlyphKind.Network,
        [3017] = GlyphKind.Tv,
        [3018] = GlyphKind.Monitor
    };

    public static GlyphKind Resolve(EndpointInfo ep, Settings settings)
    {
        // 1. The user's own choice always wins.
        if (settings.Overrides.TryGetValue(ep.Id, out var ov) && ov.Glyph is { } chosen)
            return chosen;

        // 2. Jack subtype: the physical connector. Finest signal when present, but
        //    drivers populate it inconsistently - three of the four endpoints on
        //    this machine report the generic speaker GUID.
        if (ep.JackSubType != Guid.Empty)
        {
            if (ep.JackSubType == KsHeadphones)
                return ep.IsBluetooth ? GlyphKind.BluetoothHeadphones : GlyphKind.Headphones;
            if (ep.JackSubType == KsDisplayObserved) return DisplayGlyph(ep);
            // KsSpeaker and KsLineConnector are too generic to beat form factor,
            // so they fall through rather than short-circuiting here.
            if (ep.JackSubType != KsSpeaker && ep.JackSubType != KsLineConnector)
            {
                // An unrecognised but specific jack type: keep going.
            }
        }

        // 3. Form factor plus enumerator. This tier does the real work.
        var byFactor = FromFormFactor(ep);
        if (byFactor is { } ff) return ff;

        // 4. Whatever Windows itself would show.
        var byIcon = FromIconPath(ep.IconPath);
        if (byIcon is { } ic) return ic;

        return GlyphKind.Unknown;
    }

    private static GlyphKind? FromFormFactor(EndpointInfo ep) => ep.FormFactor switch
    {
        FormFactor.Headphones => ep.IsBluetooth ? GlyphKind.BluetoothHeadphones : GlyphKind.Headphones,
        FormFactor.Headset => GlyphKind.Headset,
        FormFactor.Handset => GlyphKind.Headset,
        FormFactor.DigitalAudioDisplayDevice => DisplayGlyph(ep),
        FormFactor.SPDIF => GlyphKind.Spdif,
        FormFactor.UnknownDigitalPassthrough => GlyphKind.Spdif,
        FormFactor.RemoteNetworkDevice => GlyphKind.Network,
        FormFactor.LineLevel => IsVirtual(ep) ? GlyphKind.Virtual : GlyphKind.Line,
        FormFactor.Speakers => ep.IsBluetooth ? GlyphKind.BluetoothHeadphones
                             : IsVirtual(ep) ? GlyphKind.Virtual
                             : GlyphKind.Speakers,
        _ => null
    };

    /// <summary>
    /// Display glasses (XREAL, Viture, Rokid and the rest) reach the machine over
    /// DisplayPort or HDMI, so Windows reports them exactly as it reports a
    /// television: <see cref="FormFactor.DigitalAudioDisplayDevice"/>, on the
    /// graphics adapter. No property distinguishes them, which leaves the name.
    ///
    /// Name matching is normally the wrong tool here - two different endpoints are
    /// often both called "Speakers" - but the risk is
    /// different in this one spot. This runs only where the answer was already
    /// going to be <see cref="GlyphKind.Tv"/>, so the worst a false positive can
    /// do is draw glasses on a television whose name contains one of these words,
    /// and the user can override it in two clicks. A false negative just leaves
    /// the television glyph that would have been used anyway.
    ///
    /// The list is brands rather than model names, because model names churn every
    /// year and the brand is what actually appears in the endpoint string.
    /// </summary>
    private static readonly string[] GlassesTells =
    [
        "xreal", "nreal",          // XREAL Air / One, and the pre-rename Nreal
        "viture",                  // VITURE One / Pro / Luma
        "rokid",                   // Rokid Max / Air
        "rayneo",                  // TCL RayNeo
        "inmo",
        "even realities",
        "glasses"                  // Lenovo Legion Glasses, and anything self-describing
    ];

    private static GlyphKind DisplayGlyph(EndpointInfo ep)
    {
        string hay = $"{ep.Description} {ep.Adapter}";
        return GlassesTells.Any(t => hay.Contains(t, StringComparison.OrdinalIgnoreCase))
            ? GlyphKind.Glasses
            : GlyphKind.Tv;
    }

    /// <summary>
    /// Software endpoints - virtual cables, streaming sinks, VR audio - enumerate
    /// under ROOT or SWD rather than a real bus. This box exposes eleven of them,
    /// so telling them apart from hardware matters more here than usual.
    /// </summary>
    private static bool IsVirtual(EndpointInfo ep)
    {
        if (ep.Enumerator.Equals("SWD", StringComparison.OrdinalIgnoreCase)) return true;
        if (!ep.Enumerator.Equals("ROOT", StringComparison.OrdinalIgnoreCase)) return false;

        // ROOT alone is not conclusive, so look for the usual giveaways in the name.
        string hay = $"{ep.Description} {ep.Adapter}";
        string[] tells = ["virtual", "cable", "vb-audio", "streaming", "relay", "loopback", "voicemeeter"];
        return tells.Any(t => hay.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    private static GlyphKind? FromIconPath(string iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath)) return null;
        int comma = iconPath.LastIndexOf(',');
        if (comma < 0 || comma == iconPath.Length - 1) return null;
        if (!int.TryParse(iconPath.AsSpan(comma + 1), out int id)) return null;
        return MmresMap.TryGetValue(Math.Abs(id), out var kind) ? kind : null;
    }
}

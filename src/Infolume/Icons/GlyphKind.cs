using System.Drawing;

namespace Infolume.Icons;

public enum GlyphKind
{
    Speakers,
    Headphones,
    BluetoothHeadphones,
    Headset,
    Earbuds,
    Glasses,
    Tv,
    Monitor,
    Line,
    Spdif,
    Network,
    Virtual,
    Unknown
}

/// <summary>
/// How strongly the icon asserts that it is an audio control, over and above the
/// device glyph. Largely redundant now the level indicator is a staircase, which
/// carries that meaning by itself, but it still earns its place with the quieter
/// indicators.
/// </summary>
public enum AudioCue
{
    /// <summary>Device glyph alone. On its own this can read as a display or network icon.</summary>
    None,

    /// <summary>Device glyph with sound waves radiating from it.</summary>
    Waves,

    /// <summary>Speaker as the primary mark, device type as a corner badge.</summary>
    SpeakerBadge
}

/// <summary>How the volume level is drawn on the icon.</summary>
public enum VolumeStyle
{
    /// <summary>A solid pill along the bottom edge.</summary>
    Bar,

    /// <summary>The bottom bar cut into discrete blocks.</summary>
    Segments,

    /// <summary>Vertical bars of stepped height - the classic level-meter shape.</summary>
    Equalizer,

    /// <summary>A vertical column up the right edge; the glyph shifts left.</summary>
    Column,

    /// <summary>An arc sweeping under the glyph, like a dial.</summary>
    Arc,

    /// <summary>A row of dots, filled up to the level.</summary>
    Pips,

    /// <summary>
    /// The standard ascending "signal strength" staircase, overlaid on the corner
    /// of the glyph rather than sitting beneath it, so the device art keeps its
    /// full size.
    /// </summary>
    Incline,

    /// <summary>The same staircase spanning the full width, overlaid low on the glyph.</summary>
    InclineWide
}

/// <summary>How the glyph is coloured. The level bar always uses the device accent.</summary>
public enum ColorMode
{
    /// <summary>Follow the taskbar: near-white on dark, near-black on light.</summary>
    Theme,
    /// <summary>The endpoint's own accent, so the device is identifiable by colour alone.</summary>
    Device,
    /// <summary>A fixed colour the user picked.</summary>
    Custom
}

public static class Palette
{
    /// <summary>
    /// Device accents. Chosen to stay legible on both a light and a dark taskbar -
    /// nothing here drops below roughly 3:1 against either ground.
    /// </summary>
    public static readonly Color[] Accents =
    [
        Color.FromArgb(0x4C, 0x9B, 0xE0),
        Color.FromArgb(0x3F, 0xB0, 0x7C),
        Color.FromArgb(0xC9, 0x8A, 0x3A),
        Color.FromArgb(0x9B, 0x85, 0xD8),
        Color.FromArgb(0xE0, 0x68, 0x5C),
        Color.FromArgb(0x3F, 0xAF, 0xB0),
        Color.FromArgb(0xB9, 0xC2, 0x4E),
        Color.FromArgb(0xE0, 0x8A, 0xB8)
    ];

    public static readonly Color Muted = Color.FromArgb(0xE5, 0x60, 0x4F);

    public static Color InkOn(bool darkTaskbar) =>
        darkTaskbar ? Color.FromArgb(0xF2, 0xF4, 0xF6) : Color.FromArgb(0x17, 0x1A, 0x1E);

    public static Color TrackOn(bool darkTaskbar) =>
        darkTaskbar ? Color.FromArgb(0x48, 0x50, 0x5A) : Color.FromArgb(0xC3, 0xCA, 0xD2);

    /// <summary>
    /// Stable accent for an endpoint. Derived from the device ID so the same
    /// device keeps the same colour across restarts without storing anything.
    /// </summary>
    public static Color AccentFor(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return Accents[0];
        uint h = 2166136261u;                       // FNV-1a
        foreach (char c in deviceId)
        {
            h ^= c;
            h *= 16777619u;
        }
        return Accents[(int)(h % (uint)Accents.Length)];
    }
}

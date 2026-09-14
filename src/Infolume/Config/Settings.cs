using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;
using Infolume.Icons;

namespace Infolume.Config;

/// <summary>
/// Per-endpoint appearance, keyed on the MMDevice ID. Two endpoints on this
/// machine are both called "Speakers", so nothing here may key on the name.
/// </summary>
public sealed class DeviceOverride
{
    /// <summary>
    /// A local rename. Cosmetic only - Infolume shows it, the rest of Windows does
    /// not. Renaming the endpoint system-wide would mean writing
    /// PKEY_Device_FriendlyName, which needs either elevation or the undocumented
    /// IPolicyConfig property setter, and would be wiped anyway whenever a virtual
    /// device's host app (Steam, AudioRelay) recreates its endpoints.
    /// </summary>
    public string? Alias { get; set; }

    public GlyphKind? Glyph { get; set; }
    public ColorMode ColorMode { get; set; } = ColorMode.Theme;

    /// <summary>Hex colour, "#RRGGBB", used only when <see cref="ColorMode"/> is Custom.</summary>
    public string? CustomColor { get; set; }

    /// <summary>Hidden from the flyout's device list. Never hides the active device.</summary>
    public bool Hidden { get; set; }
}

/// <summary>Whether the icon paints for a light or dark taskbar.</summary>
public enum ThemeMode
{
    /// <summary>
    /// Follow SystemUsesLightTheme. Correct on stock Windows, but StartAllBack
    /// paints its taskbar from its own settings, so auto can disagree with what is
    /// actually behind the icon - which is why the two manual modes exist.
    /// </summary>
    Auto,
    ForceLightTaskbar,
    ForceDarkTaskbar
}

public sealed class Settings
{
    public ThemeMode Theme { get; set; } = ThemeMode.Auto;

    /// <summary>
    /// Whether scrolling over the tray icon changes the volume.
    ///
    /// This is the switch, not a filter. Off means <see cref="Native.TrayWheelRawInput"/>
    /// is never constructed, so the application makes no raw-input registration
    /// and observes no mouse events at all - which is the honest answer to
    /// "I would rather this thing did not watch my mouse" and a better one than
    /// any amount of documentation.
    /// </summary>
    public bool ScrollToAdjust { get; set; } = true;

    /// <summary>Scroll wheel over the tray icon adjusts volume by this many points.</summary>
    public int ScrollStep { get; set; } = 2;

    /// <summary>
    /// Tray icon edge length in pixels, or 0 to derive it from SM_CXSMICON.
    /// An override exists because a replaced taskbar sizes its tray slots from its
    /// own settings (StartAllBack's FatTaskbar and SysTraySpacierIcons), which no
    /// API reports - and handing the shell an icon it has to rescale is what makes
    /// the glyph look soft or squashed.
    /// </summary>
    public int IconSize { get; set; }

    /// <summary>
    /// How hard the icon asserts that it is an audio control. Defaults to Waves:
    /// a bare device outline reads as a display or network icon, which is exactly
    /// the confusion this app exists to remove.
    /// </summary>
    /// <summary>
    /// Extra "this is audio" cue on the glyph. Defaults to None because the
    /// incline staircase already carries that meaning; it only earns its place
    /// with the quieter indicators.
    /// </summary>
    public AudioCue Cue { get; set; } = AudioCue.None;

    /// <summary>How the level is drawn. The staircase reads as volume on its own.</summary>
    public VolumeStyle Style { get; set; } = VolumeStyle.InclineWide;

    /// <summary>
    /// Steps in the staircase. Six is the finest that still resolves at a 32px
    /// tray icon - each bar lands at ~3.7px, where seven is 3.0 and eight aliases.
    /// </summary>
    public int InclineBars { get; set; } = 6;

    /// <summary>Whether the per-app mixer section is folded away in the flyout.</summary>
    public bool AppsCollapsed { get; set; }

    public Dictionary<string, DeviceOverride> Overrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------ resolution

    public DeviceOverride For(string deviceId)
    {
        if (!Overrides.TryGetValue(deviceId, out var ov))
        {
            ov = new DeviceOverride();
            Overrides[deviceId] = ov;
        }
        return ov;
    }

    /// <summary>
    /// What to call this endpoint: the user's alias if set, otherwise the name
    /// Windows reports. The adapter line beside it is never substituted, so an
    /// alias can rename a device without hiding which physical thing it is.
    /// </summary>
    public string NameFor(string deviceId, string fallback)
    {
        if (Overrides.TryGetValue(deviceId, out var ov)
            && !string.IsNullOrWhiteSpace(ov.Alias))
            return ov.Alias!.Trim();
        return fallback;
    }

    /// <summary>The colour the glyph paints in, given the endpoint and taskbar theme.</summary>
    public Color GlyphColorFor(string deviceId, bool darkTaskbar)
    {
        if (!Overrides.TryGetValue(deviceId, out var ov)) return Palette.InkOn(darkTaskbar);

        return ov.ColorMode switch
        {
            ColorMode.Device => Palette.AccentFor(deviceId),
            ColorMode.Custom => ParseHex(ov.CustomColor) ?? Palette.InkOn(darkTaskbar),
            _ => Palette.InkOn(darkTaskbar)
        };
    }

    public static Color? ParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var s = hex.TrimStart('#');
        if (s.Length != 6 || !int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out int v))
            return null;
        return Color.FromArgb(255, (v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // ------------------------------------------------------------ persistence

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Infolume", "settings.json");

    public static Settings Load() => LoadFrom(Path);

    public void Save() => SaveTo(Path);

    /// <summary>
    /// The path-taking form, so the round-trip can be exercised against a temp file
    /// instead of the user's real config.
    /// </summary>
    internal static Settings LoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return new Settings();
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<Settings>(json, JsonOptions) ?? new Settings();

            // System.Text.Json builds a fresh Dictionary with the DEFAULT comparer,
            // so the OrdinalIgnoreCase intent above is lost across a load: a device
            // ID whose casing differs from the one that was saved would silently
            // stop matching its own override.
            loaded.Overrides = loaded.Overrides is null
                ? new Dictionary<string, DeviceOverride>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, DeviceOverride>(loaded.Overrides, StringComparer.OrdinalIgnoreCase);
            return loaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException
                                      or ArgumentException or NotSupportedException)
        {
            // A corrupt or unreadable config must never stop the app starting -
            // it would leave the user with no tray icon and no way to fix it.
            return new Settings();
        }
    }

    internal void SaveTo(string path)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
            Diagnostics.Log.Write("settings", $"saved to {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a preference is not worth crashing over.
            Diagnostics.Log.Error("settings", ex);
        }
    }
}

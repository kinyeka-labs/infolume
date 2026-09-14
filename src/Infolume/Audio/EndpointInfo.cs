namespace Infolume.Audio;

/// <summary>
/// EndpointFormFactor, as reported by PKEY_AudioEndpoint_FormFactor.
/// </summary>
public enum FormFactor
{
    RemoteNetworkDevice = 0,
    Speakers = 1,
    LineLevel = 2,
    Headphones = 3,
    Microphone = 4,
    Headset = 5,
    Handset = 6,
    UnknownDigitalPassthrough = 7,
    SPDIF = 8,
    DigitalAudioDisplayDevice = 9,
    UnknownFormFactor = 10
}

/// <summary>
/// One render endpoint plus the metadata the glyph resolver needs.
/// </summary>
public sealed record EndpointInfo
{
    /// <summary>MMDevice ID. Stable across reboots; the key for user overrides.</summary>
    public required string Id { get; init; }

    /// <summary>PKEY_Device_DeviceDesc, e.g. "Speakers".</summary>
    public required string Description { get; init; }

    /// <summary>
    /// PKEY_DeviceInterface_FriendlyName, e.g. "Steam Streaming Speakers".
    /// Not decoration: two endpoints on this machine both describe themselves as
    /// "Speakers", and the adapter is the only thing telling them apart.
    /// </summary>
    public required string Adapter { get; init; }

    public FormFactor FormFactor { get; init; } = FormFactor.UnknownFormFactor;

    /// <summary>PKEY_Device_EnumeratorName - "BTHENUM" and friends mean Bluetooth.</summary>
    public string Enumerator { get; init; } = "";

    /// <summary>PKEY_AudioEndpoint_JackSubType, a KSNODETYPE GUID, when the driver sets one.</summary>
    public Guid JackSubType { get; init; }

    /// <summary>PKEY_Device_IconPath, e.g. "%windir%\\system32\\mmres.dll,-3010".</summary>
    public string IconPath { get; init; } = "";

    public float Volume { get; init; }
    public bool Muted { get; init; }
    public bool IsDefault { get; init; }

    public bool IsBluetooth =>
        Enumerator.StartsWith("BTH", StringComparison.OrdinalIgnoreCase);

    /// <summary>What the flyout shows on the second line of a device row.</summary>
    public string DisplayName => Description;
}

using Infolume.Audio;

namespace Infolume.Tests;

/// <summary>
/// Endpoint builder. Every field defaults to "Windows told us nothing", so each
/// test states only the property it is actually about.
/// </summary>
internal static class Fixtures
{
    /// <summary>The three documented KSNODETYPE values the resolver knows.</summary>
    internal static readonly Guid KsSpeaker = new("DFF21CE1-F70F-11D0-B917-00A0C9223196");
    internal static readonly Guid KsHeadphones = new("DFF21CE2-F70F-11D0-B917-00A0C9223196");
    internal static readonly Guid KsLineConnector = new("DFF21FE3-F70F-11D0-B917-00A0C9223196");

    /// <summary>The NVIDIA HDMI jack GUID observed on the dev machine.</summary>
    internal static readonly Guid KsDisplayObserved = new("D1B9CC2A-F519-417F-91C9-55FA65481001");

    /// <summary>A well-formed GUID the resolver has never heard of.</summary>
    internal static readonly Guid KsUnknown = new("11111111-2222-3333-4444-555555555555");

    internal const string DeviceId = "{0.0.0.00000000}.{b3f8e2a1-0000-4000-8000-abcdefabcdef}";

    internal static EndpointInfo Ep(
        string id = DeviceId,
        string description = "Speakers",
        string adapter = "Realtek High Definition Audio",
        FormFactor formFactor = FormFactor.UnknownFormFactor,
        string enumerator = "",
        Guid jackSubType = default,
        string iconPath = "",
        float volume = 0.5f,
        bool muted = false) => new()
        {
            Id = id,
            Description = description,
            Adapter = adapter,
            FormFactor = formFactor,
            Enumerator = enumerator,
            JackSubType = jackSubType,
            IconPath = iconPath,
            Volume = volume,
            Muted = muted
        };
}

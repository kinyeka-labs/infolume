using System.Runtime.InteropServices;
using Infolume.Native;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Infolume.Audio;

/// <summary>
/// Owns the CoreAudio session: enumerates render endpoints, tracks the default
/// one's volume and mute, and raises <see cref="Changed"/> when anything moves.
///
/// Everything here is push-based - endpoint notifications for device changes and
/// IAudioEndpointVolumeCallback for volume. There is no polling timer, so the
/// icon updates the instant the hardware volume keys are pressed.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    // PKEYs the glyph resolver reads. NAudio exposes a few of these as
    // properties, but not all, so they are declared here in one place.
    private static readonly PropertyKey PKEY_Device_DeviceDesc =
        Key("a45c254e-df1c-4efd-8020-67d146a850e0", 2);
    private static readonly PropertyKey PKEY_DeviceInterface_FriendlyName =
        Key("b3f8fa53-0004-438e-9003-51a46e139bfc", 6);
    private static readonly PropertyKey PKEY_Device_EnumeratorName =
        Key("a45c254e-df1c-4efd-8020-67d146a850e0", 24);
    private static readonly PropertyKey PKEY_Device_IconPath =
        Key("259abffc-50a7-47ce-af08-68c9a7d73366", 12);
    private static readonly PropertyKey PKEY_AudioEndpoint_FormFactor =
        Key("1da5d803-d492-4edd-8c23-e0c0ffee7f0e", 0);
    private static readonly PropertyKey PKEY_AudioEndpoint_JackSubType =
        Key("1da5d803-d492-4edd-8c23-e0c0ffee7f0e", 8);

    private static PropertyKey Key(string guid, int pid) => new(new Guid(guid), pid);

    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly MMDeviceNotificationClient _notifications;
    private readonly object _gate = new();

    private MMDevice? _default;
    private bool _disposed;

    /// <summary>
    /// Raised on any volume, mute, or device change.
    ///
    /// Device notifications arrive on the synchronization context captured at
    /// construction (build this on the UI thread), but volume and mute callbacks
    /// come straight off a Windows audio worker thread. Handlers must therefore
    /// assume ANY thread and marshal before touching UI - TrayApp.OnEngineChanged
    /// is the one place that does so.
    /// </summary>
    public event Action? Changed;

    public AudioEngine()
    {
        _notifications = _enumerator.CreateNotificationClient(useSynchronizationContext: true);
        _notifications.DefaultDeviceChanged += OnDefaultDeviceChanged;
        _notifications.DeviceAdded += OnDeviceListChanged;
        _notifications.DeviceRemoved += OnDeviceListChanged;
        _notifications.DeviceStateChanged += OnDeviceListChanged;
        AttachToDefault();
    }

#if DEBUG
    private IReadOnlyList<EndpointInfo>? _mockEndpoints;
    private IReadOnlyList<SessionInfo>? _mockSessions;

    /// <summary>
    /// Dev only: report a fixed set of devices and sessions instead of the real
    /// ones, so the panel can be rendered against plausible hardware for
    /// documentation. The machine that builds this has four endpoints, three of
    /// them virtual, which is not what anyone else's audio setup looks like.
    ///
    /// Deliberately data-only. The mock supplies form factor and enumerator and
    /// lets <see cref="Icons.GlyphResolver"/> choose the glyph exactly as it
    /// would for real hardware, so the picture cannot show an arrangement the
    /// shipping code would not produce.
    ///
    /// Excluded from Release along with the rest of the render modes.
    /// </summary>
    internal void UseMockData(IReadOnlyList<EndpointInfo> endpoints, IReadOnlyList<SessionInfo> sessions)
    {
        _mockEndpoints = endpoints;
        _mockSessions = sessions;
        Changed?.Invoke();
    }
#endif

    // ---------------------------------------------------------------- state

    /// <summary>The current default render endpoint, or null if there is none.</summary>
    public EndpointInfo? Current
    {
        get
        {
#if DEBUG
            if (_mockEndpoints is { } mock)
                return mock.FirstOrDefault(e => e.IsDefault) ?? mock.FirstOrDefault();
#endif
            lock (_gate)
            {
                if (_default is null) return null;
                try { return Describe(_default, isDefault: true); }
                catch (COMException) { return null; }
            }
        }
    }

    /// <summary>Every active render endpoint, default first.</summary>
    public IReadOnlyList<EndpointInfo> Endpoints()
    {
#if DEBUG
        if (_mockEndpoints is { } mock) return mock;
#endif
        string defaultId = "";
        lock (_gate) { try { defaultId = _default?.ID ?? ""; } catch (COMException) { } }

        var list = new List<EndpointInfo>();
        try
        {
            foreach (var d in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                try { list.Add(Describe(d, d.ID == defaultId)); }
                catch (COMException) { /* endpoint vanished mid-enumeration */ }
            }
        }
        catch (COMException) { }

        return list.OrderByDescending(e => e.IsDefault)
                   .ThenBy(e => e.Description, StringComparer.CurrentCultureIgnoreCase)
                   .ToList();
    }

    /// <summary>
    /// Apps currently playing on the default endpoint. Only active sessions, so
    /// the list stays short; the system-sounds session has no process and is
    /// labelled rather than skipped.
    /// </summary>
    public IReadOnlyList<SessionInfo> Sessions()
    {
#if DEBUG
        if (_mockSessions is { } mock) return mock;
#endif
        var list = new List<SessionInfo>();
        lock (_gate)
        {
            if (_default is null) return list;
            try
            {
                var mgr = _default.AudioSessionManager;
                mgr.RefreshSessions();
                var sessions = mgr.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    try
                    {
                        var s = sessions[i];
                        if (s.State == AudioSessionState.AudioSessionStateExpired) continue;

                        uint pid = s.GetProcessID;
                        bool system = s.IsSystemSoundsSession;
                        string name = system
                            ? "System sounds"
                            : AppIcons.FriendlyName(pid, string.IsNullOrWhiteSpace(s.DisplayName) ? "Unknown app" : s.DisplayName);

                        list.Add(new SessionInfo
                        {
                            Id = s.GetSessionInstanceIdentifier ?? $"pid:{pid}",
                            Name = name,
                            Volume = s.SimpleAudioVolume.Volume,
                            Muted = s.SimpleAudioVolume.Mute,
                            IsSystemSounds = system,
                            Icon = system ? null : AppIcons.ForProcess(pid)
                        });
                    }
                    catch (COMException) { /* session died mid-enumeration */ }
                }
            }
            catch (COMException) { }
        }
        return list;
    }

    /// <summary>Sets one app session's volume, matched on its instance identifier.</summary>
    public void SetSessionVolume(string sessionId, float scalar) =>
        WithSession(sessionId, s => s.SimpleAudioVolume.Volume = Math.Clamp(scalar, 0f, 1f));

    public void ToggleSessionMute(string sessionId) =>
        WithSession(sessionId, s => s.SimpleAudioVolume.Mute = !s.SimpleAudioVolume.Mute);

    private void WithSession(string sessionId, Action<AudioSessionControl> act)
    {
        lock (_gate)
        {
            if (_default is null) return;
            try
            {
                var sessions = _default.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var s = sessions[i];
                    try
                    {
                        if (s.GetSessionInstanceIdentifier == sessionId) { act(s); return; }
                    }
                    catch (COMException) { }
                }
            }
            catch (COMException) { }
        }
        Changed?.Invoke();
    }

    // ------------------------------------------------------------- mutation

    public void SetVolume(float scalar)
    {
        lock (_gate)
        {
            if (_default is null) return;
            try { _default.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(scalar, 0f, 1f); }
            catch (COMException) { }
        }
    }

    public void ToggleMute()
    {
        lock (_gate)
        {
            if (_default is null) return;
            try { _default.AudioEndpointVolume.Mute = !_default.AudioEndpointVolume.Mute; }
            catch (COMException) { }
        }
    }

    /// <summary>Switches the default endpoint for all three roles.</summary>
    public bool SetDefault(string deviceId) => DefaultDevice.Set(deviceId);

    // -------------------------------------------------------------- reading

    private EndpointInfo Describe(MMDevice d, bool isDefault)
    {
        var props = d.Properties;

        string desc = ReadString(props, PKEY_Device_DeviceDesc);
        string adapter = ReadString(props, PKEY_DeviceInterface_FriendlyName);

        // FriendlyName is "Desc (Adapter)"; fall back to splitting it if either
        // individual property is missing.
        if (string.IsNullOrWhiteSpace(desc))
        {
            string friendly = SafeName(d);
            int paren = friendly.LastIndexOf(" (", StringComparison.Ordinal);
            desc = paren > 0 ? friendly[..paren] : friendly;
        }

        float vol = 0f;
        bool mute = false;
        try
        {
            vol = d.AudioEndpointVolume.MasterVolumeLevelScalar;
            mute = d.AudioEndpointVolume.Mute;
        }
        catch (COMException) { }

        return new EndpointInfo
        {
            Id = d.ID,
            Description = string.IsNullOrWhiteSpace(desc) ? "Unknown device" : desc,
            Adapter = adapter,
            FormFactor = (FormFactor)ReadInt(props, PKEY_AudioEndpoint_FormFactor, (int)FormFactor.UnknownFormFactor),
            Enumerator = ReadString(props, PKEY_Device_EnumeratorName),
            JackSubType = ReadGuid(props, PKEY_AudioEndpoint_JackSubType),
            IconPath = ReadString(props, PKEY_Device_IconPath),
            Volume = vol,
            Muted = mute,
            IsDefault = isDefault
        };
    }

    private static string SafeName(MMDevice d)
    {
        try { return d.FriendlyName; } catch (COMException) { return ""; }
    }

    private static string ReadString(PropertyStore store, PropertyKey key)
    {
        try
        {
            if (!store.Contains(key)) return "";
            return store[key].Value as string ?? "";
        }
        catch (COMException) { return ""; }
        catch (InvalidCastException) { return ""; }
    }

    private static int ReadInt(PropertyStore store, PropertyKey key, int fallback)
    {
        try
        {
            if (!store.Contains(key)) return fallback;
            return Convert.ToInt32(store[key].Value);
        }
        catch (COMException) { return fallback; }
        catch (InvalidCastException) { return fallback; }
        catch (FormatException) { return fallback; }
    }

    private static Guid ReadGuid(PropertyStore store, PropertyKey key)
    {
        try
        {
            if (!store.Contains(key)) return Guid.Empty;
            object v = store[key].Value;
            return v switch
            {
                Guid g => g,
                string s when Guid.TryParse(s, out var parsed) => parsed,
                _ => Guid.Empty
            };
        }
        catch (COMException) { return Guid.Empty; }
        catch (InvalidCastException) { return Guid.Empty; }
    }

    // ------------------------------------------------------ default tracking

    private void AttachToDefault()
    {
        lock (_gate)
        {
            DetachLocked();
            try
            {
                _default = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _default.AudioEndpointVolume.OnVolumeNotification += OnVolume;
            }
            catch (COMException)
            {
                _default = null;   // no active render endpoint at all
            }
        }
    }

    private void DetachLocked()
    {
        if (_default is null) return;
        try { _default.AudioEndpointVolume.OnVolumeNotification -= OnVolume; }
        catch (COMException) { }
        try { _default.Dispose(); }
        catch (COMException) { }
        _default = null;
    }

    private void OnVolume(AudioVolumeNotificationData data) => Changed?.Invoke();

    // ------------------------------------------------ endpoint notifications

    private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
    {
        if (e.Flow != DataFlow.Render) return;
        AttachToDefault();
        Changed?.Invoke();
    }

    private void OnDeviceListChanged(object? sender, EventArgs e)
    {
        // A device appearing or disappearing can also change which endpoint is
        // default (unplugging headphones, say), so re-resolve rather than assume.
        AttachToDefault();
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- teardown

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _notifications.DefaultDeviceChanged -= OnDefaultDeviceChanged;
        _notifications.DeviceAdded -= OnDeviceListChanged;
        _notifications.DeviceRemoved -= OnDeviceListChanged;
        _notifications.DeviceStateChanged -= OnDeviceListChanged;
        _notifications.Dispose();

        lock (_gate) { DetachLocked(); }
        _enumerator.Dispose();
    }
}

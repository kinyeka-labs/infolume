using System.Runtime.InteropServices;

namespace Infolume.Native;

/// <summary>
/// Undocumented shell interface for changing the default audio endpoint. There is
/// no public API for this - every audio switcher on Windows uses IPolicyConfig,
/// and its layout has been stable since Windows 7.
/// </summary>
[ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    // Only SetDefaultEndpoint is called; the rest exist to keep the vtable
    // offsets correct and are never invoked.
    int GetMixFormat(string deviceId, IntPtr format);
    int GetDeviceFormat(string deviceId, bool defaultDevice, IntPtr format);
    int ResetDeviceFormat(string deviceId);
    int SetDeviceFormat(string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
    int GetProcessingPeriod(string deviceId, bool defaultDevice, IntPtr defaultPeriod, IntPtr minimumPeriod);
    int SetProcessingPeriod(string deviceId, IntPtr period);
    int GetShareMode(string deviceId, IntPtr mode);
    int SetShareMode(string deviceId, IntPtr mode);
    int GetPropertyValue(string deviceId, bool isFxStore, IntPtr key, IntPtr value);
    int SetPropertyValue(string deviceId, bool isFxStore, IntPtr key, IntPtr value);
    int SetDefaultEndpoint(string deviceId, ERole role);
    int SetEndpointVisibility(string deviceId, bool isVisible);
}

internal enum ERole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2
}

[ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
internal class CPolicyConfigClient { }

internal static class DefaultDevice
{
    /// <summary>
    /// Points every role at <paramref name="deviceId"/>. Windows tracks Console,
    /// Multimedia and Communications separately; setting only one leaves apps
    /// split across two devices, which reads as the switch having silently failed.
    /// </summary>
    internal static bool Set(string deviceId)
    {
        object? raw = null;
        try
        {
            raw = new CPolicyConfigClient();
            var cfg = (IPolicyConfig)raw;
            foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
            {
                int hr = cfg.SetDefaultEndpoint(deviceId, role);
                if (hr != 0)
                {
                    Diagnostics.Log.Write("policyconfig", $"SetDefaultEndpoint({role}) failed hr=0x{hr:X8}");
                    return false;
                }
            }
            Diagnostics.Log.Write("policyconfig", $"default endpoint set to {deviceId}");
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or NotSupportedException)
        {
            Diagnostics.Log.Error("policyconfig", ex);
            return false;
        }
        finally
        {
            if (raw is not null) Marshal.ReleaseComObject(raw);
        }
    }
}

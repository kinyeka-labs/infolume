using Microsoft.Win32;

namespace Infolume.Config;

/// <summary>
/// Launch at sign-in, via the HKCU Run key.
///
/// Chosen over a Startup-folder shortcut or a scheduled task because it needs no
/// elevation and it is the entry Task Manager's Startup tab lists, so it can be
/// turned off from where people go looking for it.
/// </summary>
internal static class AutoStart
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Infolume";

    internal static string ExecutablePath =>
        Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath;

    internal static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s
                && s.Contains("Infolume", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static void Set(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return;
            if (on) key.SetValue(ValueName, $"\"{ExecutablePath}\"");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            Diagnostics.Log.Write("autostart", on ? $"enabled: {ExecutablePath}" : "disabled");
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            Diagnostics.Log.Error("autostart", ex);
        }
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Infolume.Audio;

/// <summary>One app playing on the current default endpoint.</summary>
public sealed class SessionInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required float Volume { get; init; }
    public required bool Muted { get; init; }
    public bool IsSystemSounds { get; init; }

    /// <summary>The app's own icon, extracted from its executable. Shared, never disposed by callers.</summary>
    public Icon? Icon { get; init; }
}

/// <summary>
/// Pulls app icons out of process executables.
///
/// AudioSessionControl.IconPath exists but is empty for almost every desktop app,
/// so the executable is the real source. Results are cached: SHGetFileInfo hits
/// disk, and the flyout rebuilds its list on every open.
/// </summary>
internal static class AppIcons
{
    private static readonly ConcurrentDictionary<string, Icon?> Cache = new(StringComparer.OrdinalIgnoreCase);

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    internal static Icon? ForProcess(uint pid)
    {
        if (pid == 0) return null;
        string? exe = ExecutablePath(pid);
        if (exe is null) return null;

        return Cache.GetOrAdd(exe, static path =>
        {
            var info = new SHFILEINFO();
            IntPtr res = SHGetFileInfo(path, 0, ref info,
                (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_SMALLICON);
            if (res == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
            try
            {
                using var tmp = Icon.FromHandle(info.hIcon);
                return (Icon)tmp.Clone();
            }
            finally
            {
                Native.NativeMethods.DestroyIcon(info.hIcon);
            }
        });
    }

    /// <summary>
    /// MainModule throws for elevated or protected processes; Process.MainModule
    /// on a 64-bit host reading a different session is the usual failure. Treat a
    /// miss as "no icon" rather than an error - the row still renders.
    /// </summary>
    internal static string? ExecutablePath(uint pid)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is ArgumentException
                                      or InvalidOperationException
                                      or System.ComponentModel.Win32Exception
                                      or NotSupportedException)
        {
            return null;
        }
    }

    internal static string FriendlyName(uint pid, string fallback)
    {
        string? exe = ExecutablePath(pid);
        if (exe is null) return fallback;
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(exe);
            if (!string.IsNullOrWhiteSpace(vi.FileDescription)) return vi.FileDescription!;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return Path.GetFileNameWithoutExtension(exe);
    }
}

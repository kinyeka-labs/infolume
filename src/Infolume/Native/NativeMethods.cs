using System.Drawing;
using System.Runtime.InteropServices;

namespace Infolume.Native;

internal static class NativeMethods
{
    // ---- icon lifetime -----------------------------------------------------
    // Every Icon.FromHandle needs a matching DestroyIcon, or the app leaks a GDI
    // handle per repaint - and the icon repaints on every volume tick.
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    // ---- tray geometry -----------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
        public readonly bool IsEmpty => Right <= Left || Bottom <= Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NOTIFYICONIDENTIFIER
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    // Documented against the modern Windows 11 tray. On a replaced taskbar
    // (StartAllBack legacy mode) this can return E_FAIL or a zero rect, so the
    // caller MUST check the HRESULT and fall back to the cursor.
    [DllImport("shell32.dll", SetLastError = false)]
    internal static extern int Shell_NotifyIconGetRect(
        ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    // ---- metrics -----------------------------------------------------------
    internal const int SM_CXSMICON = 49;

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int nIndex);

    internal const int MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromPoint(POINT pt, int dwFlags);

    [DllImport("shcore.dll")]
    internal static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X, Y;
        public POINT(int x, int y) { X = x; Y = y; }
    }

    // ---- explorer restart --------------------------------------------------
    // StartAllBack restarts explorer on settings changes more often than stock
    // Windows does; without re-adding the icon it silently disappears.
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>Effective DPI of the monitor containing <paramref name="pt"/>.</summary>
    internal static uint DpiForPoint(Point pt)
    {
        try
        {
            var mon = MonitorFromPoint(new POINT(pt.X, pt.Y), MONITOR_DEFAULTTONEAREST);
            if (mon != IntPtr.Zero && GetDpiForMonitor(mon, 0 /* MDT_EFFECTIVE_DPI */, out var x, out _) == 0)
                return x;
        }
        catch (DllNotFoundException) { /* shcore missing: fall through */ }
        return 96;
    }

    /// <summary>
    /// Tray icon edge length in physical pixels for the monitor at <paramref name="pt"/>.
    /// Never hardcode this: StartAllBack's FatTaskbar and SysTraySpacierIcons move
    /// tray metrics independently of the Windows scaling setting.
    /// </summary>
    internal static int TrayIconSize(Point pt)
    {
        uint dpi = DpiForPoint(pt);
        int size;
        try { size = GetSystemMetricsForDpi(SM_CXSMICON, dpi); }
        catch (EntryPointNotFoundException) { size = GetSystemMetrics(SM_CXSMICON); }
        if (size <= 0) size = (int)Math.Round(16 * dpi / 96.0);
        return Math.Clamp(size, 16, 96);
    }
}

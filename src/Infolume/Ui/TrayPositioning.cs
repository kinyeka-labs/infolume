using System.Drawing;
using System.Windows.Forms;
using Infolume.Native;

namespace Infolume.Ui;

/// <summary>
/// Works out where the flyout should sit.
///
/// This is the part most likely to break on a replaced taskbar.
/// <c>Shell_NotifyIconGetRect</c> is documented against the modern Windows 11
/// tray; under StartAllBack's legacy taskbar it may return E_FAIL or a zero rect.
/// So: try it, verify the result, and fall back to the cursor. Nothing here ever
/// assumes the taskbar is at the bottom of the screen or how tall it is.
/// </summary>
internal static class TrayPositioning
{
    /// <summary>
    /// Screen rect of the tray icon, or null if the shell will not tell us -
    /// which is a normal outcome, not an error.
    /// </summary>
    internal static Rectangle? IconRect(NotifyIcon notifyIcon)
    {
        try
        {
            // NotifyIcon keeps its window handle and id private; both are needed to
            // identify the icon to the shell.
            var t = typeof(NotifyIcon);
            var windowField = t.GetField("window",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var idField = t.GetField("id",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (windowField?.GetValue(notifyIcon) is not NativeWindow win || idField is null)
                return null;

            var id = new NativeMethods.NOTIFYICONIDENTIFIER
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.NOTIFYICONIDENTIFIER>(),
                hWnd = win.Handle,
                uID = Convert.ToUInt32(idField.GetValue(notifyIcon) ?? 0)
            };

            if (NativeMethods.Shell_NotifyIconGetRect(ref id, out var rect) != 0) return null;
            if (rect.IsEmpty) return null;

            var r = rect.ToRectangle();
            // A rect the shell reports but that lies on no monitor is not usable.
            return Screen.AllScreens.Any(s => s.Bounds.IntersectsWith(r)) ? r : null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException
                                      or EntryPointNotFoundException
                                      or DllNotFoundException
                                      or MissingFieldException
                                      or InvalidCastException)
        {
            return null;
        }
    }

    /// <summary>
    /// The screen the flyout will appear on. Resolved before layout so the panel
    /// is measured against the DPI and work area of the monitor it will land on,
    /// not whichever one it was last shown on.
    /// </summary>
    internal static Screen AnchorScreen(Rectangle? iconRect) =>
        Screen.FromPoint(iconRect is { } r ? Center(r) : Cursor.Position);

    /// <summary>Effective DPI of the monitor the flyout will appear on.</summary>
    internal static uint AnchorDpi(Rectangle? iconRect) =>
        NativeMethods.DpiForPoint(iconRect is { } r ? Center(r) : Cursor.Position);

    /// <summary>
    /// Top-left corner for a flyout of <paramref name="size"/>, anchored to the
    /// tray icon when the shell reports it and to the cursor otherwise. The result
    /// is always clamped inside the working area of the monitor it lands on, so a
    /// top, left, or right taskbar works without any special case.
    /// </summary>
    internal static Point Place(Rectangle? icon, Size size, int margin = 8)
    {
        Point anchor = icon?.Location ?? Cursor.Position;
        Screen screen = Screen.FromPoint(icon is { } r ? Center(r) : anchor);
        Rectangle work = screen.WorkingArea;

        int x, y;

        if (icon is { } ir)
        {
            // Centre on the icon horizontally, then push to whichever side of it
            // has room - that is what makes a top or side taskbar work.
            x = Center(ir).X - size.Width / 2;
            y = ir.Top - size.Height - margin;
            if (y < work.Top) y = ir.Bottom + margin;
        }
        else
        {
            x = anchor.X - size.Width / 2;
            y = anchor.Y - size.Height - margin;
            if (y < work.Top) y = anchor.Y + margin;
        }

        x = Math.Clamp(x, work.Left + margin, Math.Max(work.Left + margin, work.Right - size.Width - margin));
        y = Math.Clamp(y, work.Top + margin, Math.Max(work.Top + margin, work.Bottom - size.Height - margin));
        return new Point(x, y);
    }

    private static Point Center(Rectangle r) => new(r.Left + r.Width / 2, r.Top + r.Height / 2);
}

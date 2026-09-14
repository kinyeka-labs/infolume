using System.Drawing;
using System.Runtime.InteropServices;

namespace Infolume.Native;

/// <summary>
/// Scroll-over-the-icon to change volume.
///
/// NotifyIcon raises no wheel event - the shell never forwards WM_MOUSEWHEEL to
/// the window behind the icon - so the wheel has to be observed some other way.
/// This class does that with <b>Raw Input</b>: it registers for mouse input with
/// <c>RIDEV_INPUTSINK</c>, reads the wheel delta out of the <c>WM_INPUT</c>
/// messages that result, and posts a notch to the UI thread when the cursor is
/// inside the icon's own rect.
///
/// <b>Why raw input rather than a low-level mouse hook.</b> This used to be a
/// <c>WH_MOUSE_LL</c> hook, and the reason it is not any more is worth stating
/// plainly, because the hook worked.
///
/// A hook is the only way to <i>block</i> an input event. It is not the only way
/// to <i>see</i> one. The hook's callback returned 1 for a claimed wheel notch,
/// commented "the taskbar must not also act on this" - and that turned out to be
/// an assumption rather than a measurement. It was then measured: with the
/// swallow removed, scrolling over the icon behaves identically on the stock
/// Windows 11 taskbar and on StartAllBack. The shell's own wheel handling in the
/// notification area is scoped to the rect of its own volume button, which is a
/// different icon slot, so there is nothing there to double-handle.
///
/// Once nothing needs blocking, raw input is better on every axis that matters:
///
/// <list type="bullet">
/// <item><b>No hook.</b> <c>SetWindowsHookEx(WH_MOUSE_LL, ...)</c> is the call an
/// input logger makes, and some antivirus scores it on the call alone. There is
/// now no hook of any kind anywhere in this application.</item>
/// <item><b>No <c>LowLevelHooksTimeout</c> clock.</b> A hook callback that
/// overran the timeout (capped at 1000 ms since Windows 10 1709, whatever the
/// registry says) had the hook <i>silently removed</i>, with no way for the
/// application to observe it. That failure mode - scroll-to-adjust dying for the
/// rest of the session while the icon looks perfectly healthy - does not exist
/// here. A slow WM_INPUT handler delays only this thread's own queue.</item>
/// <item><b>Cheaper for everyone else.</b> A global low-level hook is a
/// chokepoint: every mouse event on the desktop had to round-trip through this
/// process before any other application saw it. Raw input is a delivery, not a
/// chokepoint - nobody else's input waits on us.</item>
/// </list>
///
/// Microsoft's own <c>LowLevelMouseProc</c> documentation recommends raw input
/// over a low-level hook for this reason.
///
/// <b>What the structure is still for.</b> <c>RIDEV_INPUTSINK</c> delivers a
/// WM_INPUT for every mouse <i>movement</i>, not just the wheel, and it does so
/// whether or not this application is in the foreground. So:
///
/// <list type="bullet">
/// <item>The registration lives on its own thread with its own message-only
/// window and its own pump. That volume of messages must not wake the UI thread,
/// which paints the flyout, renders icons and makes COM calls into the audio
/// engine under a lock. This is no longer about a timeout - it is about not
/// spending the UI thread's wakeups on mouse movement.</item>
/// <item>The handler does no COM, no UI, no disk, no lock and no allocation. It
/// reads two shorts out of a reused unmanaged buffer, and only when those say
/// "wheel" does it ask for the cursor position and hit-test it. Everything the
/// notch actually causes happens afterwards, on the UI thread.</item>
/// </list>
///
/// <c>RAWMOUSE</c> carries <i>relative</i> movement, not a screen point, so the
/// hit test uses <c>GetCursorPos</c>. That is what the wheel event's position
/// means anyway - a wheel notch is delivered to whatever is under the cursor -
/// and a user scrolling a 40x40 tray icon is not also whipping the mouse across
/// the screen.
///
/// The rect it hit-tests against is supplied by <see cref="SetIconRect"/>, which
/// the UI thread calls when the icon is hovered, when the tray is rebuilt, when
/// the display changes, and on a slow poll. Where the icon's position is unknown
/// the wheel is simply never claimed, so scrolling behaves normally instead of
/// misfiring.
///
/// Nothing constructs this class at all when scroll-to-adjust is switched off in
/// settings - see <see cref="Config.Settings.ScrollToAdjust"/>. "Off" means no
/// registration is ever made, not that events are received and ignored.
/// </summary>
internal sealed class TrayWheelRawInput : IDisposable
{
    private const int WM_INPUT = 0x00FF;
    private const int WM_QUIT = 0x0012;
    private const int WM_USER = 0x0400;
    private const uint PM_NOREMOVE = 0;

    /// <summary>HID usage page 1, usage 2: generic desktop / mouse.</summary>
    private const ushort UsagePageGeneric = 0x01;
    private const ushort UsageMouse = 0x02;

    /// <summary>Deliver input even when this application is not in the foreground.</summary>
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RIDEV_REMOVE = 0x00000001;

    private const uint RID_INPUT = 0x10000003;
    private const uint RIM_TYPEMOUSE = 0;
    private const ushort RI_MOUSE_WHEEL = 0x0400;

    private const IntPtr HWND_MESSAGE = -3;

    // The handler's catch-all is cheap, but a fault that recurs would arrive once
    // per mouse movement, so it cannot be allowed to log on every event. A fault
    // is logged in full a few times, then only when the kind of failure changes,
    // and never past the ceiling; the running total is reported by TraceIfChanged
    // instead.
    private const int FaultLogFirst = 5;
    private const int FaultLogCeiling = 20;

    // RAWINPUT, read field by field rather than marshalled as a struct: only two
    // fields matter for the common case, and the common case is every mouse move.
    // Offsets are x64, which is the only architecture this app is published for
    // (see RuntimeIdentifier in the csproj).
    //
    //   RAWINPUTHEADER            RAWMOUSE (at 24)
    //    0  DWORD     dwType        24  USHORT usFlags
    //    4  DWORD     dwSize        28  USHORT usButtonFlags
    //    8  HANDLE    hDevice       30  USHORT usButtonData
    //   16  WPARAM    wParam        32  ULONG  ulRawButtons
    //                               36  LONG   lLastX
    //                               40  LONG   lLastY
    //                               44  ULONG  ulExtraInformation
    private const int OffsetType = 0;
    private const int OffsetButtonFlags = 28;
    private const int OffsetButtonData = 30;
    private const int RawInputHeaderSize = 24;

    /// <summary>Big enough for a mouse RAWINPUT (48 bytes on x64), with headroom.</summary>
    private const int BufferSize = 128;

    private const string WindowClassName = "InfolumeRawInput";

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public NativeMethods.POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle,
        int x, int y, int w, int h, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(
        [In] RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativeMethods.POINT lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    // The window class is process-wide and outlives any one instance, so it is
    // registered once and never unregistered. Registering it per instance would
    // fail with ERROR_CLASS_ALREADY_EXISTS the second time scroll-to-adjust is
    // switched back on in settings.
    private static readonly Lock ClassLock = new();
    private static WndProc? _classProc;
    private static bool _classRegistered;

    // Keeping the delegate alive matters: if it is collected, user32 calls into
    // freed memory and the process dies without an exception. The window
    // procedure is static and rooted for the process lifetime for that reason;
    // it finds its instance through the field below.
    private static TrayWheelRawInput? _current;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly IntPtr _target;
    private readonly uint _notch;

    /// <summary>
    /// The buffer WM_INPUT is read into. Allocated once and reused: this is
    /// touched once per mouse movement, and a per-event allocation there would be
    /// garbage generated by moving the mouse.
    /// </summary>
    private readonly IntPtr _buffer = Marshal.AllocHGlobal(BufferSize);

    /// <summary>
    /// The icon rect, boxed. A <see cref="Rectangle"/> is four ints, so a plain
    /// field could be read half-written from the input thread while the UI thread
    /// updates it; publishing a box makes the swap a single reference write. The
    /// box is allocated by whoever calls <see cref="SetIconRect"/> - never on the
    /// input path, which only reads it.
    /// </summary>
    private object? _iconRect;

    private IntPtr _hwnd;
    private uint _threadId;
    private bool _registered;
    private bool _disposed;

    // Trace counters. Written on the input path because Interlocked.Increment is
    // a single lock-free instruction; the log line itself is emitted from the UI
    // thread by TraceIfChanged. Nothing is written to disk from the input path.
    private long _seen;
    private long _claimed;
    private int _lastX;
    private int _lastY;
    private long _traced = -1;

    // Fault accounting for the handler's catch-all. _lastFaultType is touched
    // only from the input thread; _faults is read from the UI thread by
    // TraceIfChanged, which is why it goes through Interlocked.
    private long _faults;
    private long _tracedFaults = -1;
    private string? _lastFaultType;

    /// <param name="target">
    /// Window handle claimed notches are posted to. Must belong to the UI thread.
    /// </param>
    /// <param name="notchMessage">Message id to post; the delta travels in wParam.</param>
    internal TrayWheelRawInput(IntPtr target, uint notchMessage)
    {
        _target = target;
        _notch = notchMessage;

        _thread = new Thread(Pump)
        {
            IsBackground = true,
            Name = "Infolume wheel raw input"
        };
        _thread.Start();

        // Wait for the registration, so the outcome is logged in construction
        // order and IsListening means something immediately. Bounded: a hang here
        // would be a hang at startup, which is worse than losing scroll-to-adjust.
        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
            Diagnostics.Log.Write("rawinput", "input thread did not report ready within 5s");
    }

    /// <summary>Whether raw input is registered. False means scroll-to-adjust is off.</summary>
    internal bool IsListening => _registered;

    /// <summary>
    /// Caches the icon rect for the input thread to hit-test against. Called from
    /// the UI thread; pass null when the icon's position is unknown or has been
    /// invalidated, which makes the wheel claim nothing.
    ///
    /// An unchanged rect is dropped rather than re-boxed: this is called on every
    /// hover event and on a poll, and there is no reason to make garbage for it.
    /// </summary>
    internal void SetIconRect(Rectangle? rect)
    {
        if (rect is not { } r)
        {
            if (Volatile.Read(ref _iconRect) is not null) Volatile.Write(ref _iconRect, null);
            return;
        }

        if (Volatile.Read(ref _iconRect) is Rectangle current && current == r) return;
        Volatile.Write(ref _iconRect, r);
    }

    /// <summary>
    /// Emits one trace line when the counters have moved since the last call.
    ///
    /// This answers the questions a wheel-that-does-nothing investigation needs -
    /// is anything arriving at all, where was the cursor, what rect was it tested
    /// against, did anything get claimed - without any of it happening on the
    /// input path.
    ///
    /// Note that <c>seen</c> counts wheel events only, not the mouse movements
    /// that share the same message.
    /// </summary>
    internal void TraceIfChanged()
    {
        if (!Diagnostics.Log.Enabled) return;

        long seen = Interlocked.Read(ref _seen);
        long faults = Interlocked.Read(ref _faults);

        // Faults are part of the trigger, not just the payload: a handler that
        // throws before it counts the event moves this counter and not the other,
        // and that is the case most worth surfacing.
        if (seen == _traced && faults == _tracedFaults) return;
        _traced = seen;
        _tracedFaults = faults;

        string rect = Volatile.Read(ref _iconRect) is Rectangle r ? r.ToString() : "unknown";
        Diagnostics.Log.Write("rawinput",
            $"seen={seen} claimed={Interlocked.Read(ref _claimed)} " +
            $"last=({_lastX},{_lastY}) rect={rect} registered={IsListening}" +
            (faults > 0 ? $" faults={faults} last-fault={_lastFaultType}" : ""));
    }

    /// <summary>
    /// The input thread: create the window, register, pump, unregister. Nothing
    /// else ever runs here, so a WM_INPUT per mouse movement costs the UI thread
    /// nothing.
    /// </summary>
    private void Pump()
    {
        try
        {
            _threadId = GetCurrentThreadId();

            // Force the message queue into existence before anyone is told this
            // thread is ready. A thread's queue is created lazily, on its first
            // call to a message function, and PostThreadMessage is documented to
            // FAIL against a thread that does not have one yet - this PeekMessage
            // is the pattern those docs prescribe for exactly this handshake.
            PeekMessage(out _, IntPtr.Zero, WM_USER, WM_USER, PM_NOREMOVE);

            EnsureWindowClass();

            // A message-only window. It has no presence on screen, is not enumerated
            // by anything, and receives nothing but what is addressed to it.
            _hwnd = CreateWindowExW(0, WindowClassName, null, 0, 0, 0, 0, 0,
                                    HWND_MESSAGE, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                Diagnostics.Log.Write("rawinput",
                    $"CreateWindowEx failed, scroll-to-adjust disabled (err={Marshal.GetLastWin32Error()})");
                return;
            }

            _current = this;

            var rid = new RAWINPUTDEVICE
            {
                usUsagePage = UsagePageGeneric,
                usUsage = UsageMouse,

                // INPUTSINK is what makes this work at all: a tray application is
                // never the foreground window, and without it raw input arrives
                // only while the registering application has focus.
                dwFlags = RIDEV_INPUTSINK,
                hwndTarget = _hwnd
            };

            _registered = RegisterRawInputDevices([rid], 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());

            // A failed registration is not fatal - everything except
            // scroll-to-adjust still works - but it is indistinguishable from a
            // failing hit test unless it is said out loud.
            if (!_registered)
                Diagnostics.Log.Write("rawinput",
                    $"RegisterRawInputDevices failed, scroll-to-adjust disabled (err={Marshal.GetLastWin32Error()})");
            else
                Diagnostics.Log.Write("rawinput", $"registered on thread {_threadId}");
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error("rawinput", ex);
        }
        finally
        {
            _ready.Set();
        }

        if (!_registered)
        {
            if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
            return;   // nothing to service
        }

        // GetMessage returns 0 for WM_QUIT and -1 on error; either ends the loop.
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        Unregister();
        Diagnostics.Log.Write("rawinput", "unregistered");
    }

    private void Unregister()
    {
        if (_registered)
        {
            // RIDEV_REMOVE requires a null target, and is the documented way to
            // stop receiving. Doing it here, on the thread that registered, keeps
            // the symmetry the API expects.
            var rid = new RAWINPUTDEVICE
            {
                usUsagePage = UsagePageGeneric,
                usUsage = UsageMouse,
                dwFlags = RIDEV_REMOVE,
                hwndTarget = IntPtr.Zero
            };
            RegisterRawInputDevices([rid], 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            _registered = false;
        }

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        _current = null;
    }

    private static void EnsureWindowClass()
    {
        lock (ClassLock)
        {
            if (_classRegistered) return;

            _classProc = StaticWndProc;
            var wc = new WNDCLASS
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_classProc),
                hInstance = GetModuleHandleW(null),
                lpszClassName = WindowClassName
            };

            if (RegisterClassW(ref wc) == 0)
            {
                int err = Marshal.GetLastWin32Error();
                const int ERROR_CLASS_ALREADY_EXISTS = 1410;
                if (err != ERROR_CLASS_ALREADY_EXISTS)
                    throw new InvalidOperationException($"RegisterClass failed (err={err})");
            }

            _classRegistered = true;
        }
    }

    private static IntPtr StaticWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_INPUT) _current?.OnRawInput(lParam);

        // WM_INPUT must reach DefWindowProc so the system can clean the message
        // up; skipping it leaks the raw input buffer for every mouse movement.
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// The input path. This runs once per mouse movement as well as once per
    /// wheel notch, so: no COM, no UI, no disk, no lock, no allocation. Two short
    /// reads decide whether it is even a wheel event; only then does it ask for
    /// the cursor position.
    /// </summary>
    private void OnRawInput(IntPtr hRawInput)
    {
        try
        {
            uint size = BufferSize;
            uint read = GetRawInputData(hRawInput, RID_INPUT, _buffer, ref size, RawInputHeaderSize);
            if (read == unchecked((uint)-1) || read < RawInputHeaderSize) return;

            if ((uint)Marshal.ReadInt32(_buffer, OffsetType) != RIM_TYPEMOUSE) return;

            ushort buttonFlags = (ushort)Marshal.ReadInt16(_buffer, OffsetButtonFlags);
            if ((buttonFlags & RI_MOUSE_WHEEL) == 0) return;   // a move, not a notch

            // usButtonData is a USHORT in the struct but carries a signed delta;
            // scroll-down arrives as -120. ReadInt16 already returns it signed,
            // which is exactly the reinterpretation wanted here.
            int delta = Marshal.ReadInt16(_buffer, OffsetButtonData);

            if (!GetCursorPos(out var pt)) return;

            Interlocked.Increment(ref _seen);
            _lastX = pt.X;
            _lastY = pt.Y;

            // Unboxing a value type out of the field is a read, not an allocation,
            // and so is widening it to Rectangle? for the hit test.
            Rectangle? rect = Volatile.Read(ref _iconRect) is Rectangle r ? r : null;

            if (WheelGesture.Claims(rect, pt.X, pt.Y))
            {
                Interlocked.Increment(ref _claimed);
                PostMessage(_target, _notch, delta, IntPtr.Zero);
            }
        }
        catch (Exception ex)
        {
            // Never let a window procedure throw across the native boundary: this
            // frame is called from user32, so an escaping exception unwinds
            // through native frames and takes the process with it.
            //
            // Deliberately catch-all rather than a type list, matching Log.Guard,
            // which every other callback in this app funnels through.
            //
            // Rate limited, because a recurring fault would arrive once per mouse
            // movement and turn this into a synchronous File.AppendAllText plus a
            // stack trace per event. FaultLogFirst is "enough of a new problem to
            // diagnose it"; FaultLogCeiling only bounds pathological churn, where
            // two exception kinds alternating would otherwise satisfy the
            // type-change test on every single event.
            //
            // The type-change test earns its place by catching a SECOND failure
            // mode appearing after the first - a recurring ObjectDisposedException
            // during shutdown masking a later NullReferenceException, say - which
            // a plain first-N rule goes blind to.
            //
            // _lastFaultType is written here and read by TraceIfChanged on the UI
            // thread with no barrier. Benign, and deliberately so: a reference read
            // is atomic, so the worst case is one trace line naming a stale type.
            long faults = Interlocked.Increment(ref _faults);
            string kind = ex.GetType().Name;
            bool changed = !string.Equals(kind, _lastFaultType, StringComparison.Ordinal);
            _lastFaultType = kind;

            if (faults <= FaultLogFirst || (changed && faults <= FaultLogCeiling))
                Diagnostics.Log.Error("rawinput", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Ends the message loop, which unregisters on the thread that registered.
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);

        // Bounded: it is a background thread, so it cannot hold up process exit,
        // and an input thread that will not come back is not worth waiting on.
        bool ended = _thread.Join(TimeSpan.FromSeconds(2));

        // Only safe once the thread that reads it has stopped. If the join timed
        // out it has not, so the buffer is deliberately leaked rather than freed
        // underneath a live reader - a few bytes at shutdown against a use-after-
        // free on the input path is not a close call.
        if (ended) Marshal.FreeHGlobal(_buffer);

        _ready.Dispose();
    }
}

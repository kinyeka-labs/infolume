using System.Drawing;
using System.Windows.Forms;
using Infolume.Audio;
using Infolume.Config;
using Infolume.Icons;
using Infolume.Native;
using Infolume.Ui;
using Microsoft.Win32;

namespace Infolume;

/// <summary>
/// Owns the tray icon and keeps it in step with the audio state.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly AudioEngine _engine;
    private readonly Settings _settings;
    private readonly NotifyIcon _tray;
    private readonly FlyoutForm _flyout;
    private readonly OsdForm _osd = new();
    private readonly MessageWindow _messages;

    // Null whenever scroll-to-adjust is off. Not readonly, because the setting can
    // be toggled while the app is running and "off" has to mean the registration
    // is actually torn down, not merely ignored.
    private TrayWheelRawInput? _wheel;
    private readonly TrayIconLocator _locator = new();
    private readonly Control _marshaller = new();
    private readonly System.Windows.Forms.Timer _rectPoll;

    private Icon? _currentIcon;
    private int _lastSize;

    internal TrayApp()
    {
        _settings = Settings.Load();
        _engine = new AudioEngine();

        _tray = new NotifyIcon
        {
            Visible = true,
            Text = "Infolume"
        };

        _flyout = new FlyoutForm(_engine, _settings);
        _flyout.SettingsChanged += Repaint;
        _flyout.SettingsRequested += OpenSettings;

        _tray.MouseUp += OnTrayMouseUp;

        // MouseMove fires only while the cursor is over this icon, which is what
        // lets the locator learn the icon's rect on taskbars where the shell will
        // not report it. It is also the best possible moment to refresh the rect
        // the wheel input hit-tests against: the cursor has to arrive at the icon
        // before it can be scrolled there, so this fires immediately before the
        // gesture it serves.
        _tray.MouseMove += (_, _) =>
        {
            _locator.NoteHover(Cursor.Position, _lastSize);
            RefreshIconRect();
            Diagnostics.Log.Write("tray", $"hover {Cursor.Position} -> rect {_locator.Estimate(_lastSize)}");
        };

        _tray.ContextMenuStrip = BuildMenu();

        // A plain hidden Control as the marshalling target. Deliberately NOT the
        // flyout: forcing a Form's handle before Show leaves Form.Visible true
        // while the window is not actually on screen, so the first tray click
        // "toggles" a window nobody can see and appears to do nothing.
        _ = _marshaller.Handle;

        _osd.Dismissed += () => Diagnostics.Log.Guard("osd", Repaint);   // restores the tooltip

        _engine.Changed += OnEngineChanged;

        // StartAllBack restarts explorer on settings changes more often than stock
        // Windows; without this the icon silently disappears until relaunch.
        //
        // Built before the wheel input because it is what that posts to: claimed
        // notches arrive here as a window message and are acted on from the
        // message loop, on this thread, rather than on the input thread.
        _messages = new MessageWindow(
            onTaskbarCreated: ReAddIcon,
            onThemeChanged: Repaint,
            onWheelNotch: OnTrayWheel);

        SyncWheelInput(startup: true);

        // A resolution or scaling change moves the icon and changes its size, so
        // the learned rect is void and the icon needs repainting at the new size.
        //
        // The flyout needs telling too, and used to not be. Every dimension and
        // font in it is multiplied by a scale taken from the DPI of the monitor
        // it was opened on, so an open flyout is laid out for a display that no
        // longer exists. A closed one is fine - Toggle recomputes on each open.
        SystemEvents.DisplaySettingsChanged += (_, _) =>
        {
            _locator.Reset();
            RefreshIconRect();
            _flyout.DisplayChanged();
            Repaint();
        };

        // The input thread cannot go and ask where the icon is - that would put a
        // shell call and a monitor enumeration on a path that runs once per mouse
        // movement - so the rect is pushed to it from here. Hovering and the
        // invalidation events above cover every move the app is told about; this
        // poll covers the ones it is not, which are ordinary: a neighbouring icon
        // appearing or disappearing shifts ours sideways, and an auto-hiding
        // taskbar moves it off-screen and back.
        //
        // One second, so a stale rect can misjudge at most that long. The cost is
        // one Shell_NotifyIconGetRect per second on an idle UI thread.
        _rectPoll = new System.Windows.Forms.Timer { Interval = 1000 };
        _rectPoll.Tick += (_, _) => Diagnostics.Log.Guard("rectpoll", () =>
        {
            RefreshIconRect();
            _wheel?.TraceIfChanged();
        });
        _rectPoll.Start();

        RefreshIconRect();
        Repaint();
    }

    // ------------------------------------------------------------- painting

    private bool DarkTaskbar => _settings.Theme switch
    {
        ThemeMode.ForceLightTaskbar => false,
        ThemeMode.ForceDarkTaskbar => true,
        _ => SystemUsesLightTheme() == false
    };

    /// <summary>
    /// Null when the value cannot be read. Note this is only a hint here:
    /// StartAllBack paints its taskbar from its own settings, which is exactly why
    /// ThemeMode has two manual overrides.
    /// </summary>
    private static bool? SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int v ? v != 0 : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Engine changes arrive from two places: device notifications on the UI
    /// thread, but volume and mute callbacks on a Windows audio worker thread.
    /// Touching NotifyIcon or any form from that worker thread marshals into the
    /// STA and can deadlock against the engine's own lock, which wedges the UI
    /// thread - the panel stops painting and queued work never runs. So everything
    /// is funnelled onto the UI thread here.
    /// </summary>
    private void OnEngineChanged()
    {
        try
        {
            if (_marshaller.IsHandleCreated && _marshaller.InvokeRequired)
            {
                _marshaller.BeginInvoke(() => Diagnostics.Log.Guard("repaint", Repaint));
                return;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return;   // shutting down
        }

        Diagnostics.Log.Guard("repaint", Repaint);
    }

    private void Repaint()
    {
        if (_tray.Container is null && !_tray.Visible) return;

        var ep = _engine.Current;
        bool dark = DarkTaskbar;

        // Size is read per repaint rather than cached at startup: the icon can move
        // between monitors with different DPI, and StartAllBack's tray metrics move
        // independently of the Windows scaling setting.
        int size = _settings.IconSize > 0
            ? _settings.IconSize
            : NativeMethods.TrayIconSize(Cursor.Position);
        _lastSize = size;

        var state = new IconState(
            Glyph: ep is null ? GlyphKind.Unknown : GlyphResolver.Resolve(ep, _settings),
            GlyphColor: ep is null ? Palette.InkOn(dark) : _settings.GlyphColorFor(ep.Id, dark),
            AccentColor: ep is null ? Palette.TrackOn(dark) : Palette.AccentFor(ep.Id),
            Volume: ep?.Volume ?? 0f,
            Muted: ep?.Muted ?? false,
            DarkTaskbar: dark,
            Size: size,
            Cue: _settings.Cue,
            Style: _settings.Style,
            InclineBars: _settings.InclineBars);

        var next = IconPainter.Render(state);
        var previous = _currentIcon;
        _tray.Icon = next;
        _currentIcon = next;
        IconPainter.DisposeIcon(previous);

        // While the readout is up the shell would draw its tooltip in the same
        // corner, on top of it. An empty string takes the tooltip away without
        // disturbing anything else.
        _tray.Text = _osd.Visible ? string.Empty : Tooltip(ep, _settings);

        // Keep an open panel truthful when the level moves from elsewhere, without
        // rebuilding it.
        _flyout.SyncLevel();
    }

    /// <summary>
    /// The hover tooltip. Leads with the app name, because the tray icon was
    /// otherwise anonymous - nothing identified what it belonged to.
    ///
    /// NotifyIcon.Text is capped at 63 characters, so the lines are dropped from
    /// the least important end rather than the string being blindly truncated:
    /// the adapter goes first, then the app name, and the device and its level
    /// always survive.
    /// </summary>
    internal static string Tooltip(EndpointInfo? ep, Settings settings)
    {
        const int cap = 63;
        if (ep is null) return "Infolume: no output device";

        string name = settings.NameFor(ep.Id, ep.Description);
        string level = ep.Muted ? "muted" : $"{(int)Math.Round(ep.Volume * 100)}%";
        string device = $"{name}  {level}";

        string full = $"Infolume\n{device}\n{ep.Adapter}";
        if (full.Length <= cap) return full;

        full = $"Infolume\n{device}";
        if (full.Length <= cap) return full;

        return device.Length <= cap ? device : device[..cap];
    }

    private void ReAddIcon()
    {
        // The tray was rebuilt, so the icon is almost certainly somewhere else now.
        // A stale rect is worse than none: it would claim scrolls over whatever
        // took that spot, moving the volume when the user meant to scroll
        // something else.
        _locator.Reset();
        _tray.Visible = false;
        _tray.Visible = true;
        RefreshIconRect();
        Repaint();
    }

    // ----------------------------------------------------------- interaction

    /// <summary>
    /// Where the icon is: the shell's answer when it gives one, otherwise the
    /// rect learned from hovering. Used for both the flyout anchor and the
    /// scroll-wheel hit test.
    /// </summary>
    private Rectangle? ResolveIconRect() =>
        TrayPositioning.IconRect(_tray) ?? _locator.Estimate(_lastSize);

    /// <summary>
    /// Hands the current icon rect to the wheel input.
    ///
    /// It used to resolve the rect itself, on the input path - reflection into
    /// <c>NotifyIcon</c>, a shell call and a monitor enumeration, once per event.
    /// Resolving it here instead means the input thread only ever reads a cached
    /// rectangle.
    /// </summary>
    private void RefreshIconRect() => _wheel?.SetIconRect(ResolveIconRect());

    /// <summary>
    /// Brings the wheel input into line with <see cref="Settings.ScrollToAdjust"/>,
    /// at startup and whenever the setting changes.
    ///
    /// Turning it off disposes the object, which unregisters raw input and
    /// destroys the message-only window it was delivered to. That is the point of
    /// the setting: off means the application is not receiving mouse input at
    /// all, which is a claim a suspicious user can check with Process Explorer
    /// rather than take on trust. Turning it back on builds a fresh one and hands
    /// it the current icon rect, so the gesture works immediately instead of
    /// waiting for the next hover.
    /// </summary>
    /// <param name="startup">
    /// True for the construction-time call. That one states the setting either
    /// way, before acting on it, so a log from a user reporting "scrolling does
    /// nothing" says immediately whether the feature is even switched on - and
    /// so the absence of any rawinput line below it is readable as the answer.
    /// Later calls only say something when the setting actually changed.
    /// </param>
    private void SyncWheelInput(bool startup = false)
    {
        string state = _settings.ScrollToAdjust ? "on" : "off";
        if (startup) Diagnostics.Log.Write("tray", $"scroll-to-adjust is {state}");

        if (_settings.ScrollToAdjust == (_wheel is not null)) return;

        if (_wheel is { } existing)
        {
            existing.Dispose();
            _wheel = null;
        }
        else
        {
            _wheel = new TrayWheelRawInput(_messages.Handle, MessageWindow.WheelNotch);
            RefreshIconRect();
        }

        if (!startup) Diagnostics.Log.Write("tray", $"scroll-to-adjust switched {state}");
    }

    private void OnTrayMouseUp(object? sender, MouseEventArgs e)
    {
        Diagnostics.Log.Write("tray", $"mouseup button={e.Button} at={Cursor.Position}");
        if (e.Button != MouseButtons.Left) return;

        _osd.HideNow();   // the flyout supersedes it

        // An exception here would otherwise vanish into the message loop and look
        // exactly like the click doing nothing.
        Diagnostics.Log.Guard("tray", () => _flyout.Toggle(ResolveIconRect(), DarkTaskbar));
    }

    /// <summary>Opens the flyout as though the icon had been clicked. Used by --selftest.</summary>
    internal void SimulateClick() =>
        Diagnostics.Log.Guard("tray", () => _flyout.Toggle(ResolveIconRect(), DarkTaskbar));

    internal void LogEnvironment()
    {
        var shell = TrayPositioning.IconRect(_tray);
        var rect = ResolveIconRect();
        Diagnostics.Log.Write("env", $"shell icon rect: {(shell is null ? "NOT REPORTED" : shell.ToString())}");
        Diagnostics.Log.Write("env", $"resolved icon rect: {(rect is null ? "unknown (never hovered)" : rect.ToString())}");
        Diagnostics.Log.Write("env", $"cursor={Cursor.Position} dark={DarkTaskbar} iconSize={_lastSize}");
        Diagnostics.Log.Write("env", $"scroll-to-adjust: {(_settings.ScrollToAdjust ? "on" : "off")}, raw input registered: {_wheel?.IsListening ?? false}");
        foreach (var s in Screen.AllScreens)
            Diagnostics.Log.Write("env", $"screen {s.DeviceName} bounds={s.Bounds} work={s.WorkingArea} primary={s.Primary}");
        var ep = _engine.Current;
        Diagnostics.Log.Write("env", ep is null ? "no default endpoint" : $"default={ep.Description} / {ep.Adapter} vol={ep.Volume:P0}");
        Diagnostics.Log.Write("env", $"endpoints={_engine.Endpoints().Count} sessions={_engine.Sessions().Count}");

        // Everything GlyphResolver reads, and what it decided. This is the line to
        // ask for when somebody reports that their device drew as the wrong thing:
        // it separates "Windows does not report what you would expect" from "the
        // resolver mishandled what it was given". Device and adapter names already
        // reach the log elsewhere, so this adds no new privacy surface.
        foreach (var e in _engine.Endpoints())
            Diagnostics.Log.Write("env",
                $"endpoint \"{e.Description}\" / \"{e.Adapter}\" form={e.FormFactor} enum={e.Enumerator} " +
                $"jack={(e.JackSubType == Guid.Empty ? "none" : e.JackSubType.ToString())} " +
                $"icon=\"{e.IconPath}\" bt={e.IsBluetooth} -> glyph={GlyphResolver.Resolve(e, _settings)}");

        // Re-assert the CURRENT default as the default. A no-op for the user, but
        // it drives IPolicyConfig end to end - the one thing a trimmed or AOT
        // build would break silently, and the reason those are disabled.
        if (_engine.Current is { } cur)
        {
            bool ok = _engine.SetDefault(cur.Id);
            Diagnostics.Log.Write("env", $"IPolicyConfig round-trip: {(ok ? "OK" : "FAILED")}");
        }
    }

    /// <summary>
    /// A claimed wheel notch, arriving as a window message from the input thread.
    ///
    /// Everything expensive about the gesture lives here rather than on the input
    /// path: the COM call into the audio engine under its lock, the OSD form,
    /// and the DPI lookup behind it. Notches stay ordered and serialized - they
    /// are processed one message at a time on this thread - so a flick still reads
    /// each level after the previous one has been applied, exactly as it did when
    /// the callback ran this inline.
    /// </summary>
    private void OnTrayWheel(int delta)
    {
        var ep = _engine.Current;
        if (ep is null) return;

        int pct = WheelGesture.Adjust((int)Math.Round(ep.Volume * 100), delta, _settings.ScrollStep);
        Diagnostics.Log.Write("tray", $"wheel delta={delta} -> {pct}%");
        _engine.SetVolume(pct / 100f);

        bool nowMuted = ep.Muted;
        if (WheelGesture.Unmutes(ep.Muted, delta)) { _engine.ToggleMute(); nowMuted = false; }

        ShowOsd(pct / 100f, nowMuted);
    }

    /// <summary>
    /// The scroll readout. Suppressed while the flyout is open, which already
    /// shows the level - two readouts for one gesture is noise.
    /// </summary>
    private void ShowOsd(float level, bool muted)
    {
        if (_flyout.Visible) return;

        var ep = _engine.Current;
        if (ep is null) return;

        bool dark = DarkTaskbar;
        var rect = ResolveIconRect();
        float scale = TrayPositioning.AnchorDpi(rect) / 96f;

        _osd.Show(
            iconRect: rect,
            scale: scale,
            dark: dark,
            glyph: GlyphResolver.Resolve(ep, _settings),
            accent: Palette.AccentFor(ep.Id),
            level: level,
            muted: muted,
            device: _settings.NameFor(ep.Id, ep.Description));
    }

    /// <summary>
    /// Three actions, nothing configurable. Everything that used to live in
    /// nested submenus here is now in the settings window, where each choice can
    /// sit beside a preview of what it does.
    /// </summary>
    private ContextMenuStrip BuildMenu()
    {
        // The image margin is where a checked item draws its tick. Hiding it left
        // "Start with Windows" toggling silently, with nothing on screen changing.
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(DarkTaskbar),
            ShowImageMargin = true
        };
        menu.Opening += (_, _) => menu.Renderer = new DarkMenuRenderer(DarkTaskbar);

        var settings = new ToolStripMenuItem("Infolume settings");
        settings.Click += (_, _) => OpenSettings();
        menu.Items.Add(settings);

        var icons = new ToolStripMenuItem("Edit icons");
        icons.Click += (_, _) =>
        {
            var cur = _engine.Current;
            if (cur is not null) OpenEditor(cur.Id);
        };
        menu.Items.Add(icons);

        menu.Items.Add(new ToolStripSeparator());

        var startup = new ToolStripMenuItem("Start with Windows") { CheckOnClick = false };
        startup.Click += (_, _) =>
        {
            bool on = !AutoStart.IsEnabled();
            AutoStart.Set(on);
            startup.Checked = AutoStart.IsEnabled();
        };
        menu.Opening += (_, _) => startup.Checked = AutoStart.IsEnabled();
        menu.Items.Add(startup);

        menu.Items.Add(new ToolStripSeparator());

        var quit = new ToolStripMenuItem("Exit");
        quit.Click += (_, _) => ExitThread();
        menu.Items.Add(quit);

        return menu;
    }

    private void OpenEditor(string deviceId)
    {
        _osd.HideNow();
        _flyout.HideForDialog();

        Diagnostics.Log.Guard("editor", () =>
        {
            using var dlg = new DeviceEditorForm(_engine, _settings, deviceId, DarkTaskbar,
                TrayPositioning.AnchorDpi(ResolveIconRect()) / 96f);
            if (dlg.ShowDialog() == DialogResult.OK) _settings.Save();
        });

        _flyout.DialogClosed();
        Repaint();
    }

    private void OpenSettings()
    {
        _osd.HideNow();
        _flyout.HideForDialog();

        Diagnostics.Log.Guard("settings", () =>
        {
            using var dlg = new SettingsForm(_settings, DarkTaskbar,
                TrayPositioning.AnchorDpi(ResolveIconRect()) / 96f);
            // Lambda rather than a method group: the optional parameter means
            // SyncWheelInput is not an Action.
            dlg.Changed += () => SyncWheelInput();
            dlg.Changed += Repaint;
            dlg.ShowDialog();
        });

        _flyout.DialogClosed();
        Repaint();
    }

    // ---------------------------------------------------------------- teardown

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Capture the handle BEFORE disposing the tray: NotifyIcon disposes the
            // Icon it holds, after which reading .Handle throws and the HICON leaks.
            IntPtr live = IntPtr.Zero;
            try { live = _currentIcon?.Handle ?? IntPtr.Zero; }
            catch (ObjectDisposedException) { }

            _rectPoll.Stop();
            _rectPoll.Dispose();

            _tray.Visible = false;
            _tray.Icon = null;
            _tray.Dispose();

            if (live != IntPtr.Zero) NativeMethods.DestroyIcon(live);
            _currentIcon = null;
            _osd.HideNow();
            _osd.Dispose();
            _flyout.Dispose();
            _marshaller.Dispose();
            _wheel?.Dispose();
            _messages.Dispose();
            _engine.Dispose();
            _settings.Save();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// A hidden window listening for the two broadcasts that affect the icon -
    /// explorer restarting, and the light/dark theme changing - and for wheel
    /// notches handed over by the hook thread.
    /// </summary>
    private sealed class MessageWindow : NativeWindow, IDisposable
    {
        private const int WM_SETTINGCHANGE = 0x001A;

        /// <summary>
        /// A claimed wheel notch, delta in wParam. Private to this window class,
        /// so a WM_APP id needs no registration and cannot collide with anything:
        /// nothing else knows this handle exists.
        /// </summary>
        internal const uint WheelNotch = 0x8000 + 1;   // WM_APP + 1

        private readonly uint _taskbarCreated;
        private readonly Action _onTaskbarCreated;
        private readonly Action _onThemeChanged;
        private readonly Action<int> _onWheelNotch;

        internal MessageWindow(Action onTaskbarCreated, Action onThemeChanged, Action<int> onWheelNotch)
        {
            _onTaskbarCreated = onTaskbarCreated;
            _onThemeChanged = onThemeChanged;
            _onWheelNotch = onWheelNotch;
            _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
            CreateHandle(new CreateParams { Caption = "InfolumeMessageWindow" });
        }

        protected override void WndProc(ref Message m)
        {
            if ((uint)m.Msg == WheelNotch)
            {
                // Posted from the hook thread. Guarded because this runs off the
                // message loop, where an exception would otherwise surface as the
                // wheel silently doing nothing.
                //
                // Truncating rather than IntPtr.ToInt32: a negative delta was sign
                // extended on the way in, and the low 32 bits are the value back.
                int delta = (int)(long)m.WParam;
                Diagnostics.Log.Guard("wheel", () => _onWheelNotch(delta));
            }
            else if (_taskbarCreated != 0 && (uint)m.Msg == _taskbarCreated)
            {
                _onTaskbarCreated();
            }
            else if (m.Msg == WM_SETTINGCHANGE)
            {
                string? section = m.LParam == IntPtr.Zero
                    ? null
                    : System.Runtime.InteropServices.Marshal.PtrToStringAuto(m.LParam);
                if (string.Equals(section, "ImmersiveColorSet", StringComparison.Ordinal))
                    _onThemeChanged();
            }
            base.WndProc(ref m);
        }

        public void Dispose() => DestroyHandle();
    }
}

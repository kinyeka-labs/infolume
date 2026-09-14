using System.Windows.Forms;

namespace Infolume;

internal static class Program
{
    private const string MutexName = "Infolume.SingleInstance";

    [STAThread]
    private static void Main(string[] args)
    {
        // Build aid: regenerate the application icon from the live painter, so the
        // shell icon cannot drift from the one in the tray. Not Debug-only: the
        // build script calls it for every build.
        if (args.Length >= 2 && args[0] == "--make-icon")
        {
            Icons.IconFile.Write(args[1]);
            return;
        }

#if DEBUG
        // The --render-* design aids exist only in Debug: the sheet renderers they
        // call are excluded from Release so a shipped binary does not carry them,
        // and referencing them here would then fail to compile.

        // Design aid: render the glyph set at a given tray size. The tray itself
        // cannot be screenshotted here, so this is how painting gets reviewed.
        if (args.Length >= 2 && args[0] == "--render-preview")
        {
            int size = args.Length >= 3 && int.TryParse(args[2], out int s) ? s : 36;
            Icons.PreviewSheet.Write(args[1], size);
            return;
        }

        // Documentation aid: render the real flyout against plausible hardware.
        // This machine's own endpoints are three-quarters virtual, so a genuine
        // screenshot of them shows nobody anything useful.
        if (args.Length >= 3 && args[0] == "--render-mockup")
        {
            // Same initialisation as the real app, not a hand-rolled subset:
            // ApplicationConfiguration.Initialize applies PerMonitorV2 from the
            // csproj. Without it the process is DPI-unaware, DpiForPoint reports
            // 96 whatever the monitor is set to, and the panel lays out at half
            // size on a 200% display - a mockup that matches nobody's screen.
            ApplicationConfiguration.Initialize();
            Icons.MockupSheet.Write(args[1], args[2]);
            return;
        }

        // Design aid: compare application-icon candidates at shell sizes.
        if (args.Length >= 2 && args[0] == "--render-appmarks")
        {
            Icons.AppMarkSheet.Write(args[1]);
            return;
        }

        // Design aid: sweep incline bar counts at true tray size.
        if (args.Length >= 2 && args[0] == "--render-incline")
        {
            int px = args.Length >= 3 && int.TryParse(args[2], out int s4) ? s4 : 32;
            var gk2 = args.Length >= 4 && Enum.TryParse<Icons.GlyphKind>(args[3], true, out var g2)
                ? g2 : Icons.GlyphKind.Tv;
            Icons.InclineSheet.Write(args[1], px, gk2);
            return;
        }

        // Design aid: compare the volume indicators at true tray size.
        // --render-volumes <path> [px] [glyph] [cue]
        if (args.Length >= 2 && args[0] == "--render-volumes")
        {
            int px = args.Length >= 3 && int.TryParse(args[2], out int s3) ? s3 : 32;
            var glyph = args.Length >= 4 && Enum.TryParse<Icons.GlyphKind>(args[3], true, out var gk)
                ? gk : Icons.GlyphKind.Tv;
            var cue = args.Length >= 5 && Enum.TryParse<Icons.AudioCue>(args[4], true, out var ac)
                ? ac : Icons.AudioCue.None;
            Icons.VolumeSheet.Write(args[1], px, glyph, cue);
            return;
        }

        // Design aid: compare the audio-cue treatments at true tray size.
        if (args.Length >= 2 && args[0] == "--render-styles")
        {
            int px = args.Length >= 3 && int.TryParse(args[2], out int s2) ? s2 : 32;
            Icons.StyleSheet.Write(args[1], px);
            return;
        }
#endif

        // Development aid: hammer the painter and report GDI handle growth. The
        // icon repaints on every volume tick, so a single missed DestroyIcon shows
        // up as a steadily climbing handle count over a long session.
        if (args.Length >= 1 && args[0] == "--leak-test")
        {
            int iterations = args.Length >= 2 && int.TryParse(args[1], out int n) ? n : 2000;
            RunLeakTest(iterations);
            return;
        }

        bool selftest = args.Contains("--selftest");

        // A second tray icon for the same thing is worse than no second instance.
        // The check comes BEFORE logging is enabled: enabling truncates the file,
        // so a doomed second instance would otherwise wipe the running one's log.
        using var single = new Mutex(initiallyOwned: true, MutexName, out bool isFirst);
        if (!isFirst) return;

        // INFOLUME_LOG still chooses the path even when --log/--selftest turn
        // logging on, so a build script can point the trace somewhere it owns.
        if (args.Contains("--log") || selftest)
        {
            var envPath = Environment.GetEnvironmentVariable("INFOLUME_LOG");
            Diagnostics.Log.Enable(string.IsNullOrEmpty(envPath) || envPath == "1" ? null : envPath);
        }
        else Diagnostics.Log.EnableFromEnvironment();

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Diagnostics.Log.Error("threadexception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Diagnostics.Log.Error("unhandled", ex);
        };

        using var app = new TrayApp();

        if (selftest)
        {
            // Drive the exact click path on the UI thread once the message loop is
            // up, dump the geometry, then quit - so the flyout can be exercised
            // without needing a real click, or a screenshot to see the result.
            var timer = new System.Windows.Forms.Timer { Interval = 1200 };
            int step = 0;
            timer.Tick += (_, _) =>
            {
                switch (step++)
                {
                    case 0:
                        app.LogEnvironment();
                        Diagnostics.Log.Write("selftest", "simulating click");
                        app.SimulateClick();
                        break;
                    case 1:
                        Diagnostics.Log.Write("selftest", "done");
                        timer.Stop();
                        app.ExitThread();
                        break;
                }
            };
            timer.Start();
        }

        Application.Run(app);
    }

    private static void RunLeakTest(int iterations)
    {
        var self = System.Diagnostics.Process.GetCurrentProcess();

        // Warm up first, so one-off allocations do not read as a leak.
        for (int i = 0; i < 50; i++) PaintOnce(i);
        self.Refresh();
        int before = self.HandleCount;

        for (int i = 0; i < iterations; i++) PaintOnce(i);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        self.Refresh();
        int after = self.HandleCount;

        Console.WriteLine($"iterations : {iterations}");
        Console.WriteLine($"handles    : {before} -> {after}  (delta {after - before:+#;-#;0})");
        Console.WriteLine($"verdict    : {(after - before > iterations / 100 ? "LEAKING" : "stable")}");
    }

    private static void PaintOnce(int i)
    {
        var icon = Icons.IconPainter.Render(new Icons.IconState(
            Glyph: (Icons.GlyphKind)(i % Enum.GetValues<Icons.GlyphKind>().Length),
            GlyphColor: Icons.Palette.InkOn(true),
            AccentColor: Icons.Palette.Accents[i % Icons.Palette.Accents.Length],
            Volume: (i % 101) / 100f,
            Muted: i % 17 == 0,
            DarkTaskbar: true,
            Size: 36));
        Icons.IconPainter.DisposeIcon(icon);
    }
}

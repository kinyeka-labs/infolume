using System.Runtime.CompilerServices;
using System.Text;

namespace Infolume.Diagnostics;

/// <summary>
/// Append-only trace log. Off unless INFOLUME_LOG is set or --log is passed, so it
/// costs nothing in normal use, but the tray is hard to observe any other way:
/// there is no console, and this machine masks screenshots.
/// </summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static string? _path;

    internal static bool Enabled => _path is not null;

    internal static string Path => _path ?? "(disabled)";

    internal static void Enable(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "infolume.log");
        try { File.WriteAllText(_path, ""); } catch (IOException) { }
        Write("log", $"started pid={Environment.ProcessId} path={_path}");
    }

    internal static void EnableFromEnvironment()
    {
        if (Environment.GetEnvironmentVariable("INFOLUME_LOG") is { Length: > 0 } v)
            Enable(v == "1" ? null : v);
    }

    internal static void Write(string tag, string message)
    {
        if (_path is null) return;
        var line = $"{DateTime.Now:HH:mm:ss.fff}  {tag,-14}  {message}";
        lock (Gate)
        {
            try { File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8); }
            catch (IOException) { }
        }
    }

    internal static void Error(string tag, Exception ex, [CallerMemberName] string caller = "")
    {
        Write(tag, $"EXCEPTION in {caller}: {ex.GetType().Name}: {ex.Message}");
        if (ex.StackTrace is { } st) Write(tag, "  " + st.Replace("\n", "\n                              "));
    }

    /// <summary>Runs <paramref name="act"/>, logging anything it throws instead of losing it.</summary>
    internal static void Guard(string tag, Action act, [CallerMemberName] string caller = "")
    {
        try { act(); }
        catch (Exception ex) { Error(tag, ex, caller); }
    }
}

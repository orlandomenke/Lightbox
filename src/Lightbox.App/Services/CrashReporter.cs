using System.Runtime.InteropServices;

namespace Lightbox.App.Services;

/// <summary>
/// Catches what nothing else caught, writes it down, and says so.
/// </summary>
/// <remarks>
/// <para>
/// Before this, an unhandled exception ended the process with nothing left
/// behind: no file, no dialog, no message. The window simply vanished. That was
/// survivable while the app was a console program and a stack trace landed in
/// the terminal; as a GUI program (B113) there was nowhere for it to go at all.
/// </para>
/// <para>
/// <b>The dialog is Win32 rather than Avalonia, on purpose.</b> This runs when
/// something has already gone wrong, and the thing most likely to be wrong is
/// the UI. Asking a possibly-broken toolkit to render a window explaining that
/// it broke is how a crash reporter becomes the second crash. <c>MessageBox</c>
/// is provided by the OS and needs nothing of ours to be working.
/// </para>
/// </remarks>
internal static class CrashReporter
{
    /// <summary>Start catching. Called once, before the app is built.</summary>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Report(e.ExceptionObject as Exception, "unhandled");

        // The one that would otherwise stay invisible: a fire-and-forget task
        // whose fault nobody observed is dropped silently at collection. The
        // start screen is launched exactly that way.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Report(e.Exception, "unobserved-task");
            // Marked observed so the fault does not also tear the process down
            // — it has been recorded, which is the thing that was missing.
            e.SetObserved();
        };
    }

    /// <summary>Write the report, then try to say where it went.</summary>
    /// <remarks>
    /// Strictly in that order. Telling the artist is the part most likely to
    /// fail here, and it must not be able to lose a file that was already
    /// written successfully.
    /// </remarks>
    public static void Report(Exception? ex, string context)
    {
        if (ex is null) return;
        var path = DiagnosticLog.WriteCrash(ex, context);
        if (path is null) return;

        try
        {
            Announce(path);
        }
        catch
        {
            // The file is on disk either way, and the next launch will mention
            // it. A failed dialog is not worth a second exception here.
        }
    }

    private static void Announce(string path)
    {
        if (!OperatingSystem.IsWindows()) return;

        const uint YesNo = 0x00000004;
        const uint IconError = 0x00000010;
        const uint TopMost = 0x00040000;
        const int Yes = 6;

        var answer = MessageBoxW(
            IntPtr.Zero,
            $"Lightbox stopped unexpectedly.\n\nWhat happened was written to:\n{path}\n\nOpen the folder?",
            "Lightbox",
            YesNo | IconError | TopMost);

        // Reuse the reveal that already knows how each desktop spells this, and
        // whose own doc promises it never throws.
        if (answer == Yes) FileReveal.Reveal(path);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}

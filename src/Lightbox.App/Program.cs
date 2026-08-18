using System.Runtime.InteropServices;
using Avalonia;
using Lightbox.App.Services;

namespace Lightbox.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // First, so a failure anywhere after this point leaves a file behind.
        CrashReporter.Install();
        OpenConsoleIfAsked();
        // So a trace can say which kind of popup produced its numbers, rather
        // than leaving a stall count to be read against an unknown setting.
        InputTrace.PopupsAreOverlays = true;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Also used by Avalonia.Headless in App.Tests.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // B255. Popups are drawn inside the window instead of as native
            // ones, because a native popup costs a window to create and
            // destroy — and on a pen tablet the driver's phantom mouse makes
            // Avalonia's own menu code open and close submenus about fifty
            // times a second. Three traces from the reporter's Huion tie the
            // freeze to exactly that: every UI-thread stall over three seconds
            // is preceded by ~100 popup opens a second, and the rate away from
            // any stall is zero — one of them blocked the thread for 18.9 s.
            //
            // This does not stop the churn, which is Windows Ink's and out of
            // reach from here (see B126). It makes each cycle cost a visual
            // rather than a window, which is the half that is ours to change.
            //
            // The cost, so it is a trade rather than a free win: an overlay
            // popup is clipped to the window, so a menu opening near an edge
            // is laid out inside it rather than spilling onto the desktop.
            .With(new Win32PlatformOptions { OverlayPopups = true })
            // Inter, bundled: the typeface both design references are set in,
            // and the same face on every platform. Half of "the fonts look
            // big" was the fallback font — DejaVu runs a size wider than
            // Inter at the same size.
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// Open a console when somebody has asked to watch the traces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two ways to ask, and they answer different situations. The environment
    /// variables are the developer's, set for one run from a terminal. The
    /// setting is the artist's, turned on from <b>Help</b> and remembered —
    /// because the way it gets used is "switch this on, restart, and make the
    /// problem happen again", and a switch that forgot itself between runs
    /// would be no use for that.
    /// </para>
    /// <para>
    /// Called before anything touches <c>Console</c>, because the streams bind
    /// to whatever handles exist the first time they are used. Reading the
    /// settings file here costs one small read on a path that has not started
    /// Avalonia yet.
    /// </para>
    /// </remarks>
    private static void OpenConsoleIfAsked()
    {
        var traced = Environment.GetEnvironmentVariable("LIGHTBOX_TRACE") is not null
                  || Environment.GetEnvironmentVariable("LIGHTBOX_PERFTRACE") is not null;

        var asked = false;
        try
        {
            asked = AppSettings.Load().ShowDiagnosticsConsole;
        }
        catch
        {
            // An unreadable settings file must not stop the app starting. The
            // environment variables still work, which is the developer's path.
        }

        if (traced || asked) DiagnosticsConsole.Open();
    }
}

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
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Also used by Avalonia.Headless in App.Tests.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
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

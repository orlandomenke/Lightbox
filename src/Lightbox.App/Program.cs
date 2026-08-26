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
            .With(new Win32PlatformOptions
            {
                OverlayPopups = true,
                CompositionMode = CompositionModes(),
            })
            // Inter, bundled: the typeface both design references are set in,
            // and the same face on every platform. Half of "the fonts look
            // big" was the fallback font — DejaVu runs a size wider than
            // Inter at the same size.
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// How the window's pixels reach the screen, in the order Avalonia should
    /// try them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Being measured rather than chosen, which is why it is an environment
    /// variable and not a setting yet.</b> The owner's report of 2026-08-26
    /// puts <c>publish -&gt; drawn</c> at 52.22 ms around a draw of 3.98 ms —
    /// about three vsyncs of pure queueing, with every other cost in the chain
    /// now under 6 ms. Composition is where that queue lives: the default path
    /// hands each frame to DWM, which holds it for its own cadence on top of
    /// the compositor's.
    /// </para>
    /// <para>
    /// <c>LowLatencyDxgiSwapChain</c> is Avalonia's answer to exactly this and
    /// presents to a swap chain directly. It is <b>not</b> the default here,
    /// because it gives up DWM's compositing — transparency and some window
    /// effects go with it — and because a latency win that has not been
    /// measured on the owner's own hardware is a guess. Set
    /// <c>LIGHTBOX_COMPOSITION=lowlatency</c> to try it; the render report
    /// prints which mode a session actually ran under, so two reports can be
    /// compared without anybody having to remember.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<Win32CompositionMode> CompositionModes() =>
        Choice switch
        {
            // Ordered as fallbacks: if the swap chain cannot be had, take the
            // ordinary path rather than failing to open a window.
            // Asked for the desktop compositor: the path Lightbox shipped
            // before this was measured, kept reachable for a driver that
            // refuses a swap chain.
            "compositor" =>
            [
                Win32CompositionMode.WinUIComposition,
                Win32CompositionMode.RedirectionSurface,
            ],
            "redirection" => [Win32CompositionMode.RedirectionSurface],
            // Ordered as fallbacks: if the swap chain cannot be had, take the
            // ordinary path rather than failing to open a window.
            _ =>
            [
                Win32CompositionMode.LowLatencyDxgiSwapChain,
                Win32CompositionMode.WinUIComposition,
                Win32CompositionMode.RedirectionSurface,
            ],
        };

    /// <summary>
    /// Which composition path to ask for: the environment variable if somebody
    /// set one, otherwise the artist's setting, otherwise the swap chain.
    /// </summary>
    /// <remarks>
    /// The variable wins so an A/B can be run without touching the settings
    /// file, which is how this became the default in the first place. Read once
    /// — a platform option is fixed for the life of the process.
    /// </remarks>
    private static string Choice
    {
        get
        {
            var env = (Environment.GetEnvironmentVariable("LIGHTBOX_COMPOSITION") ?? "")
                .Trim().ToLowerInvariant();
            if (env.Length > 0) return env;
            // Never let a broken settings file stop the window opening: this
            // runs before anything else and its failure mode is no application
            // at all, so it falls back to the measured-best path.
            try
            {
                return Services.AppSettings.Load().PresentThroughDesktopCompositor
                    ? "compositor" : "lowlatency";
            }
            catch
            {
                return "lowlatency";
            }
        }
    }

    /// <summary>What <see cref="CompositionModes"/> chose, for the render report.</summary>
    internal static string CompositionChoice => Choice switch
    {
        "compositor" => "WinUI composition (Configure ▸ asked for the desktop compositor)",
        "redirection" => "redirection surface (LIGHTBOX_COMPOSITION=redirection)",
        _ => "low-latency swap chain (the default)",
    };

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

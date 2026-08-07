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
        AttachConsoleForTracing();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Also used by Avalonia.Headless in App.Tests.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    /// <summary>
    /// Give the opt-in traces somewhere to land when the app is run from a
    /// terminal.
    /// </summary>
    /// <remarks>
    /// The app is a GUI-subsystem executable, which is what stops a console
    /// window opening beside it — and it means the process is given no console
    /// of its own. Two diagnostics write to <c>Console.Error</c> and are gated
    /// behind environment variables: <c>LIGHTBOX_TRACE</c> in
    /// <c>CanvasControl</c> and <c>LIGHTBOX_PERFTRACE</c> in
    /// <c>MainViewModel</c>.
    ///
    /// <b>This makes that deterministic rather than rescuing it.</b> A GUI
    /// process started from a shell may already inherit that shell's stderr
    /// handle, in which case the traces would have printed anyway; whether they
    /// do depends on how the launcher passes handles, which is not something
    /// this repository can test from Linux. Attaching to the launching
    /// terminal's console makes the answer the same either way, and the failure
    /// it guards against is the quiet kind — silence reads as the tracing being
    /// broken rather than as there being nowhere to print.
    ///
    /// Costs nothing when neither variable is set, and is skipped entirely off
    /// Windows. Called before anything touches <c>Console</c>, because the
    /// streams bind to whatever handles exist the first time they are used.
    /// </remarks>
    private static void AttachConsoleForTracing()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (Environment.GetEnvironmentVariable("LIGHTBOX_TRACE") is null &&
            Environment.GetEnvironmentVariable("LIGHTBOX_PERFTRACE") is null) return;

        // Not being launched from a console is the ordinary case and not a
        // failure: there is simply nothing to attach to, and the traces are
        // lost the way they would have been anyway.
        AttachConsole(AttachParentProcess);
    }

    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint dwProcessId);
}

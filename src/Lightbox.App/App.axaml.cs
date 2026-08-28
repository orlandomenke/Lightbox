using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Lightbox.App.Services;
using Lightbox.App.Views;

namespace Lightbox.App;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // B255. A pen tablet's phantom mouse makes Avalonia's menu code
            // open and close submenus sixty times a second; this refuses the
            // close that arrives within a quarter-second of the open, which is
            // the half of that cycle nobody asked for. Before any menu exists,
            // and a no-op on every machine where the churn does not happen.
            SubmenuCloseGrace.Install();

            // The GPU probe writes one line about what this machine can do, and
            // it lives in Raster now, which has no log to write to. Wired here
            // rather than moved: writing a log file is an application's job.
            GpuComposeProbe.Note = Services.DiagnosticLog.WriteNote;

            // Eight fixed shapes with no input, ~160 ms to bake on a background
            // thread of its own. Started here so the first artist to open the
            // tip library is not the one who waits for them.
            Lightbox.Raster.Tips.TipCatalogue.Warm();

            var splash = new SplashWindow();
            splash.Show();
            // Deliberately not desktop.MainWindow — see Startup.HandOffAsync.

            // Nothing paints until the dispatcher loop is running, and it is not
            // running until this method returns. So the rest is posted rather
            // than run: done inline, the splash would be constructed, shown and
            // closed again without a single frame reaching the screen.
            Dispatcher.UIThread.Post(
                () => _ = StartAsync(desktop, splash), DispatcherPriority.Background);
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Build the window behind the splash, then trade the two over.</summary>
    private static async Task StartAsync(
        IClassicDesktopStyleApplicationLifetime desktop, SplashWindow splash)
    {
        try
        {
            await Startup.WhenPaintedAsync(splash);
            var visible = Stopwatch.StartNew();

            // The one synchronous cost at startup: MainViewModel, the shortcut
            // map and every panel in the docking pool. It blocks the UI thread
            // and cannot not — window construction is UI-thread-only — which is
            // why the splash is a still panel rather than anything animated.
            var window = new MainWindow();

            // If the last run ended badly, say so once. The crash-time dialog is
            // the first attempt and the better one; this is the fallback for when
            // the UI was too far gone to show it. Consumed here, so the same
            // crash is not re-reported at every launch from now on. Wired before
            // the handoff shows the window, so the note arrives with it.
            if (Services.DiagnosticLog.TakePendingCrash() is { } crash)
            {
                window.Opened += (_, _) => window.NotePreviousCrash(crash);
            }

            var wait = Startup.Remaining(visible.Elapsed);
            if (wait > TimeSpan.Zero) await Task.Delay(wait);

            // The start screen is offered from here rather than from the window
            // itself, so that a window built directly — every headless test —
            // never has a modal dialog appear over it.
            await Startup.HandOffAsync(
                splash, window, w => desktop.MainWindow = w, window.OfferStartScreenAsync);
        }
        catch
        {
            // A splash left up over a dead application looks like a hang rather
            // than a crash. Take it down, then let the failure be a failure:
            // the app is a GUI program with no console for a stack trace to
            // land in, so swallowing this would erase it entirely.
            splash.Close();
            throw;
        }
    }
}

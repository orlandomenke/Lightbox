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
            // Before any window exists, because the phantom mouse a pen tablet
            // emits has to be dropped ahead of Avalonia recomputing which
            // control the pointer is over — that recomputation is what fires
            // the enter/exit pair, and the pair is B126, B254 and B255 (the
            // 6-second freeze) all three. See PenEchoFilter for the measured
            // case; on a machine with no pen it can never engage.
            PenEchoFilter.Install(InputManagerOrNull());

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

    /// <summary>
    /// Avalonia's input manager, or null where it cannot be reached.
    /// </summary>
    /// <remarks>
    /// <c>AvaloniaLocator</c> is another of the types the 12.1.1 reference
    /// assembly hides from the compiler while the runtime exposes it, so the
    /// service lookup goes the same way as the rest of
    /// <see cref="PenEchoFilter"/>'s: by name, guarded, and worth nothing worse
    /// than the filter not installing.
    /// </remarks>
    private static object? InputManagerOrNull()
    {
        try
        {
            var locator = typeof(Application).Assembly.GetType("Avalonia.AvaloniaLocator");
            var current = locator?.GetProperty("Current")?.GetValue(null);
            var get = current?.GetType().GetMethod("GetService", [typeof(Type)]);
            return get?.Invoke(current, [typeof(Avalonia.Input.IInputManager)]);
        }
        catch
        {
            return null;
        }
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

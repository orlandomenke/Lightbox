using Avalonia.Controls;
using Avalonia.Threading;

namespace Lightbox.App.Services;

/// <summary>
/// The order the splash and the main window change places in.
/// </summary>
/// <remarks>
/// Split out of <c>App</c> so the part with a rule in it is a pure function and
/// the part with an ordering in it can be driven by a test without a real main
/// window. Both halves have a failure that only shows up on a desktop, which is
/// exactly why they are worth pulling somewhere testable.
/// </remarks>
internal static class Startup
{
    /// <summary>How long the splash stays up once it is actually on screen.</summary>
    /// <remarks>
    /// Long enough to read as deliberate, short enough that nobody waits on it.
    /// A splash that appears and vanishes inside one frame reads as a glitch,
    /// which is worse than not having one.
    /// </remarks>
    public static readonly TimeSpan MinimumVisible = TimeSpan.FromMilliseconds(600);

    /// <summary>How much longer to hold the splash, given how long it has been visible.</summary>
    public static TimeSpan Remaining(TimeSpan visibleFor) =>
        visibleFor >= MinimumVisible ? TimeSpan.Zero : MinimumVisible - visibleFor;

    /// <summary>
    /// Wait until the splash has had a chance to paint, or give up.
    /// </summary>
    /// <remarks>
    /// The minimum is measured from here rather than from <c>Show()</c>, because
    /// "visible for 600 ms" is the promise and a splash that spent half of that
    /// unpainted has not kept it.
    ///
    /// <b>What this is not.</b> <c>RequestAnimationFrame</c> fires at the start
    /// of a tick, and Avalonia commits on a render thread — there is no public
    /// API in 12.1.1 that means "your pixels are on screen". A tick plus a
    /// yield below render priority is as close as it gets. The timeout is there
    /// because a window that never gets a render surface must not hang startup
    /// for good; it is a backstop, not the expected path.
    /// </remarks>
    public static async Task WhenPaintedAsync(Window splash)
    {
        var painted = new TaskCompletionSource();
        splash.RequestAnimationFrame(_ => painted.TrySetResult());
        await Task.WhenAny(painted.Task, Task.Delay(MinimumVisible));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    /// <summary>Splash down, main window up — in the one order that is safe.</summary>
    /// <remarks>
    /// <para>
    /// Every line here is load-bearing, and each one guards a failure that is
    /// invisible in review:
    /// </para>
    /// <para>
    /// <b>The main window is shown before the splash closes.</b> The classic
    /// desktop lifetime shuts down on the last window closing, which is its
    /// default. Close the splash first and the open-window count reaches zero
    /// between the two, so the app exits during its own startup — on Windows
    /// that looks like the app launching and instantly vanishing.
    /// </para>
    /// <para>
    /// <b>The splash is never assigned to <c>MainWindow</c>.</b> Code reads that
    /// property expecting the real thing — <c>MainViewModel.Symbols</c> pattern
    /// matches <c>app.MainWindow is Views.MainWindow</c> and silently falls back
    /// to a stored preference when it does not match. A splash parked there
    /// would not throw; it would quietly change behaviour.
    /// </para>
    /// <para>
    /// <b>The greeting comes last.</b> The start screen is a modal centred on
    /// its owner, so offering it while the splash is still up puts an orange
    /// panel over a dialog the artist cannot dismiss.
    /// </para>
    /// <para>
    /// <c>greet</c> is a parameter rather than a direct call for the reason the
    /// comment in <c>App</c> already gives for offering the start screen from
    /// there at all: a test has to be able to drive this sequence without a
    /// modal opening over it. <c>adopt</c> is a parameter for a second reason:
    /// Avalonia 12's lifetime interfaces are explicitly not implementable
    /// outside the framework, so taking the lifetime itself would have made
    /// this sequence untestable. Passing in the one thing it needs to do to the
    /// lifetime — hand it the window it should treat as the main one — keeps
    /// the ordering checkable and narrows what this can reach.
    /// </para>
    /// </remarks>
    public static async Task HandOffAsync(
        Window splash,
        Window main,
        Action<Window> adopt,
        Func<Task> greet)
    {
        adopt(main);
        main.Show();
        // Windows will otherwise sometimes leave the new window behind the one
        // that had focus. Cannot be verified from Linux; see MANUAL_TESTING.md.
        main.Activate();
        splash.Close();
        await greet();
    }
}

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The splash and the main window trade places in an order that has three ways
/// to go wrong, none of which is visible in review.
/// </summary>
/// <remarks>
/// <para>
/// Driven through <see cref="Startup.HandOffAsync"/> with two plain windows —
/// no <c>MainWindow</c>, no <c>MainViewModel</c>, no modal. The sequence is the
/// thing under test, and building the real window would only make it slower and
/// less able to fail for one reason.
/// </para>
/// <para>
/// <b>Why there is no stand-in lifetime here.</b> The first version of these
/// tests implemented <c>IClassicDesktopStyleApplicationLifetime</c>, which does
/// not compile: Avalonia 12 puts a deliberately unimplementable member on it and
/// says so in the error. That is what pushed <c>HandOffAsync</c> to take an
/// <c>adopt</c> delegate instead of the lifetime — the sequence became testable
/// and stopped being able to reach anything else at the same time.
/// </para>
/// <para>
/// <b>What is asserted and what is not.</b> These check the order of the two
/// windows and the greeting. They cannot check that the real lifetime would
/// have shut the application down on an empty window list — provoking that in a
/// test would take the test process with it — so the ordering is asserted as the
/// property that stops it ever happening.
/// </para>
/// </remarks>
public class StartupHandoffTests
{
    private sealed record Run(
        List<string> Order,
        List<Window> Adopted,
        Window Splash,
        Window Main,
        bool SplashWasOpenAtGreeting);

    private static async Task<Run> HandOff()
    {
        var splash = new Window();
        var main = new Window();
        var order = new List<string>();
        var adopted = new List<Window>();

        splash.Closed += (_, _) => order.Add("splash closed");
        main.Opened += (_, _) => order.Add("main opened");

        splash.Show();

        var splashOpenAtGreeting = true;
        await Startup.HandOffAsync(
            splash,
            main,
            w => { order.Add("adopted"); adopted.Add(w); },
            () =>
            {
                order.Add("greeted");
                splashOpenAtGreeting = splash.IsVisible;
                return Task.CompletedTask;
            });

        return new Run(order, adopted, splash, main, splashOpenAtGreeting);
    }

    [AvaloniaFact]
    public async Task TheMainWindowIsUpBeforeTheSplashGoesDown()
    {
        // The one that matters most. Closing the splash first empties the
        // window list, and the classic desktop lifetime shuts down on last
        // close by default — so the symptom on Windows is the app starting and
        // instantly vanishing, during its own startup.
        var run = await HandOff();
        var mainOpened = run.Order.IndexOf("main opened");
        var splashClosed = run.Order.IndexOf("splash closed");

        Assert.True(mainOpened >= 0, "the main window never opened");
        Assert.True(splashClosed >= 0, "the splash never closed");
        Assert.True(
            mainOpened < splashClosed,
            $"the splash closed before the main window opened: {string.Join(" → ", run.Order)}");
    }

    [AvaloniaFact]
    public async Task TheSplashIsNeverTheWindowTheApplicationAdopts()
    {
        // Code reads MainWindow expecting the real thing — MainViewModel.Symbols
        // pattern matches `app.MainWindow is Views.MainWindow` and falls back to
        // a stored preference when it does not match. A splash handed over here
        // would not throw; it would quietly change behaviour.
        var run = await HandOff();

        Assert.DoesNotContain(run.Splash, run.Adopted);
        Assert.Equal([run.Main], run.Adopted);
    }

    [AvaloniaFact]
    public async Task TheApplicationAdoptsTheMainWindowBeforeItIsShown()
    {
        // Anything reading MainWindow from an Opened handler has to find the
        // window that is opening, not null.
        var run = await HandOff();
        Assert.True(
            run.Order.IndexOf("adopted") < run.Order.IndexOf("main opened"),
            $"adopted after opening: {string.Join(" → ", run.Order)}");
    }

    [AvaloniaFact]
    public async Task TheStartScreenIsOfferedOnlyAfterTheSplashHasGone()
    {
        // The start screen is a modal centred on its owner. Offered while the
        // splash is still up, it is a panel over a dialog nobody can dismiss.
        var run = await HandOff();

        Assert.False(
            run.SplashWasOpenAtGreeting,
            "the splash was still up when the start screen was offered");
        Assert.True(
            run.Order.IndexOf("splash closed") < run.Order.IndexOf("greeted"),
            $"greeted before the splash closed: {string.Join(" → ", run.Order)}");
    }
}

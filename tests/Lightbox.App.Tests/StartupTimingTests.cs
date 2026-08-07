using Lightbox.App.Services;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// How long the splash stays up, as a rule rather than as a sleep in a view.
/// </summary>
/// <remarks>
/// Plain <see cref="FactAttribute"/> — nothing here touches Avalonia, so there
/// is no reason to run it inside the headless session. The rule is a pure
/// function of one <see cref="TimeSpan"/> precisely so it can be checked this
/// cheaply; the alternative is a <c>Task.Delay</c> buried in a startup path
/// that only a person on Windows can evaluate.
/// </remarks>
public class StartupTimingTests
{
    [Fact]
    public void AnInstantLoadStillShowsTheSplashForTheMinimum()
    {
        // The case the minimum exists for. A splash that appears and vanishes
        // inside one frame reads as a glitch rather than as a fast machine.
        Assert.Equal(Startup.MinimumVisible, Startup.Remaining(TimeSpan.Zero));
    }

    [Fact]
    public void APartLoadedSplashWaitsOutTheRemainder()
    {
        Assert.Equal(
            TimeSpan.FromMilliseconds(350),
            Startup.Remaining(TimeSpan.FromMilliseconds(250)));
    }

    [Fact]
    public void ASlowLoadIsNotPaddedFurther()
    {
        // The minimum is a floor, not a tax: a machine that took a second to
        // build the window has already shown the splash for a second.
        Assert.Equal(TimeSpan.Zero, Startup.Remaining(TimeSpan.FromMilliseconds(900)));
        Assert.Equal(TimeSpan.Zero, Startup.Remaining(Startup.MinimumVisible));
    }

    [Fact]
    public void TheMinimumIsLongEnoughToBeSeenAndShortEnoughNotToBeWaitedOn()
    {
        // The number has a reason, so a later edit to five seconds fails here
        // rather than in somebody's morning.
        Assert.InRange(Startup.MinimumVisible.TotalMilliseconds, 300, 1000);
    }
}

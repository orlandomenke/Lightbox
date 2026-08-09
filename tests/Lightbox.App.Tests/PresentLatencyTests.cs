using Lightbox.App.Rendering;
using Lightbox.App.Services;

namespace Lightbox.App.Tests;

/// <summary>
/// The publish-to-draw wait, and the environment switch that makes B150's fix
/// A/B-testable in one binary.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both of these exist because the bug they serve cannot be reproduced
/// here.</b> The container is headless and has no pointer, so the only honest
/// thing to build is the instrument — and an instrument that lies is worse than
/// none, which is what these check: that a frame nobody drew is counted as
/// dropped rather than fast, and that an unrecognised override is the default
/// rather than a crash.
/// </para>
/// <para>
/// Plain <c>[Fact]</c>: the tally is arithmetic behind a lock and the priority
/// switch reads an environment variable. Neither needs an Avalonia session, and
/// B93 is a good enough reason not to ask for one.
/// </para>
/// </remarks>
public class PresentLatencyTests(ITestOutputHelper output)
{
    [Fact]
    public void AFrameThatWasDrawnIsTimedFromWhenItWasPublished()
    {
        var latency = new PresentLatency();
        latency.Published(1);
        Thread.Sleep(12);
        latency.Rendered(1);

        var stats = latency.Snapshot;
        output.WriteLine($"{stats.Presented} drawn, mean {stats.MeanMs:F2} ms, worst {stats.WorstMs:F2} ms");

        Assert.Equal(1, stats.Presented);
        Assert.Equal(0, stats.Superseded);
        // Loose on purpose: the assertion is that the clock started at the
        // publish rather than at the render, not that Sleep is accurate.
        Assert.True(stats.MeanMs >= 8, $"mean was {stats.MeanMs} ms");
    }

    /// <summary>
    /// <b>The number that would otherwise flatter the average.</b> A frame
    /// replaced before anything drew it is the compositor falling behind, and
    /// counting only the frames that made it would report a machine dropping
    /// half its output as having a perfect mean.
    /// </summary>
    [Fact]
    public void AFrameReplacedBeforeAnythingDrewItIsCountedAsDropped()
    {
        var latency = new PresentLatency();
        for (var seq = 1; seq <= PresentLatency.Tracked * 2; seq++) latency.Published(seq);

        var stats = latency.Snapshot;
        output.WriteLine($"{PresentLatency.Tracked * 2} published, {stats.Presented} drawn, {stats.Superseded} replaced first");

        Assert.Equal(0, stats.Presented);
        Assert.Equal(PresentLatency.Tracked, stats.Superseded);
    }

    /// <summary>
    /// A render for a sequence the ring no longer holds must not be counted at
    /// all — inventing a wait for it, or crediting it to whatever is in that
    /// slot now, would put a number in the report that describes nothing.
    /// </summary>
    [Fact]
    public void ARenderForAFrameTheRingHasForgottenIsIgnored()
    {
        var latency = new PresentLatency();
        latency.Published(1);
        for (var seq = 2; seq <= PresentLatency.Tracked + 2; seq++) latency.Published(seq);

        latency.Rendered(1);

        Assert.Equal(0, latency.Snapshot.Presented);
    }

    [Fact]
    public void ResetPutsItBackToNothingMeasured()
    {
        var latency = new PresentLatency();
        latency.Published(1);
        latency.Rendered(1);
        latency.Reset();

        var stats = latency.Snapshot;
        Assert.Equal(0, stats.Presented);
        Assert.Equal(0, stats.MeanMs);
        Assert.Equal(0, stats.WorstMs);
    }

    // ---- the A/B switch --------------------------------------------------------

    /// <summary>
    /// <c>LIGHTBOX_CLOCK_PRIORITY=Background</c> puts B150's defect back on
    /// purpose, so the one variable can be flipped inside a single binary and a
    /// single session. "I installed a new build and it feels better" is not
    /// evidence — a new build changes a dozen things and a hand is a poor
    /// stopwatch.
    /// </summary>
    /// <remarks>
    /// The expected value arrives as a name rather than a
    /// <c>DispatcherPriority</c>: it is a struct in Avalonia 11 and later, so it
    /// cannot be an attribute argument.
    /// </remarks>
    [Theory]
    [InlineData("Background", "Background")]
    [InlineData("background", "Background")]
    [InlineData("  Input  ", "Input")]
    [InlineData("Loaded", "Loaded")]
    [InlineData("Render", "Render")]
    public void TheClockPriorityCanBeOverriddenForOneSession(string value, string expected)
    {
        var before = Environment.GetEnvironmentVariable("LIGHTBOX_CLOCK_PRIORITY");
        try
        {
            Environment.SetEnvironmentVariable("LIGHTBOX_CLOCK_PRIORITY", value);
            Assert.Equal(expected, PlaybackClock.PriorityFromEnvironment().ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("LIGHTBOX_CLOCK_PRIORITY", before);
        }
    }

    /// <summary>
    /// A typo is the default rather than a failure. This is meant to be typed at
    /// a command prompt by somebody chasing a stutter, and a misspelling that
    /// stopped the application starting would be a worse bug than the one it is
    /// being used to diagnose.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("Backgrund")]
    [InlineData("42")]
    public void AnUnrecognisedOverrideIsTheShippedDefault(string value)
    {
        var before = Environment.GetEnvironmentVariable("LIGHTBOX_CLOCK_PRIORITY");
        try
        {
            Environment.SetEnvironmentVariable("LIGHTBOX_CLOCK_PRIORITY", value);
            Assert.Equal(
                Avalonia.Threading.DispatcherPriority.Input,
                PlaybackClock.PriorityFromEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable("LIGHTBOX_CLOCK_PRIORITY", before);
        }
    }

    /// <summary>
    /// <b>The property the default is chosen for, rather than the value it
    /// happens to be.</b> The clock must queue BELOW the band Avalonia commits
    /// frames in, because a late tick is absorbed by <c>Pace</c> and a late
    /// commit is the stutter itself.
    /// </summary>
    /// <remarks>
    /// Asserted as an ordering because that is the argument. B150 moved the
    /// clock up from <c>Background</c> — correctly — and kept going to
    /// <c>Render</c>, which is level with the commit, so an overdue tick queues
    /// ahead of the commit the previous tick asked for. A test that only said
    /// <c>== Input</c> would be satisfied by the next person moving it back and
    /// changing the number here.
    /// </remarks>
    [Fact]
    public void TheClockQueuesBelowTheBandFramesAreCommittedIn()
    {
        var before = Environment.GetEnvironmentVariable("LIGHTBOX_CLOCK_PRIORITY");
        try
        {
            Environment.SetEnvironmentVariable("LIGHTBOX_CLOCK_PRIORITY", null);
            var clock = PlaybackClock.PriorityFromEnvironment();

            Assert.True(clock < Avalonia.Threading.DispatcherPriority.Render,
                $"the clock runs at {clock}, at or above the priority frames are committed at — "
                + "an overdue tick will queue ahead of the commit it asked for");
            Assert.True(clock > Avalonia.Threading.DispatcherPriority.Background,
                $"the clock runs at {clock}, back in the band B150 rescued it from");
        }
        finally
        {
            Environment.SetEnvironmentVariable("LIGHTBOX_CLOCK_PRIORITY", before);
        }
    }

    /// <summary>
    /// Unset is the default too, which is the case every artist who never types
    /// the variable is in.
    /// </summary>
    [Fact]
    public void NoOverrideIsTheShippedDefault()
    {
        var before = Environment.GetEnvironmentVariable("LIGHTBOX_CLOCK_PRIORITY");
        try
        {
            Environment.SetEnvironmentVariable("LIGHTBOX_CLOCK_PRIORITY", null);
            Assert.Equal(
                Avalonia.Threading.DispatcherPriority.Input,
                PlaybackClock.PriorityFromEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable("LIGHTBOX_CLOCK_PRIORITY", before);
        }
    }
}

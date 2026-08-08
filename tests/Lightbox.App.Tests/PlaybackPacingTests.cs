using Lightbox.App.Services;

namespace Lightbox.App.Tests;

/// <summary>
/// Playback keeps time against a wall clock, so a slow tick costs its own frame
/// rather than every frame after it.
/// </summary>
/// <remarks>
/// <para>
/// <b>These test arithmetic on purpose.</b> The defect they guard is a timing
/// one, and the obvious test — run the real clock, measure the gaps — cannot be
/// written honestly here: the headless dispatcher does not deliver timer ticks
/// in real time, so it would be measuring the harness and would pass on a build
/// where pacing was removed entirely. <see cref="PlaybackClock.Pace"/> is pure
/// for exactly this reason.
/// </para>
/// <para>
/// The property that matters throughout: <b>the next frame is due a whole
/// period after the last one was due, never a period after now.</b> That single
/// choice is the difference between errors cancelling and errors accumulating.
/// </para>
/// </remarks>
public class PlaybackPacingTests(ITestOutputHelper output)
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(1000.0 / 12);

    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void AnOnTimeTickAdvancesOneFrameAndWaitsAWholePeriod()
    {
        var plan = PlaybackClock.Pace(Frame, Frame, Frame);

        Assert.Equal(1, plan.Frames);
        Assert.Equal(2 * Frame, plan.NextFrameDue);
        Assert.Equal(Frame, plan.Delay);
    }

    [Fact]
    public void AnEarlyTickAdvancesNothingAndWaitsOutTheRemainder()
    {
        // A timer that fires early must not run the scene fast — the mirror of
        // the late case and just as wrong.
        var plan = PlaybackClock.Pace(Ms(50), Frame, Frame);

        Assert.Equal(0, plan.Frames);
        Assert.Equal(Frame, plan.NextFrameDue);
        Assert.Equal(33.3, plan.Delay.TotalMilliseconds, 1);
    }

    /// <summary>
    /// The defect this class was written for: the tick's own cost has to come
    /// out of the interval, not be added to it.
    /// </summary>
    [Fact]
    public void ALateTickShortensTheNextIntervalInsteadOfShiftingEveryFrameAfterIt()
    {
        // Due at 83.3, delivered at 103.3 — a 20 ms frame's worth of work.
        var plan = PlaybackClock.Pace(Ms(103.3), Frame, Frame);

        Assert.Equal(1, plan.Frames);
        // Still due a whole period from the DUE time, not from now.
        Assert.Equal(2 * Frame, plan.NextFrameDue);
        // So the wait is what remains: 166.7 - 103.3.
        Assert.InRange(plan.Delay.TotalMilliseconds, 63.0, 63.7);

        output.WriteLine($"20 ms late -> next delay {plan.Delay.TotalMilliseconds:0.0} ms (period holds at 83.3)");
    }

    /// <summary>
    /// The same run, tick after tick: the old clock lost 20 ms every frame and
    /// never got it back. This measures the gap between frames, which is what
    /// an eye actually reads.
    /// </summary>
    /// <remarks>
    /// The number to beat is the old behaviour, computed rather than asserted:
    /// a timer re-armed at 83.3 ms after a 20 ms handler produces a 103.3 ms
    /// period — 9.7 fps for a scene marked 12, with every frame held a little
    /// too long. Both numbers are printed, because "the assertion passed" is
    /// not the same as knowing the two are far apart.
    /// </remarks>
    [Fact]
    public void TwentyMsOfWorkEveryFrameDoesNotStretchTheFramePeriod()
    {
        // The timer's first fire lands one interval in, not at zero.
        var elapsed = Frame;
        var due = Frame;
        var shownAt = new List<double>();

        for (var i = 0; i < 24; i++)
        {
            var plan = PlaybackClock.Pace(elapsed, due, Frame);
            for (var f = 0; f < plan.Frames; f++) shownAt.Add(elapsed.TotalMilliseconds);
            due = plan.NextFrameDue;
            // The dispatcher waits the delay it was handed, then the handler
            // costs 20 ms recompositing and publishing.
            elapsed += plan.Delay + Ms(20);
        }

        var gaps = new List<double>();
        for (var i = 1; i < shownAt.Count; i++) gaps.Add(shownAt[i] - shownAt[i - 1]);
        var mean = gaps.Average();

        output.WriteLine(
            $"paced: {gaps.Count + 1} frames, mean period {mean:0.0} ms ({1000 / mean:0.0} fps). "
            + $"Unpaced would be {Frame.TotalMilliseconds + 20:0.0} ms "
            + $"({1000 / (Frame.TotalMilliseconds + 20):0.0} fps).");

        // Close to the 83.3 ms the scene asks for, not the 103.3 ms that
        // counting ticks produces.
        Assert.InRange(mean, 82.3, 85.0);

        // The period is STEADY, which is the half that reads as stutter: no
        // frame is held meaningfully longer than any other.
        //
        // Measured from the SECOND gap, because the first is a real one-frame
        // transient rather than a rounding artefact: the clock starts with a
        // whole period on the timer, and that first tick's work lands on top of
        // it before pacing has anything to correct against. One stretched frame
        // at the moment play is pressed; every frame after it is on the beat.
        // Asserted with the transient excluded and printed with it included, so
        // the exclusion is visible rather than convenient.
        var steady = gaps.Skip(1).ToList();
        output.WriteLine(
            $"first gap {gaps[0]:0.0} ms (startup), steady {steady.Min():0.0}-{steady.Max():0.0} ms");
        Assert.InRange(steady.Max() - steady.Min(), 0, 1.0);
    }

    [Fact]
    public void AStallLongerThanAFrameOwesTheFramesItMissed()
    {
        // Due at 83.3, delivered at 250 — two whole extra periods have passed.
        var plan = PlaybackClock.Pace(Ms(250), Frame, Frame);

        Assert.Equal(3, plan.Frames);
        output.WriteLine($"250 ms elapsed against an 83.3 ms frame -> {plan.Frames} frames owed");
    }

    [Fact]
    public void ALongStallIsCappedAndResynchronisedRatherThanRacedThrough()
    {
        // Two seconds gone — a breakpoint, a save dialog, a lid. Advancing 24
        // frames to "catch up" would race visibly through the scene.
        var plan = PlaybackClock.Pace(Ms(2000), Frame, Frame);

        Assert.Equal(PlaybackClock.MaxCatchUpFrames, plan.Frames);
        // Re-based on now, so the next frame is due one period from here rather
        // than still two seconds in the past.
        Assert.Equal(Ms(2000) + Frame, plan.NextFrameDue);
        Assert.Equal(Frame, plan.Delay);
    }

    /// <summary>
    /// A non-positive interval starves the dispatcher, which would freeze the
    /// window this class exists to keep smooth.
    /// </summary>
    [Fact]
    public void TheDelayIsNeverZeroOrNegativeHoweverFarBehindTheClockIs()
    {
        foreach (var late in new[] { 83.4, 100.0, 500.0, 5000.0 })
        {
            var plan = PlaybackClock.Pace(Ms(late), Frame, Frame);
            Assert.True(
                plan.Delay > TimeSpan.Zero,
                $"{late} ms late produced a {plan.Delay.TotalMilliseconds} ms interval");
        }
    }

    [Fact]
    public void AZeroFrameDurationFallsBackRatherThanDividingByZero()
    {
        var plan = PlaybackClock.Pace(Ms(100), Ms(83), TimeSpan.Zero);
        Assert.True(plan.Frames >= 1);
        Assert.True(plan.Delay > TimeSpan.Zero);
    }

    [Theory]
    [InlineData(12, 100)]
    [InlineData(24, 100)]
    [InlineData(12, 50)]
    [InlineData(25, 200)]
    public void TheFramePeriodStillFollowsFpsAndSpeed(int fps, int speed)
    {
        var expected = 1000.0 / (fps * speed / 100.0);
        Assert.Equal(expected, PlaybackClock.IntervalFor(fps, speed).TotalMilliseconds, 3);
    }
}

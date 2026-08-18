using Lightbox.App.Rendering;

namespace Lightbox.App.Tests;

/// <summary>
/// Reading the pen's tilt and estimating the hand's speed.
/// </summary>
/// <remarks>
/// The two axes a stroke can record beside pressure, tested where the
/// arithmetic is rather than through a window: this repository has no pen, no
/// tablet and no Windows, so everything that could be got wrong lives in
/// <see cref="PenAxes"/> precisely so it can be checked without them.
/// </remarks>
public class PenAxesTests
{
    // ---- tilt ---------------------------------------------------------------------

    [Fact]
    public void AMouseHasNoTiltAxisAtAll()
    {
        var axes = new PenAxes();
        axes.Begin();

        Assert.Equal((null, null), axes.TiltFor(isPen: false, 30, 20));
    }

    [Fact]
    public void APenThatHasReportedNothingRecordsNoTilt()
    {
        // The distinction the whole method exists for: a device with no tilt
        // sensor sends 0,0 — and so does a perfectly upright pen. One means
        // "no information" and must stay out of the record; recording it would
        // be a claim the device never made.
        var axes = new PenAxes();
        axes.Begin();

        Assert.Equal((null, null), axes.TiltFor(isPen: true, 0, 0));
    }

    [Fact]
    public void OnceAPenHasTiltedTheStrokeRecordsItIncludingBackAtUpright()
    {
        var axes = new PenAxes();
        axes.Begin();

        Assert.Equal(((double?)30, (double?)-20), axes.TiltFor(isPen: true, 30, -20));
        // Now that the device has proved it reports tilt, an upright sample is
        // real information — the artist straightened the pen — and belongs in
        // the record rather than being read as "unknown" again.
        Assert.Equal(((double?)0, (double?)0), axes.TiltFor(isPen: true, 0, 0));
    }

    [Fact]
    public void TiltIsRoundedAndClamped()
    {
        var axes = new PenAxes();
        axes.Begin();

        // One decimal, so the file carries no float noise, and never outside
        // the range the axis is defined on.
        Assert.Equal(((double?)12.3, (double?)-45.7), axes.TiltFor(isPen: true, 12.34567, -45.6789));
        Assert.Equal(((double?)90, (double?)-90), axes.TiltFor(isPen: true, 400, -400));
    }

    [Fact]
    public void BeginForgetsThatTheLastStrokeHadTilted()
    {
        var axes = new PenAxes();
        axes.Begin();
        axes.TiltFor(isPen: true, 30, 20);

        axes.Begin();
        // A fresh stroke starts from "this device has told me nothing", or a
        // pen swapped for one without a tilt sensor would keep recording zeros
        // as though they were measurements.
        Assert.Equal((null, null), axes.TiltFor(isPen: true, 0, 0));
    }

    // ---- speed --------------------------------------------------------------------

    [Fact]
    public void AStrokeStartsFromRest()
    {
        var axes = new PenAxes();
        axes.Begin();

        // The first sample has nothing to measure against. Inventing a speed
        // would put a mark on the paper the hand did not make.
        Assert.Equal(0, axes.SpeedFor(10, 10, 8));
    }

    [Fact]
    public void SamplesSharingATimestampHoldTheEstimateRatherThanDividingByZero()
    {
        var axes = new PenAxes();
        axes.Begin();
        axes.SpeedFor(0, 0, 8);

        // Coalesced intermediate points share the event's timestamp — verified
        // against the shipped Avalonia assembly — so this is the ordinary case
        // rather than a defensive one.
        var held = axes.SpeedFor(50, 0, 0);
        Assert.InRange(held, 0, 1);
    }

    [Fact]
    public void MovingFasterRecordsMoreSpeed()
    {
        static double After(double pxPerMs)
        {
            var axes = new PenAxes();
            axes.Begin();
            var v = 0.0;
            // Long enough for the average to settle at the input.
            for (var i = 0; i < 60; i++) v = axes.SpeedFor(pxPerMs * 8, 0, 8);
            return v;
        }

        var slow = After(0.15);
        var brisk = After(0.75);
        var fast = After(PenAxes.FullSpeedPxPerMs);

        // Printed rather than merely asserted, so a change that flattens the
        // scale shows as numbers instead of a passing comparison.
        Assert.True(slow < brisk && brisk < fast, $"slow {slow:F3}, brisk {brisk:F3}, fast {fast:F3}");
        // And the reference speed really is the top of the scale.
        Assert.InRange(fast, 0.95, 1.0);
        // A gentle hand must not already be pinned — an axis that reads 1 for
        // everything drives nothing.
        Assert.InRange(slow, 0.05, 0.25);
    }

    [Fact]
    public void SpeedNeverLeavesItsRange()
    {
        var axes = new PenAxes();
        axes.Begin();
        axes.SpeedFor(0, 0, 8);

        for (var i = 0; i < 20; i++)
        {
            // Far past the reference speed: a flick, or a jump after the pen
            // was lifted and put down somewhere else.
            Assert.InRange(axes.SpeedFor(9000, 9000, 8), 0, 1);
        }
    }

    [Fact]
    public void TheAverageFollowsTheHandRatherThanLaggingForever()
    {
        var axes = new PenAxes();
        axes.Begin();
        axes.SpeedFor(0, 0, 8);
        for (var i = 0; i < 60; i++) axes.SpeedFor(PenAxes.FullSpeedPxPerMs * 8, 0, 8);

        // Stop dead. After about three half-lives the estimate should have
        // given up most of its speed — a smoothing that never forgets would
        // keep a stroke "fast" long after the hand stopped.
        double v = 1;
        for (var i = 0; i < 12; i++) v = axes.SpeedFor(0, 0, 8);
        Assert.True(v < 0.15, $"still reading {v:F3} after stopping");
    }

    [Fact]
    public void BeginForgetsTheLastStrokesSpeed()
    {
        var axes = new PenAxes();
        axes.Begin();
        axes.SpeedFor(0, 0, 8);
        for (var i = 0; i < 40; i++) axes.SpeedFor(PenAxes.FullSpeedPxPerMs * 8, 0, 8);

        axes.Begin();
        // A new stroke starts from rest however fast the previous one ended,
        // or the first dab of a careful line inherits the last flick.
        Assert.Equal(0, axes.SpeedFor(10, 10, 8));
    }
}

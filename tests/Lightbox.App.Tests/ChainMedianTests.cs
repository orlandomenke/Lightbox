using Lightbox.App.Rendering;
using Lightbox.App.Services;

namespace Lightbox.App.Tests;

/// <summary>
/// Every phase of the pen-to-screen chain carries a median, and a mean that
/// cannot be trusted says so (B330).
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Tally"/> was written after a mean lied twice in one day, and
/// then applied everywhere except here.</b> Its own remarks record the incident:
/// a capture read <c>building each frame 8.27 ms, describing it 5.34</c> and the
/// report concluded the cost was in the pass list — when one 2,062 ms stall was
/// the whole figure and the true cost was 0.16 ms. The frame build gained a
/// median from that. <c>StrokeToScreen.Segment</c> did not, so the five numbers
/// an artist's lag is actually diagnosed from stayed mean-and-worst.
/// </para>
/// <para>
/// These assert the instrument rather than the code behind it, which is the rule
/// this session earned the hard way: five measurement errors, every one of them
/// passing a suite that tested the code and not the numbers describing it.
/// </para>
/// </remarks>
public class ChainMedianTests(ITestOutputHelper output)
{
    /// <summary>
    /// A stall must not be able to move the median. Ninety events at 2 ms and
    /// one at two seconds is the shape of every capture this session produced.
    /// </summary>
    [Fact]
    public void EveryPhaseOfTheChainCarriesAMedian()
    {
        var t = new Tally();
        for (var i = 0; i < 90; i++) t.Add(2.0);
        t.Add(2000.0);

        var seg = new StrokeToScreen.Segment(91, t.MeanMs, t.WorstMs, t.MedianMs, t.MeanIsDistorted);
        output.WriteLine($"median {seg.MedianMs:0.##}, mean {seg.MeanMs:0.##}, worst {seg.WorstMs:0.##}");

        Assert.Equal(2.0, seg.MedianMs, 6);
        Assert.True(seg.MeanMs > 20, $"the mean should carry the stall, it was {seg.MeanMs}");
        Assert.Equal(2000.0, seg.WorstMs, 6);
    }

    /// <summary>
    /// <b>And the report has to say which line is lying, not leave a reader to
    /// divide.</b> Every conclusion drawn from a mean this session had to be
    /// re-checked against something that happened to disagree with it; the
    /// warning is what removes the "happened to".
    /// </summary>
    [Fact]
    public void ADistortedMeanIsNamedRatherThanLeftToBeNoticed()
    {
        var stalled = new Tally();
        for (var i = 0; i < 90; i++) stalled.Add(2.0);
        stalled.Add(2000.0);

        var steady = new Tally();
        for (var i = 0; i < 90; i++) steady.Add(2.0);

        output.WriteLine(
            $"stalled: mean {stalled.MeanMs:0.##} median {stalled.MedianMs:0.##} distorted {stalled.MeanIsDistorted}; "
            + $"steady: mean {steady.MeanMs:0.##} median {steady.MedianMs:0.##} distorted {steady.MeanIsDistorted}");

        Assert.True(stalled.MeanIsDistorted, "a mean 10x its median was not flagged");
        Assert.False(steady.MeanIsDistorted, "a steady phase was flagged, so the warning means nothing");
    }

    /// <summary>
    /// A phase nothing has been recorded for reports zeroes rather than
    /// inventing a fast one — the same rule <see cref="Tally.MedianMs"/> states,
    /// checked here because a zero median printed first in the line is the one
    /// most likely to be read as a measurement.
    /// </summary>
    [Fact]
    public void AnUnmeasuredPhaseIsZeroRatherThanFast()
    {
        var seg = new StrokeToScreen.Segment(0, 0, 0, 0, false);
        Assert.Equal(0, seg.MedianMs);
        Assert.Equal(0, seg.MeanMs);
        Assert.False(seg.MeanIsDistorted);
    }
}

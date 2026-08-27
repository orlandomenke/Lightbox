using Lightbox.App.Services;

namespace Lightbox.App.Tests;

/// <summary>
/// The distribution behind a timing, and specifically that a stall cannot make
/// it describe a frame that never happened.
/// </summary>
/// <remarks>
/// Written from a real misreading: a capture of 2026-08-26 recorded 381 frame
/// builds whose typical cost was 3.2 ms and one that took 2,062 ms, and the
/// mean of 8.27 ms led the report to name the wrong phase as the cost, in a
/// sentence confident enough to have been acted on.
/// </remarks>
public class TallyTests(ITestOutputHelper output)
{
    private static Tally Of(params double[] values)
    {
        var t = new Tally();
        foreach (var v in values) t.Add(v);
        return t;
    }

    [Fact]
    public void OneStallMovesTheMeanAndNotTheMedian()
    {
        var t = new Tally();
        for (var i = 0; i < 380; i++) t.Add(3.2);
        t.Add(2062.57);

        output.WriteLine($"mean {t.MeanMs:0.##}  median {t.MedianMs:0.##}  worst {t.WorstMs:0.##}");
        Assert.True(t.MeanMs > 8, $"the mean should carry the stall, and reads {t.MeanMs:0.##}");
        Assert.Equal(3.2, t.MedianMs, 3);
        Assert.Equal(2062.57, t.WorstMs, 3);
        Assert.True(t.MeanIsDistorted, "a 2-second stall in a 3 ms distribution went unflagged");
    }

    /// <summary>
    /// And it must not cry wolf: a steady distribution has to read as steady,
    /// or the flag is noise and gets ignored the one time it matters.
    /// </summary>
    [Fact]
    public void ASteadyDistributionIsNotFlagged()
    {
        var t = new Tally();
        for (var i = 0; i < 100; i++) t.Add(3.0 + (i % 5) * 0.2);
        output.WriteLine($"mean {t.MeanMs:0.##}  median {t.MedianMs:0.##}");
        Assert.False(t.MeanIsDistorted);
    }

    /// <summary>Too few samples to judge is not the same as judged clean.</summary>
    [Fact]
    public void ItWillNotJudgeAHandfulOfSamples()
    {
        Assert.False(Of(1, 1, 900).MeanIsDistorted);
        Assert.Equal(0, new Tally().MedianMs);
        Assert.Equal(0, new Tally().MeanMs);
    }

    [Fact]
    public void TheMedianIsTheMiddleOfAnEvenCountToo()
    {
        var t = Of(4, 1, 3, 2);
        Assert.Equal(2.5, t.MedianMs, 6);
        Assert.Equal(2.5, t.MeanMs, 6);
        Assert.Equal(4, t.WorstMs, 6);
    }

    /// <summary>
    /// Past the reservoir's capacity the median must still describe the whole
    /// session rather than its opening seconds — a long stroke is exactly when
    /// a degradation would appear, and keeping only the first samples would
    /// hide it.
    /// </summary>
    [Fact]
    public void ALongSessionIsSampledThroughout()
    {
        var t = new Tally();
        for (var i = 0; i < 8192; i++) t.Add(1.0);      // fills the reservoir
        for (var i = 0; i < 24576; i++) t.Add(9.0);     // three times as many, later

        output.WriteLine($"count {t.Count}  mean {t.MeanMs:0.##}  median {t.MedianMs:0.##}");
        Assert.Equal(32768, t.Count);
        Assert.Equal(7.0, t.MeanMs, 6);
        // Three quarters of the session ran at 9, so the middle sample must be
        // 9. Keeping the first 8192 would have said 1 — the opening seconds
        // reported as the whole session.
        Assert.Equal(9.0, t.MedianMs, 6);
    }

    [Fact]
    public void NonsenseIsIgnoredRatherThanRecorded()
    {
        var t = Of(1, 2);
        t.Add(double.NaN);
        t.Add(double.PositiveInfinity);
        Assert.Equal(2, t.Count);
        Assert.Equal(1.5, t.MeanMs, 6);
    }
}

using Lightbox.App.Services;

namespace Lightbox.App.Tests;

/// <summary>
/// The headroom band names a cause only from what it measured (the owner's
/// report, 2026-10-07; the decision: name measured causes only).
/// </summary>
/// <remarks>
/// It used to advise from a checklist — more than six layers meant "merging
/// finished layers frees the most" — and it was fed the compositing time alone,
/// so it could not see the drawings being rendered that the performance lab
/// found were the real cost on the owner's eleven-layer document.
/// </remarks>
public class HeadroomAdviceTests
{
    private static PerformanceMonitor Monitor(int layers = 11)
    {
        var m = new PerformanceMonitor();
        m.DescribeDocument(1920, 1080, layers, 64, 0);
        return m;
    }

    /// <summary>A heavy median (60 ms: 11% headroom, "Struggling"), so the band asks for attention.</summary>
    private static void Heavy(PerformanceMonitor m)
    {
        for (var i = 0; i < 12; i++)
        {
            m.RecordPublish(60);
            m.RecordFrame(5);
        }
    }

    private static PerformanceMonitor.BuildSample Build(
        double total, double describe = 0, double compose = 0, long misses = 0, long evictions = 0, double at = 0) =>
        new(total, describe, compose, total - describe - compose, misses, evictions, at);

    /// <summary>The owner's document: the time went on rendering drawings, and the band says so.</summary>
    [Fact]
    public void RenderingDrawingsIsNamedAndMergingIsNotSuggested()
    {
        var m = Monitor();
        for (var i = 0; i < 5; i++) m.RecordBuild(Build(1200, describe: 1100, compose: 60, misses: 10, at: i));
        Heavy(m);

        Assert.True(m.NeedsAttention);
        Assert.Contains("rendering 50 drawings", m.Advice);
        Assert.Contains("the first look at them", m.Advice);
        Assert.DoesNotContain("merging", m.Advice);
    }

    [Fact]
    public void AFullCacheIsTheRemedyOnlyWhenTheCacheIsEvicting()
    {
        var m = Monitor();
        for (var i = 0; i < 5; i++) m.RecordBuild(Build(900, describe: 850, compose: 30, misses: 8, evictions: 8, at: i));
        Heavy(m);

        Assert.Contains("frame cache is full", m.Advice);
    }

    [Fact]
    public void CompositingIsNamedWithItsMeasuredTimeAndOnlyThenIsMergingSuggested()
    {
        var m = Monitor();
        for (var i = 0; i < 5; i++) m.RecordBuild(Build(48, describe: 4, compose: 40, at: i));
        Heavy(m);

        Assert.Contains("Compositing 11 layers takes 40 ms", m.Advice);
        Assert.Contains("merging finished layers", m.Advice);
    }

    [Fact]
    public void DisplayingIsNamedWhenTheFrameTimeIsTheCost()
    {
        var m = Monitor();
        for (var i = 0; i < 12; i++)
        {
            m.RecordPublish(30);
            m.RecordFrame(90);
        }

        Assert.Contains("Displaying the 2.1 MP canvas costs 90 ms", m.Advice);
    }

    /// <summary>A pause is worth saying whatever the median says.</summary>
    [Fact]
    public void PausesAreCountedAndRaisedEvenWithAHealthyMedian()
    {
        var m = Monitor();
        for (var i = 0; i < 12; i++)
        {
            m.RecordPublish(2);
            m.RecordFrame(2);
        }
        Assert.False(m.NeedsAttention);

        for (var i = 0; i < 3; i++) m.RecordBuild(Build(400, describe: 380, misses: 4, at: i));

        Assert.Equal(3, m.FreezesLastMinute);
        Assert.True(m.NeedsAttention);
        Assert.StartsWith("3 pauses over 0.1 s in the last minute.", m.Advice);
    }

    /// <summary>Pauses older than a minute stop counting.</summary>
    [Fact]
    public void PausesFallOutOfTheWindow()
    {
        var m = Monitor();
        for (var i = 0; i < 3; i++) m.RecordBuild(Build(400, describe: 380, misses: 4, at: i));
        m.RecordBuild(Build(5, at: 120));

        Assert.Equal(0, m.FreezesLastMinute);
    }

    /// <summary>No cause owns the time: the band says how slow, and where to look — not a guess.</summary>
    [Fact]
    public void WithNoSingleCauseTheBandPointsToTheReportInsteadOfGuessing()
    {
        var m = Monitor();
        for (var i = 0; i < 5; i++) m.RecordBuild(Build(60, describe: 20, compose: 20, at: i));
        Heavy(m);

        Assert.Contains("no single cause", m.Advice);
        Assert.Contains("render report", m.Advice);
        Assert.DoesNotContain("merging", m.Advice);
    }
}

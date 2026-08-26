using Lightbox.App.Rendering;
using Lightbox.App.Services;

namespace Lightbox.App.Tests;

/// <summary>
/// The pen→screen instrument (B189): what it counts, what it attributes, and
/// what the report says about the numbers.
/// </summary>
/// <remarks>
/// <para>
/// <b>These pin the instrument, not the lag.</b> The fault this exists for only
/// happens on a real dispatcher under real input — the headless dispatcher does
/// not deliver frames in real time, so a measured wait here would be measuring
/// the harness, the same reason <c>PresentWaitByInputTests</c> gives. What can
/// be pinned is that a capture taken on the owner's machine will be attributed
/// and worded correctly, which is the part a wrong instrument wastes a round
/// trip on.
/// </para>
/// <para>
/// The wording tests build the stats by hand at the shapes the verdicts fire
/// on, exactly as the present-wait split's do, so the sentence that names a
/// segment is pinned to the numbers that mean it.
/// </para>
/// </remarks>
public class StrokeToScreenTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "lightbox-stroke-latency-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        DiagnosticLog.DirectoryOverride = null;
        RenderReport.ResetForTests();
        try { Directory.Delete(_dir, recursive: true); } catch { /* a temp dir is not worth failing over */ }
    }

    /// <summary>
    /// B73 made one publish cover a burst of events on purpose; the instrument
    /// has to count that as one publish carrying three events, anchored to the
    /// oldest of them, or the coalescing reads as dropped ink.
    /// </summary>
    [Fact]
    public void ABurstOfEventsCoalescesIntoOnePublishAnchoredToTheOldest()
    {
        var s = new StrokeToScreen();

        var first = StrokeToScreen.EventArrived();
        s.Stamped(first);
        s.Stamped(StrokeToScreen.EventArrived());
        s.Stamped(StrokeToScreen.EventArrived());
        s.Published(seq: 7);
        s.Rendered(seq: 7);

        var stats = s.Snapshot;
        output.WriteLine($"events {stats.Events}, publishes {stats.Publishes}, drawn {stats.Drawn}");
        Assert.Equal(3, stats.Events);
        Assert.Equal(1, stats.Publishes);
        Assert.Equal(1, stats.Drawn);
        Assert.Equal(0, stats.Superseded);
        // Anchored to the oldest event: the total can only be at least the
        // publish wait, never less, because they start from the same instant.
        Assert.True(stats.PenToScreen.MeanMs >= stats.WaitToPublish.MeanMs);
    }

    /// <summary>
    /// The correction B189's second capture demanded: the oldest anchor is
    /// coalescing depth times staleness, the newest is staleness alone, and a
    /// burst spread over real time must show the newest anchor younger than
    /// the oldest — otherwise deeper coalescing reads as more lag by
    /// construction, which is exactly how that capture misread itself.
    /// </summary>
    [Fact]
    public void TheNewestAnchorIsYoungerThanTheOldestAcrossASpreadBurst()
    {
        var s = new StrokeToScreen();

        s.Stamped(StrokeToScreen.EventArrived());
        // A real gap, so old and new anchors measurably differ. These tests
        // already measure wall time; 25 ms is cheap and unambiguous.
        Thread.Sleep(25);
        s.Stamped(StrokeToScreen.EventArrived());
        s.Published(seq: 3);
        s.Rendered(seq: 3);

        var stats = s.Snapshot;
        output.WriteLine(
            $"oldest->publish {stats.WaitToPublish.MeanMs:0.##} ms, "
            + $"newest->publish {stats.TipToPublish.MeanMs:0.##} ms, "
            + $"pen->screen {stats.PenToScreen.MeanMs:0.##} ms, "
            + $"tip->screen {stats.TipToScreen.MeanMs:0.##} ms");
        Assert.True(stats.WaitToPublish.MeanMs >= 20, "the spread did not register on the oldest anchor");
        Assert.True(stats.TipToPublish.MeanMs < stats.WaitToPublish.MeanMs,
            "the newest anchor is not younger than the oldest — the two are still one number");
        Assert.True(stats.TipToScreen.MeanMs < stats.PenToScreen.MeanMs);
        Assert.Equal(stats.Drawn, stats.TipToScreen.Count);
        Assert.Equal(stats.Publishes, stats.TipToPublish.Count);
    }

    /// <summary>
    /// Publishes with no stroke behind them — playback, thumbnails, a fill —
    /// must record nothing, or every capture is polluted by whatever else the
    /// session did and the section stops being about drawing.
    /// </summary>
    [Fact]
    public void APublishWithNoInkPendingRecordsNothing()
    {
        var s = new StrokeToScreen();

        s.Published(seq: 1);
        s.Rendered(seq: 1);

        Assert.Equal(default, s.Snapshot);
    }

    /// <summary>One stamped burst is consumed by one publish, not by every publish after it.</summary>
    [Fact]
    public void ASecondPublishWithoutNewEventsAttachesNothing()
    {
        var s = new StrokeToScreen();

        s.Stamped(StrokeToScreen.EventArrived());
        s.Published(seq: 1);
        s.Published(seq: 2);
        s.Rendered(seq: 2);

        var stats = s.Snapshot;
        Assert.Equal(1, stats.Publishes);
        // Seq 2 carried nothing, so its draw completes no chain.
        Assert.Equal(0, stats.Drawn);
    }

    /// <summary>
    /// Ink published and then replaced before anything drew it is the artist's
    /// lag made countable — it must be tallied, not silently forgotten when the
    /// ring slot is reused.
    /// </summary>
    [Fact]
    public void InkReplacedBeforeDrawingIsCountedAsSuperseded()
    {
        var s = new StrokeToScreen();

        for (var seq = 1; seq <= StrokeToScreen.Tracked + 1; seq++)
        {
            s.Stamped(StrokeToScreen.EventArrived());
            s.Published(seq);
        }

        var stats = s.Snapshot;
        output.WriteLine($"publishes {stats.Publishes}, superseded {stats.Superseded}");
        Assert.Equal(StrokeToScreen.Tracked + 1, stats.Publishes);
        Assert.Equal(1, stats.Superseded);
    }

    [Fact]
    public void ResetClearsEverythingIncludingThePendingBurst()
    {
        var s = new StrokeToScreen();
        s.Stamped(StrokeToScreen.EventArrived());
        s.Reset();
        s.Published(seq: 1);
        s.Rendered(seq: 1);

        Assert.Equal(default, s.Snapshot);
    }

    // ---- the report's wording, pinned to the shapes that fire it ----------

    private static StrokeToScreen.Stats Stats(
        double stampMean, double toPublishMean, double toDrawMean,
        int drawn = 100, int superseded = 0, double? tipToScreenMean = null) =>
        new(Events: drawn * 2, Publishes: drawn + superseded, Drawn: drawn,
            Superseded: superseded,
            Stamp: new(drawn * 2, stampMean, stampMean * 3),
            WaitToPublish: new(drawn + superseded, toPublishMean, toPublishMean * 3),
            WaitToDraw: new(drawn, toDrawMean, toDrawMean * 3),
            PenToScreen: new(drawn, toPublishMean + toDrawMean, (toPublishMean + toDrawMean) * 3),
            TipToPublish: new(drawn + superseded, toPublishMean / 2, toPublishMean * 1.5),
            TipToScreen: new(
                drawn,
                tipToScreenMean ?? toPublishMean / 2 + toDrawMean,
                (tipToScreenMean ?? toPublishMean / 2 + toDrawMean) * 3));

    private string Section(
        StrokeToScreen.Stats? stats,
        (int Passes, double TotalMs, double WorstMs)? livePost = null)
    {
        Directory.CreateDirectory(_dir);
        DiagnosticLog.DirectoryOverride = _dir;
        RenderReport.ResetForTests();
        var path = RenderReport.WriteStartup(new RenderReport.Facts(
            "CPU (software)", true, false, false, 8192, 1920, 1080, 1.0, "Full", 1.0,
            StrokeWait: stats,
            // Widened where it is used rather than in the signature: these
            // cases are about the pass-cost line, and B313's wait and band
            // counters have their own tests. Zeroes there read as "not
            // measured", which is what the report prints them as.
            LivePost: livePost is { } p
                ? (p.Passes, p.TotalMs, p.WorstMs, 0, 0.0, 0.0, 0L, 0L, 0, 0, 0L, 0)
                : null))!;
        var text = File.ReadAllText(path);
        var start = text.IndexOf("-- pen to screen", StringComparison.Ordinal);
        Assert.True(start >= 0, $"the pen-to-screen section is missing:\n{text}");
        var end = text.IndexOf("-- where the tick", start, StringComparison.Ordinal);
        return end > start ? text[start..end] : text[start..];
    }

    [Fact]
    public void ACaptureWithNoStrokesSaysToDrawFirst()
    {
        var text = Section(null);
        output.WriteLine(text);
        Assert.Contains("DRAW a few strokes", text);
    }

    /// <summary>
    /// The guard the present-wait split learned from a four-frame cohort: a
    /// verdict off a handful of frames reads exactly like one that means
    /// something, so the report must refuse to give one.
    /// </summary>
    [Fact]
    public void AHandfulOfDrawnFramesGetsNoVerdict()
    {
        var text = Section(Stats(1, 2, 60, drawn: 3));
        output.WriteLine(text);
        Assert.Contains("Too few drawn frames", text);
        Assert.DoesNotContain("B178's", text);
    }

    [Fact]
    public void AFatWaitAfterThePublishIsNamedAsThePresentPath()
    {
        var text = Section(Stats(stampMean: 2, toPublishMean: 4, toDrawMean: 60));
        output.WriteLine(text);
        Assert.Contains("AFTER the publish", text);
        Assert.Contains("B178", text);
        Assert.DoesNotContain("fat segment", text);
    }

    [Fact]
    public void AFatStampIsNamedAsTheCostOfTheMark()
    {
        var text = Section(Stats(stampMean: 30, toPublishMean: 32, toDrawMean: 5));
        output.WriteLine(text);
        Assert.Contains("fat segment", text);
        Assert.DoesNotContain("B178", text);
    }

    [Fact]
    public void AFatQueueBetweenStampAndPublishIsNamedAsTheDispatcher()
    {
        var text = Section(Stats(stampMean: 2, toPublishMean: 40, toDrawMean: 5));
        output.WriteLine(text);
        Assert.Contains("BETWEEN stamp and publish", text);
    }

    /// <summary>
    /// Both anchors print, and when most of the PEN line is coalescing rather
    /// than staleness the section says so — the misreading B189's second
    /// capture actually produced (11.4 events gathered over ~90 ms read as
    /// more lag while the tip's was falling).
    /// </summary>
    [Fact]
    public void TheTipLineSeparatesCoalescingDepthFromStaleness()
    {
        var text = Section(Stats(
            stampMean: 2, toPublishMean: 80, toDrawMean: 20, tipToScreenMean: 30));
        output.WriteLine(text);
        Assert.Contains("TIP -> SCREEN", text);
        Assert.Contains("newest event", text);
        Assert.Contains("coalescing depth, not staleness", text);
    }

    /// <summary>A tip as stale as the pen is real lag, and gets no excuse.</summary>
    [Fact]
    public void ATipAsStaleAsThePenGetsNoCoalescingNote()
    {
        var text = Section(Stats(
            stampMean: 2, toPublishMean: 80, toDrawMean: 20, tipToScreenMean: 95));
        output.WriteLine(text);
        Assert.Contains("TIP -> SCREEN", text);
        Assert.DoesNotContain("coalescing depth, not staleness", text);
    }

    /// <summary>
    /// The exoneration has to be reachable, or every verdict above is a rubber
    /// stamp — and it must point the reader upstream, because a healthy chain
    /// with a lagging hand means the time is spent before the app sees the event.
    /// </summary>
    [Fact]
    public void AHealthyChainPointsUpstreamOfTheApp()
    {
        var text = Section(Stats(stampMean: 1, toPublishMean: 3, toDrawMean: 8));
        output.WriteLine(text);
        Assert.Contains("keeping up", text);
        Assert.Contains("BEFORE the event arrives", text);
    }

    /// <summary>
    /// A slow wet-media pass no longer blocks input (it runs on a worker since
    /// B189's second capture), but the wet look settles behind the pen by its
    /// cost, so the report still flags it — and a capture that never used a
    /// wet brush must say so rather than hand dry brushes' clean bill to the
    /// wet ones.
    /// </summary>
    [Fact]
    public void AnExpensiveMediumPassIsFlaggedAndAnAbsentOneIsSaidOutLoud()
    {
        var expensive = Section(Stats(1, 3, 8), livePost: (12, 12 * 170.0, 240));
        output.WriteLine(expensive);
        Assert.Contains("settles this far behind the pen tip", expensive);
        Assert.Contains("no longer blocks input", expensive);

        var none = Section(Stats(1, 3, 8));
        Assert.Contains("no wet-media brush was used", none);

        var cheap = Section(Stats(1, 3, 8), livePost: (12, 12 * 4.0, 9));
        Assert.DoesNotContain("settles this far", cheap);
        Assert.Contains("live medium passes        12", cheap);
    }
}

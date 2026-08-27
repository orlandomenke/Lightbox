using System.Diagnostics;
using Lightbox.App.Rendering;
using Lightbox.App.Services;

namespace Lightbox.App.Tests;

/// <summary>
/// B321's last split, and the floor it is measured against.
/// </summary>
/// <remarks>
/// <para>
/// <b>What these can and cannot prove.</b> The wait itself cannot be reproduced
/// here — there is no compositor, no screen and no pen, and a headless
/// dispatcher does not deliver frames in real time. What can be pinned, and is
/// the whole reason this file exists, is that the decomposition <em>adds
/// up</em>: the three phases of <c>publish -&gt; drawn</c> account for the total
/// exactly rather than approximately. Two verdicts on this bug were written from
/// a subtraction with nothing to attribute it to, and both were retracted.
/// </para>
/// <para>
/// The rest pins the report's arithmetic against a refresh rate the test
/// supplies, so the floor line is exercised on a build machine that may have no
/// display at all. A floor computed from a guessed refresh rate would be worse
/// than no floor, which is why <see cref="DisplayCadence"/> answers null rather
/// than assuming 60 Hz — and why the "no rate, no floor" case is a test.
/// </para>
/// </remarks>
[Collection("DisplayCadence")]
public class PresentFloorTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "lightbox-present-floor-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        DisplayCadence.OverrideForTest(null);
        DiagnosticLog.DirectoryOverride = null;
        RenderReport.ResetForTests();
        try { Directory.Delete(_dir, recursive: true); } catch { /* a temp dir is not worth failing over */ }
        GC.SuppressFinalize(this);
    }

    // ---- the decomposition -------------------------------------------------

    /// <summary>
    /// <b>The invariant the last two verdicts lacked.</b> Waiting for the visual
    /// pass, waiting to be picked up and drawing are the whole of the trip, so
    /// they must sum to it — not merely sit near it. The sleeps below need no
    /// accuracy for this: every phase is read off the same stopwatch, so
    /// whatever the operating system actually granted, the parts still have to
    /// account for the whole.
    /// </summary>
    [Fact]
    public void TheThreePhasesAccountForTheWholeWait()
    {
        var latency = new PresentLatency();
        latency.Published(1);
        Thread.Sleep(10);
        latency.Enqueued(1);
        Thread.Sleep(10);
        var drawStarted = Stopwatch.GetTimestamp();
        Thread.Sleep(10);
        latency.Rendered(1, drawStarted);

        var s = latency.Snapshot;
        var sum = s.ToEnqueueMeanMs + s.QueueMeanMs + s.KeyedDrawMeanMs;
        output.WriteLine(
            $"visual pass {s.ToEnqueueMeanMs:F2} + picked up {s.QueueMeanMs:F2} "
            + $"+ draw {s.KeyedDrawMeanMs:F2} = {sum:F2} against {s.MeanMs:F2}");

        Assert.Equal(1, s.Presented);
        Assert.Equal(1, s.Queued);
        // Half a millisecond is the lock and the arithmetic, nothing else.
        Assert.True(Math.Abs(sum - s.MeanMs) < 0.5,
            $"the phases came to {sum:F3} ms against a total of {s.MeanMs:F3} ms");
        // And each is a real share rather than the whole thing three times.
        Assert.True(s.QueueMeanMs >= 5, $"the wait to be picked up was {s.QueueMeanMs:F2} ms");
        Assert.True(s.KeyedDrawMeanMs >= 5, $"the draw was {s.KeyedDrawMeanMs:F2} ms");
    }

    /// <summary>
    /// A frame the report never saw handed over has no midpoint to subtract
    /// from, and inventing one would restate the entire trip as time spent
    /// waiting on the render thread — a wrong answer that looks exactly like the
    /// right one.
    /// </summary>
    [Fact]
    public void AFrameWithNoHandOverContributesNoQueueWait()
    {
        var latency = new PresentLatency();
        latency.Published(1);
        latency.Rendered(1, Stopwatch.GetTimestamp());

        var s = latency.Snapshot;
        Assert.Equal(1, s.Presented);
        Assert.Equal(0, s.Queued);
        Assert.Equal(0, s.QueueMeanMs);
    }

    /// <summary>
    /// The draw counted against a published frame is not the draw counted
    /// across the session: a hovering pen repaints the cursor many times a
    /// second, and those are real draws that no published frame paid for.
    /// </summary>
    [Fact]
    public void TheKeyedDrawExcludesTheRepaintsNoPublishAskedFor()
    {
        var latency = new PresentLatency();
        latency.Published(1);
        latency.Enqueued(1);
        var drawStarted = Stopwatch.GetTimestamp();
        Thread.Sleep(6);
        latency.Rendered(1, drawStarted);

        // Cursor repaints: timed by the op, keyed to nothing.
        for (var i = 0; i < 20; i++) latency.Drew(0.1);

        var s = latency.Snapshot;
        output.WriteLine($"keyed {s.KeyedDrawMeanMs:F2} ms, every draw {s.DrawMeanMs:F2} ms over {s.Draws}");

        Assert.Equal(20, s.Draws);
        Assert.True(s.DrawMeanMs < 1, $"the unkeyed mean was {s.DrawMeanMs:F2} ms");
        Assert.True(s.KeyedDrawMeanMs >= 3, $"the keyed mean was {s.KeyedDrawMeanMs:F2} ms");
    }

    /// <summary>
    /// <c>Reset</c> has to reach the new tallies too, or a second capture in one
    /// session reports the first one's numbers — which is how a report comes to
    /// describe a build that is no longer running.
    /// </summary>
    [Fact]
    public void ResetClearsTheNewPhasesAsWell()
    {
        var latency = new PresentLatency();
        latency.Published(1);
        latency.Enqueued(1);
        Thread.Sleep(4);
        latency.Rendered(1, Stopwatch.GetTimestamp());
        latency.Reset();

        var s = latency.Snapshot;
        Assert.Equal(0, s.Queued);
        Assert.Equal(0, s.QueueMeanMs);
        Assert.Equal(0, s.QueueBestMs);
        Assert.Equal(0, s.KeyedDrawMeanMs);
        Assert.Equal(0, s.MedianMs);
    }

    // ---- the floor ---------------------------------------------------------

    /// <summary>
    /// A 60 Hz screen, a 4.3 ms draw and 28.5 ms from publish to drawn — the
    /// two measured halves of the owner's capture of 2026-08-27. The floor for
    /// this path is one and a half refreshes plus the draw, 29.3 ms, so the
    /// capture is at it and the report has to say so rather than calling 28.5 ms
    /// slow. A latency with no floor beside it always reads as a defect.
    /// </summary>
    [Fact]
    public void ACaptureAtTheFloorIsReportedAsTheCadenceRatherThanACost()
    {
        var text = Section(AtTheFloor);
        output.WriteLine(text);

        Assert.Contains("the screen refreshes every 16.67 ms (60 Hz)", text);
        Assert.Contains("AT THE FLOOR", text);
        Assert.Contains("1.71 refreshes", text);
        Assert.Contains("That is a gate, not a queue", text);
    }

    /// <summary>
    /// The opposite finding has to be reachable, or the first is a rubber stamp
    /// — the same rule <c>PresentWaitByInputTests</c> keeps for its verdicts.
    /// </summary>
    [Fact]
    public void ACaptureAboveTheFloorIsReportedAsACost()
    {
        var text = Section(AtTheFloor with
        {
            MeanMs = 62,
            MedianMs = 60,
            QueueMeanMs = 48,
            QueueMedianMs = 47,
            QueueBestMs = 0.4,
        });
        output.WriteLine(text);

        Assert.Contains("ABOVE the floor", text);
        Assert.DoesNotContain("AT THE FLOOR", text);
        Assert.Contains("That is a queue", text);
    }

    /// <summary>
    /// <b>The owner's capture of 2026-08-27 09:16, and the one that closed
    /// B321.</b> Eighty per cent of the pick-up waits sit between 13.67 and
    /// 16.94 ms — a 3.3 ms band just under one refresh — with a median of 16.22
    /// and a single 1.61 ms outlier below the tenth percentile. That is a gate,
    /// and the frame is at the floor.
    /// </summary>
    /// <remarks>
    /// <b>This test exists because the verdict was wrong in the other direction
    /// first.</b> Reading the minimum, the report called this same capture "not
    /// a gate" on the strength of that one outlier. A gate with a rare escape is
    /// still a gate for every frame that does not take it, and nobody watches
    /// the fastest frame.
    /// </remarks>
    [Fact]
    public void TheOwnersCaptureReadsAsAGateAtTheFloor()
    {
        var text = Section(AtTheFloor with
        {
            Presented = 423,
            MeanMs = 29.59,
            MedianMs = 29.19,
            WorstMs = 256.32,
            ToEnqueueMeanMs = 8.66,
            ToEnqueueMedianMs = 8.37,
            QueueMeanMs = 16.2,
            QueueMedianMs = 16.22,
            QueueBestMs = 1.61,
            QueueP10Ms = 13.67,
            QueueP90Ms = 16.94,
            KeyedDrawMeanMs = 4.73,
        });
        output.WriteLine(text);

        Assert.Contains("AT THE FLOOR", text);
        Assert.Contains("That is a gate, not a queue", text);
        Assert.Contains("p10 13.67 ms   p90 16.94 ms", text);
        Assert.Contains("1.75 refreshes", text);
        // The reading this capture refuted.
        Assert.DoesNotContain("IT IS NOT A GATE", text);
    }

    /// <summary>
    /// The opposite distribution, and it has to stay reachable or the verdict
    /// above is a rubber stamp: a median of a refresh with a tenth of frames
    /// coming through in under a millisecond really is a race, and would reopen
    /// B321.
    /// </summary>
    [Fact]
    public void ATenthOfFramesComingThroughFastReadsAsARaceRatherThanAFloor()
    {
        var text = Section(AtTheFloor with
        {
            MeanMs = 32.11,
            MedianMs = 29.09,
            ToEnqueueMeanMs = 11.82,
            ToEnqueueMedianMs = 8.53,
            QueueMeanMs = 15.59,
            QueueMedianMs = 15.78,
            QueueBestMs = 0.4,
            QueueP10Ms = 0.9,
            QueueP90Ms = 16.4,
            KeyedDrawMeanMs = 4.71,
        });
        output.WriteLine(text);

        Assert.Contains("IT IS NOT A GATE", text);
        Assert.Contains("a race that most of them lose", text);
        Assert.Contains("p10 0.9 ms   p90 16.4 ms", text);
        Assert.DoesNotContain("That is a gate, not a queue", text);
    }

    /// <summary>
    /// <b>One outlier may not overturn the spread.</b> The minimum said "not a
    /// gate" about a capture whose every decile said otherwise, and this pins
    /// that it no longer can.
    /// </summary>
    [Fact]
    public void ASingleFastFrameDoesNotMakeAGateIntoARace()
    {
        var gated = AtTheFloor with
        {
            QueueMedianMs = 16.22, QueueP10Ms = 13.67, QueueP90Ms = 16.94,
            MedianMs = 29.19, ToEnqueueMedianMs = 8.37, KeyedDrawMeanMs = 4.73,
        };

        var withOutlier = Section(gated with { QueueBestMs = 0.2 });
        var without = Section(gated with { QueueBestMs = 13.4 });

        Assert.Contains("That is a gate, not a queue", withOutlier);
        Assert.Contains("That is a gate, not a queue", without);
    }

    /// <summary>
    /// No refresh rate, no floor. A build machine with no display must not be
    /// handed an arithmetic that quietly assumed 60 Hz — the number would look
    /// exactly as authoritative as a real one.
    /// </summary>
    [Fact]
    public void WithNoRefreshRateTheReportComputesNoFloor()
    {
        var text = Section(AtTheFloor, hz: 0);
        output.WriteLine(text);

        Assert.Contains("screen refresh rate unknown", text);
        Assert.DoesNotContain("AT THE FLOOR", text);
        Assert.DoesNotContain("floor for this path", text);
    }

    /// <summary>
    /// The floor moves with the screen, which is the claim that makes it a
    /// measurement rather than a constant somebody liked. The same 28.5 ms trip
    /// is at the floor on a 60 Hz panel and well above it on a 120 Hz one.
    /// </summary>
    [Fact]
    public void TheFloorFollowsTheRefreshRate()
    {
        var faster = Section(AtTheFloor, hz: 120);
        output.WriteLine(faster);

        Assert.Contains("the screen refreshes every 8.33 ms (120 Hz)", faster);
        Assert.Contains("ABOVE the floor", faster);
    }

    /// <summary>
    /// A capture sitting on the floor, kept as one value so every test above
    /// varies one thing against the same control.
    /// </summary>
    /// <remarks>
    /// <b>Two of these are measured and the rest are constructed, and the
    /// difference matters.</b> <c>28.5</c> ms from publish to drawn and a
    /// <c>4.3</c> ms draw are the owner's capture of 2026-08-27, recorded in
    /// B321. The three-way split did not exist when that capture was taken, so
    /// the phases here are what the floor arithmetic implies rather than
    /// anything anybody measured — chosen to sum to the total exactly, because
    /// a fixture whose parts do not add up would pass a report that prints
    /// impossible rows. When a real capture with these fields arrives, this
    /// fixture should be replaced by it and B321 amended either way.
    /// </remarks>
    private static PresentLatency.Stats AtTheFloor => new(
        Presented: 300, Superseded: 4, MeanMs: 28.5, WorstMs: 96,
        ByCohort: null,
        Enqueued: 300, ToEnqueueMeanMs: 9.4, ToEnqueueWorstMs: 41,
        Draws: 900, DrawMeanMs: 2.1, DrawWorstMs: 31,
        MedianMs: 28.5, ToEnqueueMedianMs: 9.2,
        Queued: 300, QueueMeanMs: 14.8, QueueMedianMs: 14.6,
        QueueBestMs: 13.9, QueueWorstMs: 44,
        KeyedDrawMeanMs: 4.3,
        QueueP10Ms: 13.9, QueueP90Ms: 15.6);

    private string Section(PresentLatency.Stats stats, int hz = 60)
    {
        Directory.CreateDirectory(_dir);
        DiagnosticLog.DirectoryOverride = _dir;
        DisplayCadence.OverrideForTest(hz);
        RenderReport.ResetForTests();

        var path = RenderReport.WriteStartup(RenderReportTests.Facts(presentWait: stats))!;
        var text = File.ReadAllText(path);
        var start = text.IndexOf("frames published and drawn", StringComparison.Ordinal);
        Assert.True(start >= 0, $"the present-wait section is missing:\n{text}");
        var end = text.IndexOf("the same wait, split", start, StringComparison.Ordinal);
        return end > start ? text[start..end] : text[start..];
    }
}

/// <summary>
/// <see cref="DisplayCadence"/>'s override is one static for the process, so
/// the tests that set it may not run beside each other.
/// </summary>
[CollectionDefinition("DisplayCadence", DisableParallelization = true)]
public class DisplayCadenceCollection;

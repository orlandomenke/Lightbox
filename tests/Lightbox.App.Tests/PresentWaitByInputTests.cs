using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The wait to be drawn, split by what arrived while the frame was waiting.
/// </summary>
/// <remarks>
/// <para>
/// <b>Instrumentation for a bug three fixes have missed</b>: playback is smooth
/// while the pointer moves over the canvas, stuttery when it is still, and no
/// better when the pointer moves over a docker. The last part is what matters —
/// it refutes "any input wakes the loop", which is what the previous two fixes
/// were built on.
/// </para>
/// <para>
/// These tests pin the <em>instrument</em>, not the fault. A test cannot
/// reproduce the fault: the headless dispatcher does not deliver frames in real
/// time, so a measured wait here would be measuring the harness — the same
/// reason <c>PlaybackClock</c>'s own docs give for not testing its magnitude.
/// What can be pinned is that the report will attribute frames correctly when
/// the owner captures one, which is the thing that was missing.
/// </para>
/// </remarks>
public class PresentWaitByInputTests(ITestOutputHelper output)
{
    private static PresentLatency Fresh()
    {
        var l = new PresentLatency();
        l.Reset();
        return l;
    }

    private static PresentLatency.CohortStats Of(
        PresentLatency.Stats s, PresentLatency.Cohort c) =>
        s.ByCohort![(int)c];

    [Fact]
    public void AFrameDrawnWithNothingHappeningIsTheQuietCohort()
    {
        var l = Fresh();
        l.Published(1);
        l.Rendered(1);

        var s = l.Snapshot;
        Assert.Equal(1, Of(s, PresentLatency.Cohort.Quiet).Count);
        Assert.Equal(0, Of(s, PresentLatency.Cohort.InputOnCanvas).Count);
        Assert.Equal(0, Of(s, PresentLatency.Cohort.InputElsewhere).Count);
    }

    [Fact]
    public void AFrameDrawnAfterCanvasInputIsAttributedToTheCanvas()
    {
        var l = Fresh();
        l.Published(1);
        InputPulse.OnCanvas();
        l.Rendered(1);

        Assert.Equal(1, Of(l.Snapshot, PresentLatency.Cohort.InputOnCanvas).Count);
    }

    [Fact]
    public void AFrameDrawnAfterInputSomewhereElseIsAttributedThere()
    {
        var l = Fresh();
        l.Published(1);
        InputPulse.Elsewhere();
        l.Rendered(1);

        Assert.Equal(1, Of(l.Snapshot, PresentLatency.Cohort.InputElsewhere).Count);
    }

    /// <summary>
    /// Canvas input wins a tie, because it is the candidate explanation and
    /// attributing the frame anywhere else would hide the very thing the split
    /// exists to find.
    /// </summary>
    [Fact]
    public void CanvasInputWinsWhenBothArrived()
    {
        var l = Fresh();
        l.Published(1);
        InputPulse.Elsewhere();
        InputPulse.OnCanvas();
        l.Rendered(1);

        Assert.Equal(1, Of(l.Snapshot, PresentLatency.Cohort.InputOnCanvas).Count);
        Assert.Equal(0, Of(l.Snapshot, PresentLatency.Cohort.InputElsewhere).Count);
    }

    /// <summary>
    /// Input that arrived BEFORE the frame was published cannot explain that
    /// frame's wait. Without this the counters only ever climb and every frame
    /// after the first mouse move looks input-driven.
    /// </summary>
    [Fact]
    public void InputBeforeThePublishDoesNotCount()
    {
        var l = Fresh();
        InputPulse.OnCanvas();
        InputPulse.Elsewhere();
        l.Published(1);
        l.Rendered(1);

        Assert.Equal(1, Of(l.Snapshot, PresentLatency.Cohort.Quiet).Count);
    }

    /// <summary>
    /// The shape the report is built to recognise, written out so the wording
    /// that fires on it is pinned to the numbers that mean it.
    /// </summary>
    [Fact]
    public void TheReportNamesTheCanvasWhenOnlyTheCanvasHelps()
    {
        var stats = new PresentLatency.Stats(300, 100, 40, 160,
        [
            new(PresentLatency.Cohort.Quiet, 100, 60, 200),
            new(PresentLatency.Cohort.InputElsewhere, 100, 58, 190),
            new(PresentLatency.Cohort.InputOnCanvas, 100, 4, 12),
        ]);

        var text = Section(stats);
        output.WriteLine(text);

        Assert.Contains("CONFIRMED", text);
        Assert.Contains("CANVAS is", text);
        Assert.Contains("do not", text);
    }

    /// <summary>The opposite finding has to be reachable, or the first is a rubber stamp.</summary>
    [Fact]
    public void TheReportBlamesTheWakeWhenAnyInputHelps()
    {
        var stats = new PresentLatency.Stats(300, 100, 40, 160,
        [
            new(PresentLatency.Cohort.Quiet, 100, 60, 200),
            new(PresentLatency.Cohort.InputElsewhere, 100, 5, 14),
            new(PresentLatency.Cohort.InputOnCanvas, 100, 4, 12),
        ]);

        var text = Section(stats);
        output.WriteLine(text);

        Assert.Contains("Any input helps", text);
        Assert.Contains("dispatcher priority", text);
    }

    /// <summary>And so does "input is irrelevant, look at the tick".</summary>
    [Fact]
    public void TheReportSendsYouToTheTickWhenInputChangesNothing()
    {
        var stats = new PresentLatency.Stats(300, 10, 6, 20,
        [
            new(PresentLatency.Cohort.Quiet, 100, 6, 20),
            new(PresentLatency.Cohort.InputElsewhere, 100, 6, 19),
            new(PresentLatency.Cohort.InputOnCanvas, 100, 5, 18),
        ]);

        var text = Section(stats);
        output.WriteLine(text);

        Assert.Contains("NOT the frame reaching the screen", text);
    }

    /// <summary>
    /// A capture taken entirely with the pointer moving says nothing, and has to
    /// say so rather than reporting a clean bill of health.
    /// </summary>
    [Fact]
    public void ACaptureWithNoQuietFramesAsksForAnotherOne()
    {
        var stats = new PresentLatency.Stats(100, 0, 4, 12,
        [
            new(PresentLatency.Cohort.Quiet, 0, 0, 0),
            new(PresentLatency.Cohort.InputElsewhere, 0, 0, 0),
            new(PresentLatency.Cohort.InputOnCanvas, 100, 4, 12),
        ]);

        var text = Section(stats);
        output.WriteLine(text);

        Assert.Contains("says nothing yet", text);
        Assert.Contains("pointer STILL", text);
    }

    private static string Section(PresentLatency.Stats stats)
    {
        RenderReport.ResetForTests();
        var path = RenderReport.WriteStartup(RenderReportTests.Facts(presentWait: stats))!;
        var text = File.ReadAllText(path);
        var start = text.IndexOf("the same wait, split", StringComparison.Ordinal);
        Assert.True(start >= 0, $"the split section is missing:\n{text}");
        var end = text.IndexOf("-- where the tick", start, StringComparison.Ordinal);
        return end > start ? text[start..end] : text[start..];
    }
}

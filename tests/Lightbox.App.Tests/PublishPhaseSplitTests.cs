using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The tick's publish cost, split into the composite and the handoff, and the
/// frame period it is supposed to be read against.
/// </summary>
/// <remarks>
/// <para>
/// <b>B157.</b> Four captures from one machine in one evening ended B156's
/// theory and the attribution I put on top of it. The reports were meant to
/// show whether frames wait on canvas input; what they showed is that the
/// canvas cohort is <em>slower</em>, and that the ~35 ms per tick attributed to
/// "the CPU composite at 1080p" did not fall when the compose surface did.
/// </para>
/// <para>
/// Both were one measurement doing two jobs. `Publish` timed the composite and
/// the handoff together, so a cost in either read as a cost in the other, and
/// the report asked the reader to compare it against a frame period it never
/// printed.
/// </para>
/// </remarks>
public class PublishPhaseSplitTests(ITestOutputHelper output)
{
    /// <summary>
    /// The two halves are separate rows. Without this the report can say
    /// "35 ms in Publish" and nothing about which half spent it.
    /// </summary>
    [Fact]
    public void ComposeAndHandoffAreReportedApart()
    {
        var text = Report(
        [
            new(TickProfile.Phase.Compose, 100, 800, 12.0),
            new(TickProfile.Phase.Handoff, 100, 2700, 40.0),
        ]);
        output.WriteLine(text);

        Assert.Contains("Compose", text);
        Assert.Contains("Handoff", text);
        // 27 ms a tick in the handoff has to be legible as the larger of the two.
        Assert.Matches(@"Handoff\s+27", text);
        Assert.Matches(@"Compose\s+8", text);
    }

    /// <summary>
    /// <b>The comparison the report has always asked for and never supplied.</b>
    /// The tick breakdown says "compare that with the frame period for this
    /// scene's fps"; until now the fps was not in the file, so the one piece of
    /// arithmetic the report requests had to be done from memory.
    /// </summary>
    [Fact]
    public void TheFramePeriodIsPrintedSoTheBudgetCanBeRead()
    {
        var text = Report(
            [new(TickProfile.Phase.Compose, 100, 800, 12.0)],
            scene: (5, 2, 11, 12.0));
        output.WriteLine(text);

        Assert.Contains("12 fps", text);
        Assert.Contains("83.3 ms", text);
    }

    /// <summary>
    /// A scene playing at half speed has twice the period, and a report that
    /// printed the nominal rate would make the budget look twice as generous as
    /// it is.
    /// </summary>
    [Fact]
    public void ThePeriodFollowsThePlaybackSpeedRatherThanTheScenesNominalRate()
    {
        var text = Report(
            [new(TickProfile.Phase.Compose, 100, 800, 12.0)],
            scene: (5, 2, 11, 6.0));
        output.WriteLine(text);

        Assert.Contains("166.7 ms", text);
    }

    /// <summary>
    /// <b>The defect one of the four captures exposed in my own report.</b> It
    /// announced "any input helps" — the verdict that points at the dispatcher —
    /// off FOUR quiet frames, whose 73 ms mean came from a single 163 ms stall.
    /// The three captures beside it, with 31, 52 and 53 quiet frames, all said
    /// the opposite. A verdict that turns on a handful of frames reads exactly
    /// like one that means something, which makes it worse than none.
    /// </summary>
    [Fact]
    public void ATinyCohortGetsNoVerdict()
    {
        var text = Split(new PresentLatency.Stats(71, 10, 24.21, 163.74,
        [
            new(PresentLatency.Cohort.Quiet, 4, 73.18, 163.74),
            new(PresentLatency.Cohort.InputElsewhere, 5, 19.93, 43.78),
            new(PresentLatency.Cohort.InputOnCanvas, 62, 21.4, 103.11),
        ]));
        output.WriteLine(text);

        Assert.Contains("Too few frames", text);
        Assert.DoesNotContain("Any input helps", text);
        Assert.DoesNotContain("CONFIRMED", text);
    }

    /// <summary>The guard must not swallow a capture that genuinely says something.</summary>
    [Fact]
    public void AnAmpleCohortStillGetsOne()
    {
        var text = Split(new PresentLatency.Stats(300, 100, 40, 160,
        [
            new(PresentLatency.Cohort.Quiet, 52, 21.78, 159.98),
            new(PresentLatency.Cohort.InputElsewhere, 40, 28.72, 79.73),
            new(PresentLatency.Cohort.InputOnCanvas, 113, 42.71, 119.25),
        ]));
        output.WriteLine(text);

        Assert.DoesNotContain("Too few frames", text);
        // The real shape of the reported captures: canvas input is SLOWER, so
        // the wait has nothing to do with input and the reader goes to the tick.
        Assert.Contains("NOT the frame reaching the screen", text);
    }

    private static string Report(
        IReadOnlyList<TickProfile.PhaseStats> phases,
        (int, int, int, double)? scene = null)
    {
        RenderReport.ResetForTests();
        var path = RenderReport.WriteStartup(RenderReportTests.Facts(
            tickPhases: phases, tickCount: 100, scene: scene))!;
        return File.ReadAllText(path);
    }

    private static string Split(PresentLatency.Stats stats)
    {
        RenderReport.ResetForTests();
        var text = File.ReadAllText(
            RenderReport.WriteStartup(RenderReportTests.Facts(presentWait: stats))!);
        var start = text.IndexOf("the same wait, split", StringComparison.Ordinal);
        Assert.True(start >= 0, $"the split section is missing:\n{text}");
        var end = text.IndexOf("-- where the tick", start, StringComparison.Ordinal);
        return end > start ? text[start..end] : text[start..];
    }
}

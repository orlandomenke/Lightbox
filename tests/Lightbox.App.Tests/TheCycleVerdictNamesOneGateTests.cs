using Lightbox.App.Services;

namespace Lightbox.App.Tests;

/// <summary>
/// That the cycle's verdict names one gate, that each of its arms is reachable,
/// and that it never names two (B335).
/// </summary>
/// <remarks>
/// <para>
/// <b>Written because an unexercised branch of this report was found to be
/// unreachable on the same afternoon.</b> The refusal split started with three
/// buckets and the third could not occur in any state the publish path can be
/// in. A verdict with four arms and no test is the same bet made four times, and
/// this report's own history is the argument: a section that printed
/// <c>compositing CPU raster (always)</c> unconditionally, and another that told
/// one capture in a single breath that it sat at an unimprovable floor and that
/// its pick-up was a queue with something in it.
/// </para>
/// <para>
/// <b>Wording, not arithmetic.</b> These are instrument tests — they hold what
/// the file says to whoever reads it next, which is the thing that decides where
/// the following branch's work goes. They are deliberately not B335's evidence
/// anchor.
/// </para>
/// </remarks>
public class TheCycleVerdictNamesOneGateTests(ITestOutputHelper output)
{
    /// <summary>
    /// The cycle block prints only once strokes have been measured, so the
    /// report needs publishes on the record to reach it at all.
    /// </summary>
    private static string CycleSection(
        int refusedByDam, int refusedByPost, int refusedByBoth, int letThrough,
        double cycleMedianMs = 29.26, double askedMedianMs = 1.0, double? askedWorstMs = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "lightbox-cycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DiagnosticLog.DirectoryOverride = dir;
            RenderReport.ResetForTests();

            var strokes = new Lightbox.App.Rendering.StrokeToScreen.Stats(
                Events: 1065, Publishes: 204, Drawn: 184, Superseded: 20,
                Stamp: default, WaitToPublish: default, WaitToDraw: default,
                PenToScreen: default);

            var path = RenderReport.WriteStartup(RenderReportTests.Facts(
                strokeWait: strokes,
                cycle: (cycleMedianMs, 304.04, 238, 0, 8.89, 5.68, 1065,
                        refusedByDam, refusedByPost, refusedByBoth, letThrough,
                        askedMedianMs, askedMedianMs, askedWorstMs ?? askedMedianMs * 3)))!;
            return File.ReadAllText(path);
        }
        finally
        {
            DiagnosticLog.DirectoryOverride = null;
            RenderReport.ResetForTests();
            try { Directory.Delete(dir, recursive: true); } catch { /* a temp dir is not worth failing over */ }
        }
    }

    [Fact]
    public void EachVerdictIsReachableAndOnlyOneFiresAtATime()
    {
        var verdicts = new (string Name, string Text)[]
        {
            ("dispatcher", CycleSection(refusedByDam: 40, refusedByPost: 700, refusedByBoth: 0, letThrough: 204)),
            ("pacing", CycleSection(refusedByDam: 700, refusedByPost: 40, refusedByBoth: 0, letThrough: 204)),
            ("neither", CycleSection(refusedByDam: 10, refusedByPost: 10, refusedByBoth: 0, letThrough: 900)),
            ("both", CycleSection(refusedByDam: 400, refusedByPost: 400, refusedByBoth: 0, letThrough: 204)),
        };

        // Every arm's own opening words. If one of these never appears in any
        // report the branch is unreachable, which is the finding this class
        // exists to make impossible to miss.
        var openings = new[]
        {
            "THE DISPATCHER is setting the rate",
            "THE PACING is setting the rate",
            "NEITHER GATE is the constraint",
            "BOTH GATES BIND",
        };

        foreach (var (name, text) in verdicts)
        {
            var fired = openings.Where(o => text.Contains(o, StringComparison.Ordinal)).ToList();
            output.WriteLine($"{name,-12} -> {(fired.Count == 1 ? fired[0] : $"{fired.Count} verdicts")}");
            Assert.Single(fired);
        }

        var all = verdicts.Select(v => v.Text).ToList();
        foreach (var opening in openings)
        {
            Assert.True(
                all.Any(t => t.Contains(opening, StringComparison.Ordinal)),
                $"no shape of capture produced \"{opening}\", so that arm of the verdict is "
                + "unreachable and the report can never say it");
        }
    }

    /// <summary>
    /// The two captures the owner took on 2026-08-28, one per arm, read the way
    /// they should.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The inline arm is why this test exists.</b> Its split is 110 refusals
    /// by the dam against <b>0</b> by the post — as one-sided as a split can
    /// be — and the first version of the verdict called it <i>"the two gates are
    /// turning away comparable numbers"</i>. It compared each gate to the
    /// events that got through as well as to the other gate, and 110 is not
    /// greater than 111. A verdict that can call nought-against-a-hundred-and-ten
    /// balanced would have sent the next branch to look for a dispatcher fault
    /// that the same capture had already removed.
    /// </para>
    /// <para>
    /// <b>And the default arm's verdict is deliberately not the stronger one.</b>
    /// 179 against 130 is the post ahead, not the post alone, and the two arms
    /// measured exactly that: taking the post out moved the cycle 28.84 ms to
    /// 22.12, an improvement rather than a fix, with the dam then holding 110 of
    /// the 221 that asked. The report has to say "both, and this one is larger"
    /// or the next reader over-claims what removing it buys.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOwnersTwoArmsReadTheWayTheyShould()
    {
        // render-report-20260828-123419 — the default, posted arm.
        var posted = CycleSection(
            refusedByDam: 130, refusedByPost: 179, refusedByBoth: 0, letThrough: 66,
            cycleMedianMs: 28.84, askedMedianMs: 3.94, askedWorstMs: 470.57);

        // render-report-20260828-123500 — LIGHTBOX_PUBLISH=inline.
        var inline = CycleSection(
            refusedByDam: 110, refusedByPost: 0, refusedByBoth: 0, letThrough: 111,
            cycleMedianMs: 22.12, askedMedianMs: 0, askedWorstMs: 0);

        Assert.Contains("BOTH GATES BIND, and the posted publish is the larger (179 against 130)", posted);
        Assert.DoesNotContain("THE DISPATCHER is setting the rate", posted);

        Assert.Contains("THE PACING is setting the rate", inline);
        Assert.DoesNotContain("BOTH GATES BIND", inline);

        // And the tail the median hid. 3.94 ms typical, 470.57 ms worst: the
        // half-a-cycle check reads the median and stays silent on it.
        Assert.DoesNotContain("The post itself waits longer than half a cycle", posted);
        Assert.Contains("a rare one stalls", posted);
        Assert.Contains("470.57 ms", posted);

        foreach (var line in posted.Split('\n').Where(l => l.Contains(">>") || l.Contains("rare one")))
        {
            output.WriteLine("posted: " + line.Trim());
        }

        foreach (var line in inline.Split('\n').Where(l => l.Contains(">>")))
        {
            output.WriteLine("inline: " + line.Trim());
        }
    }

    /// <summary>
    /// The split is printed as counts, and the post's bucket says outright what
    /// it claims — that the dam would have let those through.
    /// </summary>
    [Fact]
    public void TheSplitSaysWhatThePostsBucketMeans()
    {
        var text = CycleSection(refusedByDam: 40, refusedByPost: 700, refusedByBoth: 0, letThrough: 204);

        Assert.Contains("asked to publish        944   went out 204   turned away 740", text);
        Assert.Contains("(the dam would have let these go)", text);
        // Zero is the expected state, so the bucket is absent rather than noise.
        Assert.DoesNotContain("by both at once", text);

        output.WriteLine(
            string.Join(
                '\n',
                text.Split('\n').Where(l => l.Contains("asked to publish") || l.Contains("by the dam")
                                            || l.Contains("already posted") || l.Contains("asked -> published"))));
    }

    /// <summary>
    /// A non-zero overlap bucket is called out as impossible rather than
    /// printed as data.
    /// </summary>
    /// <remarks>
    /// It cannot happen unless the in-flight depth changes while a publish is
    /// posted. If it ever does, the split above stops being a clean attribution
    /// and a reader must be told so rather than shown three numbers that no
    /// longer partition anything.
    /// </remarks>
    [Fact]
    public void AnImpossibleOverlapIsFlaggedRatherThanPrinted()
    {
        var text = CycleSection(refusedByDam: 40, refusedByPost: 700, refusedByBoth: 7, letThrough: 204);

        Assert.Contains("by both at once", text);
        Assert.Contains("that bucket is supposed to be unreachable", text);
        output.WriteLine(
            string.Join(
                '\n', text.Split('\n').Where(l => l.Contains("unreachable") || l.Contains("both at once"))));
    }

    /// <summary>
    /// And the post's wait is called out when it is most of the loop — the same
    /// finding the counts give, measured the other way.
    /// </summary>
    [Fact]
    public void APostThatWaitsHalfACycleIsSaidTwice()
    {
        var slow = CycleSection(
            refusedByDam: 40, refusedByPost: 700, refusedByBoth: 0, letThrough: 204,
            cycleMedianMs: 29.26, askedMedianMs: 20);
        var quick = CycleSection(
            refusedByDam: 40, refusedByPost: 700, refusedByBoth: 0, letThrough: 204,
            cycleMedianMs: 29.26, askedMedianMs: 1);

        Assert.Contains("The post itself waits longer than half a cycle", slow);
        Assert.DoesNotContain("The post itself waits longer than half a cycle", quick);
        output.WriteLine("a 20 ms post against a 29.26 ms cycle is called out; a 1 ms one is not");
    }
}

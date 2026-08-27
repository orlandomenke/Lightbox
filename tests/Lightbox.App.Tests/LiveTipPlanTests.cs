using Lightbox.App.Rendering;

namespace Lightbox.App.Tests;

/// <summary>
/// The guard the fourth attempt at B322 did not have.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three tests passed while that fix restamped 99% of the stroke per
/// publish.</b> They pinned the tip at a small fixed rectangle and asked whether
/// the composition was correct; none of them asked whether producing it was
/// <em>bounded</em>, so they would have passed at any tip size. The fix reached
/// the owner's machine and took pen-to-screen from 63 ms to 991.
/// </para>
/// <para>
/// So boundedness is checked here as a <b>property over the whole input
/// space</b> rather than sampled at a convenient size. There is no stroke to
/// draw and no pixel to read: the decision is arithmetic, and arithmetic can be
/// proved rather than spot-checked. That is the whole reason it was pulled out
/// of the code that acts on it.
/// </para>
/// </remarks>
public class LiveTipPlanTests(ITestOutputHelper output)
{
    /// <summary>
    /// <b>The invariant, over every input that can occur.</b> Whatever the pass
    /// has reached and however long the stroke has grown, the work this plans is
    /// capped. This is invariant 6 stated about one decision.
    /// </summary>
    [Fact]
    public void NoInputAsksForMoreThanTheBudget()
    {
        var worst = 0;
        foreach (var post in new[] { -1, 0, 1, 2, 9, 10, 127, 128, 129, 1000, 100_000 })
        {
            foreach (var count in new[] { 0, 1, 10, 11, 128, 129, 1263, 10_000, 1_000_000 })
            {
                var (range, _, _) = LiveTipPlan.For(post, count);
                if (range is not { } r) continue;

                Assert.True(r.Count > 0, $"an empty range was planned for post {post}, count {count}");
                Assert.True(
                    r.Count <= LiveTipPlan.MaxDabs,
                    $"post {post}, count {count} planned {r.Count} dabs — above the budget "
                    + $"of {LiveTipPlan.MaxDabs}, which is invariant 6 broken the way B322's "
                    + "fourth attempt broke it");
                Assert.True(r.From >= post && r.To <= count, $"range {r} escapes [{post}, {count})");
                worst = Math.Max(worst, r.Count);
            }
        }

        output.WriteLine($"worst range planned across the sweep: {worst} dabs");
    }

    /// <summary>
    /// <b>The owner's capture of 2026-08-27, as numbers.</b> 1263 points with the
    /// pass at 10. The fourth attempt planned 1253 dabs here and restamped them
    /// on every publish; the answer has to be "draw nothing".
    /// </summary>
    [Fact]
    public void TheCaptureThatKilledTheFourthAttemptPlansNothing()
    {
        var (range, why, outstanding) = LiveTipPlan.For(10, 1263);
        output.WriteLine($"outstanding {outstanding}, planned {range?.Count ?? 0}, because {why}");

        Assert.Null(range);
        Assert.Equal(LiveTipPlan.Skip.TooFarBehind, why);
        Assert.Equal(1253, outstanding);
    }

    /// <summary>
    /// And it still has to do its job in the case it exists for, or the budget
    /// has simply turned the fix off.
    /// </summary>
    [Fact]
    public void AShortOutstandingRunIsDrawn()
    {
        var (range, why, outstanding) = LiveTipPlan.For(400, 440);
        output.WriteLine($"outstanding {outstanding}, planned {range?.Count ?? 0}, because {why}");

        Assert.Equal(LiveTipPlan.Skip.None, why);
        Assert.Equal(new LiveTipPlan.Range(400, 440), range);
        Assert.Equal(40, range!.Value.Count);
    }

    /// <summary>
    /// <b>Falling back is all-or-nothing on purpose.</b> Drawing only the newest
    /// budget's worth of a long outstanding run would leave the mark between it
    /// and the processed body missing — a break in the middle of the stroke,
    /// which is worse to look at than a short tail that is late.
    /// </summary>
    [Fact]
    public void JustOverTheBudgetDrawsNothingRatherThanATruncatedTip()
    {
        var (justUnder, _, _) = LiveTipPlan.For(1, 1 + LiveTipPlan.MaxDabs);
        var (justOver, why, _) = LiveTipPlan.For(1, 2 + LiveTipPlan.MaxDabs);

        Assert.NotNull(justUnder);
        Assert.Equal(LiveTipPlan.MaxDabs, justUnder!.Value.Count);
        Assert.Null(justOver);
        Assert.Equal(LiveTipPlan.Skip.TooFarBehind, why);
    }

    /// <summary>
    /// Before any pass has landed the raw scratch already is the whole mark, so
    /// a tip would be the same ink drawn twice.
    /// </summary>
    [Theory]
    [InlineData(-1, 500)]
    [InlineData(0, 500)]
    public void NothingIsPlannedBeforeTheFirstPass(int post, int count)
    {
        var (range, why, _) = LiveTipPlan.For(post, count);
        Assert.Null(range);
        Assert.Equal(LiveTipPlan.Skip.NoPassYet, why);
    }

    /// <summary>A pass that has caught up leaves nothing to draw.</summary>
    [Theory]
    [InlineData(500, 500)]
    [InlineData(500, 499)]
    public void NothingIsPlannedOnceThePassHasCaughtUp(int post, int count)
    {
        var (range, why, _) = LiveTipPlan.For(post, count);
        Assert.Null(range);
        Assert.Equal(LiveTipPlan.Skip.NothingOutstanding, why);
    }
}

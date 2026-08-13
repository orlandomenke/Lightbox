using Lightbox.App.ViewModels;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The dirty-region bookkeeping invariant 6 rests on: what a publish must repaint,
/// read and reset together.
/// </summary>
/// <remarks>
/// <para>
/// <c>PublishState</c> was extracted from <c>MainViewModel</c> under Q77. Most of it is
/// guarded already — <c>DirtyRegionTests</c>, <c>StrokeLatencyTests</c> and the
/// live-preview pixel tests all fail if a publish repaints the wrong rectangle. What
/// they cannot see is the shape of the failure this class exists to prevent, because
/// both halves of it are silent in opposite directions:
/// </para>
/// <list type="bullet">
/// <item>Clear without reading → the next publish repaints nothing that changed, so the
/// artist's last dab does not appear until something else forces a full repaint.</item>
/// <item>Read without clearing → every later publish inherits a region it has already
/// painted, so the dirty rect only ever grows and painting stops being bounded work.</item>
/// </list>
/// <para>
/// Neither throws and neither is visibly wrong on a single stroke, which is why
/// <c>TakeDirty</c> is one method rather than the three statements it replaced.
/// </para>
/// </remarks>
public class PublishStateTests(Xunit.ITestOutputHelper output)
{
    /// <summary>A fresh state repaints everything — the safe default.</summary>
    [Fact]
    public void ItStartsWantingAFullRepaint()
    {
        var publish = new PublishState();

        Assert.True(publish.WholeCanvasDirty);
        Assert.True(publish.AnythingDirty);
        Assert.Null(publish.TakeDirty());   // null means "everything"
    }

    /// <summary>Taking the dirty region resets it, so the next publish starts clean.</summary>
    [Fact]
    public void TakingTheDirtyRegionResetsIt()
    {
        var publish = new PublishState();
        publish.TakeDirty();                             // clear the initial full-repaint
        publish.MarkDirty(new SKRectI(4, 4, 10, 10));

        var first = publish.TakeDirty();
        var second = publish.TakeDirty();

        output.WriteLine($"first take {first}, second take {second}");
        Assert.Equal(new SKRectI(4, 4, 10, 10), first);
        // The region is not inherited by the publish after it — this is the half that,
        // left undone, makes the dirty rect grow forever and painting unbounded.
        Assert.Null(second);
        Assert.False(publish.WholeCanvasDirty);
        Assert.False(publish.AnythingDirty);
    }

    /// <summary>Several marks between two publishes accumulate into one region.</summary>
    [Fact]
    public void MarksBetweenPublishesAccumulate()
    {
        var publish = new PublishState();
        publish.TakeDirty();

        publish.MarkDirty(new SKRectI(0, 0, 4, 4));
        publish.MarkDirty(new SKRectI(8, 8, 12, 12));

        var dirty = publish.TakeDirty();
        output.WriteLine($"union of the two marks: {dirty}");
        Assert.Equal(new SKRectI(0, 0, 12, 12), dirty);
    }

    /// <summary>
    /// A whole-canvas invalidation wins over any region, and a later region cannot
    /// narrow it back down.
    /// </summary>
    /// <remarks>
    /// This is the ordering that keeps stale pixels off the screen: once something has
    /// said "everything changed", a subsequent edit that happens to know its own bounds
    /// must not be able to shrink the repaint to just those bounds.
    /// </remarks>
    [Fact]
    public void AFullInvalidationCannotBeNarrowedByALaterRegion()
    {
        var publish = new PublishState();
        publish.TakeDirty();

        publish.MarkDirty(new SKRectI(0, 0, 4, 4));
        publish.InvalidateWholeCanvas();
        publish.MarkDirty(new SKRectI(20, 20, 24, 24));

        Assert.True(publish.WholeCanvasDirty);
        Assert.Null(publish.TakeDirty());
    }

    /// <summary>A full invalidation also forgets the frame on screen (B165).</summary>
    [Fact]
    public void AFullInvalidationForgetsTheFrameOnScreen()
    {
        var publish = new PublishState
        {
            LastPublished = new Lightbox.App.Rendering.FrameFingerprint(0, 1.0, null, null, LayerCount: 2),
        };

        publish.InvalidateWholeCanvas();

        Assert.Null(publish.LastPublished);
    }

    /// <summary>
    /// The mid-publish repaint does <em>not</em> forget the frame on screen, which is the
    /// one line separating it from a full invalidation.
    /// </summary>
    /// <remarks>
    /// The fold transition uses this. Collapsing the two methods into one would clear the
    /// fingerprint on a publish that is about to set it anyway — equivalent today, and
    /// equivalent only because there is no early return between the two points in
    /// <c>PublishSnapshot</c>. This test is what makes the distinction survive somebody
    /// deciding the two methods are redundant.
    /// </remarks>
    [Fact]
    public void TheMidPublishRepaintKeepsTheFingerprint()
    {
        var fingerprint = new Lightbox.App.Rendering.FrameFingerprint(3, 1.0, null, null, LayerCount: 2);
        var publish = new PublishState { LastPublished = fingerprint };
        publish.TakeDirty();

        publish.RepaintEverythingThisPublish();

        Assert.True(publish.WholeCanvasDirty);
        Assert.Equal(fingerprint, publish.LastPublished);
    }

    /// <summary>Sequence numbers only ever go up, so the canvas can order snapshots.</summary>
    [Fact]
    public void SequenceNumbersIncreaseMonotonically()
    {
        var publish = new PublishState();

        var seen = new List<long>();
        for (var i = 0; i < 5; i++) seen.Add(publish.NextSequence());

        output.WriteLine($"sequence: {string.Join(", ", seen)}");
        Assert.Equal([1L, 2L, 3L, 4L, 5L], seen);
    }

    /// <summary>
    /// Marking a region while nothing is dirty at all still registers it.
    /// </summary>
    /// <remarks>
    /// The guard in <c>MarkDirty</c> is on <see cref="PublishState.WholeCanvasDirty"/>,
    /// not on "is anything dirty" — a mark arriving when the slate is clean is the
    /// ordinary case for the second and later strokes of a session, and an over-eager
    /// early return here would drop them.
    /// </remarks>
    [Fact]
    public void AMarkOnACleanSlateIsRegistered()
    {
        var publish = new PublishState();
        publish.TakeDirty();
        Assert.False(publish.AnythingDirty);

        publish.MarkDirty(new SKRectI(2, 2, 6, 6));

        Assert.True(publish.AnythingDirty);
        Assert.Equal(new SKRectI(2, 2, 6, 6), publish.TakeDirty());
    }
}

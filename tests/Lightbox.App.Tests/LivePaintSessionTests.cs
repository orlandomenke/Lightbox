using Lightbox.App.ViewModels;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The live-stroke state pools its bitmaps and resets completely, and both of those
/// fail silently when broken.
/// </summary>
/// <remarks>
/// <para>
/// <c>LivePaintSession</c> was extracted from <c>MainViewModel</c> under Q77. The
/// extraction itself is guarded by the 71 test files that construct a view model and
/// paint through it — if a field had been dropped or mis-wired, a live-preview test
/// would have gone red. What those tests do <b>not</b> guard is the part with no
/// visible symptom, and that is what this file is for.
/// </para>
/// <para>
/// <b>Both cases here are mistakes that were actually made while writing the class,
/// not hypotheticals.</b> The first draft of <c>ResetPostProcess</c> disposed
/// <c>PostScratch</c> instead of wiping the region the last stroke used. Every test
/// passed. On a 4K canvas that bitmap is 33 MB, so the only symptom would have been a
/// 33 MB allocation on every pen-down — invisible to the suite, felt by an artist as
/// the first dab of each stroke hitching. The second draft replaced Skia's mutating
/// <c>SKRectI.Union</c> with hand-rolled min/max that special-cased empty rects, which
/// changes which pixels get repainted and, again, nothing went red.
/// </para>
/// <para>
/// The general shape: <b>this class's whole job is to make expensive things cheap by
/// keeping them alive.</b> A correctness test cannot see the difference between
/// keeping a buffer and reallocating it, so the pooling has to be asserted directly
/// or it is not guarded at all.
/// </para>
/// </remarks>
public class LivePaintSessionTests(Xunit.ITestOutputHelper output)
{
    /// <summary>
    /// The post-process draft is wiped and kept, never dropped — the mistake above.
    /// </summary>
    [Fact]
    public void ResettingThePostProcessKeepsThePooledBitmap()
    {
        var live = new LivePaintSession();
        live.PostScratch = new SKBitmap(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul));
        live.PostUsed = new SKRectI(0, 0, 16, 16);
        var pooled = live.PostScratch;

        live.ResetPostProcess();

        output.WriteLine($"PostScratch after reset: {(live.PostScratch is null ? "null" : "kept")}");
        Assert.NotNull(live.PostScratch);
        Assert.Same(pooled, live.PostScratch);
        Assert.False(pooled.Handle == IntPtr.Zero, "the pooled bitmap was disposed rather than wiped");

        // The bookkeeping does reset, and PostQueued deliberately does not — it tracks
        // a pass in flight on the dispatcher, which this method has no business cancelling.
        Assert.Null(live.PostUsed);
        Assert.Equal(-1, live.PostStampedPoints);
        Assert.Equal(0, live.PostCostMs);
    }

    /// <summary>
    /// Resetting bumps the generation, so a worker pass that outlives its stroke is
    /// recognised as stale (B189).
    /// </summary>
    /// <remarks>
    /// This counter arrived with PR222's asynchronous live post-process and moved in here
    /// when the state did, because the only thing that invalidates in-flight work is this
    /// state being reset — the two must not be separable. The failure it prevents is a
    /// result landing after pen-up and resurrecting a preview the commit already replaced,
    /// which shows as the wet pass reappearing over finished art.
    /// </remarks>
    [Fact]
    public void ResettingThePostProcessInvalidatesWorkStillInFlight()
    {
        var live = new LivePaintSession();
        var before = live.PostGeneration;

        live.ResetPostProcess();
        Assert.NotEqual(before, live.PostGeneration);

        // And through the effect-state reset too, which is what EndStroke calls.
        var mid = live.PostGeneration;
        live.ClearEffectState();
        Assert.NotEqual(mid, live.PostGeneration);
    }

    /// <summary>
    /// The wiped region is actually transparent again, so a following stroke does not
    /// inherit the previous one's pixels.
    /// </summary>
    [Fact]
    public void ResettingThePostProcessClearsTheRegionTheLastStrokeUsed()
    {
        var live = new LivePaintSession();
        var bmp = new SKBitmap(new SKImageInfo(32, 32, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bmp)) canvas.Clear(SKColors.Red);
        live.PostScratch = bmp;
        live.PostUsed = new SKRectI(0, 0, 8, 8);

        live.ResetPostProcess();

        var inside = bmp.GetPixel(2, 2);
        var outside = bmp.GetPixel(20, 20);
        output.WriteLine($"inside the used region {inside}, outside {outside}");
        Assert.Equal(0, inside.Alpha);
        Assert.Equal(255, outside.Alpha);   // only the used region is touched — invariant 6
    }

    /// <summary>
    /// <c>ClearEffectState</c> leaves nothing behind. B39 is what happens when it does.
    /// </summary>
    [Fact]
    public void ClearingTheEffectStateLeavesNothingBehind()
    {
        var live = new LivePaintSession();
        live.Composite = new SKBitmap(4, 4);
        live.EffectBase = new SKBitmap(4, 4);
        live.StampedCount = 7;
        live.DabCount = 9;
        live.StableDabs = 3;
        live.TailRegion = new SKRectI(1, 1, 2, 2);
        live.Dabs = [];
        live.EffectDabs = [];
        live.EffectSettled = 5;
        live.SmudgeRegion = new SKRectI(0, 0, 1, 1);
        live.PostStampedPoints = 4;

        live.ClearEffectState();

        Assert.Null(live.Composite);
        Assert.Null(live.EffectBase);
        Assert.Equal(0, live.StampedCount);
        Assert.Equal(0, live.DabCount);
        Assert.Equal(0, live.StableDabs);
        Assert.Null(live.TailRegion);
        Assert.Null(live.Dabs);
        Assert.Null(live.EffectDabs);
        Assert.Equal(0, live.EffectSettled);
        Assert.Null(live.SmudgeRegion);
        Assert.Equal(-1, live.PostStampedPoints);   // it resets the post-process too
    }

    /// <summary>
    /// The smudge backup bitmap survives <c>ClearEffectState</c> on purpose: it is only
    /// ever written before it is read, so keeping it saves an allocation per stroke
    /// without any state surviving.
    /// </summary>
    [Fact]
    public void ClearingTheEffectStateKeepsTheSmudgeBackupBuffer()
    {
        var live = new LivePaintSession();
        live.SmudgeBackup = new SKBitmap(8, 8);
        var pooled = live.SmudgeBackup;

        live.ClearEffectState();

        Assert.Same(pooled, live.SmudgeBackup);
    }

    /// <summary>The document-sized scratch is reused at the same size and rebuilt when it changes.</summary>
    [Fact]
    public void TheScratchIsPooledAcrossStrokesAndRebuiltOnResize()
    {
        var live = new LivePaintSession();

        live.EnsureScratch(128, 96);
        var first = live.Scratch;
        var firstCanvas = live.ScratchCanvas;
        Assert.NotNull(first);
        Assert.Equal(128, first!.Width);

        live.EnsureScratch(128, 96);
        output.WriteLine($"same size: {(ReferenceEquals(first, live.Scratch) ? "reused" : "REALLOCATED")}");
        Assert.Same(first, live.Scratch);
        Assert.Same(firstCanvas, live.ScratchCanvas);

        live.EnsureScratch(256, 96);
        output.WriteLine($"resized: {(ReferenceEquals(first, live.Scratch) ? "REUSED" : "reallocated")}");
        Assert.NotSame(first, live.Scratch);
        Assert.Equal(256, live.Scratch!.Width);
        Assert.Null(live.ScratchUsed);
    }

    /// <summary>
    /// <c>UnionRect</c> keeps Skia's own semantics rather than an "obviously equivalent"
    /// rewrite, because the dirty-region arithmetic has always been measured against them.
    /// </summary>
    [Fact]
    public void UnionKeepsSkiasOwnSemanticsAndDoesNotMutateItsArguments()
    {
        var a = new SKRectI(10, 10, 20, 20);
        var b = new SKRectI(15, 5, 30, 12);

        var union = LivePaintSession.UnionRect(a, b);

        var expected = a;
        expected.Union(b);
        output.WriteLine($"union {union}, Skia says {expected}");
        Assert.Equal(expected, union);

        // Taken by value: Skia's Union mutates, and a caller passing its own dirty rect
        // must not find it changed underneath.
        Assert.Equal(new SKRectI(10, 10, 20, 20), a);
    }

    /// <summary>
    /// An empty rect is a corner, not "nothing" — the one case where the obvious
    /// rewrite of this helper diverges.
    /// </summary>
    /// <remarks>
    /// This is the assertion that makes the previous test worth having. Skia's union is a
    /// plain min/max over both corners, so a default <see cref="SKRectI"/> — empty, at the
    /// origin — drags the result out to the origin. An implementation that skipped empty
    /// rects would return the tighter <c>(5,5,9,9)</c> here and pass every other test in
    /// this file. Measured both ways before it was written down.
    /// </remarks>
    [Fact]
    public void AnEmptyRectIsACornerAndNotNothing()
    {
        var union = LivePaintSession.UnionRect(default, new SKRectI(5, 5, 9, 9));

        output.WriteLine($"default ∪ (5,5,9,9) = {union}  (the empty-skipping rewrite gives 5,5,9,9)");
        Assert.Equal(new SKRectI(0, 0, 9, 9), union);
        Assert.NotEqual(new SKRectI(5, 5, 9, 9), union);
    }

    /// <summary>A null region clears everything; a region clears only itself.</summary>
    [Fact]
    public void ClearRegionClearsOnlyTheRegionGivenOrAllOfItWhenNull()
    {
        var bmp = new SKBitmap(new SKImageInfo(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bmp)) canvas.Clear(SKColors.Blue);
        using (var canvas = new SKCanvas(bmp)) LivePaintSession.ClearRegion(canvas, new SKRectI(0, 0, 4, 4));
        Assert.Equal(0, bmp.GetPixel(1, 1).Alpha);
        Assert.Equal(255, bmp.GetPixel(10, 10).Alpha);

        using (var canvas = new SKCanvas(bmp)) LivePaintSession.ClearRegion(canvas, null);
        Assert.Equal(0, bmp.GetPixel(10, 10).Alpha);
    }
}

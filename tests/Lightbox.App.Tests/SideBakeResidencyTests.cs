using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B198's wall, scaled to suite size: a document whose single-frame working
/// set outgrows the frame-cache budget. Two promises are pinned here — the
/// picture stays right while the cache thrashes, and the thrashing stops the
/// moment the side bakes serve.
/// </summary>
/// <remarks>
/// <para>
/// The arithmetic, so the scaling is checkable: B198 measured 100 layers of
/// 1080p (~8.3 MB a cel, ~830 MB a frame) against the 512 MB budget. Here a
/// 960×540 cel is ~2 MB, eight layers are ~16 MB of frame, and the budget is
/// pushed down to 5 MB — the same shape, three orders of magnitude cheaper.
/// </para>
/// <para>
/// In the <c>FrameCacheBudget</c> collection because the budget is
/// process-wide, and on <see cref="BrushStateIsolated"/> because the tests
/// construct view models and draw.
/// </para>
/// </remarks>
[Collection("FrameCacheBudget")]
public sealed class SideBakeResidencyTests(ITestOutputHelper output) : BrushStateIsolated
{
    private readonly long _budget = FrameBitmapCache.ByteBudget;

    public override void Dispose()
    {
        FrameBitmapCache.ByteBudget = _budget;
        base.Dispose();
    }

    /// <summary>Eight layers, a distinct vertical stroke on each, active in the middle.</summary>
    private static MainViewModel OverBudgetVm(bool foldEnabled)
    {
        var vm = VmLayers.PaperVm();
        vm.StackBake.Enabled = foldEnabled;
        vm.SmoothStrokes = false;
        for (var i = 0; i < 7; i++) vm.AddPaintedLayerCommand.Execute(null);
        for (var i = 1; i <= 8; i++)
        {
            vm.ActiveLayerIndex = i;
            vm.BeginStroke(80 * i, 100, 1);
            vm.MoveStroke(80 * i, 400, 1);
            vm.EndStroke();
        }
        vm.ActiveLayerIndex = 4;
        return vm;
    }

    private static SKBitmap Published(MainViewModel vm)
    {
        SKBitmap? grabbed = null;
        void Capture(RenderSnapshot s)
        {
            using var img = s.Materialise(null);
            var bmp = new SKBitmap(img.Width, img.Height);
            img.ReadPixels(bmp.Info, bmp.GetPixels(), bmp.RowBytes, 0, 0);
            grabbed = bmp;
        }
        vm.SnapshotChanged += Capture;
        try
        {
            vm.PublishSnapshot();
        }
        finally
        {
            vm.SnapshotChanged -= Capture;
        }
        return grabbed ?? throw new InvalidOperationException("nothing was published");
    }

    /// <summary>
    /// A publish materializes its whole pass list before compositing, and a
    /// fetch can evict an earlier fetch of the same publish once the working
    /// set is over budget. The evicted bitmap is still referenced by the pass
    /// list — freeing it there was a use-after-free the composite drew from.
    /// The fetch-time pins close that window; this asserts through the pixels,
    /// because a disposed pass either throws or draws garbage.
    /// </summary>
    [AvaloniaFact]
    public void APublishWhoseWorkingSetOutgrowsTheCacheStillDrawsEveryLayer()
    {
        FrameBitmapCache.ByteBudget = 5L * 1024 * 1024;
        // Fold off: every publish materializes all eight layers, which is the
        // path that walked over the freed bitmap.
        var vm = OverBudgetVm(foldEnabled: false);

        using var shown = Published(vm);
        var scale = shown.Width / (double)vm.Doc.Scene.Width;
        for (var i = 1; i <= 8; i++)
        {
            var px = shown.GetPixel((int)(80 * i * scale), (int)(250 * scale));
            Assert.True(px.Red < 128 && px.Green < 128 && px.Blue < 128,
                $"layer {i}'s stroke is missing ({px}) — a pass bitmap was evicted mid-publish");
        }
        output.WriteLine(
            $"working set ~16 MB against a 5 MB budget: all 8 strokes present, " +
            $"evictions {vm.FrameCache.Evictions}");
        Assert.True(vm.FrameCache.Evictions > 0,
            "arrange failed: nothing was evicted, so the working set fit the budget");
    }

    /// <summary>
    /// B198 itself: once the side bakes serve, the covered cels stop being
    /// fetched, so a recomposite stops re-rasterizing what the last one
    /// evicted. Misses per publish drop to the active layer's own — which
    /// fits any budget — instead of the whole stack.
    /// </summary>
    [AvaloniaFact]
    public void AWorkingSetOverTheBudgetStopsThrashingOnceTheSidesAreBaked()
    {
        FrameBitmapCache.ByteBudget = 5L * 1024 * 1024;
        var vm = OverBudgetVm(foldEnabled: true);

        // Publish 1 sees the keys, publish 2 pays for the bakes.
        vm.PublishSnapshot();
        vm.PublishSnapshot();
        Assert.True(vm.StackBake.Rebuilds >= 1, "arrange failed: the sides never baked");

        vm.FrameCache.ResetCounters();
        const int rounds = 4;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < rounds; i++) vm.PublishSnapshot();
        sw.Stop();

        var missesPerPublish = vm.FrameCache.Misses / (double)rounds;
        output.WriteLine(
            $"after the bake: {vm.FrameCache.Misses} misses over {rounds} publishes " +
            $"({missesPerPublish:F1}/publish, {sw.Elapsed.TotalMilliseconds / rounds:F1} ms/publish), " +
            $"{vm.StackBake.FetchesSkipped} fetches skipped");
        // Unbaked, every publish misses on most of the eight cels (the budget
        // holds two). Served, only the active layer's cel is fetched at all,
        // and it stays resident. One stray miss per publish is tolerated so
        // the assertion pins the mechanism rather than the exact cache state.
        Assert.True(missesPerPublish <= 1.0,
            $"{missesPerPublish:F1} misses per publish with the sides baked — covered cels are still being fetched");
        Assert.True(vm.StackBake.FetchesSkipped > 0, "no fetches were skipped");

        // And the control: the same document without the fold keeps paying
        // the whole stack on every recomposite — the wall this exists to
        // dissolve, shown against the same budget.
        var control = OverBudgetVm(foldEnabled: false);
        control.PublishSnapshot();
        control.PublishSnapshot();
        control.FrameCache.ResetCounters();
        var controlSw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < rounds; i++) control.PublishSnapshot();
        controlSw.Stop();
        var controlMisses = control.FrameCache.Misses / (double)rounds;
        output.WriteLine(
            $"control without the fold: {controlMisses:F1} misses/publish, " +
            $"{controlSw.Elapsed.TotalMilliseconds / rounds:F1} ms/publish");
        Assert.True(controlMisses > missesPerPublish,
            "the control stopped thrashing too — this test is no longer measuring the fold");
    }
}

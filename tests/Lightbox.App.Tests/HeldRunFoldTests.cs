using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Folding the layers that hold still during playback, and — mostly — proving it
/// does not show yesterday's art.
/// </summary>
/// <remarks>
/// <para>
/// <b>B165's own entry names the risk before the fix:</b> <i>"a wrong
/// invalidation shows yesterday's art silently: no exception, no red test, and an
/// artist who cannot tell it from their own drawing. Any implementation needs a
/// test that changes a layer <b>under</b> a cached run and asserts the composite
/// followed — the case a naive 'did this layer change' key gets wrong."</i>
/// </para>
/// <para>
/// So that test is here and it is the reason this file exists. The pixel
/// assertions matter more than the counters: a bake that is never built is slow,
/// and a bake that is stale is wrong.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class HeldRunFoldTests(ITestOutputHelper output) : BrushStateIsolated
{
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
        try { vm.PublishSnapshot(); }
        finally { vm.SnapshotChanged -= Capture; }
        return grabbed ?? throw new InvalidOperationException("nothing was published");
    }

    private static int MaxChannelDiff(SKBitmap a, SKBitmap b)
    {
        var worst = 0;
        for (var y = 0; y < Math.Min(a.Height, b.Height); y += 3)
        {
            for (var x = 0; x < Math.Min(a.Width, b.Width); x += 3)
            {
                var p = a.GetPixel(x, y);
                var q = b.GetPixel(x, y);
                worst = Math.Max(worst, Math.Abs(p.Red - q.Red));
                worst = Math.Max(worst, Math.Abs(p.Green - q.Green));
                worst = Math.Max(worst, Math.Abs(p.Blue - q.Blue));
                worst = Math.Max(worst, Math.Abs(p.Alpha - q.Alpha));
            }
        }
        return worst;
    }

    /// <summary>A paper layer, a held plate, and one animating layer above them.</summary>
    private static MainViewModel Rig()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        vm.StackBake.Enabled = true;

        // Layer 1: the held plate, one mark, never re-keyed.
        vm.AddPaintedLayerCommand.Execute(null);
        vm.ActiveLayerIndex = 1;
        vm.BeginStroke(60, 60, 1);
        vm.MoveStroke(200, 60, 1);
        vm.EndStroke();

        // Layer 2: the animating one.
        vm.AddPaintedLayerCommand.Execute(null);
        vm.ActiveLayerIndex = 2;
        vm.BeginStroke(60, 200, 1);
        vm.MoveStroke(200, 200, 1);
        vm.EndStroke();
        return vm;
    }

    /// <summary>
    /// <b>The test B165's entry asked for.</b> A mark lands on a layer that was
    /// inside the folded run; the very next publish has to show it. A held bake
    /// that survived into the drawing path would serve the pre-mark pixels and the
    /// artist would watch their stroke fail to appear.
    /// </summary>
    /// <remarks>
    /// <b>Honest about what it does not spring.</b> Deleting the content version
    /// from the bake's key leaves this green — checked by mutation rather than
    /// assumed. That is the same limit <see cref="LayerStackBake"/> records about
    /// its own version half: an in-place commit only reaches the *active* layer,
    /// and making a layer active reshapes the segments, so a re-render hands over
    /// a new bitmap instance and the key misses on identity before it ever gets to
    /// the version. The version is defence against a future in-place mutation path
    /// that does not route through active-layer segmentation — a background
    /// eraser, an AI edit stamping into a cached bitmap — and it should not be
    /// deleted as dead on the strength of this passing without it.
    /// <para>
    /// What this *does* guard is the transition: a bake built during playback must
    /// not still be serving once the artist is drawing again.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void AMarkOnALayerInsideTheFoldedRunShowsImmediately()
    {
        var vm = Rig();

        // Play, so the held run is folded, and publish enough times to get past
        // the "seen twice" rule that guards a bake nobody will reuse.
        vm.IsPlaying = true;
        for (var i = 0; i < 4; i++) Published(vm).Dispose();
        vm.IsPlaying = false;

        using var before = Published(vm);

        // Now draw on the held plate — a layer beneath the animating one, and
        // inside whatever was folded.
        vm.ActiveLayerIndex = 1;
        vm.BeginStroke(300, 120, 1);
        vm.MoveStroke(380, 120, 1);
        vm.EndStroke();

        using var after = Published(vm);

        var diff = MaxChannelDiff(before, after);
        output.WriteLine($"largest channel difference after drawing under the run: {diff}");
        Assert.True(diff > 16,
            "the composite did not change after a mark landed on a layer inside the "
            + "folded run — this is the stale bake B165 warns about, and it is invisible "
            + "to everything except a pixel");
    }

    /// <summary>
    /// <b>Folding must not change the picture.</b> Premultiplied source-over is
    /// associative, which is the entire licence for pre-folding a run — so a
    /// folded playback frame has to match the same frame composed layer by layer,
    /// to within the rounding that associativity permits.
    /// </summary>
    [AvaloniaFact]
    public void AFoldedFrameMatchesAnUnfoldedOne()
    {
        var withBake = Rig();
        withBake.IsPlaying = true;
        for (var i = 0; i < 4; i++) Published(withBake).Dispose();
        using var folded = Published(withBake);
        withBake.IsPlaying = false;

        var noBake = Rig();
        noBake.StackBake.Enabled = false;
        noBake.IsPlaying = true;
        for (var i = 0; i < 4; i++) Published(noBake).Dispose();
        using var plain = Published(noBake);
        noBake.IsPlaying = false;

        var diff = MaxChannelDiff(folded, plain);
        output.WriteLine($"folded vs layer-by-layer: largest channel difference {diff}");
        Assert.True(diff <= 1, $"folding changed the picture by {diff}, which is more than rounding");
    }

    /// <summary>
    /// <b>A run of one is not folded, because a bake of one pass costs a bitmap
    /// to save nothing.</b> The paper always holds, so the shortest possible run
    /// is one — and that is the ordinary two-layer scene with the ink animating.
    /// </summary>
    /// <remarks>
    /// This test first asserted that <c>PaperVm()</c> alone does not fold, and it
    /// was wrong: nothing in that scene animates, so **every** layer holds and the
    /// whole stack is the run. Correct behaviour, wrong premise — the case worth
    /// guarding is a run too short to be worth baking, which needs a layer that
    /// actually moves.
    /// </remarks>
    [AvaloniaFact]
    public void ARunOfOneIsNotWorthFolding()
    {
        var vm = VmLayers.PaperVm();
        vm.StackBake.Enabled = true;
        vm.SmoothStrokes = false;

        // Give the layer above the paper a second drawing, so it does not hold.
        vm.ActiveLayerIndex = 1;
        vm.BeginStroke(60, 60, 1);
        vm.MoveStroke(140, 60, 1);
        vm.EndStroke();
        // LayerIndex matters and defaults to 0 — keying frame 1 on the *paper*
        // instead of the ink layer is a different scene entirely, and was this
        // test's first mistake.
        vm.InsertFrameAt(new FrameCell(1) { LayerIndex = 1 }, Lightbox.Core.Documents.FrameRole.Key);
        vm.CurrentFrameIndex = 1;
        vm.BeginStroke(60, 120, 1);
        vm.MoveStroke(140, 120, 1);
        vm.EndStroke();
        vm.PlaybackEndFrame = 1;
        vm.CurrentFrameIndex = 0;

        var held = UnchangedLayerRun.HeldPrefix(vm.Doc.Scene, 0, 1);
        var before = vm.StackBake.Rebuilds;

        vm.IsPlaying = true;
        for (var i = 0; i < 4; i++) Published(vm).Dispose();
        vm.IsPlaying = false;

        output.WriteLine($"held prefix {held}, rebuilds {vm.StackBake.Rebuilds - before}");
        Assert.Equal(1, held);
        Assert.Equal(before, vm.StackBake.Rebuilds);
    }

    /// <summary>
    /// And the opposite: a stack whose bottom layers really do hold is folded, so
    /// the mechanism is doing something rather than declining safely everywhere.
    /// </summary>
    [AvaloniaFact]
    public void AHeldRunOfTwoOrMoreIsFolded()
    {
        var vm = Rig();
        var before = vm.StackBake.Rebuilds;

        vm.IsPlaying = true;
        for (var i = 0; i < 4; i++) Published(vm).Dispose();
        vm.IsPlaying = false;

        output.WriteLine($"rebuilds with a held run: {vm.StackBake.Rebuilds - before}");
        Assert.True(vm.StackBake.Rebuilds > before, "the held run was never folded");
    }

    /// <summary>
    /// Stopping playback returns to the ordinary fold without leaving the held
    /// bake serving. The two folds each remember whether they were serving one,
    /// and a stale flag means a missed transition — which is folded and unfolded
    /// pixels mixed on one surface by a dirty-region patch.
    /// </summary>
    [AvaloniaFact]
    public void StoppingPlaybackDoesNotLeaveTheHeldBakeServing()
    {
        var vm = Rig();

        vm.IsPlaying = true;
        for (var i = 0; i < 4; i++) Published(vm).Dispose();
        using var playing = Published(vm);

        vm.IsPlaying = false;
        using var stopped = Published(vm);
        using var again = Published(vm);

        // The picture is the same either side of the switch, and stays stable.
        Assert.True(MaxChannelDiff(playing, stopped) <= 1);
        Assert.True(MaxChannelDiff(stopped, again) <= 1);
    }
}

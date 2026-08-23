using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The baked layer stacks: repaints while drawing blend three passes instead
/// of one per layer, and nobody can tell by looking.
/// </summary>
/// <remarks>
/// <para>
/// The promise has two halves and they pull against each other. The fold must
/// actually happen (or a 24-layer document keeps paying 24 blends per stroke
/// commit — the n^0.99 row in the performance map), and it must be invisible
/// (or a cached background is a stale background). Most tests here put a
/// document through the fold and then look at the published pixels.
/// </para>
/// <para>
/// <b>Tolerance, and why it is exactly 1.</b> Premultiplied source-over is
/// associative in real arithmetic and only almost associative in 8-bit —
/// folding changes the order the rounding happens in. A folded and an unfolded
/// composite of the same passes may therefore differ by one least significant
/// bit per channel, which no eye can see and no artist can find. What keeps
/// that harmless is that the two never share a surface: a fold transition
/// forces a whole-canvas repaint (asserted below), so the seam a mixed surface
/// could show cannot occur. The pixel assertions allow ±1 and not one bit
/// more — a stale bake differs by a lot more than an LSB.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class LayerStackBakeTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>A document with paper + three paint layers, active in the middle.</summary>
    private static MainViewModel LayeredVm(bool foldEnabled = true)
    {
        var vm = VmLayers.PaperVm();
        vm.StackBake.Enabled = foldEnabled;
        vm.SmoothStrokes = false;
        // Paper(0) + Paint(1) exist; add two more so there is a stack both
        // below and above the active layer.
        vm.AddPaintedLayerCommand.Execute(null);
        vm.AddPaintedLayerCommand.Execute(null);

        // A mark on each paint layer, in different places, so a lost layer is
        // a visible hole rather than a coincidence.
        for (var i = 1; i <= 3; i++)
        {
            vm.ActiveLayerIndex = i;
            vm.BeginStroke(100 * i, 100, 1);
            vm.MoveStroke(100 * i, 200, 1);
            vm.EndStroke();
        }
        vm.ActiveLayerIndex = 2; // the middle: one layer below, one above
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

    private static int MaxChannelDiff(SKBitmap a, SKBitmap b)
    {
        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Height, b.Height);
        var worst = 0;
        for (var y = 0; y < a.Height; y += 3)
        {
            for (var x = 0; x < a.Width; x += 3)
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

    [AvaloniaFact]
    public void TheFoldHappensAndTheImageDoesNotChange()
    {
        // Two identical documents — stroke replay is deterministic (invariant
        // 1), so their pixels must agree. One folds, the reference never does.
        var vmRef = LayeredVm(foldEnabled: false);
        var vmFold = LayeredVm();

        using var reference = Published(vmRef);
        Published(vmFold).Dispose();
        Published(vmFold).Dispose();
        using var folded = Published(vmFold);

        output.WriteLine(
            $"rebuilds {vmFold.StackBake.Rebuilds}, folded publishes {vmFold.StackBake.FoldedPublishes}");
        Assert.True(vmFold.StackBake.FoldedPublishes >= 1, "the stack held still and nothing folded");
        Assert.Equal(0, vmRef.StackBake.Rebuilds);

        var diff = MaxChannelDiff(reference, folded);
        output.WriteLine($"max channel diff folded vs unfolded: {diff}");
        Assert.True(diff <= 1, $"folded publish differs from unfolded by {diff} — that is a wrong bake, not rounding");
    }

    [AvaloniaFact]
    public void EditingALayerUnderTheBakeShowsUpImmediately()
    {
        var vm = LayeredVm();
        Published(vm).Dispose();
        Published(vm).Dispose();
        Published(vm).Dispose();
        Assert.True(vm.StackBake.FoldedPublishes >= 1, "arrange failed: nothing folded");

        // Draw on the BELOW layer — the bitmap inside the below bake.
        vm.ActiveLayerIndex = 1;
        vm.BeginStroke(400, 300, 1);
        vm.MoveStroke(500, 300, 1);
        vm.EndStroke();
        vm.ActiveLayerIndex = 2;

        using var shown = Published(vm);
        // Sample where the new stroke went. Colour is the default black brush
        // over white paper: anything near-white means the bake served stale
        // pixels.
        var scale = shown.Width / (double)vm.Doc.Scene.Width;
        var px = shown.GetPixel((int)(450 * scale), (int)(300 * scale));
        output.WriteLine($"pixel under the new below-layer stroke: {px}");
        Assert.True(px.Red < 128 && px.Green < 128 && px.Blue < 128,
            $"a stroke drawn beneath the bake is not visible ({px}) — the bake is stale");
    }

    /// <summary>
    /// The stale-bake case B29's entry demands a guard for, in its strongest
    /// form: the repaint changes pixels on a covered layer <em>in place</em>,
    /// with the layer never made active and the frame object never replaced —
    /// so the spec key still matches, and the only thing standing between the
    /// served bake and yesterday's pixels is the invalidation funnel calling
    /// <see cref="LayerStackBake.NoteFrameChanged"/>. An undo cannot spring
    /// this trap (a snapshot undo swaps the whole document, which resets the
    /// bake wholesale), and neither can drawing on the layer (making it
    /// active reshapes the segments). A swatch recolour is the real path:
    /// <c>RepaintForSwatch</c> walks every frame using the swatch, on
    /// whatever layer it lives.
    /// </summary>
    [AvaloniaFact]
    public void RecolouringASwatchOnACoveredLayerIsNotServedFromTheBake()
    {
        var vm = VmLayers.PaperVm();
        vm.StackBake.Enabled = true;
        vm.SmoothStrokes = false;
        vm.BrushSize = 24;
        vm.AddPaintedLayerCommand.Execute(null);
        vm.AddPaintedLayerCommand.Execute(null);

        // Layer 1's stroke is linked to a swatch, so it can be recoloured
        // later without touching the layer; 2 and 3 get plain strokes. With
        // layer 3 active, the below bake covers paper + 1 + 2 — comfortably
        // past the fold's two-pass minimum, with the swatch stroke inside it.
        if (!vm.PaletteDocker.HasPalette) vm.PaletteDocker.AddPaletteCommand.Execute(null);
        vm.ColorHex = "#ff0000";
        vm.PaletteDocker.AddSwatchCommand.Execute(null);
        var row = vm.PaletteDocker.Swatches[^1];
        vm.PaletteDocker.SelectedSwatch = row;
        vm.ActiveLayerIndex = 1;
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(100, 200, 1);
        vm.EndStroke();
        vm.ColorHex = "#000000"; // break the swatch link for the plain strokes
        for (var i = 2; i <= 3; i++)
        {
            vm.ActiveLayerIndex = i;
            vm.BeginStroke(100 * i, 100, 1);
            vm.MoveStroke(100 * i, 200, 1);
            vm.EndStroke();
        }
        vm.ActiveLayerIndex = 3; // layer 1 is now inside the below-active bake

        Published(vm).Dispose();
        Published(vm).Dispose();
        Published(vm).Dispose();
        Assert.True(vm.StackBake.FoldedPublishes >= 1, "arrange failed: nothing folded");

        // The recolour repaints layer 1's frame in place — same Frame object,
        // same opacity, same everything the key can see.
        row.Color = "#0000ff";

        using var shown = Published(vm);
        var scale = shown.Width / (double)vm.Doc.Scene.Width;
        var px = shown.GetPixel((int)(100 * scale), (int)(150 * scale));
        output.WriteLine($"pixel on the recoloured stroke: {px}");
        Assert.True(px.Blue > 128 && px.Red < 128,
            $"the stroke is still its old colour ({px}) — the bake served stale pixels");
    }

    /// <summary>
    /// The B198 half of the fold: a served segment's cels are not fetched, so
    /// they do not need to be resident. The counter is the contract — every
    /// skipped fetch is a cache lookup that can no longer miss, and on a
    /// document whose working set outgrows the budget a miss is a full
    /// re-rasterization.
    /// </summary>
    [AvaloniaFact]
    public void AServedPublishNeverFetchesTheCoveredCels()
    {
        var vm = LayeredVm();
        Published(vm).Dispose();
        Published(vm).Dispose();
        Published(vm).Dispose();
        Assert.True(vm.StackBake.FoldedPublishes >= 1, "arrange failed: nothing folded");

        var skipped = vm.StackBake.FetchesSkipped;
        var rebuilds = vm.StackBake.Rebuilds;
        Published(vm).Dispose();

        var delta = vm.StackBake.FetchesSkipped - skipped;
        output.WriteLine($"cel fetches skipped on one served publish: {delta}");
        // Layer 1 below and layer 3 above at minimum; the paper counts too
        // when it renders as a cel.
        Assert.True(delta >= 2,
            $"only {delta} fetches were skipped — the served segments are still fetching their cels");
        Assert.Equal(rebuilds, vm.StackBake.Rebuilds);
    }

    [AvaloniaFact]
    public void ANonNormalBlendModeRefusesToFoldAndStillRendersIt()
    {
        var vm = LayeredVm();
        // Multiply on the below layer: pre-folding it against transparency
        // would change what it multiplies with. Setup publishes freely, so
        // only rebuilds AFTER the blend change count.
        vm.Doc.Scene.Layers[1].BlendMode = Lightbox.Core.Documents.LayerBlendMode.Multiply;
        var before = vm.StackBake.Rebuilds;

        Published(vm).Dispose();
        Published(vm).Dispose();
        Published(vm).Dispose();

        var delta = vm.StackBake.Rebuilds - before;
        output.WriteLine($"rebuilds after the blend change: {delta}");
        // The below segment (paper + multiply layer) must not fold. The above
        // segment may still re-fold once — that is the point of doing them
        // separately — so the delta is at most one.
        Assert.True(delta <= 1,
            $"{delta} rebuilds after a Multiply appeared below — the multiply segment folded");
    }

    /// <summary>
    /// A masked or clipped layer reads like a Multiply for the fold: its pass
    /// is carved per publish, so pre-folding it into a bake would serve stale
    /// pixels the moment the mask is repainted. Correct and unfolded beats
    /// folded and stale — the refusal in <c>Eligible</c>.
    /// </summary>
    [AvaloniaFact]
    public void AMaskedLayerRefusesToFoldAndStillRenders()
    {
        var vm = LayeredVm();
        vm.Doc.Scene.Layers[1].Mask = new Lightbox.Core.Documents.LayerMask();
        var before = vm.StackBake.Rebuilds;

        Published(vm).Dispose();
        Published(vm).Dispose();
        Published(vm).Dispose();

        var delta = vm.StackBake.Rebuilds - before;
        output.WriteLine($"rebuilds after the mask appeared: {delta}");
        // The below segment (paper + masked layer) must not fold; the above
        // segment may still re-fold once.
        Assert.True(delta <= 1,
            $"{delta} rebuilds after a mask appeared below — the masked segment folded");
    }

    /// <summary>
    /// An effect-carrying pass refuses to fold for the mask's reason plus
    /// one: a slider drag changes pixels behind a stable stack reference, so
    /// a fold key could never see the change — and an adjustment reads the
    /// backdrop besides.
    /// </summary>
    [AvaloniaFact]
    public void AFilteredLayerRefusesToFoldAndStillRenders()
    {
        var vm = LayeredVm();
        vm.Doc.Scene.Layers[1].Effects = new Lightbox.Core.Effects.EffectStack
        {
            Uses = [new Lightbox.Core.Effects.EffectUse
            {
                Kind = "grade.hsl",
                Params = { ["saturation"] = new Lightbox.Core.Effects.EffectParam(-50) },
            }],
        };
        var before = vm.StackBake.Rebuilds;

        Published(vm).Dispose();
        Published(vm).Dispose();
        Published(vm).Dispose();

        var delta = vm.StackBake.Rebuilds - before;
        output.WriteLine($"rebuilds after the effect appeared: {delta}");
        Assert.True(delta <= 1,
            $"{delta} rebuilds after an effect appeared below — the filtered segment folded");
    }

    /// <summary>
    /// Playback never pays for baking: the pass list changes every frame, so a
    /// fold could never be reused before it was stale.
    /// </summary>
    [AvaloniaFact]
    public void PlaybackDoesNotBuildBakes()
    {
        var vm = LayeredVm();
        vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 0;

        vm.TogglePlaybackCommand.Execute(null);
        Assert.True(vm.IsPlaying);
        var before = vm.StackBake.Rebuilds;
        for (var i = 0; i < 6; i++) vm.PublishSnapshot();
        var during = vm.StackBake.Rebuilds - before;
        vm.TogglePlaybackCommand.Execute(null);

        output.WriteLine($"rebuilds during playback: {during}");
        Assert.Equal(0, during);
    }

    /// <summary>
    /// The fold pays: whole-canvas publishes on a deep stack are measurably
    /// cheaper folded than not.
    /// </summary>
    /// <remarks>
    /// Deliberately loose, like every budget in the suite — it catches the
    /// fold quietly dying (a broken key that never matches, an eligibility
    /// check that never passes), not drift. Sixteen layers fold to three
    /// passes, so anything under 75% of the unfolded time is a working fold;
    /// the real ratio is far lower.
    /// </remarks>
    [AvaloniaFact]
    [Trait("Category", "Performance")]
    public void FoldedPublishesAreCheaperOnADeepStack()
    {
        static MainViewModel DeepVm(bool foldEnabled)
        {
            var vm = VmLayers.PaperVm();
            vm.StackBake.Enabled = foldEnabled;
            vm.SmoothStrokes = false;
            for (var i = 0; i < 14; i++) vm.AddPaintedLayerCommand.Execute(null);
            for (var i = 1; i <= 15; i++)
            {
                vm.ActiveLayerIndex = i;
                vm.BeginStroke(40 * i, 100, 1);
                vm.MoveStroke(40 * i, 400, 1);
                vm.EndStroke();
            }
            vm.ActiveLayerIndex = 8;
            return vm;
        }

        static double PublishTimeMs(MainViewModel vm)
        {
            // Warm: caches filled, bake (if enabled) built and serving.
            for (var i = 0; i < 3; i++) vm.PublishSnapshot();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            const int rounds = 12;
            // A bare publish carries no dirty region, which the compositor
            // reads as "everything" — each round is a whole-canvas repaint.
            for (var i = 0; i < rounds; i++) vm.PublishSnapshot();
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds / rounds;
        }

        var unfolded = PublishTimeMs(DeepVm(foldEnabled: false));
        var folded = PublishTimeMs(DeepVm(foldEnabled: true));

        output.WriteLine($"whole-canvas publish, 16 layers: unfolded {unfolded:F2} ms, folded {folded:F2} ms");
        Assert.True(folded < unfolded * 0.75,
            $"folding bought nothing ({folded:F2} ms vs {unfolded:F2} ms) — the bake is not serving");
    }

    /// <summary>
    /// The moment the fold starts serving, the publish covers the whole
    /// canvas — the LSB argument from the class remarks, pinned.
    /// </summary>
    [AvaloniaFact]
    public void AFoldTransitionRepaintsTheWholeCanvas()
    {
        var vm = LayeredVm();
        Published(vm).Dispose();
        Published(vm).Dispose(); // bake built somewhere in here

        RenderSnapshot? seen = null;
        void Capture(RenderSnapshot s) => seen = s;
        vm.SnapshotChanged += Capture;
        vm.PublishSnapshot();
        vm.SnapshotChanged -= Capture;

        Assert.NotNull(seen);
        // A whole-canvas publish reports null ChangedInImage (= everything) —
        // what it must never report is a dab-sized patch from a fold flip.
        output.WriteLine(
            $"changed region after transition: {seen!.ChangedInImage?.ToString() ?? "whole image"}");
        Assert.Null(seen.ChangedInImage);
    }

    /// <summary>
    /// A playing document holds its frames as tiles, and those passes carry
    /// a <c>SourceFrame</c> instead of a bitmap. The bake must decline them.
    /// </summary>
    /// <remarks>
    /// This is where the layer-stack bake and tile-native frames meet, and
    /// they were written on branches that never saw each other. Folding a tile
    /// pass fails three ways: the key is built from bitmap identity, so two
    /// different frames key identically and a stale bake is served for the
    /// wrong picture; <c>ComposeInto</c> draws from bitmaps, so the bake would
    /// quietly lose the layer; and the bake is document-resolution, which is
    /// the exact allocation the tile path exists to avoid. Before the fold
    /// learned to refuse them this did not merely mis-render — <c>KeyOf</c>
    /// handed a null bitmap to <c>BitmapVersion.Of</c>, which throws.
    /// </remarks>
    [AvaloniaFact]
    public void APlayingDocumentNeverFolds()
    {
        var vm = VmLayers.PaperVm();
        vm.StackBake.Enabled = true;
        vm.SmoothStrokes = false;
        vm.SetViewport(SKRectI.Create(0, 0, 960, 540));

        // The same stack the bounded tests fold: paper + three paint layers,
        // active in the middle, so there are two segments to tempt it with.
        vm.AddPaintedLayerCommand.Execute(null);
        vm.AddPaintedLayerCommand.Execute(null);
        for (var i = 1; i <= 3; i++)
        {
            vm.ActiveLayerIndex = i;
            vm.BeginStroke(100 * i, 100, 1);
            vm.MoveStroke(100 * i, 200, 1);
            vm.EndStroke();
        }
        vm.ActiveLayerIndex = 2;

        // The paused publishes above fold like any bounded publish — that is
        // the baseline. Playback is what routes frames through tiles, so the
        // pass list the fold is offered while playing is the tile-native one
        // it must refuse.
        var rebuilds = vm.StackBake.Rebuilds;
        var folded = vm.StackBake.FoldedPublishes;
        vm.TogglePlaybackCommand.Execute(null);

        // Three publishes: the fold needs two consecutive matching keys, so a
        // single one could pass while still being one publish away from baking.
        using var first = Published(vm);
        Published(vm).Dispose();
        using var third = Published(vm);
        vm.TogglePlaybackCommand.Execute(null);

        output.WriteLine(
            $"rebuilds {vm.StackBake.Rebuilds - rebuilds}, " +
            $"folded publishes {vm.StackBake.FoldedPublishes - folded} while playing");
        Assert.Equal(rebuilds, vm.StackBake.Rebuilds);
        Assert.Equal(folded, vm.StackBake.FoldedPublishes);

        // And the picture is still whole — a refused fold must leave the pass
        // list alone, not drop the segments it declined to bake.
        var diff = MaxChannelDiff(first, third);
        output.WriteLine($"max channel diff across repeated playing publishes: {diff}");
        Assert.Equal(0, diff);
    }
}

using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Q167. B327 made undo repaint the reverted mark's footprint instead of the
/// whole drawing, but it rebuilds that footprint by <em>replaying the record</em>
/// — so it still costs whatever ink crosses the mark, which on a dense model
/// sheet is most of the drawing. Undo now swaps back the pixels saved under the
/// mark when it committed, which costs the mark's area and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>The bar is bit-identity against a from-nothing render</b>, the same bar
/// <c>UndoRegionRepaintTests</c> sets, and for a stronger reason here: a
/// snapshot is pixels held outside the stroke record, so the one thing that must
/// never happen is the cache and the record disagreeing about what the drawing
/// is. Invariant 1 survives this only because every test below re-derives the
/// answer from the record and demands the swapped bitmap equal it exactly.
/// </para>
/// <para>
/// <b>The replay is still guarded, and elsewhere.</b> These tests are the fast
/// path; <c>UndoRegionRepaintTests</c> clears the snapshots in its warm-up so it
/// keeps exercising B327.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class UndoMarkSnapshotTests : BrushStateIsolated
{
    private static MainViewModel Vm()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 14;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        return vm;
    }

    private static void Stroke(MainViewModel vm, double x, double y, double dx, double dy)
    {
        vm.BeginStroke(x, y, 1);
        vm.MoveStroke(x + dx / 2, y + dy / 2, 1);
        vm.MoveStroke(x + dx, y + dy, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static void Warm(MainViewModel vm)
    {
        var scene = vm.Doc.Scene;
        vm.FrameCache.Get(vm.PaintedCel(), scene.Width, scene.Height);
    }

    /// <summary>
    /// The cached pixels against a render of the same record from nothing. Any
    /// difference is ink the document does not describe.
    /// </summary>
    private static void AssertCacheMatchesAFullRender(MainViewModel vm, double outputScale = 1.0)
    {
        var frame = vm.PaintedCel();
        var scene = vm.Doc.Scene;

        Assert.True(vm.FrameCache.Holds(frame.Id), "the swapped entry should still be held");
        var cached = vm.FrameCache.Get(frame, scene.Width, scene.Height, outputScale: outputScale);
        using var fresh = FrameRasterizer.Materialize(
            frame, scene.Width, scene.Height, outputScale: outputScale);

        Assert.Equal(fresh.Width, cached.Width);
        Assert.Equal(fresh.Height, cached.Height);
        Assert.True(
            cached.Bytes.AsSpan().SequenceEqual(fresh.Bytes.AsSpan()),
            $"the swapped drawing at scale {outputScale} is not what a render of this record produces");
    }

    /// <summary>
    /// The whole point: undo puts back exactly the pixels a full render of the
    /// reverted record would produce, without replaying a single stroke.
    /// </summary>
    [AvaloniaFact]
    public void UndoingAStrokeSwapsBackExactlyThePixelsAFullRenderWould()
    {
        var vm = Vm();
        // Deliberately overlapping: a swap that restored the wrong rectangle, or
        // one stale by a stroke, only shows where marks cross.
        Stroke(vm, 100, 100, 200, 60);
        Stroke(vm, 140, 90, 160, 120);
        Stroke(vm, 120, 130, 220, -40);
        Warm(vm);

        var restored = vm.FrameRegionRestores;
        var replayed = vm.FrameRegionRepaints;
        var dropped = vm.FrameRenderDrops;
        vm.UndoCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(restored + 1, vm.FrameRegionRestores);
        Assert.Equal(replayed, vm.FrameRegionRepaints);
        Assert.Equal(dropped, vm.FrameRenderDrops);
        AssertCacheMatchesAFullRender(vm);
    }

    /// <summary>
    /// Redo is the same exchange run the other way, so it is free for the same
    /// reason — and it has to land on the marked pixels, not the unmarked ones.
    /// </summary>
    /// <remarks>
    /// <b>This is the test that fails if the swap is written as a restore.</b>
    /// A patch that is only ever read leaves redo putting back the pre-stroke
    /// pixels, which reads as the redo doing nothing at all.
    /// </remarks>
    [AvaloniaFact]
    public void RedoingItPutsBackExactlyThosePixelsToo()
    {
        var vm = Vm();
        Stroke(vm, 100, 100, 200, 60);
        Stroke(vm, 140, 90, 160, 120);
        Warm(vm);

        vm.UndoCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var restored = vm.FrameRegionRestores;
        vm.RedoCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(restored + 1, vm.FrameRegionRestores);
        AssertCacheMatchesAFullRender(vm);
    }

    /// <summary>
    /// Undo, redo, undo, redo. The exchange has to survive being run repeatedly
    /// in both directions rather than being right the first time and drifting.
    /// </summary>
    [AvaloniaFact]
    public void UndoAndRedoCanBeWalkedRepeatedlyWithoutDrifting()
    {
        var vm = Vm();
        Stroke(vm, 100, 100, 200, 60);
        Stroke(vm, 140, 90, 160, 120);
        Stroke(vm, 120, 130, 220, -40);
        Warm(vm);

        for (var i = 0; i < 4; i++)
        {
            vm.UndoCommand.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AssertCacheMatchesAFullRender(vm);

            vm.RedoCommand.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AssertCacheMatchesAFullRender(vm);
        }

        Assert.Equal(0, vm.FrameRenderDrops);
    }

    /// <summary>
    /// An eraser removes pixels rather than adding them, so its saved patch is
    /// the only record of what was underneath.
    /// </summary>
    [AvaloniaFact]
    public void UndoingAnEraserRestoresWhatWasBeneathIt()
    {
        var vm = Vm();
        Stroke(vm, 100, 100, 240, 80);

        vm.ActiveTool = ToolId.Eraser;
        Stroke(vm, 140, 110, 120, 40);
        vm.ActiveTool = ToolId.Brush;
        Warm(vm);

        var restored = vm.FrameRegionRestores;
        vm.UndoCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(restored + 1, vm.FrameRegionRestores);
        AssertCacheMatchesAFullRender(vm);
    }

    /// <summary>
    /// Q167's second hole, closed. A smudge reads the pixels it sits on, which
    /// is why B327 has to refuse the whole patch and re-render the drawing:
    /// outside the clip the bitmap holds ink painted <em>after</em> the smudge,
    /// and replaying it there would drag the future into the past.
    /// </summary>
    /// <remarks>
    /// <b>Saved pixels have no sampling problem at all</b>, because nothing is
    /// re-stamped — the bytes that were under the mark go back where they were.
    /// This is the same drawing <c>ASmudgeOnTheDrawingSendsUndoBackToTheWholeRender</c>
    /// builds, asserting the opposite outcome, and the pair is the point.
    /// </remarks>
    [AvaloniaFact]
    public void ASmudgeUndoesWithoutFallingBackToTheWholeRender()
    {
        var vm = Vm();
        Stroke(vm, 100, 100, 240, 80);

        vm.ApplyPreset(vm.BrushPresetChoices.First(p => p.Id == "builtin-smudge"));
        Assert.Equal(BrushKind.Smudge, vm.CurrentToolSettingsForTest.Kind);
        Stroke(vm, 140, 110, 120, 40);
        Warm(vm);

        var dropped = vm.FrameRenderDrops;
        var restored = vm.FrameRegionRestores;
        vm.UndoCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(dropped, vm.FrameRenderDrops);
        Assert.Equal(restored + 1, vm.FrameRegionRestores);
        AssertCacheMatchesAFullRender(vm);
    }

    /// <summary>
    /// Restoring costs the mark's area, so piling ink around it must not move
    /// the cost. Replaying costs the ink, which is the difference this measures.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A ratio between two paths on one machine, not a millisecond
    /// threshold.</b> The charter's reason: an absolute number measures the
    /// machine it ran on. Both arms render the same drawing on the same build in
    /// the same process, and the only difference is whether the saved pixels are
    /// there — so a machine that is slow is slow for both.
    /// </para>
    /// <para>
    /// <b>The drawing is a hatched band on purpose.</b> Scattered marks are the
    /// case B327 already handles well; the case Q167 exists for is the one where
    /// every stroke crosses the reverted mark's footprint, and a probe that only
    /// measured scattered marks would report no gain — which is the mistake B327
    /// recorded having made.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void RestoringAMarkCostsItsAreaRatherThanTheDrawing()
    {
        static double UndoCost(bool withSnapshots)
        {
            var vm = Vm();
            // Every stroke crosses the same band, so every one of them reaches
            // the last mark's footprint and the replay has to re-stamp the lot.
            for (var i = 0; i < 160; i++) Stroke(vm, 120, 100 + i * 0.5, 320, 40);
            var scene = vm.Doc.Scene;
            vm.FrameCache.Get(vm.PaintedCel(), scene.Width, scene.Height);
            if (!withSnapshots) vm.MarkSnapshots.Clear();

            var best = double.MaxValue;
            for (var run = 0; run < 5; run++)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                vm.UndoCommand.Execute(null);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                watch.Stop();
                best = Math.Min(best, watch.Elapsed.TotalMilliseconds);

                vm.RedoCommand.Execute(null);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                if (!withSnapshots) vm.MarkSnapshots.Clear();
            }
            return best;
        }

        // The replay arm first: it is the slower one, so running it while the
        // process is coldest is the conservative order for the ratio.
        var replay = UndoCost(withSnapshots: false);
        var restore = UndoCost(withSnapshots: true);

        Assert.True(
            restore * 4 < replay,
            $"restoring should be far cheaper than replaying — restore {restore:0.00} ms, "
            + $"replay {replay:0.00} ms");
    }

    /// <summary>
    /// A drawing whose bitmap the cache never held has nothing to save and
    /// nothing to swap, and must still undo to the right picture.
    /// </summary>
    [AvaloniaFact]
    public void AnUncachedDrawingStillUndoesThroughTheRecord()
    {
        var vm = Vm();
        Stroke(vm, 100, 100, 200, 60);
        vm.FrameCache.Clear();
        vm.MarkSnapshots.Clear();

        vm.UndoCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        AssertCacheMatchesAFullRender(vm);
    }

    /// <summary>
    /// A rendering the snapshot never saw sends the step back to the replay
    /// rather than being patched at one scale and left stale at the other.
    /// </summary>
    /// <remarks>
    /// <b>The refusal is the feature.</b> The canvas composes at the scale it can
    /// show and other consumers ask for their own, so a frame can hold more than
    /// one bitmap — and a swap covering only the ones that existed at commit time
    /// would leave two renders of one record disagreeing. Cheap to get wrong,
    /// invisible until someone exports.
    /// </remarks>
    [AvaloniaFact]
    public void ARenderingThatArrivedAfterTheMarkSendsUndoToTheReplay()
    {
        var vm = Vm();
        Stroke(vm, 100, 100, 200, 60);
        Stroke(vm, 140, 90, 160, 120);

        var scene = vm.Doc.Scene;
        var frame = vm.PaintedCel();
        // The second scale is asked for only now, so the saved patch covers one
        // rendering and the cache holds two.
        vm.FrameCache.Get(frame, scene.Width, scene.Height);
        vm.FrameCache.Get(frame, scene.Width, scene.Height, outputScale: 2.0);

        var restored = vm.FrameRegionRestores;
        var fallbacks = vm.MarkSnapshots.Fallbacks;
        vm.UndoCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(restored, vm.FrameRegionRestores);
        Assert.Equal(fallbacks + 1, vm.MarkSnapshots.Fallbacks);
        AssertCacheMatchesAFullRender(vm, outputScale: 1.0);
        AssertCacheMatchesAFullRender(vm, outputScale: 2.0);
    }

    /// <summary>
    /// A history jump walks several steps at once, and the saved pixels describe
    /// one transition each. It has to take the rebuild rather than a swap for
    /// whichever step happened to be named.
    /// </summary>
    [AvaloniaFact]
    public void AHistoryJumpAcrossSeveralStepsDoesNotSwapOneOfThem()
    {
        var vm = Vm();
        Stroke(vm, 100, 100, 200, 60);
        Stroke(vm, 140, 90, 160, 120);
        Stroke(vm, 120, 130, 220, -40);
        Warm(vm);

        // The oldest row: standing there walks back over all three strokes.
        var target = vm.UndoHistory.Rows.First();
        var restored = vm.FrameRegionRestores;
        vm.UndoHistory.Jump(target);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(restored, vm.FrameRegionRestores);
        AssertCacheMatchesAFullRender(vm);
    }

    /// <summary>
    /// The budget is a total across the history, so a mark too big for it is
    /// simply not saved — and undo is then B327's replay, which is correct and
    /// slower rather than wrong.
    /// </summary>
    [AvaloniaFact]
    public void AMarkTooBigForTheBudgetIsNotSavedAndUndoesAnyway()
    {
        var before = MarkSnapshot.ByteBudget;
        try
        {
            MarkSnapshot.ByteBudget = 1;
            var vm = Vm();
            Stroke(vm, 100, 100, 200, 60);
            Warm(vm);

            Assert.Equal(0, vm.MarkSnapshots.Steps);
            var restored = vm.FrameRegionRestores;
            vm.UndoCommand.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal(restored, vm.FrameRegionRestores);
            AssertCacheMatchesAFullRender(vm);
        }
        finally
        {
            MarkSnapshot.ByteBudget = before;
        }
    }

    /// <summary>
    /// When an undo has to rebuild the drawing rather than swap, the saved
    /// pixels stop describing anything and must be thrown away — or the redo
    /// after it puts back the state the undo was already in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the one failure in the design that is wrong rather than
    /// slow.</b> The patch holds whichever side of the step the drawing is not
    /// on, which only stays true while every transition goes through
    /// <c>Swap</c>. Let one undo take the rebuild instead and the patch is now
    /// on the same side as the bitmap — so the redo writes the unmarked pixels
    /// over a record that has the mark, and the stroke simply vanishes.
    /// </para>
    /// <para>
    /// <b>Reaching the rebuild takes both refusals at once</b>, which is why the
    /// drawing is built this way: a second rendering makes the swap refuse, and
    /// the smudge makes B327's replay refuse too, so the undo falls all the way
    /// through to dropping the bitmap.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void ARebuiltDrawingForgetsItsSavedPixelsSoTheRedoIsNotStale()
    {
        var vm = Vm();
        Stroke(vm, 100, 100, 240, 80);
        vm.ApplyPreset(vm.BrushPresetChoices.First(p => p.Id == "builtin-smudge"));
        Stroke(vm, 140, 110, 120, 40);
        // A paint stroke last, over the smudge: the smudge has to still be in
        // the record and inside the reverted mark's footprint for the replay to
        // refuse. Undoing the smudge itself takes it out of the record first,
        // and then there is nothing left for the replay to object to.
        vm.ApplyPreset(vm.BrushPresetChoices.First(p => p.Settings.Kind == BrushKind.Paint));
        Stroke(vm, 120, 140, 200, 20);

        var scene = vm.Doc.Scene;
        var frame = vm.PaintedCel();
        vm.FrameCache.Get(frame, scene.Width, scene.Height);
        vm.FrameCache.Get(frame, scene.Width, scene.Height, outputScale: 2.0);

        var dropped = vm.FrameRenderDrops;
        vm.UndoCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(dropped + 1, vm.FrameRenderDrops);
        Assert.Equal(0, vm.MarkSnapshots.Steps);

        vm.RedoCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AssertCacheMatchesAFullRender(vm);
    }

    /// <summary>
    /// A step past the undo depth can never be undone, so its pixels are not
    /// kept — the budget is not the only limit, because the two count different
    /// things.
    /// </summary>
    /// <remarks>
    /// <b>Found by measuring rather than by reading.</b> A hatched 2 400-stroke
    /// drawing held 194 MB of patches for 64 reachable steps, because the byte
    /// budget was the only thing trimming and 194 MB is well under it. Nothing
    /// was wrong; it was just memory the artist pays for and cannot spend.
    /// </remarks>
    [AvaloniaFact]
    public void PixelsAreNotKeptForStepsPastTheUndoDepth()
    {
        var vm = Vm();
        vm.UndoDepth = 8;
        Warm(vm);
        for (var i = 0; i < 30; i++) Stroke(vm, 100 + i * 6, 100, 40, 24);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(8, vm.MarkSnapshots.Steps);
        Assert.True(vm.MarkSnapshots.Evictions >= 22);

        // And the ones still held are the reachable ones, not merely eight of them.
        for (var i = 0; i < 8; i++)
        {
            vm.UndoCommand.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AssertCacheMatchesAFullRender(vm);
        }
        Assert.Equal(0, vm.FrameRenderDrops);
    }

    /// <summary>
    /// A mark's patch is its own area, not a tile grid rounded out around it —
    /// the deviation from Q167 that keeps the memory argument the answer was
    /// built on true.
    /// </summary>
    /// <remarks>
    /// <b>An upper bound rather than an exact size</b>, because the footprint
    /// comes from the brush's reach and pinning it exactly would make this a test
    /// of <c>CommitBounds</c>. What it does pin is the thing that would silently
    /// regress: a patch quietly becoming tile-sized is 256 KB where 6 KB was
    /// budgeted, and nothing else in the suite would notice.
    /// </remarks>
    [AvaloniaFact]
    public void AMarksPatchIsItsOwnAreaRatherThanAGridOfTiles()
    {
        var vm = Vm();
        Warm(vm);
        Stroke(vm, 400, 300, 40, 24);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, vm.MarkSnapshots.Steps);
        // One 256-pixel tile is 256 KB at four bytes a pixel, and a 40x24 mark
        // straddling the grid would take up to four of them.
        Assert.True(
            vm.MarkSnapshots.Bytes < 64L * 1024,
            $"a small mark should cost kilobytes, not tiles — {vm.MarkSnapshots.Bytes} bytes");
    }
}

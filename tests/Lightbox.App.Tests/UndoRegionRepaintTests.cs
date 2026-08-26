using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B327. Committing a stroke is bounded work — <c>AppendToFrameRender</c> stamps
/// the one new mark onto the cached bitmap. Undoing it used to drop that bitmap
/// and re-stamp every stroke on the drawing, so drawing was O(1) and taking it
/// back was O(n): 3 092 ms per Ctrl+Z at 1 600 strokes, dead linear. Undo now
/// repaints the reverted mark's footprint instead.
/// </summary>
/// <remarks>
/// <b>The bar is bit-identity, not similarity.</b> A patched bitmap that merely
/// looks right is the failure this whole file exists to catch — it would show ink
/// the document no longer describes, and only where two strokes happen to
/// overlap. Invariant 2 is what makes the bar reachable: <c>Hash01</c> seeds every
/// dab from the IEEE-754 bits of its position, so a stroke replayed under a clip
/// lands exactly the dabs it landed the first time.
/// </remarks>
[Collection("BrushState")]
public class UndoRegionRepaintTests : BrushStateIsolated
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

    /// <summary>
    /// The cached pixels after an undo, against a render of the same record from
    /// nothing. Any difference is ink the document does not describe.
    /// </summary>
    /// <param name="droppedBefore">
    /// <see cref="MainViewModel.FrameRenderDrops"/> as it stood before the undo.
    /// <b>Checking it is what stops this assertion passing vacuously.</b> If the
    /// undo had dropped the cache entry instead of patching it, the
    /// <c>Get</c> below would render the frame from nothing and compare that
    /// against a render from nothing — two identical things, and a green test
    /// that proved the patch was never exercised.
    /// </param>
    private static void AssertCacheMatchesAFullRender(MainViewModel vm, int droppedBefore)
    {
        var frame = vm.PaintedCel();
        var scene = vm.Doc.Scene;

        Assert.Equal(droppedBefore, vm.FrameRenderDrops);
        Assert.True(vm.FrameCache.Holds(frame.Id), "the patched entry should still be held");

        var cached = vm.FrameCache.Get(frame, scene.Width, scene.Height);
        using var fresh = FrameRasterizer.Materialize(frame, scene.Width, scene.Height);

        Assert.Equal(fresh.Width, cached.Width);
        Assert.Equal(fresh.Height, cached.Height);
        Assert.True(
            cached.Bytes.AsSpan().SequenceEqual(fresh.Bytes.AsSpan()),
            "the patched drawing is not what a render of this record produces");
    }

    /// <summary>Warm the cache the way a publish would, so there is an entry to patch.</summary>
    private static void Warm(MainViewModel vm)
    {
        var scene = vm.Doc.Scene;
        vm.FrameCache.Get(vm.PaintedCel(), scene.Width, scene.Height);
    }

    [AvaloniaFact]
    public void UndoingAStrokeLeavesExactlyThePixelsAFullRenderWould()
    {
        var vm = Vm();
        // Deliberately overlapping: a patch that replayed the wrong strokes, or
        // replayed them out of order, only shows where marks cross.
        Stroke(vm, 100, 100, 200, 60);
        Stroke(vm, 140, 90, 160, 120);
        Stroke(vm, 120, 130, 220, -40);
        Warm(vm);

        var dropped = vm.FrameRenderDrops;
        vm.UndoCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, vm.PaintStrokes().Count);
        AssertCacheMatchesAFullRender(vm, dropped);
    }

    [AvaloniaFact]
    public void RedoingItPutsBackExactlyThosePixelsToo()
    {
        var vm = Vm();
        Stroke(vm, 100, 100, 200, 60);
        Stroke(vm, 140, 90, 160, 120);
        Warm(vm);

        var dropped = vm.FrameRenderDrops;
        vm.UndoCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.RedoCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, vm.PaintStrokes().Count);
        AssertCacheMatchesAFullRender(vm, dropped);
    }

    /// <summary>
    /// An eraser takes paint away, so its undo has to put back what was under
    /// it — the case a clear-and-replay gets wrong if it replays the wrong set.
    /// </summary>
    [AvaloniaFact]
    public void UndoingAnEraserRestoresWhatWasBeneathIt()
    {
        var vm = Vm();
        Stroke(vm, 100, 100, 240, 80);
        Stroke(vm, 110, 150, 240, -60);

        vm.ActiveTool = ToolId.Eraser;
        Stroke(vm, 150, 90, 120, 80);
        vm.ActiveTool = ToolId.Brush;
        Warm(vm);

        var dropped = vm.FrameRenderDrops;
        vm.UndoCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        AssertCacheMatchesAFullRender(vm, dropped);
    }

    /// <summary>
    /// Several undos in a row, each patching the bitmap the last one patched —
    /// where an error in the region maths accumulates instead of showing once.
    /// </summary>
    [AvaloniaFact]
    public void RepeatedUndosDoNotDriftFromTheRecord()
    {
        var vm = Vm();
        for (var i = 0; i < 8; i++) Stroke(vm, 80 + i * 30, 100 + (i % 3) * 40, 90, 50);
        Warm(vm);

        for (var i = 0; i < 5; i++)
        {
            var dropped = vm.FrameRenderDrops;
            vm.UndoCommand.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AssertCacheMatchesAFullRender(vm, dropped);
        }

        Assert.Equal(3, vm.PaintStrokes().Count);
    }

    /// <summary>
    /// The point of the change, counted rather than timed: undo of a stroke
    /// patches the drawing instead of dropping it.
    /// </summary>
    /// <remarks>
    /// A count, because what this is about is <em>which path ran</em>. A
    /// millisecond assertion would measure the machine it ran on, which is the
    /// trap the charter's budgets already warn about.
    /// </remarks>
    [AvaloniaFact]
    public void UndoPatchesTheDrawingRatherThanDroppingIt()
    {
        var vm = Vm();
        for (var i = 0; i < 6; i++) Stroke(vm, 60 + i * 120, 120, 60, 40);
        Warm(vm);

        var patched = vm.FrameRegionRepaints;
        var dropped = vm.FrameRenderDrops;
        vm.UndoCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(patched + 1, vm.FrameRegionRepaints);
        Assert.Equal(dropped, vm.FrameRenderDrops);
    }

    /// <summary>
    /// A smudge reads the pixels it sits on. Outside the patched rectangle the
    /// bitmap holds the drawing as it stands now — every stroke, including ones
    /// painted after the smudge — where a full render would have shown it only
    /// what came before. So a drawing carrying one goes back to the slow path.
    /// </summary>
    [AvaloniaFact]
    public void ASmudgeOnTheDrawingSendsUndoBackToTheWholeRender()
    {
        var vm = Vm();
        Stroke(vm, 100, 100, 240, 80);

        vm.ApplyPreset(vm.BrushPresetChoices.First(p => p.Id == "builtin-smudge"));
        Assert.Equal(BrushKind.Smudge, vm.CurrentToolSettingsForTest.Kind);
        Stroke(vm, 140, 110, 120, 40);
        vm.ApplyPreset(vm.BrushPresetChoices.First(p => p.Settings.Kind == BrushKind.Paint));
        Stroke(vm, 120, 140, 200, 20);
        Warm(vm);

        var dropped = vm.FrameRenderDrops;
        vm.UndoCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(dropped + 1, vm.FrameRenderDrops);
        // Not through the helper: it asserts the entry survived, and the point
        // here is that it did not. The pixels are still checked, because falling
        // back has to be a slower way to the same drawing.
        var scene = vm.Doc.Scene;
        var rendered = vm.FrameCache.Get(vm.PaintedCel(), scene.Width, scene.Height);
        using var fresh = FrameRasterizer.Materialize(vm.PaintedCel(), scene.Width, scene.Height);
        Assert.True(rendered.Bytes.AsSpan().SequenceEqual(fresh.Bytes.AsSpan()));
    }

    /// <summary>
    /// The canvas composes at the scale it can actually show, so the cache holds
    /// renders at more than one. A patch has to land in the right place in each.
    /// </summary>
    /// <remarks>
    /// <b>The rectangle arrives in document coordinates and every entry is a
    /// different number of pixels per unit</b>, so this is where an off-by-a-scale
    /// shows up: at 1.0 it would look perfect and at 2.0 the repair would land on
    /// the wrong quarter of the drawing. Invariant 7 is why replaying at scale is
    /// sound at all — the surface is scaled and the geometry is never touched.
    /// </remarks>
    [AvaloniaFact]
    public void EveryHeldScaleIsPatched()
    {
        var vm = Vm();
        Stroke(vm, 100, 100, 200, 60);
        Stroke(vm, 140, 90, 160, 120);
        Stroke(vm, 120, 130, 220, -40);

        var scene = vm.Doc.Scene;
        var frame = vm.PaintedCel();
        // Two renders of the same drawing, the way a canvas at a fractional zoom
        // and a full-resolution consumer would each ask for one.
        vm.FrameCache.Get(frame, scene.Width, scene.Height);
        vm.FrameCache.Get(frame, scene.Width, scene.Height, outputScale: 2.0);

        var dropped = vm.FrameRenderDrops;
        vm.UndoCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(dropped, vm.FrameRenderDrops);

        var after = vm.PaintedCel();
        foreach (var scale in new[] { 1.0, 2.0 })
        {
            var cached = vm.FrameCache.Get(after, scene.Width, scene.Height, outputScale: scale);
            using var fresh = FrameRasterizer.Materialize(
                after, scene.Width, scene.Height, outputScale: scale);
            Assert.Equal(fresh.Width, cached.Width);
            Assert.True(
                cached.Bytes.AsSpan().SequenceEqual(fresh.Bytes.AsSpan()),
                $"the patch at scale {scale} is not what a render of this record produces");
        }
    }
}

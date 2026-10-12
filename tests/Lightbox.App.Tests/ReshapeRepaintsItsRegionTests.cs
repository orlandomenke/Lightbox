using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B448: reshaping a line repaints the rectangle it could reach, rather than
/// throwing the drawing's pixels away and rebuilding all of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured in the lab before any of this was written</b> (pen-path, the
/// owner-shaped document, 2026-10-12): letting go of a point dragged with the
/// white arrow took a median 237 ms to reach the screen, 183 ms of it one
/// <c>raster.miss</c> — the whole drawing re-rendered from its strokes because
/// <c>CommitPathEdit</c> named a frame and no rectangle. Reshaping is a dozen
/// small drags in a row, so that is a dozen quarter-second freezes.
/// </para>
/// <para>
/// <b>Counters for which path ran, pixels for whether it was right.</b> B327's
/// form: a millisecond assertion measures the machine, and what the fix is
/// about is whether the drawing was dropped. The pixel tests are the other
/// half — a patch that is fast and leaves the old line behind is worse than
/// the freeze, so every one of them compares against a render built from the
/// record with no cache in the way.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class ReshapeRepaintsItsRegionTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>
    /// A pen line with two drawn lines crossing it, so the rectangle that gets
    /// repainted has other people's ink in it to put back.
    /// </summary>
    /// <param name="busyElsewhere">
    /// Whether the drawing has most of its ink somewhere the reshape cannot
    /// reach. That is what makes a patch worth taking: it is priced by the
    /// strokes it replays, so a line that touches most of the drawing is
    /// cheaper to rebuild than to patch.
    /// </param>
    private static (MainViewModel Vm, string LineId) Crossed(bool busyElsewhere = true)
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 10;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        vm.BrushScatter = 0;
        vm.BrushSizeJitter = 0;
        vm.BrushFlowJitter = 0;
        vm.BrushRotationJitter = 0;
        vm.BrushRoundnessJitter = 0;
        vm.BrushColorJitter = 0;
        vm.BrushGranulation = 0;
        vm.BrushWetEdge = 0;

        vm.ActiveTool = ToolId.Brush;
        Line(vm, 150, 60, 150, 420);
        if (busyElsewhere)
        {
            for (var row = 0; row < 4; row++) Scribble(vm, 600, 440 + row * 20, 900);
        }

        vm.ActiveTool = ToolId.Pen;
        vm.ColorHex = "#2244cc";
        foreach (var (x, y) in new[] { (100.0, 200.0), (250.0, 200.0), (400.0, 200.0) })
        {
            vm.PenPress(x, y, tolerance: 6);
            vm.PenRelease();
        }
        vm.FinishPen();
        var lineId = vm.PaintedCel().Strokes[^1].Id;

        // Drawn after the pen line, so the repaint has ink above it to restore
        // as well as ink below.
        vm.ActiveTool = ToolId.Brush;
        vm.ColorHex = "#cc2222";
        Line(vm, 330, 60, 330, 420);

        vm.ActiveTool = ToolId.DirectSelect;
        return (vm, lineId);
    }

    private static void Line(MainViewModel vm, double x0, double y0, double x1, double y1)
    {
        vm.BeginStroke(x0, y0, 1);
        vm.MoveStroke((x0 + x1) / 2, (y0 + y1) / 2, 1);
        vm.MoveStroke(x1, y1, 1);
        vm.EndStroke();
    }

    /// <summary>A long line of many points, well away from the pen line.</summary>
    private static void Scribble(MainViewModel vm, double x0, double y, double x1)
    {
        vm.BeginStroke(x0, y, 1);
        for (var i = 1; i <= 60; i++) vm.MoveStroke(x0 + (x1 - x0) * i / 60, y + (i % 2) * 3, 1);
        vm.EndStroke();
    }

    /// <summary>Pull the middle point of the pen line down, and commit.</summary>
    private static void Reshape(MainViewModel vm, string lineId)
    {
        Assert.True(vm.BeginPathEdit(lineId));
        var grabbed = vm.GrabPathPart(250, 200, tolerance: 6);
        Assert.True(grabbed.IsHit);
        vm.DragPathPart(grabbed, 250, 300, 0, 100);
        Assert.True(vm.CommitPathEdit());
    }

    private static SKBitmap Shown(MainViewModel vm)
    {
        RenderSnapshot? latest = null;
        void Capture(RenderSnapshot s) => latest = s;
        vm.SnapshotChanged += Capture;
        vm.PublishSnapshot();
        vm.SnapshotChanged -= Capture;
        return SKBitmap.FromImage(latest!.Image);
    }

    /// <summary>The drawing as the record alone says it is — no cache involved.</summary>
    private static SKBitmap Rebuilt(MainViewModel vm) =>
        FrameRasterizer.Materialize(vm.PaintedCel(), vm.Doc.Scene.Width, vm.Doc.Scene.Height);

    /// <summary>The cached drawing, which is what the patch wrote into.</summary>
    private static SKBitmap Cached(MainViewModel vm) =>
        vm.FrameCache.Get(vm.PaintedCel(), vm.Doc.Scene.Width, vm.Doc.Scene.Height).Copy();

    private static (long Differing, int Worst) Compare(SKBitmap a, SKBitmap b)
    {
        long differing = 0;
        var worst = 0;
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                var p = a.GetPixel(x, y);
                var q = b.GetPixel(x, y);
                var d = Math.Max(
                    Math.Abs(p.Alpha - q.Alpha),
                    Math.Max(
                        Math.Abs(p.Red - q.Red),
                        Math.Max(Math.Abs(p.Green - q.Green), Math.Abs(p.Blue - q.Blue))));
                if (d > 0) differing++;
                worst = Math.Max(worst, d);
            }
        }
        return (differing, worst);
    }

    [AvaloniaFact]
    public void ReshapingALinePatchesInsteadOfDroppingTheDrawing()
    {
        var (vm, lineId) = Crossed();
        using var _ = Shown(vm);
        var repaintsBefore = vm.FrameRegionRepaints;
        var dropsBefore = vm.FrameRenderDrops;

        Reshape(vm, lineId);

        output.WriteLine(
            $"repaints {repaintsBefore} → {vm.FrameRegionRepaints}, "
            + $"drops {dropsBefore} → {vm.FrameRenderDrops}");
        Assert.Equal(repaintsBefore + 1, vm.FrameRegionRepaints);
        Assert.Equal(dropsBefore, vm.FrameRenderDrops);
    }

    /// <summary>
    /// The patched drawing is the drawing: the line where it now is, nothing
    /// left where it was, and the two lines crossing it untouched.
    /// </summary>
    [AvaloniaFact]
    public void AReshapedLineLooksLikeTheRecordSaysItShould()
    {
        var (vm, lineId) = Crossed();
        using var before = Shown(vm);
        using var cachedBefore = Cached(vm);

        Reshape(vm, lineId);

        using var cached = Cached(vm);
        using var rebuilt = Rebuilt(vm);
        var (differing, worst) = Compare(cached, rebuilt);
        var (moved, _) = Compare(cached, cachedBefore);
        output.WriteLine($"cache vs record: differing {differing}, worst {worst}; pixels the reshape moved: {moved}");
        // The second number is what stops the first passing on a reshape that
        // changed nothing at all.
        Assert.True(moved > 500, $"the reshape moved only {moved} pixels");
        Assert.Equal(0, differing);
    }

    /// <summary>
    /// A line that reaches most of the drawing is rebuilt, not patched — and
    /// is still right.
    /// </summary>
    /// <remarks>
    /// The patch stamps every stroke its rectangle touches in full, so here it
    /// would replay the whole drawing through a clip and cost more than the
    /// render it was meant to save. Measured, not assumed: see
    /// <c>ReplayingBeatsRebuilding</c>.
    /// </remarks>
    [AvaloniaFact]
    public void AReshapeThatReachesMostOfTheDrawingRebuildsItInstead()
    {
        var (vm, lineId) = Crossed(busyElsewhere: false);
        using var _ = Shown(vm);
        var repaintsBefore = vm.FrameRegionRepaints;
        var dropsBefore = vm.FrameRenderDrops;

        Reshape(vm, lineId);

        using var cached = Cached(vm);
        using var rebuilt = Rebuilt(vm);
        var (differing, worst) = Compare(cached, rebuilt);
        output.WriteLine(
            $"repaints {repaintsBefore} → {vm.FrameRegionRepaints}, "
            + $"drops {dropsBefore} → {vm.FrameRenderDrops}; differing {differing}, worst {worst}");
        Assert.Equal(repaintsBefore, vm.FrameRegionRepaints);
        Assert.Equal(dropsBefore + 1, vm.FrameRenderDrops);
        Assert.Equal(0, differing);
    }

    [AvaloniaFact]
    public void UndoingAReshapePutsEveryPixelBack()
    {
        var (vm, lineId) = Crossed();
        using var _ = Shown(vm);
        using var before = Cached(vm);

        Reshape(vm, lineId);
        using var __ = Shown(vm);
        var dropsBefore = vm.FrameRenderDrops;
        vm.UndoCommand.Execute(null);

        using var after = Cached(vm);
        var (differing, worst) = Compare(before, after);
        output.WriteLine(
            $"after undo: differing {differing}, worst {worst}; "
            + $"drops {dropsBefore} → {vm.FrameRenderDrops}");
        Assert.Equal(0, differing);
        Assert.Equal(dropsBefore, vm.FrameRenderDrops);
    }
}

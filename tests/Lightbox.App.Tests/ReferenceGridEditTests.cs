using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.ViewModels;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Editing the reference grid by hand: moving, resizing and deleting the
/// boxes, drawing new ones, placing pivots, and turning what was found into
/// keyframes.
/// </summary>
/// <remarks>
/// Detection gets a page most of the way there and cannot get it all the way:
/// a sheet whose figures overlap, or whose rows have no gutter, has no
/// boundary to find. These are the operations that finish the job.
/// </remarks>
[Collection("BrushState")]
public class ReferenceGridEditTests : BrushStateIsolated
{
    /// <summary>Four 10-wide cells in a 40×10 sheet, one block of colour each.</summary>
    private static string Sheet(int cells = 4)
    {
        using var bmp = new SKBitmap(cells * 10, 10, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            for (var i = 0; i < cells; i++)
            {
                using var paint = new SKPaint { Color = new SKColor(220, 20, 20) };
                canvas.DrawRect(SKRect.Create(i * 10 + 2, 2, 6, 6), paint);
            }
        }
        return PngCodec.Encode(bmp);
    }

    private static (MainViewModel Vm, Lightbox.Core.Documents.ReferenceStrip Strip) Imported(int cells = 4)
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        var strip = vm.ImportReference("Run", Sheet(cells))!;
        return (vm, strip);
    }

    // ---- the mode ---------------------------------------------------------------

    [AvaloniaFact]
    public void TheGridModeTakesTheCanvasAwayFromTheTools()
    {
        // Half a mark made while adjusting a grid is a mark you then have to
        // find and undo.
        var (vm, _) = Imported();
        var before = ((Lightbox.Core.Documents.PaintedFrame)vm.PaintLayer().Cels[0].Frame!).Strokes.Count;

        vm.ReferenceGridEditMode = true;
        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(60, 60, 1);
        vm.EndStroke();

        Assert.True(vm.SuppressesPainting);
        Assert.Equal(before, ((Lightbox.Core.Documents.PaintedFrame)vm.PaintLayer().Cels[0].Frame!).Strokes.Count);
    }

    [AvaloniaFact]
    public void LeavingTheModeGivesTheCanvasBackAndDropsTheSelection()
    {
        var (vm, _) = Imported();
        vm.ReferenceGridEditMode = true;
        vm.SelectedReferenceCell = 1;

        vm.ReferenceGridEditMode = false;

        Assert.False(vm.SuppressesPainting);
        Assert.Equal(-1, vm.SelectedReferenceCell);

        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(60, 60, 1);
        vm.EndStroke();
        Assert.NotEmpty(((Lightbox.Core.Documents.PaintedFrame)vm.PaintLayer().Cels[0].Frame!).Strokes);
    }

    // ---- where the boxes are ----------------------------------------------------

    [AvaloniaFact]
    public void ABoxIsWhereTheCompositorPutsIt()
    {
        // The gizmo and the picture have to agree, or the boxes stop sitting on
        // the drawings they describe.
        var (vm, strip) = Imported();
        var cell = strip.Cells[1];

        var (x, y, w, h) = vm.CellRect(strip, cell);

        Assert.Equal(strip.OffsetX + cell.Dx + cell.X * strip.Scale, x, 3);
        Assert.Equal(strip.OffsetY + cell.Dy + cell.Y * strip.Scale, y, 3);
        Assert.Equal(cell.Width * strip.Scale, w, 3);
        Assert.Equal(cell.Height * strip.Scale, h, 3);
    }

    [AvaloniaFact]
    public void ClickingInsideABoxFindsIt()
    {
        var (vm, strip) = Imported();
        var (x, y, w, h) = vm.CellRect(strip, strip.Cells[2]);

        Assert.Equal(2, vm.ReferenceCellAt(x + w / 2, y + h / 2));
        Assert.Equal(-1, vm.ReferenceCellAt(-50, -50));
    }

    // ---- moving a selected group of boxes ----------------------------------------

    /// <summary>
    /// The nudge moves and the window onto the sheet does not. A box's X and Y
    /// are which crop of the photograph it shows; moving those scrolls the
    /// picture inside the box and leaves the box where it was, which is a
    /// different operation and takes the registration with it. B110: the group
    /// move moved exactly those.
    /// </summary>
    [AvaloniaFact]
    public void MovingAGroupOfBoxesNudgesThemAndLeavesTheirWindowOnTheSheet()
    {
        var (vm, strip) = Imported();
        var windows = strip.Cells.Select(c => (c.X, c.Y)).ToList();
        vm.Selection.AddRefBoxToSelection(0);
        vm.Selection.AddRefBoxToSelection(2);

        vm.BeginRefBoxesMove();
        vm.UpdateRefBoxesMove(6, -2);
        vm.UpdateRefBoxesMove(6, -2);
        vm.EndRefBoxesMove();

        Assert.Equal(12, strip.Cells[0].Dx, 3);
        Assert.Equal(-4, strip.Cells[0].Dy, 3);
        Assert.Equal(12, strip.Cells[2].Dx, 3);
        Assert.Equal(-4, strip.Cells[2].Dy, 3);
        // The unselected ones stay put, and nobody's crop moved at all.
        Assert.Equal(0, strip.Cells[1].Dx, 3);
        Assert.Equal(0, strip.Cells[3].Dx, 3);
        Assert.Equal(windows, strip.Cells.Select(c => (c.X, c.Y)).ToList());
    }

    /// <summary>
    /// A fraction of a pixel per event used to be rounded to an int before it
    /// was applied, so a slow drag moved nothing at all.
    /// </summary>
    [AvaloniaFact]
    public void ASlowGroupBoxDragStillArrivesWhereItWasTaken()
    {
        var (vm, strip) = Imported();
        vm.Selection.AddRefBoxToSelection(0);

        vm.BeginRefBoxesMove();
        for (var i = 0; i < 10; i++) vm.UpdateRefBoxesMove(0.4, 0);
        vm.EndRefBoxesMove();

        Assert.Equal(4, strip.Cells[0].Dx, 3);
    }

    [AvaloniaFact]
    public void AWholeGroupBoxDragIsOneUndoStep()
    {
        var (vm, strip) = Imported();
        vm.Selection.AddRefBoxToSelection(0);
        vm.Selection.AddRefBoxToSelection(1);

        vm.BeginRefBoxesMove();
        vm.UpdateRefBoxesMove(5, 5);
        vm.UpdateRefBoxesMove(5, 5);
        vm.EndRefBoxesMove();
        Assert.Equal(10, strip.Cells[0].Dx, 3);
        Assert.Equal(10, strip.Cells[1].Dx, 3);

        vm.UndoCommand.Execute(null);

        Assert.Equal(0, strip.Cells[0].Dx, 3);
        Assert.Equal(0, strip.Cells[1].Dx, 3);
    }

    [AvaloniaFact]
    public void AGroupBoxDragThatWentNowhereIsNotAnEdit()
    {
        var (vm, _) = Imported();
        vm.Selection.AddRefBoxToSelection(0);
        var steps = vm.UndoDepth;

        vm.BeginRefBoxesMove();
        vm.EndRefBoxesMove();

        Assert.Equal(steps, vm.UndoDepth);
    }

    // ---- moving, resizing, deleting ---------------------------------------------

    [AvaloniaFact]
    public void MovingABoxMovesOnlyThatOne()
    {
        // Alignment is per drawing. A nudge that took the whole sheet with it
        // is the sheet control, and there is one of those already.
        var (vm, strip) = Imported();
        var others = strip.Cells.Where((_, i) => i != 1).Select(c => c.Dx).ToList();

        vm.MoveReferenceCell(1, 12, -4);

        Assert.Equal(12, strip.Cells[1].Dx, 3);
        Assert.Equal(-4, strip.Cells[1].Dy, 3);
        Assert.Equal(others, strip.Cells.Where((_, i) => i != 1).Select(c => c.Dx));
    }

    [AvaloniaFact]
    public void ResizingABoxShowsMoreOfTheSheetRatherThanScalingIt()
    {
        // A box that scaled its contents would be a second zoom control with
        // no way to tell it from the first.
        var (vm, strip) = Imported();
        var before = strip.Cells[0].Clone();
        var scale = strip.Scale;

        vm.ResizeReferenceCell(0, left: false, top: false, dx: 5 * scale, dy: 2 * scale);

        Assert.Equal(before.X, strip.Cells[0].X);
        Assert.Equal(before.Width + 5, strip.Cells[0].Width);
        Assert.Equal(before.Height + 2, strip.Cells[0].Height);
        Assert.Equal(scale, strip.Scale, 3);
    }

    [AvaloniaFact]
    public void DraggingTheOtherCornerMovesTheOriginToo()
    {
        var (vm, strip) = Imported();
        var before = strip.Cells[1].Clone();
        var scale = strip.Scale;

        vm.ResizeReferenceCell(1, left: true, top: true, dx: 2 * scale, dy: 1 * scale);

        Assert.Equal(before.X + 2, strip.Cells[1].X);
        Assert.Equal(before.Y + 1, strip.Cells[1].Y);
        Assert.Equal(before.Width - 2, strip.Cells[1].Width);
    }

    [AvaloniaFact]
    public void ABoxCannotBeShrunkToNothing()
    {
        // A box with no area is a box you cannot get hold of again.
        var (vm, strip) = Imported();
        var before = strip.Cells[0].Width;

        vm.ResizeReferenceCell(0, left: false, top: false, dx: -1000, dy: 0);

        Assert.Equal(before, strip.Cells[0].Width);
    }

    [AvaloniaFact]
    public void DeletingABoxLeavesTheSheetAloneAndRelaysTheRest()
    {
        var (vm, strip) = Imported();
        var png = strip.Png;

        vm.DeleteReferenceCell(1);

        Assert.Equal(3, vm.ActiveReference!.Cells.Count);
        Assert.Equal(png, vm.ActiveReference.Png);
        // The slots follow, or the frames after the gap show the wrong drawing.
        Assert.Equal([0, 1, 2], vm.ActiveReference.Slots.Where(s => s >= 0).ToList());
    }

    [AvaloniaFact]
    public void ABoxCanBeDrawnByHand()
    {
        // The escape hatch from detection: a sheet whose rows touch has no
        // gutter, and no amount of looking finds a boundary that is not there.
        var (vm, strip) = Imported();
        var scale = strip.Scale;

        vm.AddReferenceCell(strip.OffsetX + 5 * scale, strip.OffsetY + 1 * scale, 8 * scale, 6 * scale);

        var added = vm.ActiveReference!.Cells[^1];
        Assert.Equal(5, added.X);
        Assert.Equal(1, added.Y);
        Assert.Equal(8, added.Width);
        Assert.Equal(6, added.Height);
        Assert.Equal(vm.ActiveReference.Cells.Count - 1, vm.SelectedReferenceCell);
    }

    // ---- pivots -----------------------------------------------------------------

    [AvaloniaFact]
    public void APivotIsPlacedInSheetPixelsSoTheSheetCanMoveUnderIt()
    {
        var (vm, strip) = Imported();
        var scale = strip.Scale;
        var docX = strip.OffsetX + 6 * scale;
        var docY = strip.OffsetY + 9 * scale;

        vm.SetReferencePivot(0, docX, docY);

        Assert.True(strip.Cells[0].HasPivot);
        Assert.Equal(6, strip.Cells[0].PivotX!.Value, 3);
        Assert.Equal(9, strip.Cells[0].PivotY!.Value, 3);

        // Nudge the whole sheet: the pivot is still on the same part of the art.
        vm.NudgeReference(20, 20);
        Assert.Equal(6, strip.Cells[0].PivotX!.Value, 3);
    }

    [AvaloniaFact]
    public void PlacingAPivotIsUndoable()
    {
        var (vm, strip) = Imported();
        vm.SetReferencePivot(0, strip.OffsetX + 3 * strip.Scale, strip.OffsetY + 3 * strip.Scale);

        vm.UndoCommand.Execute(null);

        Assert.False(vm.ActiveReference!.Cells[0].HasPivot);
    }

    // ---- keyframes from what was found ------------------------------------------

    [AvaloniaFact]
    public void GeneratingKeyframesGrowsTheTimelineToFitTheSheet()
    {
        var (vm, strip) = Imported(6);
        // A document that has fewer frames than the sheet has drawings.
        while (vm.Doc.Scene.FrameCount > 1) vm.DeleteFrameCommand.Execute(null);
        Assert.True(vm.ReferenceNeedsKeyframes);

        vm.GenerateReferenceKeyframesCommand.Execute(null);

        Assert.True(vm.Doc.Scene.FrameCount >= strip.Cells.Count);
        Assert.False(vm.ReferenceNeedsKeyframes);
        // And every drawing on the sheet is exposed somewhere.
        Assert.Equal(
            Enumerable.Range(0, strip.Cells.Count),
            vm.ActiveReference!.Slots.Where(s => s >= 0));
    }

    [AvaloniaFact]
    public void GeneratingKeyframesRegistersTheCellsOnTheirPivots()
    {
        // The peg bar: pick the point that should not move, and every drawing
        // slides until it sits there.
        var (vm, strip) = Imported();
        var scale = strip.Scale;
        for (var i = 0; i < strip.Cells.Count; i++)
        {
            // A pivot two pixels further right in each cell than the one before.
            vm.SetReferencePivot(i, strip.OffsetX + (i * 10 + 3 + i * 2) * scale, strip.OffsetY + 8 * scale);
        }

        vm.GenerateReferenceKeyframesCommand.Execute(null);

        var live = vm.ActiveReference!;
        var first = live.Cells[0];
        var (originX, originY) = first.Pivot;
        foreach (var cell in live.Cells)
        {
            var (px, py) = cell.Pivot;
            // Every pivot lands at the same place on the canvas.
            Assert.Equal(originX * scale + live.OffsetX, px * scale + live.OffsetX + cell.Dx, 3);
            Assert.Equal(originY * scale + live.OffsetY, py * scale + live.OffsetY + cell.Dy, 3);
        }
    }

    [AvaloniaFact]
    public void AligningTwiceChangesNothing()
    {
        // Measured against the first cell, so the sheet cannot wander off the
        // canvas one press at a time.
        var (vm, strip) = Imported();
        for (var i = 0; i < strip.Cells.Count; i++)
        {
            vm.SetReferencePivot(i, strip.OffsetX + (i * 10 + 4) * strip.Scale, strip.OffsetY + 7 * strip.Scale);
        }

        vm.GenerateReferenceKeyframesCommand.Execute(null);
        var once = vm.ActiveReference!.Cells.Select(c => (c.Dx, c.Dy)).ToList();
        vm.GenerateReferenceKeyframesCommand.Execute(null);

        Assert.Equal(once, vm.ActiveReference!.Cells.Select(c => (c.Dx, c.Dy)).ToList());
    }

    // ---- the gizmos on the canvas ------------------------------------------------

    [AvaloniaFact]
    public void TheGizmosAreAbsentUntilTheModeIsOn()
    {
        // View-only chrome, like the camera frame: an artist who never touches
        // a reference sees none of it.
        var window = new Lightbox.App.Views.MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        var canvas = window.GetVisualDescendants().OfType<Lightbox.App.Rendering.CanvasControl>().First();

        vm.ImportReference("Run", Sheet());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Null(canvas.ReferenceBoxes);

        vm.ReferenceGridEditMode = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(canvas.ReferenceBoxes);
        Assert.Equal(vm.ActiveReference!.Cells.Count, canvas.ReferenceBoxes!.Count);

        vm.ReferenceGridEditMode = false;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Null(canvas.ReferenceBoxes);
    }

    [AvaloniaFact]
    public void AGizmoSitsWhereItsDrawingIs()
    {
        // The gizmo and the picture have to agree, or the boxes stop sitting
        // on the drawings they describe.
        var window = new Lightbox.App.Views.MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        var canvas = window.GetVisualDescendants().OfType<Lightbox.App.Rendering.CanvasControl>().First();

        var strip = vm.ImportReference("Run", Sheet())!;
        vm.ReferenceGridEditMode = true;
        vm.SelectedReferenceCell = 2;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var (x, y, w, h) = vm.CellRect(strip, strip.Cells[2]);
        var box = canvas.ReferenceBoxes![2];
        Assert.Equal((float)x, box.X, 2);
        Assert.Equal((float)y, box.Y, 2);
        Assert.Equal((float)w, box.W, 2);
        Assert.Equal((float)h, box.H, 2);
        Assert.True(box.Selected);
        Assert.False(canvas.ReferenceBoxes[0].Selected);
    }

    [AvaloniaFact]
    public void AnUnplacedPivotStillGetsAMark()
    {
        // Falling back to the middle of the cell's foot, which is where a
        // standing figure's contact point usually is. No mark at all would
        // make "place the pivot" a thing you had to know about.
        var window = new Lightbox.App.Views.MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        var canvas = window.GetVisualDescendants().OfType<Lightbox.App.Rendering.CanvasControl>().First();

        var strip = vm.ImportReference("Run", Sheet())!;
        vm.ReferenceGridEditMode = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var box = canvas.ReferenceBoxes![0];
        Assert.False(strip.Cells[0].HasPivot);
        Assert.Equal(box.X + box.W / 2, box.PivotX, 2);
        Assert.Equal(box.Y + box.H, box.PivotY, 2);
    }

    [AvaloniaFact]
    public void GeneratingKeyframesIsOneUndoStep()
    {
        var (vm, _) = Imported(6);
        while (vm.Doc.Scene.FrameCount > 1) vm.DeleteFrameCommand.Execute(null);
        var before = vm.Doc.Scene.FrameCount;

        vm.GenerateReferenceKeyframesCommand.Execute(null);
        vm.UndoCommand.Execute(null);

        Assert.Equal(before, vm.Doc.Scene.FrameCount);
    }
}

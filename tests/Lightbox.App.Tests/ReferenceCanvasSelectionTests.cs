using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Picking a projected reference up on the canvas with the Arrow (Q108): select
/// it, move it, scale it from a corner, and lock it so a stray drag cannot.
/// </summary>
/// <remarks>
/// Driven at the view-model boundary, which is where every decision lives — the
/// canvas contributes the pointer and nothing else. <c>ADragOfAReferenceIsOneUndoStep</c>
/// is B192's, and it is here because the gesture that fixes it is this one.
/// </remarks>
[Collection("BrushState")]
public class ReferenceCanvasSelectionTests : BrushStateIsolated
{
    /// <summary>One 40×10 sheet of four cells, imported and pinned to the frame.</summary>
    private static (MainViewModel Vm, Lightbox.Core.Documents.ReferenceStrip Strip) Imported()
    {
        using var bmp = new SKBitmap(40, 10, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            for (var i = 0; i < 4; i++)
            {
                using var paint = new SKPaint { Color = new SKColor(220, 20, 20) };
                canvas.DrawRect(SKRect.Create(i * 10 + 2, 2, 6, 6), paint);
            }
        }
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        var strip = vm.ImportReference("Run", PngCodec.Encode(bmp))!;
        return (vm, strip);
    }

    /// <summary>
    /// A point inside the reference's box at the playhead — read from the strip
    /// rather than from the selection, since it is what a first click aims at.
    /// </summary>
    private static (double X, double Y) Inside(
        MainViewModel vm, Lightbox.Core.Documents.ReferenceStrip strip)
    {
        var (x, y, w, h) = vm.CellRect(strip, strip.CellAt(vm.CurrentFrameIndex)!);
        return (x + w / 2, y + h / 2);
    }

    // ---- selecting ------------------------------------------------------------------

    [AvaloniaFact]
    public void ClickingAProjectedReferenceSelectsIt()
    {
        var (vm, strip) = Imported();
        var cell = strip.CellAt(vm.CurrentFrameIndex)!;
        var (x, y, w, h) = vm.CellRect(strip, cell);

        Assert.True(vm.SelectReferenceOnCanvasAt(x + w / 2, y + h / 2));

        Assert.True(vm.ReferenceSelectedOnCanvas);
        Assert.Same(strip, vm.SelectedCanvasReference);
        // The box the canvas draws its outline and grips from.
        Assert.NotNull(vm.CanvasReferenceBounds);
    }

    [AvaloniaFact]
    public void ClickingWhereThereIsNoReferenceDropsTheSelection()
    {
        var (vm, strip) = Imported();
        var (x, y) = Inside(vm, strip);
        vm.SelectReferenceOnCanvasAt(x, y);

        Assert.False(vm.SelectReferenceOnCanvasAt(-5000, -5000));

        Assert.False(vm.ReferenceSelectedOnCanvas);
        Assert.Null(vm.CanvasReferenceBounds);
    }

    [AvaloniaFact]
    public void LeavingTheArrowLetsTheReferenceGo()
    {
        var (vm, strip) = Imported();
        vm.SelectToolCommand.Execute(ToolId.Arrow);
        var (x, y) = Inside(vm, strip);
        vm.SelectReferenceOnCanvasAt(x, y);

        vm.SelectToolCommand.Execute(ToolId.Brush);

        // B147's rule: a highlight that outlives the tool points at a gesture
        // the artist no longer has.
        Assert.False(vm.ReferenceSelectedOnCanvas);
    }

    // ---- moving ---------------------------------------------------------------------

    [AvaloniaFact]
    public void DraggingASelectedReferenceMovesIt()
    {
        var (vm, strip) = Imported();
        var (x, y) = Inside(vm, strip);
        vm.SelectReferenceOnCanvasAt(x, y);
        var before = (strip.OffsetX, strip.OffsetY);

        Assert.True(vm.BeginReferenceGesture());
        vm.DragReferenceBy(12, -7);
        vm.EndReferenceGesture();

        Assert.Equal(before.OffsetX + 12, strip.OffsetX, 3);
        Assert.Equal(before.OffsetY - 7, strip.OffsetY, 3);
    }

    [AvaloniaFact]
    public void ADragOfAReferenceIsOneUndoStep()
    {
        // B192: the align-mode drag wrote an undo entry per pointer event, so a
        // single move took dozens of presses of Ctrl+Z to take back — and re-ran
        // the whole document-changed storm on each one.
        var (vm, strip) = Imported();
        var (x, y) = Inside(vm, strip);
        vm.SelectReferenceOnCanvasAt(x, y);
        var before = (strip.OffsetX, strip.OffsetY);

        vm.BeginReferenceGesture();
        for (var i = 0; i < 20; i++) vm.DragReferenceBy(3, 2);
        vm.EndReferenceGesture();
        Assert.Equal(before.OffsetX + 60, strip.OffsetX, 3);

        vm.UndoCommand.Execute(null);

        Assert.Equal(before.OffsetX, strip.OffsetX, 3);
        Assert.Equal(before.OffsetY, strip.OffsetY, 3);
    }

    [AvaloniaFact]
    public void AClickThatMovedNothingIsNotAnUndoStep()
    {
        var (vm, strip) = Imported();
        var (x, y) = Inside(vm, strip);
        vm.SelectReferenceOnCanvasAt(x, y);
        strip.OffsetX = 40;

        vm.BeginReferenceGesture();
        vm.EndReferenceGesture();
        vm.UndoCommand.Execute(null);

        // The undo went to whatever came before the selection, because the
        // gesture registered nothing — an empty step is one the artist has to
        // press through for no reason.
        Assert.Equal(40, strip.OffsetX, 3);
    }

    // ---- scaling --------------------------------------------------------------------

    [AvaloniaFact]
    public void ACornerScalesUniformlyAboutTheOppositeCorner()
    {
        var (vm, strip) = Imported();
        var (x, y) = Inside(vm, strip);
        vm.SelectReferenceOnCanvasAt(x, y);
        var (bx, by, bw, bh) = vm.CanvasReferenceBounds!.Value;
        var aspect = bw / bh;

        vm.BeginReferenceGesture();
        // Grab the bottom-right, so the top-left stays put.
        vm.ScaleReferenceAbout(2, bx, by);
        vm.EndReferenceGesture();

        var (nx, ny, nw, nh) = vm.CanvasReferenceBounds!.Value;
        Assert.Equal(bx, nx, 3);
        Assert.Equal(by, ny, 3);
        Assert.Equal(bw * 2, nw, 3);
        // Uniform: one scale for every frame is the record's own rule, so the
        // shape of the reference cannot change.
        Assert.Equal(aspect, nw / nh, 3);
        Assert.Equal(2, strip.Scale, 3);
    }

    [AvaloniaFact]
    public void ScalingIsMeasuredFromTheGesturesStartRatherThanCompounding()
    {
        var (vm, strip) = Imported();
        var (x, y) = Inside(vm, strip);
        vm.SelectReferenceOnCanvasAt(x, y);
        var (bx, by, _, _) = vm.CanvasReferenceBounds!.Value;

        vm.BeginReferenceGesture();
        vm.ScaleReferenceAbout(3, bx, by);
        vm.ScaleReferenceAbout(2, bx, by);
        vm.ScaleReferenceAbout(1, bx, by);
        vm.EndReferenceGesture();

        // Dragged out and back: it comes home. A factor applied against the
        // current scale each event would leave it at 6×.
        Assert.Equal(1, strip.Scale, 3);
    }

    [AvaloniaFact]
    public void AScaleIsOneUndoStepAndTakesTheOffsetBackWithIt()
    {
        var (vm, strip) = Imported();
        var (x, y) = Inside(vm, strip);
        vm.SelectReferenceOnCanvasAt(x, y);
        var before = (strip.OffsetX, strip.OffsetY, strip.Scale);
        var (bx, by, _, _) = vm.CanvasReferenceBounds!.Value;

        vm.BeginReferenceGesture();
        // About the bottom-right, which moves the offset as well as the scale.
        vm.ScaleReferenceAbout(0.5, bx + 40, by + 10);
        vm.EndReferenceGesture();
        Assert.NotEqual(before.Scale, strip.Scale);

        vm.UndoCommand.Execute(null);

        Assert.Equal(before.Scale, strip.Scale, 3);
        Assert.Equal(before.OffsetX, strip.OffsetX, 3);
        Assert.Equal(before.OffsetY, strip.OffsetY, 3);
    }

    // ---- locking --------------------------------------------------------------------

    [AvaloniaFact]
    public void ALockedReferenceStillSelectsAndRefusesToMove()
    {
        var (vm, strip) = Imported();
        var (x, y) = Inside(vm, strip);
        vm.SelectReferenceOnCanvasAt(x, y);
        vm.SetReferenceLocked(strip, true);
        var before = strip.OffsetX;

        // Selectable, so the artist can see the box and see why it will not
        // move — a gesture that silently does nothing is the worse failure.
        Assert.True(vm.SelectReferenceOnCanvasAt(x, y));
        Assert.True(vm.CanvasReferenceLocked);
        Assert.False(vm.BeginReferenceGesture());
        vm.DragReferenceBy(50, 50);

        Assert.Equal(before, strip.OffsetX, 3);
    }

    [AvaloniaFact]
    public void TheSweepLockPinsEveryReferenceWithoutTouchingTheDocument()
    {
        var (vm, strip) = Imported();
        var (x, y) = Inside(vm, strip);
        vm.SelectReferenceOnCanvasAt(x, y);

        vm.Workspace.ReferencesLocked = true;

        Assert.True(vm.CanvasReferenceLocked);
        Assert.False(vm.BeginReferenceGesture());
        // The document says nothing about it: the sweep is how the screen is
        // set up, which is the same place the guide lock lives.
        Assert.Null(strip.Locked);
    }

    [AvaloniaFact]
    public void LockingIsUndoableAndUnlockingLeavesNoKeyBehind()
    {
        var (vm, strip) = Imported();

        vm.SetReferenceLocked(strip, true);
        Assert.True(strip.IsLocked);
        vm.UndoCommand.Execute(null);
        Assert.False(strip.IsLocked);

        vm.SetReferenceLocked(strip, true);
        vm.SetReferenceLocked(strip, false);
        // Null rather than false: a document that locked something and changed
        // its mind is byte-identical to one that never did.
        Assert.Null(strip.Locked);
    }

    [AvaloniaFact]
    public void AnUnlockedReferenceWritesNoKey()
    {
        var doc = new Lightbox.Core.Documents.Doc();
        (doc.Scene.References ??= []).Add(new Lightbox.Core.Documents.ReferenceStrip { Name = "Run" });

        var json = Lightbox.Core.Serialization.DocJson.Serialize(doc);

        Assert.DoesNotContain("\"locked\"", json);
        Assert.DoesNotContain("\"isLocked\"", json);
    }
}

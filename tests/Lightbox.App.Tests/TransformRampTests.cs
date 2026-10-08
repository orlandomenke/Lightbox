using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.App.Tests;

/// <summary>
/// A transform that ramps over the frames (Q216): none of it on the first
/// drawing, the whole box on the last, eased between; holds kept, reused
/// drawings split into copies, drawings outside the range never touched.
/// </summary>
[Collection("BrushState")]
public class TransformRampTests : BrushStateIsolated
{
    private static MainViewModel Vm()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 16;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        return vm;
    }

    private static void Line(MainViewModel vm, double x, double y = 100)
    {
        vm.BeginStroke(x, y, 1);
        vm.MoveStroke(x + 60, y, 1);
        vm.EndStroke();
    }

    /// <summary>An in-place cycle: <paramref name="count"/> drawings, all at x = 100.</summary>
    private static MainViewModel InPlace(int count)
    {
        var vm = Vm();
        Line(vm, 100);
        for (var i = 1; i < count; i++)
        {
            vm.AddFrameCommand.Execute(null);
            vm.CurrentFrameIndex = i;
            Line(vm, 100);
        }
        vm.CurrentFrameIndex = 0;
        return vm;
    }

    private static Layer LayerOf(MainViewModel vm) => vm.Doc.Scene.Layers[vm.ActiveLayerIndex];

    private static double X(MainViewModel vm, int cel) => ((Frame)LayerOf(vm).Cels[cel].Frame!).Strokes[0].Points[0].X;

    private static void Ramp(MainViewModel vm, double dx, RampEase ease = RampEase.Linear)
    {
        Assert.True(vm.BeginLayerTransform());
        Assert.True(vm.RampAvailable);
        vm.RampOverFrames = true;
        vm.RampOverFramesEase = ease;
        vm.CommitTransformAffine(0, 0, 1, 1, 0, dx, 0);
    }

    [AvaloniaFact]
    public void AnInPlaceCycleTravels()
    {
        var vm = InPlace(5);
        Ramp(vm, 100);

        Assert.Equal(100, X(vm, 0), 3);   // the first drawing: none of it
        Assert.Equal(125, X(vm, 1), 3);
        Assert.Equal(150, X(vm, 2), 3);
        Assert.Equal(175, X(vm, 3), 3);
        Assert.Equal(200, X(vm, 4), 3);   // the last: the whole box
        Assert.False(vm.RampOverFrames);  // ends with the session

        vm.UndoCommand.Execute(null);     // one step for the lot
        for (var i = 0; i < 5; i++) Assert.Equal(100, X(vm, i), 3);
    }

    [AvaloniaFact]
    public void ADrawingOn2sMovesOn2s()
    {
        // 0:A 1:hold 2:B 3:hold 4:C — three positions, so B gets half and
        // the held cels show their drawing's share rather than one of their own.
        var vm = InPlace(5);
        vm.ClearCelAt(vm.LayerRows[^1].Cells.First(c => c.Index == 1));
        vm.ClearCelAt(vm.LayerRows[^1].Cells.First(c => c.Index == 3));
        Ramp(vm, 100);

        Assert.Null(LayerOf(vm).Cels[1].Frame);  // the hold is still a hold
        Assert.Equal(150, X(vm, 2), 3);
        Assert.Equal(200, X(vm, 4), 3);
    }

    [AvaloniaFact]
    public void AReusedDrawingIsSplitSoEachPlaceTakesItsOwnShare()
    {
        // A loop: the first drawing shown again at the end. One drawing cannot
        // stand at both ends of a walk, so the later place gets a copy.
        var vm = InPlace(3);
        var layer = LayerOf(vm);
        var first = layer.Cels[0].Frame!;
        layer.Cels[2].Frame = first;

        Ramp(vm, 100);

        Assert.Same(first, layer.Cels[0].Frame);       // the original stays at the start
        Assert.NotSame(first, layer.Cels[2].Frame);    // the reuse became its own drawing
        Assert.Equal(100, X(vm, 0), 3);
        Assert.Equal(150, X(vm, 1), 3);
        Assert.Equal(200, X(vm, 2), 3);
        Assert.Contains("1 copy", vm.AiStatus);

        // Undo restores from a snapshot, so the reuse comes back as the same
        // drawing by id rather than the same object.
        vm.UndoCommand.Execute(null);
        var restored = LayerOf(vm);
        Assert.Equal(restored.Cels[0].Frame!.Id, restored.Cels[2].Frame!.Id);
        Assert.Equal(100, X(vm, 2), 3);
    }

    [AvaloniaFact]
    public void ADrawingAlsoShownOutsideTheRangeIsCopiedAndTheOutsideStays()
    {
        // Marked cels 1..3; drawing 3 is the same drawing as cel 0, outside.
        var vm = InPlace(4);
        var layer = LayerOf(vm);
        var shared = layer.Cels[0].Frame!;
        layer.Cels[3].Frame = shared;
        foreach (var i in new[] { 1, 2, 3 }) vm.ToggleCelSelection(vm.LayerRows[^1].Cells.First(c => c.Index == i));
        vm.TransformScope = TransformScope.CelRange;

        Assert.Equal((vm.ActiveLayerIndex, 1, 3), vm.CelRange);
        Assert.True(vm.BeginTransform());
        vm.RampOverFrames = true;
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 100, 0);

        Assert.Same(shared, layer.Cels[0].Frame);
        Assert.Equal(100, X(vm, 0), 3);              // outside the range: untouched
        Assert.Equal(150, X(vm, 2), 3);
        Assert.NotSame(shared, layer.Cels[3].Frame);
        Assert.Equal(200, X(vm, 3), 3);
    }

    [AvaloniaFact]
    public void AnEaseShapesTheMiddle()
    {
        var vm = InPlace(3);
        Ramp(vm, 100, RampEase.EaseIn);
        Assert.Equal(125, X(vm, 1), 3);   // a quarter at half-way
    }

    [AvaloniaFact]
    public void AMirrorIsRefusedAndTheSessionStaysOpen()
    {
        var vm = InPlace(3);
        Assert.True(vm.BeginLayerTransform());
        vm.RampOverFrames = true;
        vm.CommitTransformAffine(130, 100, -1, 1, 0, 0, 0);

        Assert.True(vm.TransformActive);
        Assert.Contains("mirror", vm.AiStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(100, X(vm, 2), 3);
        vm.CancelTransform();
    }

    [AvaloniaFact]
    public void ThereIsNoRampOnASingleDrawing()
    {
        var vm = InPlace(3);
        Assert.True(vm.BeginTransform());      // this drawing
        Assert.False(vm.RampAvailable);
        vm.RampOverFrames = true;
        Assert.False(vm.RampOverFrames);
        vm.CancelTransform();
    }

    [AvaloniaFact]
    public void TheRepaintCoversADrawingHalfWayRoundATurn()
    {
        // A half turn about (300, 100): the drawing starts at x 100..160 and
        // ends at x 440..500, both on y = 100, and half-way it stands upright
        // at x = 300, y -100..-40 — outside the bounding box of both ends. The
        // plain rule (repaint the old box and the new) leaves it unpainted on a
        // bounded publish. The headless publish recomposes more than the dirty
        // rectangle, so pixels cannot catch it; the region itself is the check.
        var vm = InPlace(3);
        Assert.True(vm.BeginLayerTransform());
        vm.RampOverFrames = true;
        vm.SetTransformBox(new AffineParts(300, 100, 1, 1, Math.PI, 0, 0));

        var region = vm.RampDirtyRegion();

        Assert.NotNull(region);
        var r = region!.Value;
        Assert.True(r.Contains(300, -70),
            $"the half-way drawing (x 300, y -100..-40) is outside the repaint {r}");
        Assert.True(r.Width < vm.Doc.Scene.Width,
            $"the repaint is the whole canvas width ({r}) — invariant 6 wants it bounded");
        vm.CancelTransform();
    }

    [AvaloniaFact]
    public void ThePreviewShowsTheDrawingAtThePlayheadAtItsShare()
    {
        // A diagonal move, frame 2 of 3: half of it, not none and not all.
        var vm = InPlace(3);
        vm.CurrentFrameIndex = 1;
        vm.Onion.Enabled = false;   // the drawing at the playhead alone; ghosts have their own test
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        Assert.True(vm.BeginLayerTransform());
        vm.RampOverFrames = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var box = new AffineParts(0, 0, 1, 1, 0, 200, 200);
        vm.SetTransformBox(box);
        vm.PreviewTransform(SkiaSharp.SKMatrix.CreateTranslation(200, 200));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(latest);
        using var bmp = SkiaSharp.SKBitmap.FromImage(latest!.Image)!;
        static bool Inked(SkiaSharp.SKColor c) => c.Alpha > 20 && c.Red < 120;
        Assert.True(Inked(bmp.GetPixel(230, 200)), $"the drawing at frame 2 of 3 is not at half the move: {bmp.GetPixel(230, 200)}");
        Assert.False(Inked(bmp.GetPixel(330, 300)), "the drawing at the playhead was drawn at the whole box");
        Assert.False(Inked(bmp.GetPixel(130, 100)), "the drawing at the playhead was left where it started");
        vm.CancelTransform();
    }

    [Fact]
    public void EveryEaseHasAName()
    {
        Assert.Equal("Even", RampEaseText.Label(RampEase.Linear));
        Assert.Equal("Ease in", RampEaseText.Label(RampEase.EaseIn));
        Assert.Equal("Ease out", RampEaseText.Label(RampEase.EaseOut));
        Assert.Equal("Ease in and out", RampEaseText.Label(RampEase.EaseInOut));
    }
}

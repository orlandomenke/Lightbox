using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B5 — a transform has to show the drawing moving, not a box moving over a
/// drawing that stays put. The same argument the live-stroke preview settled:
/// an artist cannot judge a change they are not being shown.
///
/// These drive the view model half, which is where the pixels live. The gizmo
/// half is a matrix handed across on every change
/// (<c>CanvasControl.TransformMatrix</c>).
/// </summary>
[Collection("BrushState")]
public class TransformPreviewTests : BrushStateIsolated
{
    /// <summary>A black bar across the upper half, on a transparent document.</summary>
    private static MainViewModel Painted()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 30;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        vm.AntiAliasing = false;
        vm.BeginStroke(60, 60, 1);
        vm.MoveStroke(240, 60, 1);
        vm.EndStroke();
        return vm;
    }

    private static SKColor PixelAt(MainViewModel vm, int x, int y)
    {
        RenderSnapshot? latest = null;
        void Capture(RenderSnapshot s) => latest = s;
        vm.SnapshotChanged += Capture;
        vm.PublishSnapshot();
        vm.SnapshotChanged -= Capture;
        using var bmp = SKBitmap.FromImage(latest!.Image);
        return bmp.GetPixel(x, y);
    }

    [AvaloniaFact]
    public void TransformPreviewMovesThePixels()
    {
        var vm = Painted();
        Assert.True(PixelAt(vm, 150, 60).Alpha > 0);   // the bar
        Assert.Equal(0, PixelAt(vm, 150, 200).Alpha);  // empty below it

        Assert.True(vm.BeginTransform());
        vm.PreviewTransform(SKMatrix.CreateTranslation(0, 140));

        // Nothing has been applied yet — this is a preview — but the drawing
        // has to already be down there.
        Assert.True(PixelAt(vm, 150, 200).Alpha > 0, "the transform showed no pixels mid-drag");
        Assert.Equal(0, PixelAt(vm, 150, 60).Alpha);
    }

    [AvaloniaFact]
    public void ThePreviewIsNotAnEdit()
    {
        // Invariant 1: the record is the document. A drag that has not been
        // applied must not have touched a single point, and must leave no undo
        // step behind.
        var vm = Painted();
        var strokes = ((Frame)vm.PaintLayer().Cels[0].Frame!).Strokes;
        var before = strokes[0].Points.Select(p => (p.X, p.Y)).ToList();
        var undos = vm.UndoDepth;

        Assert.True(vm.BeginTransform());
        vm.PreviewTransform(SKMatrix.CreateTranslation(0, 140));
        _ = PixelAt(vm, 150, 200);

        Assert.Equal(before, strokes[0].Points.Select(p => (p.X, p.Y)).ToList());
        Assert.Equal(undos, vm.UndoDepth);
    }

    [AvaloniaFact]
    public void CancellingPutsThePixelsBack()
    {
        var vm = Painted();
        Assert.True(vm.BeginTransform());
        vm.PreviewTransform(SKMatrix.CreateTranslation(0, 140));
        Assert.True(PixelAt(vm, 150, 200).Alpha > 0);

        vm.CancelTransform();

        Assert.Equal(0, PixelAt(vm, 150, 200).Alpha);
        Assert.True(PixelAt(vm, 150, 60).Alpha > 0);
    }

    [AvaloniaFact]
    public void WhatThePreviewShowedIsWhatApplyProduces()
    {
        var vm = Painted();
        Assert.True(vm.BeginTransform());
        vm.PreviewTransform(SKMatrix.CreateTranslation(0, 140));
        var previewed = PixelAt(vm, 150, 200);

        vm.CommitTransformAffine(0, 0, 1, 1, 0, 0, 140);
        var applied = PixelAt(vm, 150, 200);

        Assert.True(previewed.Alpha > 0 && applied.Alpha > 0);
        // Both are the same black bar; the preview blits finished pixels while
        // the commit re-renders the strokes, so they agree to within resampling.
        Assert.InRange(Math.Abs(previewed.Alpha - applied.Alpha), 0, 8);
    }

    [AvaloniaFact]
    public void WithASelectionOnlyTheSelectedStrokesMove()
    {
        // A region-limited transform moves some strokes and leaves the rest.
        // Previewing the whole layer through the matrix would show the artist
        // a result the commit will not produce.
        var vm = Painted();
        vm.BrushSize = 20;
        vm.BeginStroke(60, 260, 1);   // a second bar, well below the selection
        vm.MoveStroke(240, 260, 1);
        vm.EndStroke();

        vm.ApplySelectionShape(                 // the top bar only
            [new(20, 20, 1), new(280, 20, 1), new(280, 120, 1), new(20, 120, 1)],
            add: false, subtract: false);
        Assert.True(vm.BeginTransform());
        vm.PreviewTransform(SKMatrix.CreateTranslation(0, 140));

        Assert.True(PixelAt(vm, 150, 200).Alpha > 0, "the selected bar did not move");
        Assert.True(PixelAt(vm, 150, 260).Alpha > 0, "the unselected bar moved with it");
        Assert.Equal(0, PixelAt(vm, 150, 60).Alpha);
    }
}

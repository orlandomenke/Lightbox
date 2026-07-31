using Avalonia;
using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using SkiaSharp;

namespace Lightbox.App.Tests;

public class CanvasViewTests
{
    /// <summary>An 800×600 canvas showing a 100×100 document (fit scale = 6).</summary>
    private static CanvasControl NewCanvas()
    {
        var canvas = new CanvasControl();
        canvas.Measure(new Size(800, 600));
        canvas.Arrange(new Rect(0, 0, 800, 600));
        using var surface = SKSurface.Create(new SKImageInfo(100, 100));
        canvas.UpdateSnapshot(new RenderSnapshot(surface.Snapshot(), 100, 100));
        return canvas;
    }

    [AvaloniaFact]
    public void DefaultView_MapsViewCenterToDocCenter_AtFitScale()
    {
        var canvas = NewCanvas();
        var (cx, cy) = canvas.ViewToDoc(new Point(400, 300));
        Assert.Equal(50, cx, 6);
        Assert.Equal(50, cy, 6);

        // 60 view px right of center at fit scale 6 → 10 doc units.
        var (x, _) = canvas.ViewToDoc(new Point(460, 300));
        Assert.Equal(60, x, 6);
    }

    [AvaloniaFact]
    public void Zoom_KeepsAnchorFixed_AndScalesMapping()
    {
        var canvas = NewCanvas();
        canvas.ZoomAt(new Point(400, 300), 2); // zoom ×2 around the center

        var (cx, cy) = canvas.ViewToDoc(new Point(400, 300));
        Assert.Equal(50, cx, 6); // anchor unchanged
        Assert.Equal(50, cy, 6);

        var (x, _) = canvas.ViewToDoc(new Point(460, 300));
        Assert.Equal(55, x, 6); // 60 px / (6 × 2) = 5 doc units
        Assert.Equal(200, canvas.ZoomPercent, 6);
    }

    [AvaloniaFact]
    public void Mirror_FlipsHorizontally_AroundViewCenter()
    {
        var canvas = NewCanvas();
        canvas.ToggleMirror();
        Assert.True(canvas.IsMirrored);

        var (cx, _) = canvas.ViewToDoc(new Point(400, 300));
        Assert.Equal(50, cx, 6); // center stays put

        var (x, y) = canvas.ViewToDoc(new Point(460, 300));
        Assert.Equal(40, x, 6); // right in view = left in the document
        Assert.Equal(50, y, 6);

        canvas.ToggleMirror();
        var (x2, _) = canvas.ViewToDoc(new Point(460, 300));
        Assert.Equal(60, x2, 6); // back to normal
    }

    [AvaloniaFact]
    public void Rotate90_SwapsAxes_InTheMapping()
    {
        var canvas = NewCanvas();
        canvas.RotateAt(new Point(400, 300), 90);

        // A point below the view center now maps left/right in doc space.
        var (x, y) = canvas.ViewToDoc(new Point(400, 360));
        Assert.Equal(60, x, 4);
        Assert.Equal(50, y, 4);
    }

    [AvaloniaFact]
    public void ResetView_RestoresTheDefaultMapping()
    {
        var canvas = NewCanvas();
        canvas.ZoomAt(new Point(100, 100), 3);
        canvas.RotateBy(37);
        canvas.ToggleMirror();

        canvas.ResetView();

        Assert.Equal(100, canvas.ZoomPercent, 6);
        Assert.False(canvas.IsMirrored);
        var (x, y) = canvas.ViewToDoc(new Point(460, 300));
        Assert.Equal(60, x, 6);
        Assert.Equal(50, y, 6);
    }

    [AvaloniaFact]
    public void ViewTransform_NeverTouchesTheDocument()
    {
        // The whole point of view tools: paint through a transformed view and
        // the recorded stroke is identical to painting untransformed.
        var vm = new Lightbox.App.ViewModels.MainViewModel(null) { SmoothStrokes = false };
        var canvas = NewCanvas();
        canvas.ZoomAt(new Point(400, 300), 2);
        canvas.ToggleMirror();
        canvas.RotateBy(30);

        // Simulate the input path: view point → doc point → VM (as the control does).
        var (x, y) = canvas.ViewToDoc(new Point(400, 300));
        vm.BeginStroke(x, y, 1);
        vm.EndStroke();

        var frame = (Lightbox.Core.Documents.PaintedFrame)vm.Doc.Scene.Layers[0].Cels[0].Frame!;
        var p = Assert.Single(frame.Strokes).Points[0];
        // View center still maps to doc center — the document never saw the transform.
        Assert.Equal(50, p.X, 6);
        Assert.Equal(50, p.Y, 6);
    }
}

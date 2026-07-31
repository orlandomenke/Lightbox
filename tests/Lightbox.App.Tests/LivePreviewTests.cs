using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

public class LivePreviewTests
{
    [AvaloniaFact]
    public void BatchedMoves_ProduceOneStrokeWithAllPoints()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.BeginStroke(0, 0, 0.5);
        vm.MoveStrokeBatch(
        [
            new MainViewModel.PointerSample(10, 0, 0.5),
            new MainViewModel.PointerSample(20, 0, 0.6),
            new MainViewModel.PointerSample(30, 0, 0.7),
        ]);
        vm.MoveStrokeBatch([new MainViewModel.PointerSample(40, 0, 0.8)]);
        vm.EndStroke();

        var frame = (PaintedFrame)vm.Doc.Scene.Layers[0].Cels[0].Frame!;
        var stroke = Assert.Single(frame.Strokes);
        Assert.Equal(5, stroke.Points.Count);
        Assert.Equal(new StrokePoint(40, 0, 0.8), stroke.Points[^1]);
    }

    [AvaloniaFact]
    public void CommittedPixels_MatchDirectRasterization()
    {
        // The incremental live path must not change what gets committed:
        // the committed frame re-renders from the stroke record, so pixels
        // must equal a straight rasterization of that record.
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.BeginStroke(10, 10, 1);
        for (var i = 1; i <= 20; i++)
        {
            vm.MoveStrokeBatch([new MainViewModel.PointerSample(10 + i * 4, 10 + i * 2, 1)]);
        }
        vm.EndStroke();

        var frame = (PaintedFrame)vm.Doc.Scene.Layers[0].Cels[0].Frame!;
        using var direct = Lightbox.Raster.FrameRasterizer.Rasterize(
            frame.Strokes, vm.Doc.Scene.Width, vm.Doc.Scene.Height);
        using var materialized = Lightbox.Raster.FrameRasterizer.Materialize(
            frame, vm.Doc.Scene.Width, vm.Doc.Scene.Height);

        Assert.True(direct.GetPixel(50, 30).Alpha > 0 == materialized.GetPixel(50, 30).Alpha > 0);
        Assert.Equal(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(direct.GetPixelSpan())),
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(materialized.GetPixelSpan())));
    }

    [AvaloniaFact]
    public void PointerUpWithoutDown_IsHarmless()
    {
        var vm = new MainViewModel(null);
        vm.EndStroke(); // no crash, no mutation
        vm.MoveStrokeBatch([new MainViewModel.PointerSample(1, 1, 0.5)]);
        var frame = (PaintedFrame)vm.Doc.Scene.Layers[0].Cels[0].Frame!;
        Assert.Empty(frame.Strokes);
    }
}

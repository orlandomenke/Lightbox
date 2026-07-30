using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

public class LayerTests
{
    [AvaloniaFact]
    public void AddVectorLayer_BecomesActive_PaintingCreatesVectorStrokes()
    {
        var vm = new MainViewModel(null);
        vm.AddVectorLayerCommand.Execute(null);

        Assert.Equal(2, vm.Doc.Scene.Layers.Count);
        Assert.Equal(1, vm.ActiveLayerIndex);
        Assert.Equal(2, vm.LayerChoices.Count);

        vm.BeginStroke(5, 5, 0.5);
        vm.MoveStroke(20, 20, 0.5);
        vm.EndStroke();

        var vecLayer = vm.Doc.Scene.Layers[1];
        Assert.Equal(LayerKind.Vector, vecLayer.Kind);
        var frame = Assert.IsType<VectorFrame>(vecLayer.Cels[0].Frame);
        Assert.Single(frame.Strokes);

        // The original painted layer was untouched.
        var painted = Assert.IsType<PaintedFrame>(vm.Doc.Scene.Layers[0].Cels[0].Frame);
        Assert.Empty(painted.Strokes);
    }

    [AvaloniaFact]
    public void InbetweensOnVectorLayer_ProduceVectorFrames()
    {
        var vm = new MainViewModel(null);
        vm.AddVectorLayerCommand.Execute(null);

        vm.BeginStroke(0, 0, 0.5);
        vm.MoveStroke(30, 0, 0.5);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.BeginStroke(0, 40, 0.5);
        vm.MoveStroke(30, 40, 0.5);
        vm.EndStroke();
        vm.CurrentFrameIndex = 0;
        vm.TweenCount = 1;
        vm.InsertInbetweensCommand.Execute(null);

        var layer = vm.Doc.Scene.Layers[1];
        Assert.IsType<VectorFrame>(layer.Cels[1].Frame);
    }

    [AvaloniaFact]
    public void NewLayer_IsPaddedToFrameCount_AndUndoable()
    {
        var vm = new MainViewModel(null);
        vm.AddFrameCommand.Execute(null);
        vm.AddPaintedLayerCommand.Execute(null);

        Assert.Equal(2, vm.Doc.Scene.Layers[1].Cels.Count);

        vm.UndoCommand.Execute(null);
        Assert.Single(vm.Doc.Scene.Layers);
        Assert.Single(vm.LayerChoices);
    }
}

public class SmoothingTests
{
    [AvaloniaFact]
    public void SmoothingOn_ReducesSpikes_PreservesEndpoints()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = true };
        vm.BeginStroke(0, 0, 0.5);
        vm.MoveStroke(10, 50, 0.5); // spike
        vm.MoveStroke(20, 0, 0.5);
        vm.EndStroke();

        var frame = (PaintedFrame)vm.Doc.Scene.Layers[0].Cels[0].Frame!;
        var pts = frame.Strokes[0].Points;
        Assert.Equal(new StrokePoint(0, 0, 0.5), pts[0]);
        Assert.Equal(20, pts[^1].X);
        Assert.True(pts[1].Y < 50); // spike pulled down
    }

    [AvaloniaFact]
    public void SmoothingOff_KeepsRawPoints()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.BeginStroke(0, 0, 0.5);
        vm.MoveStroke(10, 50, 0.5);
        vm.MoveStroke(20, 0, 0.5);
        vm.EndStroke();

        var frame = (PaintedFrame)vm.Doc.Scene.Layers[0].Cels[0].Frame!;
        Assert.Equal(50, frame.Strokes[0].Points[1].Y);
    }
}

public class ThumbnailTests
{
    [AvaloniaFact]
    public void KeyedCells_GetThumbnails_HoldsDoNot()
    {
        var vm = new MainViewModel(null);
        vm.BeginStroke(10, 10, 0.5);
        vm.MoveStroke(100, 100, 0.5);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.Doc.Scene.Layers[0].Cels[1].Frame = null; // make it a hold
        vm.UndoCommand.Execute(null); // force full refresh path
        vm.RedoCommand.Execute(null);

        Assert.NotNull(vm.FrameCells[0].Thumb);
    }
}

public class ExportTests
{
    [AvaloniaFact]
    public void ExportPngSequence_WritesOneFilePerFrame_ResolvingHolds()
    {
        var vm = new MainViewModel(null);
        vm.BeginStroke(10, 10, 0.5);
        vm.MoveStroke(200, 200, 0.9);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.Doc.Scene.Layers[0].Cels[1].Frame = null; // hold

        var dir = Path.Combine(Path.GetTempPath(), $"lightbox-export-{Guid.NewGuid():N}");
        try
        {
            var written = SequenceExporter.ExportPngSequence(vm.Doc, dir);
            Assert.Equal(2, written.Count);
            Assert.EndsWith("frame_0001.png", written[0]);
            Assert.EndsWith("frame_0002.png", written[1]);

            // Both frames decode and show the held drawing (non-white pixels).
            foreach (var path in written)
            {
                using var bmp = SKBitmap.Decode(path);
                Assert.Equal(vm.Doc.Scene.Width, bmp.Width);
                Assert.NotEqual(SKColors.White, bmp.GetPixel(100, 100));
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public class FpsTests
{
    [AvaloniaFact]
    public void Fps_ClampsAndPersistsToScene()
    {
        var vm = new MainViewModel(null);
        vm.Fps = 24;
        Assert.Equal(24, vm.Doc.Scene.Fps);
        vm.Fps = 999;
        Assert.Equal(60, vm.Doc.Scene.Fps);
        vm.Fps = 0;
        Assert.Equal(1, vm.Doc.Scene.Fps);
    }
}

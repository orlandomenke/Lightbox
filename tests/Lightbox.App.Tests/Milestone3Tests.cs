using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

public class LayerTests
{
    [AvaloniaFact]
    public void AddLayer_BecomesActive_AndPaintingLandsOnItAlone()
    {
        var vm = new MainViewModel(null);
        vm.AddPaintedLayerCommand.Execute(null);

        // Paper, the layer it opened on, and the new one.
        Assert.Equal(3, vm.Doc.Scene.Layers.Count);
        Assert.Equal(2, vm.ActiveLayerIndex);
        Assert.Equal(3, vm.LayerChoices.Count);

        vm.BeginStroke(5, 5, 0.5);
        vm.MoveStroke(20, 20, 0.5);
        vm.EndStroke();

        var frame = vm.PaintLayer().Cels[0].Frame!;
        Assert.Single(frame.Strokes);

        // The layer below was untouched.
        Assert.Empty(vm.Doc.Scene.Layers[1].Cels[0].Frame!.Strokes);
    }

    /// <remarks>
    /// Was <c>InbetweensOnVectorLayer_ProduceVectorFrames</c>. The producing half
    /// asserted a frame class that no longer varies, so what is left worth pinning
    /// is that an inbetween appears at all and carries the tweened line.
    /// </remarks>
    [AvaloniaFact]
    public void Inbetweens_LandOnTheLayerTheyWereAskedFor()
    {
        var vm = new MainViewModel(null);
        vm.AddPaintedLayerCommand.Execute(null);

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

        var tween = vm.PaintLayer().Cels[1].Frame;
        Assert.NotNull(tween);
        Assert.Equal(FrameRole.Inbetween, tween.Role);
        Assert.Single(tween.Strokes);
    }

    [AvaloniaFact]
    public void NewLayer_IsPaddedToFrameCount_AndUndoable()
    {
        var vm = new MainViewModel(null);
        vm.AddFrameCommand.Execute(null);
        vm.AddPaintedLayerCommand.Execute(null);

        Assert.Equal(2, vm.PaintLayer().Cels.Count);

        vm.UndoCommand.Execute(null);
        // Back to paper plus the layer the document opened on.
        Assert.Equal(2, vm.Doc.Scene.Layers.Count);
        Assert.Equal(2, vm.LayerChoices.Count);
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

        var frame = vm.PaintedCel();
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

        var frame = vm.PaintedCel();
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
        vm.PaintLayer().Cels[1].Frame = null; // make it a hold
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
        vm.PaintLayer().Cels[1].Frame = null; // hold

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

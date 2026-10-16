using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// A transform over every drawing on a layer, then what the artist sees of it.
/// </summary>
[Collection("BrushState")]
public class WholeLayerTransformRendersTests : BrushStateIsolated
{
    private static MainViewModel ThreeDrawings()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 16;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        vm.BrushWetEdge = 0;
        vm.BrushGranulation = 0;
        vm.BrushScatter = 0;
        vm.AddFrameCommand.Execute(null);
        vm.AddFrameCommand.Execute(null);
        for (var i = 0; i < 3; i++)
        {
            vm.CurrentFrameIndex = i;
            vm.BeginStroke(200, 200 + i * 10, 1);
            vm.MoveStroke(300, 200 + i * 10, 1);
            vm.EndStroke();
            Dispatcher.UIThread.RunJobs();
        }
        vm.CurrentFrameIndex = 1;
        Dispatcher.UIThread.RunJobs();
        return vm;
    }

    private static byte AlphaAt(MainViewModel vm, int frame, int x, int y)
    {
        using var bmp = SKBitmap.Decode(Convert.FromBase64String(vm.RenderFramePng(frame)));
        return bmp.GetPixel(x, y).Alpha;
    }

    private static void MoveWholeLayer(MainViewModel vm, float dx, float dy)
    {
        vm.TransformScope = TransformScope.ActiveLayerAllFrames;
        Assert.True(vm.BeginTransform());
        vm.PreviewTransform(SKMatrix.CreateTranslation(dx, dy));
        Dispatcher.UIThread.RunJobs();
        vm.CommitTransformAffine(0, 0, 1, 1, 0, dx, dy);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void EveryDrawingRendersWhereTheTransformPutIt()
    {
        var vm = ThreeDrawings();
        MoveWholeLayer(vm, 0, 150);

        for (var i = 0; i < 3; i++)
        {
            var y = 350 + i * 10;
            var landed = AlphaAt(vm, i, 250, y);
            var left = AlphaAt(vm, i, 250, 200 + i * 10);
            Assert.True(landed > 128 && left < 16,
                $"frame {i}: alpha {landed} where the line went, {left} where it was");
        }
    }

    [AvaloniaFact]
    public void AFollowUpTransformBoxesTheMovedDrawing()
    {
        var vm = ThreeDrawings();
        MoveWholeLayer(vm, 0, 150);

        (double MinX, double MinY, double MaxX, double MaxY)? box = null;
        vm.TransformBegun += (a, b, c, d) => box = (a, b, c, d);
        vm.TransformScope = TransformScope.ActiveCel;
        Assert.True(vm.BeginTransform());

        Assert.NotNull(box);
        Assert.InRange(box!.Value.MinY, 340, 362);
        Assert.InRange(box.Value.MaxY, 358, 380);
    }

    [AvaloniaFact]
    public void TheRecordOfEveryDrawingMoved()
    {
        var vm = ThreeDrawings();
        MoveWholeLayer(vm, 0, 150);

        var layer = vm.Doc.Scene.Layers[vm.ActiveLayerIndex];
        for (var i = 0; i < 3; i++)
        {
            var frame = (Frame)layer.Cels[i].Frame!;
            Assert.Equal(350 + i * 10, frame.Strokes[^1].Points[0].Y, 3);
        }
    }

    private static SKBitmap? Published(MainViewModel vm)
    {
        SKBitmap? grabbed = null;
        void Capture(Rendering.RenderSnapshot s)
        {
            using var img = s.Materialise(null);
            var bmp = new SKBitmap(img.Width, img.Height);
            img.ReadPixels(bmp.Info, bmp.GetPixels(), bmp.RowBytes, 0, 0);
            grabbed = bmp;
        }
        vm.SnapshotChanged += Capture;
        try { vm.PublishSnapshot(); }
        finally { vm.SnapshotChanged -= Capture; }
        return grabbed;
    }

    /// <summary>The owner's case: a rough and a line layer, each moved whole, then played.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PlaybackShowsTheMovedDrawings(bool viewport, bool playedBefore)
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 16;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        if (viewport) vm.SetViewport(new SKRectI(0, 0, 960, 540));
        vm.OnionSkin = false;
        while (vm.Doc.Scene.FrameCount < 4) vm.AddFrameCommand.Execute(null);
        vm.ActiveLayerIndex = 1;
        var rough = vm.ActiveLayerIndex;
        vm.AddPaintedLayerCommand.Execute(null);
        var line = vm.ActiveLayerIndex;
        foreach (var layer in new[] { rough, line })
        {
            vm.ActiveLayerIndex = layer;
            for (var f = 0; f < 4; f++)
            {
                vm.CurrentFrameIndex = f;
                vm.BeginStroke(100, 100 + f * 5 + (layer == line ? 40 : 0), 1);
                vm.MoveStroke(200, 100 + f * 5 + (layer == line ? 40 : 0), 1);
                vm.EndStroke();
            }
        }
        vm.CurrentFrameIndex = 0;
        Dispatcher.UIThread.RunJobs();
        using (Published(vm)) { }
        if (playedBefore)
        {
            vm.TogglePlaybackCommand.Execute(null);
            for (var i = 0; i < 8; i++)
            {
                vm.HandlePlaybackTickForTests(1);
                using (Published(vm)) { }
                Dispatcher.UIThread.RunJobs();
            }
            vm.TogglePlaybackCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
        }
        foreach (var layer in new[] { rough, line })
        {
            vm.ActiveLayerIndex = layer;
            MoveWholeLayer(vm, 0, 200);
            using (Published(vm)) { }
        }

        using var paused = Published(vm)!;
        var paper = paused.GetPixel(150, 500);
        vm.TogglePlaybackCommand.Execute(null);
        for (var i = 0; i < 6; i++)
        {
            vm.HandlePlaybackTickForTests(1);
            using var playing = Published(vm) ?? throw new InvalidOperationException("nothing published");
            var at = vm.CurrentFrameIndex;
            var roughPx = playing.GetPixel(150, 300 + at * 5);
            var linePx = playing.GetPixel(150, 340 + at * 5);
            var oldPx = playing.GetPixel(150, 100 + at * 5);
            Assert.True(roughPx != paper && linePx != paper && oldPx == paper,
                $"frame {at}: rough {roughPx}, line {linePx}, where they were {oldPx}, paper {paper}");
        }
        vm.TogglePlaybackCommand.Execute(null);
    }

    /// <summary>
    /// Ctrl+T on a held cel opens in "this cel", then the scope is widened to
    /// the whole layer — the order an artist meets the scope control in.
    /// </summary>
    /// <remarks>
    /// B478: the restart kept the hold the narrow scope had recorded, so the
    /// commit keyed it and moved the copy, and frame 0's drawing stayed put.
    /// </remarks>
    [AvaloniaFact]
    public void WideningTheScopeFromAHoldMovesTheDrawingsThemselves()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 16;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        while (vm.Doc.Scene.FrameCount < 4) vm.AddFrameCommand.Execute(null);
        foreach (var f in new[] { 0, 2 })
        {
            vm.CurrentFrameIndex = f;
            vm.BeginStroke(200, 200 + f * 10, 1);
            vm.MoveStroke(300, 200 + f * 10, 1);
            vm.EndStroke();
        }
        var layer = vm.Doc.Scene.Layers[vm.ActiveLayerIndex];
        layer.Cels[1].Frame = null;
        layer.Cels[3].Frame = null;
        vm.CurrentFrameIndex = 1; // on the hold of drawing 0
        Dispatcher.UIThread.RunJobs();
        var cels = layer.Cels.Count(c => c.Frame is not null);

        vm.TransformScope = TransformScope.ActiveCel;
        Assert.True(vm.BeginTransform());
        vm.TransformScope = TransformScope.ActiveLayerAllFrames;
        vm.PreviewTransform(SKMatrix.CreateTranslation(0, 150));
        Dispatcher.UIThread.RunJobs();
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 0, 150);
        Dispatcher.UIThread.RunJobs();

        var keyedNow = layer.Cels.Count(c => c.Frame is not null);
        var report = string.Join("; ", Enumerable.Range(0, 4).Select(i => { var y = 350 + (i / 2) * 20; return $"f{i} new {AlphaAt(vm, i, 250, y)} old {AlphaAt(vm, i, 250, y - 150)}"; }));
        Assert.True(cels == keyedNow, $"keyed {keyedNow - cels} hold(s); {report}");
        for (var i = 0; i < 4; i++)
        {
            var y = 350 + (i / 2) * 20;
            var landed = AlphaAt(vm, i, 250, y);
            var left = AlphaAt(vm, i, 250, y - 150);
            Assert.True(landed > 128 && left < 16,
                $"frame {i}: alpha {landed} where the line went, {left} where it was");
        }
    }
}

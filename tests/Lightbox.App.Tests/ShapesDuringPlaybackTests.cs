using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// B419: a clip and a folder shape (Q215) carve the canvas during playback as
/// they do paused. Playback takes the tiled compositor once the canvas has
/// handed over a viewport; the tile gate kept a shaped layer from becoming a
/// tile pass, but it still arrived as a bitmap pass, and that compositor drew
/// it flat — the owner saw the shading leave the flats for as long as the
/// sequence played.
/// </summary>
[Collection("BrushState")]
public sealed class ShapesDuringPlaybackTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static void Bar(MainViewModel vm, double y)
    {
        vm.BeginStroke(20, y, 1);
        vm.MoveStroke(200, y, 1);
        vm.EndStroke();
    }

    private static SKBitmap Published(MainViewModel vm)
    {
        SKBitmap? grabbed = null;
        void Capture(RenderSnapshot s)
        {
            using var img = s.Materialise(null);
            var bmp = new SKBitmap(img.Width, img.Height);
            img.ReadPixels(bmp.Info, bmp.GetPixels(), bmp.RowBytes, 0, 0);
            grabbed = bmp;
        }
        vm.SnapshotChanged += Capture;
        try { vm.PublishSnapshot(); }
        finally { vm.SnapshotChanged -= Capture; }
        return grabbed!;
    }

    private static SKBitmap? PublishedOrNull(MainViewModel vm)
    {
        SKBitmap? grabbed = null;
        void Capture(RenderSnapshot s)
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

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheCarveHoldsForEveryPlayedFrame(bool clipInstead)
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        // The real canvas hands its viewport over, and with one playback takes the tiled route.
        vm.SetViewport(new SKRectI(0, 0, 960, 540));
        vm.OnionSkin = false;
        while (vm.Doc.Scene.FrameCount < 6) vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 0;
        var flat = vm.Doc.Scene.Layers[1];
        vm.ActiveLayerIndex = 1;
        Bar(vm, 100);
        vm.AddPaintedLayerCommand.Execute(null);
        var shadeIndex = vm.ActiveLayerIndex;
        Bar(vm, 100);
        Bar(vm, 300);
        var shade = vm.Doc.Scene.Layers[shadeIndex];
        if (clipInstead)
        {
            vm.SetLayerClipped(shade, true, alone: true);
        }
        else
        {
            var (folder, refusal) = vm.ExternalGroup([flat.Id, shade.Id], "Character", false, out _);
            Assert.True(folder is not null, refusal);
            vm.SetFolderShape(vm.Doc.Scene.Layers.Single(l => l.Id == flat.Id), keepInside: true);
        }

        // A drawing of its own on every frame, so playback composes each one.
        for (var f = 1; f < vm.Doc.Scene.FrameCount; f++)
        {
            vm.CurrentFrameIndex = f;
            vm.ActiveLayerIndex = vm.Doc.Scene.Layers.FindIndex(x => x.Id == flat.Id);
            Bar(vm, 100 + f);
            vm.ActiveLayerIndex = vm.Doc.Scene.Layers.FindIndex(x => x.Id == shade.Id);
            Bar(vm, 100 + f);
            Bar(vm, 300 + f);
        }
        vm.CurrentFrameIndex = 0;
        using var paused = Published(vm);
        var paper = paused.GetPixel(150, 450);
        output.WriteLine($"{(clipInstead ? "clip" : "folder shape")}: paused off-flat {paused.GetPixel(110, 300)} (paper {paper})");
        Assert.Equal(paper, paused.GetPixel(110, 300));

        vm.TogglePlaybackCommand.Execute(null);
        for (var f = 0; f < 4; f++)
        {
            vm.HandlePlaybackTickForTests(1);
            using var playing = PublishedOrNull(vm) ?? throw new InvalidOperationException($"nothing published at frame {vm.CurrentFrameIndex}");
            var at = vm.CurrentFrameIndex;
            output.WriteLine($"  playing frame {at}: off-flat {playing.GetPixel(110, 300 + at)}, on-flat {playing.GetPixel(110, 100 + at)}");
            Assert.Equal(paper, playing.GetPixel(110, 300 + at)); // the shading off the flat stays carved away
            Assert.NotEqual(paper, playing.GetPixel(110, 100 + at)); // and over the flat it still shows
        }
        vm.TogglePlaybackCommand.Execute(null);
    }
}

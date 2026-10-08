using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// B420: an onion ghost is carved the way its frame is — by the layer's clip
/// and its folder's shape (Q215) as well as its mask, each read at the ghost's
/// own frame. Reported as "I see the previous frame's all painted areas instead
/// of the alpha clipped one."
/// </summary>
[Collection("BrushState")]
public sealed class OnionGhostCarveTests(ITestOutputHelper output) : BrushStateIsolated
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

    /// <summary>A flat (bar at y 100+f) and a shade (bars at 100+f and 300+f) on each of three frames.</summary>
    private static (MainViewModel Vm, Layer Shade) Scene(bool clipInstead)
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        while (vm.Doc.Scene.FrameCount < 3) vm.AddFrameCommand.Execute(null);
        var flatId = vm.Doc.Scene.Layers[1].Id;
        vm.AddPaintedLayerCommand.Execute(null);
        var shadeId = vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Id;
        for (var f = 0; f < 3; f++)
        {
            vm.CurrentFrameIndex = f;
            vm.ActiveLayerIndex = vm.Doc.Scene.Layers.FindIndex(l => l.Id == flatId);
            Bar(vm, 100 + 20 * f);
            vm.ActiveLayerIndex = vm.Doc.Scene.Layers.FindIndex(l => l.Id == shadeId);
            Bar(vm, 100 + 20 * f);
            Bar(vm, 300 + 20 * f);
        }
        var shade = vm.Doc.Scene.Layers.Single(l => l.Id == shadeId);
        if (clipInstead)
        {
            vm.SetLayerClipped(shade, true, alone: true);
        }
        else
        {
            var (folder, refusal) = vm.ExternalGroup([flatId, shadeId], "Character", false, out _);
            Assert.True(folder is not null, refusal);
            vm.SetFolderShape(vm.Doc.Scene.Layers.Single(l => l.Id == flatId), keepInside: true);
        }
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.FindIndex(l => l.Id == shadeId);
        vm.OnionSkin = true;
        vm.CurrentFrameIndex = 1;
        return (vm, shade);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void AGhostIsCarvedTheWayItsFrameIs(bool clipInstead)
    {
        var (vm, shade) = Scene(clipInstead);
        Assert.True(shade.OnionEnabled, "the scene needs this layer's ghosts on");
        using var shown = Published(vm);
        var paper = shown.GetPixel(400, 500);

        // Frame 0's and frame 2's shading off their flats: carved away in
        // their own frames, so carved away as ghosts.
        var previousOff = shown.GetPixel(110, 300);
        var nextOff = shown.GetPixel(110, 340);
        // Over their flats the ghosts still show.
        var previousOn = shown.GetPixel(110, 100);
        output.WriteLine($"{(clipInstead ? "clip" : "folder shape")}: ghost off the flat {previousOff} / {nextOff}, on the flat {previousOn}, paper {paper}");
        Assert.Equal(paper, previousOff);
        Assert.Equal(paper, nextOff);
        Assert.NotEqual(paper, previousOn);
    }
}

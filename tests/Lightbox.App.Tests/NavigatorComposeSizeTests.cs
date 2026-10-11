using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The navigator's picture is composed at its own size, not the document's.
/// </summary>
/// <remarks>
/// <para>
/// It composed every visible layer onto a surface the size of the document
/// and then shrank the result to 256 pixels across. On the lab's 30-layer
/// 1080p document that was 159-188 ms on the UI thread after every flip to a
/// frame not seen before — the whole of the thumbnail cost that held up the
/// flip and the pen (2026-10-11), for a picture a stamp's size. Composed at
/// its size, each layer is one small draw.
/// </para>
/// <para>
/// Invariant 7 holds: the surface is scaled, never the geometry — the passes
/// are the drawings' finished bitmaps, drawn under a canvas scale.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class NavigatorComposeSizeTests : BrushStateIsolated
{
    private static MainViewModel Vm()
    {
        var vm = VmLayers.PaperVm(); // 960 x 540
        vm.Onion.Enabled = false;
        vm.SmoothStrokes = false;
        vm.BrushSize = 40;
        vm.Workspace.NavigatorVisible = true;
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(300, 100, 1);
        vm.EndStroke();
        vm.PublishSnapshot();
        return vm;
    }

    [AvaloniaFact]
    public void ThePictureIsComposedAtItsOwnSize()
    {
        var vm = Vm();

        vm.RefreshThumbnails();

        Assert.True(vm.LastNavigatorCompose is { Width: > 0 } size && Math.Max(size.Width, size.Height) <= 256,
            $"the navigator composed {vm.LastNavigatorCompose} for a 256-pixel picture");
    }

    /// <summary>
    /// Composing small gives the picture shrinking a full composite gave: the
    /// same drawing, to within the filtering of one pixel in a stamp.
    /// </summary>
    [AvaloniaFact]
    public void ComposingSmallMatchesShrinkingAFullComposite()
    {
        var vm = Vm();
        var stroke = vm.FrameCache.Get(vm.PaintedCel(), 960, 540);
        var passes = new[] { new Lightbox.Raster.RenderPass(stroke, null, 1.0) };
        const double scale = 256.0 / 960;

        using var full = Lightbox.Raster.SceneRenderer.Compose(960, 540, passes, SKColors.White);
        using var fullBitmap = SKBitmap.FromImage(full);
        using var shrunk = new SKBitmap(256, 144);
        using (var canvas = new SKCanvas(shrunk))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(fullBitmap, new SKRect(0, 0, 256, 144), new SKPaint { IsAntialias = true });
        }
        using var small = Lightbox.Raster.SceneRenderer.Compose(256, 144, passes, SKColors.White, scale: scale);
        using var smallBitmap = SKBitmap.FromImage(small);

        var worst = 0;
        var inked = 0;
        for (var y = 0; y < 144; y++)
        {
            for (var x = 0; x < 256; x++)
            {
                SKColor a = shrunk.GetPixel(x, y), b = smallBitmap.GetPixel(x, y);
                worst = Math.Max(worst, Math.Abs(a.Red - b.Red));
                if (b.Red < 128) inked++;
            }
        }
        Assert.True(inked > 100, $"only {inked} pixels of the stroke in the small composite");
        Assert.True(worst <= 64, $"the small composite differs from the shrunk one by {worst} in a channel");
    }

}

using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The transform preview repaints only where the moving pixels were and where
/// they now are — invariant 6 applied to the drag. It used to invalidate the
/// whole canvas per pointer event, a full recomposite per event: paced to the
/// present rate, that read as a slideshow on any document where a recomposite
/// is slow, which the artist reported as "the move only shows after I let go"
/// — exactly where a brush stroke stays live because its repaint is bounded.
/// </summary>
[Collection("BrushState")]
public class TransformPreviewBoundsTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>A black bar across the upper-left, on a transparent document.</summary>
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

    private static SKBitmap Published(MainViewModel vm)
    {
        RenderSnapshot? latest = null;
        void Capture(RenderSnapshot s) => latest = s;
        vm.SnapshotChanged += Capture;
        vm.PublishSnapshot();
        vm.SnapshotChanged -= Capture;
        return SKBitmap.FromImage(latest!.Image);
    }

    [AvaloniaFact]
    public void AMovePreviewRepaintsARegion_NotTheCanvas()
    {
        var vm = Painted();
        _ = Published(vm); // settle: the committed bar is on screen

        Assert.True(vm.BeginMove(150, 60, wholeLayer: false));
        vm.UpdateMove(150, 200, axisLock: false); // drag down by 140
        using var bmp = Published(vm);

        var clip = vm.LastPublishClip;
        output.WriteLine($"preview publish clip: {clip}");
        Assert.NotNull(clip); // null would mean the whole canvas recomposited
        var doc = vm.Doc.Scene;
        Assert.True(clip!.Value.Width < doc.Width || clip.Value.Height < doc.Height,
            $"the preview clip {clip} is not smaller than the {doc.Width}×{doc.Height} canvas");

        // Bounded must not mean wrong: the drawing has left and arrived.
        Assert.True(bmp.GetPixel(150, 200).Alpha > 0, "the drawing is not shown at the dragged-to position");
        Assert.Equal(0, bmp.GetPixel(150, 60).Alpha);
    }

    [AvaloniaFact]
    public void TheRegionCoversWhereTheDrawingWas_AcrossSuccessiveEvents()
    {
        var vm = Painted();
        _ = Published(vm);

        Assert.True(vm.BeginMove(150, 60, wholeLayer: false));
        vm.UpdateMove(150, 130, axisLock: false);
        using (Published(vm)) { }
        // The second step must clean up after the first preview position, not
        // only after the committed one — its region is old-preview ∪ new.
        vm.UpdateMove(150, 200, axisLock: false);
        using var bmp = Published(vm);

        Assert.True(bmp.GetPixel(150, 200).Alpha > 0);
        Assert.Equal(0, bmp.GetPixel(150, 130).Alpha);
        Assert.Equal(0, bmp.GetPixel(150, 60).Alpha);
    }

    [AvaloniaFact]
    public void ABaselineRidesTheLayer_SoThePreviewFallsBackToTheWholeCanvas()
    {
        var vm = Painted();
        // A raster baseline moves with the layer and is not bounded by any
        // stroke, so the honest region is everything.
        vm.PaintedCel().PngBase64 = Convert.ToBase64String(
            SKImage.FromBitmap(new SKBitmap(new SKImageInfo(4, 4)))
                .Encode(SKEncodedImageFormat.Png, 100).ToArray());
        _ = Published(vm);

        Assert.True(vm.BeginMove(150, 60, wholeLayer: false));
        vm.UpdateMove(150, 200, axisLock: false);
        using (Published(vm)) { }

        Assert.Null(vm.LastPublishClip);
    }

    [AvaloniaFact]
    public void WithASelection_TheBaselineStaysPut_AndTheRegionIsStillBounded()
    {
        var vm = Painted();
        vm.PaintedCel().PngBase64 = Convert.ToBase64String(
            SKImage.FromBitmap(new SKBitmap(new SKImageInfo(4, 4)))
                .Encode(SKEncodedImageFormat.Png, 100).ToArray());
        vm.ApplySelectionShape(
            [new(20, 20, 1), new(280, 20, 1), new(280, 120, 1), new(20, 120, 1)],
            add: false, subtract: false);
        _ = Published(vm);

        Assert.True(vm.BeginTransform());
        vm.PreviewTransform(SKMatrix.CreateTranslation(0, 140));
        using (Published(vm)) { }

        // Only the selected strokes move; the baseline is in the static half,
        // so the stroke record still bounds the change.
        Assert.NotNull(vm.LastPublishClip);
    }
}

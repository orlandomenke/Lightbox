using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The mask edit mode: strokes land on the active layer's mask, undo takes
/// them back off it, the mode's own switches are undoable steps, and the
/// live preview shows the carve the commit will make.
/// </summary>
[Collection("BrushState")]
public sealed class MaskEditingTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel Vm()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        return vm;
    }

    private static Layer Paint(MainViewModel vm) => vm.Doc.Scene.Layers[1];

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
        try
        {
            vm.PublishSnapshot();
        }
        finally
        {
            vm.SnapshotChanged -= Capture;
        }
        return grabbed ?? throw new InvalidOperationException("nothing was published");
    }

    [AvaloniaFact]
    public void AddingAMaskIsUndoableAndStartsTheEditMode()
    {
        var vm = Vm();
        var layer = Paint(vm);

        vm.AddLayerMask(layer, paintHides: false);
        Assert.NotNull(layer.Mask);
        Assert.Null(layer.Mask!.Inverted);
        Assert.True(vm.EditingLayerMask);

        // Undo restores a cloned tree, so the layer is re-fetched — holding
        // the old reference would be asserting on an orphan.
        vm.UndoCommand.Execute(null);
        Assert.Null(Paint(vm).Mask);
        // The mode ends with the mask: it is derived from the mask's
        // existence, not tracked beside it.
        Assert.False(vm.EditingLayerMask);
    }

    [AvaloniaFact]
    public void PaintHidesStartsInverted()
    {
        var vm = Vm();
        vm.AddLayerMask(Paint(vm), paintHides: true);
        Assert.True(Paint(vm).Mask!.IsInverted);
    }

    [AvaloniaFact]
    public void AStrokeLandsOnTheMaskAndUndoTakesItBackOffIt()
    {
        var vm = Vm();
        var layer = Paint(vm);
        Bar(vm, 100); // ordinary content, before the mask exists

        vm.AddLayerMask(layer, paintHides: false);
        Bar(vm, 100); // now targeting the mask

        var cel = (Frame)layer.Cels[0].Frame!;
        Assert.Single(cel.Strokes);
        var maskStroke = Assert.Single(layer.Mask!.Frame.Strokes);
        // The layer's alpha lock guards content, not coverage.
        Assert.False(maskStroke.AlphaLocked);

        vm.UndoCommand.Execute(null);
        Assert.Empty(layer.Mask.Frame.Strokes);
        Assert.Single(cel.Strokes);
    }

    [AvaloniaFact]
    public void ClippingIsUndoableAndAbsentWhenReleased()
    {
        var vm = Vm();
        var layer = Paint(vm);

        vm.SetLayerClipped(layer, true, alone: true);
        Assert.True(layer.IsClipped);
        vm.UndoCommand.Execute(null);
        Assert.False(Paint(vm).IsClipped);
        Assert.Null(Paint(vm).ClipToBelow); // released is absent, not false

        vm.SetLayerClipped(Paint(vm), true, alone: true);
        vm.SetLayerClipped(Paint(vm), false, alone: true);
        Assert.Null(Paint(vm).ClipToBelow);
    }

    [AvaloniaFact]
    public void TheMaskCarvesThePublishedComposite()
    {
        var vm = Vm();
        var layer = Paint(vm);
        Bar(vm, 100); // a bar right across the canvas

        using var before = Published(vm);
        Assert.True(before.GetPixel(150, 100).Alpha > 0);
        var unmasked = before.GetPixel(150, 100);

        // Paint-shows: the fresh mask has no coverage, so the bar vanishes.
        vm.AddLayerMask(layer, paintHides: false);
        using var hidden = Published(vm);
        var paper = Published(vm).GetPixel(150, 100);
        output.WriteLine($"unmasked {unmasked}, masked-empty {paper}");
        Assert.NotEqual(unmasked, paper);

        // Reveal the left span only: the bar returns there and nowhere else.
        vm.BeginStroke(20, 100, 1);
        vm.MoveStroke(90, 100, 1);
        vm.EndStroke();
        using var revealed = Published(vm);
        Assert.Equal(unmasked, revealed.GetPixel(50, 100));
        Assert.Equal(paper, revealed.GetPixel(150, 100));
    }

    [AvaloniaFact]
    public void TheLivePreviewShowsTheCarveBeforeThePenLifts()
    {
        var vm = Vm();
        var layer = Paint(vm);
        Bar(vm, 100);
        vm.AddLayerMask(layer, paintHides: false); // everything hidden

        // Mid-stroke on the mask: the reveal must already be visible — an
        // artist cannot judge a mark they are not being shown, and a mask
        // mark IS what it reveals.
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        vm.BeginStroke(20, 100, 1);
        vm.MoveStroke(90, 100, 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // flush the coalesced publish

        Assert.NotNull(latest);
        using var during = SKBitmap.FromImage(latest!.Image);
        var mid = during.GetPixel(50, 100);
        var still = during.GetPixel(150, 100);
        output.WriteLine($"mid-stroke revealed {mid}, still hidden {still}");
        Assert.NotEqual(still, mid);
        vm.EndStroke();

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var after = SKBitmap.FromImage(latest!.Image);
        Assert.Equal(mid, after.GetPixel(50, 100));
    }
}

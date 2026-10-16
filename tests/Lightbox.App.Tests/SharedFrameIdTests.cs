using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Serialization;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Two open documents that share frame ids each show their own drawing (B483).
/// </summary>
/// <remarks>
/// <para>
/// A document made from a template, a duplicated project document and the
/// same file opened twice all carry the original's frame ids —
/// <c>DocJson.Clone</c> preserves them. The frame cache keyed a drawing's
/// pixels by that id alone, and since B362 it keeps every document's pixels
/// across a tab switch, so the two tabs shared one bitmap: a stroke drawn on
/// the copy appeared on the original, whose record does not contain it.
/// </para>
/// <para>
/// Read off the published picture rather than the cache, because the
/// complaint is what the artist sees. Each test also checks the stroke is
/// visible where it was drawn, so a build that drew nothing cannot pass.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class SharedFrameIdTests : BrushStateIsolated
{
    private static readonly (int X, int Y) OnTheStroke = (200, 100);
    private static readonly (int X, int Y) Untouched = (700, 450);

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

    private static void Stroke(MainViewModel vm, float y = 100)
    {
        vm.BeginStroke(100, y, 1);
        vm.MoveStroke(300, y, 1);
        vm.EndStroke();
    }

    /// <summary>Whether the published picture shows ink at the stroke, judged against untouched paper.</summary>
    private static bool ShowsTheStroke(MainViewModel vm)
    {
        using var shot = Published(vm);
        return shot.GetPixel(OnTheStroke.X, OnTheStroke.Y) != shot.GetPixel(Untouched.X, Untouched.Y);
    }

    /// <summary>A second tab holding a copy of the first, ids and all, made active.</summary>
    private static (DocumentTab Original, DocumentTab Copy) OpenACopy(MainViewModel vm)
    {
        var original = vm.ActiveTab!;
        vm.OpenDocumentTab(DocJson.Clone(vm.Doc), null);
        var copy = vm.ActiveTab!;
        Assert.NotSame(original, copy);
        return (original, copy);
    }

    [AvaloniaFact]
    public void AStrokeOnACopyDoesNotShowOnTheOriginal()
    {
        var vm = VmLayers.BareVm();
        Published(vm).Dispose();
        var (original, _) = OpenACopy(vm);

        Stroke(vm);
        Assert.True(ShowsTheStroke(vm), "the copy should show the stroke drawn on it");

        vm.ActiveTab = original;
        Assert.Empty(vm.PaintStrokes());
        Assert.False(ShowsTheStroke(vm), "the original shows a stroke that was drawn on its copy");
    }

    [AvaloniaFact]
    public void AStrokeOnTheOriginalDoesNotShowOnTheCopy()
    {
        var vm = VmLayers.BareVm();
        Published(vm).Dispose();
        var (original, copy) = OpenACopy(vm);
        Published(vm).Dispose();

        vm.ActiveTab = original;
        Stroke(vm);
        Assert.True(ShowsTheStroke(vm), "the original should show the stroke drawn on it");

        vm.ActiveTab = copy;
        Assert.Empty(vm.PaintStrokes());
        Assert.False(ShowsTheStroke(vm), "the copy shows a stroke that was drawn on the original");
    }

    [AvaloniaFact]
    public void UndoingOnACopyLeavesTheOriginalsDrawing()
    {
        // The undo path patches every cached rendering of the drawing in place
        // (B327) rather than fetching one, so it is a second way into another
        // document's pixels: it rebuilt the region from the copy's record on the
        // original's bitmap too. The undo comes after a trip to the other tab
        // and back, because a switch forgets the pixels saved for each mark
        // (Q167) and only then does an undo replay the region.
        var vm = VmLayers.BareVm();
        Published(vm).Dispose();
        var (original, copy) = OpenACopy(vm);
        Published(vm).Dispose();

        vm.ActiveTab = original;
        Stroke(vm);
        Assert.True(ShowsTheStroke(vm), "the original should show its own stroke");

        vm.ActiveTab = copy;
        Stroke(vm);
        vm.ActiveTab = original;
        vm.ActiveTab = copy;
        var repaints = vm.FrameRegionRepaints;
        vm.UndoCommand.Execute(null);
        Assert.True(vm.FrameRegionRepaints > repaints, "the undo should have replayed the region, which is the path under test");
        Assert.Empty(vm.PaintStrokes());
        Assert.False(ShowsTheStroke(vm), "undo on the copy should take its stroke away there");

        vm.ActiveTab = original;
        Assert.Single(vm.PaintStrokes());
        Assert.True(ShowsTheStroke(vm), "undoing on the copy took the original's stroke off its canvas");
    }

    [AvaloniaFact]
    public void ClosingATabLetsGoOfItsPictures()
    {
        // Keyed per document, a closed document's pictures can never be found
        // again; held anyway, they push out pictures of documents still open.
        var vm = VmLayers.BareVm();
        Stroke(vm);
        Published(vm).Dispose();
        var (original, _) = OpenACopy(vm);
        Published(vm).Dispose();
        var both = vm.FrameCacheTraffic.Bytes;

        vm.CloseTab(original);
        Published(vm).Dispose();

        Assert.True(vm.FrameCacheTraffic.Bytes < both,
            $"closing a document kept {vm.FrameCacheTraffic.Bytes} of {both} bytes of its pictures");
    }
}

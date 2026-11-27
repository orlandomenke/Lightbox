using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// The navigator never hands back a picture of strokes that are gone (B503).
/// </summary>
/// <remarks>
/// <para>
/// <b>The navigator's key named each drawing by its id and its stroke count</b>,
/// and the remarks on it promised that an edit the count missed would heal on
/// the next one. That holds only while the count keeps growing. A clear, an
/// undo or an erasure takes it back to a number it has had before, and the
/// next stroke lands on the key of a picture composed back then: the navigator
/// showed strokes that no longer existed, in their old places, and kept doing
/// so for every count it had passed through once.
/// </para>
/// <para>
/// <b>Identity, not pixels.</b> The headless suite draws no pixels into an
/// Avalonia bitmap, so these ask the question the bug is about directly:
/// whether the bitmap handed back after the edit is the one composed before it.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class NavigatorFollowsEditsTests : BrushStateIsolated
{
    private static MainViewModel Vm()
    {
        var vm = VmLayers.PaperVm();
        vm.Workspace.NavigatorVisible = true;
        vm.SmoothStrokes = false;
        vm.BrushSize = 8;
        return vm;
    }

    private static Bitmap Stroke(MainViewModel vm, double x, double y)
    {
        vm.BeginStroke(x, y, 1);
        vm.MoveStroke(x + 40, y + 20, 1);
        vm.EndStroke();
        vm.RefreshThumbnails();
        return vm.NavigatorThumb!;
    }

    [AvaloniaFact]
    public void AfterAClearTheNextStrokeIsNotShownAsTheOneThatWasCleared()
    {
        var vm = Vm();
        var first = Stroke(vm, 20, 20);

        vm.ClearLayerContent(vm.Doc.Scene.Layers[vm.ActiveLayerIndex]);
        vm.RefreshThumbnails();
        var another = Stroke(vm, 200, 150);

        Assert.NotSame(first, another);
    }

    [AvaloniaFact]
    public void AfterAnUndoTheNextStrokeIsNotShownAsTheOneThatWasUndone()
    {
        var vm = Vm();
        var first = Stroke(vm, 20, 20);

        vm.UndoCommand.Execute(null);
        vm.RefreshThumbnails();
        var another = Stroke(vm, 200, 150);

        Assert.NotSame(first, another);
    }

    /// <summary>
    /// What the fix leans on: an invalidated drawing takes every picture
    /// composed from it with it, and leaves pictures of other drawings alone.
    /// </summary>
    [AvaloniaFact]
    public void InvalidatingADrawingDropsThePicturesComposedFromIt()
    {
        var cache = new ThumbnailCache();
        var renders = 0;
        Bitmap Make() { renders++; return new WriteableBitmap(new Avalonia.PixelSize(2, 2), new Avalonia.Vector(96, 96)); }

        cache.Get("nav:a+b", Make, ["a", "b"]);
        cache.Get("nav:c", Make, ["c"]);
        Assert.Equal(2, renders);

        cache.Invalidate("b");
        cache.Get("nav:a+b", Make, ["a", "b"]);
        cache.Get("nav:c", Make, ["c"]);

        Assert.Equal(3, renders);
    }
}

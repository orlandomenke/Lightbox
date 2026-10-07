using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Undoing a transform re-renders the drawings it moved, and only those.
/// </summary>
/// <remarks>
/// The performance lab's first unattended run (Q209) measured undo of a
/// transform at 10.5 s on the owner-shaped document. The commit was recorded as
/// a whole-document step, so its undo fell to ApplyEditScope's last branch —
/// ClearFrameRenders and every thumbnail dirty — and all 64 drawings were
/// replayed from their strokes to put back the handful one layer had moved.
/// </remarks>
[Collection("BrushState")]
public sealed class TransformUndoScopeTests : BrushStateIsolated
{
    private static MainViewModel TwoLayers(out string still, out string moved)
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("probe", 400, 300, 12, 72, "#ffffff", true));
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 10;
        Stroke(vm, 40, 60, 160, 120);
        still = vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame!.Id;

        vm.AddPaintedLayerCommand.Execute(null);
        Stroke(vm, 200, 150, 320, 220);
        moved = vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame!.Id;
        vm.PublishSnapshot();
        return vm;
    }

    private static void Stroke(MainViewModel vm, double x0, double y0, double x1, double y1)
    {
        vm.BeginStroke(x0, y0, 1);
        vm.MoveStroke((x0 + x1) / 2, (y0 + y1) / 2, 1);
        vm.MoveStroke(x1, y1, 1);
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

    /// <summary>
    /// The drawing on the other layer is not re-rendered by the undo: no cache
    /// miss names it. On the old path every drawing missed.
    /// </summary>
    [AvaloniaFact]
    public void UndoingATransformLeavesOtherDrawingsRendersAlone()
    {
        var vm = TwoLayers(out var still, out var moved);
        Assert.True(vm.BeginTransform());
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 40, 0);
        vm.PublishSnapshot();
        Assert.True(vm.IsFrameCached(still), "precondition: the untouched drawing is cached before the undo");

        var missesBefore = vm.FrameCache.Misses;
        vm.UndoCommand.Execute(null);

        var missedIds = vm.FrameCache.RecentMisses.TakeLast((int)Math.Min(8, vm.FrameCache.Misses - missesBefore))
            .Select(m => m.FrameId).ToList();
        Assert.DoesNotContain(still, missedIds);
        Assert.Contains(moved, missedIds);
    }

    /// <summary>
    /// Exact as well as fast: after the undo the canvas is pixel-for-pixel what
    /// it was before the transform, so no stale render survived the narrower
    /// invalidation.
    /// </summary>
    [AvaloniaFact]
    public void TheCanvasAfterUndoIsExactlyTheCanvasBeforeTheTransform()
    {
        var vm = TwoLayers(out _, out _);
        using var before = Published(vm);

        Assert.True(vm.BeginTransform());
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 40, 0);
        using var moved = Published(vm);
        Assert.NotEqual(Fingerprint(before), Fingerprint(moved));

        vm.UndoCommand.Execute(null);
        using var after = Published(vm);
        Assert.Equal(Fingerprint(before), Fingerprint(after));

        vm.RedoCommand.Execute(null);
        using var redone = Published(vm);
        Assert.Equal(Fingerprint(moved), Fingerprint(redone));
    }

    private static string Fingerprint(SKBitmap bmp) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bmp.GetPixelSpan()));
}

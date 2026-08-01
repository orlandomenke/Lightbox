using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// What the artist sees mid-stroke must be what lands on pen-up.
///
/// It was not. Alpha lock and the selection clip were applied only when the
/// stroke committed, so a locked layer showed the stroke running across the
/// whole canvas and then snapped to the masked shape when the pen lifted.
/// You cannot judge a mark you are not being shown — every other painting
/// tool renders the mask live, and so must this.
///
/// These drive the real pipeline (BeginStroke → MoveStroke → published
/// snapshot) and read the pixels the canvas would display.
/// </summary>
[Collection("BrushState")]
public class LiveMaskPixelTests : BrushStateIsolated
{
    private const int W = 300, H = 200;

    /// <summary>A layer with an opaque blue block in the middle and nothing else.</summary>
    private static MainViewModel VmWithBlock()
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("Mask", W, H, 12, 72, "#ffffff", false));
        vm.SmoothStrokes = false;
        vm.ColorHex = "#0000ff";
        vm.BrushSize = 20;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        vm.BrushScatter = 0;
        vm.BrushWetEdge = 0;
        vm.BrushGranulation = 0;

        // A block from x=100 to x=200, painted normally.
        vm.BeginStroke(110, 100, 1);
        for (var x = 110; x <= 190; x += 8) vm.MoveStroke(x, 100, 1);
        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
        return vm;
    }

    /// <summary>The layer strokes land on — not the locked paper beneath it.</summary>
    private static Layer PaintLayer(MainViewModel vm) =>
        vm.Doc.Scene.Layers.First(l => !l.IsBackground);

    private static SKBitmap Pixels(RenderSnapshot s)
    {
        var bmp = SKBitmap.FromImage(s.Image);
        Assert.NotNull(bmp);
        return bmp!;
    }

    /// <summary>Paint a red bar left-to-right, returning the mid-stroke frame and the committed one.</summary>
    private static (SKBitmap Live, SKBitmap Committed) BarAcross(MainViewModel vm)
    {
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        vm.ColorHex = "#ff0000";

        vm.BeginStroke(20, 100, 1);
        for (var x = 20; x <= 280; x += 10) vm.MoveStroke(x, 100, 1);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(latest);
        var live = Pixels(latest!);

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(latest);
        return (live, Pixels(latest!));
    }

    [AvaloniaFact]
    public void AlphaLocked_TheLiveStrokeIsAlreadyMasked()
    {
        var vm = VmWithBlock();
        PaintLayer(vm).AlphaLocked = true;

        var (live, committed) = BarAcross(vm);
        using (live)
        using (committed)
        {
            // Inside the block: red, both live and committed.
            Assert.Equal(SKColors.Red, live.GetPixel(150, 100));
            Assert.Equal(SKColors.Red, committed.GetPixel(150, 100));

            // Outside it, on the same stroke line: paper, both. This is the
            // bug — live used to show red right across the canvas.
            Assert.Equal(SKColors.White, live.GetPixel(40, 100));
            Assert.Equal(SKColors.White, committed.GetPixel(40, 100));
            Assert.Equal(SKColors.White, live.GetPixel(260, 100));
            Assert.Equal(SKColors.White, committed.GetPixel(260, 100));
        }
    }

    [AvaloniaFact]
    public void AlphaLocked_LiveAndCommittedAgreeEverywhere()
    {
        // The stronger statement: not "the mask is applied" but "the frame
        // you were shown is the frame you got".
        var vm = VmWithBlock();
        PaintLayer(vm).AlphaLocked = true;

        var (live, committed) = BarAcross(vm);
        using (live)
        using (committed)
        {
            Assert.True(Agree(live, committed, out var diff, out var where),
                $"live and committed differ at {diff} px, worst {where}");
        }
    }

    [AvaloniaFact]
    public void NotAlphaLocked_TheStrokeStillCoversTheWholeCanvas()
    {
        // The mask must not leak into the ordinary case.
        var vm = VmWithBlock();
        var (live, committed) = BarAcross(vm);
        using (live)
        using (committed)
        {
            Assert.Equal(SKColors.Red, live.GetPixel(40, 100));
            Assert.Equal(SKColors.Red, committed.GetPixel(40, 100));
        }
    }

    [AvaloniaFact]
    public void Selected_TheLiveStrokeIsAlreadyClipped()
    {
        var vm = VmWithBlock();
        // A hard rectangular selection over the middle third.
        vm.ApplySelectionShape(
        [
            new StrokePoint(100, 60, 1), new StrokePoint(200, 60, 1),
            new StrokePoint(200, 140, 1), new StrokePoint(100, 140, 1),
        ], false, false);
        Dispatcher.UIThread.RunJobs();

        var (live, committed) = BarAcross(vm);
        using (live)
        using (committed)
        {
            Assert.Equal(SKColors.Red, live.GetPixel(150, 100));
            Assert.Equal(SKColors.Red, committed.GetPixel(150, 100));
            // Outside the selection the stroke must be absent live too.
            Assert.NotEqual(SKColors.Red, live.GetPixel(40, 100));
            Assert.NotEqual(SKColors.Red, committed.GetPixel(40, 100));
            Assert.True(Agree(live, committed, out var diff, out var where),
                $"live and committed differ at {diff} px, worst {where}");
        }
    }

    [AvaloniaFact]
    public void AlphaLockedAndSelected_BothMasksApplyLive()
    {
        var vm = VmWithBlock();
        PaintLayer(vm).AlphaLocked = true;
        // A selection covering only the LEFT half of the existing block.
        vm.ApplySelectionShape(
        [
            new StrokePoint(60, 60, 1), new StrokePoint(150, 60, 1),
            new StrokePoint(150, 140, 1), new StrokePoint(60, 140, 1),
        ], false, false);
        Dispatcher.UIThread.RunJobs();

        var (live, committed) = BarAcross(vm);
        using (live)
        using (committed)
        {
            // Where both allow it: red.
            Assert.Equal(SKColors.Red, live.GetPixel(130, 100));
            // Inside the block but outside the selection: untouched blue.
            Assert.NotEqual(SKColors.Red, live.GetPixel(180, 100));
            // Inside the selection but outside the block: paper.
            Assert.Equal(SKColors.White, live.GetPixel(70, 100));
            Assert.True(Agree(live, committed, out var diff, out var where),
                $"live and committed differ at {diff} px, worst {where}");
        }
    }

    /// <summary>
    /// Pixels may differ by a little at antialiased edges — the commit
    /// rasterises the whole stroke at once while the preview accumulates
    /// segments — but nothing may differ by a lot, and only a handful at all.
    /// </summary>
    private static bool Agree(SKBitmap a, SKBitmap b, out int differing, out string worst)
    {
        using var pa = a.PeekPixels();
        using var pb = b.PeekPixels();
        var sa = pa.GetPixelSpan();
        var sb = pb.GetPixelSpan();
        differing = 0;
        var worstDelta = 0;
        worst = "none";
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                var ia = y * pa.RowBytes + x * 4;
                var ib = y * pb.RowBytes + x * 4;
                var d = 0;
                for (var c = 0; c < 4; c++) d = Math.Max(d, Math.Abs(sa[ia + c] - sb[ib + c]));
                if (d <= 8) continue;
                differing++;
                if (d > worstDelta)
                {
                    worstDelta = d;
                    worst = $"({x},{y}) delta {d}";
                }
            }
        }
        // 0.5% of the canvas, which covers stroke-edge antialiasing and
        // nothing structural: a mask that failed to apply is tens of
        // thousands of pixels at full delta.
        return differing < a.Width * a.Height / 200;
    }
}

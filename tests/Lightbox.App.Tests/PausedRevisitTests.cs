using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Flipping back to a frame already seen shows its finished picture again
/// rather than blending every layer once more — and never a stale one.
/// </summary>
/// <remarks>
/// <para>
/// A flip back on the lab's 30-layer document cost ~320 ms with every drawing
/// already in memory (2026-10-11): the paused canvas re-composed all thirty
/// layers over the whole page on the UI thread, every time. The composite cache
/// that makes this free during playback was gated to playback.
/// </para>
/// <para>
/// <b>The key is the composite's own input</b> — the pass list the compositor
/// reads, the scale, the paper and the edit counter — so a change that alters
/// the picture alters the key by construction rather than by a list someone has
/// to remember to extend. Those are the tests below the first: a stroke, an
/// opacity and a hidden layer, each changed away from the frame and seen on the
/// way back.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class PausedRevisitTests : BrushStateIsolated
{
    private static MainViewModel Vm()
    {
        var vm = VmLayers.PaperVm();
        vm.Onion.Enabled = false;
        vm.SmoothStrokes = false;
        vm.BrushSize = 14;
        // The whole page in view: the ring route, where a revisit used to recompose.
        vm.SetViewport(SKRectI.Create(0, 0, 960, 540));
        for (var i = 0; i < 4; i++)
        {
            if (i > 0) vm.AddFrameCommand.Execute(null);
            vm.BeginStroke(80 + i * 60, 60 + i * 30, 1);
            vm.MoveStroke(500 - i * 40, 400 - i * 20, 1);
            vm.EndStroke();
        }
        return vm;
    }

    private static RenderSnapshot Show(MainViewModel vm, int frame)
    {
        RenderSnapshot? latest = null;
        void Take(RenderSnapshot s) => latest = s;
        vm.SnapshotChanged += Take;
        try
        {
            vm.CurrentFrameIndex = frame;
            vm.PublishSnapshot();
        }
        finally
        {
            vm.SnapshotChanged -= Take;
        }
        Assert.NotNull(latest);
        Assert.NotNull(latest!.Image);
        return latest;
    }

    private static byte[] Pixels(RenderSnapshot snapshot)
    {
        using var bmp = SKBitmap.FromImage(snapshot.Image);
        return bmp.Bytes;
    }

    private static SKColor PixelAt(RenderSnapshot snapshot, int x, int y)
    {
        using var bmp = SKBitmap.FromImage(snapshot.Image);
        return bmp.GetPixel(x, y);
    }

    [AvaloniaFact]
    public void AFlipBackShowsTheFramesPictureWithoutComposingItAgain()
    {
        var vm = Vm();
        var first = Pixels(Show(vm, 0));
        Show(vm, 1);
        var hits = vm.RevisitHits;

        var back = Pixels(Show(vm, 0));

        Assert.True(vm.RevisitHits > hits, "the flip back composed the frame again");
        Assert.True(first.AsSpan().SequenceEqual(back), "the picture shown on the way back differs from the one composed");
    }

    [AvaloniaFact]
    public void AStrokeOnTheFrameIsSeenOnTheWayBack()
    {
        var vm = Vm();
        Show(vm, 0);
        Assert.Equal(SKColors.White, PixelAt(Show(vm, 0), 700, 100));
        vm.BeginStroke(690, 90, 1);
        vm.MoveStroke(710, 110, 1);
        vm.EndStroke();
        Show(vm, 1);

        var back = Show(vm, 0);

        Assert.NotEqual(SKColors.White, PixelAt(back, 700, 100));
    }

    [AvaloniaFact]
    public void AnOpacityChangedElsewhereIsSeenOnTheWayBack()
    {
        var vm = Vm();
        var before = PixelAt(Show(vm, 0), 80, 60);
        Show(vm, 1);
        vm.ActiveLayerOpacity = 30;

        var back = PixelAt(Show(vm, 0), 80, 60);

        Assert.True(back.Red > before.Red + 40, $"the ink is {back} on the way back, {before} before the opacity changed");
    }

    [AvaloniaFact]
    public void ALayerHiddenElsewhereIsGoneOnTheWayBack()
    {
        var vm = Vm();
        Assert.NotEqual(SKColors.White, PixelAt(Show(vm, 0), 80, 60));
        Show(vm, 1);
        vm.PaintLayer().Visible = false;

        var back = PixelAt(Show(vm, 0), 80, 60);

        Assert.Equal(SKColors.White, back);
    }
}

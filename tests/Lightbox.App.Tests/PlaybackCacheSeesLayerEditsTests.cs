using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// A layer edit between two plays reaches the second play: the composite cache
/// never serves a frame composed before it.
/// </summary>
/// <remarks>
/// <para>
/// The playback composite cache (B167 phase 7) keys a finished frame by where
/// it is and what the view is (<see cref="FrameFingerprint"/>) and by the render
/// epoch — which only drawing edits bump. Opacity, blend mode, hiding a layer
/// and showing another change none of them, so after play, stop, an opacity
/// change and play again, the first lap showed the old opacity: stale art, with
/// nothing to say so. Found by the design review for prepared playback frames
/// (2026-10-12), which would have served far more from that cache.
/// </para>
/// <para>
/// The canvas is what hands a playing frame to the cache; here the test does,
/// on a cache of its own, the way <c>ComposeCacheWiringTests</c> does.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class PlaybackCacheSeesLayerEditsTests : BrushStateIsolated
{
    private static MainViewModel Vm()
    {
        var vm = VmLayers.PaperVm();
        vm.Onion.Enabled = false;
        vm.SmoothStrokes = false;
        vm.BrushSize = 30;
        // A view onto part of the page: the routes that compose on the render
        // thread and so can be cached. A whole-page view composes on the ring.
        vm.SetViewport(SKRectI.Create(0, 0, 600, 340));
        for (var i = 0; i < 3; i++)
        {
            if (i > 0) vm.AddFrameCommand.Execute(null);
            vm.BeginStroke(100, 100, 1);
            vm.MoveStroke(400, 100, 1);
            vm.EndStroke();
        }
        vm.CurrentFrameIndex = 0;
        return vm;
    }

    /// <summary>Play from frame 0, compose what the first publish hands over as the canvas would, stop.</summary>
    private static RenderSnapshot PlayOnce(MainViewModel vm, ComposeCache cache)
    {
        RenderSnapshot? shown = null;
        void Take(RenderSnapshot s) => shown = s;
        vm.SnapshotChanged += Take;
        try
        {
            vm.CurrentFrameIndex = 0;
            vm.TogglePlaybackCommand.Execute(null);
        }
        finally
        {
            vm.SnapshotChanged -= Take;
        }
        Assert.NotNull(shown);
        Assert.True(shown!.CacheKey is not null, "a playing frame after a stroke was not offered to the cache");
        shown.Materialise(null, null, cache);
        vm.TogglePlaybackCommand.Execute(null);
        return shown;
    }

    private static void AssertNotServedStale(MainViewModel vm, Action edit, string what)
    {
        var cache = new ComposeCache(256L * 1024 * 1024);
        PlayOnce(vm, cache);

        edit();
        var again = PlayOnce(vm, cache);

        Assert.False(again.FromCache, $"after {what} the second play was served the frame composed before it");
    }

    [AvaloniaFact]
    public void AnOpacityChangedBetweenPlaysIsSeen()
    {
        var vm = Vm();
        AssertNotServedStale(vm, () => vm.ActiveLayerOpacity = 30, "an opacity change");
    }

    [AvaloniaFact]
    public void ABlendModeChangedBetweenPlaysIsSeen()
    {
        var vm = Vm();
        AssertNotServedStale(vm, () => vm.ActiveLayerBlendMode = LayerBlendMode.Multiply, "a blend mode change");
    }

    [AvaloniaFact]
    public void HidingOneLayerAndShowingAnotherBetweenPlaysIsSeen()
    {
        var vm = Vm();
        vm.AddPaintedLayerCommand.Execute(null);
        var second = vm.Doc.Scene.Layers[^1];
        second.Visible = false;
        var first = vm.Doc.Scene.Layers.First(l => !l.IsBackground && !ReferenceEquals(l, second));
        AssertNotServedStale(vm, () =>
        {
            first.Visible = false;
            second.Visible = true;
        }, "swapping which layer is hidden");
    }

    [AvaloniaFact]
    public void TheSamePlayTwiceIsStillServedFromTheCache()
    {
        var vm = Vm();
        var cache = new ComposeCache(256L * 1024 * 1024);
        PlayOnce(vm, cache);

        var again = PlayOnce(vm, cache);

        Assert.True(again.FromCache, "nothing changed and the second play composed again: the fix keyed too much");
    }

    /// <summary>
    /// A reference strip's dials are drawn in the playback composite and are not
    /// undo steps, so the adversary review found them outside the key: change a
    /// reference's opacity between plays and the next play showed the old one.
    /// </summary>
    [AvaloniaFact]
    public void AReferenceChangedBetweenPlaysIsSeen()
    {
        var vm = Vm();
        using (var bmp = new SKBitmap(160, 120, SKColorType.Rgba8888, SKAlphaType.Premul))
        {
            using (var canvas = new SKCanvas(bmp))
            {
                canvas.Clear(new SKColor(180, 160, 140));
                using var paint = new SKPaint { Color = new SKColor(40, 60, 90) };
                for (var i = 0; i < 6; i++) canvas.DrawRect(SKRect.Create(10 + i * 24, 15 + (i % 3) * 30, 18, 22), paint);
            }
            Assert.NotNull(vm.ImportReference("Photo", Lightbox.Raster.PngCodec.Encode(bmp)));
        }
        AssertNotServedStale(vm, () => vm.ReferenceOpacity = 0.9, "a reference opacity change");
    }
}

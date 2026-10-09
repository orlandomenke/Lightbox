using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Arriving at a frame whose drawings are not cached renders them on the
/// prewarmer's workers, in parallel, rather than one after another on the UI
/// thread. On a 30-layer document the lab measured a flip at 3.6 s (1080p) and
/// 12.5 s (4K), nearly all of it the UI thread rendering layer after layer
/// (`raster.miss`) while the other cores idled (2026-10-09).
/// </summary>
[Collection("BrushState")]
public class ColdFrameParallelTests : BrushStateIsolated
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _posted = new();

    // Thumbnails render on the thumbnail worker in the app; unwired, a headless
    // test renders them on this thread and they would read as the publish's.
    public ColdFrameParallelTests() => Lightbox.App.Services.ThumbnailWorker.Post = _posted.Enqueue;

    public override void Dispose()
    {
        Lightbox.App.Services.ThumbnailWorker.Post = null;
        base.Dispose();
    }

    [AvaloniaFact]
    public void ArrivingAtAColdFrameRendersNoneOfItsDrawingsOnTheUIThread()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        vm.Onion.Enabled = false;
        vm.SetViewport(SKRectI.Create(0, 0, 960, 540));
        for (var l = 0; l < 6; l++) vm.AddPaintedLayerCommand.Execute(null);
        vm.AddFrameCommand.Execute(null);
        // A drawing on every layer at both frames, so the second is all new.
        for (var f = 0; f < 2; f++)
        {
            vm.CurrentFrameIndex = f;
            for (var l = 1; l < vm.Doc.Scene.Layers.Count; l++)
            {
                vm.ActiveLayerIndex = l;
                vm.BeginStroke(40 + l * 30 + f * 7, 60, 1);
                vm.MoveStroke(500, 300 + l * 10, 1);
                vm.EndStroke();
            }
        }
        vm.CurrentFrameIndex = 0;
        vm.PublishSnapshot();
        Assert.True(vm.Prewarm.WaitForIdle(TimeSpan.FromSeconds(20)));
        vm.FrameCache.Clear();

        var misses = vm.FrameCache.Misses;
        vm.CurrentFrameIndex = 1; // arriving at a frame nothing of which is cached
        vm.PublishSnapshot();

        Assert.True(misses == vm.FrameCache.Misses, $"{vm.FrameCache.Misses - misses} UI render(s): " + string.Join(" | ", vm.FrameCache.RecentMisses.Select(m => $"{m}")));
        var scene = vm.Doc.Scene;
        var held = scene.Layers.Count(l =>
            Lightbox.Core.Timeline.ExposureSheet.ExposedFrame(l, 1) is { } f
            && vm.FrameCache.Holds(f, scene.Width, scene.Height, 1.0, 1));
        Assert.True(held >= 7, $"only {held} of the frame's drawings were rendered");
    }

    /// <summary>
    /// No more at once than the cache can hold. Rendered before any is taken
    /// in, a 30-layer 4K frame with onion ghosts is several GB in hand at once,
    /// and past the budget the batch evicts its own first renders, which the
    /// publish then renders again (leak review, 2026-10-09).
    /// </summary>
    [AvaloniaFact]
    public void AColdFrameRendersNoMoreAtOnceThanTheCacheCanHold()
    {
        var budget = Lightbox.Raster.FrameBitmapCache.ByteBudget;
        try
        {
            var vm = ColdFrameVm(layers: 6);
            var scene = vm.Doc.Scene;
            Lightbox.Raster.FrameBitmapCache.ByteBudget = 3L * scene.Width * scene.Height * 4 + 1024;
            vm.FrameCache.Clear();
            vm.LargestParallelRender = 0;

            vm.CurrentFrameIndex = 1;
            vm.PublishSnapshot();

            Assert.InRange(vm.LargestParallelRender, 2, 3);
        }
        finally
        {
            Lightbox.Raster.FrameBitmapCache.ByteBudget = budget;
        }
    }

    /// <summary>
    /// When not everything fits, the frame's own drawings come before its onion
    /// ghosts. Interleaved layer by layer, the cap kept the first layers with
    /// their ghosts and cut the last layers' own drawings, which then rendered
    /// one after another on the UI thread: on a 30-layer document with onion
    /// on, the lab's second flip was slower than before (2026-10-09).
    /// </summary>
    [AvaloniaFact]
    public void WhatDoesNotFitIsTheGhostsNotTheFrame()
    {
        var budget = Lightbox.Raster.FrameBitmapCache.ByteBudget;
        try
        {
            var vm = ColdFrameVm(layers: 6, onion: true);
            var scene = vm.Doc.Scene;
            var own = scene.Layers.Count(l => Lightbox.Core.Timeline.ExposureSheet.ExposedFrame(l, 1) is not null);
            Lightbox.Raster.FrameBitmapCache.ByteBudget = (long)own * scene.Width * scene.Height * 4 + 1024;
            vm.FrameCache.Clear();
            vm.LargestParallelRender = 0;

            vm.CurrentFrameIndex = 1;

            Assert.Equal(own, vm.LastParallelBatch.Count);
            Assert.All(vm.LastParallelBatch, s => Assert.Equal(1, s.Cel));
        }
        finally
        {
            Lightbox.Raster.FrameBitmapCache.ByteBudget = budget;
        }
    }

    private static MainViewModel ColdFrameVm(int layers, bool onion = false)
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        vm.Onion.Enabled = onion;
        vm.SetViewport(SKRectI.Create(0, 0, 960, 540));
        for (var l = 0; l < layers; l++) vm.AddPaintedLayerCommand.Execute(null);
        vm.AddFrameCommand.Execute(null);
        for (var f = 0; f < 2; f++)
        {
            vm.CurrentFrameIndex = f;
            for (var l = 1; l < vm.Doc.Scene.Layers.Count; l++)
            {
                vm.ActiveLayerIndex = l;
                vm.BeginStroke(40 + l * 30 + f * 7, 60, 1);
                vm.MoveStroke(500, 300 + l * 10, 1);
                vm.EndStroke();
            }
        }
        vm.CurrentFrameIndex = 0;
        vm.PublishSnapshot();
        Assert.True(vm.Prewarm.WaitForIdle(TimeSpan.FromSeconds(20)));
        return vm;
    }
}

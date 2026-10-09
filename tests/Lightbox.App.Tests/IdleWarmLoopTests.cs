using System.Collections.Concurrent;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B425: a frame whose stills outgrow the still cache's budget must not keep
/// the idle warm rendering forever.
/// </summary>
/// <remarks>
/// Found while testing the neighbour warm (2026-10-09): with a paper and a
/// drawing on screen and room for one still, the idle warm took the drawing in
/// as wanted, which evicted the paper; the next drain asked for the paper, which
/// evicted the drawing — 2,038 renders in 30 s and never settling. A 30-layer 4K
/// frame with its onion ghosts outgrows the budget the same way. The frame on
/// screen is never evicted now, so the second render finds the first still held.
/// </remarks>
[Collection("BrushState")]
public sealed class IdleWarmLoopTests : BrushStateIsolated
{
    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly long _stillBudget = Lightbox.Raster.FrameBitmapCache.ByteBudget;

    public IdleWarmLoopTests() => ThumbnailWorker.Post = _posted.Enqueue;

    public override void Dispose()
    {
        ThumbnailWorker.Post = null;
        Lightbox.Raster.FrameBitmapCache.ByteBudget = _stillBudget;
        base.Dispose();
    }

    [AvaloniaFact]
    public void AFrameLargerThanTheStillBudgetDoesNotLoopTheIdleWarm()
    {
        var vm = VmLayers.PaperVm();
        vm.WarmPlaybackAtIdle = true;
        vm.Onion.Enabled = false;
        vm.SmoothStrokes = false;
        vm.SetViewport(SKRectI.Create(0, 0, 960, 540));
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(300, 200, 1);
        vm.EndStroke();
        var scene = vm.Doc.Scene;
        // The frame on screen is a paper and a drawing; there is room for one.
        Lightbox.Raster.FrameBitmapCache.ByteBudget = 1L * scene.Width * scene.Height * 4 + 1024;
        vm.FrameCache.Clear();
        var before = vm.Prewarm.Rendered;

        vm.PublishSnapshot();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var quietSince = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline)
        {
            if (_posted.TryDequeue(out var run))
            {
                run();
                quietSince = DateTime.UtcNow;
            }
            else if (!vm.Prewarm.IsBusy && DateTime.UtcNow - quietSince > TimeSpan.FromMilliseconds(500))
            {
                break;
            }
            else
            {
                Thread.Sleep(5);
            }
        }

        var renders = vm.Prewarm.Rendered - before;
        Assert.True(renders < 20, $"the idle warm rendered {renders} stills and did not settle");
    }
}

using System.Collections.Concurrent;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// While playing, the navigator keeps its picture rather than render the frame's
/// drawings on the UI thread; it catches up when playback stops.
/// </summary>
/// <remarks>
/// <para>
/// Every first Play on the lab's 30-layer document froze the window for about
/// 2.5 s (18 runs of 18, 2026-10-10). The thumbnails made on opening were still
/// arriving, each arrival refreshes the thumbnails, and the navigator's picture
/// is composed from the full-size stills of the frame on screen. Playback draws
/// tiles, so those stills were not there, and the UI thread rendered all thirty,
/// one after another, for a picture the size of a stamp. Stop's tile hold
/// already had this guard (Q218); playing did not.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class NavigatorDuringPlaybackTests : BrushStateIsolated
{
    private readonly ConcurrentQueue<Action> _posted = new();

    public NavigatorDuringPlaybackTests() => ThumbnailWorker.Post = _posted.Enqueue;

    public override void Dispose()
    {
        ThumbnailWorker.Post = null;
        base.Dispose();
    }

    private static MainViewModel Vm()
    {
        var vm = VmLayers.PaperVm();
        vm.Onion.Enabled = false;
        vm.SmoothStrokes = false;
        vm.BrushSize = 14;
        vm.SetViewport(SKRectI.Create(0, 0, 960, 540));
        vm.Workspace.NavigatorVisible = true;
        for (var i = 0; i < 4; i++)
        {
            if (i > 0) vm.AddFrameCommand.Execute(null);
            vm.BeginStroke(80 + i * 60, 60 + i * 30, 1);
            vm.MoveStroke(500 - i * 40, 400 - i * 20, 0.6);
            vm.EndStroke();
        }
        vm.CurrentFrameIndex = 0;
        return vm;
    }

    private void Settle(MainViewModel vm)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
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
                return;
            }
            else
            {
                Thread.Sleep(5);
            }
        }
        throw new TimeoutException("never settled");
    }

    /// <summary>
    /// Play or stop. Starting may buffer first — the loop's first frames are
    /// finished before the clock runs (prepared playback frames, step 6) — so
    /// this runs what the workers hand back until playback has actually begun.
    /// </summary>
    private void Toggle(MainViewModel vm)
    {
        var starting = !vm.IsPlaying;
        vm.TogglePlaybackCommand.Execute(null);
        if (!starting) return;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (vm.IsBufferingPlay && DateTime.UtcNow < deadline)
        {
            if (_posted.TryDequeue(out var run)) run();
            else Thread.Sleep(5);
        }
    }

    [AvaloniaFact]
    public void AThumbnailRefreshWhilePlayingRendersNoStillOnTheUiThread()
    {
        var vm = Vm();
        vm.PublishSnapshot();
        Settle(vm);

        Toggle(vm);
        vm.CurrentFrameIndex = 2;
        vm.PublishSnapshot();
        // Playback draws tiles: the frame's stills are not there, as on a
        // document larger than the still cache.
        vm.FrameCache.Clear();
        var before = vm.FrameCache.Misses;
        var picture = vm.NavigatorThumb;

        vm.RefreshThumbnails(); // what an arriving thumbnail sets off

        Assert.Equal(0, vm.FrameCache.Misses - before);
        Assert.Same(picture, vm.NavigatorThumb);
    }

    [AvaloniaFact]
    public void TheNavigatorCatchesUpWhenPlaybackStops()
    {
        var vm = Vm();
        vm.PublishSnapshot();
        Settle(vm);
        var atStart = vm.NavigatorThumb;

        Toggle(vm);
        vm.CurrentFrameIndex = 2;
        vm.PublishSnapshot();
        vm.RefreshThumbnails();
        Toggle(vm);
        Settle(vm);

        Assert.NotNull(vm.NavigatorThumb);
        Assert.NotSame(atStart, vm.NavigatorThumb);
    }
}

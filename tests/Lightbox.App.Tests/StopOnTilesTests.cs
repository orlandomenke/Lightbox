using System.Collections.Concurrent;
using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Playback phase 3 (docs/DESIGN-playback-first-loop.md, Q218): Stop keeps the
/// stopped frame on the playback tiles until the still canvas's own images of it
/// are ready, then swaps.
/// </summary>
/// <remarks>
/// Stop used to render every drawing of the stopped frame on the UI thread before
/// showing it — 5.7-8.8 s on the owner-shaped document. The held frame is the
/// same bytes as the still from 50% zoom up, measured; below that, stroke edges
/// settle at the swap (the owner's choice, Q218).
/// </remarks>
[Collection("BrushState")]
public sealed class StopOnTilesTests : BrushStateIsolated
{
    private readonly ITestOutputHelper _output;
    private readonly ConcurrentQueue<Action> _posted = new();

    public StopOnTilesTests(ITestOutputHelper output)
    {
        _output = output;
        ThumbnailWorker.Post = _posted.Enqueue;
    }

    public override void Dispose()
    {
        ThumbnailWorker.Post = null;
        base.Dispose();
    }

    private static MainViewModel Vm(double displayScale = 1.0)
    {
        var vm = VmLayers.PaperVm();
        vm.Onion.Enabled = false; // ghosts are a different picture paused and playing; not this question
        vm.SmoothStrokes = false;
        vm.BrushSize = 14;
        vm.BrushHardness = 0.6;
        vm.SetViewport(SKRectI.Create(0, 0, 960, 540));
        vm.SetDisplayScale(displayScale);
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

    private static SKBitmap Pixels(RenderSnapshot snapshot)
    {
        using var composed = snapshot.Materialise(null);
        return SKBitmap.FromImage(composed)!;
    }

    /// <summary>Play a little, let the range be prepared, then stop on frame 2.</summary>
    private RenderSnapshot? PlayThenStop(MainViewModel vm, Action<RenderSnapshot> seen)
    {
        vm.PublishSnapshot();
        Settle(vm); // the idle warm prepares every frame's tiles
        // As in the app after a real play: the stopped frame was never shown
        // paused, so the still canvas has no images of it. (Here, drawing on it
        // made them — and a frame whose stills exist rightly is not held.)
        vm.FrameCache.Clear();
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => { latest = s; seen(s); };
        vm.TogglePlaybackCommand.Execute(null);
        vm.CurrentFrameIndex = 2;
        vm.PublishSnapshot();
        vm.TogglePlaybackCommand.Execute(null); // Stop: publishes the stopped frame
        return latest;
    }

    [AvaloniaFact]
    public void StopShowsTheFrameFromTheTilesThenSwapsToTheStill()
    {
        var vm = Vm();
        RenderSnapshot? atStop = null;
        var missesBefore = 0L;
        PlayThenStop(vm, s => { if (!vm.IsPlaying && atStop is null) atStop = s; });
        missesBefore = vm.FrameCache.Misses;

        Assert.True(vm.HoldingTilesAfterStop, "Stop did not hold the playback tiles");
        Assert.True(atStop?.Deferred?.Tiled == true, "the stopped frame was not composed from tiles");

        Settle(vm); // the stills arrive; the drain swaps
        _output.WriteLine($"still-image renders on the UI thread after stop: {vm.FrameCache.Misses - missesBefore}");
        Assert.False(vm.HoldingTilesAfterStop, "the hold never ended");
    }

    /// <summary>
    /// The stop renders no still image on the UI thread — that was the whole
    /// freeze — and the frame it shows is the still's bytes from 50% zoom up.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(0.5)]
    public void TheHeldFrameIsTheStillPictureByteForByteFromHalfZoomUp(double zoom)
    {
        var vm = Vm(zoom);
        RenderSnapshot? atStop = null;
        long missesAtStop = 0;
        vm.SnapshotChanged += s => { if (!vm.IsPlaying && vm.HoldingTilesAfterStop) atStop ??= s; };
        vm.PublishSnapshot();
        Settle(vm);
        vm.FrameCache.Clear(); // see PlayThenStop
        vm.TogglePlaybackCommand.Execute(null);
        vm.CurrentFrameIndex = 2;
        vm.PublishSnapshot();
        var missesBefore = vm.FrameCache.Misses;
        vm.TogglePlaybackCommand.Execute(null);
        missesAtStop = vm.FrameCache.Misses - missesBefore;
        Assert.NotNull(atStop);
        using var held = Pixels(atStop!);

        RenderSnapshot? still = null;
        vm.SnapshotChanged += s => still = s;
        Settle(vm);
        Assert.False(vm.HoldingTilesAfterStop);
        Assert.NotNull(still);
        Assert.False(still!.Deferred?.Tiled == true, "the swap did not reach the still route");
        using var final = Pixels(still);

        _output.WriteLine($"zoom {zoom:P0}: still-image misses during the stop publish {missesAtStop}");
        Assert.Equal(0, missesAtStop);
        Assert.True(held.Bytes.AsSpan().SequenceEqual(final.Bytes), $"at {zoom:P0} the held frame is not the still's bytes");
    }

    [AvaloniaFact]
    public void AStrokeEndsTheHold()
    {
        var vm = Vm();
        PlayThenStop(vm, _ => { });
        Assert.True(vm.HoldingTilesAfterStop);

        vm.BeginStroke(600, 300, 1);
        Assert.False(vm.HoldingTilesAfterStop);
        vm.EndStroke();
    }

    [AvaloniaFact]
    public void MovingThePlayheadEndsTheHold()
    {
        var vm = Vm();
        PlayThenStop(vm, _ => { });
        Assert.True(vm.HoldingTilesAfterStop);

        vm.CurrentFrameIndex = 1;
        Assert.False(vm.HoldingTilesAfterStop);
    }

    /// <summary>Without a way to hand the stills back, nothing would ever swap — so nothing is held.</summary>
    [AvaloniaFact]
    public void WithNoWayToDeliverTheStillsStopDoesNotHold()
    {
        ThumbnailWorker.Post = null;
        var vm = Vm();
        vm.TogglePlaybackCommand.Execute(null);
        vm.CurrentFrameIndex = 2;
        vm.PublishSnapshot();
        vm.TogglePlaybackCommand.Execute(null);

        Assert.False(vm.HoldingTilesAfterStop);
    }
}

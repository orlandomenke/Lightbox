using System.Collections.Concurrent;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Idle time renders the drawings either side of the playhead, so a flip to the
/// next or previous drawing finds them ready.
/// </summary>
/// <remarks>
/// <para>
/// The lab's large documents (30 layers, 200 drawings, 2026-10-09) spent most
/// of a flip rendering the frame it arrived at. The idle warm already made the
/// current frame's stills, its onion ghosts (Q219) and the playback range as
/// tiles, but a flip lands on the paused canvas, which draws stills. With onion
/// off, the owner's default, nothing either side was ready.
/// </para>
/// <para>
/// A guess, so it goes in only where there is room (<c>InsertWarm</c>) and
/// answers to the same switch as the playback warm: the switch exists for a
/// laptop on battery, and this is idle CPU too.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class NeighbourStillWarmTests : BrushStateIsolated
{
    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly long _stillBudget = Lightbox.Raster.FrameBitmapCache.ByteBudget;

    public NeighbourStillWarmTests() => ThumbnailWorker.Post = _posted.Enqueue;

    public override void Dispose()
    {
        ThumbnailWorker.Post = null;
        Lightbox.Raster.FrameBitmapCache.ByteBudget = _stillBudget;
        base.Dispose();
    }

    /// <summary>Six frames, each its own drawing on one layer, onion off.</summary>
    private static MainViewModel Vm(bool warm = true)
    {
        var vm = VmLayers.PaperVm();
        vm.WarmPlaybackAtIdle = warm;
        vm.Onion.Enabled = false;
        vm.SmoothStrokes = false;
        vm.BrushSize = 12;
        vm.SetViewport(SKRectI.Create(0, 0, 960, 540));
        for (var i = 0; i < 6; i++)
        {
            if (i > 0) vm.AddFrameCommand.Execute(null);
            vm.BeginStroke(100 + i * 20, 100, 1);
            vm.MoveStroke(300 - i * 20, 200, 1);
            vm.EndStroke();
        }
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
        throw new TimeoutException($"the idle warm never settled: rendered {vm.Prewarm.Rendered}, installed {vm.Prewarm.Installed}, refused {vm.Prewarm.Refused}, busy {vm.Prewarm.IsBusy}, still entries {vm.FrameCache.CachedFrames}");
    }

    private static Frame DrawingAt(MainViewModel vm, int index) =>
        vm.Doc.Scene.Layers.Where(l => !l.IsBackground)
            .Select(l => ExposureSheet.ExposedFrame(l, index)).OfType<Frame>().Single();

    private static bool Held(MainViewModel vm, int index) =>
        vm.FrameCache.Holds(DrawingAt(vm, index), vm.Doc.Scene.Width, vm.Doc.Scene.Height, 1.0, index);

    [AvaloniaFact]
    public void AtIdleTheDrawingsEitherSideAreReadyForAFlip()
    {
        var vm = Vm();
        vm.CurrentFrameIndex = 2;
        vm.FrameCache.Clear();
        vm.PublishSnapshot();
        Settle(vm);

        Assert.True(Held(vm, 1), "the previous drawing was not prepared");
        Assert.True(Held(vm, 3), "the next drawing was not prepared");

        var misses = vm.FrameCache.Misses;
        vm.CurrentFrameIndex = 3;
        vm.PublishSnapshot();
        Assert.True(misses == vm.FrameCache.Misses,
            "the flip rendered on the UI thread: " + string.Join(" | ", vm.FrameCache.RecentMisses));
    }

    /// <summary>
    /// Only as many as there is room for, nearest first: a guess the cache will
    /// refuse is a render thrown away, every idle (the leak review; B408's shape).
    /// </summary>
    [AvaloniaFact]
    public void OnlyTheNeighboursThereIsRoomForAreRendered()
    {
        var vm = Vm();
        var scene = vm.Doc.Scene;
        vm.CurrentFrameIndex = 2;
        vm.FrameCache.Clear();
        // The frame on screen (its paper and its drawing), two more, and the
        // one still's slack the warm leaves for smaller pictures.
        Lightbox.Raster.FrameBitmapCache.ByteBudget = 5L * scene.Width * scene.Height * 4 + 1024;
        vm.PublishSnapshot();
        Settle(vm);

        Assert.Equal(0, vm.NeighboursRefused);
        Assert.True(Held(vm, 3) && Held(vm, 1), "the nearest neighbours were not the ones given the room");
    }

    /// <summary>
    /// A still cache with no room must not stop the playback warm, which fills
    /// another cache: a refused neighbour used to set the flag that ends all
    /// idle warming until the next edit (the leak review, 2026-10-09).
    /// </summary>
    [AvaloniaFact]
    public void AFullStillCacheDoesNotStopThePlaybackWarm()
    {
        var vm = Vm();
        var scene = vm.Doc.Scene;
        vm.CurrentFrameIndex = 2;
        vm.FrameCache.Clear();
        // Room for the frame on screen, its paper and its drawing, and no more.
        // (Less than that loops the idle warm on main — B-filed separately.)
        Lightbox.Raster.FrameBitmapCache.ByteBudget = 2L * scene.Width * scene.Height * 4 + 1024;
        vm.PublishSnapshot();
        Settle(vm);

        vm.TileFrames.Clear();
        vm.CurrentFrameIndex = 3; // a flip, not an edit
        vm.PublishSnapshot();
        Settle(vm);

        var drawings = Enumerable.Range(0, scene.FrameCount).Select(i => DrawingAt(vm, i)).ToList();
        Assert.All(drawings, f => Assert.True(vm.TileFrames.Holds(f.Id), $"{f.Id}'s playback tiles were not prepared again"));
    }

    /// <summary>
    /// The room for guesses is what the still cache has left <em>and</em> what
    /// the overall picture limit has left, less a still of slack. Measured
    /// against the cache alone, once its count cap stopped binding (B440), one
    /// idle batch could take the broker past its limit by the batch's size, and
    /// the next publish evicted the playback tiles to pay for guesses (the leak
    /// review, 2026-10-10).
    /// </summary>
    [Theory]
    [InlineData(100, 10, 1000, 990, 1, 9)]      // the overall limit is nearer: 10 left, less the slack
    [InlineData(100, 95, 1000, 0, 1, 4)]        // the cache is nearer
    [InlineData(100, 10, 1000, 1200, 1, 0)]     // already over the overall limit: nothing
    [InlineData(100, 10, 1000, 0, 30, 2)]       // in stills, not bytes
    public void TheRoomForNeighboursIsTheLesserOfTheCacheAndTheOverallLimit(
        long budget, long cached, long brokered, long total, long perStill, int room)
    {
        Assert.Equal(room, MainViewModel.RoomForGuesses(budget, cached, brokered, total, perStill));
    }

    [AvaloniaFact]
    public void WithTheSwitchOffTheNeighboursAreNotPrepared()
    {
        var vm = Vm(warm: false);
        vm.CurrentFrameIndex = 2;
        vm.FrameCache.Clear();
        vm.PublishSnapshot();
        Settle(vm);

        Assert.False(Held(vm, 1));
        Assert.False(Held(vm, 3));
    }
}

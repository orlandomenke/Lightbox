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

    public NeighbourStillWarmTests() => ThumbnailWorker.Post = _posted.Enqueue;

    public override void Dispose()
    {
        ThumbnailWorker.Post = null;
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
        throw new TimeoutException("the idle warm never settled");
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

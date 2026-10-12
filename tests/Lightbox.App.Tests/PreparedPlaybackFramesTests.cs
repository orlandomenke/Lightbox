using System.Collections.Concurrent;
using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Playing frames are finished before the playhead reaches them, so the first
/// lap is shown from finished composites as the second already is (prepared
/// playback frames, steps 4-7).
/// </summary>
/// <remarks>
/// <para>
/// With playback paced by the screen's refresh and a composed frame shown
/// without being built (#705), the second lap of the lab's 30-layer document
/// dropped nothing; the first still dropped about forty frames in fifteen
/// seconds, every one of them a frame built and blended on its own tick. A
/// frame prepared ahead is built on the UI thread between refreshes and
/// blended on a worker, then offered to the cache under the key the tick asks
/// with.
/// </para>
/// <para>
/// <b>Identity is the guard.</b> A prepared frame must be the picture the tick
/// would have composed, byte for byte — it is built by the same calls, and the
/// test composes the same frame the ordinary way in a twin document to compare.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class PreparedPlaybackFramesTests : BrushStateIsolated
{
    private readonly ConcurrentQueue<Action> _posted = new();

    public override void Dispose()
    {
        ThumbnailWorker.Post = null;
        base.Dispose();
    }

    private static MainViewModel Vm(ComposeCache cache)
    {
        var vm = VmLayers.PaperVm();
        vm.WarmPlaybackAtIdle = false;
        vm.Onion.Enabled = false;
        vm.SmoothStrokes = false;
        vm.BrushSize = 30;
        vm.SetViewport(SKRectI.Create(0, 0, 600, 340));
        vm.PlaybackComposites = cache;
        for (var i = 0; i < 4; i++)
        {
            if (i > 0) vm.AddFrameCommand.Execute(null);
            vm.BeginStroke(100 + i * 60, 100, 1);
            vm.MoveStroke(400 - i * 40, 200, 1);
            vm.EndStroke();
        }
        vm.CurrentFrameIndex = 0;
        return vm;
    }

    private static byte[] Bytes(SKImage image)
    {
        using var bmp = SKBitmap.FromImage(image);
        return bmp.Bytes;
    }

    /// <summary>What the tick composes for <paramref name="frame"/>, the ordinary way.</summary>
    private static (byte[] Pixels, ComposeKey? Key) Composed(MainViewModel vm, ComposeCache cache, int frame)
    {
        RenderSnapshot? shown = null;
        void Take(RenderSnapshot s) => shown = s;
        vm.SnapshotChanged += Take;
        try
        {
            vm.CurrentFrameIndex = frame;
        }
        finally
        {
            vm.SnapshotChanged -= Take;
        }
        Assert.NotNull(shown);
        var image = shown!.Materialise(null, null, cache);
        return (Bytes(image), shown.CacheKey);
    }

    private void Settle(MainViewModel vm)
    {
        var quietSince = DateTime.UtcNow;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (_posted.TryDequeue(out var run))
            {
                run();
                quietSince = DateTime.UtcNow;
            }
            else if (!vm.Prewarm.IsBusy && !vm.IsPreparingFrames && DateTime.UtcNow - quietSince > TimeSpan.FromMilliseconds(500))
            {
                return;
            }
            else
            {
                Thread.Sleep(5);
            }
        }
    }

    [AvaloniaFact]
    public void APreparedFrameIsThePictureTheTickWouldHaveComposed()
    {
        // The twin composes frame 2 the ordinary way.
        var plainCache = new ComposeCache(256L * 1024 * 1024);
        var twin = Vm(plainCache);
        twin.TogglePlaybackCommand.Execute(null);
        var (expected, _) = Composed(twin, plainCache, 2);
        twin.TogglePlaybackCommand.Execute(null);

        var cache = new ComposeCache(256L * 1024 * 1024);
        var vm = Vm(cache);
        vm.TogglePlaybackCommand.Execute(null);
        Assert.True(vm.PrepareFrameNow(2), "frame 2 could not be prepared");

        RenderSnapshot? shown = null;
        void Take(RenderSnapshot s) => shown = s;
        vm.SnapshotChanged += Take;
        vm.CurrentFrameIndex = 2;
        vm.SnapshotChanged -= Take;
        vm.TogglePlaybackCommand.Execute(null);

        Assert.NotNull(shown);
        // Served from the cache: the prepared frame's key is the one the tick asked
        // with. (Not compared with the twin's: layer ids differ per document.)
        Assert.True(shown!.FromCache, "the tick built a frame that was prepared");
        Assert.True(expected.AsSpan().SequenceEqual(Bytes(shown.Image!)), "the prepared frame differs from the one the tick composes");
    }

    [AvaloniaFact]
    public void PreparingAFrameDoesNotChangeTheNextOneTheTickComposes()
    {
        var plainCache = new ComposeCache(256L * 1024 * 1024);
        var twin = Vm(plainCache);
        twin.TogglePlaybackCommand.Execute(null);
        var (expected, _) = Composed(twin, plainCache, 1);
        twin.TogglePlaybackCommand.Execute(null);

        var cache = new ComposeCache(256L * 1024 * 1024);
        var vm = Vm(cache);
        vm.TogglePlaybackCommand.Execute(null);
        Assert.True(vm.PrepareFrameNow(3));
        var (pixels, _) = Composed(vm, cache, 1); // not prepared: built on its tick
        vm.TogglePlaybackCommand.Execute(null);

        Assert.True(expected.AsSpan().SequenceEqual(pixels), "preparing frame 3 changed how frame 1 was composed");
    }

    /// <summary>
    /// While playing nothing is prepared: a frame's blend costs about 330 ms on
    /// the lab's 30-layer document, and a worker doing that beside the tick took
    /// cores from playback and dropped more frames than it saved (2026-10-12).
    /// Frames are prepared while play buffers, on every spare core, and not after.
    /// </summary>
    [AvaloniaFact]
    public void WhilePlayingNothingIsPrepared()
    {
        ThumbnailWorker.Post = _posted.Enqueue;
        var cache = new ComposeCache(256L * 1024 * 1024);
        var vm = Vm(cache);
        vm.TogglePlaybackCommand.Execute(null);
        Settle(vm); // buffers, prepares, starts
        Assert.True(vm.IsPlaying);
        var prepared = vm.FramesPrepared;

        vm.CurrentFrameIndex = 1;
        vm.CurrentFrameIndex = 2;
        Settle(vm);
        vm.TogglePlaybackCommand.Execute(null);

        Assert.Equal(prepared, vm.FramesPrepared);
    }
}

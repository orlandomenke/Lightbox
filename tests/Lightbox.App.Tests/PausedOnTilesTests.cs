using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Phase 1 of <c>docs/DESIGN-one-picture-cache.md</c>: the paused canvas composes
/// from the tiles playback already holds, rather than from a full-canvas still
/// per layer per drawing.
/// </summary>
/// <remarks>
/// <b>The gate is identity.</b> The paused canvas is where an artist judges a
/// line, so the tiled picture must be the still picture's bytes at every zoom —
/// including below 50%, where Q218 measured the playback route differ by up to
/// 64/255. The paused route therefore composes from level-0 tiles, never from a
/// pyramid level, and this is what proves it.
/// </remarks>
[Collection("BrushState")]
public class PausedOnTilesTests : BrushStateIsolated
{
    private static MainViewModel Vm(double zoom, bool onion)
    {
        var vm = VmLayers.PaperVm();
        vm.Onion.Enabled = onion;
        vm.SmoothStrokes = false;
        vm.BrushSize = 14;
        vm.BrushHardness = 0.6;
        vm.SetViewport(SKRectI.Create(0, 0, 960, 540));
        vm.SetDisplayScale(zoom);
        for (var i = 0; i < 4; i++)
        {
            if (i > 0) vm.AddFrameCommand.Execute(null);
            vm.BeginStroke(80 + i * 60, 60 + i * 30, 1);
            vm.MoveStroke(500 - i * 40, 400 - i * 20, 0.6);
            vm.EndStroke();
        }
        vm.CurrentFrameIndex = 2;
        return vm;
    }

    private static RenderSnapshot Publish(MainViewModel vm)
    {
        RenderSnapshot? latest = null;
        void Seen(RenderSnapshot s) => latest = s;
        vm.SnapshotChanged += Seen;
        vm.PublishSnapshot();
        vm.SnapshotChanged -= Seen;
        return latest ?? throw new InvalidOperationException("nothing was published");
    }

    private static SKBitmap Pixels(RenderSnapshot snapshot)
    {
        using var composed = snapshot.Materialise(null);
        return SKBitmap.FromImage(composed)!;
    }

    [AvaloniaTheory]
    [InlineData(1.0, false)]
    [InlineData(0.5, false)]
    [InlineData(0.33, false)]
    [InlineData(0.25, false)]
    [InlineData(1.0, true)]
    [InlineData(0.25, true)]
    public void ThePausedCanvasFromTilesIsTheStillPictureByteForByte(double zoom, bool onion)
    {
        var vm = Vm(zoom, onion);

        vm.PausedOnTiles = false;
        var reference = Publish(vm);
        Assert.False(reference.Deferred?.Tiled == true, "the reference did not take the still route");
        using var still = Pixels(reference);
        vm.PausedOnTiles = true;
        // Tiles show while the frame on screen has no stills yet: as on
        // arriving at it.
        Assert.True(vm.Prewarm.WaitForIdle(TimeSpan.FromSeconds(20)));
        vm.FrameCache.Clear();
        Assert.True(vm.PausedTilesNow, "an edit is still counted as in progress");
        var snapshot = Publish(vm);
        Assert.True(snapshot.Deferred?.Tiled == true, "the paused frame was not composed from tiles");
        using var tiled = Pixels(snapshot);

        var differing = 0;
        var a = still.Bytes;
        var b = tiled.Bytes;
        Assert.Equal(a.Length, b.Length);
        for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) differing++;
        Assert.True(differing == 0, $"at {zoom:P0}, onion {onion}: {differing} byte(s) differ from the still");
    }

    /// <summary>
    /// The first dab after a frame's stills arrive costs what it cost when the
    /// paused canvas was on stills. Shown from tiles until pen-down, the layer
    /// stacks around the active layer were first baked at the stroke — 3.7 →
    /// 47 ms at 1080p and 11 → 186 ms at 4K (seven layers, measured) — because a
    /// bake waits for a second sighting of its key, and the stills' arrival
    /// publish was the only first one. It now bakes there.
    /// </summary>
    [AvaloniaFact]
    public void TheFirstDabAfterTheStillsArriveBuildsNoBake()
    {
        var posted = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        Lightbox.App.Services.ThumbnailWorker.Post = posted.Enqueue;
        try
        {
            var vm = Vm(1.0, onion: false);
            for (var l = 0; l < 3; l++) vm.AddPaintedLayerCommand.Execute(null); // stacks above and below
            vm.ActiveLayerIndex = 1;
            vm.PausedOnTiles = true;
            Assert.True(vm.Prewarm.WaitForIdle(TimeSpan.FromSeconds(20)));
            while (posted.TryDequeue(out var stale)) stale();
            vm.FrameCache.Clear(); // arriving at the frame
            Assert.True(Publish(vm).Deferred?.Tiled == true, "the frame was not shown from tiles on arrival");
            for (var i = 0; i < 20 && vm.PausedTilesNow; i++)
            {
                Assert.True(vm.Prewarm.WaitForIdle(TimeSpan.FromSeconds(20)));
                while (posted.TryDequeue(out var run)) run();
            }
            Assert.False(vm.PausedTilesNow, "the frame's stills never arrived");

            var rebuilds = vm.StackBake.Rebuilds;
            vm.BeginStroke(300, 200, 1);
            vm.MoveStroke(310, 205, 1);
            vm.PublishSnapshot();
            vm.EndStroke();

            Assert.Equal(rebuilds, vm.StackBake.Rebuilds);
        }
        finally
        {
            Lightbox.App.Services.ThumbnailWorker.Post = null;
        }
    }

    /// <summary>
    /// Paging through drawings while paused leaves no stills behind: the frame
    /// on screen keeps its own, warmed for the next stroke, and a frame the
    /// playhead has left gives them back.
    /// </summary>
    [AvaloniaFact]
    public void PagingThroughDrawingsLeavesNoStillsBehind()
    {
        var vm = Vm(1.0, onion: false);
        vm.PausedOnTiles = true;
        for (var f = 0; f < 4; f++)
        {
            vm.CurrentFrameIndex = f;
            Publish(vm);
            Assert.True(vm.Prewarm.WaitForIdle(TimeSpan.FromSeconds(20)), "the warm never finished");
            Publish(vm); // takes what was warmed, and releases what was left
        }

        var onScreen = vm.Doc.Scene.Layers
            .Select(l => Lightbox.Core.Timeline.ExposureSheet.ExposedFrame(l, vm.CurrentFrameIndex)?.Id)
            .OfType<string>().ToHashSet();
        var scene = vm.Doc.Scene;
        var leftBehind = scene.Layers
            .SelectMany(l => l.Cels.Select((c, i) => (Frame: c.Frame, Cel: i)))
            .Where(x => x.Frame is { } f && !onScreen.Contains(f.Id) && vm.TileFrames.Holds(f.Id)
                        && vm.FrameCache.Holds(f, scene.Width, scene.Height, 1.0, x.Cel))
            .Select(x => x.Frame!.Id).ToList();
        Assert.True(leftBehind.Count == 0, $"stills kept for drawings off screen: {string.Join(", ", leftBehind)}");
        // Not vacuous: the drawings on screen do keep theirs, for the next stroke.
        Assert.Contains(scene.Layers.SelectMany(l => l.Cels.Select((c, i) => (c.Frame, i))),
            x => x.Frame is { } f && onScreen.Contains(f.Id) && vm.FrameCache.Holds(f, scene.Width, scene.Height, 1.0, x.i));
    }

    /// <summary>
    /// Showing a paused frame renders no still on the UI thread: the frame is
    /// shown from tiles, and its stills (for the next stroke) are warmed in the
    /// background.
    /// </summary>
    [AvaloniaFact]
    public void ShowingAPausedFrameRendersNoStillOnTheUIThread()
    {
        var vm = Vm(1.0, onion: false);
        vm.PausedOnTiles = false;
        Assert.True(vm.Prewarm.WaitForIdle(TimeSpan.FromSeconds(20)));
        vm.FrameCache.Clear();
        var before = vm.FrameCache.Misses;
        Publish(vm);
        Assert.True(vm.FrameCache.Misses > before, "not vacuous: the still route renders the frame's stills");

        vm.PausedOnTiles = true;
        Assert.True(vm.Prewarm.WaitForIdle(TimeSpan.FromSeconds(20)));
        vm.FrameCache.Clear();
        before = vm.FrameCache.Misses;
        Publish(vm);

        Assert.Equal(before, vm.FrameCache.Misses);
    }
}

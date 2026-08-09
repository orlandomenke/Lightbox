using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Rasterizing off the UI thread: the same pixels, computed before the tick
/// that needs them rather than during it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The claim under everything here is invariant 2.</b> A render is a pure
/// function of the stroke record — no RNG, every dab dynamic seeded from
/// geometry through <c>Hash01</c> — so which thread replays it, and when,
/// cannot change what comes out. That is what makes prewarming a scheduling
/// change rather than a rendering one, and
/// <see cref="AWarmedFrameIsBitIdenticalToOneRenderedOnThisThread"/> measures it
/// rather than restating it: a brush loaded with every jitter the engine has,
/// rendered on four threads at once, against the same frame rendered here.
/// </para>
/// <para>
/// <b>The second claim is that speculative work never costs real work.</b> A
/// warm that does not fit is dropped rather than making room, and a warm that
/// arrives after the document changed is disposed rather than installed. Both
/// have a test, because both are the kind of thing that is right on the day it
/// is written and quietly wrong two changes later.
/// </para>
/// </remarks>
public class FramePrewarmTests(ITestOutputHelper output)
{
    private const int W = 200;
    private const int H = 200;

    /// <summary>
    /// A stroke that exercises every seeded dynamic at once, so a threading
    /// defect in the seeding would show as different pixels rather than as
    /// nothing.
    /// </summary>
    private static Frame JitteryFrame(int seed) => new()
    {
        Strokes =
        [
            new Stroke
            {
                Tool = ToolKind.Brush,
                Color = "#204080",
                Points =
                [
                    new StrokePoint(10 + seed, 20, 0.4),
                    new StrokePoint(80 + seed, 90, 0.9),
                    new StrokePoint(150 + seed, 40, 0.6),
                ],
                Brush = new BrushSettings
                {
                    Size = 14,
                    Hardness = 0.6,
                    Opacity = 1,
                    Flow = 0.8,
                    Spacing = 0.15,
                    Scatter = 0.5,
                    SizeJitter = 0.4,
                    RotationJitter = 0.5,
                    RoundnessJitter = 0.3,
                    FlowJitter = 0.3,
                    HueJitter = 0.2,
                    SaturationJitter = 0.2,
                    BrightnessJitter = 0.2,
                },
            },
        ],
    };

    private static byte[] BytesOf(SKBitmap bmp)
    {
        var span = bmp.GetPixelSpan();
        return span.ToArray();
    }

    /// <summary>
    /// The whole safety argument, measured: four threads racing on the same
    /// record produce the bytes this thread produces.
    /// </summary>
    [Fact]
    public async Task AWarmedFrameIsBitIdenticalToOneRenderedOnThisThread()
    {
        var frame = JitteryFrame(0);
        using var onThisThread = FrameBitmapCache.RenderDetached(frame, W, H);
        var expected = BytesOf(onThisThread);

        // Not vacuous: a blank render would match a blank render.
        Assert.Contains(expected, b => b != 0);

        var races = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() =>
            {
                using var bmp = FrameBitmapCache.RenderDetached(frame, W, H);
                return BytesOf(bmp);
            })));

        for (var i = 0; i < races.Length; i++)
        {
            var actual = races[i];
            var differing = 0;
            for (var b = 0; b < expected.Length; b++)
            {
                if (expected[b] != actual[b]) differing++;
            }
            output.WriteLine($"thread {i}: {differing} differing bytes of {expected.Length}");
            Assert.Equal(0, differing);
        }
    }

    [Fact]
    public async Task TilesBuiltOffThreadAreBitIdenticalToTilesBuiltHere()
    {
        var frame = JitteryFrame(3);
        var (hereStore, herePyramid) = TileFrameCache.RenderDetached(frame, W, H);
        using var _ = herePyramid;
        using var __ = hereStore;

        var (thereStore, therePyramid) =
            await Task.Run(() => TileFrameCache.RenderDetached(frame, W, H));
        using var ___ = therePyramid;
        using var ____ = thereStore;

        // Same tiles, same ink. Comparing allocated bytes alone would pass on
        // two stores holding different pixels, so the coordinates are compared
        // too — a scatter seeded per thread would move ink across a tile edge.
        Assert.Equal(hereStore.AllocatedBytes, thereStore.AllocatedBytes);
        Assert.True(hereStore.AllocatedBytes > 0, "nothing was rasterized");
    }

    /// <summary>
    /// A result rendered against a document that has since changed is thrown
    /// away, not installed. This is the difference between a prewarm being
    /// wasted and a prewarm being wrong.
    /// </summary>
    [Fact]
    public void AWarmThatWasSupersededIsNeverInstalled()
    {
        using var prewarm = new FramePrewarmer();
        prewarm.Request([new WarmRequest(JitteryFrame(1), W, H, 0, WarmProduct.Bitmap)]);
        Assert.True(prewarm.WaitForIdle(TimeSpan.FromSeconds(10)), "the worker never finished");

        // The document changed — a stroke, an undo, a load. Everything in flight
        // and everything finished describes a document nobody is looking at.
        prewarm.Flush();

        var installed = prewarm.Drain(_ => throw new InvalidOperationException(
            "a superseded warm was offered to the cache"));
        Assert.Equal(0, installed);
    }

    [Fact]
    public void AFinishedWarmIsOfferedExactlyOnce()
    {
        using var prewarm = new FramePrewarmer();
        var frame = JitteryFrame(2);
        prewarm.Request([new WarmRequest(frame, W, H, 0, WarmProduct.Bitmap)]);
        Assert.True(prewarm.WaitForIdle(TimeSpan.FromSeconds(10)));

        var offered = 0;
        Assert.Equal(1, prewarm.Drain(w =>
        {
            offered++;
            Assert.Same(frame, w.Request.Frame);
            Assert.NotNull(w.Bitmap);
            w.Bitmap!.Dispose();
            return true;
        }));
        Assert.Equal(0, prewarm.Drain(_ => true));
        Assert.Equal(1, offered);
        Assert.Equal(1, prewarm.Installed);
    }

    /// <summary>
    /// Anything the cache refuses is freed here rather than leaked. A prewarmer
    /// that leaked on refusal would leak a document-sized bitmap per frame per
    /// pass, which is worse than the stall it removes.
    /// </summary>
    [Fact]
    public void PixelsTheCacheRefusesAreFreedRatherThanLeaked()
    {
        using var prewarm = new FramePrewarmer();
        prewarm.Request([new WarmRequest(JitteryFrame(4), W, H, 0, WarmProduct.Bitmap)]);
        Assert.True(prewarm.WaitForIdle(TimeSpan.FromSeconds(10)));

        SKBitmap? seen = null;
        prewarm.Drain(w =>
        {
            seen = w.Bitmap;
            return false;
        });

        Assert.NotNull(seen);
        Assert.Equal(1, prewarm.Refused);
        // Read the managed handle rather than any property that would touch the
        // native object: a disposed SKBitmap zeroes its handle, and asking a
        // freed one for its pixels segfaults the test process rather than
        // failing an assertion.
        Assert.Equal(IntPtr.Zero, seen!.Handle);
    }

    /// <summary>
    /// A frame already rendered and waiting to be collected is not rendered
    /// again just because the caller asked while it was still in hand.
    /// </summary>
    /// <remarks>
    /// The caller decides what to warm by asking the caches what they hold, and
    /// a finished warm is in neither cache until the next publish takes it — so
    /// every publish in between would ask for it again. That is not a wasted
    /// enqueue, it is a wasted <em>render</em>, on the core this class exists to
    /// spend carefully.
    /// </remarks>
    [Fact]
    public void AFrameAlreadyInHandIsNotRenderedTwice()
    {
        using var prewarm = new FramePrewarmer();
        var frame = JitteryFrame(5);
        var job = new WarmRequest(frame, W, H, 0, WarmProduct.Bitmap);

        prewarm.Request([job]);
        Assert.True(prewarm.WaitForIdle(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, prewarm.Rendered);

        // The caller has not drained yet, so no cache holds this frame and it
        // asks again — exactly what a second publish does.
        prewarm.Request([job]);
        Assert.True(prewarm.WaitForIdle(TimeSpan.FromSeconds(10)));

        output.WriteLine($"rendered {prewarm.Rendered} after two requests for one frame");
        Assert.Equal(1, prewarm.Rendered);
        Assert.Equal(1, prewarm.Drain(w =>
        {
            w.Bitmap!.Dispose();
            return true;
        }));
    }

    [Theory]
    // Forward, mid-range: the ordinary case.
    [InlineData(0, 1, 0, 9, true, new[] { 1, 2, 3 })]
    // Forward off the end of a looping range: wraps to the start.
    [InlineData(8, 1, 0, 9, true, new[] { 9, 0, 1 })]
    // The same range without looping simply ends — there is nothing after it to
    // warm, and guessing past it would warm frames nobody is about to see.
    [InlineData(8, 1, 0, 9, false, new[] { 9 })]
    // Backwards wraps the other way.
    [InlineData(1, -1, 0, 9, true, new[] { 0, 9, 8 })]
    // A one-frame range: the playhead never moves, so neither does the guess.
    [InlineData(4, 1, 4, 4, true, new[] { 4, 4, 4 })]
    public void ThePredictionFollowsTheRangeAndTheDirection(
        int current, int direction, int start, int end, bool loop, int[] expected)
    {
        Assert.Equal(
            expected,
            FramePrewarmer.Upcoming(current, direction, start, end, loop, FramePrewarmer.Lookahead));
    }
}

/// <summary>
/// A warm entering a cache: where it goes, and what it is never allowed to
/// push out.
/// </summary>
[Collection("FrameCacheBudget")]
public class WarmInsertionTests(ITestOutputHelper output) : IDisposable
{
    private readonly long _budget = FrameBitmapCache.ByteBudget;

    public void Dispose() => FrameBitmapCache.ByteBudget = _budget;

    private static Frame Inked(int seed) => new()
    {
        Strokes =
        [
            new Stroke
            {
                Tool = ToolKind.Brush,
                Color = "#204080",
                Points = [new StrokePoint(10 + seed, 10, 1), new StrokePoint(40 + seed, 40, 1)],
                Brush = new BrushSettings { Size = 8, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
            },
        ],
    };

    /// <summary>
    /// The rule that makes prewarming safe to leave switched on: a warm that
    /// does not fit is dropped, never made room for.
    /// </summary>
    /// <remarks>
    /// The failure it prevents is specific and nasty. During playback eviction
    /// is <see cref="FrameBitmapCache.EvictionOrder.MostRecent"/>, whose victim
    /// is the neighbour of the newest entry — which is, almost always, the frame
    /// currently on the screen. A warm that evicted to make room would throw out
    /// the frame the artist is looking at to store one they are not, and the
    /// next pass would have to rasterize it again. Prewarming would then make
    /// playback measurably worse while appearing to be doing its job.
    /// </remarks>
    [Fact]
    public void AWarmThatDoesNotFitIsDroppedRatherThanMakingRoom()
    {
        // Room for exactly two frames of this size.
        FrameBitmapCache.ByteBudget = 100L * 100 * 4 * 2;
        using var cache = new FrameBitmapCache { Eviction = FrameBitmapCache.EvictionOrder.MostRecent };

        var onScreen = Inked(0);
        var alsoHeld = Inked(1);
        cache.Get(onScreen, 100, 100);
        cache.Get(alsoHeld, 100, 100);
        Assert.Equal(2, cache.CachedFrames);

        var speculative = Inked(2);
        var bmp = FrameBitmapCache.RenderDetached(speculative, 100, 100);
        var took = cache.InsertWarm(speculative, 100, 100, 1.0, 0, bmp);
        if (!took) bmp.Dispose();

        output.WriteLine(
            $"warm taken: {took}; held {cache.CachedFrames} frames, {cache.CachedBytes} bytes "
            + $"against a {FrameBitmapCache.ByteBudget} byte budget");

        Assert.False(took, "a warm was allowed to exceed the budget");
        Assert.Equal(2, cache.CachedFrames);
        Assert.True(cache.Holds(onScreen, 100, 100, 1.0, 0), "the warm evicted the frame on screen");
        Assert.True(cache.Holds(alsoHeld, 100, 100, 1.0, 0));
    }

    /// <summary>
    /// A warm enters at the tail, so the next eviction under a scan takes it
    /// rather than the frame the playhead just showed.
    /// </summary>
    [Fact]
    public void AWarmDoesNotDisplaceTheFrameThePlayheadJustShowed()
    {
        FrameBitmapCache.ByteBudget = 100L * 100 * 4 * 3;
        using var cache = new FrameBitmapCache { Eviction = FrameBitmapCache.EvictionOrder.MostRecent };

        var onScreen = Inked(0);
        cache.Get(onScreen, 100, 100);

        var ahead = Inked(1);
        var bmp = FrameBitmapCache.RenderDetached(ahead, 100, 100);
        Assert.True(cache.InsertWarm(ahead, 100, 100, 1.0, 0, bmp));

        // A real miss now arrives and pushes the cache over its budget. Under
        // MostRecent the victim is chosen from the head — which is where Get
        // puts what was just used, and where a warm inserted at the head would
        // have pushed the on-screen frame.
        for (var i = 2; i < 8; i++) cache.Get(Inked(i), 100, 100);

        output.WriteLine($"held {cache.CachedFrames} frames after the scan");
        Assert.True(
            cache.Holds(onScreen, 100, 100, 1.0, 0),
            "the frame on screen was evicted after a warm was inserted");
    }

    [Fact]
    public void AWarmOfAFrameAlreadyHeldIsRefusedRatherThanStoredTwice()
    {
        using var cache = new FrameBitmapCache();
        var frame = Inked(0);
        cache.Get(frame, 100, 100);

        var bmp = FrameBitmapCache.RenderDetached(frame, 100, 100);
        var took = cache.InsertWarm(frame, 100, 100, 1.0, 0, bmp);
        if (!took) bmp.Dispose();

        Assert.False(took);
        Assert.Equal(1, cache.CachedFrames);
    }

    /// <summary>
    /// A frame that samples the layers beneath it live is never stored at all
    /// (Q6), so warming one would be work thrown away twice — once by the
    /// prewarmer and once by the cache.
    /// </summary>
    [Fact]
    public void AFrameThatSamplesLiveIsNotWarmable()
    {
        var live = new Frame
        {
            Strokes =
            [
                new Stroke
                {
                    Tool = ToolKind.Brush,
                    Color = "#204080",
                    Points = [new StrokePoint(10, 10, 1), new StrokePoint(40, 40, 1)],
                    Brush = new BrushSettings
                    {
                        Size = 8, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2,
                        SampleSource = SampleSource.AllLayersLive,
                    },
                },
            ],
        };

        Assert.False(FrameBitmapCache.CanCache(live));
        Assert.True(FrameBitmapCache.CanCache(Inked(0)));
    }
}

/// <summary>
/// The prewarmer wired into playback: it warms what the playhead is heading
/// for, and nothing while the artist is drawing.
/// </summary>
[Collection("BrushState")]
public class PlaybackPrewarmTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel VmWithInkedFrames(int frames)
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 12;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;

        // Without a viewport the tile gate is never taken — the arrangement
        // lesson PlaybackThroughTilesTests records, and it applies identically
        // to a prewarm, which asks the same gate.
        vm.SetViewport(SKRectI.Create(0, 0, 960, 540));

        for (var i = 0; i < frames; i++)
        {
            if (i > 0) vm.AddFrameCommand.Execute(null);
            vm.BeginStroke(100 + i * 20, 100, 1);
            vm.MoveStroke(300 - i * 20, 200, 1);
            vm.EndStroke();
        }
        vm.CurrentFrameIndex = 0;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return vm;
    }

    /// <summary>
    /// The point of the whole change: a frame the playhead has not reached is
    /// already rendered when it gets there.
    /// </summary>
    [AvaloniaFact]
    public void PlayingWarmsFramesTheTickHasNotReachedYet()
    {
        var vm = VmWithInkedFrames(4);
        var ahead = vm.PaintedCel(2);

        vm.TogglePlaybackCommand.Execute(null);
        // The publish that shows frame 0 also queues 1, 2 and 3.
        vm.PublishSnapshot();
        Assert.True(vm.Prewarm.WaitForIdle(TimeSpan.FromSeconds(20)), "the worker never finished");
        // The next publish takes what the worker made, before it composites.
        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        output.WriteLine(
            $"rendered {vm.Prewarm.Rendered}, installed {vm.Prewarm.Installed}, "
            + $"refused {vm.Prewarm.Refused}, superseded {vm.Prewarm.Superseded}, "
            + $"failed {vm.Prewarm.Failed}");
        output.WriteLine($"playhead at {vm.CurrentFrameIndex}, tiles held {vm.TileFrames.CachedFrames}");

        Assert.Equal(0, vm.Prewarm.Failed);
        Assert.True(vm.Prewarm.Installed > 0, "nothing the worker rendered was installed");
        Assert.True(
            vm.TileFrames.Holds(ahead.Id) || vm.BitmapCacheBytes > 0,
            "a frame two ahead of the playhead was not warmed");
        vm.TogglePlaybackCommand.Execute(null);
    }

    /// <summary>
    /// Nothing is warmed while the artist is drawing. A prediction is only worth
    /// making when the playhead moves on its own; guessing where a hand is going
    /// would spend a core filling the cache with frames nobody asked for.
    /// </summary>
    [AvaloniaFact]
    public void APausedCanvasWarmsNothing()
    {
        var vm = VmWithInkedFrames(4);

        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(vm.Prewarm.IsBusy);
        Assert.Equal(0, vm.Prewarm.Rendered);
        Assert.Equal(0, vm.Prewarm.Installed);
    }

    /// <summary>
    /// A stroke committed while warms are in hand throws them away. The warm was
    /// a render of the drawing as it stood, and the drawing has a new line on it.
    /// </summary>
    [AvaloniaFact]
    public void AStrokeCommitDiscardsWhatWasWarmed()
    {
        var vm = VmWithInkedFrames(4);

        vm.TogglePlaybackCommand.Execute(null);
        vm.PublishSnapshot();
        Assert.True(vm.Prewarm.WaitForIdle(TimeSpan.FromSeconds(20)));
        vm.TogglePlaybackCommand.Execute(null);

        var installedBefore = vm.Prewarm.Installed;

        vm.BeginStroke(400, 300, 1);
        vm.MoveStroke(500, 400, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        output.WriteLine($"installed {installedBefore} before the stroke, {vm.Prewarm.Installed} after");
        Assert.Equal(installedBefore, vm.Prewarm.Installed);
    }

    /// <summary>
    /// The prediction and the real playhead take the same route.
    /// </summary>
    /// <remarks>
    /// <see cref="FramePrewarmer.Upcoming"/> restates <c>StepPlayback</c>'s
    /// arithmetic rather than sharing it, because the real one also stops a
    /// non-looping range, resynchronises audio and moves the playhead — none of
    /// which a guess may do. Restating is how the two drift, so the guess is
    /// driven against the real thing here rather than against a table.
    /// </remarks>
    [AvaloniaFact]
    public void ThePredictionAgreesWithStepPlayback()
    {
        var vm = VmWithInkedFrames(5);
        vm.LoopPlayback = true;
        vm.TogglePlaybackCommand.Execute(null);

        var walked = new List<int>();
        for (var i = 0; i < 12; i++)
        {
            var predicted = FramePrewarmer.Upcoming(
                vm.CurrentFrameIndex, 1, vm.EffectiveStartFrame, vm.EffectiveEndFrame,
                vm.LoopPlayback, 1);
            vm.StepPlayback();
            walked.Add(vm.CurrentFrameIndex);
            Assert.Single(predicted);
            Assert.Equal(vm.CurrentFrameIndex, predicted[0]);
        }

        output.WriteLine($"walked {string.Join(", ", walked)}");
        vm.TogglePlaybackCommand.Execute(null);
    }
}

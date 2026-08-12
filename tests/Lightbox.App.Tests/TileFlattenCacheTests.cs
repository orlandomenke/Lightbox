using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B167 phase 2: a layer that holds is flattened from its tiles once, not once
/// per playback frame.
/// </summary>
/// <remarks>
/// <para>
/// <b>The plan named the risk before the code existed, and it is staleness</b> —
/// a cached flatten surviving a change to the tiles under it. So the tests that
/// matter here are not the hit-rate ones: they are the two that try to serve
/// wrong pixels, plus the one that tries to free pixels somebody is still
/// reading.
/// </para>
/// <para>
/// The cache-level tests use bare bitmaps rather than a document, because what
/// is under test is the key and the lifetime, and a real flatten would drag a
/// whole scene in to prove something about a dictionary.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class TileFlattenCacheTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static SKBitmap Bitmap(int w = 64, int h = 64) =>
        new(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));

    private static readonly SKRectI Viewport = SKRectI.Create(0, 0, 64, 64);

    [Fact]
    public void TheSameTilesLevelAndRectangleAreServedFromMemory()
    {
        using var cache = new TileFlattenCache();
        var flat = Bitmap();

        Assert.True(cache.Insert("f1", stamp: 7, level: 0, Viewport, flat));

        Assert.Same(flat, cache.Get("f1", 7, 0, Viewport));
        Assert.Equal(1, cache.Hits);
    }

    /// <summary>
    /// <b>The staleness test the plan demanded.</b> A commit stamps a stroke into
    /// the tiles in place, so the frame id is unchanged and the pixels are not —
    /// which is exactly the case a frame-id key would serve wrongly. The stamp is
    /// what makes it a miss.
    /// </summary>
    [Fact]
    public void TilesThatChangedUnderneathAreNotServed()
    {
        using var cache = new TileFlattenCache();
        cache.Insert("f1", stamp: 7, level: 0, Viewport, Bitmap());

        // Same frame, same level, same rectangle — everything a weaker key would
        // have looked at — and different tiles.
        Assert.Null(cache.Get("f1", 8, 0, Viewport));
    }

    /// <summary>
    /// A different level or a different rectangle is different pixels, and both
    /// change under an ordinary zoom or pan.
    /// </summary>
    [Theory]
    [InlineData(1, 0, 0, 64, 64)]
    [InlineData(0, 16, 0, 64, 64)]
    [InlineData(0, 0, 0, 32, 64)]
    public void ADifferentLevelOrRectangleIsADifferentEntry(int level, int x, int y, int w, int h)
    {
        using var cache = new TileFlattenCache();
        cache.Insert("f1", stamp: 7, level: 0, Viewport, Bitmap());

        Assert.Null(cache.Get("f1", 7, level, SKRectI.Create(x, y, w, h)));
    }

    /// <summary>
    /// Tiles that are not held cannot have produced a flatten, and asking is
    /// answered without a lookup.
    /// </summary>
    [Fact]
    public void AFrameWithNoTilesIsNeverServedAndIsNeverStored()
    {
        using var cache = new TileFlattenCache();
        var flat = Bitmap();

        Assert.False(cache.Insert("f1", stamp: 0, level: 0, Viewport, flat));
        Assert.Null(cache.Get("f1", 0, 0, Viewport));

        flat.Dispose();
    }

    [Fact]
    public void ItStaysInsideItsByteBudget()
    {
        var previous = TileFlattenCache.ByteBudget;
        try
        {
            // Room for two 64×64 bitmaps and not a third.
            TileFlattenCache.ByteBudget = 64L * 64 * 4 * 2;
            using var cache = new TileFlattenCache();

            for (var i = 1; i <= 5; i++)
                cache.Insert($"f{i}", stamp: i, level: 0, Viewport, Bitmap());

            output.WriteLine($"{cache.CachedCount} entries, {cache.CachedBytes} bytes " +
                             $"against a {TileFlattenCache.ByteBudget} byte budget, " +
                             $"{cache.Evictions} thrown out");
            Assert.True(cache.CachedBytes <= TileFlattenCache.ByteBudget);
            Assert.True(cache.Evictions > 0);
        }
        finally
        {
            TileFlattenCache.ByteBudget = previous;
        }
    }

    /// <summary>
    /// <b>A flatten larger than the whole budget is refused rather than cached
    /// and instantly evicted</b>, and the caller then owns it. Without this the
    /// cache would evict everything on every publish at a large viewport, which
    /// is worse than not caching at all.
    /// </summary>
    [Fact]
    public void AFlattenBiggerThanTheBudgetIsRefused()
    {
        var previous = TileFlattenCache.ByteBudget;
        try
        {
            TileFlattenCache.ByteBudget = 1024;
            using var cache = new TileFlattenCache();
            var flat = Bitmap();

            Assert.False(cache.Insert("f1", stamp: 1, level: 0, Viewport, flat));
            Assert.Equal(0, cache.CachedCount);

            flat.Dispose();
        }
        finally
        {
            TileFlattenCache.ByteBudget = previous;
        }
    }

    /// <summary>
    /// <b>B130's shape, one cache down.</b> Before phase 2 a flattened bitmap was
    /// allocated per publish and owned by nobody, so eviction could not race a
    /// draw. A cached one is read by the render thread, and freeing it mid-draw
    /// is an access violation inside native code with an empty crash log.
    /// </summary>
    [Fact]
    public void APinnedFlattenSurvivesEvictionUntilItIsReleased()
    {
        var previous = TileFlattenCache.ByteBudget;
        try
        {
            TileFlattenCache.ByteBudget = 64L * 64 * 4;
            using var cache = new TileFlattenCache();

            var first = Bitmap();
            cache.Insert("f1", stamp: 1, level: 0, Viewport, first);
            cache.Pin(first);

            // Push it out.
            cache.Insert("f2", stamp: 2, level: 0, Viewport, Bitmap());
            cache.Insert("f3", stamp: 3, level: 0, Viewport, Bitmap());

            output.WriteLine($"evicted {cache.Evictions}, awaiting unpin {cache.AwaitingUnpinBytes} bytes");
            Assert.Null(cache.Get("f1", 1, 0, Viewport));
            // Handle rather than Width: reading a property off a disposed
            // SKBitmap dereferences freed native memory, so the obvious version
            // of this assertion segfaults the runner whether the code is right
            // or wrong. Handle is managed state and goes to zero on dispose.
            Assert.NotEqual(IntPtr.Zero, first.Handle);
            Assert.True(cache.AwaitingUnpinBytes > 0);

            cache.Unpin(first);
            Assert.Equal(IntPtr.Zero, first.Handle);
            Assert.Equal(0, cache.AwaitingUnpinBytes);
        }
        finally
        {
            TileFlattenCache.ByteBudget = previous;
        }
    }

    /// <summary>
    /// Counted rather than flagged, for the reason <c>FrameBitmapCache</c> gives:
    /// one bitmap can be in two live pass lists at once, and a flag would free it
    /// on the first release while the second reader was still going.
    /// </summary>
    [Fact]
    public void TwoReadersBothHaveToLetGo()
    {
        var previous = TileFlattenCache.ByteBudget;
        try
        {
            TileFlattenCache.ByteBudget = 64L * 64 * 4;
            using var cache = new TileFlattenCache();

            var first = Bitmap();
            cache.Insert("f1", stamp: 1, level: 0, Viewport, first);
            cache.Pin(first);
            cache.Pin(first);

            cache.Insert("f2", stamp: 2, level: 0, Viewport, Bitmap());
            cache.Insert("f3", stamp: 3, level: 0, Viewport, Bitmap());

            cache.Unpin(first);
            Assert.NotEqual(IntPtr.Zero, first.Handle);

            cache.Unpin(first);
            Assert.Equal(IntPtr.Zero, first.Handle);
        }
        finally
        {
            TileFlattenCache.ByteBudget = previous;
        }
    }

    // ---- the stamp, which is what makes any of the above safe ----------------

    /// <summary>
    /// <b>A commit stamps into the tiles in place</b> — so the frame id survives
    /// its pixels changing, and the stamp is what does not.
    /// </summary>
    [AvaloniaFact]
    public void CommittingAStrokeChangesTheFramesStamp()
    {
        var vm = TiledVm();
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(150, 100, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Appending stamps into tiles the cache already holds, and playback is
        // what builds them — one playing publish warms the frame's store.
        WarmTiles(vm);

        var frameId = FirstFrameId(vm);
        var before = vm.TileFrames.StampOf(frameId);

        vm.BeginStroke(200, 200, 1);
        vm.MoveStroke(250, 200, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var after = vm.TileFrames.StampOf(frameId);
        output.WriteLine($"stamp {before} -> {after}");
        Assert.NotEqual(before, after);
    }

    /// <summary>
    /// <b>And a rebuild after invalidation never reuses a stamp</b>, which is the
    /// failure a per-frame counter restarting at zero would have. It looks right
    /// and serves the pixels from before the change.
    /// </summary>
    [AvaloniaFact]
    public void AStampIsNeverIssuedTwice()
    {
        var vm = TiledVm();
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(150, 100, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        WarmTiles(vm);

        var frameId = FirstFrameId(vm);
        var seen = new HashSet<long> { vm.TileFrames.StampOf(frameId) };

        for (var i = 0; i < 4; i++)
        {
            vm.BeginStroke(200 + i * 20, 200, 1);
            vm.MoveStroke(240 + i * 20, 200, 1);
            vm.EndStroke();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(seen.Add(vm.TileFrames.StampOf(frameId)), "a stamp was issued twice");
        }

        output.WriteLine($"stamps: {string.Join(", ", seen)}");
    }

    // ---- and end to end, which is the only place the saving is real ----------

    /// <summary>
    /// <b>Redrawing the same frame flattens once.</b> This is the phase's whole
    /// claim, and the cache-level tests above cannot make it — they prove the
    /// dictionary works, not that the publish path asks it anything.
    /// </summary>
    [AvaloniaFact]
    public void RepublishingTheSameFrameDoesNotFlattenAgain()
    {
        var vm = TiledVm();
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(150, 100, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // The flatten cache is only asked on the tiled route, which is playback.
        vm.TogglePlaybackCommand.Execute(null);
        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var before = vm.TileFlats.Misses;
        for (var i = 0; i < 5; i++)
        {
            vm.PublishSnapshot();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        vm.TogglePlaybackCommand.Execute(null);

        output.WriteLine($"{vm.TileFlats.Hits} reused, {vm.TileFlats.Misses} flattened " +
                         $"({vm.TileFlats.Misses - before} of them after the stroke)");
        Assert.True(vm.TileFlats.Hits > 0, "no flatten was reused across five identical publishes");
        Assert.Equal(before, vm.TileFlats.Misses);
    }

    /// <summary>
    /// <b>And the pixels still follow the document.</b> A cache that never
    /// invalidates passes every test above and shows the artist their previous
    /// stroke forever, which is the failure worth spending a slow test on.
    /// </summary>
    [AvaloniaFact]
    public void AStrokeCommittedUnderACachedFlattenStillReachesTheScreen()
    {
        var vm = TiledVm();
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(150, 100, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Publish through the tiled route twice so the first stroke's flatten
        // is definitely cached.
        vm.TogglePlaybackCommand.Execute(null);
        vm.PublishSnapshot();
        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(vm.TileFlats.Hits > 0, "the first stroke's flatten was never cached");
        vm.TogglePlaybackCommand.Execute(null);

        // Draw while paused — the commit stamps into the tiles in place, which
        // is exactly the change a frame-id key would miss.
        vm.BeginStroke(300, 300, 1);
        vm.MoveStroke(350, 300, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Back through the tiled route, which must not serve the stale flatten.
        vm.TogglePlaybackCommand.Execute(null);
        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.TogglePlaybackCommand.Execute(null);

        Assert.NotNull(latest);
        using var composed = latest!.Materialise(null);
        using var bmp = SKBitmap.FromImage(composed);

        var first = bmp.GetPixel(125, 100).Red;
        var second = bmp.GetPixel(325, 300).Red;
        output.WriteLine($"first stroke {first}, second stroke {second}");

        Assert.True(first < 100, "the first stroke vanished");
        Assert.True(second < 100, "the second stroke never reached the screen — a stale flatten");
    }

    /// <summary>
    /// The frame the test's strokes landed on.
    /// </summary>
    /// <remarks>
    /// <b>Background layers are skipped, and leaving them in cost a confusing
    /// failure.</b> A paper layer holds a fill stroke, so "the first frame with
    /// strokes on it" is the paper — whose tiles never change, so the stamp
    /// correctly never moved and the test read that as the stamp being broken.
    /// A null cel is a hold rather than a frame, hence the filter.
    /// </remarks>
    /// <summary>One playing publish, so the tile cache holds the frame.</summary>
    private static void WarmTiles(MainViewModel vm)
    {
        vm.TogglePlaybackCommand.Execute(null);
        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.TogglePlaybackCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static string FirstFrameId(MainViewModel vm) =>
        vm.Doc.Scene.Layers
            .Where(l => !l.IsBackground)
            .SelectMany(l => l.Cels)
            .Select(c => c.Frame)
            .First(f => f is { Strokes.Count: > 0 })!.Id;

    /// <summary>
    /// A viewport is what the tile path needs and headless tests lack; the
    /// flatten cache itself is only consulted on tile-native publishes, which
    /// is playback — tests that need one toggle playback around the publish.
    /// </summary>
    private static MainViewModel TiledVm()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 12;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        vm.BrushWetEdge = 0;
        vm.BrushGranulation = 0;
        vm.BrushScatter = 0;
        vm.SetViewport(SKRectI.Create(0, 0, 960, 540));
        return vm;
    }
}

using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Lightbox.Raster;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// B392: a composite that fetches several cached bitmaps must not be able to
/// free the earlier ones with the later fetches.
/// </summary>
/// <remarks>
/// The owner's report: pressing play on an 11-layer, 64-drawing document froze
/// Lightbox and it died — twice in one morning, the first time taking unsaved
/// work with it. The dump put the UI thread inside the brush ring's colour
/// sample, composing the layer stack under the pointer from bitmaps the frame
/// cache had already freed. Playback switches the cache to evicting the most
/// recent entry but one, which is precisely the bitmap the previous fetch
/// returned, so on any document over the byte budget each layer freed the one
/// below it.
/// </remarks>
[Collection("FrameCacheBudget")]
public sealed class CacheFetchHoldTests : BrushStateIsolated
{
    private readonly long _budget = FrameBitmapCache.ByteBudget;

    public override void Dispose()
    {
        FrameBitmapCache.ByteBudget = _budget;
        base.Dispose();
    }

    private static Frame Inked(int seed) => new()
    {
        Strokes =
        [
            new Stroke
            {
                Tool = ToolKind.Brush,
                Color = "#204080",
                Points = [new StrokePoint(10 + seed, 10, 1), new StrokePoint(80 + seed, 80, 1)],
                Brush = new BrushSettings { Size = 30, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
            },
        ],
    };

    /// <summary>
    /// The hazard, measured, and the hold that closes it.
    /// </summary>
    /// <remarks>
    /// The first half is the control: it proves the budget here is small
    /// enough that the second fetch really does free the first, so the second
    /// half passing means the hold did something rather than that nothing was
    /// ever evicted.
    /// </remarks>
    [Fact]
    public void AFetchInsideAHoldOutlivesTheFetchThatEvictsIt()
    {
        FrameBitmapCache.ByteBudget = 200 * 200 * 4; // room for one frame
        using var cache = new FrameBitmapCache { Eviction = FrameBitmapCache.EvictionOrder.MostRecent };

        var unheld = cache.Get(Inked(0), 200, 200);
        cache.Get(Inked(1), 200, 200);
        Assert.True(unheld.Handle == IntPtr.Zero, "control: the second fetch should have freed the first");

        SkiaSharp.SKBitmap held;
        using (var hold = cache.HoldFetches())
        {
            held = cache.Get(Inked(2), 200, 200);
            cache.Get(Inked(3), 200, 200);
            Assert.True(held.Handle != IntPtr.Zero, "a held fetch was freed by the fetch after it");
            Assert.Equal(2, hold.Count);
        }

        // Released, and the eviction that was deferred has now happened.
        Assert.True(held.Handle == IntPtr.Zero, "the hold kept a bitmap the cache had already let go of");
        Assert.Equal(0, cache.PinnedCount);
        Assert.Equal(0, cache.AwaitingUnpinBytes);
    }

    /// <summary>
    /// Without a hold, fetching changes nothing about pinning — the hold is
    /// opt-in, and the drawing path's own pins are not doubled by it.
    /// </summary>
    [Fact]
    public void FetchingOutsideAHoldPinsNothing()
    {
        using var cache = new FrameBitmapCache();
        cache.Get(Inked(0), 100, 100);
        Assert.Equal(0, cache.PinnedCount);
    }

    /// <summary>
    /// The owner's crash, as it happened: playing, the pointer over the canvas,
    /// a document whose layers do not fit the cache.
    /// </summary>
    /// <remarks>
    /// Before the fix this threw <see cref="ObjectDisposedException"/> from the
    /// compositor's guard — and before the guard, it was a native access
    /// violation that took the test host down.
    /// </remarks>
    [AvaloniaFact]
    public void HoveringDuringPlaybackOnADocumentOverTheBudgetDoesNotComposeFreedLayers()
    {
        const int w = 400, h = 300;
        var doc = DocumentFactory.CreateDoc(w, h);
        doc.Scene.Layers.Clear();
        for (var i = 0; i < 5; i++)
        {
            doc.Scene.Layers.Add(new Layer { Name = $"L{i}", Cels = { new Cel { Frame = Inked(i * 20) } } });
        }

        // Before the document arrives: lowered afterwards, the first publish has
        // already cached every layer, a hit never evicts, and the test passes on
        // the broken build (it did, the first time this was written).
        FrameBitmapCache.ByteBudget = w * h * 4; // one layer fits, five do not
        var vm = new MainViewModel(artist: null);
        vm.ReplaceDocument(doc);
        vm.IsPlaying = true;
        Assert.Equal(FrameBitmapCache.EvictionOrder.MostRecent, vm.FrameCacheEviction);

        var ex = Record.Exception(() => vm.UpdatePointerContext(40, 40, KeyModifiers.None));
        Assert.Null(ex);
    }

    /// <summary>
    /// The same fetch loop behind the MCP <c>render_frame</c> tool, which an
    /// agent can call while the document is playing.
    /// </summary>
    /// <remarks>
    /// Found by the adversarial pass on the first draft of this fix, which held
    /// the hover route and left this one — four copies of one loop, and the fix
    /// reached one of them.
    /// </remarks>
    [AvaloniaFact]
    public void RenderingAFrameForAnAgentDuringPlaybackDoesNotComposeFreedLayers()
    {
        var vm = PlayingOverBudget();
        var ex = Record.Exception(() => vm.RenderFramePng(0));
        Assert.Null(ex);
    }

    private static MainViewModel PlayingOverBudget()
    {
        const int w = 400, h = 300;
        var doc = DocumentFactory.CreateDoc(w, h);
        doc.Scene.Layers.Clear();
        for (var i = 0; i < 5; i++)
        {
            doc.Scene.Layers.Add(new Layer { Name = $"L{i}", Cels = { new Cel { Frame = Inked(i * 20) } } });
        }
        FrameBitmapCache.ByteBudget = w * h * 4;
        var vm = new MainViewModel(artist: null);
        vm.ReplaceDocument(doc);
        vm.IsPlaying = true;
        return vm;
    }
}

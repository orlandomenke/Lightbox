using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The cursor's tip outline may be replaced in the cache and must not be freed
/// while a queued draw can still reach it (B348).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this guards is a use-after-free across two threads, and the only
/// part of it a test can hold is the lifetime.</b> <c>TipOutlinePath</c> hands
/// the live <see cref="SKPath"/> straight into a <c>DrawOp</c>, and Avalonia
/// runs that on the render thread some time after <c>Render</c> returned. The
/// cache used to <c>Dispose</c> the outgoing path the moment the tip id
/// changed, on the UI thread — so a draw already queued with it drew a freed
/// native path. <c>sk_canvas_draw_path</c> dereferenced the hole and the
/// process went.
/// </para>
/// <para>
/// <b>Nothing was logged, because there was nothing to log.</b> An access
/// violation is not a managed exception: the crash reporter's three hooks
/// cannot see it. Windows kept what the application could not — two Application
/// Error records, <c>0xC0000005</c> at the same fault offset on two different
/// builds, each naming <c>SkiaSharp.SkiaApi.sk_canvas_draw_path</c>. Reached by
/// holding E to erase and letting go, which puts the eraser's own size back and
/// changes the cursor's tip.
/// </para>
/// <para>
/// <b>The race itself is not reproducible here and this test does not pretend
/// to reproduce it.</b> It asserts the property that makes the race harmless:
/// a path this cache has handed out stays usable after the cache has moved on.
/// A test that tried to catch the timing would be a test that passes on a good
/// day, which is the failure mode this bug already demonstrated by needing two
/// sessions and a Windows event log to be seen at all.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class TheCursorOutlineOutlivesItsCacheTests : BrushStateIsolated
{
    /// <summary>Two registered tips, so the outline tracer has something to trace.</summary>
    private static void RegisterTheBuiltInTips() =>
        Lightbox.Raster.BrushTipRegistry.Register(
            Lightbox.Raster.Tips.TipCatalogue.All.ToDictionary(t => t.Id, t => t.Png));

    private const string Bristle = Lightbox.Raster.Tips.TipCatalogue.IdPrefix + "bristle";
    private const string Spatter = Lightbox.Raster.Tips.TipCatalogue.IdPrefix + "spatter";

    [AvaloniaFact]
    public void APathTheCacheHandedOutSurvivesTheNextTip()
    {
        RegisterTheBuiltInTips();
        var canvas = new CanvasControl();

        var handedOut = canvas.TipOutlinePath(Bristle);
        Assert.NotNull(handedOut);

        // The cursor moves to another tip — what releasing a held E does.
        canvas.TipOutlinePath(Spatter);

        // A queued DrawOp still holds the first one. Touching it must not throw:
        // a disposed SKPath refuses, and a freed one takes the process with it.
        Assert.False(handedOut!.IsEmpty, "the outline the cache handed out was freed underneath a draw that still held it");
    }

    [AvaloniaFact]
    public void APathTheCacheHandedOutSurvivesTheTipGoingAway()
    {
        RegisterTheBuiltInTips();
        var canvas = new CanvasControl();

        var handedOut = canvas.TipOutlinePath(Bristle);
        Assert.NotNull(handedOut);

        // Back to the round dab — no tip at all, the other way this invalidates.
        canvas.TipOutlinePath(null);

        Assert.False(handedOut!.IsEmpty, "the outline was freed when the cursor went back to the round dab");
    }
}

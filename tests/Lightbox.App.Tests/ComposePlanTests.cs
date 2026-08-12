using Lightbox.App.Rendering;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Which compositor a publish goes through, asserted on as arithmetic.
/// </summary>
/// <remarks>
/// <para>
/// <b>These three conditions were each learned by breaking them, and none of
/// them had a direct test.</b> They were checked only through whole-pipeline
/// tests that compose an image and measure it — which is how B121's rule came
/// to be wrong in the first place, at a cost of a **109× enlargement** of every
/// incremental repaint (1 232 px against 134 400 px for the same dab; 0.26 ms
/// against 76 ms on a 4K document).
/// </para>
/// <para>
/// The pipeline tests still matter and are not replaced: they prove the pixels
/// are right. These prove the *decision* is right, which is a different failure
/// and a much cheaper one to localise.
/// </para>
/// </remarks>
public class ComposePlanTests(ITestOutputHelper output)
{
    private const int DocW = 1920;
    private const int DocH = 1080;

    private static ComposePlan Plan(
        SKRectI? viewport = null, SKRectI? dirty = null, bool tileNative = false,
        SKSizeI? camera = null, double scale = 1.0, int w = DocW, int h = DocH) =>
        ComposePlan.For(w, h, camera, viewport, dirty, tileNative, scale);

    /// <summary>A viewport well inside the document, so culling is worth taking.</summary>
    private static SKRectI SmallViewport => SKRectI.Create(400, 300, 640, 360);

    /// <summary>
    /// <b>B121, the one that mattered.</b> The culled path builds a fresh surface
    /// and has to fill all of it — it cannot honour a dirty region the way the
    /// ring does. So culling an incremental publish turns a dab-sized repaint
    /// into a viewport-sized one, and a small dirty region is already
    /// area-independent, which means culling there can only ever lose.
    /// </summary>
    [Fact]
    public void AnIncrementalPublishNeverCulls()
    {
        var dab = SKRectI.Create(686, 386, 28, 28);

        var incremental = Plan(SmallViewport, dirty: dab);
        var whole = Plan(SmallViewport, dirty: null);

        output.WriteLine($"dirty={dab} -> {incremental.Route}; dirty=null -> {whole.Route}");
        Assert.Equal(ComposeRoute.Ring, incremental.Route);
        Assert.Null(incremental.CullRect);
        // And it still wins where it is supposed to: the publish that would
        // repaint everything anyway, which is the frame change B29 is about.
        Assert.Equal(ComposeRoute.ViewportCulled, whole.Route);
    }

    /// <summary>
    /// The incremental surface must stay the whole document, not the viewport —
    /// the ring patches a dirty region into a document-sized buffer, and a
    /// viewport-sized one would place every dab in the wrong place.
    /// </summary>
    [Fact]
    public void TheRingRouteGetsADocumentSizedSurface()
    {
        var plan = Plan(SmallViewport, dirty: SKRectI.Create(10, 10, 8, 8));

        Assert.Equal(DocW, plan.Info.Width);
        Assert.Equal(DocH, plan.Info.Height);
    }

    /// <summary>
    /// <b>The rectangle is clamped to the document.</b> A zoomed-out view reports
    /// a viewport far larger than the canvas — {-480,-450,1921,1440} for a
    /// 960×540 document at 50% — and an unclamped one is both a source rect off
    /// the end of the layer bitmap and a surface bigger than the full-document
    /// one it was meant to be cheaper than.
    /// </summary>
    [Fact]
    public void AZoomedOutViewportIsClampedRatherThanTakenLiterally()
    {
        var huge = new SKRectI(-480, -450, 1921, 1440);
        var plan = Plan(huge, w: 960, h: 540);

        output.WriteLine($"{huge} -> {plan.Route}, surface {plan.Info.Width}×{plan.Info.Height}");
        // Clamped, it is the whole document — which is not smaller, so there is
        // nothing to win and the ring keeps it.
        Assert.Equal(ComposeRoute.Ring, plan.Route);
        Assert.Equal(960, plan.Info.Width);
        Assert.Equal(540, plan.Info.Height);
    }

    /// <summary>
    /// Culling only pays when the clamped rectangle is actually smaller. A
    /// viewport covering the whole document is the ordinary fit-to-window case.
    /// </summary>
    [Fact]
    public void AViewportCoveringTheWholeDocumentDoesNotCull()
    {
        var plan = Plan(SKRectI.Create(0, 0, DocW, DocH));

        Assert.Equal(ComposeRoute.Ring, plan.Route);
        Assert.Null(plan.CullRect);
    }

    /// <summary>
    /// The culled surface covers exactly the clamped rectangle, at the compose
    /// scale — the painter draws it back into the same document rectangle, so a
    /// mismatch here lands the whole viewport in the wrong place.
    /// </summary>
    [Fact]
    public void TheCulledSurfaceIsTheClampedRectangleAtComposeScale()
    {
        var plan = Plan(SmallViewport, scale: 0.5);

        Assert.Equal(ComposeRoute.ViewportCulled, plan.Route);
        Assert.Equal(SmallViewport, plan.CullRect);
        Assert.Equal(320, plan.Info.Width);
        Assert.Equal(180, plan.Info.Height);
    }

    /// <summary>
    /// <b>A camera defers to the camera path.</b> The tiled compositor maps
    /// the viewport itself, and two things that both map the view disagree. The
    /// culled path is out for the same reason.
    /// </summary>
    [Fact]
    public void LookingThroughACameraTakesNeitherShortcut()
    {
        var camera = new SKSizeI(1280, 720);

        var withTiles = Plan(SmallViewport, tileNative: true, camera: camera);
        var wouldHaveCulled = Plan(SmallViewport, camera: camera);

        Assert.Equal(ComposeRoute.Ring, withTiles.Route);
        Assert.Equal(ComposeRoute.Ring, wouldHaveCulled.Route);
    }

    /// <summary>
    /// <b>The canvas is told the view size whatever the compositor did.</b> It
    /// derives its fit scale and its pointer mapping from these two numbers, so
    /// reporting a culled image's size moves the cursor off its mark —
    /// <c>CursorAlignmentTests</c> measures exactly how far.
    /// </summary>
    [Fact]
    public void TheReportedViewSizeIsTheDocumentEvenWhenCulled()
    {
        var culled = Plan(SmallViewport);

        Assert.Equal(ComposeRoute.ViewportCulled, culled.Route);
        Assert.Equal(DocW, culled.ViewWidth);
        Assert.Equal(DocH, culled.ViewHeight);
        // And the camera's output when there is one, since that is what the
        // finished image represents.
        var framed = Plan(SmallViewport, camera: new SKSizeI(1280, 720));
        Assert.Equal(1280, framed.ViewWidth);
        Assert.Equal(720, framed.ViewHeight);
    }

    /// <summary>
    /// Tile-native passes go to the compositor that can read them. The bounded
    /// one skips such a pass rather than dereferencing nothing, so sending it
    /// there loses a layer silently.
    /// </summary>
    [Fact]
    public void TileNativePassesRouteToTheTiledCompositor()
    {
        Assert.Equal(ComposeRoute.Tiled, Plan(SmallViewport, tileNative: true).Route);
        // Without a viewport there is nothing to cull against and the tiled
        // path has nothing to map, so it is out regardless of the passes.
        Assert.Equal(ComposeRoute.Ring, Plan(viewport: null, tileNative: true).Route);
    }

    /// <summary>
    /// A surface is never zero-sized. A viewport of a few pixels at a low
    /// compose scale rounds to nothing, and <c>SKSurface.Create</c> on a 0×0
    /// info returns null — which the compose paths throw on.
    /// </summary>
    [Fact]
    public void ASurfaceIsNeverEmptyHoweverSmallTheScale()
    {
        var plan = Plan(SKRectI.Create(0, 0, 2, 2), scale: 0.01);

        output.WriteLine($"{plan.Info.Width}×{plan.Info.Height}");
        Assert.True(plan.Info.Width >= 1 && plan.Info.Height >= 1);
    }

    /// <summary>
    /// A viewport entirely off the document clamps to nothing, which must fall
    /// back to the whole document rather than to an empty surface.
    /// </summary>
    [Fact]
    public void AViewportOffTheDocumentFallsBackToTheWholeThing()
    {
        var plan = Plan(SKRectI.Create(5000, 5000, 100, 100));

        Assert.Equal(ComposeRoute.Ring, plan.Route);
        Assert.Null(plan.CullRect);
        Assert.Equal(DocW, plan.Info.Width);
    }
}

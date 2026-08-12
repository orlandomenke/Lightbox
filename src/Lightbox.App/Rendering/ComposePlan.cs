using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>Which compositor a publish goes through.</summary>
internal enum ComposeRoute
{
    /// <summary>
    /// The full-document compositor, through <see cref="ComposeRing"/>, which is
    /// the only one of the three that can honour a dirty region.
    /// </summary>
    Ring,

    /// <summary>
    /// B82: a fresh surface covering only the clamped visible rectangle, so the
    /// cost is proportional to what the artist can see.
    /// </summary>
    ViewportCulled,

    /// <summary>The tiled compositor — the route a playing document takes.</summary>
    Tiled,
}

/// <summary>
/// Which compositor a publish should use, on what surface, covering what.
/// </summary>
/// <remarks>
/// <para>
/// <b>B166's second seam, and the reason it is worth its own type:</b> the three
/// routing conditions below were learned by breaking them, each carries a
/// measured cost in its comment, and none of them had a direct test. They were
/// checked only by whole-pipeline tests that compose an image and look at it —
/// which is how B121's rule came to be wrong in the first place, and it cost a
/// **109× enlargement** of every incremental repaint before anyone noticed.
/// </para>
/// <para>
/// It is arithmetic on six numbers, so it can be asserted on directly. That is
/// the entire argument for pulling it out.
/// </para>
/// <para>
/// <b>What is deliberately not here:</b> the compositing itself. The ring
/// rotates buffers and the tiled path reads tile caches — both hold state
/// with a lifetime, which is what keeps them in the view model for now.
/// </para>
/// </remarks>
/// <param name="Route">Which compositor to call.</param>
/// <param name="Info">The surface to compose into.</param>
/// <param name="CullRect">
/// The document rectangle a culled compose covers, or null on the other two
/// routes. Set only when <see cref="Route"/> is
/// <see cref="ComposeRoute.ViewportCulled"/>, so the two cannot disagree.
/// </param>
/// <param name="ViewWidth">
/// The document width the finished image represents — the camera's output when
/// looking through one, the scene otherwise. Reported to the canvas whatever the
/// compositor actually did, because the canvas derives its fit scale and its
/// pointer mapping from it: handing it a culled image's size moves the cursor
/// off its mark (<c>CursorAlignmentTests</c> measures how far).
/// </param>
/// <param name="ViewHeight">As <paramref name="ViewWidth"/>.</param>
/// <param name="ImageCovers">
/// Which document rectangle the finished image actually covers, or null for the
/// whole document. A property of the image rather than of what the canvas asked
/// for — the painter needs it to place the result, and
/// <see cref="SnapshotGeometry.ChangedInImageSpace"/> needs it to offset the
/// patch rectangle. Derived here because it is decided by the route: the raw
/// viewport when tiled, the clamped rectangle when culled, and nothing at
/// all when the ring composes the whole document.
/// </param>
internal readonly record struct ComposePlan(
    ComposeRoute Route,
    SKImageInfo Info,
    SKRectI? CullRect,
    int ViewWidth,
    int ViewHeight,
    SKRectI? ImageCovers)
{
    /// <summary>
    /// Decide the route and the surface.
    /// </summary>
    /// <param name="docWidth">Scene width.</param>
    /// <param name="docHeight">Scene height.</param>
    /// <param name="cameraOutput">
    /// The camera's output size when the view is looking through a camera, null
    /// otherwise. Null is the ordinary case and every asset document.
    /// </param>
    /// <param name="viewport">The rectangle the canvas last asked for, unclamped.</param>
    /// <param name="dirty">
    /// What changed since the last publish, or null for "everything" — which is
    /// what a frame change, a layer edit or a view change produces.
    /// </param>
    /// <param name="tileNative">
    /// Whether the pass list carries tile-native passes
    /// (<see cref="ScenePassBuilder.Result.TileNative"/>). Passed in rather than
    /// re-derived: a tile pass is only legible to the tiled compositor, and
    /// two copies of that condition is how they come to differ.
    /// </param>
    /// <param name="renderScale">The compose scale.</param>
    internal static ComposePlan For(
        int docWidth, int docHeight, SKSizeI? cameraOutput,
        SKRectI? viewport, SKRectI? dirty, bool tileNative, double renderScale)
    {
        // Looking through the camera reframes the canvas, so the surface is
        // the camera's output rather than the document. Without one — the
        // ordinary case and every asset document — nothing here changes.
        var viewWidth = cameraOutput?.Width ?? docWidth;
        var viewHeight = cameraOutput?.Height ?? docHeight;
        var haveViewport = viewport is { Width: > 0, Height: > 0 };

        // A camera defers to the camera path: the tiled compositor maps the
        // viewport itself, and two things that both map the view disagree.
        var tiled = tileNative && cameraOutput is null && haveViewport;

        // B82: compose only the visible rectangle, so the cost is proportional to
        // what the artist can see rather than to the whole document.
        //
        // Three conditions, every one of them learned by breaking it:
        //
        //  * The rectangle is CLAMPED to the document. A zoomed-out view reports a
        //    viewport far larger than the canvas — {-480,-450,1921,1440} for a
        //    960×540 document at 50% — and an unclamped rectangle is both a source
        //    rect off the end of the layer bitmap and a surface bigger than the
        //    full-document one it was meant to be cheaper than.
        //  * Culling only pays when the clamped rectangle is actually SMALLER.
        //  * **Only on a whole-canvas publish (B121).** This is the one that
        //    mattered. The culled path builds a fresh surface, so it has to fill
        //    all of it — it cannot honour a dirty region the way `ComposeRing`
        //    does. Culling an incremental publish therefore turns a dab-sized
        //    repaint into a viewport-sized one: measured at 1 232 px against
        //    134 400 px for the same dab, a 109× enlargement, and 0.26 ms against
        //    76 ms on a 4K document. Since a small dirty region is already
        //    area-independent, culling can only ever lose there. It wins on the
        //    publishes that would repaint everything anyway, which is exactly the
        //    frame change B29 is about.
        var clamped = ClampToDocument(viewport, viewWidth, viewHeight);
        var culled = !tiled
            && cameraOutput is null
            && dirty is null
            && clamped is { } vp
            && (long)vp.Width * vp.Height < (long)viewWidth * viewHeight;

        var (surfaceWidth, surfaceHeight) = culled && clamped is { } cull
            ? ((int)Math.Ceiling(cull.Width * renderScale), (int)Math.Ceiling(cull.Height * renderScale))
            : ((int)Math.Ceiling(viewWidth * renderScale), (int)Math.Ceiling(viewHeight * renderScale));

        var info = new SKImageInfo(
            Math.Max(1, surfaceWidth),
            Math.Max(1, surfaceHeight),
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        var route = tiled ? ComposeRoute.Tiled
            : culled ? ComposeRoute.ViewportCulled
            : ComposeRoute.Ring;

        // The tiled compositor maps the RAW viewport itself rather than the
        // clamped one — tiles exist wherever ink does, so there is no layer
        // bitmap edge to clamp against.
        var covers = route switch
        {
            ComposeRoute.Tiled => viewport,
            ComposeRoute.ViewportCulled => clamped,
            _ => null,
        };

        return new ComposePlan(
            route, info, culled ? clamped : null, viewWidth, viewHeight, covers);
    }

    /// <summary>
    /// The visible rectangle, clipped to the document. Null when nothing of the
    /// document is on screen, or when there is no viewport to speak of.
    /// </summary>
    internal static SKRectI? ClampToDocument(SKRectI? viewport, int docWidth, int docHeight)
    {
        if (viewport is not { } vp) return null;
        if (docWidth <= 0 || docHeight <= 0) return null;
        var left = Math.Clamp(vp.Left, 0, docWidth);
        var top = Math.Clamp(vp.Top, 0, docHeight);
        var right = Math.Clamp(vp.Right, 0, docWidth);
        var bottom = Math.Clamp(vp.Bottom, 0, docHeight);
        if (right - left <= 0 || bottom - top <= 0) return null;
        return new SKRectI(left, top, right, bottom);
    }
}

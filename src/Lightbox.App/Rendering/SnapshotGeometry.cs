using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Where a finished composite sits, and which part of it changed.
/// </summary>
/// <remarks>
/// <para>
/// <b>B166's third seam, and it is smaller than the entry implied — deliberately.</b>
/// The rest of the publish concern is the ring, the sequence number and the
/// event, and moving those buys nothing: they are stateful by nature, so they
/// would be exactly as hard to assert on in a new class as in the old one.
/// B125 stage 3 rewires that half anyway, which is the right time to move it.
/// </para>
/// <para>
/// <b>What is here is the part that is pure and was not tested:</b> the
/// arithmetic that tells the canvas which pixels to patch. Its own comment says
/// what being wrong costs — <i>"a wrong rectangle here would show stale pixels
/// rather than merely cost a repaint"</i> — and the only assertion that touched
/// it checked one null case, through a whole pipeline, incidentally, from a test
/// about something else.
/// </para>
/// </remarks>
internal static class SnapshotGeometry
{
    /// <summary>
    /// Convert the document rectangle a publish repainted into the image's own
    /// pixel space, for <see cref="PresentedFrame"/> to patch (B122).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two transforms and one refusal. The rectangle is offset by whatever the
    /// image covers — a culled image starts at the viewport's corner rather than
    /// the document's — and then scaled by <paramref name="renderScale"/>, since
    /// the surface may be smaller than the document. It is grown by a pixel on
    /// every side afterwards, because the composite's own edges are antialiased
    /// and a patch that is exact to the rectangle can leave a seam.
    /// </para>
    /// <para>
    /// The refusal is the important part: <b>under a camera this returns null</b>.
    /// A camera maps the document through an arbitrary matrix, so a document
    /// rectangle is not an axis-aligned image rectangle at all, and a wrong
    /// rectangle here would show stale pixels rather than merely cost a repaint.
    /// Null is always safe — it means "repaint everything" — so anything this
    /// function is not certain about must return it.
    /// </para>
    /// </remarks>
    internal static SKRectI? ChangedInImageSpace(
        SKRectI? changedInDoc, SKRectI? imageCovers, double renderScale, bool throughCamera)
    {
        if (throughCamera) return null;
        if (changedInDoc is not { } doc) return null;
        if (!double.IsFinite(renderScale) || renderScale <= 0) return null;

        var offsetX = imageCovers?.Left ?? 0;
        var offsetY = imageCovers?.Top ?? 0;
        var left = (int)Math.Floor((doc.Left - offsetX) * renderScale) - 1;
        var top = (int)Math.Floor((doc.Top - offsetY) * renderScale) - 1;
        var right = (int)Math.Ceiling((doc.Right - offsetX) * renderScale) + 1;
        var bottom = (int)Math.Ceiling((doc.Bottom - offsetY) * renderScale) + 1;
        if (right <= left || bottom <= top) return null;
        return new SKRectI(left, top, right, bottom);
    }
}

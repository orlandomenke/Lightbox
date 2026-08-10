using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Everything outside the layer stack that a composite depends on, so "nothing
/// changed" can be a decision rather than a hope.
/// </summary>
/// <remarks>
/// <para>
/// <b>B165's other half, and the one the render reports made urgent.</b> The
/// held-run fold saves blends inside a frame; this skips the frame. On 2s every
/// second playhead position exposes exactly the drawings the last one did, so the
/// composite would be identical pixel for pixel — and at 4K that composite
/// measured <b>62.76 ms against an 83.3 ms budget</b>, which is the largest single
/// cost in the tick.
/// </para>
/// <para>
/// It is also **route-independent**, which is why it matters more than it looks:
/// B125's GPU work only reaches the culled route, and playback takes the tiled
/// one. Skipping applies wherever the composite would have happened.
/// </para>
/// <para>
/// <b>The risk is stale pixels, which this codebase says in its own words is the
/// failure it is least able to notice.</b> Showing frame 4 while the playhead says
/// 5 produces no exception, no red test, and an artist who cannot tell it from
/// their own drawing. So the guard is a conjunction of everything a composite
/// reads, and every field here is one that has to match — the record exists so
/// that adding an input to compositing without adding it here is a visible
/// omission rather than an invisible one.
/// </para>
/// </remarks>
/// <param name="FrameIndex">Which playhead position produced the frame on screen.</param>
/// <param name="ComposeScale">
/// The resolution the canvas composited at. Changes with zoom and with the canvas
/// quality setting, and a frame composed at a different scale is a different
/// image however identical the drawings.
/// </param>
/// <param name="Viewport">
/// What the canvas asked to see. A pan moves this without touching a single
/// drawing, and reusing across it shows the previous view.
/// </param>
/// <param name="CameraFraming">
/// The camera transform, or null when there is none. A keyframed camera moves
/// every pixel while the drawings hold perfectly still — the one case where
/// "nothing changed" is most obviously true of the document and most obviously
/// false of the picture.
/// </param>
/// <param name="LayerCount">
/// How many layers were visible. A layer hidden or shown does not change any
/// drawing, and changes the composite completely.
/// </param>
internal readonly record struct FrameFingerprint(
    int FrameIndex,
    double ComposeScale,
    SKRectI? Viewport,
    SKMatrix44? CameraFraming,
    int LayerCount)
{
    /// <summary>
    /// Whether a composite for <paramref name="now"/> would be identical to the
    /// one already on screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately conservative in both directions that matter. It answers
    /// <c>false</c> whenever anything is unknown — no previous frame, a pending
    /// edit, a camera at all — because a wrong <c>true</c> shows stale art and a
    /// wrong <c>false</c> costs one composite.
    /// </para>
    /// <para>
    /// <b>A camera is refused outright rather than compared.</b> Comparing two
    /// <see cref="SKMatrix44"/> values would be correct for a still camera and
    /// exactly wrong for a keyframed one that happens to hold on two adjacent
    /// frames but is mid-move — and the cases are hard to tell apart from here.
    /// Refusing costs a composite on documents that have a camera at all, which
    /// is the ordinary case for none of them.
    /// </para>
    /// </remarks>
    internal static bool WouldBeIdentical(
        FrameFingerprint? previous, FrameFingerprint now, Scene scene, bool anythingDirty)
    {
        if (previous is not { } before) return false;
        if (anythingDirty) return false;
        if (before.CameraFraming is not null || now.CameraFraming is not null) return false;
        if (before.ComposeScale != now.ComposeScale) return false;
        if (before.Viewport != now.Viewport) return false;
        if (before.LayerCount != now.LayerCount) return false;

        // **The playhead must actually have moved.** A republish at the same frame
        // would compose identical pixels, so skipping it looks free — and it is
        // not. Nothing in playback republishes the same index (the tick advances
        // it, and B162's dropped frames advance it further), so the branch buys
        // nothing in production; what it does buy is refusing an explicit
        // "publish this now" from anywhere else, which is a reasonable thing to
        // ask and cheap to honour. Ten tests said so at once.
        if (before.FrameIndex == now.FrameIndex) return false;

        // Every visible layer exposes the same drawing it did. This is the whole
        // claim, and it is the exposure sheet's answer rather than a comparison
        // of pixels — a hold shows the same Frame object.
        return UnchangedLayerRun.NothingExposedChanged(scene, before.FrameIndex, now.FrameIndex);
    }
}

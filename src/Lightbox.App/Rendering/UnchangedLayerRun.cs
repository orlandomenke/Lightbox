using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// How much of the layer stack, counted from the bottom, shows the same drawing
/// at two playhead positions.
/// </summary>
/// <remarks>
/// <para>
/// <b>B165's sizing question, answered from the exposure sheet rather than from
/// pixels.</b> During playback the compositor blends every layer of every frame
/// from scratch — a five-frame, two-layer capture showed 1 138 layer passes over
/// 568 ticks. A held background, a colour-hold layer, a locked BG plate and a
/// static prop are all re-blended twelve times a second to produce pixels
/// identical to the ones already on screen.
/// </para>
/// <para>
/// <b>Whether a layer's pixels change between two playhead positions is a
/// property of the exposure sheet, not of the image.</b> A layer shows a
/// <see cref="Frame"/>; the playhead moving changes <em>which</em> frame, and a
/// hold means it changes nothing at all. So the run can be computed before
/// anything is composed, which is what makes the fix bounded work rather than a
/// comparison.
/// </para>
/// <para>
/// <b>Why the bottom run specifically, and not "which layers changed".</b>
/// Compositing is ordered and blends are not all associative: a Multiply layer
/// reads what is beneath it. So the only run that can be collapsed into one
/// cached bitmap is a <em>contiguous run from the bottom</em>, ending at the
/// first layer that moved. A static layer above a moving one saves nothing,
/// because what is beneath it changed.
/// </para>
/// <para>
/// <b>The foldability rules are <see cref="LayerStackBake"/>'s, deliberately.</b>
/// Correctness rests on premultiplied source-over being associative; a segment
/// containing a non-SrcOver blend cannot be pre-folded. Sharing the rule rather
/// than restating it is what keeps the two from disagreeing later.
/// </para>
/// </remarks>
internal static class UnchangedLayerRun
{
    /// <summary>
    /// How many layers from the bottom show the same drawing at
    /// <paramref name="toFrame"/> as at <paramref name="fromFrame"/>.
    /// </summary>
    /// <remarks>
    /// Counts <em>visible</em> layers, since invisible ones are never composited
    /// and so are neither a saving nor a barrier. Stops at the first layer whose
    /// drawing changed, or which cannot be folded at all.
    /// </remarks>
    internal static int Depth(Scene scene, int fromFrame, int toFrame)
    {
        var depth = 0;
        foreach (var layer in scene.Layers)
        {
            if (!scene.IsLayerVisible(layer)) continue;

            // A blend that reads what is beneath it cannot be pre-folded, so the
            // run ends here whether or not this layer's drawing moved.
            if (SceneRenderer.ToSkia(layer.BlendMode) != SKBlendMode.SrcOver) break;

            var before = ExposureSheet.ExposedFrame(layer, fromFrame);
            var after = ExposureSheet.ExposedFrame(layer, toFrame);
            // Reference equality is the right test and the cheap one: a hold
            // exposes the same Frame object, and two different drawings are two
            // objects even when they happen to look alike.
            if (!ReferenceEquals(before, after)) break;

            depth++;
        }
        return depth;
    }

    /// <summary>
    /// How many layers from the bottom show the same drawing at <em>every</em>
    /// position in a range.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The run that can actually be baked, as opposed to the one that happens
    /// to be unchanged right now.</b> <see cref="Depth"/> compares two adjacent
    /// frames, and on 2s its answer alternates — the whole stack on a hold, just
    /// the paper on a key. A bake keyed on that would be rebuilt every frame,
    /// which is the cost it exists to avoid; <see cref="LayerStackBake"/> requires
    /// a key to repeat before it pays for a bake precisely because of this.
    /// </para>
    /// <para>
    /// This is the durable prefix instead: the layers holding still for the whole
    /// range — the paper, a BG plate, a locked prop, a colour hold. Its key is the
    /// same on every frame of playback, so the bake is built once and reused.
    /// </para>
    /// <para>
    /// <b>It captures less than <see cref="BlendsOverRange"/> reports</b>, and the
    /// difference is worth naming rather than quietly ignoring: that figure
    /// includes the hold frames where nothing at all moved, which is a whole-frame
    /// reuse rather than a layer fold and needs its own mechanism. See
    /// <see cref="BakeableSaving"/> for what this half is worth on its own.
    /// </para>
    /// </remarks>
    internal static int HeldPrefix(Scene scene, int firstFrame, int lastFrame)
    {
        if (lastFrame < firstFrame) return 0;

        var depth = 0;
        foreach (var layer in scene.Layers)
        {
            if (!scene.IsLayerVisible(layer)) continue;
            if (SceneRenderer.ToSkia(layer.BlendMode) != SKBlendMode.SrcOver) break;

            var first = ExposureSheet.ExposedFrame(layer, firstFrame);
            var holds = true;
            for (var i = firstFrame + 1; i <= lastFrame; i++)
            {
                if (ReferenceEquals(ExposureSheet.ExposedFrame(layer, i), first)) continue;
                holds = false;
                break;
            }
            if (!holds) break;

            depth++;
        }
        return depth;
    }

    /// <summary>
    /// What folding the held prefix alone saves — the part a bake can actually
    /// deliver, as distinct from the ceiling <see cref="BlendsOverRange"/> reports.
    /// </summary>
    internal static (int Today, int Folded, double Saved) BakeableSaving(
        Scene scene, int firstFrame, int lastFrame)
    {
        var layers = VisibleLayers(scene);
        var frames = lastFrame - firstFrame;
        if (layers == 0 || frames <= 0) return (0, 0, 0);

        var prefix = HeldPrefix(scene, firstFrame, lastFrame);
        var today = layers * frames;
        var folded = prefix > 1 ? (layers - prefix + 1) * frames : today;
        return (today, folded, today == 0 ? 0 : 1.0 - ((double)folded / today));
    }

    /// <summary>
    /// How many visible layers the compositor would blend at a playhead position.
    /// </summary>
    internal static int VisibleLayers(Scene scene)
    {
        var n = 0;
        foreach (var layer in scene.Layers) if (scene.IsLayerVisible(layer)) n++;
        return n;
    }

    /// <summary>
    /// What caching the unchanged bottom run would save across a playback range,
    /// as a share of the layer blends performed today.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The arithmetic the fix is worth. Today every frame costs one blend per
    /// visible layer. With the run cached, a frame costs one blend for the cached
    /// run plus one per layer above it — so the saving on a transition of depth
    /// <c>d</c> is <c>d - 1</c> blends, and a depth of 0 or 1 saves nothing at
    /// all.
    /// </para>
    /// <para>
    /// <b>That "minus one" is why this is measured rather than assumed.</b> A
    /// two-layer scene with a held background has depth 1 and saves exactly
    /// nothing, which is the shape of the test scenes this repository already
    /// has — so a sweep over them would have reported no win and been right about
    /// the wrong document.
    /// </para>
    /// </remarks>
    internal static (int Today, int Cached, double Saved) BlendsOverRange(
        Scene scene, int firstFrame, int lastFrame)
    {
        var layers = VisibleLayers(scene);
        if (layers == 0 || lastFrame <= firstFrame) return (0, 0, 0);

        var today = 0;
        var cached = 0;
        for (var i = firstFrame + 1; i <= lastFrame; i++)
        {
            today += layers;
            var depth = Depth(scene, i - 1, i);
            cached += depth > 1 ? layers - depth + 1 : layers;
        }
        return (today, cached, today == 0 ? 0 : 1.0 - ((double)cached / today));
    }
}

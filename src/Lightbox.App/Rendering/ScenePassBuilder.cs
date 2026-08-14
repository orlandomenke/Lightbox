using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Turns the document, the playhead and whatever is being drawn right now into
/// the ordered list of passes a compositor blends. Data in, data out — no
/// surface, no window, no graphics context.
/// </summary>
/// <remarks>
/// <para>
/// <b>The first seam out of a 12,264-line view model (B166), and it is first
/// on purpose.</b> Pass building is the only part of the publish pipeline that
/// is a pure function: given a scene and the state below it produces a list,
/// and it can be asserted on without a surface, a window or a GPU. That is the
/// property none of the compositing code had, and it is why so much of
/// B156–B164 had to be diagnosed from field captures on the owner's machine
/// instead of from a test.
/// </para>
/// <para>
/// <b>It is also the thing B125 stage 3 hands across a thread.</b> Once the
/// canvas composites from passes inside the draw op, this list is the
/// interface between the view model and the render thread — so it wants to be
/// a named unit with its inputs written down, rather than 250 lines in the
/// middle of <c>PublishSnapshot</c>.
/// </para>
/// <para>
/// <b>What is deliberately NOT here:</b> the bake fold, the compose scale, the
/// ring and the publish sequencing. Those are the next two seams and they are
/// not pure — the fold caches, the ring rotates buffers, and publishing raises
/// an event. Keeping them out is what makes this one testable.
/// </para>
/// </remarks>
internal static class ScenePassBuilder
{
    /// <summary>
    /// The pass list, plus where the active layer's own contribution begins
    /// and ends in it.
    /// </summary>
    /// <remarks>
    /// The two indices are what <see cref="LayerStackBake"/> folds around: the
    /// layers that are not being drawn on collapse into two baked bitmaps, and
    /// the segment between these bounds is the part that must stay live. They
    /// are produced here rather than recomputed downstream because only the
    /// loop that builds the list knows where the boundaries fell — an empty
    /// cel, a transform preview and an ordinary layer each close the segment at
    /// a different point.
    /// </remarks>
    /// <param name="TileNative">
    /// Whether tile-carrying passes were built. Reported rather than recomputed
    /// by the caller **because the comment in <see cref="Build"/> is a
    /// requirement, not an observation**: a tile pass is only legible to the
    /// tiled compositor, so the decision to build one must equal the
    /// decision to use it, and a pass sent to the bounded path would silently
    /// vanish. Two copies of that condition is exactly how they come to differ.
    /// </param>
    internal readonly record struct Result(
        List<RenderPass> Passes, int ActiveStart, int ActiveEnd, bool TileNative);

    /// <summary>
    /// Everything the pass list depends on that is not the document itself.
    /// </summary>
    /// <remarks>
    /// Written out rather than reached for through a view model, because the
    /// list is a function of exactly these and naming them is most of the value
    /// of the extraction. If a pass changes and none of these did, that is a
    /// bug rather than a setting.
    /// </remarks>
    /// <param name="ActiveLayerId">
    /// The layer being drawn on, or null when there is none. **Null is a real
    /// state, not defensive padding**: a document can have no layers at all, and
    /// the inline version only survived that by accident — it asked for the
    /// active layer inside the loop, which a layerless scene never entered. Once
    /// the question is asked up front it has to have an answer, and "nothing is
    /// active" is it.
    /// </param>
    internal readonly record struct State(
        int FrameIndex,
        string? ActiveLayerId,
        bool IsPlaying,
        bool IsLightTable,
        bool HaveViewport,
        OnionSettings Onion);

    /// <summary>
    /// The moving and staying halves of a frame under a live transform, as the
    /// builder needs to see them.
    /// </summary>
    /// <remarks>
    /// A view of the session's own <c>TransformSession.Parts</c>, not a copy of the
    /// bookkeeping: building those caches bitmaps and owns their disposal,
    /// which is state with a lifetime and therefore not something a pure
    /// function should hold. The view model keeps that and passes a resolver.
    /// </remarks>
    internal readonly record struct TransformSplit(SKBitmap Moving, SKBitmap? Static);

    /// <summary>
    /// The edit in flight: the scratch surfaces, the stroke being drawn and the
    /// transform being dragged.
    /// </summary>
    /// <remarks>
    /// All of it is null or empty on the common path — a publish with nothing
    /// under the pen — and <see cref="None"/> is that case named, which is what
    /// most tests want.
    /// </remarks>
    internal readonly record struct LiveEdit(
        SKBitmap? Composite = null,
        SKBitmap? Scratch = null,
        SKBitmap? PostScratch = null,
        int PostStampedCount = -1,
        Stroke? Shape = null,
        Stroke? Gradient = null,
        Stroke? BrushStroke = null,
        SKMatrix? TransformPreview = null,
        IReadOnlyList<Frame>? TransformFrames = null,
        Func<Frame, TransformSplit?>? PartsFor = null)
    {
        internal static readonly LiveEdit None = new();
    }

    /// <summary>
    /// Build the pass list for one publish.
    /// </summary>
    internal static Result Build(
        Scene scene, State state, FrameBitmapCache cache, TileFallbackTally tileFallbacks,
        LiveEdit live)
    {
        var passes = new List<RenderPass>();

        // Tile-native passes are only legible to the tiled compositor, so
        // the decision to build them must equal the decision to use it — a
        // publish with no viewport yet, or with a camera authored, takes the
        // bounded path, and a tile pass sent there would silently vanish.
        //
        // Playback is what turns tiles on (B144/Q62): while frames are
        // flipping, a cel costs its ink rather than its paper, so a
        // scene whose full-frame bitmaps thrash the cache above 720p stays
        // resident as tiles — measured at 145 → 14 ms a frame at 1080p on
        // sparse cels. The line is drawn at motion on purpose: a paused
        // publish returns to the bounded compositor, so the still picture is
        // always today's canonical render, and any resample difference the
        // pyramid introduces below 100% zoom exists only while the sequence
        // is moving. No live stroke, transform or ghost pass exists during
        // playback, which is what keeps the two compositors' remaining
        // differences (live clip masks, bake folding) out of reach.
        // Whether tiles are on the table at all. The two *document* conditions —
        // a camera, and a viewport to cull against — are asked through
        // TileFallback so a report can name them; the mode check stays here
        // because "not playing" is a choice rather than a frame the tiles
        // could not serve.
        var tileModeOn = state.IsPlaying;
        var tileNativeDoc = tileModeOn && scene.Camera is null && state.HaveViewport;

        // Where the active layer's contribution begins and ends in the pass
        // list, so the layers that are NOT being drawn on can be folded into
        // two baked bitmaps below. The active layer's own ghosts belong to its
        // segment — they change with the playhead and the onion settings, and
        // a bake that had to watch them would rebuild on exactly the publishes
        // it exists to make cheap.
        var activeStart = -1;
        var activeEnd = -1;

        var referencesQueued = false;
        foreach (var layer in scene.Layers)
        {
            if (!scene.IsLayerVisible(layer)) continue;
            var isActive = layer.Id == state.ActiveLayerId;

            // An imported reference goes over the paper and under every
            // drawing — the same place as the photograph you would tape to the
            // lightbox. Over the paper because the paper is opaque and would
            // hide it; under the drawings because it is what you are drawing
            // against, not something you are drawing on top of.
            if (!referencesQueued && !layer.IsBackground)
            {
                passes.AddRange(ReferencePasses(scene, state));
                referencesQueued = true;
            }

            // After the references block on purpose: a reference is part of
            // what is beneath the drawing even when the active layer is the
            // first one over the paper.
            if (isActive) activeStart = passes.Count;

            // Ghosts go directly beneath the layer they belong to, not beneath
            // the whole stack. Queuing them all first was invisible while every
            // layer was transparent; the moment a document opened on opaque
            // paper, the paper painted over every ghost. Interleaving is also
            // what makes multi-layer onion read correctly — a layer's ghosts
            // sit under it, exactly as its own earlier frames would.
            var ghosts = GhostPassesFor(layer, scene, state, cache);
            if (!state.Onion.DrawOver) passes.AddRange(ghosts);

            var frame = ExposureSheet.ExposedFrame(layer, state.FrameIndex);
            if (frame is null)
            {
                // An empty cel is exactly when onion skin earns its keep: you
                // are looking at the gap you are about to draw the inbetween
                // into. The ghosts still show — there is simply no drawing of
                // this layer's own to put them under or over.
                if (state.Onion.DrawOver) passes.AddRange(ghosts);
                if (isActive) activeEnd = passes.Count;
                continue;
            }

            // A playing document holds tileable frames as tiles, so the
            // pass carries the FRAME and the compositor reads the tile cache —
            // fetching the bitmap here would materialise the document-sized
            // allocation the tile store exists to avoid. A frame the tiles
            // cannot say (baseline pixels, placements, an effect stroke — see
            // TileFrameCache.CanTileFrame) falls back to the bitmap path, as
            // does the active layer while a live blur/smudge is replacing it.
            Frame? tileFrame = null;
            SKBitmap? bmp = null;
            var liveEffectHere = live.Composite is not null
                && live.BrushStroke is not null && isActive;
            // One expression decides and explains (B144). While the tile mode is
            // on, every frame it looks at is tallied — so a render report can say
            // *why* a document is paying the old cost instead of leaving "it is
            // slow" and "it never tiled" indistinguishable.
            if (tileModeOn)
            {
                var why = TileFallback.Reason(
                    frame, scene.Camera is not null, state.HaveViewport, liveEffectHere);
                tileFallbacks.Note(why);
                if (why == TileFallbackReason.None) tileFrame = frame;
            }

            if (tileFrame is null)
            {
                bmp = cache.Get(frame, scene.Width, scene.Height, celIndex: state.FrameIndex);
            }

            // Blur and smudge REPLACE the layer rather than overlaying it.
            //
            // They rework pixels that are already there, so there is no set of
            // new marks to lay on top — the answer is the whole layer, redone.
            // BeginStroke already copies the layer into _liveComposite for
            // exactly this, and FlushLivePreview has been appending each drag
            // segment to it every event; it simply was never shown, so the
            // smear only appeared when the pen lifted and the commit landed.
            if (liveEffectHere)
            {
                bmp = live.Composite;
            }

            var overlay = OverlayFor(live, isActive);

            // A transform in progress: show the drag, not just the box around
            // it. The strokes that move are drawn through the gizmo's matrix
            // and the ones that stay (a region-limited transform) are drawn
            // where they are, which is exactly the split the commit makes.
            if (live.TransformPreview is { } preview
                && Moving(live.TransformFrames, frame)
                && live.PartsFor?.Invoke(frame) is { } parts)
            {
                if (parts.Static is { } stay)
                {
                    passes.Add(new RenderPass(
                        stay, null, layer.Opacity, SceneRenderer.ToSkia(layer.BlendMode)));
                }
                passes.Add(new RenderPass(
                    parts.Moving, null, layer.Opacity, SceneRenderer.ToSkia(layer.BlendMode),
                    overlay, preview));
                if (state.Onion.DrawOver) passes.AddRange(ghosts);
                if (isActive) activeEnd = passes.Count;
                continue;
            }

            // A light table makes the sheet you are drawing on the crisp one and
            // the sheets under it faint. Untinted, because they are the same
            // drawing seen through paper, not a different moment in time.
            //
            // The paper is exempt: it is the desk the sheets lie on, not one of
            // them. Dimming it would punch the checkerboard through an opaque
            // document the moment the mode was switched on.
            var opacity = state.IsLightTable && !state.IsPlaying
                && !layer.IsBackground && !isActive
                ? layer.Opacity * state.Onion.Opacity
                : layer.Opacity;
            passes.Add(new RenderPass(
                bmp, null, opacity, SceneRenderer.ToSkia(layer.BlendMode), overlay,
                SourceFrame: tileFrame));

            // Draw-over puts them above instead. Under is how a lightbox works
            // and is what you want while drawing; over is for checking, when a
            // line you have just made would otherwise hide the one you are
            // comparing it to.
            if (state.Onion.DrawOver) passes.AddRange(ghosts);
            if (isActive) activeEnd = passes.Count;
        }

        // A document with nothing but paper in it still shows its reference —
        // that is the state you are in when you have imported one and have not
        // drawn anything yet, which is every time you start.
        if (!referencesQueued) passes.AddRange(ReferencePasses(scene, state));

        return new Result(passes, activeStart, activeEnd, tileNativeDoc);
    }

    /// <summary>
    /// Whether this frame is one the live transform is dragging. Indexed rather
    /// than LINQ: it is asked once per visible layer per publish, which is a
    /// per-pointer-event path during a drag.
    /// </summary>
    private static bool Moving(IReadOnlyList<Frame>? frames, Frame frame)
    {
        if (frames is null) return false;
        for (var i = 0; i < frames.Count; i++) if (frames[i].Id == frame.Id) return true;
        return false;
    }

    /// <summary>
    /// The overlay riding on the active layer: the dabs, the shape drag or the
    /// gradient drag, whichever is in flight.
    /// </summary>
    /// <remarks>
    /// Live stroke: the dabs live in their own scratch and composite over the
    /// layer here. The layer bitmap is never copied for a preview — a
    /// full-canvas copy costs ~1 s at 4K.
    /// <para>
    /// Skipped entirely once <c>_liveComposite</c> has taken over this layer
    /// (B39): a blur or smudge REPLACES the layer rather than overlaying it, so
    /// the bitmap is already the whole answer. If the scratch still held dabs
    /// from whatever ordinary stroke ran immediately before — BeginStroke
    /// clears it for every tool except Blur/Smudge, since those never draw into
    /// it — building an overlay from it here composited that stale content a
    /// SECOND time over the composite, which already carried it once. Over a
    /// wash the two SrcOver passes measured 61 → 108, and a harder edge reaches
    /// fully opaque: the hard-edged black band and the "smaller black dash" the
    /// report showed are exactly the shape of dab patches left over from the
    /// previous stroke.
    /// </para>
    /// </remarks>
    private static StrokeOverlay? OverlayFor(LiveEdit live, bool isActive)
    {
        if (live.Composite is not null || live.Scratch is null || !isActive) return null;

        // The shape tool's drag preview. It was rendering into the scratch
        // and never being shown: the overlay only knew about a gradient
        // drag or a live brush stroke, and a shape is neither — so the
        // rectangle appeared out of nowhere on release. Same shape of
        // overlay as the gradient's, for the same reason.
        if (live.Shape is { } shaping)
        {
            return new StrokeOverlay(
                live.Scratch,
                shaping.Brush.Opacity,
                shaping.Tool == ToolKind.Eraser,
                shaping.AlphaLocked,
                shaping.ClipId is null ? null : ClipRegionRegistry.Resolve(shaping.ClipId));
        }

        // The gradient tool's drag preview. Opacity and the alpha lock
        // ride on the overlay, exactly as they do for a brush stroke,
        // so the preview and the commit agree.
        if (live.Gradient is { } drag)
        {
            return new StrokeOverlay(
                live.Scratch,
                drag.Brush.Opacity,
                false,
                drag.AlphaLocked,
                drag.ClipId is null ? null : ClipRegionRegistry.Resolve(drag.ClipId));
        }

        if (live.BrushStroke is { } stroke)
        {
            // Prefer the fully rendered stroke when a pass has completed —
            // medium, wet edge and texture included — and fall back to raw
            // dabs only for the first few events of a heavy brush, before
            // the first pass lands.
            var source = live.PostStampedCount > 0 && live.PostScratch is not null
                ? live.PostScratch
                : live.Scratch;
            // Everything the commit will mask the stroke with, applied
            // now: an artist cannot judge a mark they are not being shown.
            return new StrokeOverlay(
                source,
                stroke.Brush.Opacity,
                stroke.Tool == ToolKind.Eraser,
                stroke.AlphaLocked,
                stroke.ClipId is null ? null : ClipRegionRegistry.Resolve(stroke.ClipId));
        }

        return null;
    }

    /// <summary>
    /// The ghost passes for one layer: the drawings around the playhead, the
    /// frames pinned as ghosts, or — in light-table mode — nothing, because
    /// that mode ghosts other layers rather than other frames.
    /// </summary>
    internal static IReadOnlyList<RenderPass> GhostPassesFor(
        Layer layer, Scene scene, State state, FrameBitmapCache cache)
    {
        var onion = state.Onion;
        // Ghosts are a drawing aid. During playback they are noise, and the
        // one thing playback has to show is the animation.
        //
        // The empty answers return a shared empty array rather than a fresh
        // list: this is asked once per visible layer per publish, and a publish
        // happens per pointer event during a stroke, so an unused List per
        // layer per event is a real allocation on the drawing path.
        if (!onion.Enabled || state.IsPlaying || !layer.OnionEnabled) return [];

        var previous = SceneRenderer.ParseTint(onion.PreviousTint, SceneRenderer.OnionPrevTint);
        var next = SceneRenderer.ParseTint(onion.NextTint, SceneRenderer.OnionNextTint);

        if (onion.Mode == OnionMode.LightTable)
        {
            // A light table shows the sheets under this one, not this sheet's
            // own history. The other layers are already composited in their
            // own right, so there is nothing to add here — the mode's effect
            // is that the time-based ghosts are absent.
            return [];
        }

        var passes = new List<RenderPass>();

        // Pinned first, so they sit furthest back: they are the reference the
        // near ghosts and the current drawing are being placed against.
        foreach (var index in PinnedGhostIndices(scene))
        {
            if (index == state.FrameIndex) continue;
            if (ExposureSheet.ExposedFrame(layer, index) is not { } pinned) continue;
            passes.Add(new RenderPass(
                cache.Get(pinned, scene.Width, scene.Height, celIndex: index),
                index < state.FrameIndex ? previous : next,
                onion.Opacity));
        }

        // Furthest first so the nearest ghost ends up on top of the others,
        // which is the order their opacities assume.
        var around = OnionSkin.Ghosts(
            layer, state.FrameIndex, onion.Before, onion.After, onion.KeysOnly);
        foreach (var ghost in around.OrderByDescending(g => g.Steps))
        {
            passes.Add(new RenderPass(
                // The ghost's own position, not the playhead's: a rig-bound
                // ghost must show the pose it holds where it lives.
                cache.Get(ghost.Frame, scene.Width, scene.Height, celIndex: ghost.Index),
                ghost.Before ? previous : next,
                OnionSkin.OpacityAt(ghost.Steps, onion.Opacity, onion.Falloff)));
        }
        return passes;
    }

    private static IReadOnlyList<int> PinnedGhostIndices(Scene scene) =>
        scene.GhostFrames is { Count: > 0 } pinned ? pinned : [];

    /// <summary>
    /// The reference cells showing at the playhead — usually none, sometimes
    /// one, more only if several sheets are loaded at once.
    /// </summary>
    /// <remarks>
    /// Cheap on the ordinary path: a document with no references returns an
    /// empty list without touching the registry, and one with references does
    /// a dictionary lookup and builds a translate matrix. Nothing is decoded,
    /// copied or scaled here — the cell is cut out of the sheet by the
    /// compositor, which is doing a blit either way.
    /// </remarks>
    internal static IReadOnlyList<RenderPass> ReferencePasses(Scene scene, State state)
    {
        // The overwhelmingly common case — no references — costs nothing, which
        // matters because this runs on every publish of every document.
        if (scene.References is not { Count: > 0 } strips) return [];

        var passes = new List<RenderPass>();
        foreach (var strip in strips)
        {
            if (!strip.Visible || strip.Opacity <= 0) continue;
            if (strip.CellAt(state.FrameIndex) is not { } cell) continue;
            if (ReferenceStripRegistry.Resolve(strip.Id) is not { } sheet) continue;

            var scale = (float)Math.Max(0.01, strip.Scale);
            var matrix = SKMatrix.CreateScaleTranslation(
                scale, scale,
                (float)(strip.OffsetX + cell.Dx),
                (float)(strip.OffsetY + cell.Dy));
            passes.Add(new RenderPass(
                sheet, null, strip.Opacity, SKBlendMode.SrcOver, null, matrix,
                SKRectI.Create(cell.X, cell.Y, cell.Width, cell.Height)));
        }
        return passes;
    }
}

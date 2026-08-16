using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Folds the render passes that are not the active layer into two baked
/// bitmaps — everything beneath it and everything above it — so a repaint
/// while drawing blends three passes instead of one per layer, and the cels
/// under a valid bake are never even fetched from the frame cache.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The bench harness measured a whole-frame
/// recomposite at n^0.99 in the layer count — every publish pays one blend per
/// visible layer, and a stroke commit publishes the whole canvas. An artist
/// edits one layer at a time; the other N−1 layers are the same drawings with
/// the same opacities publish after publish, and re-blending them is the
/// definition of work that can be done once.
/// </para>
/// <para>
/// <b>The key is the described pass list, not the fetched bitmaps — and that
/// difference is B198's whole wall.</b> The first version of this class keyed
/// on bitmap identity plus content version, which meant every layer's cel had
/// to be fetched from <see cref="FrameBitmapCache"/> before the key could even
/// be compared. On a document whose single-frame working set exceeds the cache
/// budget (about 64 layers at 1080p, ~830 MB against 512), every fetch
/// re-rasterized a cel the previous publish had just evicted — 7 seconds per
/// recomposite at 100 layers — and the fold never engaged at all, because an
/// eviction hands back a fresh instance and an identity key never repeats.
/// Keying on <see cref="ScenePassBuilder.PassSpec"/>s instead — frame
/// <em>references</em> from the exposure sheet, opacities, blends, tints —
/// costs no fetch to compare, and a served segment skips the fetches entirely:
/// two held surfaces plus the active cel need ~25 MB resident, not 830.
/// </para>
/// <para>
/// <b>What a spec key cannot see is an in-place edit, so invalidation is
/// load-bearing rather than defensive.</b> A frame compares by reference and
/// an edit mutates the frame behind the reference. Every frame mutation in the
/// view model funnels through <c>InvalidateFrameRender</c>, and that funnel
/// now calls <see cref="NoteFrameChanged"/>: a bake covering the edited frame
/// is dropped before the next publish can serve it. The bitmap-version half of
/// the old key is gone because on a served publish there is no bitmap to read
/// a version from — which is exactly why the funnel must not grow bypasses.
/// A mutation path that skips <c>InvalidateFrameRender</c> shows stale art
/// here, silently; the guarding test is
/// <c>AnEditUnderTheBakeThatNeverTouchesTheActiveLayerShowsUp</c>.
/// </para>
/// <para>
/// <b>Correctness rests on associativity, so only SrcOver folds.</b>
/// Pre-folding a run of layers into one bitmap and drawing that over what is
/// beneath gives the same pixels as drawing them one by one <i>only</i>
/// because premultiplied source-over is associative. A Multiply layer reads
/// the pixels beneath it, so a segment containing any non-SrcOver blend, a
/// live overlay, or a tile-native pass refuses to fold and composites exactly
/// as before. Layers default to SrcOver, so the common document folds; the
/// exotic one stays correct.
/// </para>
/// <para>
/// <b>Folding waits for the second consecutive publish with the same key.</b>
/// A fold costs a full-canvas composite, which is roughly the recomposite it
/// saves — paying it on a key seen once would make playback and scrubbing
/// slower, since every frame change is a new key. Seen twice means the stack
/// is holding still (the artist is drawing), and every publish from the third
/// onward blends three passes. The caller also skips folding outright during
/// playback, where the not-being-drawn-on segment's key never repeats;
/// <see cref="FoldHeldRun"/> is the playback counterpart, folding the prefix
/// that holds still across the whole range.
/// </para>
/// <para>
/// <b>A retired bake is disposed through <see cref="RetireBitmap"/>, never
/// directly.</b> A published snapshot on the deferred route carries its passes
/// to the render thread, and a baked bitmap rides in those passes — so the UI
/// thread disposing one at rebuild time is B130's crash with a different
/// owner. The view model routes retirement through the frame cache's
/// pin-aware deferral, which frees the bitmap at the last unpin. Null means
/// dispose immediately, which is right for tests that never publish.
/// </para>
/// <para>
/// Owned by the UI thread, like the frame cache and the compose ring.
/// The baked bitmaps are document-resolution, the same as the layer bitmaps
/// they replace, so the culled and camera paths treat them identically.
/// </para>
/// </remarks>
public sealed class LayerStackBake : IDisposable
{
    private readonly record struct PassKey(
        SKBitmap? Bitmap, long Version, SKColor? Tint, double Opacity, SKBlendMode Blend,
        SKMatrix? Matrix, SKRectI? Source);

    private sealed class Segment
    {
        public List<ScenePassBuilder.PassSpec> Key = [];
        public SKBitmap? Baked;
        public int Width;
        public int Height;

        /// <summary>
        /// The ids of every frame whose pixels are inside the bake — the set
        /// <see cref="NoteFrameChanged"/> checks. Ghost frames included; they
        /// are pixels in the bake like any other.
        /// </summary>
        public readonly HashSet<string> Covered = [];
    }

    private sealed class HeldSegment
    {
        public List<PassKey> Key = [];
        public SKBitmap? Baked;
    }

    private readonly Segment _below = new();
    private readonly Segment _above = new();
    private readonly HeldSegment _held = new();
    private List<PassKey>? _pendingHeld;
    private bool _servedHeld;
    private List<ScenePassBuilder.PassSpec>? _pendingBelow;
    private List<ScenePassBuilder.PassSpec>? _pendingAbove;
    private bool _servedBelow;
    private bool _servedAbove;

    /// <summary>
    /// Test seam: false composites every pass exactly as before this class
    /// existed. The pixel-identity tests need an unfolded reference render,
    /// and a switch is honest where a re-implementation of the old loop in a
    /// test would drift.
    /// </summary>
    internal bool Enabled { get; set; } = true;

    /// <summary>
    /// How a retired baked bitmap is freed. The view model points this at the
    /// frame cache's pin-aware deferral so a bake still riding in a published
    /// pass list is not pulled out from under the render thread; null disposes
    /// immediately. See the class remarks.
    /// </summary>
    internal Action<SKBitmap>? RetireBitmap { get; set; }

    /// <summary>How many publishes were served from the bakes, for diagnostics.</summary>
    public long FoldedPublishes { get; private set; }

    /// <summary>How many times the bakes were (re)built, for diagnostics.</summary>
    public long Rebuilds { get; private set; }

    /// <summary>
    /// How many cel fetches were skipped because a valid bake covered them —
    /// the B198 number: every one is a cache lookup that can no longer miss,
    /// and on a thrashing document a miss is a full re-rasterization.
    /// </summary>
    public long FetchesSkipped { get; private set; }

    /// <summary>
    /// Materialize the pass list for this publish, serving the segments around
    /// the active layer from their bakes where possible. Specs under a valid
    /// bake are never handed to <paramref name="materialize"/> — their cels
    /// are not fetched at all.
    /// </summary>
    /// <param name="plan">The described publish, from <see cref="ScenePassBuilder.Describe"/>.</param>
    /// <param name="width">Document width — the bakes match the layer bitmaps.</param>
    /// <param name="height">Document height.</param>
    /// <param name="hold">
    /// True while keys cannot repeat (playback): skip folding and drop
    /// nothing, so the machinery costs nothing where it cannot help.
    /// </param>
    /// <param name="materialize">
    /// Turns one spec into a drawable pass — the cache fetch, deferred to
    /// here so a served segment can decline to make it.
    /// </param>
    /// <param name="transitioned">
    /// True when a segment changed between baked and unbaked since the last
    /// publish. Premultiplied source-over does not commute with resampling
    /// bit-for-bit, so a folded repaint next to an unfolded one can differ by
    /// a least significant bit — invisible on a full repaint, a faint seam if
    /// it lands inside a dirty-region patch. The caller widens that one
    /// publish to the whole canvas; transitions only happen when the stack
    /// changed, which repaints everything anyway.
    /// </param>
    internal List<RenderPass> Fold(
        ScenePassBuilder.Plan plan, int width, int height, bool hold,
        Func<ScenePassBuilder.PassSpec, RenderPass> materialize, out bool transitioned)
    {
        var specs = plan.Specs;
        if (!Enabled || hold || specs.Count == 0)
        {
            _pendingBelow = _pendingAbove = null;
            transitioned = _servedBelow || _servedAbove;
            _servedBelow = _servedAbove = false;
            return MaterializeRange(specs, 0, specs.Count, materialize, null);
        }

        var activeStart = plan.ActiveStart;
        var activeEnd = plan.ActiveEnd;
        if (activeStart < 0)
        {
            activeStart = specs.Count;
            activeEnd = specs.Count;
        }

        var belowBaked = FoldSegment(
            _below, ref _pendingBelow, specs, 0, activeStart, width, height, materialize);
        var aboveBaked = FoldSegment(
            _above, ref _pendingAbove, specs, activeEnd, specs.Count, width, height,
            materialize);
        transitioned = (belowBaked is not null) != _servedBelow
                       || (aboveBaked is not null) != _servedAbove;
        _servedBelow = belowBaked is not null;
        _servedAbove = aboveBaked is not null;

        var folded = new List<RenderPass>(
            (activeEnd - activeStart) + 2
            + (belowBaked is null ? activeStart : 0)
            + (aboveBaked is null ? specs.Count - activeEnd : 0));
        if (belowBaked is not null) folded.Add(belowBaked);
        else MaterializeRange(specs, 0, activeStart, materialize, folded);
        MaterializeRange(specs, activeStart, activeEnd, materialize, folded);
        if (aboveBaked is not null) folded.Add(aboveBaked);
        else MaterializeRange(specs, activeEnd, specs.Count, materialize, folded);
        if (belowBaked is not null || aboveBaked is not null) FoldedPublishes++;
        return folded;
    }

    private static List<RenderPass> MaterializeRange(
        List<ScenePassBuilder.PassSpec> specs, int start, int end,
        Func<ScenePassBuilder.PassSpec, RenderPass> materialize, List<RenderPass>? into)
    {
        into ??= new List<RenderPass>(end - start);
        for (var i = start; i < end; i++) into.Add(materialize(specs[i]));
        return into;
    }

    /// <summary>
    /// A frame's content changed behind its reference — a stroke edit, an
    /// undo, a palette repaint. Any bake covering it is showing yesterday's
    /// pixels and is dropped on the spot. Cheap when nothing covers it: two
    /// hash lookups, which is what lets the stroke-commit path call this
    /// unconditionally.
    /// </summary>
    public void NoteFrameChanged(string frameId)
    {
        if (_below.Covered.Contains(frameId)) RetireSegment(_below);
        if (_above.Covered.Contains(frameId)) RetireSegment(_above);
        // The held bake keys on bitmap identity, and an invalidated frame
        // re-materializes as a new instance — its key misses on its own.
    }

    /// <summary>Drop both bakes — a document switch, a resize, anything wholesale.</summary>
    public void Reset()
    {
        RetireSegment(_below);
        RetireSegment(_above);
        RetireHeld();
        _pendingBelow = _pendingAbove = null;
        _pendingHeld = null;
        _servedBelow = _servedAbove = false;
        _servedHeld = false;
    }

    public void Dispose() => Reset();

    /// <summary>
    /// Fold the bottom <paramref name="count"/> passes into one baked bitmap —
    /// the layers holding still for the whole playback range (B165).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The playback counterpart to <see cref="Fold"/>:</b> during playback
    /// the not-being-drawn-on segment changes the moment any layer in it
    /// exposes a new cel, so the spec-keyed fold holds off — but the held
    /// prefix's passes are identical on every frame of the range, and folding
    /// those is one blend instead of however many there are.
    /// </para>
    /// <para>
    /// <b>The caller chooses the count from the exposure sheet</b>
    /// (<see cref="UnchangedLayerRun.HeldPrefix"/>) rather than this class
    /// guessing from the passes. Two different questions: which layers hold still
    /// over time is a property of the document, and whether a run of passes may be
    /// pre-folded at all is a property of their blends — the second is checked
    /// here, as it is for every other segment.
    /// </para>
    /// <para>
    /// <b>This fold keys on materialized bitmaps rather than specs, and that is
    /// correct where it stands:</b> playback has already fetched these passes
    /// (the held prefix is by definition resident and reused), so identity plus
    /// content version is available, and an invalidated frame re-materializes
    /// as a new instance so the key misses without needing
    /// <see cref="NoteFrameChanged"/>. The key must be seen twice before a
    /// bake is paid for, as everywhere else.
    /// </para>
    /// </remarks>
    public List<RenderPass> FoldHeldRun(
        List<RenderPass> passes, int count, int width, int height, out bool transitioned)
    {
        if (!Enabled || count < 2 || count > passes.Count)
        {
            _pendingHeld = null;
            transitioned = _servedHeld;
            _servedHeld = false;
            return passes;
        }

        var held = passes.GetRange(0, count);
        var baked = FoldHeldSegment(held, width, height);
        transitioned = (baked is not null) != _servedHeld;
        _servedHeld = baked is not null;
        if (baked is null) return passes;

        var folded = new List<RenderPass>(passes.Count - count + 1) { baked };
        folded.AddRange(passes.GetRange(count, passes.Count - count));
        FoldedPublishes++;
        return folded;
    }

    /// <summary>
    /// Serve a segment of the spec list from its bake, building the bake when
    /// its key has been seen twice, or decline and let the caller materialize.
    /// </summary>
    private RenderPass? FoldSegment(
        Segment segment, ref List<ScenePassBuilder.PassSpec>? pending,
        List<ScenePassBuilder.PassSpec> specs, int start, int end, int width, int height,
        Func<ScenePassBuilder.PassSpec, RenderPass> materialize)
    {
        var count = end - start;
        if (count < 2 || !Eligible(specs, start, end))
        {
            // The stack changed shape (a transform session, a blend mode): a
            // stale bake must not survive to a later publish that happens to
            // match its old key.
            RetireSegment(segment);
            pending = null;
            return null;
        }

        if (segment.Baked is not null
            && segment.Width == width && segment.Height == height
            && SameKey(segment.Key, specs, start, end))
        {
            // Served: none of these specs is materialized, so none of their
            // cels needs to be resident. This is the line B198 is about.
            for (var i = start; i < end; i++)
            {
                if (specs[i].CelFrame is not null) FetchesSkipped++;
            }
            return new RenderPass(segment.Baked, null, 1.0);
        }

        // First sighting of this key: remember it and let this publish pay the
        // per-layer cost it would have paid anyway. Second sighting: bake.
        if (pending is null || !SameKey(pending, specs, start, end))
        {
            pending = specs.GetRange(start, count);
            return null;
        }

        RetireSegment(segment);
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bmp = new SKBitmap(info);
        using (var surface = SKSurface.Create(info, bmp.GetPixels(), bmp.RowBytes))
        {
            if (surface is null)
            {
                bmp.Dispose();
                return null;
            }
            var passes = MaterializeRange(specs, start, end, materialize, null);
            SceneRenderer.ComposeInto(surface, passes, SKColors.Transparent);
        }
        segment.Baked = bmp;
        segment.Key = pending;
        segment.Width = width;
        segment.Height = height;
        for (var i = start; i < end; i++)
        {
            if (specs[i].CelFrame is { } frame) segment.Covered.Add(frame.Id);
        }
        pending = null;
        Rebuilds++;
        return new RenderPass(bmp, null, 1.0);
    }

    private RenderPass? FoldHeldSegment(List<RenderPass> passes, int width, int height)
    {
        if (!EligiblePasses(passes))
        {
            RetireHeld();
            _pendingHeld = null;
            return null;
        }

        var key = KeyOf(passes);
        if (_held.Baked is not null && SameHeldKey(_held.Key, key))
        {
            return new RenderPass(_held.Baked, null, 1.0);
        }

        if (_pendingHeld is null || !SameHeldKey(_pendingHeld, key))
        {
            _pendingHeld = key;
            return null;
        }

        RetireHeld();
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bmp = new SKBitmap(info);
        using (var surface = SKSurface.Create(info, bmp.GetPixels(), bmp.RowBytes))
        {
            if (surface is null)
            {
                bmp.Dispose();
                return null;
            }
            SceneRenderer.ComposeInto(surface, passes, SKColors.Transparent);
        }
        _held.Baked = bmp;
        _held.Key = key;
        _pendingHeld = null;
        Rebuilds++;
        return new RenderPass(bmp, null, 1.0);
    }

    private void RetireSegment(Segment segment)
    {
        if (segment.Baked is { } baked) Retire(baked);
        segment.Baked = null;
        segment.Key = [];
        segment.Covered.Clear();
    }

    private void RetireHeld()
    {
        if (_held.Baked is { } baked) Retire(baked);
        _held.Baked = null;
        _held.Key = [];
    }

    private void Retire(SKBitmap baked)
    {
        if (RetireBitmap is { } retire) retire(baked);
        else baked.Dispose();
    }

    /// <summary>
    /// Whether a segment of specs can be pre-folded without changing the
    /// picture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SrcOver only, because folding rests on its associativity; no live
    /// overlays, because they change per pointer event. A spec matrix is
    /// allowed — a reference strip is positioned by one and is otherwise
    /// perfectly static — because it folds through the same ComposeInto the
    /// unbaked path uses, and it is part of the key, so a transform drag whose
    /// matrix moves every event simply never matches twice.
    /// </para>
    /// <para>
    /// A tile-native spec — one carrying a <c>SourceFrame</c>, which is how a
    /// playing document holds a frame — is refused: it has no pixels a
    /// document-resolution bake could hold, and folding there would spend the
    /// sparsity the tile store exists for. A frame that samples the layers
    /// beneath it live (<see cref="FrameBitmapCache.CanCache"/> is false) is
    /// refused too: its render changes with state no key here can see, which
    /// is why the frame cache itself refuses to hold it.
    /// </para>
    /// </remarks>
    private static bool Eligible(List<ScenePassBuilder.PassSpec> specs, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            var s = specs[i];
            if (s.Blend != SKBlendMode.SrcOver) return false;
            if (s.Overlay is not null) return false;
            if (s.SourceFrame is not null) return false;
            if (s.CelFrame is { } frame)
            {
                if (!FrameBitmapCache.CanCache(frame)) return false;
            }
            else if (s.Bitmap is null)
            {
                return false;
            }
        }
        return true;
    }

    private static bool EligiblePasses(List<RenderPass> passes) =>
        passes.TrueForAll(p =>
            p.Blend == SKBlendMode.SrcOver
            && p.Overlay is null
            && p.SourceFrame is null
            && p.Bitmap is not null);

    private static List<PassKey> KeyOf(List<RenderPass> passes)
    {
        var key = new List<PassKey>(passes.Count);
        foreach (var p in passes)
        {
            // EligiblePasses has already refused a null bitmap, so this is the
            // belt to that braces: BitmapVersion.Of would throw rather than
            // warn, and a key that cannot be built must fail closed — version
            // 0 against a null bitmap never matches a real pass.
            key.Add(new PassKey(
                p.Bitmap, p.Bitmap is null ? 0 : Lightbox.Raster.BitmapVersion.Of(p.Bitmap),
                p.Tint, p.Opacity, p.Blend, p.Matrix, p.Source));
        }
        return key;
    }

    private static bool SameKey(
        List<ScenePassBuilder.PassSpec> key, List<ScenePassBuilder.PassSpec> specs,
        int start, int end)
    {
        if (key.Count != end - start) return false;
        for (var i = 0; i < key.Count; i++)
        {
            if (!key[i].Equals(specs[start + i])) return false;
        }
        return true;
    }

    private static bool SameHeldKey(List<PassKey> a, List<PassKey> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!a[i].Equals(b[i])) return false;
        }
        return true;
    }
}

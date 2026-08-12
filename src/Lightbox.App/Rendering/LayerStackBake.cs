using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Folds the render passes that are not the active layer into two baked
/// bitmaps — everything beneath it and everything above it — so a repaint
/// while drawing blends three passes instead of one per layer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The bench harness measured a whole-frame
/// recomposite at n^0.99 in the layer count — every publish pays one blend per
/// visible layer, and a stroke commit publishes the whole canvas. An artist
/// edits one layer at a time; the other N−1 layers are the same bitmaps with
/// the same opacities publish after publish, and re-blending them is the
/// definition of work that can be done once.
/// </para>
/// <para>
/// <b>Correctness rests on associativity, so only SrcOver folds.</b>
/// Pre-folding a run of layers into one bitmap and drawing that over what is
/// beneath gives the same pixels as drawing them one by one <i>only</i>
/// because premultiplied source-over is associative. A Multiply layer reads
/// the pixels beneath it, so a segment containing any non-SrcOver blend, a
/// live overlay, or a transform-preview matrix refuses to fold and composites
/// exactly as before. Layers default to SrcOver, so the common document folds;
/// the exotic one stays correct.
/// </para>
/// <para>
/// <b>The key is bitmap identity plus content version — identity alone is a
/// trap here.</b> Committing a stroke does not replace the cached layer
/// bitmap; <c>FrameRasterizer.Append</c> stamps into it in place (bounded
/// work, invariant 6), so an instance survives its pixels changing.
/// <c>Lightbox.Raster.BitmapVersion</c> carries the content version the
/// mutator bumps, and it joins every reference in the key. Nothing
/// subscribes to anything: a re-render is a new instance, an in-place edit
/// is a new version, and either way the key misses and the bake rebuilds.
/// </para>
/// <para>
/// <b>Honest note: the version half is defence, not a tested fix.</b> The
/// stale-bake trap could not be sprung through today's public API — in-place
/// commits only reach the <em>active</em> layer, and making a layer active
/// reshapes the segments, which disposes or rebuilds the bake before a stale
/// key can match. Every attempt at the failing test came back green with the
/// version deleted. It is kept because the reachability argument is
/// incidental — one new in-place mutation path that does not route through
/// active-layer segmentation (a background eraser, an AI edit stamping into
/// a cached bitmap) makes it load-bearing, and stale art is wrong quietly.
/// Same standing as the ComposeRing invalidation note in
/// <c>PublishSnapshot</c>: do not delete it as dead on the strength of the
/// tests passing without it.
/// </para>
/// <para>
/// <b>Folding waits for the second consecutive publish with the same key.</b>
/// A fold costs two full-canvas composites, which is roughly the recomposite
/// it saves — paying it on a key seen once would make playback and scrubbing
/// slower, since every frame change is a new key. Seen twice means the stack
/// is holding still (the artist is drawing), and every publish from the third
/// onward blends three passes. The caller also skips folding outright during
/// playback, where keys never repeat by construction.
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

    private sealed class Segment : IDisposable
    {
        public List<PassKey> Key = [];
        public SKBitmap? Baked;

        public void Dispose()
        {
            Baked?.Dispose();
            Baked = null;
            Key = [];
        }
    }

    private readonly Segment _below = new();
    private readonly Segment _above = new();
    private readonly Segment _held = new();
    private List<PassKey>? _pendingHeld;
    private bool _servedHeld;
    private List<PassKey>? _pendingBelow;
    private List<PassKey>? _pendingAbove;
    private bool _servedBelow;
    private bool _servedAbove;

    /// <summary>
    /// Test seam: false composites every pass exactly as before this class
    /// existed. The pixel-identity tests need an unfolded reference render,
    /// and a switch is honest where a re-implementation of the old loop in a
    /// test would drift.
    /// </summary>
    internal bool Enabled { get; set; } = true;

    /// <summary>How many publishes were served from the bakes, for diagnostics.</summary>
    public long FoldedPublishes { get; private set; }

    /// <summary>How many times the bakes were (re)built, for diagnostics.</summary>
    public long Rebuilds { get; private set; }

    /// <summary>
    /// Replace the passes around the active segment with baked equivalents
    /// where possible. Returns the pass list to composite — either the input,
    /// untouched, or a shorter list drawing the same pixels.
    /// </summary>
    /// <param name="passes">The full pass list for this publish, in order.</param>
    /// <param name="activeStart">
    /// Index of the first pass belonging to the active layer (its under-ghosts
    /// included), or -1 when the active layer contributed nothing — a hidden
    /// layer — in which case the whole list is treated as "below".
    /// </param>
    /// <param name="activeEnd">One past the active layer's last pass.</param>
    /// <param name="width">Document width — the bakes match the layer bitmaps.</param>
    /// <param name="height">Document height.</param>
    /// <param name="hold">
    /// True while keys cannot repeat (playback, scrubbing): skip folding and
    /// drop nothing, so the machinery costs nothing where it cannot help.
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
    public List<RenderPass> Fold(
        List<RenderPass> passes, int activeStart, int activeEnd, int width, int height, bool hold,
        out bool transitioned)
    {
        if (!Enabled || hold || passes.Count == 0)
        {
            _pendingBelow = _pendingAbove = null;
            transitioned = _servedBelow || _servedAbove;
            _servedBelow = _servedAbove = false;
            return passes;
        }

        if (activeStart < 0)
        {
            activeStart = passes.Count;
            activeEnd = passes.Count;
        }

        var below = passes.GetRange(0, activeStart);
        var active = passes.GetRange(activeStart, activeEnd - activeStart);
        var above = passes.GetRange(activeEnd, passes.Count - activeEnd);

        // Folding one pass saves nothing and costs a bitmap; require two.
        var belowBaked = FoldSegment(_below, ref _pendingBelow, below, width, height);
        var aboveBaked = FoldSegment(_above, ref _pendingAbove, above, width, height);
        transitioned = (belowBaked is not null) != _servedBelow
                       || (aboveBaked is not null) != _servedAbove;
        _servedBelow = belowBaked is not null;
        _servedAbove = aboveBaked is not null;
        if (belowBaked is null && aboveBaked is null) return passes;

        var folded = new List<RenderPass>(active.Count + 2);
        if (belowBaked is not null) folded.Add(belowBaked);
        else folded.AddRange(below);
        folded.AddRange(active);
        if (aboveBaked is not null) folded.Add(aboveBaked);
        else folded.AddRange(above);
        FoldedPublishes++;
        return folded;
    }

    /// <summary>Drop both bakes — a document switch, a resize, anything wholesale.</summary>
    public void Reset()
    {
        _below.Dispose();
        _above.Dispose();
        _held.Dispose();
        _pendingBelow = _pendingAbove = null;
        _servedBelow = _servedAbove = false;
    }

    public void Dispose() => Reset();

    /// <summary>
    /// Fold the bottom <paramref name="count"/> passes into one baked bitmap —
    /// the layers holding still for the whole playback range (B165).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The playback counterpart to <see cref="Fold"/>, and it exists because
    /// this class already said why it could not help here:</b> <i>"the caller also
    /// skips folding outright during playback, where keys never repeat by
    /// construction."</i> True of the key <see cref="Fold"/> uses — the whole
    /// not-being-drawn-on segment, which changes the moment any layer in it
    /// exposes a new cel. False of the held prefix, whose key is identical on
    /// every frame of the range.
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
    /// Everything else is <see cref="FoldSegment"/>'s: the key includes bitmap
    /// identity and content version, a changed key rebuilds, and a key must be
    /// seen twice before a bake is paid for.
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
        var baked = FoldSegment(_held, ref _pendingHeld, held, width, height);
        transitioned = (baked is not null) != _servedHeld;
        _servedHeld = baked is not null;
        if (baked is null) return passes;

        var folded = new List<RenderPass>(passes.Count - count + 1) { baked };
        folded.AddRange(passes.GetRange(count, passes.Count - count));
        FoldedPublishes++;
        return folded;
    }

    private RenderPass? FoldSegment(
        Segment segment, ref List<PassKey>? pending, List<RenderPass> passes, int width, int height)
    {
        if (passes.Count < 2 || !Eligible(passes))
        {
            // The stack changed shape (a transform session, a blend mode): a
            // stale bake must not survive to a later publish that happens to
            // match its old key.
            segment.Dispose();
            pending = null;
            return null;
        }

        var key = KeyOf(passes);
        if (segment.Baked is not null && SameKey(segment.Key, key))
        {
            return new RenderPass(segment.Baked, null, 1.0);
        }

        // First sighting of this key: remember it and let this publish pay the
        // per-layer cost it would have paid anyway. Second sighting: bake.
        if (pending is null || !SameKey(pending, key))
        {
            pending = key;
            return null;
        }

        segment.Dispose();
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
        segment.Baked = bmp;
        segment.Key = key;
        pending = null;
        Rebuilds++;
        return new RenderPass(bmp, null, 1.0);
    }

    /// <summary>
    /// Whether a segment can be pre-folded without changing the picture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SrcOver only, because folding rests on its associativity; no live
    /// overlays, because they change per pointer event. A pass matrix is
    /// allowed — a reference strip is positioned by one and is otherwise
    /// perfectly static — because it folds through the same ComposeInto the
    /// unbaked path uses, and it is part of the key, so a transform drag whose
    /// matrix moves every event simply never matches twice.
    /// </para>
    /// <para>
    /// A tile-native pass — one carrying a <c>SourceFrame</c> rather than a
    /// bitmap, which is how an unbounded document holds a frame — is refused,
    /// for three separate reasons and any one of them is enough. Its
    /// <c>Bitmap</c> is null, so it has no identity to key on and two
    /// different frames would key identically, which is a stale bake served
    /// for the wrong picture. <see cref="SceneRenderer.ComposeInto"/> draws
    /// from bitmaps, so a bake of one would silently lose the layer. And the
    /// bake is document-resolution, which is exactly the allocation the tile
    /// path exists to avoid — folding there would spend the property that
    /// makes an unbounded canvas possible in order to save a blend.
    /// </para>
    /// </remarks>
    private static bool Eligible(List<RenderPass> passes) =>
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
            // Eligible has already refused a null bitmap, so this is the
            // belt to that braces: BitmapVersion.Of would throw rather than
            // warn, and a key that cannot be built must fail closed — version
            // 0 against a null bitmap never matches a real pass.
            key.Add(new PassKey(
                p.Bitmap, p.Bitmap is null ? 0 : Lightbox.Raster.BitmapVersion.Of(p.Bitmap),
                p.Tint, p.Opacity, p.Blend, p.Matrix, p.Source));
        }
        return key;
    }

    private static bool SameKey(List<PassKey> a, List<PassKey> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!a[i].Equals(b[i])) return false;
        }
        return true;
    }
}

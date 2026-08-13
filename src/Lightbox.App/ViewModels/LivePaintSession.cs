using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The state of the stroke currently under the pen, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Tier 0 of <c>docs/DESIGN-mainviewmodel-decomposition.md</c>, extracted per Q74.
/// Twenty-two fields that lived in <see cref="MainViewModel"/> live here instead,
/// in the manner of <see cref="SelectionManager"/>: one long-lived object, owned by
/// the view model for its lifetime, so nothing on the paint path allocates a
/// collaborator per stroke or per pointer event.
/// </para>
/// <para>
/// <b>Why the state and not the engine.</b> The methods that drive this —
/// <c>MoveStroke</c>, <c>FlushLivePreview</c>, <c>StampLiveDabs</c>,
/// <c>StampLiveSmudge</c>, <c>RenderLivePostProcess</c>, <c>EndStroke</c> — stay on
/// the view model, because they also need the editor, the frame cache, the brush
/// settings and the stroke builder. Moving them here would drag those four in behind
/// them and this would become a second view model. What moves is the thing that was
/// genuinely unowned: the scratch bitmaps and the dab bookkeeping, which nothing
/// outside a live stroke has any business reading.
/// </para>
/// <para>
/// <b>What this is not.</b> It is not an encapsulation boundary that validates
/// anything. The engine mutates these properties directly, and a version that hid
/// them behind methods would have to expose the whole dab walk to be useful. What it
/// buys is narrower: the fields have one owner, a reader can see the whole of the
/// live-stroke state on one screen, and the lifecycle operations that used to be
/// six separate private methods on a 13,000-line class are now four methods next to
/// the state they reset. The value is in <see cref="ClearEffectState"/> being
/// impossible to get half-right, not in access control.
/// </para>
/// <para>
/// <b>Two invariants ride on this and neither fails loudly.</b> Painting is bounded
/// work (invariant 6), so <see cref="ScratchUsed"/> and <see cref="PostUsed"/> exist
/// to keep a pen-up clearing the region a stroke touched rather than the canvas; a
/// change that leaves either null where it used to be a rect turns every stroke into
/// a full-canvas clear and nothing goes red. And the tail lend-and-take-back that
/// <see cref="TailBackup"/> serves is what keeps a self-crossing stroke accumulating
/// identically live and committed — see the remarks on that property.
/// </para>
/// <para>
/// <b>No <c>IDisposable</c>, deliberately.</b> These bitmaps outlive every stroke by
/// design — the scratch is pooled because at 4K it is 33 MB — and
/// <see cref="MainViewModel"/> is not disposable, so a <c>Dispose</c> here would have
/// no caller. Adding one would invent a teardown the application does not have and
/// leave dead code that looks like a lifecycle. If view-model disposal ever arrives,
/// releasing <see cref="Scratch"/>, <see cref="ScratchCanvas"/>,
/// <see cref="PostScratch"/>, <see cref="TailBackup"/>, <see cref="SmudgeBackup"/>,
/// <see cref="Composite"/> and <see cref="EffectBase"/> is what it needs to do.
/// </para>
/// </remarks>
sealed class LivePaintSession
{
    // ---- the paint scratch ---------------------------------------------------

    // A persistent copy-free overlay that only the NEW segment of the stroke gets
    // stamped into per pointer event — this is what keeps painting O(stroke length)
    // instead of O(length²). It holds the whole-stroke dab accumulation WITHOUT the
    // stroke's opacity, so the compositor lays it over the layer and applies opacity
    // once and a self-crossing stroke looks the same live as committed. Pooled across
    // strokes: at 4K this bitmap is 33 MB, far too big to allocate per stroke.
    internal SKBitmap? Scratch { get; private set; }

    internal SKCanvas? ScratchCanvas { get; private set; }

    /// <summary>
    /// Region of the scratch actually touched by the current stroke, so pen-up can
    /// clear just that much instead of the whole canvas (invariant 6).
    /// </summary>
    internal SKRectI? ScratchUsed { get; set; }

    // ---- the effect path (blur and smudge) -----------------------------------

    /// <summary>
    /// Blur brushes read the canvas underneath them, so they cannot be composited
    /// from a separate scratch — they keep the copy-based path.
    /// </summary>
    internal SKBitmap? Composite { get; set; }

    /// <summary>
    /// The pristine pre-stroke pixels an effect brush samples, kept apart from the
    /// composite it writes into. See the note in <c>MainViewModel.BeginStroke</c>.
    /// </summary>
    internal SKBitmap? EffectBase { get; set; }

    internal int StampedCount { get; set; }

    /// <summary>
    /// How many of the live stroke's dabs are already in the scratch.
    /// </summary>
    /// <remarks>
    /// Counted in <b>dabs</b>, not in points, and the two are not interchangeable: the
    /// walk emits a dab every <c>spacing × diameter</c> of arc length, so a slow pointer
    /// produces many points and few dabs and a fast one the reverse. This is the number
    /// that lets the engine walk the whole stroke and draw only what is new (B45).
    /// </remarks>
    internal int DabCount { get; set; }

    /// <summary>
    /// The scratch pixels under the provisional tail, before it was stamped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the tail is drawn and then taken back rather than simply held until it
    /// settles.</b> A dab's position is provisional until the next point arrives, so
    /// stamping it immediately means stamping it in the wrong place; holding it back
    /// instead was tried and measured, and it costs too much — the live mark came out 4%
    /// short on a long stroke and <b>39% short on a six-event flick</b>, which reads as
    /// the stroke lagging behind the pen. An artist notices that far more than they would
    /// notice a settled dab.
    /// </para>
    /// <para>
    /// So the tail is stamped for the tip to be live, its region is remembered, and the
    /// next event restores those exact pixels before re-stamping. Restoring by copy makes
    /// it byte-exact, and because the stable count only ever grows, the dabs still land in
    /// index order — which is what keeps a self-crossing accumulating the same way it does
    /// in a single-pass render.
    /// </para>
    /// </remarks>
    internal SKBitmap? TailBackup { get; set; }

    internal SKRectI? TailRegion { get; set; }

    /// <summary>Dabs in the scratch whose position is settled, so never taken back.</summary>
    internal int StableDabs { get; set; }

    /// <summary>
    /// The dab walk from the previous pointer event.
    /// </summary>
    /// <remarks>
    /// Kept for two reasons, both measured. It is what the new walk is compared against to
    /// find the settled prefix, which avoids walking the stroke a second time just to ask
    /// that question. And walking is not free: four walks an event made a 600-event stroke
    /// cost 3.2× more per event at the end than at the start, which invariant 6 forbids.
    /// </remarks>
    internal List<BrushEngine.Dab>? Dabs { get; set; }

    /// <summary>
    /// The densify cache for the stroke in hand (B46).
    /// </summary>
    /// <remarks>
    /// One per session rather than one per stroke: it keys off the points it last saw, so a
    /// new stroke simply looks like a wholesale change and rebuilds. Kept alive between
    /// strokes so the common case allocates nothing.
    /// </remarks>
    internal Lightbox.Core.Geometry.IncrementalDensify Densify { get; } = new();

    /// <summary>The effect draft's own dab bookkeeping, mirroring <see cref="Dabs"/>.</summary>
    /// <remarks>
    /// Separate because the two paths are exclusive — a stroke is either an effect or paint — and
    /// sharing one field would make whichever ran second compare against the other's walk. B54.
    /// </remarks>
    internal List<BrushEngine.Dab>? EffectDabs { get; set; }

    /// <summary>How many effect dabs are already on the composite and must not be drawn again.</summary>
    internal int EffectSettled { get; set; }

    /// <summary>
    /// What the smudge was carrying at dab <see cref="EffectSettled"/>, so a resumed range
    /// starts where a single pass would have (B69/B89).
    /// </summary>
    /// <remarks>
    /// The blur needs no equivalent: its dabs read the pre-stroke pixels and are independent of one
    /// another. A smudge's are a chain, and this is the one link that has to survive between pointer
    /// events. It is two values, so checkpointing it every event costs nothing.
    /// </remarks>
    internal BrushEngine.SmudgeCarry SmudgeCarry { get; set; }

    /// <summary>
    /// The composite's pixels under the provisional smudge tail, before it was stamped.
    /// </summary>
    /// <remarks>
    /// The same lend-and-take-back the paint scratch does in <c>StampLiveDabs</c>, for the
    /// same reason and with one extra: a smudge <em>reads</em> the bitmap it writes, so re-stamping
    /// an unsettled dab over its own previous deposit compounds the smear rather than replacing it.
    /// Restoring first is what makes the replayed range see what a single pass would have seen.
    /// </remarks>
    internal SKBitmap? SmudgeBackup { get; set; }

    internal SKRectI? SmudgeRegion { get; set; }

    // ---- live post-processing (medium, wet edge, texture, granulation) --------
    //
    // These are STROKE-GLOBAL: the wet edge is derived from the whole silhouette and
    // the fluid lattice flows across the whole wet area, so running them per segment
    // would rim and pool each segment separately — visibly wrong, and not what
    // commits. They have to be recomputed over the whole stroke so far, which at
    // 45–143 ms on a 4K canvas cannot happen on every pointer event.
    //
    // So: raw dabs go into Scratch immediately (2 ms, the pen never lags), and a full
    // render of the stroke-so-far lands in PostScratch as often as its own measured
    // cost allows. The compositor shows the rendered one when it exists. The artist
    // sees the true mark converging a fraction behind the tip rather than seeing flat
    // dabs until pen-up, which is how wet media behave in every tool that has them.

    internal SKBitmap? PostScratch { get; set; }

    internal SKRectI? PostUsed { get; set; }

    /// <summary>Cost of the last pass, milliseconds — reported by the performance panel.</summary>
    internal double PostCostMs { get; set; }

    internal int PostStampedCount { get; set; } = -1;

    internal bool PostQueued { get; set; }

    /// <summary>How many times the live post-process has rendered. Tests only.</summary>
    internal int PostPasses { get; set; }

    /// <summary>Total milliseconds spent in those passes. Tests only.</summary>
    internal double PostTotalMs { get; set; }

    // ---- lifecycle -----------------------------------------------------------

    /// <summary>A document-sized scratch bitmap for the live preview overlay.</summary>
    /// <remarks>
    /// Takes the size rather than reading the scene, so this stays a state holder with
    /// no opinion about the document. Reallocates only when the size actually changed —
    /// at 4K the bitmap is 33 MB and a per-stroke allocation would be felt.
    /// </remarks>
    internal void EnsureScratch(int width, int height)
    {
        if (Scratch is not null && Scratch.Width == width && Scratch.Height == height)
        {
            return;
        }
        ScratchCanvas?.Dispose();
        Scratch?.Dispose();
        Scratch = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        ScratchCanvas = new SKCanvas(Scratch);
        ScratchUsed = null;
    }

    /// <summary>Wipe only the region the previous stroke actually touched.</summary>
    internal void ClearScratch()
    {
        if (ScratchCanvas is null) return;
        if (ScratchUsed is not { } used)
        {
            ScratchCanvas.Clear(SKColors.Transparent);
            return;
        }
        ScratchCanvas.Save();
        ScratchCanvas.ClipRect(SKRect.Create(used.Left, used.Top, used.Width, used.Height));
        ScratchCanvas.Clear(SKColors.Transparent);
        ScratchCanvas.Restore();
        ScratchUsed = null;
    }

    /// <summary>
    /// Drop the Blur/Smudge live preview and the ordinary-paint dab-walk bookkeeping.
    /// <c>MainViewModel.EndStroke</c> calls this after a real commit; anything that
    /// abandons a stroke without going through it — <c>AttachEditor</c> on a tab
    /// switch, <c>StartPlayback</c> — must call it too.
    /// </summary>
    /// <remarks>
    /// <c>_strokeBuilder.Cancel()</c> alone leaves <see cref="Composite"/> non-null, and
    /// every publish after that treats a non-null composite as "an effect brush is live
    /// on this layer" — which, left stale, silently suppressed the overlay for every
    /// ordinary stroke, gradient and shape drag afterward, on any document, until ink
    /// happened to reset it (B39's fix). That is the reason this is one method rather
    /// than a dozen assignments at each call site: the failure is silent, and a caller
    /// that forgets one field looks exactly like a caller that forgot none.
    /// </remarks>
    internal void ClearEffectState()
    {
        Composite?.Dispose();
        Composite = null;
        EffectBase?.Dispose();
        EffectBase = null;
        StampedCount = 0;
        DabCount = 0;
        StableDabs = 0;
        TailRegion = null;
        Dabs = null;
        EffectDabs = null;
        EffectSettled = 0;
        // The carry and the lent region go with the composite they described. The backup
        // bitmap does not: it is reused across strokes and only ever written before it is
        // read, so keeping it saves an allocation per stroke without any state surviving.
        SmudgeCarry = default;
        SmudgeRegion = null;
        ResetPostProcess();
    }

    /// <summary>Forget the live post-process draft, so the next stroke starts clean.</summary>
    /// <remarks>
    /// <b>The bitmap is wiped, not dropped.</b> <see cref="PostScratch"/> is pooled for
    /// the same reason <see cref="Scratch"/> is — at 4K it is 33 MB — so this clears the
    /// region the last stroke used and keeps the allocation. Disposing it here instead
    /// would put a 33 MB allocation on every pen-down and would not fail any test.
    /// <see cref="PostQueued"/> is deliberately untouched: it tracks whether a pass is
    /// in flight on the dispatcher, which is not this method's business to cancel.
    /// </remarks>
    internal void ResetPostProcess()
    {
        if (PostScratch is not null && PostUsed is not null)
        {
            using var canvas = new SKCanvas(PostScratch);
            ClearRegion(canvas, PostUsed);
        }
        PostUsed = null;
        PostCostMs = 0;
        PostStampedCount = -1;
    }

    /// <summary>Clear one region of a canvas, or all of it when the region is null.</summary>
    internal static void ClearRegion(SKCanvas canvas, SKRectI? region)
    {
        if (region is not { } r)
        {
            canvas.Clear(SKColors.Transparent);
            return;
        }
        canvas.Save();
        canvas.ClipRect(SKRect.Create(r.Left, r.Top, r.Width, r.Height));
        canvas.Clear(SKColors.Transparent);
        canvas.Restore();
    }

    /// <summary>The smallest rect covering both.</summary>
    /// <remarks>
    /// <para>
    /// Skia's <c>SKRectI.Union</c> mutates in place, so this takes a copy and returns it.
    /// </para>
    /// <para>
    /// <b>Do not "improve" it by ignoring empty rects.</b> Skia's union is a plain
    /// min/max over both corners and does <i>not</i> treat an empty rect as "nothing" —
    /// measured: <c>(0,0,0,0) ∪ (5,5,9,9)</c> is <c>(0,0,9,9)</c> in Skia and
    /// <c>(5,5,9,9)</c> under the obvious empty-skipping rewrite. A default
    /// <see cref="SKRectI"/> is empty at the origin, so the difference is real whenever
    /// one is unioned in: Skia stretches the dirty region to the canvas origin. That
    /// costs a larger repaint and is <i>safe</i>; the tighter version is the one that
    /// risks leaving pixels stale, and it is the one that looks like a cleanup.
    /// <c>LivePaintSessionTests</c> pins the divergent case.
    /// </para>
    /// </remarks>
    internal static SKRectI UnionRect(SKRectI a, SKRectI b)
    {
        a.Union(b);
        return a;
    }

}

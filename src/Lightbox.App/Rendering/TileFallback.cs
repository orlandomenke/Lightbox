using Lightbox.Core.Documents;
using Lightbox.Raster;

namespace Lightbox.App.Rendering;

/// <summary>
/// Why a frame did not take the tile path.
/// </summary>
public enum TileFallbackReason
{
    /// <summary>It did. Tiles carried the frame.</summary>
    None = 0,

    /// <summary>No viewport had been measured, so there is nothing to cull against.</summary>
    NoViewport,

    /// <summary>The scene has a camera, and the tile compositor does not read one.</summary>
    Camera,

    /// <summary>The frame carries baseline pixels — imported or flattened art.</summary>
    Baseline,

    /// <summary>The frame places symbols, which are rasterized whole rather than per tile.</summary>
    Placements,

    /// <summary>A smudge or blur stroke, which reads pixels one tile does not hold (B59).</summary>
    EffectStroke,

    /// <summary>An effect stroke is in flight on this layer right now.</summary>
    LiveEffect,

    /// <summary>
    /// Strokes here follow the rig, so the frame's pixels depend on the
    /// playhead — and the tile store knows frames only by id.
    /// </summary>
    BoundStrokes,

    /// <summary>
    /// A mask or clipping mask carves this layer, and the tiled compositor
    /// draws frames alone — it has no isolation to carve in.
    /// </summary>
    Shaped,

    /// <summary>
    /// The document carries a live effect — a filtered layer, an adjustment
    /// layer, or a scene grade — and effects need the bounded compositor's
    /// isolation and backdrop.
    /// </summary>
    Effects,
}

/// <summary>
/// The one place that decides whether a frame can be drawn from tiles — and
/// says why when it cannot.
/// </summary>
/// <remarks>
/// <para>
/// <b>The decision and the explanation are the same expression, on purpose.</b>
/// The obvious shape is a <c>bool</c> gate beside a separate "why" used for
/// reporting, and the obvious shape rots: the two drift, and the report then
/// confidently names a reason that is not the one the code acted on. Here
/// <see cref="Reason"/> is the gate — <c>None</c> means tiled — so a wrong
/// reason and a wrong decision are the same bug rather than two.
/// </para>
/// <para>
/// <b>Why this exists at all (B144, B125).</b> Playback joins the tile path
/// while frames flip, which measured 145 → 14 ms a frame at 1080p. A frame that
/// falls back pays the old cost instead, and falling back is <em>silent</em> —
/// so a document that stutters and a document that is simply slow look
/// identical from the outside. That is the same failure
/// <c>PresentedFrame.GpuSurfaceRequestFailed</c> exists to name: "it barely
/// improved" and "it never took the fast path" are indistinguishable until
/// something counts them.
/// </para>
/// <para>
/// The order of the checks is the order they are cheap in, and
/// <see cref="TileFallbackReason.EffectStroke"/> is last because it is the only
/// one that walks the stroke list.
/// </para>
/// </remarks>
public static class TileFallback
{
    /// <summary>
    /// Whether this frame can be drawn from tiles, and why not when it cannot.
    /// </summary>
    /// <param name="hasCamera">The scene has an authored camera.</param>
    /// <param name="haveViewport">A viewport has been measured.</param>
    /// <param name="liveEffectHere">A live blur or smudge is replacing this layer.</param>
    /// <param name="posed">
    /// The rig moves this drawing — <c>RigIndex.IsPosed</c>. Passed in rather
    /// than read off the frame, because a drawing on a rigged LAYER carries no
    /// weights of its own (Q90) and the frame cannot answer for it. Required
    /// rather than defaulted: a call site that forgot would tile a posed frame
    /// and draw it at rest, which looks like the rig not working at all.
    /// </param>
    /// <param name="shaped">
    /// A mask or clipping mask carves this layer's pass. Required rather than
    /// defaulted for <paramref name="posed"/>'s reason: a call site that
    /// forgot would tile the layer and show it uncarved — the mask visibly
    /// not working — exactly while the sequence moves.
    /// </param>
    /// <param name="docEffects">
    /// The document carries a live effect stack somewhere — a document-level
    /// condition like <paramref name="hasCamera"/>, asked here so the report
    /// can name it.
    /// </param>
    public static TileFallbackReason Reason(
        Frame frame, bool hasCamera, bool haveViewport, bool liveEffectHere, bool posed,
        bool shaped, bool docEffects = false)
    {
        if (!haveViewport) return TileFallbackReason.NoViewport;
        if (hasCamera) return TileFallbackReason.Camera;
        if (docEffects) return TileFallbackReason.Effects;
        if (liveEffectHere) return TileFallbackReason.LiveEffect;
        if (shaped) return TileFallbackReason.Shaped;
        if (frame.HasBaseline) return TileFallbackReason.Baseline;
        if (frame.HasPlacements) return TileFallbackReason.Placements;
        // Either half is enough, and neither implies the other: a stroke can
        // carry painted weights on a layer nobody rigged, and a rigged layer
        // moves strokes that carry no weights of their own.
        if (posed || frame.HasBoundStrokes) return TileFallbackReason.BoundStrokes;
        if (!TiledRasterizer.CanTile(frame.Strokes)) return TileFallbackReason.EffectStroke;
        return TileFallbackReason.None;
    }

    /// <summary>What to tell an artist whose playback is slow.</summary>
    public static string Explain(TileFallbackReason reason) => reason switch
    {
        TileFallbackReason.None => "tiled",
        TileFallbackReason.NoViewport => "no viewport measured yet",
        TileFallbackReason.Camera => "the scene has a camera",
        TileFallbackReason.Baseline => "frames carry imported or flattened pixels",
        TileFallbackReason.Placements => "frames place symbols",
        TileFallbackReason.EffectStroke => "frames contain smudge or blur strokes",
        TileFallbackReason.BoundStrokes => "frames contain strokes bound to the rig",
        TileFallbackReason.LiveEffect => "a smudge or blur was in flight",
        TileFallbackReason.Shaped => "layers carry a mask or clip to another",
        TileFallbackReason.Effects => "the document carries live effects",
        _ => reason.ToString(),
    };
}

/// <summary>
/// Running counts of why frames fell back, for the render report.
/// </summary>
/// <remarks>
/// Counts rather than a flag, because the useful answer is not "did anything
/// fall back" but "what is doing it for most of my frames" — a scene where one
/// layer of ten has a symbol on it is a different problem from one where every
/// frame does, and only a count separates them.
/// </remarks>
public sealed class TileFallbackTally
{
    private readonly long[] _counts = new long[Enum.GetValues<TileFallbackReason>().Length];

    /// <summary>
    /// Layer passes offered to the tile path, tiled or not.
    /// </summary>
    /// <remarks>
    /// <b>Passes, not frames</b>, and the distinction is not pedantry: the gate
    /// is asked once per layer per publish, so a two-layer document reports two
    /// for every frame shown. Calling these "frames" made a 50% share read as
    /// though half the sequence was broken when it was one layer of two.
    /// </remarks>
    public long Considered { get; private set; }

    public void Note(TileFallbackReason reason)
    {
        Considered++;
        _counts[(int)reason]++;
    }

    public long CountOf(TileFallbackReason reason) => _counts[(int)reason];

    /// <summary>Frames that actually came from tiles.</summary>
    public long Tiled => CountOf(TileFallbackReason.None);

    /// <summary>Passes that did not come from tiles, for any reason.</summary>
    public long FellBack => Considered - Tiled;

    /// <summary>What share of the passes fell back, 0–1.</summary>
    public double FallbackShare => Considered == 0 ? 0 : (double)FellBack / Considered;

    /// <summary>
    /// The reason that cost the most passes, or <c>None</c> when nothing fell
    /// back at all.
    /// </summary>
    /// <remarks>
    /// <b>Not "the reason that beat tiling".</b> That was the first rule here and
    /// it is wrong in the ordinary case: a scene whose paper layer tiles and
    /// whose paint layer never does is exactly 50/50, so the interesting reason
    /// scored equal to tiling and the tally reported <c>None</c> — announcing
    /// nothing was wrong on a document where half of every publish paid the old
    /// cost. Any fallback is worth naming; how much it matters is
    /// <see cref="FallbackShare"/>'s job, and keeping the two apart is what
    /// stops one of them lying.
    /// </remarks>
    public TileFallbackReason Dominant
    {
        get
        {
            var best = TileFallbackReason.None;
            long most = 0;
            foreach (var reason in Enum.GetValues<TileFallbackReason>())
            {
                if (reason == TileFallbackReason.None) continue;
                if (_counts[(int)reason] <= most) continue;
                most = _counts[(int)reason];
                best = reason;
            }
            return best;
        }
    }

    /// <summary>One line per reason that ever happened; empty when nothing did.</summary>
    public IEnumerable<(TileFallbackReason Reason, long Count)> NonZero()
    {
        foreach (var reason in Enum.GetValues<TileFallbackReason>())
        {
            if (reason != TileFallbackReason.None && _counts[(int)reason] > 0)
                yield return (reason, _counts[(int)reason]);
        }
    }

    public void Reset()
    {
        Array.Clear(_counts);
        Considered = 0;
    }
}

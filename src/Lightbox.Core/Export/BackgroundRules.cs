using Lightbox.Core.Documents;

namespace Lightbox.Core.Export;

/// <summary>What the exporter does about background layers.</summary>
public enum BackgroundHandling
{
    /// <summary>
    /// Leave out the document's paper layer and nothing else. The default, and it
    /// is what the exporter has always done.
    /// </summary>
    /// <remarks>
    /// Kept as the default so an existing export is byte-identical. It is not
    /// enough on its own — the paper layer is only the background an artist got for
    /// free, not the one they made themselves — which is why
    /// <see cref="Detected"/> exists.
    /// </remarks>
    PaperOnly,

    /// <summary>
    /// Also leave out any layer that is a full-canvas fill on every drawing it shows.
    /// </summary>
    /// <remarks>
    /// The mode a character wants. An artist adds a layer and floods it grey so they
    /// can see the line, and that layer is not the paper layer and may not be called
    /// anything in particular — but it covers the canvas on every frame, which
    /// nothing an animator is actually exporting ever does.
    /// </remarks>
    Detected,

    /// <summary>
    /// Export every visible layer, paper included.
    /// </summary>
    /// <remarks>
    /// For the asset whose background <em>is</em> the point: a backdrop, a sky, a
    /// tiling floor. Not reachable before this existed, which is the other half of
    /// why the omission is a mode rather than a hidden rule.
    /// </remarks>
    Everything,
}

/// <summary>Why a layer was left out of an export.</summary>
public enum BackgroundSignal
{
    /// <summary>The artist set <see cref="Layer.OmitFromExport"/>. Beats everything.</summary>
    Pinned,

    /// <summary>It is the document's paper layer (<see cref="Layer.IsBackground"/>).</summary>
    Paper,

    /// <summary>It is hidden, so it was never going to be exported.</summary>
    Hidden,

    /// <summary>
    /// Every drawing it shows covers the whole canvas. The strong signal, and the
    /// only one detection acts on by itself.
    /// </summary>
    FullCanvasFill,
}

/// <summary>A layer the export left out, and why.</summary>
public sealed record OmittedLayer(string LayerId, string Name, BackgroundSignal Signal);

/// <summary>
/// A layer the export <b>kept</b> that might have wanted leaving out.
/// </summary>
/// <remarks>
/// The weak signals live here rather than in <see cref="OmittedLayer"/>, and that
/// split is the whole design. A name that reads like a background is a guess: "Sky"
/// is usually scenery and sometimes the asset, and a rule that dropped it would
/// eventually ship a sprite sheet with a layer quietly missing — the failure an
/// artist finds last, in the engine, on a build. So the strong signal acts and the
/// weak signal advises.
/// </remarks>
public sealed record SuspectedBackground(string LayerId, string Name, string Reason);

/// <summary>
/// Deciding which layers an export leaves out.
/// </summary>
/// <remarks>
/// <para>
/// The policy is here, in Core, with no pixels in it: the caller measures coverage
/// (which needs a rasterizer) and this decides what the measurement means. That
/// keeps the part with the judgement in it testable without composing a frame.
/// </para>
/// <para>
/// <b>Nothing is ever dropped silently.</b> Every omission comes back in
/// <see cref="OmittedLayer"/> and every near-miss in
/// <see cref="SuspectedBackground"/>, because the failure mode being avoided is not
/// "the background got exported" — it is "a layer the artist wanted is missing and
/// they find out in the engine".
/// </para>
/// </remarks>
public static class BackgroundRules
{
    /// <summary>
    /// The fraction of the canvas a layer must cover to count as a full-canvas fill.
    /// </summary>
    /// <remarks>
    /// Not 1.0. A flood fill on an antialiased canvas, a fill run to the edge with a
    /// soft brush, or a one-pixel rounding at a corner all leave a handful of
    /// not-quite-opaque pixels, and a background that failed to be recognised
    /// because of four corner pixels would be worse than useless — it would be
    /// unpredictable. Not lower either: 0.99 of a 1920×1080 canvas is 20 000 pixels,
    /// which is a whole small drawing.
    /// </remarks>
    public const double FullCanvasCoverage = 0.999;

    /// <summary>Names that read like a background, for the advisory signal only.</summary>
    private static readonly string[] BackgroundWords =
        ["background", "backdrop", "bg", "paper", "sky", "backing"];

    /// <summary>
    /// Whether a layer's name reads like a background.
    /// </summary>
    /// <remarks>
    /// <b>Advisory only, and never a reason to omit anything.</b> It is
    /// English-only, it is defeated by any studio convention that does not happen to
    /// match, and "Backdrop" is the layer name of both a thing to drop and a thing
    /// to export. Used to raise a <see cref="SuspectedBackground"/> so a person can
    /// answer, which is the only place a guess this weak belongs.
    /// </remarks>
    public static bool NameLooksLikeBackground(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var lowered = name.Trim().ToLowerInvariant();

        foreach (var word in BackgroundWords)
        {
            // Whole word, so "bg" does not match "bgroup" and — the case that
            // matters — "sky" does not match "skyline".
            var at = lowered.IndexOf(word, StringComparison.Ordinal);
            while (at >= 0)
            {
                var beforeOk = at == 0 || !char.IsLetterOrDigit(lowered[at - 1]);
                var end = at + word.Length;
                var afterOk = end >= lowered.Length || !char.IsLetterOrDigit(lowered[end]);
                if (beforeOk && afterOk) return true;
                at = lowered.IndexOf(word, at + 1, StringComparison.Ordinal);
            }
        }
        return false;
    }

    /// <summary>
    /// Why this layer should be left out, or null to export it.
    /// </summary>
    /// <param name="layer">The layer.</param>
    /// <param name="visible">Whether the scene resolves it as visible, folders included.</param>
    /// <param name="mode">What the export asked for.</param>
    /// <param name="coversCanvas">
    /// Whether every drawing this layer shows covers at least
    /// <see cref="FullCanvasCoverage"/> of the canvas. Only consulted under
    /// <see cref="BackgroundHandling.Detected"/>, so a caller may skip the
    /// measurement entirely in the other modes.
    /// </param>
    public static BackgroundSignal? OmissionFor(
        Layer layer, bool visible, BackgroundHandling mode, bool coversCanvas)
    {
        // The artist's own decision, first and unconditionally. A pinned layer stays
        // out even of Everything, because Everything is a statement about backgrounds
        // and this is a statement about one layer.
        if (layer.IsOmittedFromExport) return BackgroundSignal.Pinned;

        if (!visible) return BackgroundSignal.Hidden;

        // Pinned *in* beats every rule below it, which is what makes a backdrop
        // exportable without turning detection off for the whole document.
        if (layer.IsKeptInExport) return null;

        if (layer.IsBackground && mode != BackgroundHandling.Everything)
            return BackgroundSignal.Paper;

        if (mode == BackgroundHandling.Detected && coversCanvas)
            return BackgroundSignal.FullCanvasFill;

        return null;
    }

    /// <summary>
    /// Whether this kept layer is worth mentioning, or null to say nothing.
    /// </summary>
    /// <remarks>
    /// Deliberately quiet. It fires on a layer that was exported, whose name reads
    /// like a background, and which detection did <em>not</em> catch — so the artist
    /// hears about the case where the two signals disagree and nothing else.
    /// </remarks>
    public static SuspectedBackground? SuspicionFor(Layer layer, BackgroundSignal? omission)
    {
        if (omission is not null) return null;
        if (layer.IsKeptInExport) return null;
        if (!NameLooksLikeBackground(layer.Name)) return null;

        return new SuspectedBackground(
            layer.Id, layer.Name,
            "the name reads like a background, but it does not cover the canvas");
    }
}

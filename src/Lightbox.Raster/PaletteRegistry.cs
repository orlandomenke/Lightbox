using System.Collections.Concurrent;
using Lightbox.Core.Documents;

namespace Lightbox.Raster;

/// <summary>
/// Live palette swatches and gradients, keyed by document id — the same
/// arrangement as <see cref="ClipRegionRegistry"/>. Documents register theirs
/// on load and on change; the engine resolves by id when rendering.
///
/// This is what makes a palette live in the Toon Boom sense: a stroke can
/// record <em>which swatch</em> it was painted with rather than a literal
/// colour, so recolouring the swatch recolours every stroke that references
/// it — across every frame and every layer, raster and vector alike, because
/// both are the same stroke record.
///
/// Note how this sits with invariant 4 ("settings that affect pixels are
/// stored per stroke, not read from global state at render time"). It does
/// not breach it: the palette is <em>document</em> state, saved in the file
/// and versioned with the art, and the link is authored deliberately. The
/// invariant exists to stop an app <em>preference</em> — anti-aliasing, a
/// pressure curve — silently repainting finished work. A swatch the artist
/// edited on purpose is the opposite of that.
/// </summary>
public static class PaletteRegistry
{
    private static readonly ConcurrentDictionary<string, Swatch> Swatches = new();
    private static readonly ConcurrentDictionary<string, Gradient> Gradients = new();

    /// <summary>
    /// Point the registry at a document's palettes, replacing what was there
    /// for those swatches. Unlike the clip registry this overwrites rather
    /// than TryAdd — a clip region is content-hashed and immutable, whereas a
    /// swatch is meant to be edited, and the whole feature is that the new
    /// value wins.
    /// </summary>
    public static void Register(IEnumerable<Palette> palettes)
    {
        foreach (var palette in palettes)
        {
            // Kept twice on purpose: flat for strokes painted before palette ids
            // were recorded, and per palette so a stroke that names one cannot
            // be answered by another's swatch of the same id.
            var owned = new Dictionary<string, Swatch>(StringComparer.Ordinal);
            foreach (var swatch in palette.Swatches)
            {
                if (swatch.Id is not { Length: > 0 } id) continue;
                Swatches[id] = swatch;
                owned[id] = swatch;
            }
            if (palette.Id is { Length: > 0 } pid) Palettes[pid] = owned;
        }
    }

    public static void Register(IReadOnlyDictionary<string, Gradient> gradients)
    {
        foreach (var (id, gradient) in gradients) Gradients[id] = gradient;
    }

    public static void Register(string id, Gradient gradient) => Gradients[id] = gradient;

    /// <summary>
    /// Point the registry at exactly one document's palettes and gradients,
    /// dropping everything else. <see cref="Register(IEnumerable{Palette})"/>
    /// only ever adds, so a deleted swatch would keep resolving and an undo
    /// that replaced the document would leave the old <see cref="Swatch"/>
    /// objects behind — live, and no longer the ones the artist is editing.
    /// </summary>
    public static void Reset(IEnumerable<Palette> palettes, IReadOnlyDictionary<string, Gradient> gradients)
    {
        Clear();
        Register(palettes);
        Register(gradients);
    }

    public static Swatch? ResolveSwatch(string id) => Swatches.GetValueOrDefault(id);

    /// <summary>
    /// The swatch a stroke means, given which palette it was painted from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q30 step 2.</b> A bare swatch id was unambiguous while every document
    /// saw every palette. Scoping ends that: two palettes duplicated from one
    /// another share swatch ids, so the same id resolves to a different colour
    /// depending on where the document is filed, and a document dragged between
    /// folders would recolour.
    /// </para>
    /// <para>
    /// Named palette first, bare id second, and null last so the caller falls
    /// back to the stroke's literal colour. <b>Never guess across palettes:</b>
    /// if the stroke names a palette and that palette is not registered, the
    /// last colour the artist actually saw is a better answer than a stranger's
    /// swatch that happens to share an id.
    /// </para>
    /// </remarks>
    public static Swatch? ResolveSwatch(string? paletteId, string id)
    {
        if (paletteId is not { Length: > 0 }) return ResolveSwatch(id);
        if (!Palettes.TryGetValue(paletteId, out var owned)) return null;
        return owned.GetValueOrDefault(id);
    }

    private static readonly ConcurrentDictionary<string, Dictionary<string, Swatch>> Palettes = new();

    /// <summary>Which palette holds this swatch, or null if none registered does.</summary>
    /// <remarks>
    /// Asked once when an artist picks a colour rather than per dab, so the
    /// stroke can record the pair and rendering never has to search.
    /// </remarks>
    public static string? PaletteOf(string swatchId) =>
        Palettes.FirstOrDefault(p => p.Value.ContainsKey(swatchId)).Key;

    public static Gradient? ResolveGradient(string id) => Gradients.GetValueOrDefault(id);

    /// <summary>Drop everything — a new document must not see the last one's palette.</summary>
    public static void Clear()
    {
        Swatches.Clear();
        Palettes.Clear();
        Gradients.Clear();
    }
}

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
            foreach (var swatch in palette.Swatches)
            {
                if (swatch.Id is { Length: > 0 } id) Swatches[id] = swatch;
            }
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

    public static Gradient? ResolveGradient(string id) => Gradients.GetValueOrDefault(id);

    /// <summary>Drop everything — a new document must not see the last one's palette.</summary>
    public static void Clear()
    {
        Swatches.Clear();
        Gradients.Clear();
    }
}

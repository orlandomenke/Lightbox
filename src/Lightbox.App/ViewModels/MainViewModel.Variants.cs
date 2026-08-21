using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Raster;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The canvas half of subject variants: making the palette a variant names be
/// the one the drawings actually paint with while it is being viewed.
/// </summary>
/// <remarks>
/// <para>
/// The docker owns <em>which</em> variant is active
/// (<see cref="ProjectViewModel.SwitchVariant"/> writes
/// <see cref="Project.ActiveVariant"/>); this owns what that does to pixels.
/// The split matters because the registry is per-active-document — variant
/// palettes follow the tab the way every other project resource does.
/// </para>
/// <para>
/// <b>Why a stand-in rather than just registering the variant's palette.</b>
/// A stroke records the palette it was painted from, and
/// <c>PaletteRegistry.ResolveSwatch</c> never answers from another palette
/// that shares the swatch id (Q30 — two duplicated palettes must not bleed
/// into one another). The variant's copy therefore repaints nothing under its
/// own id; it has to answer <em>for the base palette's id</em>. The facade
/// keeps the variant's <see cref="Swatch"/> instances, so recolouring the
/// variant in the palette docker is live on the canvas the same way any
/// swatch edit is.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    /// <summary>
    /// Replace base palettes with the active variants' copies, and hide every
    /// variant copy from its own id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hiding half is not an optimisation: a variant copy carries the base
    /// palette's swatch ids on purpose, so letting it register beside the base
    /// would leave the flat (legacy, no-palette-id) lookup answering from
    /// whichever palette registered last.
    /// </para>
    /// <para>
    /// <b>A swatch that exists only on the variant's copy</b> is recorded on
    /// strokes under the base palette's id like every other pick — the base id
    /// is the shared slot every variant answers for. So the stroke follows the
    /// swatch while that variant is viewed, and outside it the named palette
    /// owns no such swatch and the stroke keeps its literal ink (Q30's
    /// fallback, wanted here). Pinned by
    /// <c>ASwatchOnlyTheVariantHasFreezesToItsInkOutsideTheVariant</c>.
    /// </para>
    /// </remarks>
    private List<Palette> ApplyVariantStandIns(List<Palette> palettes)
    {
        if (ProjectDocker.Project is not { } project) return palettes;
        var variantOwned = project.VariantPaletteIds().ToHashSet(StringComparer.Ordinal);
        if (variantOwned.Count == 0) return palettes;

        var standIns = project.PaletteStandInsFor((SaveTargetTab ?? ActiveTab)?.Source);
        var result = new List<Palette>(palettes.Count);
        foreach (var palette in palettes)
        {
            if (palette.Id is not { Length: > 0 } id)
            {
                result.Add(palette);
            }
            else if (standIns.TryGetValue(id, out var standIn))
            {
                // The facade: the variant's swatches, answering to the base
                // palette's id. Same Swatch instances, so edits stay live.
                result.Add(new Palette
                {
                    Id = id,
                    Name = standIn.Name,
                    Columns = standIn.Columns,
                    Swatches = standIn.Swatches,
                });
            }
            else if (!variantOwned.Contains(id))
            {
                result.Add(palette);
            }
        }
        return result;
    }

    /// <summary>
    /// The viewed variant changed: re-resolve the palettes and repaint what is
    /// on screen.
    /// </summary>
    /// <remarks>
    /// Deliberately <em>not</em> <c>MarkDocumentEdited</c>: which variant is
    /// being viewed is view state, like the playhead — nothing serialized
    /// moved, so nothing gets dirty. The repaint is every open frame rather
    /// than a per-swatch scan because a variant swaps a whole palette at once;
    /// it runs once per deliberate gesture, never per pointer event, so
    /// invariant 6 is not in play.
    /// </remarks>
    internal void OnVariantSwitched()
    {
        RegisterResources();
        var seen = new HashSet<string>();
        foreach (var layer in Tabs.SelectMany(t => t.Doc.Scene.Layers))
        {
            foreach (var cel in layer.Cels)
            {
                if (cel.Frame is not { } frame || !seen.Add(frame.Id)) continue;
                InvalidateFrameRender(frame.Id);
                _dirtyThumbIds.Add(frame.Id);
            }
        }
        _publish.InvalidateWholeCanvas();
        _composeRing.InvalidateAll();
        PublishSnapshot();
        RefreshThumbnails();
    }
}

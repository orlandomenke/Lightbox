using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The one door the effects window mutates the document through.
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists so <c>InvalidateFrameRender</c> can stay private.</b> That funnel's
/// own remark says a mutation path bypassing it is a defect even when every cache
/// happens to survive — so the answer to "the effects window needs to invalidate"
/// is a method beside the other mutation paths, not a wider accessor. The window's
/// view model stays free of the cache, the compose ring and the publish state,
/// which is what lets it be tested without a canvas.
/// </para>
/// <para>
/// <b>The invalidation goes before the edit, not after</b>, for the reason
/// <c>CommitTransform</c> records: the <c>Changed</c> refresh that <c>Perform</c>
/// raises re-renders from the mutated record, so anything still cached under a
/// surviving frame id has to be gone by then. Ids that <em>do not</em> survive need
/// nothing — a freshly grown cel carries a new id and no cache can answer for it.
/// </para>
/// </remarks>
public sealed partial class MainViewModel
{
    /// <summary>
    /// Replace an element's drawings with a freshly baked set, as one undoable step.
    /// </summary>
    internal void ApplySimBake(SimElement element, IEnumerable<(int Frame, List<Stroke> Strokes)> baked)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(baked);

        foreach (var id in SimFrameIds(element))
        {
            InvalidateFrameRender(id);
            _dirtyThumbIds.Add(id);
        }

        _editor.Perform(doc => SimBakeOps.Apply(doc, element, baked), label: "Bake effect");
    }

    /// <summary>
    /// A copy of whatever the toolbar is holding, for an element to keep as its pen.
    /// </summary>
    /// <remarks>
    /// A copy rather than the working settings themselves: the artist goes on
    /// editing those with the size slider, and an element sharing the object would
    /// have its line change every time they did.
    /// </remarks>
    internal BrushSettings CurrentBrushCopy() => CurrentToolSettings.Clone();

    /// <summary>
    /// Put a group's baked layers in one folder, as one undoable step.
    /// </summary>
    /// <remarks>
    /// A document-wide edit rather than a frame one: folding reorders the layer
    /// stack so the members are contiguous, which is what a layer folder
    /// requires. Nothing about the frames changes, so the caches keyed by frame
    /// are left alone and the <c>Changed</c> refresh does the rest.
    /// </remarks>
    internal void FoldSimGroup(SimGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        _editor.Perform(doc => SimGroupOps.Fold(doc, group), label: "Fold effect layers");
    }

    /// <summary>
    /// Make an effect from a preset, as one undoable step.
    /// </summary>
    /// <remarks>
    /// Nothing to invalidate before it: a preset brings parameters, and nothing
    /// is drawn until somebody bakes. The <c>Changed</c> refresh is all this
    /// needs.
    /// </remarks>
    internal SimGroup? MakeEffectFromPreset(
        EffectPreset preset, double originX, double originY, int firstFrame)
    {
        ArgumentNullException.ThrowIfNull(preset);

        SimGroup? made = null;
        _editor.Perform(
            doc => made = EffectPresets.Instantiate(doc, preset, originX, originY, firstFrame),
            label: "Use effect preset");
        return made;
    }

    /// <summary>Delete a group and every element in it, as one undoable step.</summary>
    internal void DeleteSimGroup(SimGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        foreach (var element in SimGroupOps.Members(Doc, group))
        {
            foreach (var id in SimFrameIds(element))
            {
                InvalidateFrameRender(id);
                _dirtyThumbIds.Add(id);
            }
        }

        _editor.Perform(doc => SimGroupOps.Delete(doc, group), label: "Delete effect");
    }

    /// <summary>
    /// Say an effects element's own record changed — a slider, an emitter, a
    /// colour — so the document is dirty and autosave knows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The window edits the record without going through the editor, and this
    /// is what keeps that honest.</b> Tuning is a loop of a hundred small moves;
    /// one undo step per slider tick would bury the history the artist actually
    /// wants, and the bake — which is the moment the change reaches the drawing —
    /// <em>is</em> one step. What must not happen is the other half: an hour of
    /// tuning with the title bar still saying the document is unchanged.
    /// </para>
    /// <para>
    /// The cost, stated rather than hidden: an undo taken after tuning and before
    /// baking restores the parameters along with the document, because undo swaps
    /// a whole <see cref="Doc"/>. That is a real wart and the fix is a coalescing
    /// step per gesture, which the field rows have no notion of yet — see
    /// <c>docs/DESIGN-fluid-effects.md</c>.
    /// </para>
    /// </remarks>
    internal void NoteEffectEdited() => MarkDocumentEdited();

    /// <summary>Drop this element's drawings, as one undoable step.</summary>
    internal void ClearSimBake(SimElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        foreach (var id in SimFrameIds(element))
        {
            InvalidateFrameRender(id);
            _dirtyThumbIds.Add(id);
        }

        _editor.Perform(doc => SimBakeOps.Clear(doc, element), label: "Clear effect");
    }

    /// <summary>
    /// Every frame id an element's layer currently exposes, each once.
    /// </summary>
    /// <remarks>
    /// A hold is one <see cref="Frame"/> standing in several cels, so walking the
    /// strip yields the same id repeatedly; invalidating it twice is only waste,
    /// but the set also keeps the thumbnail queue honest about how much changed.
    /// </remarks>
    private IEnumerable<string> SimFrameIds(SimElement element)
    {
        var seen = new HashSet<string>();
        foreach (var layer in Scene.Layers)
        {
            if (layer.SimId != element.Id) continue;
            foreach (var cel in layer.Cels)
            {
                if (cel.Frame is { } frame && seen.Add(frame.Id)) yield return frame.Id;
            }
        }
    }
}

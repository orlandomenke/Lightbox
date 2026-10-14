using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>Part of MainViewModel — what Ctrl+C, Ctrl+X and Ctrl+V mean.</summary>
/// <remarks>
/// <para>
/// <b>The owner's rule (B474), in three lines.</b> With a selection up, copy
/// and cut take only what was selected. With none, they take the whole content
/// of the layer. And a paste always lands as a new layer directly above the
/// active one. The timeline's right-click Copy and Paste are the exception,
/// because they say "cel" by name.
/// </para>
/// <para>
/// <b>A selection never falls through.</b> This used to read "the selected
/// lines, else the cel", and a selection that held no strokes — a box over a
/// merged layer, whose drawing is pixels — was taken as no selection at all, so
/// Ctrl+C copied the whole cel and Ctrl+X cut it. A selection that holds
/// nothing now copies nothing, and says so.
/// </para>
/// <para>
/// <b>Picked cels are a selection too.</b> Several cels picked in the timeline
/// with the playhead's among them copy as cels, as they always have; one cel
/// picked is only where the playhead is, and is not.
/// </para>
/// <para>
/// Here rather than in the key handler so the decision can be tested, and so
/// a rebinding in the Configure window carries all of it.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    private const string NothingInTheSelection = "Nothing inside the selection to copy.";

    /// <summary>
    /// Several cels are picked in the timeline and the active layer's cel at
    /// the playhead is one of them.
    /// </summary>
    private bool CelsArePickedAtThePlayhead =>
        _keySelection.Count(k => k.IsCel) > 1
        && _keySelection.Contains(TimelineKey.Cel(ActiveLayerIndex, CurrentFrameIndex));

    /// <summary>Ctrl+C: the selection, else picked cels, else the whole layer.</summary>
    public void EditCopy()
    {
        if (HasCopyableSelection)
        {
            if (!CopySelectedLines()) AiStatus = NothingInTheSelection;
            return;
        }
        if (CelsArePickedAtThePlayhead)
        {
            CopyCurrentCel();
            return;
        }
        CopyLayers();
    }

    /// <summary>Ctrl+X: as <see cref="EditCopy"/>, then take out what was copied.</summary>
    public void EditCut()
    {
        if (HasCopyableSelection)
        {
            if (!CutSelectedLines()) AiStatus = NothingInTheSelection;
            return;
        }
        if (CelsArePickedAtThePlayhead)
        {
            CutCurrentCel();
            return;
        }
        CutLayerContents();
    }

    /// <summary>
    /// Ctrl+V: whichever clipboard was filled last, as a new layer directly
    /// above the active one.
    /// </summary>
    /// <remarks>
    /// Freshest rather than a fixed order, because an artist who has used more
    /// than one kind of copy thinks "the last thing I copied", not "lines
    /// first". The three clipboards stamp themselves on one counter for this.
    /// </remarks>
    public void EditPaste()
    {
        var lines = StrokeClipboard.HasContent ? StrokeClipboard.Stamp : 0;
        var cels = _celClipboard is not null ? _celClipboardStamp : 0;
        var layers = _layerClipboard is not null ? _layerClipboardStamp : 0;
        if (lines == 0 && cels == 0 && layers == 0)
        {
            AiStatus = "Nothing has been copied.";
            return;
        }
        if (layers > lines && layers > cels) PasteLayers();
        else if (cels > lines) PasteCelsAsLayer();
        else PasteLinesAsLayer();
    }

    /// <summary>
    /// Copy the layer, then empty it — every drawing gone, the layer and its
    /// settings kept. One undo step for the emptying.
    /// </summary>
    /// <remarks>
    /// Emptied rather than removed (owner's choice, B474): a Ctrl+X that slips
    /// should not take a layer out of the stack, and the layer's place, mask
    /// and effects are usually what the artist wants to draw into again.
    /// </remarks>
    private void CutLayerContents()
    {
        if (!CopyLayers() || _layerClipboard is not { } held) return;
        var ids = held.Clip.Layers.Select(l => l.Id).ToHashSet();
        var targets = Scene.Layers
            .Where(l => ids.Contains(l.Id) && l.Cels.Any(c => c.Frame is not null))
            .Where(l => CanEdit(l, "cut from it"))
            .Select(l => l.Id)
            .ToHashSet();
        if (targets.Count == 0) return;
        _editor.Perform(doc =>
        {
            foreach (var layer in doc.Scene.Layers.Where(l => targets.Contains(l.Id)))
            {
                foreach (var cel in layer.Cels) cel.Frame = null;
            }
        });
        RefreshThumbnails();
        AiStatus = Describe(held.Clip, "cut") + " The layer stays, emptied.";
    }

    /// <summary>
    /// The copied cels as a new layer directly above the active one, starting
    /// at the playhead — Ctrl+V after a cel was copied. The timeline's own
    /// right-click Paste still pastes onto the cel it was opened on.
    /// </summary>
    public bool PasteCelsAsLayer()
    {
        if (_celClipboard is not { } clip || IsPlaying) return false;

        var place = NewLayerPlacement();
        var start = CurrentFrameIndex;
        string? addedId = null;
        _editor.Perform(doc =>
        {
            var scene = doc.Scene;
            var end = start + clip.Frames.Count;
            if (end > scene.FrameCount)
            {
                scene.FrameCount = end;
                foreach (var other in scene.Layers)
                {
                    while (other.Cels.Count < end) other.Cels.Add(new Cel());
                }
            }
            var layer = new Layer
            {
                Name = $"Pasted {scene.Layers.Count + 1}",
                Kind = clip.Kind,
                Cels = [],
            };
            // Holds paste as holds, and the cels before the playhead stay
            // empty, as a pasted drawing does (PasteLinesAsLayer).
            for (var i = 0; i < scene.FrameCount; i++)
            {
                layer.Cels.Add(new Cel
                {
                    Frame = i >= start && i < end && clip.Frames[i - start] is { } source
                        ? DocumentEditor.CloneFrame(source)
                        : null,
                });
            }
            scene.Layers.Add(layer);
            if (place is { } p) FolderTree.Move(scene, [StackRef.Of(layer)], p.Target, p.Where);
            foreach (var folder in FolderTree.FoldersOf(scene, layer)) folder.Collapsed = false;
            addedId = layer.Id;
        }, frameContentUnchanged: true);

        ActiveLayerIndex = Scene.Layers.FindIndex(l => l.Id == addedId);
        _publish.InvalidateWholeCanvas();
        PublishSnapshot();
        RefreshThumbnails();
        AiStatus = clip.Frames.Count == 1
            ? "Pasted the cel onto a new layer."
            : $"Pasted {clip.Frames.Count} cels onto a new layer.";
        return true;
    }
}

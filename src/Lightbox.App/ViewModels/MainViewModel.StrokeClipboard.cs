using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;
using Lightbox.Raster;

namespace Lightbox.App.ViewModels;

/// <summary>Part of MainViewModel — see MainViewModel.cs.</summary>
/// <remarks>
/// <para>
/// <b>Copy takes exactly what a transform would move.</b> That is the whole
/// design rule here, and it buys two things at once. An artist never has to
/// learn a second answer to "what is selected" — the marquee-beats-picked-lines
/// precedence is Q97's, decided once and read from
/// <see cref="SelectedStrokesForAnOperation"/> by both. And the erased-ink
/// doctrine comes along for free: the classification runs through
/// <see cref="TransformErasures.MovingWithin"/>, so a line rubbed out along its
/// whole length is never copied, exactly as it is never moved (B297) and never
/// clicked (B232). A clipboard that read the raw record would have been the
/// fifth door into the same bug.
/// </para>
/// <para>
/// <b>A region copy clips; a picked-line copy does not.</b> Boxing part of a
/// drawing means the pixels in the box, so the copies carry the selection as a
/// clip and the strokes stay whole underneath it — nothing is torn, and the
/// record still holds the line it was cut from. Picking a line with the Arrow
/// means that line, so it copies entire. The two gestures say different things
/// and the clipboard keeps the difference.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    /// <summary>Is there anything a copy could take — a region, or picked lines?</summary>
    public bool HasCopyableSelection => HasSelection || HasStrokeSelection;

    public bool HasStrokeClipboard => StrokeClipboard.HasContent;

    /// <summary>
    /// The strokes an operation on "the selection" should take, and the clip
    /// that carves them — null when nothing is selected.
    /// </summary>
    /// <remarks>
    /// The clip is non-null only for a region: it is what makes a partial copy
    /// show the part that was boxed. A stroke that already carries one has the
    /// two intersected (<see cref="ClipMeeting"/>), because a copy must never
    /// show ink the original was not showing.
    /// </remarks>
    private List<Stroke>? SelectedStrokesForAnOperation()
    {
        if (PaintTargetOrKey() is not { } frame) return null;

        // A marquee wins over picked lines — Q97, the same order the transform
        // takes, read from the same place so the two can never drift apart.
        if (HasSelection)
        {
            int w = Scene.Width, h = Scene.Height;
            var mask = MaskFromContours(_selectionContours, w, h);
            var caught = TransformErasures.MovingWithin(frame.Strokes, mask, w, h);
            if (caught.Count == 0) return null;
            if (PrepareClipForSelection() is not { } selection) return null;

            var taken = new List<Stroke>(caught.Count);
            var inkSoFar = false;
            foreach (var index in caught)
            {
                var source = frame.Strokes[index];

                // An erasure comes along only to keep carving the ink it is
                // carving — and a copy that has taken no ink yet has nothing
                // for it to carve. This is the one rule a copy needs that a
                // transform does not: a moved erasure still sits over the
                // drawing it always did, but a *copied* one lands on a fresh
                // layer where there is nothing beneath it, so an erasure taken
                // on its own would be exactly the invisible stray Q102 exists
                // to stop — unremovable, because no tool can select an
                // erasure (B232).
                if (IsErasure(source))
                {
                    if (!inkSoFar) continue;
                }
                else
                {
                    inkSoFar = true;
                }

                var copy = source.Clone(newId: false);
                copy.ClipId = source.ClipId is { } already
                    ? ClipMeeting(already, selection.Id, selection.Region)
                    : selection.Id;
                taken.Add(copy);
            }
            // No ink at all means the box holds nothing an artist can see —
            // erased lines and the erasers that removed them. There is nothing
            // to copy, and saying so lets Ctrl+C fall through to the cel.
            return inkSoFar ? taken : null;
        }

        if (!HasStrokeSelection) return null;
        var picked = frame.Strokes.Where(s => Selection.IsStrokeSelected(s.Id)).ToList();
        return picked.Count > 0 ? picked : null;
    }

    /// <summary>
    /// The id of a clip that carves exactly where two clips overlap,
    /// registering it if it is new.
    /// </summary>
    /// <remarks>
    /// <b>Why this cannot be skipped.</b> A stroke painted under a selection is
    /// only visible inside it. Copying a region and giving that stroke the new
    /// region as its clip would replace the old carve rather than add to it —
    /// so the pasted copy would show ink the artist has never seen, which is
    /// the resurrection bug of B297 wearing a different hat. Masks are ANDed
    /// and re-traced rather than the contours being intersected analytically:
    /// the shapes come from a hand-drawn lasso and may be concave, multiple and
    /// holed, and a mask says what a polygon library would have to be trusted
    /// to say.
    /// </remarks>
    private string ClipMeeting(string existingId, string selectionId, ClipRegion selection)
    {
        if (existingId == selectionId) return existingId;
        if (ClipRegionRegistry.Resolve(existingId) is not { } existing) return selectionId;

        int w = Scene.Width, h = Scene.Height;
        var a = MaskFromContours(ToSurfaceContours(existing.Contours), w, h);
        var b = MaskFromContours(ToSurfaceContours(selection.Contours), w, h);
        for (var i = 0; i < a.Length; i++) a[i] = a[i] && b[i];

        var region = new ClipRegion
        {
            Contours = ToDocument(FloodFill.TraceAllContours(a, w, h)),
            // The softer of the two edges would show through the harder one, so
            // the meeting keeps the harder — it is the one that actually cuts.
            Feather = Math.Min(existing.Feather, selection.Feather),
        };
        return RegisterClip(region);
    }

    /// <summary>Document-space contours as surface-space ones — the inverse of ToDocument.</summary>
    private List<List<StrokePoint>> ToSurfaceContours(IEnumerable<List<StrokePoint>> contours)
    {
        int dx = Scene.Left, dy = Scene.Top;
        return [.. contours.Select(c => dx == 0 && dy == 0
            ? new List<StrokePoint>(c)
            : [.. c.Select(pt => pt with { X = pt.X - dx, Y = pt.Y - dy })])];
    }
    // The menu's three entries. Commands rather than click handlers because
    // every other entry on the Select menu is one and a test holds the menu to
    // it — an entry wired straight to a handler is invisible to anything that
    // enumerates the menu, which is the same class of miss as a shortcut that
    // never reached ShortcutMap. The bool-returning forms below stay, because
    // the Ctrl+C/X/V path needs the answer to decide whether to fall through
    // to the cel; a command has no use for it.
    [RelayCommand]
    private void CopyLines() => CopySelectedLines();

    [RelayCommand]
    private void CutLines() => CutSelectedLines();

    [RelayCommand]
    private void PasteLines() => PasteLinesAsLayer();

    /// <summary>
    /// Copy the selected lines (or the boxed region) to the line clipboard.
    /// Returns false when there was no selection to take, so the key handler
    /// can fall through to the cel clipboard.
    /// </summary>
    public bool CopySelectedLines()
    {
        if (SelectedStrokesForAnOperation() is not { } taken) return false;
        StrokeClipboard.Put(taken, ClipRegionRegistry.Resolve);
        AiStatus = taken.Count == 1 ? "Copied the line." : $"Copied {taken.Count} lines.";
        return true;
    }

    /// <summary>
    /// Copy, then take the same content out of the drawing. Returns false when
    /// there was nothing selected.
    /// </summary>
    /// <remarks>
    /// <b>The removal is <see cref="DeleteSelectionContentsCommand"/>, not a
    /// deletion of the strokes this copied.</b> That command already answers
    /// the two halves correctly and differently: a boxed region becomes a
    /// <see cref="ToolKind.ClearRegion"/> stroke, so lines crossing the edge
    /// keep the part outside the box and lose only what was in it; picked lines
    /// are removed outright, because picking a line means the line. Deleting
    /// what the copy took would have been wrong for the first case — a stroke
    /// half inside the box would have vanished entirely.
    /// </remarks>
    public bool CutSelectedLines()
    {
        if (!CopySelectedLines()) return false;
        var count = StrokeClipboard.Count;
        DeleteSelectionContentsCommand.Execute(null);
        AiStatus = count == 1 ? "Cut the line." : $"Cut {count} lines.";
        return true;
    }

    /// <summary>
    /// Paste the copied lines onto a new layer directly above the active one,
    /// at the position they were copied from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>In place, and a layer of its own.</b> In place because the common
    /// gesture is carrying a drawing to another frame or another shot, where
    /// landing anywhere but the original coordinates means lining it up by hand
    /// every time; the tools are right there to move it afterwards if that is
    /// what was wanted. A layer of its own because a paste is not part of the
    /// drawing underneath it until the artist says so — it arrives movable,
    /// undoable and mergeable, and nothing that was already on the page is
    /// touched.
    /// </para>
    /// <para>
    /// The clips come with it (invariant 3): a partial copy is only the shape
    /// it was boxed as if the region that carved it is in this document too, so
    /// the regions are added here rather than left in the process registry
    /// where a save would not find them.
    /// </para>
    /// </remarks>
    public bool PasteLinesAsLayer()
    {
        if (StrokeClipboard.Take() is not { } payload) return false;
        if (IsPlaying) return false;

        var at = Math.Clamp(ActiveLayerIndex + 1, 0, Scene.Layers.Count);
        var frameIndex = CurrentFrameIndex;
        _editor.Perform(doc =>
        {
            foreach (var (id, region) in payload.Clips) doc.ClipRegions.TryAdd(id, region);

            var layer = new Layer
            {
                Name = $"Pasted {doc.Scene.Layers.Count + 1}",
                Kind = LayerKind.Painted,
                Cels = [],
            };
            // The paste lands on the cel the playhead is on, and the cels before
            // it stay empty: a drawing pasted at frame 12 belongs at frame 12,
            // not at the head of a layer that then holds it for twelve frames.
            for (var i = 0; i < Math.Max(doc.Scene.FrameCount, frameIndex + 1); i++)
            {
                layer.Cels.Add(new Cel
                {
                    Frame = i == frameIndex ? new Frame { Strokes = [.. payload.Strokes] } : null,
                });
            }
            doc.Scene.Layers.Insert(Math.Min(at, doc.Scene.Layers.Count), layer);
        }, frameContentUnchanged: true);

        ActiveLayerIndex = Math.Min(at, Scene.Layers.Count - 1);
        _publish.InvalidateWholeCanvas();
        PublishSnapshot();
        RefreshThumbnails();
        OnPropertyChanged(nameof(HasStrokeClipboard));
        AiStatus = payload.Strokes.Count == 1
            ? "Pasted the line onto a new layer."
            : $"Pasted {payload.Strokes.Count} lines onto a new layer.";
        return true;
    }
}

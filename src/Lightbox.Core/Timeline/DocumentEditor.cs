using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Timeline;

/// <summary>
/// The single entry point for document mutations, with snapshot-based
/// undo/redo. Callers mutate through methods here (or wrap ad-hoc edits in
/// <see cref="Perform"/>) so every change is undoable.
///
/// Snapshots are JSON clones — cheap at pencil-test scale; to be replaced by
/// command deltas when heavy raster editing arrives (flagged in the plan).
/// </summary>
public sealed class DocumentEditor
{
    private readonly Stack<Doc> _undo = new();
    private readonly Stack<Doc> _redo = new();
    private const int MaxUndo = 64;

    public Doc Doc { get; private set; }

    public event Action? Changed;

    public DocumentEditor(Doc doc)
    {
        Doc = doc;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Run a mutation as one undoable step.</summary>
    public void Perform(Action<Doc> mutate)
    {
        PushUndo();
        mutate(Doc);
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(Doc);
        Doc = _undo.Pop();
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(Doc);
        Doc = _redo.Pop();
        Changed?.Invoke();
    }

    private void PushUndo()
    {
        _undo.Push(DocJson.Clone(Doc));
        if (_undo.Count > MaxUndo)
        {
            // Stack has no trim; rebuild without the oldest entry.
            var items = _undo.ToArray(); // newest..oldest
            _undo.Clear();
            for (var i = items.Length - 2; i >= 0; i--) _undo.Push(items[i]);
        }
        _redo.Clear();
    }

    // ---- Timeline operations ----------------------------------------------

    /// <summary>Insert a new keyed (empty) frame on every layer after index i.</summary>
    public void AddFrameAfter(int i)
    {
        Perform(doc =>
        {
            var at = Math.Clamp(i + 1, 0, doc.Scene.FrameCount);
            foreach (var layer in doc.Scene.Layers)
            {
                PadCels(layer, doc.Scene.FrameCount);
                layer.Cels.Insert(at, new Cel { Frame = NewEmptyFrame(layer) });
            }
            doc.Scene.FrameCount++;
        });
    }

    /// <summary>Duplicate the exposed frame at index i into a new cel after it.</summary>
    public void DuplicateFrame(int i)
    {
        Perform(doc =>
        {
            var at = Math.Clamp(i + 1, 0, doc.Scene.FrameCount);
            foreach (var layer in doc.Scene.Layers)
            {
                PadCels(layer, doc.Scene.FrameCount);
                var src = ExposureSheet.ExposedFrame(layer, i);
                layer.Cels.Insert(at, new Cel { Frame = CloneFrame(src) });
            }
            doc.Scene.FrameCount++;
        });
    }

    public void DeleteFrame(int i)
    {
        Perform(doc =>
        {
            if (doc.Scene.FrameCount <= 1) return;
            foreach (var layer in doc.Scene.Layers)
            {
                PadCels(layer, doc.Scene.FrameCount);
                if (i < layer.Cels.Count) layer.Cels.RemoveAt(i);
            }
            doc.Scene.FrameCount--;
        });
    }

    /// <summary>
    /// Insert already-built inbetween frames on a layer between key indices
    /// a and b (exclusive). Frames may come from the deterministic engine or
    /// the AI — same code path. Existing cels between a and b are replaced;
    /// if there aren't enough cels, new ones are inserted.
    /// </summary>
    public void InsertInbetweens(string layerId, int aIndex, IReadOnlyList<Frame> frames)
    {
        Perform(doc =>
        {
            var layer = doc.Scene.Layers.First(l => l.Id == layerId);
            PadCels(layer, doc.Scene.FrameCount);
            var bIndex = ExposureSheet.NextKeyIndex(layer, aIndex);
            var gap = bIndex < 0 ? 0 : bIndex - aIndex - 1;
            var replace = Math.Min(gap, frames.Count);

            for (var k = 0; k < replace; k++)
                layer.Cels[aIndex + 1 + k].Frame = frames[k];

            var extra = frames.Count - replace;
            for (var k = 0; k < extra; k++)
            {
                var at = aIndex + 1 + replace + k;
                foreach (var other in doc.Scene.Layers)
                {
                    PadCels(other, doc.Scene.FrameCount);
                    other.Cels.Insert(at, new Cel
                    {
                        Frame = other.Id == layerId ? frames[replace + k] : null,
                    });
                }
                doc.Scene.FrameCount++;
            }
        });
    }

    /// <summary>
    /// Make the cel at <paramref name="index"/> a drawn frame with the given
    /// role — creating an empty frame on a hold cel, or re-marking an existing
    /// one. An index beyond the timeline extends it (holds on every layer).
    /// One undo step.
    /// </summary>
    public void SetKeyAt(string layerId, int index, FrameRole role)
    {
        if (index < 0) return;
        Perform(doc =>
        {
            var scene = doc.Scene;
            if (index >= scene.FrameCount) scene.FrameCount = index + 1;
            foreach (var layer in scene.Layers) PadCels(layer, scene.FrameCount);

            var target = scene.Layers.First(l => l.Id == layerId);
            var cel = target.Cels[index];
            if (cel.Frame is null)
            {
                cel.Frame = NewEmptyFrame(target);
            }
            cel.Frame.Role = role;
        });
    }

    /// <summary>
    /// Extend the drawing exposed at <paramref name="index"/> by one frame:
    /// a hold cel is inserted after it on this layer only, shifting the rest
    /// of the layer right (other layers are untouched — classic X-sheet).
    /// </summary>
    public void ExtendExposure(string layerId, int index)
    {
        if (index < 0 || FindLayer(layerId) is null) return;
        Perform(doc =>
        {
            var layer = doc.Scene.Layers.First(l => l.Id == layerId);
            PadCels(layer, doc.Scene.FrameCount);
            var at = Math.Clamp(index + 1, 0, layer.Cels.Count);
            layer.Cels.Insert(at, new Cel());
            if (layer.Cels.Count > doc.Scene.FrameCount) doc.Scene.FrameCount = layer.Cels.Count;
        });
    }

    /// <summary>
    /// Shorten the exposure at <paramref name="index"/> by one frame: the hold
    /// cel directly after it is removed, pulling the rest of the layer left.
    /// A drawing is never removed — no-op when the next cel is keyed.
    /// </summary>
    public void ReduceExposure(string layerId, int index)
    {
        var layer = FindLayer(layerId);
        if (layer is null || index < 0) return;
        var next = index + 1;
        if (next >= layer.Cels.Count || layer.Cels[next].Frame is not null) return;
        Perform(doc =>
        {
            var target = doc.Scene.Layers.First(l => l.Id == layerId);
            if (next < target.Cels.Count && target.Cels[next].Frame is null) target.Cels.RemoveAt(next);
        });
    }

    /// <summary>
    /// Remove the drawing at exactly <paramref name="index"/> — the cel
    /// becomes a hold, so the previous drawing shows through. No-op on holds.
    /// </summary>
    public void ClearCel(string layerId, int index)
    {
        var layer = FindLayer(layerId);
        if (layer is null || index < 0 || index >= layer.Cels.Count || layer.Cels[index].Frame is null) return;
        Perform(doc =>
        {
            var target = doc.Scene.Layers.First(l => l.Id == layerId);
            if (index < target.Cels.Count) target.Cels[index].Frame = null;
        });
    }

    /// <summary>
    /// Put a drawing at exactly (layer, index) — the paste half of
    /// copy/paste. Replaces whatever the cel held; pasting beyond the end
    /// extends the timeline (holds everywhere else).
    /// </summary>
    public void SetFrameAt(string layerId, int index, Frame frame)
    {
        if (index < 0 || FindLayer(layerId) is null) return;
        Perform(doc =>
        {
            var scene = doc.Scene;
            if (index >= scene.FrameCount) scene.FrameCount = index + 1;
            foreach (var layer in scene.Layers) PadCels(layer, scene.FrameCount);
            scene.Layers.First(l => l.Id == layerId).Cels[index].Frame = frame;
        });
    }

    /// <summary>
    /// Move a layer within the stack. <paramref name="delta"/> is in
    /// Scene.Layers order: +1 moves it up toward the viewer.
    /// </summary>
    public void MoveLayer(string layerId, int delta)
    {
        var layers = Doc.Scene.Layers;
        var from = layers.FindIndex(l => l.Id == layerId);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= layers.Count || delta == 0) return;
        Perform(doc =>
        {
            var list = doc.Scene.Layers;
            var i = list.FindIndex(l => l.Id == layerId);
            var layer = list[i];
            list.RemoveAt(i);
            list.Insert(i + delta, layer);
        });
    }

    private Layer? FindLayer(string layerId) => Doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId);

    private static void PadCels(Layer layer, int frameCount)
    {
        while (layer.Cels.Count < frameCount) layer.Cels.Add(new Cel());
    }

    private static Frame NewEmptyFrame(Layer layer) => layer.Kind switch
    {
        LayerKind.Vector => new VectorFrame(),
        _ => new PaintedFrame(),
    };

    /// <summary>Deep-clone a frame with a fresh id (ids key the render cache).</summary>
    public static Frame? CloneFrame(Frame? src) => src switch
    {
        null => null,
        VectorFrame v => new VectorFrame { Role = v.Role, Strokes = v.Strokes.Select(s => s.Clone()).ToList() },
        PaintedFrame p => new PaintedFrame
        {
            Role = p.Role,
            PngBase64 = p.PngBase64,
            Strokes = p.Strokes.Select(s => s.Clone()).ToList(),
        },
        _ => throw new InvalidOperationException($"Unknown frame type {src.GetType().Name}"),
    };
}

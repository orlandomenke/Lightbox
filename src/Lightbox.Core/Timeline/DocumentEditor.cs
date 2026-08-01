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
    private readonly Stack<IEditStep> _undo = new();
    private readonly Stack<IEditStep> _redo = new();
    /// <summary>
    /// Undo steps kept. Stroke commits are cheap deltas, but structural edits
    /// snapshot the whole document, so on a large scene this trades memory
    /// for history depth.
    /// </summary>
    public int MaxUndo { get; set; } = 64;

    public Doc Doc { get; private set; }

    public event Action? Changed;

    public DocumentEditor(Doc doc)
    {
        Doc = doc;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Run a mutation as one undoable step (whole-document snapshot).</summary>
    public void Perform(Action<Doc> mutate)
    {
        PushStep(new SnapshotStep(DocJson.Clone(Doc)));
        mutate(Doc);
        Changed?.Invoke();
    }

    /// <summary>
    /// Run a mutation as one undoable step WITHOUT snapshotting the document —
    /// the hot path for stroke commits, where serializing the whole document
    /// per pen lift caused a visible pause. <paramref name="apply"/> must be
    /// re-runnable (redo) and <paramref name="revert"/> must exactly undo it.
    /// <paramref name="affectedFrameId"/> lets undo/redo invalidate only that
    /// frame instead of every cached bitmap and thumbnail.
    /// </summary>
    public void PerformDelta(Action<Doc> apply, Action<Doc> revert, string? affectedFrameId = null)
    {
        PushStep(new DeltaStep(apply, revert, affectedFrameId));
        apply(Doc);
        Changed?.Invoke();
    }

    /// <summary>What an undo/redo touched: nothing, one frame, or the whole document.</summary>
    public readonly record struct EditScope(bool Any, string? FrameId)
    {
        public bool DocumentWide => Any && FrameId is null;
    }

    public void Undo() => UndoScoped();

    public void Redo() => RedoScoped();

    public EditScope UndoScoped()
    {
        if (_undo.Count == 0) return new EditScope(false, null);
        var step = _undo.Pop();
        Doc = step.Rollback(Doc);
        _redo.Push(step);
        Changed?.Invoke();
        return new EditScope(true, step.FrameId);
    }

    public EditScope RedoScoped()
    {
        if (_redo.Count == 0) return new EditScope(false, null);
        var step = _redo.Pop();
        Doc = step.Apply(Doc);
        _undo.Push(step);
        Changed?.Invoke();
        return new EditScope(true, step.FrameId);
    }

    private void PushStep(IEditStep step)
    {
        _undo.Push(step);
        if (_undo.Count > MaxUndo)
        {
            // Stack has no trim; rebuild without the oldest entry.
            var items = _undo.ToArray(); // newest..oldest
            _undo.Clear();
            for (var i = items.Length - 2; i >= 0; i--) _undo.Push(items[i]);
        }
        _redo.Clear();
    }

    /// <summary>One entry on the undo/redo stacks.</summary>
    private interface IEditStep
    {
        /// <summary>The single frame this step touches, or null for document-wide.</summary>
        string? FrameId { get; }

        /// <summary>Take the document back to before this step; returns the doc to use.</summary>
        Doc Rollback(Doc doc);

        /// <summary>Re-apply this step; returns the doc to use.</summary>
        Doc Apply(Doc doc);
    }

    /// <summary>Classic whole-document snapshot: rollback/apply swap the doc instance.</summary>
    private sealed class SnapshotStep(Doc other) : IEditStep
    {
        private Doc _other = other;

        public string? FrameId => null; // whole-document

        public Doc Rollback(Doc doc) => Swap(doc);

        public Doc Apply(Doc doc) => Swap(doc);

        private Doc Swap(Doc doc)
        {
            var restored = _other;
            _other = doc;
            return restored;
        }
    }

    /// <summary>Targeted mutation with an exact inverse — no document clone.</summary>
    private sealed class DeltaStep(Action<Doc> apply, Action<Doc> revert, string? frameId) : IEditStep
    {
        public string? FrameId => frameId;

        public Doc Rollback(Doc doc)
        {
            revert(doc);
            return doc;
        }

        public Doc Apply(Doc doc)
        {
            apply(doc);
            return doc;
        }
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
    /// Move (or, with <paramref name="copy"/>, duplicate) the drawing keyed at
    /// <paramref name="fromIndex"/> to <paramref name="toIndex"/> on the same
    /// layer — the drag-a-cel operation. Whatever the target cel held is
    /// replaced; dropping past the end extends the timeline.
    /// </summary>
    public void MoveCel(string layerId, int fromIndex, int toIndex, bool copy = false)
    {
        var layer = FindLayer(layerId);
        if (layer is null || fromIndex == toIndex || fromIndex < 0 || toIndex < 0) return;
        if (fromIndex >= layer.Cels.Count || layer.Cels[fromIndex].Frame is null) return;
        Perform(doc =>
        {
            var scene = doc.Scene;
            if (toIndex >= scene.FrameCount) scene.FrameCount = toIndex + 1;
            foreach (var l in scene.Layers) PadCels(l, scene.FrameCount);
            var target = scene.Layers.First(l => l.Id == layerId);
            var frame = target.Cels[fromIndex].Frame!;
            target.Cels[toIndex].Frame = copy ? CloneFrame(frame) : frame;
            if (!copy) target.Cels[fromIndex].Frame = null;
        });
    }

    /// <summary>Clear every drawing in [from, to] on a layer (cels become holds). One undo step.</summary>
    public void ClearCels(string layerId, int from, int to)
    {
        var layer = FindLayer(layerId);
        if (layer is null) return;
        (from, to) = (Math.Min(from, to), Math.Max(from, to));
        if (!Enumerable.Range(from, to - from + 1).Any(i => i < layer.Cels.Count && layer.Cels[i].Frame is not null)) return;
        Perform(doc =>
        {
            var target = doc.Scene.Layers.First(l => l.Id == layerId);
            for (var i = Math.Max(0, from); i <= to && i < target.Cels.Count; i++)
            {
                target.Cels[i].Frame = null;
            }
        });
    }

    /// <summary>
    /// Write a sequence of cels starting at <paramref name="start"/> — the
    /// paste-a-range operation. A null entry makes that cel a hold, exactly as
    /// it was copied. Extends the timeline as needed. One undo step.
    /// </summary>
    public void SetFrameRange(string layerId, int start, IReadOnlyList<Frame?> frames)
    {
        if (start < 0 || frames.Count == 0 || FindLayer(layerId) is null) return;
        Perform(doc =>
        {
            var scene = doc.Scene;
            var end = start + frames.Count - 1;
            if (end >= scene.FrameCount) scene.FrameCount = end + 1;
            foreach (var l in scene.Layers) PadCels(l, scene.FrameCount);
            var target = scene.Layers.First(l => l.Id == layerId);
            for (var k = 0; k < frames.Count; k++)
            {
                target.Cels[start + k].Frame = frames[k];
            }
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

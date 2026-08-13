using System.Runtime.CompilerServices;
using System.Text;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Timeline;

/// <summary>
/// The single entry point for document mutations, with snapshot-based
/// undo/redo. Callers mutate through methods here (or wrap ad-hoc edits in
/// <see cref="Perform"/>) so every change is undoable.
///
/// Two kinds of step, and the difference is the whole performance story.
/// <see cref="PerformDelta"/> carries an apply/revert pair and touches nothing
/// else — that is the path a stroke commit takes, at ~0.002 ms to push.
/// <see cref="Perform"/> freezes the whole document, which is what a structural
/// edit needs when its inverse is not expressible as a closure.
///
/// This remark used to read *"snapshots are JSON clones — cheap at pencil-test
/// scale; to be replaced by command deltas when heavy raster editing arrives"*,
/// and it was right about the scale it named. Heavy raster editing arrived and
/// the snapshot became a one-second freeze on adding a layer (B142). The clone
/// is gone — a snapshot is now compact UTF-8 held frozen until an undo asks for
/// it — but the sentence's actual prediction still stands: the structural edits
/// that <em>can</em> express an inverse should become deltas, and most of them
/// can, because most touch one layer or one list rather than the document.
/// </summary>
public sealed class DocumentEditor
{
    private readonly Stack<Entry> _undo = new();
    private readonly Stack<Entry> _redo = new();

    /// <summary>A step, the revision the document reached by applying it, and its name.</summary>
    private readonly record struct Entry(IEditStep Step, long Revision, string Label);

    /// <summary>Hands out revision numbers; never reused, never reset.</summary>
    private long _nextRevision;

    /// <summary>
    /// Which edit the document currently stands at. Zero is "as it was created
    /// or loaded"; every step reaches a new number, and undo returns to the
    /// number the previous step reached.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B98.</b> This exists so a caller can ask *whether the document
    /// differs from what was saved* instead of asserting that it does.
    /// `IsDirty` used to be set by each edit path, which meant any path that
    /// forgot to check whether anything actually changed raised the badge —
    /// B79, B94, B95 and B96 were four of those in one week — and no path could
    /// ever lower it again, which was B97.
    /// </para>
    /// <para>
    /// <b>Read the top of the stack, never its depth.</b> <see cref="MaxUndo"/>
    /// trims the *bottom*, so a depth comparison reads clean on the 65th edit
    /// after a save. That failure is silent and it loses work, which is why the
    /// number is carried per step rather than counted.
    /// </para>
    /// <para>
    /// Trimming is still honest at the other end: undoing to the oldest step the
    /// stack still holds lands on that step's revision rather than on zero, so a
    /// document whose earliest edits can no longer be undone correctly keeps
    /// reading as changed.
    /// </para>
    /// </remarks>
    public long Revision => _undo.Count > 0 ? _undo.Peek().Revision : 0;
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
    /// <remarks>
    /// <b>The snapshot is a direct deep copy, not a serialize-and-parse (B142).</b>
    /// This read <c>DocJson.Clone(Doc)</c>, which made every structural edit — add a
    /// layer, edit a palette, apply a template, 43 call sites — build the whole
    /// document as JSON text and parse it back. Measured on a 5 000-stroke painting:
    /// 615 ms warm, ~1.1 s on the first one after opening, 72.5 MB allocated. Adding
    /// a layer froze, and got worse the longer the painting went on.
    /// <para>
    /// <see cref="Doc.Clone"/> walks the graph instead. Same guarantee — nothing
    /// shared with the live document — without the text. B142 was fixed twice in
    /// parallel, and this is the survivor: the other fix froze the document to
    /// UTF-8 bytes here and parsed them back lazily on the first undo, which cut
    /// the edit to ~70 ms and moved the parse to Ctrl+Z. The clone is another
    /// order of magnitude cheaper on the edit (5.8 ms at the same scale) and
    /// leaves nothing for the undo to pay, so the laziness had nothing left to
    /// defer. The freeze design's fidelity suite survives it —
    /// <c>UndoSnapshotFidelityTests</c> compares whole serialized documents
    /// across the round trip and does not care how the copy was made.
    /// </para>
    /// </remarks>
    public void Perform(Action<Doc> mutate, string? label = null, [CallerMemberName] string caller = "")
    {
        PushStep(new SnapshotStep(Doc.Clone()), label ?? Humanize(caller));
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
    public void PerformDelta(
        Action<Doc> apply, Action<Doc> revert, string? affectedFrameId = null,
        string? label = null, [CallerMemberName] string caller = "")
    {
        PushStep(new DeltaStep(apply, revert, affectedFrameId), label ?? Humanize(caller));
        apply(Doc);
        Changed?.Invoke();
    }

    /// <summary>
    /// "CommitStroke" → "Commit stroke". The default naming for the history
    /// panel: every step arrives named after the method that made it, via
    /// <c>CallerMemberName</c>, so sixty call sites did not need editing to
    /// give the history words — and the handful whose method name reads badly
    /// pass an explicit label instead.
    /// </summary>
    private static string Humanize(string member)
    {
        if (member.Length == 0) return "Edit";
        var text = new StringBuilder(member.Length + 8);
        text.Append(char.ToUpperInvariant(member[0]));
        for (var i = 1; i < member.Length; i++)
        {
            var c = member[i];
            if (char.IsUpper(c) && !char.IsUpper(member[i - 1]))
            {
                text.Append(' ');
                text.Append(char.ToLowerInvariant(c));
            }
            else
            {
                text.Append(c);
            }
        }
        return text.ToString();
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
        var entry = _undo.Pop();
        Doc = entry.Step.Rollback(Doc);
        _redo.Push(entry);
        Changed?.Invoke();
        return new EditScope(true, entry.Step.FrameId);
    }

    public EditScope RedoScoped()
    {
        if (_redo.Count == 0) return new EditScope(false, null);
        var entry = _redo.Pop();
        Doc = entry.Step.Apply(Doc);
        // Pushed with the revision it originally reached, not a fresh one, so
        // redoing back to a saved state reads as saved rather than as new work.
        _undo.Push(entry);
        Changed?.Invoke();
        return new EditScope(true, entry.Step.FrameId);
    }

    private void PushStep(IEditStep step, string label)
    {
        _undo.Push(new Entry(step, ++_nextRevision, label));
        if (_undo.Count > MaxUndo)
        {
            // Stack has no trim; rebuild without the oldest entry.
            var items = _undo.ToArray(); // newest..oldest
            _undo.Clear();
            for (var i = items.Length - 2; i >= 0; i--) _undo.Push(items[i]);
            _trimmed = true;
        }
        _redo.Clear();
    }

    // ---- the history, as the panel reads it ---------------------------------

    /// <summary>Whether the oldest edits have been dropped by <see cref="MaxUndo"/>.</summary>
    /// <remarks>
    /// The panel's root row ("as opened") is only honest while this is false —
    /// once trimming starts, revision zero is a state undo can no longer
    /// reach, and offering a row that cannot be jumped to is a button that
    /// silently does less than it says.
    /// </remarks>
    public bool HistoryTrimmed => _trimmed;

    private bool _trimmed;

    /// <summary>One line of the history: a named state the document can stand at.</summary>
    /// <param name="IsUndone">
    /// True for the part ahead of the current state — steps that have been
    /// undone and are still redoable. The panel dims them.
    /// </param>
    public readonly record struct HistoryEntry(long Revision, string Label, bool IsUndone);

    /// <summary>
    /// Every state the stacks can reach, oldest first: the undo line up to the
    /// current state, then what has been undone, in the order redo would
    /// replay it. At most <see cref="MaxUndo"/> rows of labels — reading it
    /// allocates a small list and touches no document.
    /// </summary>
    public IReadOnlyList<HistoryEntry> History
    {
        get
        {
            var rows = new List<HistoryEntry>(_undo.Count + _redo.Count);
            foreach (var entry in _undo.Reverse())
                rows.Add(new HistoryEntry(entry.Revision, entry.Label, IsUndone: false));
            // A stack enumerates top-first, and the redo top is the next step
            // forward — so this is already chronological.
            foreach (var entry in _redo)
                rows.Add(new HistoryEntry(entry.Revision, entry.Label, IsUndone: true));
            return rows;
        }
    }

    /// <summary>
    /// Walk undo or redo until the document stands at
    /// <paramref name="revision"/> — what clicking a history row does. Zero
    /// means "as opened", reachable only while nothing has been trimmed.
    /// </summary>
    /// <returns>
    /// The union of what the steps touched: one frame when every step agreed,
    /// document-wide when they did not, nothing when already there.
    /// </returns>
    public EditScope JumpTo(long revision)
    {
        var merged = new EditScope(false, null);
        var first = true;
        while (Revision > revision && CanUndo) Take(UndoScoped());
        while (Revision < revision && CanRedo) Take(RedoScoped());
        return merged;

        void Take(EditScope scope)
        {
            if (!scope.Any) return;
            merged = first ? scope
                : new EditScope(true, merged.FrameId == scope.FrameId ? merged.FrameId : null);
            first = false;
        }
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

    /// <summary>
    /// Whole-document snapshot: rollback/apply swap the document this step
    /// leads away from for the one it leads back to.
    /// </summary>
    /// <remarks>
    /// <b>The other side is a live clone, held as-is (B142).</b> A frozen-bytes
    /// variant of this class existed briefly — serialize eagerly, parse lazily
    /// on the first undo — built when taking the snapshot cost hundreds of
    /// milliseconds and deferring the parse was worth a two-state object.
    /// <see cref="Doc.Clone"/> made the snapshot cheaper than the serialize
    /// half alone, so the step holds the document itself and undo pays
    /// nothing. Swapping rather than copying is what makes redo exact: the
    /// step always holds whichever document is not current.
    /// </remarks>
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

    /// <summary>
    /// Insert a new keyed (empty) frame on every layer after index i — except
    /// the paper, which <b>holds</b>.
    ///
    /// A blank key on a background layer shadows the paper, so adding a second
    /// frame used to leave it transparent: an empty drawing where the artist
    /// expects the same sheet of paper they started on. Paper is not animated;
    /// exposing it as a hold is what a paper layer means.
    /// </summary>
    public void AddFrameAfter(int i)
    {
        Perform(doc =>
        {
            var at = Math.Clamp(i + 1, 0, doc.Scene.FrameCount);
            foreach (var layer in doc.Scene.Layers)
            {
                PadCels(layer, doc.Scene.FrameCount);
                layer.Cels.Insert(at, new Cel { Frame = layer.IsBackground ? null : NewEmptyFrame(layer) });
            }
            doc.Scene.FrameCount++;
            RippleReferences(doc.Scene, at, +1);
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
            RippleReferences(doc.Scene, at, +1);
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
            RippleReferences(doc.Scene, i, -1);
        });
    }

    /// <summary>
    /// Move imported references along with a timeline edit.
    /// </summary>
    /// <remarks>
    /// You insert a frame in order to draw an inbetween, so the reference for
    /// the next extreme belongs <i>after</i> the new frame, not on it — and the
    /// new frame gets no reference, because there is no reference drawing for
    /// a drawing that did not exist a moment ago.
    ///
    /// A strip with <see cref="ReferenceStrip.FollowsTimeline"/> off is pinned
    /// to absolute timing and stays where it is; that is the whole point of the
    /// switch. Nothing here happens at all on a document with no references,
    /// which is every document until somebody imports one.
    /// </remarks>
    private static void RippleReferences(Scene scene, int at, int delta)
    {
        if (scene.References is not { Count: > 0 } strips) return;
        foreach (var strip in strips)
        {
            if (!strip.FollowsTimeline) continue;
            if (delta > 0)
            {
                if (at <= strip.Slots.Count) strip.Slots.Insert(at, -1);
            }
            else if (at < strip.Slots.Count)
            {
                strip.Slots.RemoveAt(at);
            }
        }
    }

    /// <summary>
    /// Insert already-built inbetween frames on a layer between key indices
    /// a and b (exclusive). Frames may come from the deterministic engine or
    /// the AI — same code path. Existing cels between a and b are replaced;
    /// if there aren't enough cels, new ones are inserted.
    /// </summary>
    /// <remarks>
    /// A null entry means "leave that slot alone" — it keeps its cel (a hold)
    /// rather than receiving a drawing. That is how a per-frame AI refusal
    /// (Q32) inserts three frames of four without shifting the surviving ones
    /// off their own timing: each accepted frame stays at its t's slot.
    /// </remarks>
    public void InsertInbetweens(string layerId, int aIndex, IReadOnlyList<Frame?> frames)
    {
        Perform(doc =>
        {
            var layer = doc.Scene.Layers.First(l => l.Id == layerId);
            PadCels(layer, doc.Scene.FrameCount);
            var bIndex = ExposureSheet.NextKeyIndex(layer, aIndex);
            var gap = bIndex < 0 ? 0 : bIndex - aIndex - 1;
            var replace = Math.Min(gap, frames.Count);

            for (var k = 0; k < replace; k++)
            {
                if (frames[k] is { } frame) layer.Cels[aIndex + 1 + k].Frame = frame;
            }

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
                RippleReferences(doc.Scene, at, +1);
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
    /// Re-time a range so every drawing in it is held for <paramref name="step"/>
    /// frames. The range gets longer and no drawing is lost — this is what an
    /// animator means by "animating on 2s".
    /// </summary>
    /// <returns>Frames the range grew by.</returns>
    public int StretchExposure(string layerId, int from, int to, int step)
    {
        if (step < 1 || FindLayer(layerId) is null) return 0;
        var grew = 0;
        Perform(doc =>
        {
            var layer = doc.Scene.Layers.First(l => l.Id == layerId);
            PadCels(layer, doc.Scene.FrameCount);
            var lo = Math.Clamp(Math.Min(from, to), 0, layer.Cels.Count - 1);
            var hi = Math.Clamp(Math.Max(from, to), 0, layer.Cels.Count - 1);

            // Rebuild the span: each drawing, then step-1 holds behind it.
            // Holds already in the range are absorbed rather than multiplied,
            // so stretching to 2s twice does not land on 4s.
            var rebuilt = new List<Cel>();
            for (var i = lo; i <= hi; i++)
            {
                if (layer.Cels[i].Frame is null) continue; // an existing hold
                rebuilt.Add(layer.Cels[i]);
                for (var h = 1; h < step; h++) rebuilt.Add(new Cel());
            }
            if (rebuilt.Count == 0) return;

            var original = hi - lo + 1;
            layer.Cels.RemoveRange(lo, original);
            layer.Cels.InsertRange(lo, rebuilt);
            grew = rebuilt.Count - original;
            if (layer.Cels.Count > doc.Scene.FrameCount) doc.Scene.FrameCount = layer.Cels.Count;
        });
        return grew;
    }

    /// <summary>
    /// Re-time a range to a saved pattern, as one undoable step.
    /// </summary>
    /// <remarks>
    /// The general case of <see cref="StretchExposure"/>: that one holds every
    /// drawing for the same number of frames, this one follows a pattern, so a
    /// slow-in of 1-1-2-3-4 is expressible where a single step is not. Same
    /// guarantee — no drawing is created or destroyed, only re-spaced — and the
    /// row grows or shrinks to fit the pattern rather than the selection.
    /// </remarks>
    public ExposureSheet.TimingChange ApplyTiming(string layerId, int from, int to, TimingPreset preset)
    {
        if (FindLayer(layerId) is null) return default;
        var change = default(ExposureSheet.TimingChange);
        Perform(doc =>
        {
            var layer = doc.Scene.Layers.First(l => l.Id == layerId);
            PadCels(layer, doc.Scene.FrameCount);
            var lo = Math.Clamp(Math.Min(from, to), 0, layer.Cels.Count - 1);
            var hi = Math.Clamp(Math.Max(from, to), 0, layer.Cels.Count - 1);
            change = ExposureSheet.ApplyTiming(layer, lo, hi - lo + 1, preset);
            // The row may now be longer than the scene. Growing the scene is
            // what StretchExposure already does; never shrinking it is
            // deliberate, because other layers still occupy those frames.
            if (layer.Cels.Count > doc.Scene.FrameCount) doc.Scene.FrameCount = layer.Cels.Count;
        });
        return change;
    }

    /// <summary>
    /// Thin a range to every <paramref name="step"/>-th drawing, keeping the
    /// range the same length by holding what survives. Destructive: the
    /// drawings between are discarded.
    /// </summary>
    /// <returns>Drawings removed.</returns>
    public int ReduceToStep(string layerId, int from, int to, int step)
    {
        if (step < 2 || FindLayer(layerId) is null) return 0;
        var dropped = 0;
        Perform(doc =>
        {
            var layer = doc.Scene.Layers.First(l => l.Id == layerId);
            PadCels(layer, doc.Scene.FrameCount);
            var lo = Math.Clamp(Math.Min(from, to), 0, layer.Cels.Count - 1);
            var hi = Math.Clamp(Math.Max(from, to), 0, layer.Cels.Count - 1);

            var kept = 0;
            for (var i = lo; i <= hi; i++)
            {
                if (layer.Cels[i].Frame is null) continue;
                // Keep every step-th drawing; the rest become holds, so the
                // range keeps its length and its timing.
                if (kept % step != 0)
                {
                    layer.Cels[i] = new Cel();
                    dropped++;
                }
                kept++;
            }
        });
        return dropped;
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
    /// Remove cels from one layer and pull the rest back — the exposure
    /// sheet's ripple delete. One undo step.
    ///
    /// Distinct from both of its neighbours, and the distinction is the whole
    /// feature: <see cref="ClearCels"/> blanks a cel and keeps the timing;
    /// <see cref="DeleteFrame"/> removes a frame from <em>every</em> layer and
    /// shortens the scene. This shortens one layer's row and pads the tail with
    /// holds, so the timeline keeps its length while everything after the hole
    /// moves up — which is what "delete this drawing" means to an animator.
    /// </summary>
    public void DeleteCels(string layerId, int from, int to)
    {
        var layer = FindLayer(layerId);
        if (layer is null) return;
        (from, to) = (Math.Max(0, Math.Min(from, to)), Math.Max(from, to));
        if (from >= layer.Cels.Count) return;

        Perform(doc =>
        {
            var target = doc.Scene.Layers.First(l => l.Id == layerId);
            var last = Math.Min(to, target.Cels.Count - 1);
            var count = last - from + 1;
            if (count <= 0) return;
            target.Cels.RemoveRange(from, count);
            // Pad back to the scene's length: the other layers did not change,
            // and a short row would desynchronise every cel after it.
            while (target.Cels.Count < doc.Scene.FrameCount) target.Cels.Add(new Cel());
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

    /// <summary>
    /// Add one frame at the end, without the ripple.
    /// </summary>
    /// <remarks>
    /// For growing a document to fit something that already has a length — a
    /// reference sheet's worth of frames. <see cref="AddFrameAfter"/> is the
    /// wrong shape for that: it is an insertion, so it ripples every reference
    /// slot after it, and doing that once per frame while appending would
    /// shuffle the very strip being made room for.
    /// </remarks>
    public static void AppendFrame(Scene scene)
    {
        foreach (var layer in scene.Layers)
        {
            PadCels(layer, scene.FrameCount);
            layer.Cels.Add(new Cel { Frame = layer.IsBackground ? null : NewEmptyFrame(layer) });
        }
        scene.FrameCount++;
    }

    private static void PadCels(Layer layer, int frameCount)
    {
        while (layer.Cels.Count < frameCount) layer.Cels.Add(new Cel());
    }

    /// <summary>
    /// An empty drawing. Takes the layer for symmetry with its callers rather than
    /// because it reads anything off it — one frame class means the layer no longer
    /// decides what kind of frame it gets.
    /// </summary>
    private static Frame NewEmptyFrame(Layer layer) => new();

    /// <summary>Deep-clone a frame with a fresh id (ids key the render cache).</summary>
    /// <remarks>
    /// <b>Placements are deliberately not cloned</b>, and that predates the frame
    /// merge: this used to have two arms and only the raster one carried
    /// placements, which it also did not copy. Duplicating a cel therefore
    /// duplicates the drawing and not the symbols placed over it. Recorded rather
    /// than changed, because changing it is a behaviour decision and this is a
    /// refactor.
    /// </remarks>
    public static Frame? CloneFrame(Frame? src) => src is null ? null : new Frame
    {
        Role = src.Role,
        PngBase64 = src.PngBase64,
        Strokes = src.Strokes.Select(s => s.Clone()).ToList(),
    };
}

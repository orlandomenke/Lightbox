using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.App.Rendering;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The rig overlay: placing anchors and collision shapes on the canvas.
/// </summary>
/// <remarks>
/// <para>
/// P5b and P5c had their records, their multi-frame operations and their whole export
/// path before they had any way to place one by dragging — so the roadmap carried
/// "no canvas overlay yet" three times over. This is that overlay, and it is
/// deliberately <b>one</b> of them: an anchor is a point and a shape is a rectangle,
/// and treating a point as a zero-sized rectangle means one hit-test, one drag and
/// one set of handles rather than two of each.
/// </para>
/// <para>
/// <b>Edits land on the drawing, not on the frame index.</b> Every write goes through
/// <see cref="Anchors.SetAcross"/> or <see cref="CollisionShapes.SetAcross"/> with a
/// range of one at the current frame, which resolves through the exposure — so
/// dragging a socket while parked on a hold moves the drawing being held, which is the
/// only reading that survives a re-time.
/// </para>
/// <para>
/// The overlay is chrome: it never reaches a pixel, and a document with no anchors and
/// no shapes shows nothing at all. The mode is off by default and absent from the
/// canvas until switched on — the camera's rule again.
/// </para>
/// </remarks>
public sealed partial class MainViewModel
{
    /// <summary>
    /// Dragging on the canvas edits the rig instead of drawing.
    /// </summary>
    /// <remarks>
    /// A mode rather than a modifier, for the reason the reference-align mode gives:
    /// Shift, Ctrl and Alt are already spoken for on the canvas, and a fourth meaning
    /// for one of them is a chord nobody finds and everybody triggers by accident.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RigMarks))]
    private bool _rigEditMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RigMarks))]
    private string? _selectedRigMarkId;

    /// <summary>
    /// What the overlay draws: every anchor and shape that applies at this frame.
    /// </summary>
    /// <remarks>
    /// Resolved through holds, so a socket placed on a drawing shows on every frame
    /// that drawing is exposed. Empty when the mode is off, which is what keeps the
    /// overlay absent rather than merely invisible.
    /// </remarks>
    public IReadOnlyList<RigMark> RigMarks
    {
        get
        {
            if (!RigEditMode) return [];

            var marks = new List<RigMark>();
            var anchors = Anchors.ResolvedAt(Doc.Scene, CurrentFrameIndex);
            foreach (var declared in Doc.Scene.Anchors ?? [])
            {
                if (!anchors.TryGetValue(declared.Id, out var point)) continue;
                marks.Add(new RigMark(
                    declared.Id, RigMarkKind.Anchor, declared.Name,
                    point.X, point.Y, 0, 0, declared.Id == SelectedRigMarkId));
            }

            var boxes = CollisionShapes.ResolvedAt(Doc.Scene, CurrentFrameIndex);
            foreach (var declared in Doc.Scene.Shapes ?? [])
            {
                if (!boxes.TryGetValue(declared.Id, out var box)) continue;
                marks.Add(new RigMark(
                    declared.Id, RigMarkKind.Shape, declared.Name,
                    box.X, box.Y, box.W, box.H, declared.Id == SelectedRigMarkId));
            }
            return marks;
        }
    }

    /// <summary>Whether anything is declared for the overlay to show.</summary>
    public bool HasRig =>
        Doc.Scene.Anchors is { Count: > 0 } || Doc.Scene.Shapes is { Count: > 0 };

    /// <summary>The mark with this id, or null.</summary>
    private RigMark? MarkOf(string? id) =>
        id is null ? null : RigMarks.FirstOrDefault(m => m.Id == id) is { Id: not null } m ? m : null;

    /// <summary>Select what a press hit, or clear the selection on empty canvas.</summary>
    public RigHit PressRig(double x, double y, double scale)
    {
        var hit = RigOverlay.Hit(RigMarks, x, y, scale);
        SelectedRigMarkId = hit.Id;
        return hit;
    }

    /// <summary>
    /// Apply a drag to a mark and write the result to the drawing this frame shows.
    /// </summary>
    /// <remarks>
    /// One editor step per call rather than one per gesture, which is the simple
    /// reading and the wrong one for a long drag — so the canvas calls this on release
    /// with the total delta, not on every pointer move. Live feedback during the drag
    /// is the overlay's own business and touches nothing.
    /// </remarks>
    public void DragRig(string id, RigCorner corner, double dx, double dy)
    {
        if (MarkOf(id) is not { } mark) return;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return;

        var moved = RigOverlay.Drag(mark, corner, dx, dy);
        WriteRig(moved);
    }

    /// <summary>Write a mark's geometry to the drawing the current frame exposes.</summary>
    private void WriteRig(RigMark mark)
    {
        // Q90, and the reason the rig writers now refuse an index past the sheet
        // rather than clamping to it: posing past the end would otherwise have
        // written the pose onto the LAST drawing, silently, on a frame the
        // artist is not looking at. Growing first is what makes posing out
        // there mean what it looks like it means.
        EnsureSceneReachesPlayhead();
        var layerId = ActiveLayer.Id;
        var frame = CurrentFrameIndex;

        _editor.Perform(doc =>
        {
            if (doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId) is not { } layer) return;

            if (mark.Kind == RigMarkKind.Anchor)
            {
                Anchors.SetAcross(layer, frame, 1, mark.Id, new AnchorPoint(mark.X, mark.Y));
            }
            else
            {
                CollisionShapes.SetAcross(
                    layer, frame, 1, mark.Id, new ShapeBox(mark.X, mark.Y, mark.W, mark.H));
            }
        });
        OnPropertyChanged(nameof(RigMarks));
    }

    /// <summary>
    /// Declare an anchor and place it here.
    /// </summary>
    /// <remarks>
    /// Declaring and placing in one step, because they are one gesture: an artist who
    /// clicks to add a socket has already chosen where it goes, and a two-step
    /// "declare then place" would leave a named anchor that exists nowhere.
    /// </remarks>
    public string AddAnchorAt(string name, double x, double y, AnchorKind kind = AnchorKind.Socket)
    {
        var id = "";
        var layerId = ActiveLayer.Id;
        var frame = CurrentFrameIndex;

        _editor.Perform(doc =>
        {
            var anchor = Anchors.Declare(doc.Scene, name, kind);
            id = anchor.Id;
            if (doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId) is { } layer)
                Anchors.SetAcross(layer, frame, 1, anchor.Id, new AnchorPoint(x, y));
        });

        SelectedRigMarkId = id;
        OnPropertyChanged(nameof(RigMarks));
        OnPropertyChanged(nameof(HasRig));
        return id;
    }

    /// <summary>Declare a collision shape and place it here.</summary>
    public string AddShapeAt(
        string name, double x, double y, double w, double h,
        ShapeRole role = ShapeRole.Hurtbox)
    {
        var id = "";
        var layerId = ActiveLayer.Id;
        var frame = CurrentFrameIndex;
        // Through the same normaliser the drag uses, so a rectangle dragged out
        // upwards or leftwards arrives the right way round and never degenerate.
        var box = RigOverlay.Normalised(
            new RigMark("", RigMarkKind.Shape, name, 0, 0, 0, 0, false), x, y, x + w, y + h);

        _editor.Perform(doc =>
        {
            var shape = CollisionShapes.Declare(doc.Scene, name, role);
            id = shape.Id;
            if (doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId) is { } layer)
            {
                CollisionShapes.SetAcross(
                    layer, frame, 1, shape.Id, new ShapeBox(box.X, box.Y, box.W, box.H));
            }
        });

        SelectedRigMarkId = id;
        OnPropertyChanged(nameof(RigMarks));
        OnPropertyChanged(nameof(HasRig));
        return id;
    }

    /// <summary>
    /// Remove the selected mark's declaration and every position of it.
    /// </summary>
    /// <remarks>
    /// The declaration, not just this frame's placement — that is what the Delete key
    /// means on a selected socket. Clearing it from one frame is
    /// <see cref="ClearRigHere"/>, which is a different intention and a different key.
    /// </remarks>
    public void DeleteSelectedRigMark()
    {
        if (SelectedRigMarkId is not { } id) return;
        if (MarkOf(id) is not { } mark) return;

        _editor.Perform(doc =>
        {
            if (mark.Kind == RigMarkKind.Anchor) Anchors.Remove(doc.Scene, id);
            else CollisionShapes.Remove(doc.Scene, id);
        });

        SelectedRigMarkId = null;
        OnPropertyChanged(nameof(RigMarks));
        OnPropertyChanged(nameof(HasRig));
    }

    /// <summary>
    /// Take the selected mark off this drawing, leaving it declared.
    /// </summary>
    /// <remarks>
    /// How a hitbox stops being active partway through a swing: absence is the off
    /// state, so removing the rectangle from the frames that do not connect is the
    /// whole mechanism.
    /// </remarks>
    public void ClearRigHere()
    {
        if (SelectedRigMarkId is not { } id) return;
        if (MarkOf(id) is not { } mark) return;

        var layerId = ActiveLayer.Id;
        var frame = CurrentFrameIndex;

        _editor.Perform(doc =>
        {
            if (doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId) is not { } layer) return;
            if (mark.Kind == RigMarkKind.Anchor) Anchors.ClearAcross(layer, frame, 1, id);
            else CollisionShapes.ClearAcross(layer, frame, 1, id);
        });
        OnPropertyChanged(nameof(RigMarks));
    }

    /// <summary>
    /// Copy the selected mark's geometry from this drawing across a range.
    /// </summary>
    /// <remarks>
    /// The bridge to the multi-frame operations that already existed: an artist places
    /// a physics body once and pushes it across the whole cycle, rather than dragging
    /// it twenty-four times. Works on drawings, so a hold is visited once.
    /// </remarks>
    /// <returns>Drawings changed.</returns>
    public int PushRigAcross(int from, int count)
    {
        if (SelectedRigMarkId is not { } id) return 0;
        if (MarkOf(id) is not { } mark) return 0;

        var layerId = ActiveLayer.Id;
        var changed = 0;

        _editor.Perform(doc =>
        {
            if (doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId) is not { } layer) return;
            changed = mark.Kind == RigMarkKind.Anchor
                ? Anchors.SetAcross(layer, from, count, id, new AnchorPoint(mark.X, mark.Y))
                : CollisionShapes.SetAcross(
                    layer, from, count, id, new ShapeBox(mark.X, mark.Y, mark.W, mark.H));
        });

        OnPropertyChanged(nameof(RigMarks));
        return changed;
    }

    partial void OnRigEditModeChanged(bool value)
    {
        // Leaving the mode drops the selection, so re-entering it does not resume a
        // gesture from a session the artist has forgotten about.
        if (!value) SelectedRigMarkId = null;
        OnPropertyChanged(nameof(HasRig));
    }
}

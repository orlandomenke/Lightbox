using Lightbox.App.Rendering;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The bone gesture, both halves: the live preview per pointer move and the
/// one editor step on release. One dispatch shape, twice, side by side —
/// <see cref="PreviewBoneGesture"/> applies <see cref="ArmatureGesture"/>'s
/// edits to a scratch clone, <see cref="EndBoneGesture"/> applies the same
/// edits to the document. Keeping the two adjacent is the guard against the
/// drift that would make the drag show one thing and the release land
/// another.
/// </summary>
/// <remarks>
/// The un-fixed sibling of B112, fixed the same way rig marks were: the
/// gesture used to consume no pointer move at all — chrome and pixels both
/// jumped on release — because "one editor step per gesture" was met by
/// suppressing the preview instead of separating it from the commit. The
/// preview never touches <c>_editor</c>, so the undo story is unchanged.
/// </remarks>
public sealed partial class MainViewModel
{
    /// <summary>A scratch clone carrying the provisional bind-mode edit, or null.</summary>
    private Armature? _bonePreviewArmature;

    /// <summary>The playhead pose with the provisional key merged in, or null.</summary>
    private Dictionary<string, BonePose>? _bonePreviewPose;

    /// <summary>
    /// Show what the release would do, per pointer move. Chrome only — the
    /// record is untouched, which is what keeps one undo step per gesture.
    /// </summary>
    public void PreviewBoneGesture(
        string? id, BoneGrab grab, double x0, double y0, double x, double y, bool extruding)
    {
        if (PosingMode)
        {
            // Posing an empty spot means nothing and writes nothing — the
            // same refusal the release makes.
            if (id is not null) PreviewPoseDrag(id, x, y);
            return;
        }

        Armature scratch;
        if (Doc.Armature is { } armature) scratch = armature.Clone();
        else if (id is null) scratch = new Armature(); // first drag on an unrigged document
        else return;

        if (id is null)
        {
            ArmatureGesture.ApplyCreate(scratch, SelectedBoneId, NextBoneName(), x0, y0, x, y);
        }
        else if (extruding && grab is BoneGrab.Tip)
        {
            ArmatureGesture.ApplyExtrude(scratch, id, NextBoneName(), x, y);
        }
        else if (grab is BoneGrab.Origin or BoneGrab.Tip)
        {
            ArmatureGesture.ApplyDragBind(scratch, id, grab, x, y);
        }
        else
        {
            ArmatureGesture.ApplyMoveBy(scratch, id, x - x0, y - y0);
        }

        _bonePreviewArmature = scratch;
        _bonePreviewPose = null;
        OnPropertyChanged(nameof(BoneChromes));
    }

    /// <summary>
    /// The release: the same dispatch as the preview, landing one editor
    /// step through the commit methods.
    /// </summary>
    public void EndBoneGesture(
        string? id, BoneGrab grab, double x0, double y0, double x1, double y1, bool extruding)
    {
        ClearBoneGesturePreview();
        if (id is null)
        {
            // An empty-canvas drag creates a bone — in bind mode only.
            if (!PosingMode) CreateBoneFromDrag(x0, y0, x1, y1);
        }
        else if (PosingMode)
        {
            PoseBoneTo(id, x1, y1);
        }
        else if (extruding && grab is BoneGrab.Tip)
        {
            // Blender's idiom: take hold of the tip and pull a child out of
            // it, already joined to the parent.
            ExtrudeChildFrom(id, x1, y1);
        }
        else if (grab is BoneGrab.Origin or BoneGrab.Tip)
        {
            DragBoneBind(id, grab, x1, y1);
        }
        else
        {
            // The shaft moves the whole bone, children and all.
            MoveBoneBy(id, x1 - x0, y1 - y0);
        }
    }

    /// <summary>Drop the provisional chrome; the record never knew about it.</summary>
    public void ClearBoneGesturePreview()
    {
        if (_bonePreviewArmature is null && _bonePreviewPose is null) return;
        _bonePreviewArmature = null;
        _bonePreviewPose = null;
        OnPropertyChanged(nameof(BoneChromes));
    }

    /// <summary>
    /// The pose-mode preview: the playhead pose with the provisional key
    /// merged, solved by the same chrome path the committed pose uses. The
    /// dispatch mirrors <c>PoseBoneTo</c> — chain and spline handles
    /// translate, everything else aims.
    /// </summary>
    private void PreviewPoseDrag(string id, double x, double y)
    {
        if (Doc.Armature is not { } armature || armature.BoneById(id) is null) return;

        var pose = ArmatureOps.PoseAt(Doc.Scene.PoseTrack, CurrentFrameIndex);
        var placements = ArmatureOps.Solve(armature, pose);

        string targetId;
        var translate = true;
        if (ChainTouching(id) is { } chain)
        {
            targetId = chain.PoleBoneId == id || chain.TargetBoneId == id ? id : chain.TargetBoneId;
        }
        else if (SplineTouching(id) is { } spline)
        {
            targetId = spline.HandleBoneIds.Contains(id) ? id : NearestHandle(spline, x, y);
        }
        else
        {
            targetId = id;
            translate = false;
        }

        // Clone before editing: PoseAt hands back copies today, but a pose
        // object shared with the track being mutated by a preview is the
        // kind of corruption nothing would catch until a re-render.
        var edited = pose.TryGetValue(targetId, out var b) ? b.Clone() : new BonePose();
        if (translate)
        {
            if (armature.BoneById(targetId) is not { } target) return;
            (edited.X, edited.Y) = ArmatureGesture.PoseTranslationDelta(placements, target, x, y);
        }
        else
        {
            edited.RotationDeg = ArmatureGesture.PoseAimDelta(armature, placements, targetId, x, y);
        }
        pose[targetId] = edited;

        _bonePreviewPose = pose;
        _bonePreviewArmature = null;
        OnPropertyChanged(nameof(BoneChromes));
    }
}

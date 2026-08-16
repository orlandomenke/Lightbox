using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

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
    /// Where the last pose-preview publish painted, so the next one can put
    /// those pixels back as well as paint the new ones — old ∪ new, the same
    /// shape <c>PreviewDirtyRegion</c> gives the transform preview.
    /// </summary>
    private SKRectI? _bonePreviewDirty;

    /// <summary>
    /// The mode the gesture is being previewed in, latched at its first
    /// preview. The posing toggle is a hotkey that can fire while the button
    /// is still down, and a release that read the <em>live</em> mode landed a
    /// pose key after a drag whose preview showed a bind edit the whole way —
    /// the adversarial pass's counterexample to "one construction, twice".
    /// What was shown is what lands; a click that never previewed anything
    /// still reads the live mode, because it showed nothing to contradict.
    /// </summary>
    private bool? _boneGesturePosing;

    /// <summary>
    /// Show what the release would do, per pointer move. Chrome only — the
    /// record is untouched, which is what keeps one undo step per gesture.
    /// </summary>
    public void PreviewBoneGesture(
        string? id, BoneGrab grab, double x0, double y0, double x, double y, bool extruding)
    {
        _boneGesturePosing ??= PosingMode;
        if (_boneGesturePosing.Value)
        {
            // Posing an empty spot means nothing and writes nothing — the
            // same refusal the release makes.
            if (id is not null) PreviewPoseDrag(id, x, y);
            return;
        }

        // A fresh clone per move is deliberate, and leak-hunter has priced
        // it: a rig is tens of small objects, so this is the same order of
        // transient gen0 garbage the paint path makes per event — retained
        // by nothing. The alternative (an idempotent re-apply onto one
        // persistent scratch) would give the preview its own edit path,
        // which is exactly the drift the shared construction exists to
        // prevent.
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
        // The mode the drag previewed in, not the mode of this instant —
        // see _boneGesturePosing. Read before the clear resets the latch.
        var posing = _boneGesturePosing ?? PosingMode;
        ClearBoneGesturePreview();
        if (id is null)
        {
            // An empty-canvas drag creates a bone — in bind mode only.
            if (!posing) CreateBoneFromDrag(x0, y0, x1, y1);
        }
        else if (posing)
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
        _boneGesturePosing = null;
        if (_bonePreviewArmature is null && _bonePreviewPose is null) return;
        _bonePreviewArmature = null;
        _bonePreviewPose = null;
        // Pixels previewed the provisional pose; put the committed one back.
        // On a release that commits, InvalidateRiggedFrames repeats this a
        // moment later — the publishes coalesce, and a cancelled drag has no
        // other path back to the record's pixels.
        if (_bonePreviewDirty is { } dirty)
        {
            var affected = PosedFramesAtPlayhead();
            foreach (var frame in affected)
            {
                _cache.Invalidate(frame.Id);
                _stackBake.NoteFrameChanged(frame.Id);
            }
            // Two regions to repaint: where the preview drew (erase it) and
            // where the committed pose puts the strokes back. The stored rect
            // only remembers the first — the last event's preview bounds.
            var committed = PosedReach(
                affected, ArmatureOps.PoseAt(Doc.Scene.PoseTrack, CurrentFrameIndex));
            _publish.MarkDirty(committed is { } home ? SKRectI.Union(dirty, home) : dirty);
            _bonePreviewDirty = null;
            RequestSnapshot();
        }
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
        PublishPosePreview();
    }

    /// <summary>
    /// Q81 decision 5's pixel half: the bound strokes at the playhead
    /// re-render through the provisional pose, per pointer event, and only
    /// the region they can reach is repainted. The render itself goes
    /// through the cache's normal miss path — the resolver sees
    /// <see cref="_bonePreviewPose"/> — so the preview pixels are the commit
    /// pixels by construction, expensive brushes excepted (they ghost, and
    /// land exactly on release).
    /// </summary>
    private void PublishPosePreview()
    {
        if (_bonePreviewPose is null) return;
        var affected = PosedFramesAtPlayhead();
        if (affected.Count == 0) return; // chrome-only rig: nothing bound to repaint

        // First event of the drag: where the committed pose put the pixels
        // is where they must be repainted FROM, so measure it once.
        _bonePreviewDirty ??= PosedReach(affected, ArmatureOps.PoseAt(Doc.Scene.PoseTrack, CurrentFrameIndex));

        foreach (var frame in affected)
        {
            _cache.Invalidate(frame.Id);
            _stackBake.NoteFrameChanged(frame.Id);
        }

        var now = PosedReach(affected, _bonePreviewPose);
        var patch = (_bonePreviewDirty, now) switch
        {
            ({ } a, { } b) => SKRectI.Union(a, b),
            ({ } a, null) => a,
            (null, { } b) => b,
            _ => (SKRectI?)null,
        };
        if (patch is { } rect) _publish.MarkDirty(rect);
        else _publish.InvalidateWholeCanvas();
        if (now is { } moved) _bonePreviewDirty = moved;
        RequestSnapshot(); // paced by the publish dam, like every live preview
    }

    /// <summary>The drawings the pose drag can move: visible, exposed at the playhead, rigged.</summary>
    private List<Frame> PosedFramesAtPlayhead()
    {
        var frames = new List<Frame>();
        foreach (var layer in Scene.Layers)
        {
            if (!Scene.IsLayerVisible(layer)) continue;
            if (ExposureSheet.ExposedFrame(layer, CurrentFrameIndex) is not { } frame) continue;
            if (!_cache.Rig.IsPosed(frame)) continue;
            frames.Add(frame);
        }
        return frames;
    }

    /// <summary>
    /// The doc-space region these frames' bound strokes cover under a pose,
    /// render reach included — <c>PreviewMovingBounds</c>' arithmetic pointed
    /// at posed geometry. Unbound strokes are skipped by identity: the pose
    /// funnel only replaces the strokes it moves.
    /// </summary>
    private SKRectI? PosedReach(List<Frame> frames, IReadOnlyDictionary<string, BonePose> pose)
    {
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        var any = false;
        foreach (var frame in frames)
        {
            var posed = Skinning.PoseFrameForRender(Doc, frame, CurrentFrameIndex, _cache.Rig, pose);
            if (ReferenceEquals(posed, frame)) continue;
            for (var i = 0; i < frame.Strokes.Count; i++)
            {
                if (ReferenceEquals(posed.Strokes[i], frame.Strokes[i])) continue;
                var stroke = posed.Strokes[i];
                var reach = (float)BrushEngine.ReachOf(stroke.Brush);
                foreach (var p in stroke.Points)
                {
                    any = true;
                    if ((float)p.X - reach < minX) minX = (float)p.X - reach;
                    if ((float)p.Y - reach < minY) minY = (float)p.Y - reach;
                    if ((float)p.X + reach > maxX) maxX = (float)p.X + reach;
                    if ((float)p.Y + reach > maxY) maxY = (float)p.Y + reach;
                }
            }
        }
        if (!any) return null;
        var rect = new SKRect(minX, minY, maxX, maxY);
        rect.Inflate(2, 2); // the antialias seam, SnapshotGeometry's allowance
        return SKRectI.Ceiling(rect);
    }
}

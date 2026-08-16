using Lightbox.App.Rendering;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>
/// What a bone gesture does to the record, as pure edits of whichever
/// armature they are handed — the document's inside one editor step on
/// release, a <see cref="Armature.Clone"/> per pointer event for the live
/// preview. One construction for both is what makes the preview honest:
/// the chrome an artist watches mid-drag is computed by the same code that
/// lands the edit, so the release cannot land somewhere the drag never
/// showed. The same argument that made the live rigged render share
/// <c>PoseStroke</c> with bake.
/// </summary>
public static class ArmatureGesture
{
    /// <summary>
    /// A drag from empty canvas: a new bone from press to pointer, parented
    /// to the selection when there is one. Returns the bone it added.
    /// </summary>
    public static Bone ApplyCreate(
        Armature armature, string? parentId, string name,
        double x0, double y0, double x1, double y1)
    {
        var (length, worldAngle) = ArmatureOverlay.CreateFrom(x0, y0, x1, y1);
        var bone = new Bone { Name = name, Length = length };
        var parent = parentId is null ? null : armature.BoneById(parentId);
        if (parent is null)
        {
            bone.X = x0;
            bone.Y = y0;
            bone.RotationDeg = worldAngle;
        }
        else
        {
            // Into the parent's frame, so the record stays hierarchical:
            // the press point relative to the parent's origin, unrotated.
            var placements = ArmatureOps.Solve(armature);
            var p = placements[parent.Id];
            var rad = -p.RotationDeg * Math.PI / 180.0;
            var (dx, dy) = (x0 - p.X, y0 - p.Y);
            bone.ParentId = parent.Id;
            bone.X = Math.Cos(rad) * dx - Math.Sin(rad) * dy;
            bone.Y = Math.Sin(rad) * dx + Math.Cos(rad) * dy;
            bone.RotationDeg = worldAngle - p.RotationDeg;
        }
        armature.Bones.Add(bone);
        return bone;
    }

    /// <summary>
    /// A bind-mode drag: the origin grab moves the joint, the tip grab
    /// re-aims and re-lengths.
    /// </summary>
    public static void ApplyDragBind(Armature armature, string id, BoneGrab grab, double x, double y)
    {
        if (armature.BoneById(id) is not { } bone) return;

        var placements = ArmatureOps.Solve(armature);
        var parentRot = bone.ParentId is not null && placements.TryGetValue(bone.ParentId, out var pp)
            ? pp.RotationDeg
            : 0.0;
        var parentPos = bone.ParentId is not null && placements.TryGetValue(bone.ParentId, out var pq)
            ? (pq.X, pq.Y)
            : (0.0, 0.0);
        var own = placements[bone.Id];

        if (grab == BoneGrab.Tip)
        {
            var world = ArmatureOverlay.AngleFrom(own.X, own.Y, x, y);
            bone.RotationDeg = world - parentRot;
            bone.Length = Math.Max(
                ArmatureOverlay.MinimumLength, Dist(own.X, own.Y, x, y));
        }
        else
        {
            // The origin lands where the pointer is, expressed in the
            // parent's frame — a root's frame is the document's. Dragging
            // it off a glued joint unglues it: a silent no-op is worse than
            // an unglue the artist can see and undo.
            if (bone.IsConnected) Unglue(armature, bone);
            var rad = -parentRot * Math.PI / 180.0;
            var (dx, dy) = (x - parentPos.Item1, y - parentPos.Item2);
            bone.X = Math.Cos(rad) * dx - Math.Sin(rad) * dy;
            bone.Y = Math.Sin(rad) * dx + Math.Cos(rad) * dy;
        }
    }

    /// <summary>A shaft drag: the whole bone, children and all, by a world-space delta.</summary>
    public static void ApplyMoveBy(Armature armature, string id, double dx, double dy)
    {
        if (armature.BoneById(id) is not { } bone) return;

        var placements = ArmatureOps.Solve(armature);
        var parentRot = bone.ParentId is not null && placements.TryGetValue(bone.ParentId, out var p)
            ? p.RotationDeg
            : 0.0;
        var rad = -parentRot * Math.PI / 180.0;
        // Moving a glued bone means unglueing it. The alternative is a
        // gesture that writes an offset the solve then ignores — a drag
        // that visibly does nothing, which reads as the tool being broken.
        if (bone.IsConnected) Unglue(armature, bone);
        bone.X += Math.Cos(rad) * dx - Math.Sin(rad) * dy;
        bone.Y += Math.Sin(rad) * dx + Math.Cos(rad) * dy;
    }

    /// <summary>
    /// Blender's extrude: a connected child grown from a bone's tip, reaching
    /// to the pointer. Returns the child it added.
    /// </summary>
    public static Bone? ApplyExtrude(Armature armature, string parentId, string name, double x, double y)
    {
        if (armature.BoneById(parentId) is not { } parent) return null;

        var placements = ArmatureOps.Solve(armature);
        var at = placements[parentId];
        var (tipX, tipY) = at.Tip(parent.Length);
        var (length, worldAngle) = ArmatureOverlay.CreateFrom(tipX, tipY, x, y);

        var child = new Bone
        {
            Name = name,
            ParentId = parentId,
            // The parent's own frame: along its length, on its axis. Written
            // as well as glued, so an unglue later has somewhere to land and
            // a reader of the JSON sees where the joint sits.
            X = parent.Length,
            Y = 0,
            Connected = true,
            RotationDeg = worldAngle - at.RotationDeg,
            Length = length,
        };
        armature.Bones.Add(child);
        return child;
    }

    /// <summary>
    /// A pose-mode aim: the rotation delta the key would hold so the bone
    /// points at the pointer. The pivot is where the bone is drawn; the angle
    /// is measured against forward kinematics — see <c>PoseBoneTo</c> for why
    /// a constraint must not bend the meaning of the key.
    /// </summary>
    public static double PoseAimDelta(
        Armature armature, IReadOnlyDictionary<string, BonePlacement> placements,
        string id, double x, double y)
    {
        var own = placements[id];
        var bone = armature.BoneById(id)!;
        var parentWorld = bone.ParentId is { } pid && placements.TryGetValue(pid, out var pp)
            ? pp.RotationDeg
            : 0.0;
        return ArmatureOverlay.AngleFrom(own.X, own.Y, x, y) - parentWorld - bone.RotationDeg;
    }

    /// <summary>
    /// A pose-mode handle drag: the translation the key would hold so the
    /// handle lands under the pointer, in the parent's frame.
    /// </summary>
    public static (double X, double Y) PoseTranslationDelta(
        IReadOnlyDictionary<string, BonePlacement> placements, Bone target, double x, double y)
    {
        var (px, py, prot) = target.ParentId is { } pid && placements.TryGetValue(pid, out var pp)
            ? (pp.X, pp.Y, pp.RotationDeg)
            : (0.0, 0.0, 0.0);
        var rad = -prot * Math.PI / 180.0;
        var (dx, dy) = (x - px, y - py);
        var localX = Math.Cos(rad) * dx - Math.Sin(rad) * dy;
        var localY = Math.Sin(rad) * dx + Math.Cos(rad) * dy;
        return (localX - target.X, localY - target.Y);
    }

    /// <summary>
    /// Break a bone's glue to its parent's tip without moving it: the offset
    /// the solve was ignoring becomes the offset it reads.
    /// </summary>
    /// <remarks>
    /// Called before any bind-mode edit that writes <see cref="Bone.X"/> or
    /// <see cref="Bone.Y"/> on a connected bone, so the edit lands on values
    /// that are actually in effect. Writing the parent's length in first is
    /// what keeps the unglue itself invisible — the bone is where it was, and
    /// only the drag moves it.
    /// </remarks>
    internal static void Unglue(Armature armature, Bone bone)
    {
        if (bone.ParentId is { } pid && armature.BoneById(pid) is { } parent)
        {
            bone.X = parent.Length;
            bone.Y = 0;
        }
        bone.Connected = null;
    }

    private static double Dist(double x0, double y0, double x1, double y1) =>
        Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
}

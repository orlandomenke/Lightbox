namespace Lightbox.App.Rendering;

/// <summary>
/// One bone, flattened for the overlay: its placement in document
/// coordinates, solved for whatever pose the playhead shows.
/// </summary>
/// <remarks>
/// A snapshot rather than the document object, for <c>RigMark</c>'s reason:
/// the renderer runs off the UI thread and must never read a record the UI
/// thread may be halfway through editing. The view model solves FK once and
/// flattens; everything here is doubles.
/// </remarks>
/// <summary>
/// Whether a chrome entry is the live skeleton or an onion ghost of a
/// neighbouring pose key.
/// </summary>
/// <remarks>
/// A field on <see cref="BoneChrome"/> rather than a second list through the
/// draw op, and the ratchet is why: ghosts ride the plumbing the bones
/// already have — one property, one push, one paint call — and only the
/// painter and the hit-test, which live outside the ratcheted file, know the
/// difference. A ghost is never hit: it shows where a pose came from or
/// goes to, it is not a thing to grab.
/// </remarks>
public enum BoneGhost
{
    None,

    /// <summary>The nearest pose keys behind the playhead.</summary>
    Before,

    /// <summary>The nearest pose keys ahead of it.</summary>
    After,
}

public readonly record struct BoneChrome(
    string Id,
    string Name,
    double X0,
    double Y0,
    double X1,
    double Y1,
    bool Selected,
    /// <summary>An IK handle or pole rather than a bone of the body — drawn apart, because it is dragged for a different reason.</summary>
    bool IsHandle = false,
    BoneGhost Ghost = BoneGhost.None);

/// <summary>Which part of a bone a press grabbed.</summary>
public enum BoneGrab
{
    None,

    /// <summary>The origin handle — moving it moves the bone's rest offset.</summary>
    Origin,

    /// <summary>The tip handle — dragging it rotates (pose mode) or re-lengths (bind mode).</summary>
    Tip,

    /// <summary>The shaft — selects, and drags rotate in pose mode.</summary>
    Body,
}

/// <param name="Id">The bone that was hit, or null for empty canvas.</param>
public readonly record struct BoneHit(string? Id, BoneGrab Grab = BoneGrab.None)
{
    public bool Any => Id is not null;
}

/// <summary>
/// The armature overlay's decisions: what a press hit, and the angles and
/// lengths a drag means. Pure functions of points, chrome and zoom —
/// <c>RigOverlay</c>'s discipline, for <c>RigOverlay</c>'s reason: gesture
/// arithmetic in <c>CanvasControl</c> is gesture arithmetic nobody can test.
/// </summary>
public static class ArmatureOverlay
{
    /// <summary>Handle half-size in screen pixels — <c>RigOverlay</c>'s number, on purpose.</summary>
    public const double HandleScreenRadius = 5;

    /// <summary>How close a press must be to a bone's shaft to grab it, in screen pixels.</summary>
    public const double BodyScreenRadius = 6;

    /// <summary>The shortest a bone can be drawn or dragged to, in document pixels.</summary>
    /// <remarks>
    /// Not zero, for <c>RigOverlay.MinimumShapeSize</c>'s reason: a zero-length
    /// bone has no direction, so a pose rotation of it means nothing, and it is
    /// invisible and ungrabbable besides.
    /// </remarks>
    public const double MinimumLength = 4;

    /// <summary>
    /// What a press at this document point grabbed.
    /// </summary>
    /// <remarks>
    /// Tips before origins before bodies, and selected bones first within each:
    /// a tip sits on its own shaft, so body-before-tip would make tips
    /// ungrabbable, and a child's origin sits on its parent's tip by
    /// construction — the selected bone winning is what lets an artist work a
    /// joint without the neighbour stealing every press.
    /// </remarks>
    public static BoneHit Hit(IReadOnlyList<BoneChrome> bones, double x, double y, double scale)
    {
        var handle = HandleScreenRadius / Math.Max(scale, 0.01);
        var body = BodyScreenRadius / Math.Max(scale, 0.01);

        foreach (var selectedFirst in new[] { true, false })
        {
            // Ghosts are furniture: they show a neighbouring pose, and a
            // press through one must land on whatever real bone is under it.
            foreach (var bone in bones)
            {
                if (bone.Ghost is not BoneGhost.None || bone.Selected != selectedFirst) continue;
                if (Dist(x, y, bone.X1, bone.Y1) <= handle) return new BoneHit(bone.Id, BoneGrab.Tip);
            }
            foreach (var bone in bones)
            {
                if (bone.Ghost is not BoneGhost.None || bone.Selected != selectedFirst) continue;
                if (Dist(x, y, bone.X0, bone.Y0) <= handle) return new BoneHit(bone.Id, BoneGrab.Origin);
            }
            foreach (var bone in bones)
            {
                if (bone.Ghost is not BoneGhost.None || bone.Selected != selectedFirst) continue;
                if (DistToSegment(x, y, bone) <= body) return new BoneHit(bone.Id, BoneGrab.Body);
            }
        }
        return new BoneHit(null);
    }

    /// <summary>The world angle, clockwise degrees, of the pointer seen from a pivot.</summary>
    public static double AngleFrom(double pivotX, double pivotY, double x, double y) =>
        Math.Atan2(y - pivotY, x - pivotX) * 180 / Math.PI;

    /// <summary>What a create-drag from press to pointer makes: a length and a world angle.</summary>
    /// <remarks>Length is floored at <see cref="MinimumLength"/>, so a click without a drag still makes a bone somebody can find.</remarks>
    public static (double Length, double AngleDeg) CreateFrom(
        double pressX, double pressY, double x, double y) =>
        (Math.Max(MinimumLength, Dist(pressX, pressY, x, y)), AngleFrom(pressX, pressY, x, y));

    private static double Dist(double x0, double y0, double x1, double y1)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double DistToSegment(double x, double y, BoneChrome bone)
    {
        var vx = bone.X1 - bone.X0;
        var vy = bone.Y1 - bone.Y0;
        var len2 = vx * vx + vy * vy;
        var t = len2 <= 0 ? 0 : Math.Clamp(((x - bone.X0) * vx + (y - bone.Y0) * vy) / len2, 0, 1);
        return Dist(x, y, bone.X0 + t * vx, bone.Y0 + t * vy);
    }
}

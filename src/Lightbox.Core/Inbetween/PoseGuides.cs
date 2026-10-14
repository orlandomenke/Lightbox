using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.Core.Inbetween;

/// <summary>A joint of a pose at some point between the keys.</summary>
public readonly record struct PosedJoint(string Name, StrokePoint At);

/// <summary>A part of a pose at some point between the keys.</summary>
public sealed record PosedPart(string Name, IReadOnlyList<PosedJoint> Joints);

/// <summary>
/// One bone's volume between the keys: the bone from one joint to the next,
/// and how wide the part is around it, measured from the keys' ink.
/// </summary>
public readonly record struct BoneVolume(StrokePoint From, StrokePoint To, double Width);

/// <summary>
/// The pose guide between two keys (Q274): where the parts are part of the way
/// from key A to key B, and how big they are.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pose.</b> Each part's root joint moves in a straight line; every bone
/// after it swings from its angle in key A to its angle in key B, the short way
/// round, and its length blends between the two — the arc an animator draws a
/// limb on, so a limb keeps its length through a swing (Q273's reason). At the
/// keys it is exactly the keys' pose.
/// </para>
/// <para>
/// <b>Volume.</b> Measured, not guessed: at points along each bone, the
/// nearest ink either side of it in the key, inside a reach of the bone's
/// length; the width is the typical of those, blended between the keys. A bone
/// with no ink around it in one key takes the other key's width; with none in
/// either it has none, and the guide shows the bone alone.
/// </para>
/// </remarks>
public static class PoseGuides
{
    /// <summary>The pose at <paramref name="t"/> (0 is key A, 1 key B).</summary>
    public static List<PosedPart> At(PoseGuide guide, double t)
    {
        var parts = new List<PosedPart>(guide.Parts.Count);
        foreach (var part in guide.Parts)
        {
            var joints = new List<PosedJoint>(part.Joints.Count);
            if (part.Joints.Count > 0)
            {
                var root = part.Joints[0];
                var at = new StrokePoint(root.A.X + (root.B.X - root.A.X) * t, root.A.Y + (root.B.Y - root.A.Y) * t, 0);
                joints.Add(new PosedJoint(root.Name, at));
                for (var i = 1; i < part.Joints.Count; i++)
                {
                    var (p, j) = (part.Joints[i - 1], part.Joints[i]);
                    var (ax, ay) = (j.A.X - p.A.X, j.A.Y - p.A.Y);
                    var (bx, by) = (j.B.X - p.B.X, j.B.Y - p.B.Y);
                    var (angleA, angleB) = (Math.Atan2(ay, ax), Math.Atan2(by, bx));
                    var turn = Math.IEEERemainder(angleB - angleA, 2 * Math.PI);
                    var angle = angleA + turn * t;
                    var length = Math.Sqrt(ax * ax + ay * ay) * (1 - t) + Math.Sqrt(bx * bx + by * by) * t;
                    at = new StrokePoint(at.X + length * Math.Cos(angle), at.Y + length * Math.Sin(angle), 0);
                    joints.Add(new PosedJoint(j.Name, at));
                }
            }
            parts.Add(new PosedPart(part.Name, joints));
        }
        return parts;
    }

    /// <summary>
    /// Every bone of every part at <paramref name="t"/>, with its width measured
    /// from <paramref name="keyA"/> and <paramref name="keyB"/>'s ink.
    /// </summary>
    public static List<List<BoneVolume>> Volumes(PoseGuide guide, IReadOnlyList<Stroke> keyA, IReadOnlyList<Stroke> keyB, double t)
    {
        var inkA = Ink(keyA);
        var inkB = Ink(keyB);
        var posed = At(guide, t);
        var volumes = new List<List<BoneVolume>>(guide.Parts.Count);
        for (var p = 0; p < guide.Parts.Count; p++)
        {
            var joints = guide.Parts[p].Joints;
            var bones = new List<BoneVolume>();
            for (var i = 1; i < joints.Count; i++)
            {
                var wa = Width(Point(joints[i - 1].A), Point(joints[i].A), inkA);
                var wb = Width(Point(joints[i - 1].B), Point(joints[i].B), inkB);
                var width = (wa, wb) switch
                {
                    ({ } x, { } y) => x * (1 - t) + y * t,
                    ({ } x, null) => x,
                    (null, { } y) => y,
                    _ => 0,
                };
                bones.Add(new BoneVolume(posed[p].Joints[i - 1].At, posed[p].Joints[i].At, width));
            }
            volumes.Add(bones);
        }
        return volumes;
    }

    private static StrokePoint Point(PosePoint p) => new(p.X, p.Y, 0);

    /// <summary>Every stroke's points, a few pixels apart, as one cloud of ink.</summary>
    private static List<StrokePoint> Ink(IReadOnlyList<Stroke> strokes) =>
        [.. StrokeRecordCleaner.EffectiveStrokes(strokes)
            .Where(s => s.Tool is not (ToolKind.Eraser or ToolKind.ClearRegion))
            .SelectMany(s => LinePieces.Even(s.Points, 3))];

    /// <summary>
    /// How wide the part is around a bone in one key: at five points along it,
    /// the nearest ink on each side within reach; the median of those that
    /// found ink on both sides, or null when none did.
    /// </summary>
    private static double? Width(StrokePoint from, StrokePoint to, List<StrokePoint> ink)
    {
        var (dx, dy) = (to.X - from.X, to.Y - from.Y);
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-6 || ink.Count == 0) return null;
        var (ux, uy) = (dx / length, dy / length);
        var reach = length * 0.8;
        var widths = new List<double>();
        foreach (var f in new[] { 0.2, 0.35, 0.5, 0.65, 0.8 })
        {
            var (cx, cy) = (from.X + dx * f, from.Y + dy * f);
            double left = double.MaxValue, right = double.MaxValue;
            foreach (var q in ink)
            {
                var (rx, ry) = (q.X - cx, q.Y - cy);
                var along = rx * ux + ry * uy;
                // Only ink beside this point of the bone, not further along it.
                if (Math.Abs(along) > length * 0.1) continue;
                var across = -rx * uy + ry * ux;
                if (across > 0 && across < reach) right = Math.Min(right, across);
                else if (across < 0 && -across < reach) left = Math.Min(left, -across);
            }
            if (left < double.MaxValue && right < double.MaxValue) widths.Add(left + right);
        }
        if (widths.Count == 0) return null;
        widths.Sort();
        return widths[widths.Count / 2];
    }
}

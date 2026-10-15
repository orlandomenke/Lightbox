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
/// either it borrows the nearest measured bone's of the same part, and with
/// none in the part it has none, and the guide shows the bone alone.
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
    public static List<List<BoneVolume>> Volumes(PoseGuide guide, IReadOnlyList<Stroke> keyA, IReadOnlyList<Stroke> keyB, double t) =>
        Volumes(guide, Ink(keyA), Ink(keyB), t);

    /// <summary>
    /// Every bone of every part at <paramref name="t"/>, with its width measured
    /// from the keys' ink gathered by <see cref="Ink"/> — once per key, since
    /// the guide is asked for on every playhead step and every move of a drag.
    /// </summary>
    public static List<List<BoneVolume>> Volumes(PoseGuide guide, PoseInk inkA, PoseInk inkB, double t)
    {
        var posed = At(guide, t);
        var volumes = new List<List<BoneVolume>>(guide.Parts.Count);
        for (var p = 0; p < guide.Parts.Count; p++)
        {
            var joints = guide.Parts[p].Joints;
            var widths = new double[Math.Max(0, joints.Count - 1)];
            for (var i = 1; i < joints.Count; i++)
            {
                var wa = Width(Point(joints[i - 1].A), Point(joints[i].A), inkA);
                var wb = Width(Point(joints[i - 1].B), Point(joints[i].B), inkB);
                widths[i - 1] = (wa, wb) switch
                {
                    ({ } x, { } y) => x * (1 - t) + y * t,
                    ({ } x, null) => x,
                    (null, { } y) => y,
                    _ => 0,
                };
            }
            // A bone nothing could be measured around — a toe past the drawn
            // shoe, a joint the model placed off the form — takes its nearest
            // measured neighbour's width rather than showing no volume at all.
            // Only measured widths are lent: a borrowed one is already a guess.
            var measured = widths.Select(w => w > 0).ToArray();
            for (var i = 0; i < widths.Length; i++)
            {
                if (measured[i]) continue;
                for (var d = 1; d < widths.Length; d++)
                {
                    if (i - d >= 0 && measured[i - d]) { widths[i] = widths[i - d]; break; }
                    if (i + d < widths.Length && measured[i + d]) { widths[i] = widths[i + d]; break; }
                }
            }
            var bones = new List<BoneVolume>(widths.Length);
            for (var i = 1; i < joints.Count; i++)
            {
                bones.Add(new BoneVolume(posed[p].Joints[i - 1].At, posed[p].Joints[i].At, widths[i - 1]));
            }
            volumes.Add(bones);
        }
        return volumes;
    }

    private static StrokePoint Point(PosePoint p) => new(p.X, p.Y, 0);

    /// <summary>A key's strokes as one cloud of ink, a few pixels apart, filed by place so a bone reads only the ink near it.</summary>
    public static PoseInk Ink(IReadOnlyList<Stroke> strokes) =>
        new([.. StrokeRecordCleaner.EffectiveStrokes(strokes)
            .Where(s => s.Tool is not (ToolKind.Eraser or ToolKind.ClearRegion))
            .SelectMany(s => LinePieces.Even(s.Points, 3))]);

    /// <summary>
    /// How far either side of a bone ink still counts as its part, at least:
    /// a short bone in a wide part — a toe in a shoe, a hand — would otherwise
    /// reach no further than its own length and find no edge at all.
    /// </summary>
    private const double MinReach = 30;

    /// <summary>
    /// How wide the part is around a bone in one key: at five points along it,
    /// the nearest ink on each side within reach; the median of those that
    /// found ink on both sides, or null when none did.
    /// </summary>
    private static double? Width(StrokePoint from, StrokePoint to, PoseInk ink)
    {
        var (dx, dy) = (to.X - from.X, to.Y - from.Y);
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-6 || ink.Count == 0) return null;
        var (ux, uy) = (dx / length, dy / length);
        var reach = Math.Max(length * 0.8, MinReach);
        var beside = Math.Max(length * 0.1, 3);
        var widths = new List<double>();
        foreach (var f in new[] { 0.2, 0.35, 0.5, 0.65, 0.8 })
        {
            var (cx, cy) = (from.X + dx * f, from.Y + dy * f);
            double left = double.MaxValue, right = double.MaxValue;
            foreach (var q in ink.Near(cx, cy, reach + beside))
            {
                var (rx, ry) = (q.X - cx, q.Y - cy);
                var along = rx * ux + ry * uy;
                // Only ink beside this point of the bone, not further along it.
                if (Math.Abs(along) > beside) continue;
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

/// <summary>
/// One key's ink for measuring a pose's volumes: its points, filed in a grid
/// so a bone reads the ink around it rather than all of it.
/// </summary>
public sealed class PoseInk
{
    private const double Cell = 32;
    private readonly Dictionary<(int, int), List<StrokePoint>> _cells = [];

    public PoseInk(IReadOnlyList<StrokePoint> points)
    {
        Count = points.Count;
        foreach (var p in points)
        {
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y)) continue;
            var key = ((int)Math.Floor(p.X / Cell), (int)Math.Floor(p.Y / Cell));
            if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = [];
            list.Add(p);
        }
    }

    /// <summary>How many points the key's ink has.</summary>
    public int Count { get; }

    /// <summary>The points within <paramref name="radius"/> of a place, and perhaps a few beyond: every one the caller could want.</summary>
    public IEnumerable<StrokePoint> Near(double x, double y, double radius)
    {
        var (x0, x1) = ((int)Math.Floor((x - radius) / Cell), (int)Math.Floor((x + radius) / Cell));
        var (y0, y1) = ((int)Math.Floor((y - radius) / Cell), (int)Math.Floor((y + radius) / Cell));
        for (var cy = y0; cy <= y1; cy++)
        for (var cx = x0; cx <= x1; cx++)
        {
            if (!_cells.TryGetValue((cx, cy), out var list)) continue;
            foreach (var p in list) yield return p;
        }
    }
}

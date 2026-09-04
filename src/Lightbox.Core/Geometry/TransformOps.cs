namespace Lightbox.Core.Geometry;

using Lightbox.Core.Documents;

/// <summary>
/// Deterministic geometric transforms over the stroke record. Because
/// frames are stroke data, one point map applied to N frames is N lossless
/// edits — strokes re-render at full fidelity afterwards. Only raster
/// baselines ever need pixel resampling (handled by the caller, which owns
/// a rasterizer).
/// </summary>
public static class TransformOps
{
    /// <summary>A 2D point map (document space in, document space out).</summary>
    public delegate (double X, double Y) PointMap(double x, double y);

    /// <summary>
    /// Affine map built the way the gizmo composes it: translate the pivot
    /// to the origin, scale, rotate, translate back, then offset.
    /// </summary>
    public static PointMap Affine(
        double pivotX, double pivotY,
        double scaleX, double scaleY,
        double angleRadians,
        double offsetX, double offsetY)
    {
        var cos = Math.Cos(angleRadians);
        var sin = Math.Sin(angleRadians);
        return (x, y) =>
        {
            var dx = (x - pivotX) * scaleX;
            var dy = (y - pivotY) * scaleY;
            return (pivotX + dx * cos - dy * sin + offsetX,
                    pivotY + dx * sin + dy * cos + offsetY);
        };
    }

    /// <summary>
    /// Homography mapping the four source corners onto the four destination
    /// corners (order-sensitive; both quads as [x0,y0,...,x3,y3]). Solved
    /// directly from the standard 8-equation linear system.
    /// </summary>
    public static PointMap Perspective(double[] src, double[] dst)
    {
        var h = PerspectiveCoefficients(src, dst);
        return (x, y) =>
        {
            var w = h[6] * x + h[7] * y + 1;
            if (Math.Abs(w) < 1e-12) w = 1e-12;
            return ((h[0] * x + h[1] * y + h[2]) / w,
                    (h[3] * x + h[4] * y + h[5]) / w);
        };
    }

    /// <summary>
    /// The eight homography coefficients (a..h) for the same mapping —
    /// exposed so callers can hand the matrix to a pixel rasterizer
    /// (row-major 3×3 with the ninth element fixed at 1).
    /// </summary>
    public static double[] PerspectiveCoefficients(double[] src, double[] dst)
    {
        if (src.Length != 8 || dst.Length != 8)
            throw new ArgumentException("Quads must contain four x,y pairs.");
        // Build A·h = b for h = (a,b,c,d,e,f,g,h) with x' = (a x + b y + c)/(g x + h y + 1).
        var a = new double[8, 8];
        var b = new double[8];
        for (var i = 0; i < 4; i++)
        {
            double x = src[i * 2], y = src[i * 2 + 1];
            double u = dst[i * 2], v = dst[i * 2 + 1];
            var r = i * 2;
            a[r, 0] = x; a[r, 1] = y; a[r, 2] = 1;
            a[r, 6] = -x * u; a[r, 7] = -y * u;
            b[r] = u;
            r++;
            a[r, 3] = x; a[r, 4] = y; a[r, 5] = 1;
            a[r, 6] = -x * v; a[r, 7] = -y * v;
            b[r] = v;
        }
        return SolveLinear(a, b);
    }

    /// <summary>
    /// Transform a stroke in place: every point (and fill hole), plus the
    /// brush size scaled by <paramref name="sizeScale"/> so line weight
    /// follows the geometry.
    /// </summary>
    /// <param name="mapClip">
    /// The same map, applied to the stroke's clip region — given the id of a
    /// region, hand back the id of that region moved. Null leaves the clip
    /// alone, which is right only for a caller that has already decided where
    /// the clip goes.
    /// </param>
    /// <remarks>
    /// <b>B340. A clip is geometry the stroke carries, not a setting.</b> The
    /// points moved and the stencil did not, so a whole-layer rotation cut a
    /// clipped mark against the boundary it used to sit behind — the ink
    /// visible after the commit was a different part of the stroke from the
    /// ink visible during the drag, which reads on the canvas as one line
    /// jumping somewhere else while everything around it rotated correctly.
    /// A clipping transform of a <em>region</em> already knew this (B319's
    /// <c>MapClip</c>); every other transform did not, and the Move tool
    /// commits through the same path.
    /// </remarks>
    public static void TransformStroke(
        Stroke stroke, PointMap map, double sizeScale = 1, Func<string, string>? mapClip = null)
    {
        MapPoints(stroke.Points, map);
        if (stroke.Holes is not null)
        {
            foreach (var hole in stroke.Holes) MapPoints(hole, map);
        }
        MapPath(stroke, map);
        if (mapClip is not null && stroke.ClipId is { } clip) stroke.ClipId = mapClip(clip);
        if (Math.Abs(sizeScale - 1) > 1e-9)
        {
            stroke.Brush.Size = Math.Clamp(stroke.Brush.Size * sizeScale, 0.1, 2000);
        }
    }

    /// <summary>
    /// Carry an authored path through the same transform as its points, or drop
    /// it where that cannot be done exactly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="StrokePath"/>'s invariant, and this is its first caller.</b>
    /// A stroke's path and its points must never disagree — so a transform either
    /// moves the nodes with the points or removes the path. Leaving a stale path
    /// behind would be the worst outcome available: nothing renders differently,
    /// so nothing looks wrong, until the artist reshapes a node and the line jumps
    /// back to where it used to be.
    /// </para>
    /// <para>
    /// <b>Handles are mapped as differences, not as points.</b> A handle is an
    /// offset from its node, so it is transformed by mapping the control point and
    /// subtracting the mapped node — which is the same thing as applying the
    /// linear part of the map and is correct for a perspective one too, where
    /// applying the map to the offset directly would translate it as well.
    /// </para>
    /// <para>
    /// <b>The one case that drops.</b> A non-invertible or degenerate map can
    /// collapse a node and its control point onto the same place, which turns a
    /// curve into a corner without the artist asking. That is a real change to the
    /// authored geometry rather than a move of it, so the path goes and the points
    /// — which are the truth — stand alone. A later fit brings it back.
    /// </para>
    /// </remarks>
    private static void MapPath(Stroke stroke, PointMap map)
    {
        if (stroke.Path is not { IsUsable: true } path) return;

        var nodes = path.Nodes;
        for (var i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            var (x, y) = map(n.X, n.Y);
            var (inX, inY) = map(n.X + n.InX, n.Y + n.InY);
            var (outX, outY) = map(n.X + n.OutX, n.Y + n.OutY);

            if (!double.IsFinite(x) || !double.IsFinite(y)
                || !double.IsFinite(inX) || !double.IsFinite(inY)
                || !double.IsFinite(outX) || !double.IsFinite(outY))
            {
                stroke.Path = null;
                return;
            }

            nodes[i] = n with
            {
                X = x,
                Y = y,
                InX = inX - x,
                InY = inY - y,
                OutX = outX - x,
                OutY = outY - y,
            };
        }
    }

    /// <summary>
    /// Transform every stroke of a frame that passes <paramref name="filter"/>
    /// (null = all). Returns how many strokes moved. The raster baseline of a
    /// painted frame is NOT touched here — the caller resamples it.
    /// </summary>
    /// <inheritdoc cref="TransformStroke" path="/param[@name='mapClip']"/>
    public static int TransformFrame(
        Frame frame, PointMap map, double sizeScale = 1, Func<Stroke, bool>? filter = null,
        Func<string, string>? mapClip = null)
    {
        var strokes = StrokesOf(frame);
        var count = 0;
        foreach (var stroke in strokes)
        {
            if (filter is not null && !filter(stroke)) continue;
            TransformStroke(stroke, map, sizeScale, mapClip);
            count++;
        }
        return count;
    }

    /// <summary>The frame's editable stroke list.</summary>
    /// <remarks>
    /// Kept as a named method rather than inlined to <c>frame.Strokes</c> at every
    /// call site: it used to switch on the two frame classes, and the callers read
    /// better for saying what they want than for reaching through a field.
    /// </remarks>
    public static List<Stroke> StrokesOf(Frame frame) => frame.Strokes;

    /// <summary>
    /// Region filter: true when the majority of a stroke's points fall
    /// inside the mask (row-major w×h booleans). Strokes move whole — no
    /// point-tearing — so connected drawings stay connected.
    /// </summary>
    /// <remarks>
    /// A gradient is judged by what it covers rather than by where its points
    /// are, because its points are not a centreline: they are the two ends of
    /// the axis the ramp runs along, and the ramp colours the whole layer (or
    /// its clip) regardless of where they sit. Counting them is how a
    /// selection drawn straight over a visible gradient used to come back
    /// "nothing to transform in this scope" — the pixels were plainly inside,
    /// and the two points that decided the question were not.
    ///
    /// Covering everything means a gradient joins any region-limited
    /// transform, and moves whole when it does. That is the same rule the rest
    /// of this method follows, applied to an object whose extent happens to be
    /// the canvas.
    /// </remarks>
    /// <summary>
    /// Region filter: true when <em>any</em> of a stroke's points falls inside
    /// the mask. A gradient is judged by what it covers, exactly as in
    /// <see cref="MajorityInside"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B319. The rule a clipping transform needs, and the one a
    /// whole-stroke transform could not use.</b> While a region moved strokes
    /// entire, a majority was the only defensible bar — half in and half out
    /// had to resolve to one answer, and taking a stroke on a single point
    /// inside would have dragged a whole line across the canvas because its
    /// tip grazed the marquee.
    /// </para>
    /// <para>
    /// Once the region moves only the <em>part</em> it covers, that reasoning
    /// inverts. A stroke with one point inside has one point's worth to
    /// contribute and nothing else of it moves, so there is no longer a
    /// penalty for saying yes — and there is a large penalty for saying no,
    /// because "the majority is outside" is what made a box over part of a
    /// drawing report <em>nothing to transform in this scope</em>.
    /// </para>
    /// </remarks>
    public static bool AnyInside(Stroke stroke, bool[] mask, int w, int h)
    {
        if (stroke.Tool == ToolKind.Gradient) return Array.IndexOf(mask, true) >= 0;
        foreach (var p in stroke.Points)
        {
            var x = (int)Math.Round(p.X);
            var y = (int)Math.Round(p.Y);
            if (x >= 0 && x < w && y >= 0 && y < h && mask[y * w + x]) return true;
        }
        return false;
    }

    public static bool MajorityInside(Stroke stroke, bool[] mask, int w, int h)
    {
        if (stroke.Tool == ToolKind.Gradient) return Array.IndexOf(mask, true) >= 0;
        if (stroke.Points.Count == 0) return false;
        var inside = 0;
        foreach (var p in stroke.Points)
        {
            var x = (int)Math.Round(p.X);
            var y = (int)Math.Round(p.Y);
            if (x >= 0 && x < w && y >= 0 && y < h && mask[y * w + x]) inside++;
        }
        return inside * 2 >= stroke.Points.Count;
    }

    /// <summary>
    /// Bounds of one stroke, padded by its brush radius so the box wraps the
    /// painted pixels rather than the centerline; null when it has no points.
    /// </summary>
    /// <remarks>
    /// Split out of the frames overload below so that "where is this stroke"
    /// has one answer. The MCP surface reports a box per stroke so an agent can
    /// tell which strokes a change touches without reading their geometry, and
    /// a second implementation of the padding rule would let that box and the
    /// transform gizmo disagree about the same stroke.
    /// </remarks>
    public static (double MinX, double MinY, double MaxX, double MaxY)? Bounds(Stroke stroke)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        var any = false;
        var reach = stroke.Brush.Size / 2;
        foreach (var p in stroke.Points)
        {
            any = true;
            if (p.X - reach < minX) minX = p.X - reach;
            if (p.Y - reach < minY) minY = p.Y - reach;
            if (p.X + reach > maxX) maxX = p.X + reach;
            if (p.Y + reach > maxY) maxY = p.Y + reach;
        }
        return any ? (minX, minY, maxX, maxY) : null;
    }

    /// <summary>
    /// Bounds of all (filtered) strokes across frames, padded by each
    /// stroke's brush radius so the box wraps the painted pixels, not just
    /// the centerlines; null when empty.
    /// </summary>
    public static (double MinX, double MinY, double MaxX, double MaxY)? Bounds(
        IEnumerable<Frame> frames, Func<Stroke, bool>? filter = null)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        var any = false;
        foreach (var frame in frames)
        {
            foreach (var stroke in StrokesOf(frame))
            {
                if (filter is not null && !filter(stroke)) continue;
                if (Bounds(stroke) is not { } b) continue;
                any = true;
                if (b.MinX < minX) minX = b.MinX;
                if (b.MinY < minY) minY = b.MinY;
                if (b.MaxX > maxX) maxX = b.MaxX;
                if (b.MaxY > maxY) maxY = b.MaxY;
            }
        }
        return any ? (minX, minY, maxX, maxY) : null;
    }

    private static void MapPoints(List<StrokePoint> points, PointMap map)
    {
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var (x, y) = map(p.X, p.Y);
            points[i] = p with { X = x, Y = y };
        }
    }

    /// <summary>Gaussian elimination with partial pivoting (8×8 system).</summary>
    private static double[] SolveLinear(double[,] a, double[] b)
    {
        var n = b.Length;
        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var row = col + 1; row < n; row++)
            {
                if (Math.Abs(a[row, col]) > Math.Abs(a[pivot, col])) pivot = row;
            }
            if (Math.Abs(a[pivot, col]) < 1e-12)
                throw new InvalidOperationException("Degenerate quad: perspective transform is not solvable.");
            if (pivot != col)
            {
                for (var k = 0; k < n; k++) (a[col, k], a[pivot, k]) = (a[pivot, k], a[col, k]);
                (b[col], b[pivot]) = (b[pivot], b[col]);
            }
            for (var row = col + 1; row < n; row++)
            {
                var f = a[row, col] / a[col, col];
                for (var k = col; k < n; k++) a[row, k] -= f * a[col, k];
                b[row] -= f * b[col];
            }
        }
        var x = new double[n];
        for (var row = n - 1; row >= 0; row--)
        {
            var sum = b[row];
            for (var k = row + 1; k < n; k++) sum -= a[row, k] * x[k];
            x[row] = sum / a[row, row];
        }
        return x;
    }
}

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
    public static void TransformStroke(Stroke stroke, PointMap map, double sizeScale = 1)
    {
        MapPoints(stroke.Points, map);
        if (stroke.Holes is not null)
        {
            foreach (var hole in stroke.Holes) MapPoints(hole, map);
        }
        if (Math.Abs(sizeScale - 1) > 1e-9)
        {
            stroke.Brush.Size = Math.Clamp(stroke.Brush.Size * sizeScale, 0.1, 2000);
        }
    }

    /// <summary>
    /// Transform every stroke of a frame that passes <paramref name="filter"/>
    /// (null = all). Returns how many strokes moved. The raster baseline of a
    /// painted frame is NOT touched here — the caller resamples it.
    /// </summary>
    public static int TransformFrame(Frame frame, PointMap map, double sizeScale = 1, Func<Stroke, bool>? filter = null)
    {
        var strokes = StrokesOf(frame);
        var count = 0;
        foreach (var stroke in strokes)
        {
            if (filter is not null && !filter(stroke)) continue;
            TransformStroke(stroke, map, sizeScale);
            count++;
        }
        return count;
    }

    /// <summary>The editable stroke list of either frame kind.</summary>
    public static List<Stroke> StrokesOf(Frame frame) => frame switch
    {
        PaintedFrame p => p.Strokes,
        VectorFrame v => v.Strokes,
        _ => throw new ArgumentException($"Unknown frame kind {frame.GetType().Name}"),
    };

    /// <summary>
    /// Region filter: true when the majority of a stroke's points fall
    /// inside the mask (row-major w×h booleans). Strokes move whole — no
    /// point-tearing — so connected drawings stay connected.
    /// </summary>
    public static bool MajorityInside(Stroke stroke, bool[] mask, int w, int h)
    {
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
                var reach = stroke.Brush.Size / 2;
                foreach (var p in stroke.Points)
                {
                    any = true;
                    if (p.X - reach < minX) minX = p.X - reach;
                    if (p.Y - reach < minY) minY = p.Y - reach;
                    if (p.X + reach > maxX) maxX = p.X + reach;
                    if (p.Y + reach > maxY) maxY = p.Y + reach;
                }
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

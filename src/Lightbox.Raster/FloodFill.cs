using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// The fill tool's algorithm, pure and deterministic: scanline flood fill
/// over a sampled bitmap with color tolerance, optional gap closing (treat
/// openings up to N px as closed), grow/shrink of the result, and contour
/// tracing (outer boundary + holes) so the region can live in the stroke
/// record as geometry rather than pixels.
/// </summary>
public static class FloodFill
{
    public sealed record Options(
        double Tolerance = 32,     // 0..255 color distance from the seed
        double GapPx = 0,          // openings up to this many px count as closed
        double GrowPx = 0,         // dilate (+) or erode (−) the result
        int MaxRegionArea = 0);    // 0 = unlimited

    public sealed record Result(
        List<StrokePoint> Outer,
        List<List<StrokePoint>> Holes,
        int Area);

    /// <summary>
    /// Flood from (seedX, seedY) over <paramref name="sample"/>; returns the
    /// traced region or null when the seed is out of bounds / region empty.
    /// <paramref name="selection"/> (optional) limits the fill to a mask.
    /// </summary>
    public static Result? Fill(SKBitmap sample, int seedX, int seedY, Options options, bool[]? selection = null)
    {
        int w = sample.Width, h = sample.Height;
        if (seedX < 0 || seedY < 0 || seedX >= w || seedY >= h) return null;

        // Barrier map: pixels too different from the seed color.
        var seed = sample.GetPixel(seedX, seedY);
        var barrier = new bool[w * h];
        var pixels = sample.Pixels; // one copy; SKColor[]
        var tolerance = Math.Clamp(options.Tolerance, 0, 255);
        for (var i = 0; i < pixels.Length; i++)
        {
            barrier[i] = ColorDistance(pixels[i], seed) > tolerance;
        }
        if (selection is not null)
        {
            for (var i = 0; i < barrier.Length; i++)
            {
                if (!selection[i]) barrier[i] = true;
            }
        }

        // Gap closing: thicken barriers so openings up to GapPx read as closed…
        var gap = (int)Math.Round(Math.Clamp(options.GapPx, 0, 64) / 2);
        var flooded = ScanlineFill(
            gap > 0 ? Dilate(barrier, w, h, gap) : barrier, w, h, seedX, seedY, options.MaxRegionArea);
        if (flooded is null) return null;

        // …then win back the border zone the thickened barriers stole, without
        // ever crossing a real barrier.
        if (gap > 0)
        {
            flooded = DilateInto(flooded, barrier, w, h, gap);
        }

        // Over/underfill.
        var grow = (int)Math.Round(Math.Clamp(options.GrowPx, -32, 32));
        flooded = grow switch
        {
            > 0 => Dilate(flooded, w, h, grow),
            < 0 => Erode(flooded, w, h, -grow),
            _ => flooded,
        };

        var area = flooded.Count(x => x);
        if (area == 0) return null;

        var (outer, holes) = ContourTracer.Trace(flooded, w, h);
        if (outer.Count < 3) return null;
        return new Result(
            Simplify(outer),
            holes.Select(hole => Simplify(hole)).Where(c => c.Count >= 3).ToList(),
            area);
    }

    private static double ColorDistance(SKColor a, SKColor b)
    {
        // Alpha-aware: empty vs painted is the dominant signal for line art.
        var da = Math.Abs(a.Alpha - b.Alpha);
        var dr = Math.Abs(a.Red - b.Red);
        var dg = Math.Abs(a.Green - b.Green);
        var db = Math.Abs(a.Blue - b.Blue);
        return Math.Max(da, (dr + dg + db) / 3.0);
    }

    // ---- flood ---------------------------------------------------------------

    private static bool[]? ScanlineFill(bool[] barrier, int w, int h, int sx, int sy, int maxArea)
    {
        if (barrier[sy * w + sx]) return null;
        var filled = new bool[w * h];
        var stack = new Stack<(int X, int Y)>();
        stack.Push((sx, sy));
        var area = 0;

        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            var i = y * w + x;
            if (filled[i] || barrier[i]) continue;

            // walk to the span's left edge
            var left = x;
            while (left > 0 && !barrier[y * w + left - 1] && !filled[y * w + left - 1]) left--;
            var right = x;
            while (right < w - 1 && !barrier[y * w + right + 1] && !filled[y * w + right + 1]) right++;

            for (var px = left; px <= right; px++)
            {
                filled[y * w + px] = true;
                area++;
            }
            if (maxArea > 0 && area > maxArea) return filled;

            for (var py = y - 1; py <= y + 1; py += 2)
            {
                if (py < 0 || py >= h) continue;
                for (var px = left; px <= right; px++)
                {
                    var pi = py * w + px;
                    if (!filled[pi] && !barrier[pi]) stack.Push((px, py));
                }
            }
        }
        return filled;
    }

    /// <summary>
    /// Trace EVERY region in a mask: outer contours of all components plus
    /// their holes — one even-odd contour set (used for selections, which can
    /// be disjoint).
    /// </summary>
    public static List<List<StrokePoint>> TraceAllContours(bool[] mask, int w, int h)
    {
        var contours = new List<List<StrokePoint>>();
        var seen = new bool[w * h];
        for (var i = 0; i < mask.Length; i++)
        {
            if (!mask[i] || seen[i]) continue;
            // extract this component
            var component = new bool[w * h];
            var queue = new Queue<int>();
            queue.Enqueue(i);
            seen[i] = true;
            component[i] = true;
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                int x = p % w, y = p / w;
                foreach (var n in Neighbors(x, y, w, h))
                {
                    if (mask[n] && !seen[n])
                    {
                        seen[n] = true;
                        component[n] = true;
                        queue.Enqueue(n);
                    }
                }
            }
            var (outer, holes) = ContourTracer.Trace(component, w, h);
            if (outer.Count >= 3) contours.Add(Simplify(outer));
            contours.AddRange(holes.Select(hole => Simplify(hole)).Where(c => c.Count >= 3));
        }
        return contours;
    }

    private static IEnumerable<int> Neighbors(int x, int y, int w, int h)
    {
        if (x > 0) yield return y * w + x - 1;
        if (x < w - 1) yield return y * w + x + 1;
        if (y > 0) yield return (y - 1) * w + x;
        if (y < h - 1) yield return (y + 1) * w + x;
    }

    // ---- morphology ------------------------------------------------------------

    public static bool[] Dilate(bool[] mask, int w, int h, int radius)
    {
        var result = mask;
        for (var step = 0; step < radius; step++) result = Dilate1(result, w, h);
        return result;
    }

    public static bool[] Erode(bool[] mask, int w, int h, int radius)
    {
        var inverted = mask.Select(v => !v).ToArray();
        inverted = Dilate(inverted, w, h, radius);
        return inverted.Select(v => !v).ToArray();
    }

    private static bool[] Dilate1(bool[] mask, int w, int h)
    {
        var result = new bool[mask.Length];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x;
                if (mask[i]
                    || (x > 0 && mask[i - 1]) || (x < w - 1 && mask[i + 1])
                    || (y > 0 && mask[i - w]) || (y < h - 1 && mask[i + w]))
                {
                    result[i] = true;
                }
            }
        }
        return result;
    }

    /// <summary>Dilate <paramref name="mask"/> by <paramref name="radius"/> but never onto barrier pixels.</summary>
    private static bool[] DilateInto(bool[] mask, bool[] barrier, int w, int h, int radius)
    {
        var result = mask;
        for (var step = 0; step < radius; step++)
        {
            var grown = Dilate1(result, w, h);
            for (var i = 0; i < grown.Length; i++)
            {
                if (barrier[i]) grown[i] = result[i];
            }
            result = grown;
        }
        return result;
    }

    // ---- simplify ----------------------------------------------------------------

    /// <summary>Ramer–Douglas–Peucker with a sub-pixel epsilon, keeping closed rings valid.</summary>
    internal static List<StrokePoint> Simplify(List<StrokePoint> points, double epsilon = 0.75)
    {
        if (points.Count <= 4) return points;
        var keep = new bool[points.Count];
        keep[0] = keep[^1] = true;
        SimplifySegment(points, 0, points.Count - 1, epsilon, keep);
        var result = new List<StrokePoint>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            if (keep[i]) result.Add(points[i]);
        }
        return result;
    }

    private static void SimplifySegment(List<StrokePoint> pts, int first, int last, double epsilon, bool[] keep)
    {
        if (last <= first + 1) return;
        double maxDist = -1;
        var index = -1;
        for (var i = first + 1; i < last; i++)
        {
            var d = Core.Geometry.GeometryOps.DistToSegment(pts[i], pts[first], pts[last]);
            if (d > maxDist)
            {
                maxDist = d;
                index = i;
            }
        }
        if (maxDist > epsilon && index > 0)
        {
            keep[index] = true;
            SimplifySegment(pts, first, index, epsilon, keep);
            SimplifySegment(pts, index, last, epsilon, keep);
        }
    }
}

/// <summary>
/// Moore-neighbour boundary tracing over a boolean mask: the region's outer
/// contour plus the contours of its holes (for even-odd filling).
/// </summary>
internal static class ContourTracer
{
    public static (List<StrokePoint> Outer, List<List<StrokePoint>> Holes) Trace(bool[] mask, int w, int h)
    {
        // Border classification via component labelling of the EMPTY space:
        // empty components not connected to the bitmap border are holes.
        var outer = TraceBoundary(mask, w, h, findOuter: true);

        var holes = new List<List<StrokePoint>>();
        var visited = new bool[w * h];
        // mark empty space connected to the border as "outside"
        var outside = new bool[w * h];
        var queue = new Queue<int>();
        for (var x = 0; x < w; x++)
        {
            Enqueue(x, 0);
            Enqueue(x, h - 1);
        }
        for (var y = 0; y < h; y++)
        {
            Enqueue(0, y);
            Enqueue(w - 1, y);
        }
        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            int x = i % w, y = i / w;
            if (x > 0) Enqueue(x - 1, y);
            if (x < w - 1) Enqueue(x + 1, y);
            if (y > 0) Enqueue(x, y - 1);
            if (y < h - 1) Enqueue(x, y + 1);
        }

        // every unvisited empty pixel belongs to a hole; trace each hole once
        var holeSeen = new bool[w * h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x;
                if (mask[i] || outside[i] || holeSeen[i]) continue;
                // flood this hole to mark it, and trace its boundary
                var holeMask = FloodHole(mask, outside, holeSeen, w, h, x, y);
                var contour = TraceBoundary(holeMask, w, h, findOuter: true);
                if (contour.Count >= 3) holes.Add(contour);
            }
        }
        return (outer, holes);

        void Enqueue(int x, int y)
        {
            var i = y * w + x;
            if (visited[i] || mask[i])
            {
                visited[i] = true;
                return;
            }
            if (outside[i]) return;
            outside[i] = true;
            visited[i] = true;
            queue.Enqueue(i);
        }
    }

    private static bool[] FloodHole(bool[] mask, bool[] outside, bool[] holeSeen, int w, int h, int sx, int sy)
    {
        var hole = new bool[w * h];
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((sx, sy));
        holeSeen[sy * w + sx] = true;
        hole[sy * w + sx] = true;
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
            {
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                var i = ny * w + nx;
                if (mask[i] || outside[i] || holeSeen[i]) continue;
                holeSeen[i] = true;
                hole[i] = true;
                queue.Enqueue((nx, ny));
            }
        }
        return hole;
    }

    /// <summary>Moore boundary trace of the first (topmost-leftmost) region in the mask.</summary>
    private static List<StrokePoint> TraceBoundary(bool[] mask, int w, int h, bool findOuter)
    {
        // find start pixel
        var start = -1;
        for (var i = 0; i < mask.Length; i++)
        {
            if (mask[i])
            {
                start = i;
                break;
            }
        }
        if (start < 0) return [];

        int sx = start % w, sy = start / w;
        // Moore neighbourhood, clockwise from W
        Span<(int dx, int dy)> dirs =
        [
            (-1, 0), (-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1),
        ];

        var contour = new List<StrokePoint>();
        int cx = sx, cy = sy;
        var backtrack = 0; // came from W
        var guard = 4 * w * h;
        do
        {
            contour.Add(new StrokePoint(cx + 0.5, cy + 0.5, 1));
            var found = false;
            for (var k = 0; k < 8; k++)
            {
                var dir = (backtrack + k) % 8;
                var nx = cx + dirs[dir].dx;
                var ny = cy + dirs[dir].dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h || !mask[ny * w + nx]) continue;
                // next backtrack: the direction pointing back toward the previous pixel, +1
                backtrack = (dir + 5) % 8;
                cx = nx;
                cy = ny;
                found = true;
                break;
            }
            if (!found) break; // isolated pixel
        }
        while ((cx != sx || cy != sy) && --guard > 0);

        return contour;
    }
}

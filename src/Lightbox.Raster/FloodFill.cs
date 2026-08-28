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

        var seed = sample.GetPixel(seedX, seedY);
        var tolerance = Math.Clamp(options.Tolerance, 0, 255);
        // Gap closing: openings up to GapPx read as closed, by thickening the
        // barriers the walk may not cross.
        var gap = (int)Math.Round(Math.Clamp(options.GapPx, 0, 64) / 2);

        // Which pixels the walk may not cross — asked on demand, so the page
        // the artist did not click on is never looked at. See Wall.
        bool[]? barrier = null;
        var wall = Wall.Lazy(sample, seed, tolerance, selection, w * h);
        if (!wall.IsLazy)
        {
            barrier = new bool[w * h];
            BuildBarrier(sample, seed, tolerance, barrier);
            if (selection is not null)
            {
                for (var i = 0; i < barrier.Length; i++)
                {
                    if (!selection[i]) barrier[i] = true;
                }
            }
            wall = Wall.Precomputed(gap > 0 ? Dilate(barrier, w, h, gap) : barrier);
        }

        var walk = ScanlineFill(ref wall, w, h, seedX, seedY, options.MaxRegionArea);
        if (walk is not { } run) return null;
        var (flooded, area, bounds) = run;
        if (area == 0) return null;

        // Gap closing, in the only place it can be done without sweeping the
        // page: inside the box the ungapped walk just landed in.
        //
        // Thickening barriers can only TAKE pixels away from a flood — the
        // non-barrier set shrinks, so the seed's component shrinks with it.
        // The gap-closed region is therefore a subset of the one above, and its
        // box is a subset of that box, which is what makes it safe to build the
        // thickened map over this window and nowhere else. Widened by gap+1 so
        // the barriers that stopped the first walk are inside it.
        //
        // Done this way round rather than by asking "is a barrier within gap of
        // this pixel" per pixel, which is what the first attempt did: that is a
        // diamond scan of 2·gap²+2·gap+1 reads for every pixel the walk touches,
        // so an artist who set the gap slider high enough turned a fill into a
        // hang. Windowed dilate passes are linear in the gap, as the page-wide
        // ones always were.
        if (gap > 0 && wall.IsLazy && options.MaxRegionArea <= 0)
        {
            var window = Widen(bounds, gap + 1, w, h);
            barrier = new bool[w * h];
            for (var y = window.MinY; y <= window.MaxY; y++)
            {
                var row = y * w;
                for (var x = window.MinX; x <= window.MaxX; x++)
                {
                    barrier[row + x] = wall.At(row + x);
                }
            }
            var thick = DilateWithin(barrier, w, h, gap, Widen(bounds, gap, w, h));
            var closed = Wall.Precomputed(thick);
            var second = ScanlineFill(ref closed, w, h, seedX, seedY, options.MaxRegionArea);
            if (second is not { } run2) return null;
            (flooded, area, bounds) = run2;
            if (area == 0) return null;
        }

        // …then win back the border zone the thickened barriers stole, without
        // ever crossing a real barrier. Both this and the grow below can push
        // the region outward, so the box grows with them.
        if (gap > 0)
        {
            var reach = Widen(bounds, gap, w, h);
            flooded = DilateInto(flooded, barrier!, w, h, gap, reach);
            bounds = reach;
        }

        // Over/underfill.
        var grow = (int)Math.Round(Math.Clamp(options.GrowPx, -32, 32));
        if (grow > 0)
        {
            var reach = Widen(bounds, grow, w, h);
            flooded = DilateWithin(flooded, w, h, grow, reach);
            bounds = reach;
        }
        else if (grow < 0)
        {
            flooded = ErodeWithin(flooded, w, h, -grow, bounds);
        }

        if (gap > 0 || grow != 0) area = CountWithin(flooded, w, bounds);
        if (area == 0) return null;

        var (outer, holes) = ContourTracer.Trace(flooded, w, h, bounds);
        if (outer.Count < 3) return null;
        return new Result(
            Simplify(outer),
            holes.Select(hole => Simplify(hole)).Where(c => c.Count >= 3).ToList(),
            area);
    }

    /// <summary>
    /// Which pixels the fill may not cross — asked one at a time, and
    /// remembered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A flood touches its own region and the ring around it; classifying
    /// the page was the rest of a fill's cost.</b> With the contour trace
    /// windowed, building the barrier map was 47 ms of a 50 ms fill, and it was
    /// the same 47 ms whether the artist clicked a 2,556-pixel corner or a
    /// 120,019-pixel field: 2.1 million pixels compared against the seed so
    /// that a few thousand could be walked.
    /// </para>
    /// <para>
    /// <b>The memo is what keeps it proportional to the region.</b> The
    /// scanline walk reads pixels repeatedly — the span scan looks past its own
    /// ends, and every span probes the two rows beside it — so without one, a
    /// pixel on a boundary would be re-compared several times. Three states,
    /// not two: unknown, open, wall.
    /// </para>
    /// <para>
    /// <b>Gap closing is not asked of this at all</b>, because "is a barrier
    /// within gap of here" cannot be answered cheaply one pixel at a time — see
    /// <see cref="Fill"/>, which walks once without it and then builds the
    /// thickened map inside the box that walk landed in. A bitmap whose bytes do
    /// not come back in a layout this can read falls back to the whole map, with
    /// one definition of "too different from the seed" behind both roads so they
    /// cannot drift.
    /// </para>
    /// </remarks>
    private ref struct Wall
    {
        private readonly bool[]? _map;
        private readonly ReadOnlySpan<uint> _pixels;
        private readonly bool[]? _selection;
        private readonly byte[]? _memo;
        private readonly int _sr, _sg, _sb, _sa;
        private readonly double _tolerance;
        private readonly bool _swapped;

        private Wall(bool[] map) => _map = map;

        private Wall(
            ReadOnlySpan<uint> pixels, bool swapped, SKColor seed, double tolerance,
            bool[]? selection, int count)
        {
            _pixels = pixels;
            _swapped = swapped;
            _sr = seed.Red;
            _sg = seed.Green;
            _sb = seed.Blue;
            _sa = seed.Alpha;
            _tolerance = tolerance;
            _selection = selection;
            _memo = new byte[count];
        }

        /// <summary>Whether this classifies on demand rather than reading a map.</summary>
        public readonly bool IsLazy => _memo is not null;

        public static Wall Precomputed(bool[] map) => new(map);

        /// <summary>
        /// On demand, when the bitmap's bytes can be read directly. Comes back
        /// not lazy — see <see cref="IsLazy"/> — when they cannot, and the
        /// caller builds the whole map instead.
        /// </summary>
        public static Wall Lazy(
            SKBitmap sample, SKColor seed, double tolerance, bool[]? selection, int count)
        {
            var info = sample.Info;
            if (info.BytesPerPixel != 4
                || info.ColorType is not (SKColorType.Rgba8888 or SKColorType.Bgra8888))
            {
                return default;
            }
            var bytes = sample.GetPixelSpan();
            if (bytes.Length < count * 4) return default;
            var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(bytes);
            return new Wall(
                words, info.ColorType == SKColorType.Bgra8888, seed, tolerance, selection, count);
        }

        public readonly bool At(int i)
        {
            if (_map is not null) return _map[i];
            var known = _memo![i];
            if (known != 0) return known == 2;
            var wall = Classify(i);
            _memo[i] = wall ? (byte)2 : (byte)1;
            return wall;
        }

        private readonly bool Classify(int i)
        {
            if (_selection is not null && !_selection[i]) return true;
            var px = _pixels[i];
            int a = (int)(px >> 24), g = (int)((px >> 8) & 0xFF);
            int first = (int)(px & 0xFF), third = (int)((px >> 16) & 0xFF);
            var r = _swapped ? third : first;
            var b = _swapped ? first : third;
            return Distance(r, g, b, a, _sr, _sg, _sb, _sa) > _tolerance;
        }
    }

    /// <summary>The whole map, for the callers that need every pixel classified.</summary>
    /// <remarks>
    /// Read from the bitmap's own bytes rather than through <c>SKBitmap.Pixels</c>,
    /// which materialises an <c>SKColor</c> for every pixel of the page into a
    /// managed array — 2.1 million of them at 1920×1080, 12–21 ms, thrown away
    /// one comparison later.
    /// </remarks>
    private static void BuildBarrier(SKBitmap sample, SKColor seed, double tolerance, bool[] barrier)
    {
        var info = sample.Info;
        var bytes = sample.GetPixelSpan();
        if (info.BytesPerPixel == 4
            && info.ColorType is SKColorType.Rgba8888 or SKColorType.Bgra8888
            && bytes.Length >= barrier.Length * 4)
        {
            var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(bytes);
            var swapped = info.ColorType == SKColorType.Bgra8888;
            int sr = seed.Red, sg = seed.Green, sb = seed.Blue, sa = seed.Alpha;
            for (var i = 0; i < barrier.Length; i++)
            {
                var px = words[i];
                int a = (int)(px >> 24), g = (int)((px >> 8) & 0xFF);
                int first = (int)(px & 0xFF), third = (int)((px >> 16) & 0xFF);
                var r = swapped ? third : first;
                var b = swapped ? first : third;
                barrier[i] = Distance(r, g, b, a, sr, sg, sb, sa) > tolerance;
            }
            return;
        }
        var pixels = sample.Pixels;
        for (var i = 0; i < barrier.Length; i++)
        {
            barrier[i] = ColorDistance(pixels[i], seed) > tolerance;
        }
    }

    /// <summary>
    /// <see cref="ColorDistance"/> on channels already unpacked — one
    /// definition of "too different from the seed", so the eager map and the
    /// on-demand one cannot answer differently.
    /// </summary>
    private static double Distance(int r, int g, int b, int a, int sr, int sg, int sb, int sa)
    {
        var da = Math.Abs(a - sa);
        var rgb = (Math.Abs(r - sr) + Math.Abs(g - sg) + Math.Abs(b - sb)) / 3.0;
        return Math.Max(da, rgb);
    }

    /// <summary>The box, grown by a margin and kept on the page.</summary>
    private static ContourTracer.Box Widen(ContourTracer.Box box, int by, int w, int h) =>
        new(Math.Max(0, box.MinX - by), Math.Max(0, box.MinY - by),
            Math.Min(w - 1, box.MaxX + by), Math.Min(h - 1, box.MaxY + by));

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

    /// <summary>
    /// The connected run of non-barrier pixels around the seed, with the area
    /// it covers and the box it sits in.
    /// </summary>
    /// <remarks>
    /// The area is counted here rather than by a pass over the result: the walk
    /// already knows, and <c>flooded.Count(x =&gt; x)</c> is a delegate call per
    /// pixel of the whole bitmap to learn it a second time. The box is what lets
    /// everything downstream stop looking at the rest of the page.
    /// </remarks>
    private static (bool[] Filled, int Area, ContourTracer.Box Bounds)? ScanlineFill(
        ref Wall barrier, int w, int h, int sx, int sy, int maxArea)
    {
        if (barrier.At(sy * w + sx)) return null;
        var filled = new bool[w * h];
        var stack = new Stack<(int X, int Y)>();
        stack.Push((sx, sy));
        var area = 0;
        int minX = sx, minY = sy, maxX = sx, maxY = sy;

        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            var i = y * w + x;
            if (filled[i] || barrier.At(i)) continue;

            // walk to the span's left edge
            var left = x;
            while (left > 0 && !filled[y * w + left - 1] && !barrier.At(y * w + left - 1)) left--;
            var right = x;
            while (right < w - 1 && !filled[y * w + right + 1] && !barrier.At(y * w + right + 1)) right++;

            for (var px = left; px <= right; px++)
            {
                filled[y * w + px] = true;
                area++;
            }
            if (left < minX) minX = left;
            if (right > maxX) maxX = right;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
            if (maxArea > 0 && area > maxArea)
            {
                return (filled, area, new ContourTracer.Box(minX, minY, maxX, maxY));
            }

            for (var py = y - 1; py <= y + 1; py += 2)
            {
                if (py < 0 || py >= h) continue;
                for (var px = left; px <= right; px++)
                {
                    var pi = py * w + px;
                    if (!filled[pi] && !barrier.At(pi)) stack.Push((px, py));
                }
            }
        }
        return (filled, area, new ContourTracer.Box(minX, minY, maxX, maxY));
    }

    /// <summary>
    /// Trace EVERY region in a mask: outer contours of all components plus
    /// their holes — one even-odd contour set (used for selections, which can
    /// be disjoint).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B341, and the shape of the cost is what makes it worth writing down.</b>
    /// This walk is proportional to the pixels it covers and there is no way
    /// round that — but the constant in front of it was enormous, and the
    /// caller that hurt was the one asking for the <em>complement</em> of a
    /// selection, which is nearly the whole page. Measured on 1920×1080:
    /// <b>240 ms to return ten points</b>, which is what a region transform
    /// paid on the first drag and again on the commit.
    /// </para>
    /// <para>
    /// Three things, none of them clever: the flood ran through a
    /// <c>Queue&lt;int&gt;</c> and a <c>yield return</c> neighbour enumerator,
    /// so every one of 1.9 million pixels allocated an iterator; the frontier
    /// is a set rather than a sequence, so a stack serves it and a stack can be
    /// one array; and the visited flags already say which component a pixel
    /// belongs to, so <see cref="ContourTracer"/> can be handed the component
    /// through the same <c>seen</c> array instead of a fresh page-sized
    /// <c>bool[]</c> per component. Same components, same order, same contours
    /// — <c>TracingIsUnchangedByHowItIsWalked</c> is the pin.
    /// </para>
    /// </remarks>
    public static List<List<StrokePoint>> TraceAllContours(bool[] mask, int w, int h)
    {
        var contours = new List<List<StrokePoint>>();
        var seen = new bool[w * h];
        // One buffer for every component: filled here, cleared by the walk that
        // fills the next one. A page-sized array per component was 2 MB of
        // garbage each on a 1920×1080 selection.
        var component = new bool[w * h];
        var stack = new int[w * h];
        var found = new List<int>();
        for (var i = 0; i < mask.Length; i++)
        {
            if (!mask[i] || seen[i]) continue;

            // Clear only what the previous component set — the whole array is
            // the page, and the components are usually far smaller than it.
            foreach (var p in found) component[p] = false;
            found.Clear();

            // A pixel is marked when it is PUSHED, never when it is popped, so
            // no index reaches the stack twice and `w * h` slots cannot
            // overflow however the region is shaped.
            var top = 0;
            stack[top++] = i;
            seen[i] = true;
            component[i] = true;
            found.Add(i);
            int minX = i % w, minY = i / w, maxX = minX, maxY = minY;
            // The four neighbours are written out rather than enumerated. A
            // `stackalloc` inside this loop is the trap: the frame is not
            // released until the method returns, so a page-sized region
            // overflows the stack outright — found by a crashed test process,
            // not by a slow one.
            while (top > 0)
            {
                var p = stack[--top];
                var x = p % w;
                if (x > 0) Take(p - 1);
                if (x < w - 1) Take(p + 1);
                if (p >= w) Take(p - w);
                if (p < mask.Length - w) Take(p + w);
            }

            void Take(int n)
            {
                if (!mask[n] || seen[n]) return;
                seen[n] = true;
                component[n] = true;
                found.Add(n);
                stack[top++] = n;
                int nx = n % w, ny = n / w;
                if (nx < minX) minX = nx;
                if (nx > maxX) maxX = nx;
                if (ny < minY) minY = ny;
                if (ny > maxY) maxY = ny;
            }

            // The component's own box, so the trace does not sweep the page
            // once per component.
            var (outer, holes) = ContourTracer.Trace(
                component, w, h, new ContourTracer.Box(minX, minY, maxX, maxY));
            if (outer.Count >= 3) contours.Add(Simplify(outer));
            contours.AddRange(holes.Select(hole => Simplify(hole)).Where(c => c.Count >= 3));
        }
        return contours;
    }

    // ---- morphology ------------------------------------------------------------

    /// <param name="outside">
    /// What lies beyond the bitmap. False for a dilate — a shape does not grow
    /// out of thin air at the edge — and true for the inverted pass inside
    /// <see cref="Erode"/>, where "outside the canvas" means "not selected".
    /// </param>
    public static bool[] Dilate(bool[] mask, int w, int h, int radius, bool outside = false)
    {
        var result = mask;
        for (var step = 0; step < radius; step++) result = Dilate1(result, w, h, outside);
        return result;
    }

    /// <summary>
    /// Shrink a mask by <paramref name="radius"/> pixels on every side.
    /// </summary>
    /// <remarks>
    /// Erosion is a dilation of the complement, and the complement includes
    /// everything off the edge of the bitmap. That is what the
    /// <c>outside: true</c> is for, and leaving it out was a real bug rather
    /// than a nicety: a selection touching the canvas border did not shrink on
    /// that side at all, and Select All followed by Shrink did nothing
    /// whatsoever — the complement was empty, so there was nothing to grow
    /// inward from.
    /// </remarks>
    public static bool[] Erode(bool[] mask, int w, int h, int radius)
    {
        var inverted = mask.Select(v => !v).ToArray();
        inverted = Dilate(inverted, w, h, radius, outside: true);
        return inverted.Select(v => !v).ToArray();
    }

    private static bool[] Dilate1(bool[] mask, int w, int h, bool outside)
    {
        var result = new bool[mask.Length];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x;
                if (mask[i]
                    || (x > 0 ? mask[i - 1] : outside) || (x < w - 1 ? mask[i + 1] : outside)
                    || (y > 0 ? mask[i - w] : outside) || (y < h - 1 ? mask[i + w] : outside))
                {
                    result[i] = true;
                }
            }
        }
        return result;
    }

    // ---- morphology, kept to the region --------------------------------------

    /// <summary>
    /// The same grow, shrink and grow-into-a-barrier as the passes above, done
    /// only inside a box.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every one of these used to sweep the page, and with the shipped
    /// defaults a fill runs three of them.</b> <c>FillGapPx</c> is 4 and
    /// <c>FillGrowPx</c> is 2, so the ordinary click pays two rounds of
    /// gap-closing dilate, two of dilate-into and two of overfill — measured on
    /// 1920×1080 at 29, 38 and 29 ms around a flood that took <b>6</b>.
    /// </para>
    /// <para>
    /// A region cannot grow further than its own box plus the radius, and
    /// outside that box the mask is empty, so a pass that writes only inside
    /// the widened box writes exactly what the page-wide pass wrote. The result
    /// is still a page-sized array — everything downstream indexes by
    /// <c>y * w + x</c>, and a zeroed 2 MB allocation is a tenth of a
    /// millisecond, which is not where the time was.
    /// </para>
    /// </remarks>
    private static bool[] DilateWithin(bool[] mask, int w, int h, int radius, ContourTracer.Box box)
    {
        var result = mask;
        for (var step = 0; step < radius; step++) result = Dilate1Within(result, w, h, box);
        return result;
    }

    private static bool[] Dilate1Within(bool[] mask, int w, int h, ContourTracer.Box box)
    {
        var result = new bool[mask.Length];
        for (var y = box.MinY; y <= box.MaxY; y++)
        {
            var row = y * w;
            for (var x = box.MinX; x <= box.MaxX; x++)
            {
                var i = row + x;
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

    /// <summary>
    /// Shrink by <paramref name="radius"/>, off the edge of the bitmap as well
    /// as off the region — <see cref="Erode"/>'s <c>outside: true</c>, written
    /// directly rather than as a dilation of a complement that covers the page.
    /// </summary>
    private static bool[] ErodeWithin(bool[] mask, int w, int h, int radius, ContourTracer.Box box)
    {
        var result = mask;
        for (var step = 0; step < radius; step++)
        {
            var next = new bool[mask.Length];
            for (var y = box.MinY; y <= box.MaxY; y++)
            {
                var row = y * w;
                for (var x = box.MinX; x <= box.MaxX; x++)
                {
                    var i = row + x;
                    if (!result[i]) continue;
                    if (x == 0 || x == w - 1 || y == 0 || y == h - 1) continue;
                    if (result[i - 1] && result[i + 1] && result[i - w] && result[i + w])
                    {
                        next[i] = true;
                    }
                }
            }
            result = next;
        }
        return result;
    }

    /// <summary>
    /// Grow back into the zone gap closing took, never onto a real barrier —
    /// <see cref="DilateInto"/>, kept to a box.
    /// </summary>
    private static bool[] DilateInto(
        bool[] mask, bool[] barrier, int w, int h, int radius, ContourTracer.Box box)
    {
        var result = mask;
        for (var step = 0; step < radius; step++)
        {
            var grown = Dilate1Within(result, w, h, box);
            for (var y = box.MinY; y <= box.MaxY; y++)
            {
                var row = y * w;
                for (var x = box.MinX; x <= box.MaxX; x++)
                {
                    var i = row + x;
                    if (barrier[i]) grown[i] = result[i];
                }
            }
            result = grown;
        }
        return result;
    }

    /// <summary>How many pixels are set inside the box.</summary>
    /// <remarks>
    /// The whole region is inside it, so this is the same number
    /// <c>mask.Count(x =&gt; x)</c> gave — for a delegate call per pixel of the
    /// page instead of per pixel of the box.
    /// </remarks>
    private static int CountWithin(bool[] mask, int w, ContourTracer.Box box)
    {
        var area = 0;
        for (var y = box.MinY; y <= box.MaxY; y++)
        {
            var row = y * w;
            for (var x = box.MinX; x <= box.MaxX; x++)
            {
                if (mask[row + x]) area++;
            }
        }
        return area;
    }

    /// <summary>Dilate <paramref name="mask"/> by <paramref name="radius"/> but never onto barrier pixels.</summary>
    private static bool[] DilateInto(bool[] mask, bool[] barrier, int w, int h, int radius)
    {
        var result = mask;
        for (var step = 0; step < radius; step++)
        {
            var grown = Dilate1(result, w, h, outside: false);
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
    public static (List<StrokePoint> Outer, List<List<StrokePoint>> Holes) Trace(bool[] mask, int w, int h) =>
        Trace(mask, w, h, new Box(0, 0, w - 1, h - 1));

    /// <summary>The smallest rectangle holding every set pixel, inclusive.</summary>
    internal readonly record struct Box(int MinX, int MinY, int MaxX, int MaxY);

    /// <summary>
    /// The region's outer contour and its holes, doing no work outside
    /// <paramref name="bounds"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The window is what makes a fill cost the region rather than the
    /// page.</b> Every step here used to sweep the whole bitmap: the search for
    /// a start pixel, the flood that marks empty space "outside", and the scan
    /// looking for hole seeds. On a 1920×1080 canvas that is 2.1 million pixels
    /// walked three times — so flooding a 2,556-pixel corner cost 170 ms, two
    /// thirds of what a 120,019-pixel one cost. A fill that takes the same time
    /// whatever you click on is not measuring the thing it is filling.
    /// </para>
    /// <para>
    /// <b>Why a one-pixel inflation is enough, and why it is necessary.</b>
    /// Holes are the empty pixels that <em>cannot</em> reach the outside, so
    /// the flood has to start somewhere known to be outside. No set pixel lies
    /// beyond <paramref name="bounds"/>, so the ring one pixel out is empty; the
    /// whole exterior of a rectangle is connected, so that ring reaches the
    /// bitmap border, and any path from an empty pixel inside the window to the
    /// border must cross the ring. Seeding the ring therefore marks exactly the
    /// pixels the page-wide flood marked. Clamped where the region touches the
    /// bitmap edge — there the window border <em>is</em> the bitmap border, and
    /// the two agree by construction.
    /// </para>
    /// </remarks>
    internal static (List<StrokePoint> Outer, List<List<StrokePoint>> Holes) Trace(
        bool[] mask, int w, int h, Box bounds)
    {
        var x0 = Math.Max(0, bounds.MinX - 1);
        var y0 = Math.Max(0, bounds.MinY - 1);
        var x1 = Math.Min(w - 1, bounds.MaxX + 1);
        var y1 = Math.Min(h - 1, bounds.MaxY + 1);

        // Border classification via component labelling of the EMPTY space:
        // empty components not connected to the window border are holes.
        var outer = FirstSetPixel(mask, w, x0, y0, x1, y1) is { } start
            ? TraceBoundary(mask, w, h, start)
            : [];

        var holes = new List<List<StrokePoint>>();
        // mark empty space connected to the window border as "outside"
        var outside = new bool[w * h];
        var stack = new Stack<int>();
        for (var x = x0; x <= x1; x++)
        {
            Seed((y0 * w) + x);
            Seed((y1 * w) + x);
        }
        for (var y = y0; y <= y1; y++)
        {
            Seed((y * w) + x0);
            Seed((y * w) + x1);
        }
        while (stack.Count > 0)
        {
            var i = stack.Pop();
            var x = i % w;
            if (x > x0) Seed(i - 1);
            if (x < x1) Seed(i + 1);
            if (i >= (y0 + 1) * w) Seed(i - w);
            if (i < y1 * w) Seed(i + w);
        }

        // every unmarked empty pixel belongs to a hole; trace each hole once
        var holeSeen = new bool[w * h];
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var i = (y * w) + x;
                if (mask[i] || outside[i] || holeSeen[i]) continue;
                // flood this hole to mark it, and trace its boundary
                var holeMask = FloodHole(mask, outside, holeSeen, w, x0, y0, x1, y1, x, y);
                // The scan reached this pixel first, so it is the hole's own
                // topmost-leftmost — the start a page-wide search would find.
                var contour = TraceBoundary(holeMask, w, h, i);
                if (contour.Count >= 3) holes.Add(contour);
            }
        }
        return (outer, holes);

        void Seed(int i)
        {
            if (outside[i] || mask[i]) return;
            outside[i] = true;
            stack.Push(i);
        }
    }

    /// <summary>Row-major first set pixel inside the window, or null.</summary>
    private static int? FirstSetPixel(bool[] mask, int w, int x0, int y0, int x1, int y1)
    {
        for (var y = y0; y <= y1; y++)
        {
            var row = y * w;
            for (var x = x0; x <= x1; x++)
            {
                if (mask[row + x]) return row + x;
            }
        }
        return null;
    }

    /// <remarks>
    /// <b>B341.</b> Indices rather than <c>(x, y)</c> pairs, and a stack rather
    /// than a queue: the old walk allocated a four-element tuple array for the
    /// neighbours of <em>every</em> pixel it visited, which on the hole a
    /// full-page selection leaves is a quarter of a million allocations to
    /// decide the same set. Marked on push, so no index is queued twice.
    /// </remarks>
    private static bool[] FloodHole(
        bool[] mask, bool[] outside, bool[] holeSeen, int w,
        int x0, int y0, int x1, int y1, int sx, int sy)
    {
        var hole = new bool[mask.Length];
        var stack = new Stack<int>();
        var start = (sy * w) + sx;
        stack.Push(start);
        holeSeen[start] = true;
        hole[start] = true;
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            var x = p % w;
            if (x > x0) Take(p - 1);
            if (x < x1) Take(p + 1);
            if (p >= (y0 + 1) * w) Take(p - w);
            if (p < y1 * w) Take(p + w);
        }
        return hole;

        void Take(int i)
        {
            if (mask[i] || outside[i] || holeSeen[i]) return;
            holeSeen[i] = true;
            hole[i] = true;
            stack.Push(i);
        }
    }

    /// <summary>
    /// Moore boundary trace of the region containing <paramref name="start"/>,
    /// which must be its topmost-leftmost pixel.
    /// </summary>
    /// <remarks>
    /// The start is given rather than searched for. It used to be found by
    /// scanning the mask from index zero, which is a whole-bitmap sweep to
    /// locate a pixel both callers already had in their hands — and the sweep
    /// is longest exactly when the region is near the bottom of the page.
    /// </remarks>
    private static List<StrokePoint> TraceBoundary(bool[] mask, int w, int h, int start)
    {
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

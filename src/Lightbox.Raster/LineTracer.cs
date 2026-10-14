using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// Flattened line art back into strokes (Q269): each line becomes one brush
/// stroke down its middle, with pressure read from how wide the line is there.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> Before merging kept strokes (Q265), a merge with an
/// eraser, a blend or a mask in it flattened the drawing to pixels — and pixels
/// are something neither the inbetweener nor any stroke tool can work with. The
/// documents that went through that are already pixels, and no older copy of
/// their strokes exists. This is the way back that is left.
/// </para>
/// <para>
/// <b>Lossy, and the losses are named.</b> The exact edge of a line comes back
/// as a round brush's edge; the order the lines were drawn in, and the pressure
/// the pen really had, are not in the pixels — pressure here is the line's width
/// divided by the widest point of the same line. Where lines cross, the tracer
/// guesses which way each one went by carrying on the straightest, which is
/// right for a crossing and a T and can be wrong in a tangle.
/// </para>
/// <para>
/// <b>How.</b> Ink is the pixels at a quarter opacity or more. A distance
/// transform gives every ink pixel its distance to the paper, which is half the
/// line's width at the line's middle; thinning (Zhang–Suen) leaves the middle as
/// a one-pixel skeleton. The skeleton is walked into paths between its ends and
/// its junctions, short spurs that thinning leaves at the corners of thick lines
/// are dropped, and at each junction the two paths that continue each other most
/// nearly straight are joined, so a crossing is two lines rather than four. Each
/// path is smoothed, simplified, and becomes a stroke.
/// </para>
/// <para>
/// Deterministic, like everything that writes the record: the same pixels trace
/// to the same strokes, in the same order — top to bottom, left to right by where
/// each line starts.
/// </para>
/// </remarks>
public static class LineTracer
{
    /// <summary>
    /// Trace a picture's line art into strokes, in the picture's pixel
    /// coordinates — which are stroke coordinates for a drawing's baseline.
    /// </summary>
    public static List<Stroke> Trace(
        SKBitmap picture, LineTraceOptions? options = null, CancellationToken cancel = default)
    {
        options ??= new LineTraceOptions();
        cancel.ThrowIfCancellationRequested();
        var (ink, pixels, w, h, count) = Read(picture, options);
        if (count == 0 || TooMuch(count, picture)) return [];

        var distance = DistanceToPaper(ink, w, h);
        var skeleton = Thin(ink, w, h, cancel);
        KeepDots(ink, skeleton, distance, w, h);
        var paths = Paths(skeleton, w, h, cancel);
        paths = Pruned(paths, distance, w);
        paths = Joined(paths);

        var strokes = new List<Stroke>();
        foreach (var path in paths.OrderBy(p => p.Points.Min(q => q.Y)).ThenBy(p => p.Points.Min(q => q.X)))
        {
            // Detail: a line shorter than this is a tick of noise, not a mark.
            // A dot as wide as a line is kept whatever (Pruned drops specks).
            if (path.Points.Count > 1 && Length(path) < options.MinimumLength) continue;
            if (StrokeOf(path, distance, pixels, w, ReadsDarkness(picture, options)) is { } stroke) strokes.Add(stroke);
        }
        return strokes;
    }

    /// <summary>
    /// Why this picture will not trace with these settings, in words — or null
    /// when it will. A picture that is mostly ink has no lines in it to find,
    /// and thinning it costs a pass of the whole page per pixel of its depth.
    /// </summary>
    public static string? WhyNot(SKBitmap picture, LineTraceOptions? options = null)
    {
        options ??= new LineTraceOptions();
        var (_, _, _, _, count) = Read(picture, options);
        if (count == 0) return "Nothing here counts as ink at this setting — lower Faintest ink.";
        if (TooMuch(count, picture))
        {
            return "Most of this picture counts as ink, so there are no lines in it to find — "
                + "raise Faintest ink, or read the ink from darkness.";
        }
        return null;
    }

    /// <summary>More than half the page: a fill, a photograph read the wrong way, not line art.</summary>
    private static bool TooMuch(int ink, SKBitmap picture) => ink * 2L > (long)picture.Width * picture.Height;

    private static bool ReadsDarkness(SKBitmap picture, LineTraceOptions options) => options.Source switch
    {
        InkSource.Darkness => true,
        InkSource.Transparency => false,
        _ => IsOpaque(picture.Pixels),
    };

    /// <summary>
    /// The ink, and the colours, on a page one pixel bigger all round. The
    /// border is paper: thinning and the walk never have to treat the edge as
    /// special, and a line running off the page is thinned down its middle
    /// like any other instead of staying where it touches the edge.
    /// </summary>
    private static (bool[] Ink, SKColor[] Pixels, int W, int H, int Count) Read(SKBitmap picture, LineTraceOptions options)
    {
        var source = picture.Pixels;   // unpremultiplied colours
        var byDarkness = options.Source switch
        {
            InkSource.Darkness => true,
            InkSource.Transparency => false,
            _ => IsOpaque(source),
        };
        // How much ink a pixel holds, 0..1: its opacity on a drawn layer, how
        // dark it is (over white, which is what a scan is on) in a picture.
        var level = (int)Math.Round(Math.Clamp(options.Threshold, 0.01, 0.99) * 255);
        var w = picture.Width + 2;
        var h = picture.Height + 2;
        var ink = new bool[w * h];
        var pixels = new SKColor[w * h];
        var count = 0;
        for (var y = 0; y < picture.Height; y++)
        for (var x = 0; x < picture.Width; x++)
        {
            var c = source[y * picture.Width + x];
            var i = (y + 1) * w + x + 1;
            pixels[i] = c;
            if (InkOf(c, byDarkness) < level) continue;
            ink[i] = true;
            count++;
        }
        return (ink, pixels, w, h, count);
    }

    /// <summary>A picture with no transparency to speak of: a scan, a photo, a flattened export.</summary>
    private static bool IsOpaque(SKColor[] pixels)
    {
        var step = Math.Max(1, pixels.Length / 4096);
        var transparent = 0;
        var sampled = 0;
        for (var i = 0; i < pixels.Length; i += step)
        {
            sampled++;
            if (pixels[i].Alpha < 250) transparent++;
        }
        return transparent * 100 < sampled;   // under one in a hundred
    }

    /// <summary>Ink in a pixel, 0..255: opacity, or darkness laid over white.</summary>
    private static int InkOf(SKColor c, bool byDarkness)
    {
        if (!byDarkness) return c.Alpha;
        // Over white, then how far from white: Rec. 709 luminance.
        var a = c.Alpha / 255.0;
        var r = c.Red * a + 255 * (1 - a);
        var g = c.Green * a + 255 * (1 - a);
        var b = c.Blue * a + 255 * (1 - a);
        return 255 - (int)Math.Round(0.2126 * r + 0.7152 * g + 0.0722 * b);
    }

    // ---- distance -------------------------------------------------------------

    /// <summary>
    /// Euclidean distance from each ink pixel to the nearest paper pixel —
    /// exact, by Felzenszwalb and Huttenlocher's two passes of lower envelopes.
    /// Paper is zero; the page edge counts as paper.
    /// </summary>
    private static float[] DistanceToPaper(bool[] ink, int w, int h)
    {
        const float Far = 1e20f;
        var squared = new float[w * h];
        for (var i = 0; i < squared.Length; i++) squared[i] = ink[i] ? Far : 0;

        var line = new float[Math.Max(w, h) + 2];
        var output = new float[line.Length];
        // Columns, then rows; a padded paper pixel each side is the page edge.
        for (var x = 0; x < w; x++)
        {
            line[0] = 0;
            for (var y = 0; y < h; y++) line[y + 1] = squared[y * w + x];
            line[h + 1] = 0;
            Envelope(line, h + 2, output);
            for (var y = 0; y < h; y++) squared[y * w + x] = output[y + 1];
        }
        for (var y = 0; y < h; y++)
        {
            line[0] = 0;
            for (var x = 0; x < w; x++) line[x + 1] = squared[y * w + x];
            line[w + 1] = 0;
            Envelope(line, w + 2, output);
            for (var x = 0; x < w; x++) squared[y * w + x] = output[x + 1];
        }
        for (var i = 0; i < squared.Length; i++) squared[i] = MathF.Sqrt(squared[i]);
        return squared;
    }

    /// <summary>One dimension of the squared distance transform: the lower envelope of parabolas.</summary>
    private static void Envelope(float[] f, int n, float[] d)
    {
        var v = new int[n];
        var z = new float[n + 1];
        var k = 0;
        v[0] = 0;
        z[0] = float.NegativeInfinity;
        z[1] = float.PositiveInfinity;
        for (var q = 1; q < n; q++)
        {
            float s;
            while (true)
            {
                s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]);
                if (s > z[k] || k == 0) break;
                k--;
            }
            if (s <= z[k] && k == 0)
            {
                v[0] = q;
                z[0] = float.NegativeInfinity;
                z[1] = float.PositiveInfinity;
                continue;
            }
            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = float.PositiveInfinity;
        }
        k = 0;
        for (var q = 0; q < n; q++)
        {
            while (z[k + 1] < q) k++;
            d[q] = (q - v[k]) * (q - v[k]) + f[v[k]];
        }
    }

    // ---- thinning -------------------------------------------------------------

    /// <summary>Zhang–Suen thinning to a one-pixel, 8-connected skeleton.</summary>
    /// <remarks>
    /// Only the pixels still set are looked at on each pass, not the page: a
    /// pass costs the ink that is left, so a drawing that is mostly paper
    /// thins in a few passes over a few thousand pixels. The page is padded
    /// (<see cref="Read"/>), so no set pixel is on its edge.
    /// </remarks>
    private static bool[] Thin(bool[] ink, int w, int h, CancellationToken cancel)
    {
        var s = (bool[])ink.Clone();
        var live = new List<int>();
        for (var i = 0; i < s.Length; i++) if (s[i]) live.Add(i);
        var doomed = new List<int>();
        bool changed;
        do
        {
            cancel.ThrowIfCancellationRequested();
            changed = false;
            for (var pass = 0; pass < 2; pass++)
            {
                doomed.Clear();
                foreach (var i in live)
                {
                    if (!s[i]) continue;
                    // p2..p9, clockwise from north.
                    bool p2 = s[i - w], p3 = s[i - w + 1], p4 = s[i + 1], p5 = s[i + w + 1];
                    bool p6 = s[i + w], p7 = s[i + w - 1], p8 = s[i - 1], p9 = s[i - w - 1];
                    var b = (p2 ? 1 : 0) + (p3 ? 1 : 0) + (p4 ? 1 : 0) + (p5 ? 1 : 0)
                          + (p6 ? 1 : 0) + (p7 ? 1 : 0) + (p8 ? 1 : 0) + (p9 ? 1 : 0);
                    if (b < 2 || b > 6) continue;
                    var a = (!p2 && p3 ? 1 : 0) + (!p3 && p4 ? 1 : 0) + (!p4 && p5 ? 1 : 0) + (!p5 && p6 ? 1 : 0)
                          + (!p6 && p7 ? 1 : 0) + (!p7 && p8 ? 1 : 0) + (!p8 && p9 ? 1 : 0) + (!p9 && p2 ? 1 : 0);
                    if (a != 1) continue;
                    if (pass == 0 ? (p2 && p4 && p6) || (p4 && p6 && p8) : (p2 && p4 && p8) || (p2 && p6 && p8)) continue;
                    doomed.Add(i);
                }
                foreach (var i in doomed) s[i] = false;
                changed |= doomed.Count > 0;
            }
            if (changed) live.RemoveAll(i => !s[i]);
        } while (changed);
        return s;
    }

    /// <summary>
    /// A blob that thinning erased entirely — a dot, small enough that every
    /// pixel of it was edge — keeps its deepest pixel, so a dot an artist drew
    /// comes back as a dot.
    /// </summary>
    private static void KeepDots(bool[] ink, bool[] skeleton, float[] distance, int w, int h)
    {
        var seen = new bool[ink.Length];
        var stack = new Stack<int>();
        for (var i = 0; i < ink.Length; i++)
        {
            if (!ink[i] || seen[i]) continue;
            var deepest = i;
            var hasSkeleton = false;
            seen[i] = true;
            stack.Push(i);
            while (stack.Count > 0)
            {
                var j = stack.Pop();
                hasSkeleton |= skeleton[j];
                if (distance[j] > distance[deepest]) deepest = j;
                var x = j % w;
                var y = j / w;
                for (var k = 0; k < 8; k++)
                {
                    var nx = x + Dx[k];
                    var ny = y + Dy[k];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    var n = ny * w + nx;
                    if (!ink[n] || seen[n]) continue;
                    seen[n] = true;
                    stack.Push(n);
                }
            }
            if (!hasSkeleton) skeleton[deepest] = true;
        }
    }

    private static readonly int[] Dx = [0, 1, 1, 1, 0, -1, -1, -1];
    private static readonly int[] Dy = [-1, -1, 0, 1, 1, 1, 0, -1];

    private static int Count(bool[] s, int i, int w)
    {
        var x = i % w;
        var y = i / w;
        var n = 0;
        for (var k = 0; k < 8; k++)
        {
            var j = (y + Dy[k]) * w + x + Dx[k];
            if (j >= 0 && j < s.Length && s[j]) n++;
        }
        return n;
    }

    // ---- the skeleton as paths ------------------------------------------------

    /// <summary>A run of skeleton pixels, and which junction (if any) each end meets.</summary>
    private sealed class Path
    {
        public List<SKPointI> Points { get; } = [];
        public int StartNode { get; set; } = -1;
        public int EndNode { get; set; } = -1;
        public bool Closed { get; set; }
    }

    private static List<Path> Paths(bool[] s, int w, int h, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        // Branches, not neighbours: the number of separate runs of skeleton in
        // the ring of eight. A staircase pixel on a curve has three neighbours
        // and two branches; counted by neighbours it was a junction, and a
        // circle came back in twenty pieces.
        var degree = new int[s.Length];
        for (var i = 0; i < s.Length; i++) if (s[i]) degree[i] = Branches(s, i, w, h);

        // Junction pixels that touch each other are one junction.
        var node = new int[s.Length];
        Array.Fill(node, -1);
        var nodes = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (!s[i] || degree[i] < 3 || node[i] >= 0) continue;
            var stack = new Stack<int>();
            stack.Push(i);
            node[i] = nodes;
            while (stack.Count > 0)
            {
                var j = stack.Pop();
                foreach (var n in Neighbours(s, j, w, h))
                {
                    if (degree[n] < 3 || node[n] >= 0) continue;
                    node[n] = nodes;
                    stack.Push(n);
                }
            }
            nodes++;
        }

        var visited = new bool[s.Length];
        var paths = new List<Path>();

        // From every end first, then from every junction, walk each way out
        // not yet walked. Ends first, because a walk out of a junction can
        // circle its own cluster and take the pixels a line arriving at that
        // junction needed to get there.
        foreach (var fromJunctions in (bool[])[false, true])
        for (var i = 0; i < s.Length; i++)
        {
            if (!s[i]) continue;
            var isEnd = degree[i] <= 1;
            if (fromJunctions ? node[i] < 0 : !isEnd) continue;
            if (degree[i] == 0)
            {
                if (visited[i]) continue;
                visited[i] = true;
                var dot = new Path();
                dot.Points.Add(new SKPointI(i % w, i / w));
                paths.Add(dot);
                continue;
            }
            foreach (var first in Neighbours(s, i, w, h))
            {
                if (node[i] >= 0 && node[first] == node[i]) continue;   // inside the junction
                if (visited[first] && node[first] < 0) continue;
                var path = Walk(s, w, h, i, first, node, visited, degree);
                // Round its own junction and back: the cluster's own pixels,
                // not a line.
                if (path is null || (path.StartNode >= 0 && path.StartNode == path.EndNode && path.Points.Count < 8)) continue;
                paths.Add(path);
            }
            if (isEnd) visited[i] = true;
        }

        // What is left is loops with no end and no junction.
        for (var i = 0; i < s.Length; i++)
        {
            if (!s[i] || visited[i]) continue;
            var loop = new Path { Closed = true };
            var at = i;
            var previous = -1;
            while (true)
            {
                visited[at] = true;
                loop.Points.Add(new SKPointI(at % w, at / w));
                var next = Neighbours(s, at, w, h).FirstOrDefault(n => !visited[n] && n != previous, -1);
                if (next < 0) break;
                previous = at;
                at = next;
            }
            // A few pixels left over beside a junction are not a loop.
            if (loop.Points.Count < 6) continue;
            loop.Points.Add(loop.Points[0]);
            paths.Add(loop);
        }
        return paths;
    }

    private static Path? Walk(bool[] s, int w, int h, int from, int first, int[] node, bool[] visited, int[] degree)
    {
        var path = new Path { StartNode = node[from] };
        path.Points.Add(new SKPointI(from % w, from / w));
        var previous = from;
        var at = first;
        while (true)
        {
            path.Points.Add(new SKPointI(at % w, at / w));
            if (node[at] >= 0)
            {
                // Reached a junction. Walked from the other side already?
                path.EndNode = node[at];
                if (node[from] >= 0 && path.Points.Count == 2) return null;   // two junction pixels side by side
                break;
            }
            if (visited[at]) return null;
            visited[at] = true;
            if (degree[at] <= 1) break;   // an end
            // The first way on, 4-neighbours before diagonals: a step round a
            // staircase corner goes through the corner pixel rather than past
            // it, so no pixel of the line is left for a later walk to find as
            // a fragment. A junction within reach is taken over anything else.
            var next = -1;
            foreach (var n in Neighbours(s, at, w, h))
            {
                if (n == previous || (visited[n] && node[n] < 0)) continue;
                if (node[from] >= 0 && node[n] == node[from] && path.Points.Count < 3) continue;
                if (node[n] >= 0)
                {
                    next = n;
                    break;
                }
                if (next < 0) next = n;
            }
            if (next < 0) break;
            previous = at;
            at = next;
        }
        // A path that ended where it started at an end pixel was walked from
        // its other end already.
        return path;
    }

    /// <summary>Separate runs of set pixels around the ring of eight: 0 alone, 1 an end, 2 a path, 3+ a junction.</summary>
    private static int Branches(bool[] s, int i, int w, int h)
    {
        var x = i % w;
        var y = i / w;
        var ring = new bool[8];
        var count = 0;
        for (var k = 0; k < 8; k++)
        {
            var nx = x + Dx[k];
            var ny = y + Dy[k];
            ring[k] = nx >= 0 && ny >= 0 && nx < w && ny < h && s[ny * w + nx];
            if (ring[k]) count++;
        }
        if (count == 0) return 0;
        if (count == 8) return 1;
        var runs = 0;
        for (var k = 0; k < 8; k++)
        {
            // A run starts where the ring turns on — and a diagonal is joined to
            // its run through either neighbouring 4-neighbour.
            if (ring[k] && !ring[(k + 7) % 8]) runs++;
        }
        return runs;
    }

    private static IEnumerable<int> Neighbours(bool[] s, int i, int w, int h)
    {
        var x = i % w;
        var y = i / w;
        // 4-neighbours first, so a walk takes the straight step before the diagonal.
        foreach (var k in (int[])[0, 2, 4, 6, 1, 3, 5, 7])
        {
            var nx = x + Dx[k];
            var ny = y + Dy[k];
            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
            var j = ny * w + nx;
            if (s[j]) yield return j;
        }
    }

    // ---- tidying --------------------------------------------------------------

    /// <summary>
    /// Drop the short branches thinning grows at the corners and ends of a thick
    /// line: a path from an end to a junction no longer than the line is wide there.
    /// </summary>
    private static List<Path> Pruned(List<Path> paths, float[] distance, int w)
    {
        var kept = new List<Path>();
        foreach (var path in paths)
        {
            var dangling = (path.StartNode < 0) != (path.EndNode < 0);
            if (dangling)
            {
                var junction = path.StartNode >= 0 ? path.Points[0] : path.Points[^1];
                var wide = distance[junction.Y * w + junction.X];
                if (Length(path) <= wide * 1.5f + 1) continue;
            }
            // A speck: a lone pixel or two of noise.
            if (path.StartNode < 0 && path.EndNode < 0 && !path.Closed && path.Points.Count <= 2)
            {
                var p = path.Points[0];
                if (distance[p.Y * w + p.X] < 2) continue;
            }
            kept.Add(path);
        }
        return kept;
    }

    /// <summary>
    /// At each junction, join the two paths that carry on each other most
    /// nearly straight, and again for what is left — so a crossing is two lines
    /// and a T is its bar and its stem.
    /// </summary>
    /// <remarks>
    /// Decided once per junction, then the paths are strung together along the
    /// pairs in a single walk. The first version joined one pair, rebuilt
    /// everything and started again: a 1080×1080 grid took 15.6 s and 7.8 GB
    /// (the adversary).
    /// </remarks>
    private static List<Path> Joined(List<Path> paths)
    {
        var open = paths.Where(p => !p.Closed).ToList();
        var result = paths.Where(p => p.Closed).ToList();

        // Ends by junction, in a stable order: path order, start before end.
        var byNode = new SortedDictionary<int, List<(int Path, bool AtStart)>>();
        for (var k = 0; k < open.Count; k++)
        {
            if (open[k].StartNode >= 0) Add(byNode, open[k].StartNode, (k, true));
            if (open[k].EndNode >= 0) Add(byNode, open[k].EndNode, (k, false));
        }

        // Which end continues into which.
        var partner = new Dictionary<(int, bool), (int Path, bool AtStart)>();
        foreach (var ends in byNode.Values)
        {
            if (ends.Count < 2) continue;
            var pairs = new List<(double Turn, int A, int B)>();
            for (var a = 0; a < ends.Count; a++)
            for (var b = a + 1; b < ends.Count; b++)
            {
                // A path's own two ends at one junction close it into a loop —
                // only where nothing else meets there.
                if (ends[a].Path == ends[b].Path && ends.Count > 2) continue;
                var da = Leaving(open[ends[a].Path], ends[a].AtStart);
                var db = Leaving(open[ends[b].Path], ends[b].AtStart);
                // Leaving in opposite directions is going straight through.
                var turn = Math.Acos(Math.Clamp(-(da.X * db.X + da.Y * db.Y), -1, 1));
                // Two ends are one line through a junction of two; through a
                // busier one only when they carry on within 50° of straight.
                if (ends.Count > 2 && turn > Math.PI * 50 / 180) continue;
                pairs.Add((turn, a, b));
            }
            var taken = new bool[ends.Count];
            foreach (var (_, a, b) in pairs.OrderBy(p => p.Turn).ThenBy(p => p.A).ThenBy(p => p.B))
            {
                if (taken[a] || taken[b]) continue;
                taken[a] = taken[b] = true;
                partner[ends[a]] = ends[b];
                partner[ends[b]] = ends[a];
            }
        }

        // String the paths together: from every path end with no partner,
        // then round whatever is left, which is loops through junctions.
        var used = new bool[open.Count];
        void Chain(int first, bool forward)
        {
            var joined = new Path();
            var path = first;
            var reversed = !forward;
            while (true)
            {
                used[path] = true;
                var points = open[path].Points;
                var ordered = reversed ? Enumerable.Reverse(points) : points;
                joined.Points.AddRange(joined.Points.Count == 0 ? ordered : ordered.Skip(1));
                var leaving = (path, reversed);   // the far end: the start when reversed
                if (!partner.TryGetValue(leaving, out var next)) break;
                if (used[next.Path])
                {
                    joined.Closed = next.Path == first;
                    break;
                }
                path = next.Path;
                reversed = !next.AtStart;
            }
            result.Add(joined);
        }
        for (var k = 0; k < open.Count; k++)
        {
            if (used[k]) continue;
            if (!partner.ContainsKey((k, true))) Chain(k, forward: true);
            else if (!partner.ContainsKey((k, false))) Chain(k, forward: false);
        }
        for (var k = 0; k < open.Count; k++)
        {
            if (!used[k]) Chain(k, forward: true);
        }
        return result;

        static void Add(SortedDictionary<int, List<(int, bool)>> map, int key, (int, bool) value)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = [];
            list.Add(value);
        }
    }

    /// <summary>The unit direction a path leaves its junction in, over a few pixels.</summary>
    private static (double X, double Y) Leaving(Path path, bool atStart)
    {
        var points = path.Points;
        var reach = Math.Min(8, points.Count - 1);
        var from = atStart ? points[0] : points[^1];
        var to = atStart ? points[reach] : points[^(reach + 1)];
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        return length == 0 ? (0, 0) : (dx / length, dy / length);
    }

    private static float Length(Path path)
    {
        var length = 0f;
        for (var i = 1; i < path.Points.Count; i++)
        {
            var dx = path.Points[i].X - path.Points[i - 1].X;
            var dy = path.Points[i].Y - path.Points[i - 1].Y;
            length += MathF.Sqrt(dx * dx + dy * dy);
        }
        return length;
    }

    // ---- strokes --------------------------------------------------------------

    private static Stroke? StrokeOf(Path path, float[] distance, SKColor[] pixels, int w, bool byDarkness)
    {
        // Positions at pixel centres; width from the distance at each.
        var raw = path.Points
            // Back from the padded page: one pixel up and left.
            .Select(p => (X: p.X - 0.5, Y: p.Y - 0.5, Width: Math.Max(1.0, 2.0 * distance[p.Y * w + p.X])))
            .ToList();
        var smooth = Smoothed(raw, path.Closed);
        var simple = Simplified(smooth, tolerance: 0.6);
        // A path that never went anywhere (a knot of noise) is a dot, not a
        // line of no length.
        if (simple.All(q => Math.Abs(q.X - simple[0].X) < 0.01 && Math.Abs(q.Y - simple[0].Y) < 0.01)) simple = [simple[0]];

        var widest = simple.Max(p => p.Width);
        var size = Math.Max(1.0, Math.Round(widest, 1));
        var colour = ColourOf(path, pixels, w, byDarkness);
        return new Stroke
        {
            Color = $"#{colour.Red:x2}{colour.Green:x2}{colour.Blue:x2}",
            Points = [.. simple.Select(p => new StrokePoint(p.X, p.Y, Math.Clamp(p.Width / size, 0.05, 1)))],
            Brush = new BrushSettings
            {
                Size = size,
                Hardness = 0.9,
                Opacity = 1,
                Flow = 1,
                Spacing = 0.05,
                PressureSizeGamma = 1,
            },
        };
    }

    /// <summary>The ink's colour along the line: alpha-weighted, from the line's own pixels.</summary>
    private static SKColor ColourOf(Path path, SKColor[] pixels, int w, bool byDarkness)
    {
        double r = 0, g = 0, b = 0, weight = 0;
        foreach (var p in path.Points)
        {
            var c = pixels[p.Y * w + p.X];
            // On a scan the darkest middle of the line is its colour, and the
            // paper showing through its edges is not.
            double how = byDarkness ? InkOf(c, byDarkness: true) : c.Alpha;
            r += c.Red * how;
            g += c.Green * how;
            b += c.Blue * how;
            weight += how;
        }
        if (weight <= 0) return SKColors.Black;
        return new SKColor((byte)Math.Round(r / weight), (byte)Math.Round(g / weight), (byte)Math.Round(b / weight));
    }

    /// <summary>A moving average over five pixels, ends held: the staircase out, the shape kept.</summary>
    private static List<(double X, double Y, double Width)> Smoothed(List<(double X, double Y, double Width)> points, bool closed)
    {
        if (points.Count < 5) return points;
        var result = new List<(double X, double Y, double Width)>(points.Count);
        var n = points.Count;
        for (var i = 0; i < n; i++)
        {
            if (!closed && (i < 2 || i >= n - 2))
            {
                result.Add(points[i]);
                continue;
            }
            double x = 0, y = 0, width = 0;
            for (var k = -2; k <= 2; k++)
            {
                var j = closed ? ((i + k) % (n - 1) + (n - 1)) % (n - 1) : i + k;
                x += points[j].X;
                y += points[j].Y;
                width += points[j].Width;
            }
            result.Add((x / 5, y / 5, width / 5));
        }
        if (closed) result[^1] = result[0];
        return result;
    }

    /// <summary>Ramer–Douglas–Peucker on position, keeping each kept point's width.</summary>
    private static List<(double X, double Y, double Width)> Simplified(List<(double X, double Y, double Width)> points, double tolerance)
    {
        if (points.Count < 3) return points;
        var keep = new bool[points.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, points.Count - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            var farthest = -1;
            var distance = tolerance;
            for (var i = a + 1; i < b; i++)
            {
                var d = DistanceToSegment(points[i], points[a], points[b]);
                // A change of width is kept as well as a change of direction.
                var widthChange = Math.Abs(points[i].Width - Lerp(points[a].Width, points[b].Width, a, b, i));
                var score = Math.Max(d, widthChange / 2);
                if (score > distance)
                {
                    distance = score;
                    farthest = i;
                }
            }
            if (farthest < 0) continue;
            keep[farthest] = true;
            stack.Push((a, farthest));
            stack.Push((farthest, b));
        }
        var result = new List<(double X, double Y, double Width)>();
        for (var i = 0; i < points.Count; i++) if (keep[i]) result.Add(points[i]);
        return result;
    }

    private static double Lerp(double from, double to, int a, int b, int i) => from + (to - from) * (i - a) / (b - a);

    private static double DistanceToSegment((double X, double Y, double W) p, (double X, double Y, double W) a, (double X, double Y, double W) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared == 0) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        var x = a.X + t * dx - p.X;
        var y = a.Y + t * dy - p.Y;
        return Math.Sqrt(x * x + y * y);
    }
}

/// <summary>Where a picture's ink is read from.</summary>
public enum InkSource
{
    /// <summary>Transparency on a drawn layer, darkness on a picture with none.</summary>
    Auto,

    /// <summary>Opacity: a drawn or flattened layer, ink on nothing.</summary>
    Transparency,

    /// <summary>Darkness: a scan or a photo, ink on paper.</summary>
    Darkness,
}

/// <summary>How a trace reads a picture (Q269): the knobs an artist turns while watching the preview.</summary>
public sealed record LineTraceOptions
{
    /// <summary>
    /// 0..1: how much ink a pixel needs to count — lower takes fainter lines,
    /// higher only the darkest. On a picture this is the contrast control:
    /// both decide which pixels are ink, which is all a trace asks of them.
    /// </summary>
    public double Threshold { get; init; } = 0.25;

    /// <summary>Where the ink is read from.</summary>
    public InkSource Source { get; init; } = InkSource.Auto;

    /// <summary>Lines shorter than this, in pixels, are dropped as noise. A lone dot is kept.</summary>
    public double MinimumLength { get; init; } = 3;
}

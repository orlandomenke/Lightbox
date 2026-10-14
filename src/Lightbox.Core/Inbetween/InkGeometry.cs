using Lightbox.Core.Documents;

namespace Lightbox.Core.Inbetween;

/// <summary>
/// Points of ink in square cells, so "the nearest ink to here" looks at a few
/// cells instead of every point.
/// </summary>
/// <remarks>
/// The bend and the solid fit ask it for every point of a part, against every
/// point of the other key's part: on a dense key that was hundreds of millions
/// of distances per click (the leak review, 2026-10-14). The cell is about the
/// reach a bend is asked to make, so most questions are answered in the first
/// ring of cells.
/// </remarks>
internal sealed class InkGrid
{
    private const double Cell = 12;

    private readonly Dictionary<(int, int), List<StrokePoint>> _cells = [];
    private readonly StrokePoint _any;

    public InkGrid(IEnumerable<StrokePoint> ink)
    {
        foreach (var p in ink)
        {
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y)) continue;
            var key = Key(p);
            if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = [];
            list.Add(p);
            _any = p;
        }
    }

    public bool IsEmpty => _cells.Count == 0;

    private static (int, int) Key(StrokePoint p) =>
        ((int)Math.Floor(p.X / Cell), (int)Math.Floor(p.Y / Cell));

    /// <summary>The nearest point of ink and its distance; for an empty grid, a point infinitely far.</summary>
    public (StrokePoint Point, double Distance) Nearest(StrokePoint p)
    {
        if (_cells.Count == 0 || !double.IsFinite(p.X) || !double.IsFinite(p.Y)) return (_any, double.PositiveInfinity);
        var (cx, cy) = Key(p);
        var best = _any;
        var bestD = double.PositiveInfinity;
        // Rings outward until the nearest ring cannot hold anything closer.
        for (var ring = 0; ; ring++)
        {
            for (var x = cx - ring; x <= cx + ring; x++)
            for (var y = cy - ring; y <= cy + ring; y++)
            {
                if (Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) != ring) continue;
                if (!_cells.TryGetValue((x, y), out var list)) continue;
                foreach (var q in list)
                {
                    var d = (q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y);
                    if (d < bestD) (best, bestD) = (q, d);
                }
            }
            if (bestD <= (ring * Cell) * (ring * Cell)) break;
            if (ring > 4096) break;   // ink further than any drawing reaches
        }
        return (best, Math.Sqrt(bestD));
    }
}

/// <summary>
/// A move, turn and uniform scale: <c>x' = Scale · R(Angle) · x + (Tx, Ty)</c>.
/// </summary>
internal readonly record struct Similarity(double Angle, double Scale, double Tx, double Ty)
{
    public StrokePoint Apply(StrokePoint p)
    {
        var (c, s) = (Math.Cos(Angle), Math.Sin(Angle));
        return p with { X = Scale * (c * p.X - s * p.Y) + Tx, Y = Scale * (s * p.X + c * p.Y) + Ty };
    }

    public StrokePoint Invert(StrokePoint p)
    {
        var (x, y) = ((p.X - Tx) / Scale, (p.Y - Ty) / Scale);
        var (c, s) = (Math.Cos(-Angle), Math.Sin(-Angle));
        return p with { X = c * x - s * y, Y = s * x + c * y };
    }

    /// <summary>The turns a fit starts from: a shoe can arrive at any of them between two keys.</summary>
    private static readonly double[] Starts = [.. Enumerable.Range(0, 8).Select(k => k * Math.PI / 4)];

    /// <summary>
    /// The move, turn and scale that best lays ink <paramref name="a"/> on ink
    /// <paramref name="b"/>: closest points paired both ways round, the best
    /// similarity solved for the pairs, repeated, from every start in
    /// <see cref="Starts"/>; the fit leaving the least distance wins.
    /// </summary>
    /// <remarks>
    /// Both ways round so that lines one key has and the other does not — a
    /// lace, a crease — pull less than the outline both share. Deterministic:
    /// fixed starts, fixed samples, fixed iterations.
    /// </remarks>
    public static Similarity Fit(
        List<StrokePoint> a, List<StrokePoint> b,
        (StrokePoint Centre, double Spread) boxA, (StrokePoint Centre, double Spread) boxB)
    {
        var scale0 = boxA.Spread > 0 && boxB.Spread > 0 ? boxB.Spread / boxA.Spread : 1;
        if (a.Count == 0 || b.Count == 0) return Around(0, scale0, boxA.Centre, boxB.Centre);
        var sa = Sample(a, 160);
        var sb = Sample(b, 160);
        var gridA = new InkGrid(a);
        var gridB = new InkGrid(b);

        var best = Around(0, scale0, boxA.Centre, boxB.Centre);
        var bestError = double.PositiveInfinity;
        foreach (var start in Starts)
        {
            var fit = Around(start, scale0, boxA.Centre, boxB.Centre);
            for (var i = 0; i < 30; i++)
            {
                var pairs = new List<(StrokePoint P, StrokePoint Q)>(sa.Count + sb.Count);
                foreach (var p in sa) pairs.Add((p, gridB.Nearest(fit.Apply(p)).Point));
                foreach (var q in sb) pairs.Add((gridA.Nearest(fit.Invert(q)).Point, q));
                if (Solve(pairs) is not { } next) break;
                fit = next;
            }
            var error = sa.Average(p => gridB.Nearest(fit.Apply(p)).Distance)
                        + sb.Average(q => gridA.Nearest(fit.Invert(q)).Distance) * fit.Scale;
            if (error < bestError) (best, bestError) = (fit, error);
        }
        return best;
    }

    /// <summary>A turn of <paramref name="angle"/> and a scale about <paramref name="from"/>, carrying it to <paramref name="to"/>.</summary>
    private static Similarity Around(double angle, double scale, StrokePoint from, StrokePoint to)
    {
        var turned = new Similarity(angle, scale, 0, 0).Apply(from);
        return new Similarity(angle, scale, to.X - turned.X, to.Y - turned.Y);
    }

    private static List<StrokePoint> Sample(List<StrokePoint> ink, int n) =>
        ink.Count <= n ? ink : [.. Enumerable.Range(0, n).Select(i => ink[(int)((long)i * ink.Count / n)])];

    /// <summary>The least-squares similarity carrying each P onto its Q (Umeyama, in two dimensions).</summary>
    private static Similarity? Solve(List<(StrokePoint P, StrokePoint Q)> pairs)
    {
        if (pairs.Count < 2) return null;
        double px = 0, py = 0, qx = 0, qy = 0;
        foreach (var (p, q) in pairs) { px += p.X; py += p.Y; qx += q.X; qy += q.Y; }
        var n = pairs.Count;
        (px, py, qx, qy) = (px / n, py / n, qx / n, qy / n);
        double dot = 0, cross = 0, spread = 0;
        foreach (var (p, q) in pairs)
        {
            var (ax, ay, bx, by) = (p.X - px, p.Y - py, q.X - qx, q.Y - qy);
            dot += ax * bx + ay * by;
            cross += ax * by - ay * bx;
            spread += ax * ax + ay * ay;
        }
        if (spread <= 1e-9) return null;
        var angle = Math.Atan2(cross, dot);
        var scale = Math.Sqrt(dot * dot + cross * cross) / spread;
        if (!double.IsFinite(scale) || scale <= 1e-6) return null;
        var (c, s) = (Math.Cos(angle), Math.Sin(angle));
        return new Similarity(angle, scale, qx - scale * (c * px - s * py), qy - scale * (s * px + c * py));
    }
}

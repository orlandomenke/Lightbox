using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.Core.Inbetween;

/// <summary>A piece of a stroke cut for matching: its own geometry, the stroke it came from, and its place along it.</summary>
public sealed record LinePiece(Stroke Stroke, string Source, int Ordinal);

/// <summary>
/// Strokes cut into the pieces an artist matches (Q271): where another line
/// ends against a stroke, and at a sharp corner.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> One stroke is often several lines of the subject — a traced
/// outline running from the waistband down the front of a leg and over a shoe,
/// or a hand that did not lift the pen at a corner. Matching whole strokes
/// cannot say "this part of that stroke is the shin". The pieces exist for
/// matching only: the drawing is never changed.
/// </para>
/// <para>
/// <b>Stable.</b> The same drawing always cuts the same way, so a saved
/// <see cref="PieceRef"/> means the same piece after a reload. Pieces are
/// numbered from the stroke's start.
/// </para>
/// </remarks>
public static class LinePieces
{
    /// <summary>Points along a stroke are put this far apart before cutting, in pixels.</summary>
    private const double Step = 3;

    /// <summary>How far, in points, a corner is measured either side; about 24px.</summary>
    private const int Arm = 8;

    /// <summary>Turns sharper than this are corners.</summary>
    private const double Corner = Math.PI / 3;

    /// <summary>The most points one span of a stroke is split into when spacing it.</summary>
    private const int MaxSplits = 4096;

    /// <summary>No piece shorter than this many points (about 15px) is cut off.</summary>
    private const int Shortest = 5;

    public static List<LinePiece> Cut(IReadOnlyList<Stroke> strokes)
    {
        var dense = strokes.Select(s => Even(s.Points, Step)).ToList();
        var ends = new List<(int Owner, StrokePoint P)>();
        for (var i = 0; i < dense.Count; i++)
        {
            if (dense[i].Count == 0) continue;
            ends.Add((i, dense[i][0]));
            ends.Add((i, dense[i][^1]));
        }

        var reachOf = strokes.Select(s => Math.Max(s.Brush.Size * 2, 8)).ToList();
        var junctions = JunctionsOf(dense, ends, reachOf);

        var pieces = new List<LinePiece>();
        for (var i = 0; i < strokes.Count; i++)
        {
            var points = dense[i];
            if (points.Count < Shortest * 2)
            {
                pieces.Add(new LinePiece(strokes[i], strokes[i].Id, 0));
                continue;
            }

            var cuts = new SortedSet<int>();
            foreach (var at in junctions[i])
            {
                if (at > Shortest && at < points.Count - 1 - Shortest) cuts.Add(at);
            }
            Corners(points, cuts);

            var kept = new List<int>();
            var last = 0;
            foreach (var c in cuts)
            {
                if (c - last < Shortest || points.Count - 1 - c < Shortest) continue;
                kept.Add(c);
                last = c;
            }
            if (kept.Count == 0)
            {
                pieces.Add(new LinePiece(strokes[i], strokes[i].Id, 0));
                continue;
            }

            var start = 0;
            var ordinal = 0;
            foreach (var c in kept.Append(points.Count - 1))
            {
                var piece = strokes[i].Clone(newId: false);
                piece.Id = $"{strokes[i].Id}#{ordinal}";
                piece.Points = points.GetRange(start, c - start + 1);
                piece.Path = null;
                pieces.Add(new LinePiece(piece, strokes[i].Id, ordinal++));
                start = c;
            }
        }
        return pieces;
    }

    /// <summary>
    /// The stroke's own points, with straight points added wherever two lie
    /// further apart than <paramref name="step"/>. Not <see cref="GeometryOps.Densify"/>:
    /// that curves the spans for rendering and hands a two-point line back
    /// untouched, which leaves a straight line with no middle to cut at.
    /// </summary>
    internal static List<StrokePoint> Even(IReadOnlyList<StrokePoint> points, double step)
    {
        var even = new List<StrokePoint>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            if (i > 0)
            {
                // Bounded: a hostile or broken file's segment of absurd length
                // gets at most MaxSplits points, never billions (the sensitivity
                // review, 2026-10-14). Not finite is not a length at all.
                var length = GeometryOps.Dist(points[i - 1], points[i]);
                var n = double.IsFinite(length) ? (int)Math.Min(Math.Ceiling(length / step), MaxSplits) : 0;
                for (var k = 1; k < n; k++) even.Add(GeometryOps.LerpPoint(points[i - 1], points[i], (double)k / n));
            }
            even.Add(points[i]);
        }
        return even;
    }

    /// <summary>Cell size of the junction search, in pixels.</summary>
    private const double JunctionCell = 16;

    /// <summary>
    /// For each stroke, the points where another stroke's end touches it: for
    /// every end, the nearest point of every other stroke within that stroke's
    /// reach. Every point sits in a grid cell, so an end looks at the cells
    /// around it rather than at every point of every stroke — the search was
    /// ends × points, a few million distances on a dense key, paid on every
    /// redraw (the leak review, 2026-10-14). The answer is the same.
    /// </summary>
    private static List<int>[] JunctionsOf(List<List<StrokePoint>> dense, List<(int Owner, StrokePoint P)> ends, List<double> reachOf)
    {
        var grid = new Dictionary<(int, int), List<(int Stroke, int Index)>>();
        (int, int) Key(StrokePoint p) => ((int)Math.Floor(p.X / JunctionCell), (int)Math.Floor(p.Y / JunctionCell));
        for (var s = 0; s < dense.Count; s++)
        {
            for (var k = 0; k < dense[s].Count; k++)
            {
                var p = dense[s][k];
                if (!double.IsFinite(p.X) || !double.IsFinite(p.Y)) continue;
                var key = Key(p);
                if (!grid.TryGetValue(key, out var cell)) grid[key] = cell = [];
                cell.Add((s, k));
            }
        }

        var found = new List<int>[dense.Count];
        for (var s = 0; s < dense.Count; s++) found[s] = [];
        var span = (int)Math.Ceiling(reachOf.DefaultIfEmpty(8).Max() / JunctionCell);
        var nearest = new Dictionary<int, (double D, int Index)>();
        foreach (var (owner, end) in ends)
        {
            if (!double.IsFinite(end.X) || !double.IsFinite(end.Y)) continue;
            nearest.Clear();
            var (cx, cy) = Key(end);
            for (var x = cx - span; x <= cx + span; x++)
            for (var y = cy - span; y <= cy + span; y++)
            {
                if (!grid.TryGetValue((x, y), out var cell)) continue;
                foreach (var (s, k) in cell)
                {
                    if (s == owner) continue;
                    var d = GeometryOps.Dist(dense[s][k], end);
                    if (d > reachOf[s]) continue;
                    if (!nearest.TryGetValue(s, out var best) || d < best.D || (d == best.D && k < best.Index)) nearest[s] = (d, k);
                }
            }
            foreach (var (s, hit) in nearest) found[s].Add(hit.Index);
        }
        return found;
    }

    /// <summary>The sharpest point of each turn sharper than <see cref="Corner"/>.</summary>
    private static void Corners(List<StrokePoint> points, SortedSet<int> cuts)
    {
        var turn = new double[points.Count];
        for (var k = Arm; k < points.Count - Arm; k++)
        {
            var before = Math.Atan2(points[k].Y - points[k - Arm].Y, points[k].X - points[k - Arm].X);
            var after = Math.Atan2(points[k + Arm].Y - points[k].Y, points[k + Arm].X - points[k].X);
            var d = Math.Abs(after - before);
            turn[k] = d > Math.PI ? 2 * Math.PI - d : d;
        }
        for (var k = Arm; k < points.Count - Arm; k++)
        {
            if (turn[k] < Corner) continue;
            var peak = true;
            for (var j = k - Arm; j <= k + Arm && peak; j++)
            {
                if (turn[j] > turn[k] || (turn[j] == turn[k] && j < k)) peak = false;
            }
            if (peak) cuts.Add(k);
        }
    }
}

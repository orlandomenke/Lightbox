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
            Junctions(points, ends, i, Math.Max(strokes[i].Brush.Size * 2, 8), cuts);
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

    /// <summary>Where another stroke's end touches this one, away from this one's own ends.</summary>
    private static void Junctions(List<StrokePoint> points, List<(int Owner, StrokePoint P)> ends, int self, double reach, SortedSet<int> cuts)
    {
        foreach (var (owner, end) in ends)
        {
            if (owner == self) continue;
            var (best, at) = (double.MaxValue, -1);
            for (var k = 0; k < points.Count; k++)
            {
                var d = GeometryOps.Dist(points[k], end);
                if (d < best) (best, at) = (d, k);
            }
            if (best <= reach && at > Shortest && at < points.Count - 1 - Shortest) cuts.Add(at);
        }
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

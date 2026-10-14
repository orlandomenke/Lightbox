using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.Core.Inbetween;

/// <summary>
/// One part of the drawing as each key draws it: the strokes of key A, and the
/// strokes of key B, that are the same line (indices into each key's
/// effective strokes). Either side may be empty: a part that appears, or one
/// that goes.
/// </summary>
/// <remarks>
/// Only <em>which</em> strokes — not their order, nor which way each runs. The
/// engine strings each side together by its nearest ends, which it does more
/// reliably than an answer can state it.
/// </remarks>
/// <param name="Solid">
/// Whether the part is one solid object — a shoe, a cuff — that moves, turns
/// and scales as a whole, drawn from the nearer key's lines; null leaves it to
/// the engine, which treats a part of many small lines that way (Q271).
/// </param>
public sealed record PartMatch(IReadOnlyList<int> A, IReadOnlyList<int> B, bool? Solid = null);

/// <summary>
/// An inbetween drawn from a stated correspondence (Q270, 2026-10-14).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> The stroke matcher pairs strokes one to one. Keys drawn with
/// different strokes — another split, another order, the other way round, as
/// a hand or a trace draws them — have no one-to-one pairing, so it pairs the
/// wrong lines and the frame scrambles. A model asked to draw the frame
/// instead could see which line was which and still could not draw it: four
/// runs on a traced leg all lost the knee. So the model, or the artist, says
/// which strokes go together, and this draws.
/// </para>
/// <para>
/// <b>How.</b> Each side of a part is strung into one path, ends nearest to
/// ends; the two paths are resampled by length, oriented (and, when both are
/// closed, turned) so their points correspond, and tweened point by point. The
/// result is cut back into the pieces of the nearer key, without the jumps
/// between them, and each piece keeps that key's brush — the line comes back
/// in the artist's own pieces and marks.
/// </para>
/// <para>
/// Strokes the answer does not mention go to the ordinary matcher, so a
/// partial answer still gives a whole drawing; a part with nothing on one side
/// fades as an unmatched stroke always has.
/// </para>
/// </remarks>
public static class MatchedInbetween
{
    /// <summary>
    /// An inbetween from the artist's saved matches (Q271): both keys' raw
    /// strokes, and the matches kept on key A.
    /// </summary>
    /// <remarks>
    /// A stroke a match names is cut into its pieces (<see cref="LinePieces"/>);
    /// a stroke no match names stays whole, so the ordinary matcher sees what
    /// the artist drew. A reference to a stroke or piece that is gone is
    /// dropped, and a match left with nothing on either side is skipped.
    /// </remarks>
    public static List<Stroke> Inbetween(
        IReadOnlyList<Stroke> a, IReadOnlyList<Stroke> b, IReadOnlyList<LineMatch> matches, double t, Easing easing)
    {
        var ea = StrokeRecordCleaner.EffectiveStrokes(a);
        var eb = StrokeRecordCleaner.EffectiveStrokes(b);
        var (sa, ia) = Resolved(ea, matches.SelectMany(m => m.A));
        var (sb, ib) = Resolved(eb, matches.SelectMany(m => m.B));
        var parts = matches
            .Select(m => new PartMatch(
                [.. m.A.Select(r => ia.GetValueOrDefault(r, -1)).Where(i => i >= 0)],
                [.. m.B.Select(r => ib.GetValueOrDefault(r, -1)).Where(i => i >= 0)],
                m.Solid))
            .Where(p => p.A.Count > 0 || p.B.Count > 0)
            .ToList();
        return Inbetween(sa, sb, parts, t, easing);
    }

    /// <summary>One key's strokes with the ones a match names cut into pieces, and where each named piece now sits.</summary>
    private static (List<Stroke> Strokes, Dictionary<PieceRef, int> Index) Resolved(
        IReadOnlyList<Stroke> strokes, IEnumerable<PieceRef> named)
    {
        var mentioned = named.Select(r => r.Stroke).ToHashSet();
        var pieces = (mentioned.Count == 0 ? [] : LinePieces.Cut(strokes)).ToLookup(p => p.Source);
        var list = new List<Stroke>();
        var index = new Dictionary<PieceRef, int>();
        foreach (var s in strokes)
        {
            if (!mentioned.Contains(s.Id))
            {
                list.Add(s);
                continue;
            }
            foreach (var piece in pieces[s.Id])
            {
                index[new PieceRef(s.Id, piece.Ordinal)] = list.Count;
                list.Add(piece.Stroke);
            }
        }
        return (list, index);
    }

    public static List<Stroke> Inbetween(
        IReadOnlyList<Stroke> a, IReadOnlyList<Stroke> b, IReadOnlyList<PartMatch> parts, double t, Easing easing)
    {
        var et = EasingOps.Ease(Math.Clamp(t, 0, 1), easing);
        var claimedA = new HashSet<int>();
        var claimedB = new HashSet<int>();
        var drawn = new List<Stroke>();
        var matched = new List<(List<Stroke> A, List<Stroke> B)>();

        foreach (var part in parts)
        {
            var pa = Claim(part.A, a, claimedA);
            var pb = Claim(part.B, b, claimedB);
            if (pa.Count > 0 && pb.Count > 0)
            {
                matched.Add((pa, pb));
                var cluster = IsCluster(pa, pb);
                drawn.AddRange((part.Solid ?? cluster) ? Solid(pa, pb, et) : cluster ? Together(pa, pb, et) : Tween(pa, pb, et));
            }
            else if (pa.Count > 0) drawn.AddRange(Fade(pa, Math.Max(0, 1 - et * 2)));
            else if (pb.Count > 0) drawn.AddRange(Fade(pb, Math.Max(0, et * 2 - 1)));
        }

        var restA = a.Where((_, i) => !claimedA.Contains(i)).ToList();
        var restB = b.Where((_, i) => !claimedB.Contains(i)).ToList();
        if (restA.Count > 0 || restB.Count > 0) drawn.AddRange(Leftovers(restA, restB, et, matched));
        return drawn;
    }

    /// <summary>
    /// The lines no match names, in a gap where the artist matched others: a
    /// pair the ordinary matcher proposes is kept only when it is convincing —
    /// about the same length and shape, and, for a speck, in about the same
    /// place. Everything else is a line only one key has, and it fades where
    /// that key has it.
    /// </summary>
    /// <remarks>
    /// The owner, 2026-10-14: "I hope it does not create lines or marks that
    /// aren't in other frames." The matcher pairs every line it can, however
    /// unalike: on the traced run it paired the back leg's sock, hidden in the
    /// next key, with a short unrelated mark, and the halfway line between them
    /// was a tangle in the cuff that neither key has; specks across the page
    /// paired into a speck in the empty space between. A gap with no matches
    /// at all never comes here, and keeps the ordinary matcher's answer.
    /// </remarks>
    private static IEnumerable<Stroke> Leftovers(
        List<Stroke> a, List<Stroke> b, double et, List<(List<Stroke> A, List<Stroke> B)> matched)
    {
        var pen = Math.Max(a.Concat(b).Select(s => s.Brush.Size).DefaultIfEmpty(1).Max(), 1);
        var parts = matched.Select(m => (A: Frame(m.A), B: Frame(m.B), Ink: m.A.SelectMany(s => s.Points).ToList())).ToList();
        foreach (var pair in StrokeMatcher.Match(a, b))
        {
            if (pair.A is { } pa && pair.B is { } pb && Convincing(pa, pb, pen) && TravelsWithItsPart(pa, pb, parts))
            {
                yield return StrokeInterpolator.Interpolate(pa, pb, et);
                continue;
            }
            if (pair.A is { } onlyA)
            {
                foreach (var s in Fade([onlyA], Math.Max(0, 1 - et * 2))) yield return s;
            }
            if (pair.B is { } onlyB)
            {
                foreach (var s in Fade([onlyB], Math.Max(0, et * 2 - 1))) yield return s;
            }
        }
    }

    /// <summary>
    /// Whether a leftover line of key B sits where the matched part nearest
    /// the key A line carried it — a detail travels with the part it is drawn
    /// on. Within half the part's size, and never less than 24px. A line not
    /// on any matched part — further from the nearest than that part's own
    /// size — has no part to go by, and any place will do.
    /// </summary>
    /// <remarks>
    /// The owner, 2026-10-14, of the traced run: "the diagonal/vertical line
    /// in the upper leg of the back leg". A crease at the knee in one key and a
    /// look-alike mark on the other leg in the next are the same length and
    /// shape, so they passed as one line, and their halfway version sat in the
    /// middle of the thigh where neither key has anything.
    /// </remarks>
    private static bool TravelsWithItsPart(
        Stroke a, Stroke b, List<((StrokePoint Centre, double Spread) A, (StrokePoint Centre, double Spread) B, List<StrokePoint> Ink)> parts)
    {
        if (parts.Count == 0 || a.Points.Count == 0 || b.Points.Count == 0) return true;
        var ca = GeometryOps.Centroid(a.Points);
        var nearest = parts.MinBy(p => p.Ink.Count == 0 ? double.MaxValue : p.Ink.Min(q => GeometryOps.Dist(q, ca)));
        var reach = Math.Max(24, nearest.A.Spread * 0.5);
        // A line nowhere near any matched part is not on one: it is its own
        // thing, with its own motion, and no part's motion says where it goes.
        if (nearest.Ink.Count == 0 || nearest.Ink.Min(q => GeometryOps.Dist(q, ca)) > Math.Max(reach, nearest.A.Spread)) return true;
        var expected = new StrokePoint(
            ca.X + nearest.B.Centre.X - nearest.A.Centre.X,
            ca.Y + nearest.B.Centre.Y - nearest.A.Centre.Y, 0);
        return GeometryOps.Dist(GeometryOps.Centroid(b.Points), expected) <= reach;
    }

    /// <summary>Shorter than this, a line is a speck: it pairs only with a speck in about the same place.</summary>
    private const double Speck = 12;

    /// <summary>
    /// Whether two lines are believably the same line: lengths within a factor
    /// of 1.6, shapes within a third of their length of each other once laid
    /// over each other (either way round), and specks no further apart than a
    /// few pen widths.
    /// </summary>
    private static bool Convincing(Stroke a, Stroke b, double pen)
    {
        var (la, lb) = (GeometryOps.PathLength(a.Points), GeometryOps.PathLength(b.Points));
        var longer = Math.Max(la, lb);
        if (longer < Speck)
        {
            return GeometryOps.Dist(GeometryOps.Centroid(a.Points), GeometryOps.Centroid(b.Points)) <= pen * 3;
        }
        if (Math.Min(la, lb) * 1.6 < longer || a.Points.Count < 2 || b.Points.Count < 2) return false;
        var ra = GeometryOps.Resample(a.Points, 16);
        var rb = GeometryOps.Resample(b.Points, 16);
        var (ca, cb) = (GeometryOps.Centroid(ra), GeometryOps.Centroid(rb));
        double forward = 0, backward = 0;
        for (var i = 0; i < 16; i++)
        {
            forward += Math.Sqrt(Sq(ra[i].X - ca.X - (rb[i].X - cb.X)) + Sq(ra[i].Y - ca.Y - (rb[i].Y - cb.Y)));
            backward += Math.Sqrt(Sq(ra[i].X - ca.X - (rb[15 - i].X - cb.X)) + Sq(ra[i].Y - ca.Y - (rb[15 - i].Y - cb.Y)));
        }
        return Math.Min(forward, backward) / 16 <= longer / 3;
        static double Sq(double v) => v * v;
    }

    /// <summary>The strokes a part names that exist and no earlier part took; nonsense is skipped.</summary>
    private static List<Stroke> Claim(IReadOnlyList<int> indices, IReadOnlyList<Stroke> strokes, HashSet<int> claimed)
    {
        var taken = new List<Stroke>();
        foreach (var i in indices)
        {
            if (i < 0 || i >= strokes.Count || strokes[i].Points.Count == 0 || !claimed.Add(i)) continue;
            taken.Add(strokes[i]);
        }
        return taken;
    }

    private static IEnumerable<Stroke> Fade(List<Stroke> strokes, double factor)
    {
        foreach (var s in strokes)
        {
            var op = s.Brush.Opacity * factor;
            if (op <= Inbetweener.FadeThreshold) continue;
            var faded = s.Clone();
            faded.Brush.Opacity = op;
            yield return faded;
        }
    }

    /// <summary>A side of a part strung into one path: its points, and which piece each point came from.</summary>
    private sealed record Chain(List<StrokePoint> Points, List<int> PieceOf, List<Stroke> Pieces, double Length);

    private static List<Stroke> Tween(List<Stroke> a, List<Stroke> b, double et)
    {
        var ca = Strung(a);
        var cb = Strung(b);
        var n = Math.Clamp((int)(Math.Max(ca.Length, cb.Length) / 3), StrokeInterpolator.Samples, 2000);
        var (ra, la) = Resampled(ca, n);
        var (rb, lb) = Resampled(cb, n);

        if (Closed(ca) && Closed(cb))
        {
            Turn(ra, rb, lb);
        }
        else
        {
            var forward = GeometryOps.Dist(ra[0], rb[0]) + GeometryOps.Dist(ra[^1], rb[^1]);
            var backward = GeometryOps.Dist(ra[0], rb[^1]) + GeometryOps.Dist(ra[^1], rb[0]);
            if (backward < forward)
            {
                rb.Reverse();
                lb.Reverse();
            }
        }

        // On an arc about the part's joint, not along the chord (Q273).
        var points = Arc.Lerp(ra, rb, et);

        // Cut back into the nearer key's pieces; a sample on a jump between
        // two pieces belongs to neither and is not drawn.
        var (labels, pieces) = et < 0.5 ? (la, ca.Pieces) : (lb, cb.Pieces);
        var cut = new List<Stroke>();
        var start = 0;
        for (var i = 1; i <= n; i++)
        {
            if (i < n && labels[i] == labels[start]) continue;
            if (labels[start] >= 0 && i - start >= 2)
            {
                var piece = pieces[labels[start]].Clone();
                piece.Points = points.GetRange(start, i - start);
                // Generated geometry has no authored nodes (see StrokeInterpolator).
                piece.Path = null;
                piece.Label = a[0].Label ?? b[0].Label;
                cut.Add(piece);
            }
            start = i;
        }
        return cut;
    }

    /// <summary>
    /// A part made of many small lines — a cuff, a shoe, a waistband — rather
    /// than one line drawn in pieces: four or more pieces on either side, or
    /// pieces that do not join up end to end.
    /// </summary>
    /// <remarks>
    /// Strung into one path, lines that sit side by side (the double line of a
    /// sole, a lace across a shoe) are tied into a zigzag, and two zigzags
    /// strung differently tween into a knot. Found on a traced leg, 2026-10-14.
    /// </remarks>
    private static bool IsCluster(List<Stroke> a, List<Stroke> b) => Clustered(a, b);

    /// <summary>
    /// Whether a match of these lines is one solid object when the artist has
    /// not said (Q271): a part of many small lines, or of lines that do not join
    /// up end to end - a shoe, a cuff - rather than one line drawn in pieces.
    /// </summary>
    public static bool SolidByDefault(IReadOnlyList<Stroke> a, IReadOnlyList<Stroke> b) => Clustered([.. a], [.. b]);

    private static bool Clustered(List<Stroke> a, List<Stroke> b) =>
        a.Count >= 4 || b.Count >= 4 || Jumps(Strung(a)) > 0.25 || Jumps(Strung(b)) > 0.25;

    /// <summary>How much of a chain is the jumps between its pieces, as a share of its length.</summary>
    private static double Jumps(Chain chain)
    {
        double jumps = 0;
        for (var i = 1; i < chain.Points.Count; i++)
        {
            if (chain.PieceOf[i] != chain.PieceOf[i - 1]) jumps += GeometryOps.Dist(chain.Points[i - 1], chain.Points[i]);
        }
        return chain.Length > 0 ? jumps / chain.Length : 0;
    }

    /// <summary>
    /// A cluster tweened as one thing: laid over each other, each key's lines
    /// bend towards the other key's nearest ink, so no piece has to be the
    /// partner of another. Before halfway key A's lines are drawn, after it key
    /// B's — each in its own pieces and brush. A line with no ink near it in
    /// the other key rides along with the part while it fades.
    /// </summary>
    /// <remarks>
    /// Pairing pieces one to one was tried first and fails on exactly the case
    /// this is for: a shoe drawn as four edges in one key and two corners in
    /// the other has no piece in common with itself.
    /// </remarks>
    private static IEnumerable<Stroke> Together(List<Stroke> a, List<Stroke> b, double et)
    {
        var (ca, ra) = Frame(a);
        var (cb, rb) = Frame(b);
        var inkA = new InkGrid(a.SelectMany(s => LinePieces.Even(s.Points, 3)));
        var inkB = new InkGrid(b.SelectMany(s => LinePieces.Even(s.Points, 3)));
        var pen = Math.Max(a.Concat(b).Max(s => s.Brush.Size), 1);

        // Lines with nothing near them in the other key, once the two are laid
        // over each other; the part's motion is then the motion of the rest.
        bool Orphan(Stroke s, InkGrid other, StrokePoint from, StrokePoint to, double scale, double spread)
        {
            var reach = Math.Max(pen * 2, spread * 0.15);
            // A median needs a sample, not every point: 32 along the line.
            var gaps = (s.Points.Count < 2 ? s.Points.ToList() : GeometryOps.Resample(s.Points, 32))
                .Select(p => Nearest(Over(p, from, to, scale), other).Distance)
                .OrderBy(d => d)
                .ToList();
            return gaps[gaps.Count / 2] > reach;
        }
        var scale0 = ra > 0 && rb > 0 ? rb / ra : 1;
        var orphanA = a.Where(s => Orphan(s, inkB, ca, cb, scale0, rb)).ToHashSet();
        var orphanB = b.Where(s => Orphan(s, inkA, cb, ca, 1 / scale0, ra)).ToHashSet();
        if (orphanA.Count < a.Count && orphanB.Count < b.Count)
        {
            (ca, ra) = Frame([.. a.Where(s => !orphanA.Contains(s))]);
            (cb, rb) = Frame([.. b.Where(s => !orphanB.Contains(s))]);
            inkA = new InkGrid(a.Where(s => !orphanA.Contains(s)).SelectMany(s => LinePieces.Even(s.Points, 3)));
            inkB = new InkGrid(b.Where(s => !orphanB.Contains(s)).SelectMany(s => LinePieces.Even(s.Points, 3)));
        }
        var scale = ra > 0 && rb > 0 ? rb / ra : 1;
        var centre = GeometryOps.LerpPoint(ca, cb, et);
        var size = GeometryOps.Lerp(1, scale, et);

        Stroke Drawn(Stroke s, IEnumerable<StrokePoint> points)
        {
            var piece = s.Clone();
            piece.Points = [.. points];
            piece.Path = null;
            return piece;
        }

        if (et < 0.5)
        {
            foreach (var s in a.Where(s => !orphanA.Contains(s)))
            {
                yield return Drawn(s, LinePieces.Even(s.Points, 3)
                    .Select(p => GeometryOps.LerpPoint(p, Nearest(Over(p, ca, cb, scale), inkB).Point with { Pressure = p.Pressure }, et)));
            }
        }
        else
        {
            foreach (var s in b.Where(s => !orphanB.Contains(s)))
            {
                yield return Drawn(s, LinePieces.Even(s.Points, 3)
                    .Select(q => GeometryOps.LerpPoint(Nearest(Over(q, cb, ca, 1 / scale), inkA).Point with { Pressure = q.Pressure }, q, et)));
            }
        }

        // Orphans travel with the part, at its size, while they fade.
        foreach (var (orphans, from, shrink, fade) in new[]
                 {
                     (orphanA, ca, 1.0, Math.Max(0, 1 - et * 2)),
                     (orphanB, cb, 1 / scale, Math.Max(0, et * 2 - 1)),
                 })
        {
            foreach (var s in orphans)
            {
                var op = s.Brush.Opacity * fade;
                if (op <= Inbetweener.FadeThreshold) continue;
                var piece = Drawn(s, s.Points.Select(p => p with
                {
                    X = centre.X + (p.X - from.X) * shrink * size,
                    Y = centre.Y + (p.Y - from.Y) * shrink * size,
                }));
                piece.Brush.Opacity = op;
                yield return piece;
            }
        }
    }

    /// <summary>A point of one key's part, laid over the other key's part: same place relative to its middle, at its size.</summary>
    private static StrokePoint Over(StrokePoint p, StrokePoint from, StrokePoint to, double scale) =>
        p with { X = to.X + (p.X - from.X) * scale, Y = to.Y + (p.Y - from.Y) * scale };

    private static (StrokePoint Point, double Distance) Nearest(StrokePoint p, InkGrid ink) => ink.Nearest(p);

    /// <summary>
    /// A part of many small lines drawn as one solid object (Q271): the nearer
    /// key's lines, moved, turned and scaled part of the way to where the
    /// other key has the same object. Never bent, so every line keeps the
    /// shape the artist gave it — a shoe stays a shoe the artist drew.
    /// </summary>
    /// <remarks>
    /// The motion is fitted, not assumed: the other key's ink is pulled onto
    /// this key's ink both ways round from several starting turns, and the
    /// closest fit kept. The part then turns about its bounding box's middle
    /// and that middle travels in a straight line — the same rule the rest of
    /// the engine follows, so a shoe does not swing off on an arc of its own.
    /// </remarks>
    private static IEnumerable<Stroke> Solid(List<Stroke> a, List<Stroke> b, double et)
    {
        var inkA = a.SelectMany(s => LinePieces.Even(s.Points, 3)).ToList();
        var inkB = b.SelectMany(s => LinePieces.Even(s.Points, 3)).ToList();
        var fit = Similarity.Fit(inkA, inkB, Frame(a), Frame(b));
        var centre = Frame(a).Centre;
        var travel = fit.Apply(centre);
        var turn = fit.Angle * et;
        var size = Math.Pow(fit.Scale, et);
        var (cos, sin) = (Math.Cos(turn), Math.Sin(turn));
        StrokePoint Place(StrokePoint p)
        {
            var (x, y) = ((p.X - centre.X) * size, (p.Y - centre.Y) * size);
            return p with
            {
                X = centre.X + (travel.X - centre.X) * et + x * cos - y * sin,
                Y = centre.Y + (travel.Y - centre.Y) * et + x * sin + y * cos,
            };
        }
        var (from, back) = et < 0.5 ? (a, (Func<StrokePoint, StrokePoint>)(p => p)) : (b, fit.Invert);
        foreach (var s in from)
        {
            var piece = s.Clone();
            piece.Points = [.. s.Points.Select(p => Place(back(p)))];
            piece.Path = null;
            yield return piece;
        }
    }

    /// <summary>
    /// Where a part is and how big: the middle of its bounding box and the
    /// box's diagonal. The box rather than the ink's centre of mass, because
    /// the outline says where a shoe is and the details inside it do not — a
    /// sole drawn as a double line in one key would otherwise pull the whole
    /// part off its outline. Found by breaking it, 2026-10-14.
    /// </summary>
    private static (StrokePoint Centre, double Spread) Frame(IReadOnlyList<Stroke> strokes)
    {
        var points = strokes.SelectMany(s => s.Points).ToList();
        if (points.Count == 0) return (new StrokePoint(0, 0, 0), 0);
        var (minX, maxX) = (points.Min(p => p.X), points.Max(p => p.X));
        var (minY, maxY) = (points.Min(p => p.Y), points.Max(p => p.Y));
        return (new StrokePoint((minX + maxX) / 2, (minY + maxY) / 2, 0), Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY)) / 2);
    }

    /// <summary>
    /// The pieces strung end to nearest end, starting from the loosest end —
    /// the one furthest from any other piece — so an open line is walked from
    /// one of its tips rather than from its middle.
    /// </summary>
    private static Chain Strung(List<Stroke> pieces)
    {
        var left = Enumerable.Range(0, pieces.Count).ToList();
        StrokePoint First(int i) => pieces[i].Points[0];
        StrokePoint Last(int i) => pieces[i].Points[^1];

        var (head, headReversed) = (0, false);
        var loosest = double.MinValue;
        if (pieces.Count > 1)
        {
            foreach (var i in left)
            {
                foreach (var reversed in new[] { false, true })
                {
                    var end = reversed ? Last(i) : First(i);
                    var nearest = left.Where(j => j != i)
                        .Min(j => Math.Min(GeometryOps.Dist(end, First(j)), GeometryOps.Dist(end, Last(j))));
                    if (nearest > loosest) (loosest, head, headReversed) = (nearest, i, reversed);
                }
            }
        }

        var points = new List<StrokePoint>();
        var pieceOf = new List<int>();
        var order = new List<Stroke>();
        var (current, currentReversed) = (head, headReversed);
        while (true)
        {
            left.Remove(current);
            IEnumerable<StrokePoint> run = currentReversed
                ? Enumerable.Reverse(pieces[current].Points)
                : pieces[current].Points;
            var label = order.Count;
            order.Add(pieces[current]);
            foreach (var p in run)
            {
                points.Add(p);
                pieceOf.Add(label);
            }
            if (left.Count == 0) break;
            var tail = points[^1];
            var next = left.MinBy(j => Math.Min(GeometryOps.Dist(tail, First(j)), GeometryOps.Dist(tail, Last(j))));
            (current, currentReversed) = (next, GeometryOps.Dist(tail, Last(next)) < GeometryOps.Dist(tail, First(next)));
        }
        return new Chain(points, pieceOf, order, GeometryOps.PathLength(points));
    }

    /// <summary>
    /// <paramref name="n"/> points evenly along the chain, each labelled with
    /// the piece it lies on, or -1 on a jump from one piece to the next.
    /// </summary>
    private static (List<StrokePoint> Points, List<int> Labels) Resampled(Chain chain, int n)
    {
        var src = chain.Points;
        var points = new List<StrokePoint>(n);
        var labels = new List<int>(n);
        if (src.Count == 1 || chain.Length <= 0)
        {
            for (var i = 0; i < n; i++)
            {
                points.Add(src[0]);
                labels.Add(chain.PieceOf[0]);
            }
            return (points, labels);
        }

        var step = chain.Length / (n - 1);
        var seg = 0;
        double walked = 0;
        for (var k = 0; k < n; k++)
        {
            var target = Math.Min(k * step, chain.Length);
            while (seg < src.Count - 2 && walked + GeometryOps.Dist(src[seg], src[seg + 1]) < target)
            {
                walked += GeometryOps.Dist(src[seg], src[seg + 1]);
                seg++;
            }
            var len = GeometryOps.Dist(src[seg], src[seg + 1]);
            var u = len > 0 ? Math.Clamp((target - walked) / len, 0, 1) : 0;
            points.Add(GeometryOps.LerpPoint(src[seg], src[seg + 1], u));
            var from = chain.PieceOf[seg];
            var to = chain.PieceOf[seg + 1];
            labels.Add(from == to ? from : u <= 0 ? from : u >= 1 ? to : -1);
        }
        return (points, labels);
    }

    /// <summary>A chain whose ends meet, within a little of its length: an outline, not a line.</summary>
    private static bool Closed(Chain chain) =>
        chain.Points.Count > 2
        && GeometryOps.Dist(chain.Points[0], chain.Points[^1])
            <= Math.Max(chain.Pieces.Max(p => p.Brush.Size) * 2, chain.Length * 0.03);

    /// <summary>
    /// Two closed outlines started at different places, or running opposite
    /// ways: turn and, if it fits better, reverse B so each point meets its own.
    /// </summary>
    private static void Turn(List<StrokePoint> a, List<StrokePoint> b, List<int> labels)
    {
        // The closing point repeats the first; turn the n-1 distinct ones.
        var m = a.Count - 1;
        var ca = GeometryOps.Centroid(a);
        var cb = GeometryOps.Centroid(b);
        int Index(int shift, bool reversed, int i) => reversed ? ((shift - i) % m + m) % m : (shift + i) % m;

        var (bestShift, bestReversed, best) = (0, false, double.MaxValue);
        var stride = Math.Max(1, m / 64);
        foreach (var reversed in new[] { false, true })
        {
            for (var s = 0; s < m; s++)
            {
                double sum = 0;
                for (var i = 0; i < m; i += stride)
                {
                    var j = Index(s, reversed, i);
                    sum += Math.Abs(a[i].X - ca.X - (b[j].X - cb.X)) + Math.Abs(a[i].Y - ca.Y - (b[j].Y - cb.Y));
                }
                if (sum < best) (best, bestShift, bestReversed) = (sum, s, reversed);
            }
        }

        var turned = new List<StrokePoint>(m + 1);
        var turnedLabels = new List<int>(m + 1);
        for (var i = 0; i < m; i++)
        {
            var j = Index(bestShift, bestReversed, i);
            turned.Add(b[j]);
            turnedLabels.Add(labels[j]);
        }
        turned.Add(turned[0]);
        turnedLabels.Add(turnedLabels[0]);
        b.Clear();
        b.AddRange(turned);
        labels.Clear();
        labels.AddRange(turnedLabels);
    }
}

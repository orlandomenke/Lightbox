using System.Globalization;
using System.Text;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;

namespace Lightbox.Core.Cleanup;

/// <summary>What a reviewer may ask the engine to do to one line (Q297).</summary>
public enum ReviewFixKind
{
    /// <summary>Cut a line's end back to where it meets another line.</summary>
    Trim,
    /// <summary>Carry two ends that should meet onto one point.</summary>
    Join,
    /// <summary>Take out a short mark.</summary>
    Remove,
}

/// <summary>
/// One fix a reviewer — a model, or an agent — names: which lines, by the
/// numbers on the review sheet, which key lines it compared them with, and why.
/// </summary>
/// <param name="Line">The inbetween line the fix is to, as numbered on the sheet (1-based).</param>
/// <param name="AtStart">Which end of <paramref name="Line"/>; ignored for <see cref="ReviewFixKind.Remove"/>.</param>
/// <param name="Other">For a trim, the line the end is cut back to; for a join, the line whose end it meets.</param>
/// <param name="OtherAtStart">For a join, which end of <paramref name="Other"/>.</param>
/// <param name="Cites">The key lines this was judged against, as "A3" or "B5" (Q298). A fix that cites none is refused.</param>
public sealed record ReviewFix(
    ReviewFixKind Kind, int Line, bool AtStart, int? Other, bool OtherAtStart,
    IReadOnlyList<string> Cites, string Reason);

/// <summary>A fix the engine would not make, and why — returned so the reviewer learns.</summary>
public sealed record RefusedFix(ReviewFix Fix, string Why);

/// <summary>A review turned into a cleanup proposal: the record as it would be, what changed, and what was refused.</summary>
public sealed record ReviewOutcome(CleanupResult Result, IReadOnlyList<ReviewFix> Made, IReadOnlyList<RefusedFix> Refused);

/// <summary>
/// The numbered drawing a reviewer reads: the inbetween's lines as L1…, the
/// keys' as A1… and B1…, and a listing that says, at every end, which lines are
/// near it and how far they run alongside it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The listing is the review for a reviewer that cannot see.</b> Two
/// providers take no image, and the mistake the review is for — two lines that
/// run on past the joint where they should meet, side by side — is written
/// plainly in "runs alongside L4 for 62 px", which no single coordinate says.
/// </para>
/// <para>
/// <b>Numbers rather than ids</b>, because a model copies "L6" far more
/// reliably than a 20-character id, and they are only meaningful against the
/// sheet they were read from: the engine resolves them through this sheet and
/// nothing else.
/// </para>
/// </remarks>
public sealed class ReviewSheet
{
    public IReadOnlyList<Stroke> Lines { get; }
    public IReadOnlyList<Stroke> KeyA { get; }
    public IReadOnlyList<Stroke> KeyB { get; }

    private ReviewSheet(IReadOnlyList<Stroke> lines, IReadOnlyList<Stroke> keyA, IReadOnlyList<Stroke> keyB)
    {
        Lines = lines;
        KeyA = keyA;
        KeyB = keyB;
    }

    /// <summary>The sheet for a drawing between two keys. Only plain drawn lines are numbered.</summary>
    public static ReviewSheet For(IReadOnlyList<Stroke> record, IReadOnlyList<Stroke> keyA, IReadOnlyList<Stroke> keyB)
    {
        static List<Stroke> Numbered(IReadOnlyList<Stroke> strokes) =>
            [.. StrokeRecordCleaner.EffectiveStrokes(strokes).Where(LineCleaner.Cleanable)];
        return new ReviewSheet(Numbered(record), Numbered(keyA), Numbered(keyB));
    }

    /// <summary>Whether "A3" or "B5" names a line on this sheet.</summary>
    public bool Names(string cite)
    {
        if (string.IsNullOrWhiteSpace(cite)) return false;
        var c = cite.Trim().ToUpperInvariant();
        if (c.Length < 2 || !int.TryParse(c[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1) return false;
        return c[0] switch { 'A' => n <= KeyA.Count, 'B' => n <= KeyB.Count, _ => false };
    }

    /// <summary>
    /// Whether the drawings are past the size a review is bounded to — the
    /// cleaner's own limits, over all three (perf review: an unbounded listing
    /// took 56 s on a dense drawing and ran to ~85k tokens).
    /// </summary>
    public bool TooLarge =>
        KeyA.Count + Lines.Count + KeyB.Count > LineCleaner.MaxLines
        || KeyA.Concat(Lines).Concat(KeyB).Sum(s => (long)s.Points.Count) > LineCleaner.MaxPoints;

    /// <summary>
    /// The listing: every line of each drawing with its length and ends, and
    /// what lies near each end of the inbetween's lines — and of the keys' when
    /// <paramref name="keyNeighbours"/>, for a reviewer with no picture to
    /// compare by (Q298). With a picture the keys' neighbours are two thirds of
    /// the tokens and say what the picture already shows.
    /// </summary>
    public string Listing(bool keyNeighbours = true)
    {
        var text = new StringBuilder();
        void Drawing(string title, string prefix, IReadOnlyList<Stroke> lines, bool neighbours)
        {
            text.Append(title).Append('\n');
            var coarse = neighbours ? lines.Select(l => Coarse(l.Points)).ToList() : null;
            var boxes = coarse?.Select(c => GeometryOps.BoundsOf(c)).ToList();
            var pairs = new Dictionary<(int, int), (double Alongside, bool Crosses)>();
            var grid = coarse is null ? null : Grid(coarse);
            for (var i = 0; i < lines.Count; i++)
            {
                var s = lines[i];
                var a = s.Points[0];
                var b = s.Points[^1];
                text.Append(CultureInfo.InvariantCulture,
                    $"  {prefix}{i + 1}: {GeometryOps.PathLength(s.Points):0} px long, from ({a.X:0},{a.Y:0}) to ({b.X:0},{b.Y:0}), {s.Brush.Size:0.#} px wide\n");
                if (coarse is null) continue;
                foreach (var atStart in new[] { true, false })
                {
                    var near = NearEnd(lines, coarse, boxes!, pairs, grid!, i, atStart);
                    if (near.Count == 0) continue;
                    text.Append(CultureInfo.InvariantCulture, $"    {(atStart ? "start" : "end")}: ")
                        .Append(string.Join("; ", near.Select(n =>
                            string.Create(CultureInfo.InvariantCulture,
                                $"{prefix}{n.Line + 1} {n.Distance:0} px away{(n.Alongside >= 2 * s.Brush.Size ? $", runs alongside it for {n.Alongside:0} px" : "")}{(n.Crosses ? ", crosses it" : "")}"))))
                        .Append('\n');
                }
            }
        }
        Drawing("Key A (drawn by the artist):", "A", KeyA, keyNeighbours);
        Drawing("Key B (drawn by the artist):", "B", KeyB, keyNeighbours);
        Drawing("The inbetween under review:", "L", Lines, true);
        return text.ToString();
    }

    /// <summary>How far, in pixels, around an end other lines are reported.</summary>
    private const double Reach = 40;

    /// <summary>The most points a line is measured with for the listing.</summary>
    private const int CoarsePoints = 24;

    /// <summary>A line resampled evenly to at most <see cref="CoarsePoints"/> points — the listing describes, it does not edit.</summary>
    internal static List<StrokePoint> Coarse(IReadOnlyList<StrokePoint> points)
    {
        if (points.Count <= CoarsePoints) return [.. points];
        return GeometryOps.Resample(points, CoarsePoints);
    }

    /// <summary>
    /// The nearest lines to one end, closest first, at most four — chosen by
    /// distance alone, cheaply, and only those four measured for how far they
    /// run alongside and whether they cross.
    /// </summary>
    private static List<(int Line, double Distance, double Alongside, bool Crosses)> NearEnd(
        IReadOnlyList<Stroke> lines, List<List<StrokePoint>> coarse, List<GeometryOps.BBox> boxes,
        Dictionary<(int, int), (double Alongside, bool Crosses)> pairs,
        Dictionary<(long, long), List<int>> grid, int line, bool atStart)
    {
        var mine = coarse[line];
        var end = atStart ? lines[line].Points[0] : lines[line].Points[^1];
        var nearest = new List<(int Line, double D)>();
        // Only the lines with a point in the cells around the end, each once.
        var candidates = new SortedSet<int>();
        var (cx, cy) = (Cell(end.X), Cell(end.Y));
        for (var x = cx - 1; x <= cx + 1; x++)
        for (var y = cy - 1; y <= cy + 1; y++)
        {
            if (grid.TryGetValue((x, y), out var cell)) candidates.UnionWith(cell);
        }
        foreach (var j in candidates)
        {
            if (j == line) continue;
            var o = coarse[j];
            var box = boxes[j];
            if (end.X < box.MinX - Reach || end.X > box.MaxX + Reach || end.Y < box.MinY - Reach || end.Y > box.MaxY + Reach) continue;
            var d = o.Count == 1 ? GeometryOps.Dist(end, o[0]) : double.MaxValue;
            for (var k = 1; k < o.Count; k++) d = Math.Min(d, GeometryOps.DistToSegment(end, o[k - 1], o[k]));
            if (d <= Reach) nearest.Add((j, d));
        }
        var within = Math.Max(3 * lines[line].Brush.Size, 10);
        (double, bool) Measured(int other)
        {
            if (!pairs.TryGetValue((line, other), out var m))
            {
                m = (Alongside(mine, coarse[other], within), Crosses(mine, coarse[other]));
                pairs[(line, other)] = m;
            }
            return m;
        }
        return [.. nearest.OrderBy(n => n.D).ThenBy(n => n.Line).Take(4)
            .Select(n => { var (along, crosses) = Measured(n.Line); return (n.Line, n.D, along, crosses); })];
    }

    /// <summary>
    /// Every line filed by the <see cref="Reach"/>-sized cells its sample points
    /// fall in, and the cells between them — a line passes through every cell
    /// it crosses, so an end finds it from its own and the eight around it.
    /// </summary>
    private static Dictionary<(long, long), List<int>> Grid(List<List<StrokePoint>> coarse)
    {
        var grid = new Dictionary<(long, long), List<int>>();
        for (var j = 0; j < coarse.Count; j++)
        {
            var filed = new HashSet<(long, long)>();
            var pts = coarse[j];
            for (var k = 0; k < pts.Count; k++)
            {
                // Samples further apart than a cell would skip one; walk between them.
                var a = k == 0 ? pts[0] : pts[k - 1];
                var b = pts[k];
                var steps = Math.Clamp((int)Math.Ceiling(GeometryOps.Dist(a, b) / Reach), 1, 64);
                for (var q = 0; q <= steps; q++)
                {
                    var at = GeometryOps.LerpPoint(a, b, (double)q / steps);
                    if (!double.IsFinite(at.X) || !double.IsFinite(at.Y)) continue;
                    var key = (Cell(at.X), Cell(at.Y));
                    if (!filed.Add(key)) continue;
                    if (!grid.TryGetValue(key, out var list)) grid[key] = list = [];
                    list.Add(j);
                }
            }
        }
        return grid;
    }

    private static long Cell(double v) => (long)Math.Floor(v / Reach);

    /// <summary>How much of a line, in pixels, lies within <paramref name="within"/> of another.</summary>
    internal static double Alongside(IReadOnlyList<StrokePoint> line, IReadOnlyList<StrokePoint> other, double within)
    {
        var total = 0.0;
        for (var i = 1; i < line.Count; i++)
        {
            var mid = GeometryOps.LerpPoint(line[i - 1], line[i], 0.5);
            var d = double.MaxValue;
            for (var k = 1; k < other.Count; k++) d = Math.Min(d, GeometryOps.DistToSegment(mid, other[k - 1], other[k]));
            if (d <= within) total += GeometryOps.Dist(line[i - 1], line[i]);
        }
        return total;
    }

    private static bool Crosses(IReadOnlyList<StrokePoint> a, IReadOnlyList<StrokePoint> b)
    {
        for (var i = 1; i < a.Count; i++)
        for (var k = 1; k < b.Count; k++)
        {
            if (LineReview.Intersect(a[i - 1], a[i], b[k - 1], b[k]) is not null) return true;
        }
        return false;
    }
}

/// <summary>
/// Makes the fixes a reviewer names (Q296–Q298): the reviewer judges which line
/// is wrong, against the keys; the engine does the geometry, inside bounds of
/// its own, and refuses what it cannot do safely.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the split.</b> A model can see that a knee's lines run on past the
/// joint where neither key has them, and cannot place a coordinate reliably —
/// the same finding that made line matching (Q270) a model naming pieces and
/// an engine drawing. So the reviewer's whole vocabulary is three verbs on
/// numbered lines, and every number is resolved through the sheet it was read
/// from.
/// </para>
/// <para>
/// <b>The keys decide (Q298).</b> A fix that cites no key line, or a line that
/// is not on the sheet, is refused, not trusted: "it looks wrong" is not a
/// reason the engine acts on.
/// </para>
/// <para>
/// <b>Past a line width, never past these bounds (Q297).</b> A trim may cut
/// further than the cleaner's line-width bound — the knee overlap is ~70 px —
/// but never more than half the line; a join spans at most
/// <see cref="MaxJoin"/>; only a short mark is removed. Nothing is added but a
/// joining end point, nothing is merged, and the result is a proposal, written
/// only by Apply.
/// </para>
/// </remarks>
public static class LineReview
{
    /// <summary>The widest gap a join closes, in pixels.</summary>
    public const double MaxJoin = 40;

    /// <summary>The most of a line a trim may take, as a fraction of its length.</summary>
    public const double MaxTrim = 0.5;

    /// <summary>The longest mark a fix may remove: this many line widths, or <see cref="MinMark"/> px.</summary>
    public const double MarkWidths = 8, MinMark = 40;

    /// <summary>A join must not turn back on either end's own direction by more than this.</summary>
    private const double MaxJoinTurnDeg = 90;

    /// <summary>How far from a mark a key's short mark has to be for the mark to be the keys' style or motion.</summary>
    private const double MarkReach = 60;

    /// <summary>Make the fixes that can be made, in the order given; report the rest.</summary>
    /// <remarks>
    /// Every bound is measured against the line <b>as drawn</b>, not as an
    /// earlier fix left it (adversary review): three trims on one line could
    /// otherwise take 85% of it, and a trim followed by a removal take a whole
    /// line. Each end joins at most once, so an end cannot walk across its
    /// neighbours one join at a time.
    /// </remarks>
    public static ReviewOutcome Apply(IReadOnlyList<Stroke> record, ReviewSheet sheet, IReadOnlyList<ReviewFix> fixes)
    {
        var points = new Dictionary<string, List<StrokePoint>>();
        var removed = new HashSet<string>();
        var taken = new Dictionary<string, double>();
        var joinedEnds = new HashSet<(string, bool)>();
        var made = new List<ReviewFix>();
        var refused = new List<RefusedFix>();
        var changes = new List<CleanupChange>();

        List<StrokePoint> Current(Stroke s) => points.TryGetValue(s.Id, out var p) ? p : [.. s.Points];
        Stroke? Line(int n) => n >= 1 && n <= sheet.Lines.Count ? sheet.Lines[n - 1] : null;

        foreach (var fix in fixes)
        {
            void Refuse(string why) => refused.Add(new RefusedFix(fix, why));

            if (fix.Line < 1 && fix.Reason.StartsWith("unreadable", StringComparison.Ordinal))
            {
                Refuse(fix.Reason);
                continue;
            }
            if (fix.Cites is not { Count: > 0 } || fix.Cites.Any(c => !sheet.Names(c)))
            {
                Refuse("It cites no key line, or one that is not on the sheet; the keys decide what is a mistake (Q298).");
                continue;
            }
            if (Line(fix.Line) is not { } line || removed.Contains(line.Id))
            {
                Refuse($"There is no line L{fix.Line} to fix.");
                continue;
            }
            var other = fix.Other is { } o ? Line(o) : null;
            if (fix.Kind != ReviewFixKind.Remove && (other is null || other.Id == line.Id || removed.Contains(other.Id)))
            {
                Refuse($"A {fix.Kind.ToString().ToLowerInvariant()} needs another line, and L{fix.Other} is not one.");
                continue;
            }
            var drawnLength = GeometryOps.PathLength(line.Points);

            switch (fix.Kind)
            {
                case ReviewFixKind.Trim:
                {
                    var pts = Current(line);
                    var oriented = fix.AtStart ? Enumerable.Reverse(pts).ToList() : pts;
                    if (TrimTo(oriented, Current(other!)) is not { } cutLine)
                    {
                        Refuse($"L{fix.Line}'s {(fix.AtStart ? "start" : "end")} does not come near L{fix.Other}.");
                        break;
                    }
                    var cutAway = GeometryOps.PathLength(oriented) - GeometryOps.PathLength(cutLine);
                    if (cutAway < 0.5) { Refuse($"L{fix.Line} already ends at L{fix.Other}."); break; }
                    var total = taken.GetValueOrDefault(line.Id) + cutAway;
                    if (total > MaxTrim * drawnLength || cutLine.Count < 2)
                    {
                        Refuse($"That would cut away {total:0} of L{fix.Line}'s {drawnLength:0} px in all — more than half the line.");
                        break;
                    }
                    cutLine = Taper(cutLine, oriented, cutAway);
                    taken[line.Id] = total;
                    points[line.Id] = fix.AtStart ? Enumerable.Reverse(cutLine).ToList() : cutLine;
                    changes.Add(new CleanupChange(line.Id, CleanupKind.Trimmed, cutAway));
                    made.Add(fix);
                    break;
                }
                case ReviewFixKind.Join:
                {
                    if (joinedEnds.Contains((line.Id, fix.AtStart)) || joinedEnds.Contains((other!.Id, fix.OtherAtStart)))
                    {
                        Refuse("An end joins once; that end has already been joined.");
                        break;
                    }
                    var a = Current(line);
                    var b = Current(other);
                    var ea = fix.AtStart ? a[0] : a[^1];
                    var eb = fix.OtherAtStart ? b[0] : b[^1];
                    var gap = GeometryOps.Dist(ea, eb);
                    if (!(gap <= MaxJoin))
                    {
                        Refuse($"The ends are {gap:0} px apart; a join closes at most {MaxJoin:0} px.");
                        break;
                    }
                    if (gap < 0.5) { Refuse("The ends already meet."); break; }
                    var mid = GeometryOps.LerpPoint(ea, eb, 0.5);
                    // Two ends that have already run past each other would close
                    // into a hairpin pointing past the joint: the spur from each
                    // end must carry on roughly the way the end was going.
                    if (TurnsBack(a, fix.AtStart, mid) || TurnsBack(b, fix.OtherAtStart, mid))
                    {
                        Refuse("Joining these ends would fold a line back on itself — trim the line that runs past the joint first.");
                        break;
                    }
                    points[line.Id] = Extended(a, fix.AtStart, mid);
                    points[other.Id] = Extended(b, fix.OtherAtStart, mid);
                    joinedEnds.Add((line.Id, fix.AtStart));
                    joinedEnds.Add((other.Id, fix.OtherAtStart));
                    changes.Add(new CleanupChange(line.Id, CleanupKind.Joined, gap / 2));
                    changes.Add(new CleanupChange(other.Id, CleanupKind.Joined, gap / 2));
                    made.Add(fix);
                    break;
                }
                case ReviewFixKind.Remove:
                {
                    var longest = Math.Max(MarkWidths * line.Brush.Size, MinMark);
                    if (drawnLength > longest)
                    {
                        Refuse($"L{fix.Line} is {drawnLength:0} px long — a line, not a mark; only marks up to {longest:0} px are removed.");
                        break;
                    }
                    if (KeyMarkNear(sheet, line, longest) is { } keyMark)
                    {
                        Refuse($"{keyMark} is a mark like it nearby — it is the keys' own mark, moving, and stays.");
                        break;
                    }
                    removed.Add(line.Id);
                    changes.Add(new CleanupChange(line.Id, CleanupKind.Removed, 0));
                    made.Add(fix);
                    break;
                }
            }
        }

        var output = new List<Stroke>(record.Count);
        foreach (var s in record)
        {
            if (removed.Contains(s.Id)) continue;
            if (!points.TryGetValue(s.Id, out var p)) { output.Add(s); continue; }
            var copy = s.Clone(newId: false);
            copy.Points = p;
            output.Add(copy);
        }
        return new ReviewOutcome(new CleanupResult(output, changes), made, refused);
    }

    /// <summary>Whether a spur from a line's end to <paramref name="to"/> doubles back on the way the end was going.</summary>
    private static bool TurnsBack(List<StrokePoint> pts, bool atStart, StrokePoint to)
    {
        var oriented = atStart ? Enumerable.Reverse(pts).ToList() : pts;
        var end = oriented[^1];
        var walked = 0.0;
        var back = oriented[0];
        for (var i = oriented.Count - 1; i > 0; i--)
        {
            walked += GeometryOps.Dist(oriented[i], oriented[i - 1]);
            back = oriented[i - 1];
            if (walked >= 6) break;
        }
        var ux = end.X - back.X; var uy = end.Y - back.Y;
        var vx = to.X - end.X; var vy = to.Y - end.Y;
        var lu = Math.Sqrt(ux * ux + uy * uy); var lv = Math.Sqrt(vx * vx + vy * vy);
        if (lu < 1e-9 || lv < 1e-9) return false;
        var cos = (ux * vx + uy * vy) / (lu * lv);
        return Math.Acos(Math.Clamp(cos, -1, 1)) * 180 / Math.PI > MaxJoinTurnDeg;
    }

    /// <summary>A key's short mark near this one, named ("A8"), or null.</summary>
    private static string? KeyMarkNear(ReviewSheet sheet, Stroke mark, double longest)
    {
        foreach (var (prefix, lines) in new[] { ("A", sheet.KeyA), ("B", sheet.KeyB) })
        {
            for (var i = 0; i < lines.Count; i++)
            {
                if (GeometryOps.PathLength(lines[i].Points) > 2 * longest) continue;
                var coarse = ReviewSheet.Coarse(lines[i].Points);
                foreach (var p in ReviewSheet.Coarse(mark.Points))
                {
                    var d = coarse.Count == 1 ? GeometryOps.Dist(p, coarse[0]) : double.MaxValue;
                    for (var k = 1; k < coarse.Count; k++) d = Math.Min(d, GeometryOps.DistToSegment(p, coarse[k - 1], coarse[k]));
                    if (d <= MarkReach) return $"{prefix}{i + 1}";
                }
            }
        }
        return null;
    }

    /// <summary>
    /// The line cut back to <paramref name="other"/>: at the crossing nearest
    /// its end, or — two lines running alongside each other past the joint —
    /// at the point nearest the other line's nearer end. Null when the line's
    /// end half never comes within reach of the other.
    /// </summary>
    private static List<StrokePoint>? TrimTo(List<StrokePoint> line, List<StrokePoint> other)
    {
        var length = GeometryOps.PathLength(line);
        // A crossing in the end half, nearest the end.
        var walked = 0.0;
        for (var i = line.Count - 1; i > 0 && walked <= length / 2; i--)
        {
            for (var k = 1; k < other.Count; k++)
            {
                if (Intersect(line[i - 1], line[i], other[k - 1], other[k]) is { } hit)
                {
                    var cut = line.GetRange(0, i);
                    cut.Add(GeometryOps.LerpPoint(line[i - 1], line[i], hit));
                    return cut;
                }
            }
            walked += GeometryOps.Dist(line[i - 1], line[i]);
        }

        // No crossing: the joint is where the other line ends. Cut at the
        // point of this line nearest that end — if it is near at all.
        var reach = 40.0;
        StrokePoint[] ends = [other[0], other[^1]];
        var best = (Index: -1, T: 0.0, D: double.MaxValue);
        foreach (var e in ends)
        {
            for (var i = 1; i < line.Count; i++)
            {
                var (t, d) = Closest(e, line[i - 1], line[i]);
                if (d < best.D) best = (i, t, d);
            }
        }
        if (best.Index < 0 || !(best.D <= reach)) return null;
        var kept = line.GetRange(0, best.Index);
        kept.Add(GeometryOps.LerpPoint(line[best.Index - 1], line[best.Index], best.T));
        return kept;
    }

    private static (double T, double D) Closest(StrokePoint p, StrokePoint a, StrokePoint b)
    {
        var dx = b.X - a.X; var dy = b.Y - a.Y;
        var len2 = dx * dx + dy * dy;
        var t = len2 == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        return (t, GeometryOps.Dist(p, GeometryOps.LerpPoint(a, b, t)));
    }

    /// <summary>Where segment a–b meets c–d, as a fraction along a–b, or null.</summary>
    internal static double? Intersect(StrokePoint a, StrokePoint b, StrokePoint c, StrokePoint d)
    {
        var rx = b.X - a.X; var ry = b.Y - a.Y;
        var sx = d.X - c.X; var sy = d.Y - c.Y;
        var den = rx * sy - ry * sx;
        if (Math.Abs(den) < 1e-12) return null;
        var t = ((c.X - a.X) * sy - (c.Y - a.Y) * sx) / den;
        var u = ((c.X - a.X) * ry - (c.Y - a.Y) * rx) / den;
        return t is >= 0 and <= 1 && u is >= 0 and <= 1 ? t : null;
    }

    /// <summary>
    /// The drawn end's lift-off moved onto the new end, over the length the
    /// drawn taper had — not over the length cut away (art-director: a 70 px
    /// trim would otherwise thin the line for 70 px into the joint, where the
    /// weight belongs).
    /// </summary>
    private static List<StrokePoint> Taper(List<StrokePoint> cut, List<StrokePoint> drawn, double cutAway)
    {
        var endPressure = drawn[^1].Pressure;
        var peak = drawn.Max(p => p.Pressure);
        // The drawn taper: from the end back to where the pressure first
        // reaches 90% of the line's peak.
        var taper = 0.0;
        for (var i = drawn.Count - 1; i > 0 && drawn[i].Pressure < 0.9 * peak; i--) taper += GeometryOps.Dist(drawn[i], drawn[i - 1]);
        var over = Math.Max(Math.Min(taper, GeometryOps.PathLength(cut) / 2), 0.5);
        cut[^1] = cut[^1] with { Pressure = endPressure };
        var walked = 0.0;
        for (var q = cut.Count - 2; q >= 0; q--)
        {
            walked += GeometryOps.Dist(cut[q], cut[q + 1]);
            if (!(walked < over)) break;
            var w = 1 - walked / over;
            cut[q] = cut[q] with { Pressure = cut[q].Pressure + (endPressure - cut[q].Pressure) * w };
        }
        return cut;
    }

    private static List<StrokePoint> Extended(List<StrokePoint> pts, bool atStart, StrokePoint to)
    {
        var copy = new List<StrokePoint>(pts);
        var end = atStart ? copy[0] : copy[^1];
        var joined = end with { X = to.X, Y = to.Y };
        if (atStart) copy.Insert(0, joined); else copy.Add(joined);
        return copy;
    }
}

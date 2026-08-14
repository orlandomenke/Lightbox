using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.Core.Inbetween;

public sealed record StrokePair(Stroke? A, Stroke? B);

/// <summary>
/// Pairs strokes of two keyframes. Identical labels win; the remainder is
/// matched at minimum total cost by centroid distance and length ratio.
/// Leftovers pair with null (they fade in/out in the inbetween).
/// </summary>
/// <remarks>
/// <para>
/// <b>Minimum total cost, not cheapest-first — B113.</b> This pass used to sort
/// every candidate pair by cost and take them in order, which is greedy and
/// gets a whole class of ordinary drawings wrong. A box drawn as four strokes
/// and moved down half its height pairs old-bottom with new-top, because that
/// is the single cheapest pair available; old-top is then left with new-bottom
/// and the top edge sweeps down through the shape. The greedy total is 120
/// against an optimal 80, so the right answer was reachable and not taken.
/// </para>
/// <para>
/// This matters more than a tidier inbetween. The deterministic engine is the
/// floor the AI path falls back to, so a wrong pairing here is wrong art with
/// or without a model — and it fails silently, which is why it survived until
/// somebody drew two boxes and watched.
/// </para>
/// <para>
/// The pairing is <em>derived</em>, never declared. Labels are a fast path when
/// they happen to exist; unlabelled art matches on geometry alone, which is the
/// property that lets an artist draw anything in any style without naming a
/// thing.
/// </para>
/// <para>
/// <b>Shape is in the cost, and it has to be added rather than multiplied.</b>
/// B113 fixed <em>which</em> assignment is chosen and left <em>what the cost
/// knows</em> alone — centroid and length, which cannot see orientation at all.
/// Two strokes crossing in an X share a centroid and a length exactly, so every
/// entry in the matrix is identical: measured on an X rotated 20°, all four
/// costs were <b>0.0000</b>. That is not a matcher choosing badly between two
/// numbers, it is a matcher with no information, and what decides the pairing
/// is the order the artist happened to draw in — listing the same two strokes
/// the other way round swapped the match. A figure's two arms are the same
/// failure less starkly: identity 41.23 against a crossed 41.37, a 0.3% margin
/// that hand jitter decides.
/// </para>
/// <para>
/// A multiplicative term cannot fix that, which is the trap worth naming: the
/// case it exists for has a centroid distance of zero, and any multiple of zero
/// is zero. So the two shape terms are distances in pixels and simply add —
/// <b>where the ends went</b> (mean endpoint displacement) and <b>which way it
/// bows</b> (<see cref="GeometryOps.SignedBow"/>). Neither carries a tuned
/// weight, because both are already measured in the same unit as the term they
/// join.
/// </para>
/// <para>
/// The endpoint term scores the <em>better</em> of the two orientations rather
/// than the literal point order, because <see cref="StrokeInterpolator"/>
/// reverses B when its ends are crossed. Penalising a backwards-drawn stroke
/// here would refuse a pairing the interpolator handles perfectly well, and an
/// artist redrawing a line from the other end is not making a different mark.
/// </para>
/// </remarks>
public static class StrokeMatcher
{
    public static List<StrokePair> Match(IReadOnlyList<Stroke> a, IReadOnlyList<Stroke> b)
    {
        var pairs = new List<StrokePair>();
        var usedB = new HashSet<string>();

        // Pass 1: label matches.
        var unmatchedA = new List<Stroke>();
        foreach (var sa in a)
        {
            var hit = sa.Label is null
                ? null
                : b.FirstOrDefault(sb => !usedB.Contains(sb.Id) && sb.Label == sa.Label);
            if (hit is not null)
            {
                usedB.Add(hit.Id);
                pairs.Add(new StrokePair(sa, hit));
            }
            else
            {
                unmatchedA.Add(sa);
            }
        }

        // Pass 2: minimum-total-cost geometric matching over the remainder.
        var remB = b.Where(sb => !usedB.Contains(sb.Id)).ToList();
        var usedA = new HashSet<string>();
        if (unmatchedA.Count > 0 && remB.Count > 0)
        {
            var cost = new double[unmatchedA.Count, remB.Count];
            for (var i = 0; i < unmatchedA.Count; i++)
            {
                var pa = unmatchedA[i].Points;
                var ca = GeometryOps.Centroid(pa);
                var la = GeometryOps.PathLength(pa);
                var bowA = GeometryOps.SignedBow(pa);
                for (var j = 0; j < remB.Count; j++)
                {
                    var pb = remB[j].Points;
                    var cb = GeometryOps.Centroid(pb);
                    var lb = GeometryOps.PathLength(pb);
                    var lenRatio = la == 0 && lb == 0
                        ? 1
                        : Math.Min(la, lb) / Math.Max(Math.Max(la, lb), 1e-6);
                    // Distance dominates; a big length mismatch inflates the cost.
                    var place = GeometryOps.Dist(ca, cb) * (2 - lenRatio);

                    // Where the ends went, and which way the stroke bows. Both
                    // are already in pixels, so they add rather than scale —
                    // see the class remarks for why scaling cannot work here.
                    double ends = 0, bow = 0;
                    if (pa.Count > 0 && pb.Count > 0)
                    {
                        var forward = GeometryOps.Dist(pa[0], pb[0]) + GeometryOps.Dist(pa[^1], pb[^1]);
                        var backward = GeometryOps.Dist(pa[0], pb[^1]) + GeometryOps.Dist(pa[^1], pb[0]);
                        // The interpolator reverses B when its ends are crossed,
                        // so score the orientation it would actually use.
                        var flipped = backward < forward;
                        ends = Math.Min(forward, backward) / 2;
                        bow = Math.Abs(bowA - (flipped ? -GeometryOps.SignedBow(pb) : GeometryOps.SignedBow(pb)));
                    }

                    cost[i, j] = place + ends + bow;
                }
            }

            var assigned = Assignment.Solve(cost);
            for (var i = 0; i < assigned.Length; i++)
            {
                if (assigned[i] < 0) continue;
                var sa = unmatchedA[i];
                var sb = remB[assigned[i]];
                usedA.Add(sa.Id);
                usedB.Add(sb.Id);
                pairs.Add(new StrokePair(sa, sb));
            }
        }

        foreach (var sa in unmatchedA)
            if (!usedA.Contains(sa.Id)) pairs.Add(new StrokePair(sa, null));
        foreach (var sb in b)
            if (!usedB.Contains(sb.Id)) pairs.Add(new StrokePair(null, sb));
        return pairs;
    }
}

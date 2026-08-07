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
                var ca = GeometryOps.Centroid(unmatchedA[i].Points);
                var la = GeometryOps.PathLength(unmatchedA[i].Points);
                for (var j = 0; j < remB.Count; j++)
                {
                    var cb = GeometryOps.Centroid(remB[j].Points);
                    var lb = GeometryOps.PathLength(remB[j].Points);
                    var lenRatio = la == 0 && lb == 0
                        ? 1
                        : Math.Min(la, lb) / Math.Max(Math.Max(la, lb), 1e-6);
                    // Distance dominates; a big length mismatch inflates the cost.
                    cost[i, j] = GeometryOps.Dist(ca, cb) * (2 - lenRatio);
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

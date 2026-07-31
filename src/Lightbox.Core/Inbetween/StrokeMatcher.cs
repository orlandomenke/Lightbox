using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.Core.Inbetween;

public sealed record StrokePair(Stroke? A, Stroke? B);

/// <summary>
/// Pairs strokes of two keyframes. Identical labels win; the remainder is
/// matched greedily by a cost combining centroid distance and length ratio.
/// Leftovers pair with null (they fade in/out in the inbetween).
/// </summary>
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

        // Pass 2: greedy geometric matching over the remainder.
        var remB = b.Where(sb => !usedB.Contains(sb.Id)).ToList();
        var candidates = new List<(Stroke Sa, Stroke Sb, double Cost)>();
        foreach (var sa in unmatchedA)
        {
            var ca = GeometryOps.Centroid(sa.Points);
            var la = GeometryOps.PathLength(sa.Points);
            foreach (var sb in remB)
            {
                var cb = GeometryOps.Centroid(sb.Points);
                var lb = GeometryOps.PathLength(sb.Points);
                var lenRatio = la == 0 && lb == 0
                    ? 1
                    : Math.Min(la, lb) / Math.Max(Math.Max(la, lb), 1e-6);
                // Distance dominates; a big length mismatch inflates the cost.
                var cost = GeometryOps.Dist(ca, cb) * (2 - lenRatio);
                candidates.Add((sa, sb, cost));
            }
        }
        candidates.Sort((x, y) => x.Cost.CompareTo(y.Cost));
        var usedA = new HashSet<string>();
        foreach (var (sa, sb, _) in candidates)
        {
            if (usedA.Contains(sa.Id) || usedB.Contains(sb.Id)) continue;
            usedA.Add(sa.Id);
            usedB.Add(sb.Id);
            pairs.Add(new StrokePair(sa, sb));
        }

        foreach (var sa in unmatchedA)
            if (!usedA.Contains(sa.Id)) pairs.Add(new StrokePair(sa, null));
        foreach (var sb in b)
            if (!usedB.Contains(sb.Id)) pairs.Add(new StrokePair(null, sb));
        return pairs;
    }
}

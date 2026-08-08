using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.Core.Inbetween;

public static class StrokeInterpolator
{
    /// <summary>Points each stroke is resampled to before interpolation.</summary>
    public const int Samples = 64;

    public static Stroke Interpolate(Stroke a, Stroke b, double t)
    {
        var pa = GeometryOps.Resample(a.Points, Samples);
        var pb = GeometryOps.Resample(b.Points, Samples);

        // Orient B to minimize travel: if matching endpoints crossed, reverse it.
        if (pa.Count > 1 && pb.Count > 1)
        {
            var forward = GeometryOps.Dist(pa[0], pb[0]) + GeometryOps.Dist(pa[^1], pb[^1]);
            var reversed = GeometryOps.Dist(pa[0], pb[^1]) + GeometryOps.Dist(pa[^1], pb[0]);
            if (reversed < forward) pb.Reverse();
        }

        var points = new List<StrokePoint>(pa.Count);
        for (var i = 0; i < pa.Count; i++)
            points.Add(GeometryOps.LerpPoint(pa[i], pb[i], t));

        return new Stroke
        {
            Tool = a.Tool,
            Color = ColorOps.LerpColor(a.Color, b.Color, t),
            Brush = new BrushSettings
            {
                Size = GeometryOps.Lerp(a.Brush.Size, b.Brush.Size, t),
                Hardness = GeometryOps.Lerp(a.Brush.Hardness, b.Brush.Hardness, t),
                Opacity = GeometryOps.Lerp(a.Brush.Opacity, b.Brush.Opacity, t),
                Spacing = GeometryOps.Lerp(a.Brush.Spacing, b.Brush.Spacing, t),
            },
            // No Path, and that is a decision rather than an omission. An
            // inbetween is generated geometry: it is resampled to a fixed count
            // and lerped point by point, so there are no authored nodes to carry
            // and no correspondence between A's nodes and B's that would say
            // where they went. Carrying either end's path would describe a line
            // that is not this one — StrokePath's invariant, and the case where
            // dropping is the only honest answer. Fitting one on demand is what
            // makes the result editable.
            Points = points,
            Label = a.Label ?? b.Label,
        };
    }
}

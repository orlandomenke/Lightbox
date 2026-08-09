using Lightbox.Core.Documents;

namespace Lightbox.Core.Geometry;

/// <summary>
/// How hard the pen was pressing, as a function of how far along the line you
/// are — so a reshaped stroke keeps the weight it was drawn with.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reshaping loses pressure without this, and it is not subtle.</b> A drawn
/// stroke carries hundreds of points, each with its own pressure; fitting it
/// produces a handful of nodes, and a node only samples the pressure at the
/// exact point it landed on. Re-flattening then interpolates linearly between
/// those few samples, so a line drawn with a confident taper at each end comes
/// back as four straight ramps. The shape survives the edit and the
/// <em>weight</em> does not.
/// </para>
/// <para>
/// That matters more than it sounds like it should, because the roadmap item
/// this serves is worded <em>"a drawn line can be re-shaped <b>and keeps the
/// mark it was drawn with</b>"</em>. Pressure is part of the mark — arguably the
/// part an animator would notice first, since it is what makes a line look
/// drawn rather than plotted.
/// </para>
/// <para>
/// <b>Parameterised by arc length, not by index.</b> An edit changes how long
/// the line is and how that length is distributed, so the profile has to stretch
/// with it: drag a node out and the taper spreads over the new distance rather
/// than bunching at the old point count. Indices would tie the weight to the
/// sampling rate, which is exactly what the flatten is free to change.
/// </para>
/// <para>
/// <b>Deterministic, like everything that reaches pixels.</b> Pressure feeds dab
/// size and flow, so a profile that resampled differently between runs would be
/// a different mark from an identical record — invariant 2, one step removed.
/// </para>
/// </remarks>
public sealed class PressureProfile
{
    private readonly double[] _at;
    private readonly double[] _pressure;

    private PressureProfile(double[] at, double[] pressure)
    {
        _at = at;
        _pressure = pressure;
    }

    /// <summary>Read the profile off a drawn line, or null if there is none to read.</summary>
    /// <remarks>
    /// Null for a line of fewer than two points, and for one with no length —
    /// a stroke that never moved has no "along" to be a function of. Callers
    /// fall back to interpolating between node pressures, which is the right
    /// answer for a path that was authored rather than drawn.
    /// </remarks>
    public static PressureProfile? Of(IReadOnlyList<StrokePoint> points)
    {
        if (points.Count < 2) return null;

        var at = new double[points.Count];
        var pressure = new double[points.Count];
        var total = 0.0;

        at[0] = 0;
        pressure[0] = points[0].Pressure;
        for (var i = 1; i < points.Count; i++)
        {
            total += Distance(points[i - 1], points[i]);
            at[i] = total;
            pressure[i] = points[i].Pressure;
        }

        if (total <= 0) return null;
        for (var i = 0; i < at.Length; i++) at[i] /= total;
        return new PressureProfile(at, pressure);
    }

    /// <summary>The pressure a fraction of the way along the original line.</summary>
    public double At(double t)
    {
        if (t <= 0) return _pressure[0];
        if (t >= 1) return _pressure[^1];

        // Binary search rather than a walk: this is called once per flattened
        // point and the profile has one entry per *drawn* point, so a linear
        // scan would make re-flattening quadratic in the length of the stroke —
        // on the path a node drag runs through.
        var lo = 0;
        var hi = _at.Length - 1;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (_at[mid] <= t) lo = mid; else hi = mid;
        }

        var span = _at[hi] - _at[lo];
        if (span <= 0) return _pressure[lo];
        var f = (t - _at[lo]) / span;
        return _pressure[lo] + (_pressure[hi] - _pressure[lo]) * f;
    }

    /// <summary>
    /// Re-weight a reshaped line: same geometry, the original stroke's pressure
    /// redistributed along it.
    /// </summary>
    public List<StrokePoint> ApplyTo(IReadOnlyList<StrokePoint> points)
    {
        if (points.Count < 2) return [.. points];

        var lengths = new double[points.Count];
        var total = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            total += Distance(points[i - 1], points[i]);
            lengths[i] = total;
        }
        // A reshaped line collapsed onto a single point has no length to
        // distribute along, so the profile cannot say anything about it.
        if (total <= 0) return [.. points];

        var reweighted = new List<StrokePoint>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            reweighted.Add(points[i] with { Pressure = At(lengths[i] / total) });
        }
        return reweighted;
    }

    private static double Distance(StrokePoint a, StrokePoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

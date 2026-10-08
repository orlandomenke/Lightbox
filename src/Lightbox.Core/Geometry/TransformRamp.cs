namespace Lightbox.Core.Geometry;

/// <summary>How a ramp's share grows from the first drawing to the last.</summary>
public enum RampEase
{
    /// <summary>The same step between every pair of drawings.</summary>
    Linear,

    /// <summary>Starts slow and speeds up — a push-in that gathers pace.</summary>
    EaseIn,

    /// <summary>Starts fast and settles — a move that lands.</summary>
    EaseOut,

    /// <summary>Slow at both ends — the animator's default slow-in, slow-out.</summary>
    EaseInOut,
}

/// <summary>
/// The gizmo's affine box in the parts it is built from: a pivot, a scale, a
/// rotation and an offset, composed the way <see cref="TransformOps.Affine"/>
/// composes them.
/// </summary>
/// <remarks>
/// <b>Parts rather than a matrix, because a ramp interpolates.</b> Blending two
/// matrices entry by entry shrinks a rotation through its middle — a quarter
/// turn passes through a squashed drawing at half-way — so each part is eased
/// on its own and the matrix is rebuilt from the result (Q216).
/// </remarks>
public readonly record struct AffineParts(
    double PivotX, double PivotY,
    double ScaleX, double ScaleY,
    double Angle,
    double OffsetX, double OffsetY)
{
    /// <summary>
    /// This box with <paramref name="share"/> of it applied: nothing at 0, all
    /// of it at 1. The pivot stays where it is — it is the point the drawings
    /// scale and turn round, and moving it along the ramp would be a second
    /// motion nobody asked for.
    /// </summary>
    public AffineParts At(double share) => new(
        PivotX, PivotY,
        1 + ((ScaleX - 1) * share),
        1 + ((ScaleY - 1) * share),
        Angle * share,
        OffsetX * share,
        OffsetY * share);

    public TransformOps.PointMap Map() =>
        TransformOps.Affine(PivotX, PivotY, ScaleX, ScaleY, Angle, OffsetX, OffsetY);

    /// <summary>What a brush size multiplies by — the same rule the plain commit uses.</summary>
    public double SizeScale => Math.Sqrt(Math.Abs(ScaleX * ScaleY));

    /// <summary>
    /// A mirror cannot be ramped: a scale that changes sign passes through
    /// zero on its way, and the drawing there is a line.
    /// </summary>
    public bool Mirrors => ScaleX <= 0 || ScaleY <= 0;

    public bool IsIdentity =>
        ScaleX == 1 && ScaleY == 1 && Angle == 0 && OffsetX == 0 && OffsetY == 0;
}

/// <summary>
/// The arithmetic of a transform that ramps over a sequence (Q216): which share
/// of the transform each drawing gets, eased.
/// </summary>
/// <remarks>
/// <para>
/// <b>A share belongs to a position in time, not to a drawing.</b> The first
/// drawing in the range gets none of the transform and the last gets all of it;
/// the ones between get the fraction of the way they sit from the first
/// exposure to the last. A drawing held across several frames takes the share of
/// the frame it starts on, so a cycle on 2s still moves on 2s — the timing the
/// artist chose is kept, which is Q216's answer on holds.
/// </para>
/// <para>
/// Arithmetic only: no model, no randomness, and nothing here touches a stroke.
/// </para>
/// </remarks>
public static class TransformRamp
{
    /// <summary>Apply an ease to a linear fraction in [0, 1].</summary>
    public static double Ease(RampEase ease, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return ease switch
        {
            RampEase.EaseIn => t * t,
            RampEase.EaseOut => 1 - ((1 - t) * (1 - t)),
            RampEase.EaseInOut => t * t * (3 - (2 * t)),
            _ => t,
        };
    }

    /// <summary>
    /// The share each exposure position gets, from the first position (0) to
    /// the last (1). Positions are cel indices where a drawing starts showing;
    /// duplicates are ignored. A single position gets all of it.
    /// </summary>
    public static IReadOnlyDictionary<int, double> Shares(IEnumerable<int> positions, RampEase ease)
    {
        var sorted = positions.Distinct().Order().ToList();
        var shares = new Dictionary<int, double>(sorted.Count);
        if (sorted.Count == 0) return shares;
        if (sorted.Count == 1)
        {
            shares[sorted[0]] = 1;
            return shares;
        }
        double first = sorted[0], span = sorted[^1] - sorted[0];
        foreach (var p in sorted) shares[p] = Ease(ease, (p - first) / span);
        return shares;
    }
}

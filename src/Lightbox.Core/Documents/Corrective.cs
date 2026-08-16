namespace Lightbox.Core.Documents;

/// <summary>One control point's departure from where it was drawn, in rest space.</summary>
public readonly record struct PointOffset(double X, double Y);

/// <summary>What one stroke's points do at a corrective's stop.</summary>
/// <remarks>
/// Dense over the stroke's control points, and matched by <em>count</em> at
/// solve time: a stroke that has since been re-shaped no longer describes the
/// same points, so its offsets are dropped rather than applied to whichever
/// points happen to line up. Only strokes the artist actually moved appear.
/// </remarks>
public sealed class StrokeCorrection
{
    public string StrokeId { get; set; } = "";

    public List<PointOffset> Offsets { get; set; } = [];

    /// <summary>A copy holding no reference in common with this one.</summary>
    public StrokeCorrection Clone()
    {
        var copy = (StrokeCorrection)MemberwiseClone();
        copy.Offsets = [.. Offsets];
        return copy;
    }
}

/// <summary>The drawing's shape at one angle of the driving joint.</summary>
public sealed class CorrectiveStop
{
    /// <summary>The driver bone's pose rotation this shape was drawn at, in degrees.</summary>
    public double AngleDeg { get; set; }

    public List<StrokeCorrection> Strokes { get; set; } = [];

    /// <summary>A copy holding no reference in common with this one.</summary>
    public CorrectiveStop Clone()
    {
        var copy = (CorrectiveStop)MemberwiseClone();
        copy.Strokes = Strokes.Select(s => s.Clone()).ToList();
        return copy;
    }
}

/// <summary>
/// A shape the artist drew at a joint extreme, blended in as that joint
/// approaches it — Moho's "smart bones", phase 4 of <c>docs/DESIGN-bones.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it is for.</b> Linear-blend skinning collapses the inside of a
/// sharply bent joint and pinches the outside. No weight painting fixes that,
/// because the correct shape at 120° is not a linear blend of anything. So the
/// artist draws the correct shape once and it eases in.
/// </para>
/// <para>
/// <b>Rest space, before skinning</b> (Q100). The offsets move the rest shape
/// and the rig then carries the corrected shape into pose — so a corrective
/// composes with IK, spline chains, constraints and the pose itself for
/// nothing, and none of them need to know it exists. Posed-space offsets would
/// have been trivial to author and wrong the moment a parent moved: a fix made
/// with the shoulder down would not rotate when the shoulder lifted.
/// </para>
/// <para>
/// <b>Rest is an implicit stop at 0°.</b> A corrective is a *departure* from
/// the drawing, so an unbent joint corrects nothing without the artist having
/// to author a stop full of zeroes — unless they author one at 0° themselves,
/// which is how a drawing that needs fixing at rest says so.
/// </para>
/// </remarks>
public sealed class Corrective
{
    public string Id { get; set; } = Ids.NewId("fix");

    public string Name { get; set; } = "Corrective";

    /// <summary>The bone whose pose rotation drives the blend.</summary>
    public string DriverBoneId { get; set; } = "";

    /// <summary>The authored shapes, in no guaranteed order — read through <see cref="CorrectiveOps"/>.</summary>
    public List<CorrectiveStop> Stops { get; set; } = [];

    /// <summary>A copy holding no reference in common with this one.</summary>
    public Corrective Clone()
    {
        var copy = (Corrective)MemberwiseClone();
        copy.Stops = Stops.Select(s => s.Clone()).ToList();
        return copy;
    }
}

/// <summary>Evaluating correctives: which offsets apply at a pose.</summary>
/// <remarks>
/// Deterministic by construction — doubles, a fixed evaluation order, no state
/// but the arguments — for the reason every part of the rig is: a corrective is
/// replayed on load, on undo and by the inbetweener, and two evaluations of the
/// same pose must agree to the bit.
/// </remarks>
public static class CorrectiveOps
{
    /// <summary>
    /// The offsets in force for a drawing at a pose: stroke id → one offset per
    /// control point. Empty when nothing corrects, which is every ordinary
    /// drawing.
    /// </summary>
    /// <remarks>
    /// Stops are read in angle order and ties keep authoring order, so the
    /// answer does not depend on how the list happens to be stored. Several
    /// correctives on one drawing <b>add</b>: an elbow fix and a wrist fix are
    /// independent departures, and summing is the only combination that does
    /// not make one of them quietly conditional on the other.
    /// </remarks>
    public static Dictionary<string, PointOffset[]> Resolve(
        IReadOnlyList<Corrective>? correctives,
        IReadOnlyDictionary<string, BonePose>? pose)
    {
        var result = new Dictionary<string, PointOffset[]>();
        if (correctives is not { Count: > 0 }) return result;

        foreach (var corrective in correctives)
        {
            var angle = pose?.GetValueOrDefault(corrective.DriverBoneId)?.RotationDeg ?? 0;
            foreach (var (strokeId, offsets) in At(corrective, angle))
            {
                if (!result.TryGetValue(strokeId, out var running))
                {
                    result[strokeId] = offsets;
                    continue;
                }
                // A second corrective on the same stroke adds to the first, and
                // only where both describe the same points.
                if (running.Length != offsets.Length) continue;
                for (var i = 0; i < running.Length; i++)
                    running[i] = new PointOffset(running[i].X + offsets[i].X, running[i].Y + offsets[i].Y);
            }
        }
        return result;
    }

    /// <summary>One corrective's offsets at a driver angle.</summary>
    /// <remarks>
    /// The implicit rest stop is <b>materialised</b> rather than special-cased.
    /// Written as a branch it needed four of them and each was a place to be
    /// subtly wrong about which side of zero an angle fell; as a stop in the
    /// list the whole evaluation is a clamp and a bracket, which is a thing a
    /// reader can check by eye.
    /// </remarks>
    public static Dictionary<string, PointOffset[]> At(Corrective corrective, double angleDeg)
    {
        if (corrective.Stops.Count == 0) return [];

        // OrderBy is stable, so stops authored at the same angle keep the order
        // they were written in and the answer does not depend on storage.
        var stops = corrective.Stops.OrderBy(s => s.AngleDeg).ToList();
        if (!stops.Any(s => Math.Abs(s.AngleDeg) < 1e-9))
            stops = stops.Concat([new CorrectiveStop { AngleDeg = 0 }]).OrderBy(s => s.AngleDeg).ToList();

        // Held outside the authored range — the pose track's semantics, and the
        // reason a limb bent past the extreme stays fixed rather than sliding
        // back toward the shape that was wrong.
        if (angleDeg <= stops[0].AngleDeg) return Scaled(stops[0]);
        if (angleDeg >= stops[^1].AngleDeg) return Scaled(stops[^1]);

        for (var i = 0; i < stops.Count - 1; i++)
        {
            var (below, above) = (stops[i], stops[i + 1]);
            if (angleDeg < below.AngleDeg || angleDeg > above.AngleDeg) continue;
            var span = above.AngleDeg - below.AngleDeg;
            return Mix(below, above, span <= 0 ? 1 : (angleDeg - below.AngleDeg) / span);
        }
        return Scaled(stops[^1]);
    }

    private static Dictionary<string, PointOffset[]> Scaled(CorrectiveStop stop)
    {
        var result = new Dictionary<string, PointOffset[]>();
        foreach (var stroke in stop.Strokes) result[stroke.StrokeId] = [.. stroke.Offsets];
        return result;
    }

    /// <summary>
    /// Blend two stops. A stroke named by only one of them ramps from nothing,
    /// which is what lets an artist add a fix to one line at a second stop
    /// without having to restate every line at the first.
    /// </summary>
    private static Dictionary<string, PointOffset[]> Mix(CorrectiveStop a, CorrectiveStop b, double t)
    {
        var result = new Dictionary<string, PointOffset[]>();
        foreach (var stroke in a.Strokes) result[stroke.StrokeId] = Blend(stroke.Offsets, null, t);
        foreach (var stroke in b.Strokes)
        {
            var from = a.Strokes.FirstOrDefault(s => s.StrokeId == stroke.StrokeId);
            result[stroke.StrokeId] = Blend(from?.Offsets, stroke.Offsets, t);
        }
        return result;
    }

    private static PointOffset[] Blend(List<PointOffset>? from, List<PointOffset>? to, double t)
    {
        var count = Math.Max(from?.Count ?? 0, to?.Count ?? 0);
        var offsets = new PointOffset[count];
        for (var i = 0; i < count; i++)
        {
            var a = from is not null && i < from.Count ? from[i] : default;
            var b = to is not null && i < to.Count ? to[i] : default;
            offsets[i] = new PointOffset(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }
        return offsets;
    }
}

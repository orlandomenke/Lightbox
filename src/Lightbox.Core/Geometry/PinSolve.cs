namespace Lightbox.Core.Geometry;

using Lightbox.Core.Documents;

/// <summary>What a pin does to the mesh under it.</summary>
public enum PinKind
{
    /// <summary>Holds its point of the drawing where the pose puts it. The joint.</summary>
    Position,

    /// <summary>Moves nothing; keeps the mesh within its extent from bending.</summary>
    Starch,
}

/// <summary>
/// One pin as the solve sees it: where it was dropped on the rest drawing,
/// what kind it is and, for starch, how far it reaches.
/// </summary>
public readonly record struct PinHandle(double X, double Y, PinKind Kind = PinKind.Position, double Extent = 0);

/// <summary>
/// The as-rigid-as-possible solve for a pinned <see cref="PinMesh"/>:
/// factorised once when the pins are placed, then a pair of
/// back-substitutions per drag (Q239, <c>docs/DESIGN-puppet-pins.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Not built yet.</b> Every member throws; <c>PinSolveTests</c> and
/// <c>PinSolvePerformanceTests</c> are red on purpose.
/// </para>
/// <para>
/// <b>The split is the performance design.</b> Everything that depends only
/// on the mesh and on where the pins were dropped is paid in
/// <see cref="Prepare"/>, once; <c>Solve</c>
/// runs per pointer event and per rendered frame, writes into the caller's
/// buffer and allocates nothing.
/// </para>
/// <para>
/// <b>Deterministic by construction</b>: a direct factorisation in doubles
/// with a fixed elimination order and a fixed number of local/global rounds
/// — never iterate-to-tolerance — so the same pins give the same vertices to
/// the bit on every machine (invariant 2, and the bones design's determinism
/// rules).
/// </para>
/// </remarks>
public sealed class PinSolve
{
    private const string Pending = "Puppet pins are designed and not built: docs/DESIGN-puppet-pins.md.";

    private PinSolve() { }

    /// <summary>
    /// Factorise for a mesh and the pins dropped on it. A pin off the mesh
    /// takes the nearest triangle; a piece of the mesh holding no position
    /// pin is left out of the system and stays at rest.
    /// </summary>
    public static PinSolve Prepare(PinMesh mesh, IReadOnlyList<PinHandle> pins) =>
        throw new NotImplementedException(Pending);

    /// <summary>How many vertices a solve writes.</summary>
    public int VertexCount => throw new NotImplementedException(Pending);

    /// <summary>
    /// Where every vertex stands when each pin is at its target — one target
    /// per pin, in the order the pins were prepared. A starch pin's target is
    /// ignored, and a target that is not finite leaves its pin at rest. When
    /// no pin has left home the rest positions are copied without solving, so
    /// an untouched puppet is the identity to the last bit.
    /// </summary>
    public void Solve(ReadOnlySpan<(double X, double Y)> targets, Span<(double X, double Y)> deformed) =>
        throw new NotImplementedException(Pending);

    /// <summary>The same solve into a new array. Convenience for tests and one-off callers.</summary>
    public (double X, double Y)[] Solve(IReadOnlyList<(double X, double Y)> targets) =>
        throw new NotImplementedException(Pending);

    /// <summary>
    /// Where a rest-space point lands on a deformed mesh: its
    /// <see cref="PinMesh.Embedding"/> applied to the moved corners.
    /// </summary>
    public static (double X, double Y) MapPoint(
        PinMesh mesh, ReadOnlySpan<(double X, double Y)> deformed, double x, double y) =>
        throw new NotImplementedException(Pending);
}

/// <summary>
/// Carrying strokes on a deformed <see cref="PinMesh"/> — the pin half of
/// what <see cref="Skinning"/> does for bones, under the same rule: the
/// stroke's points move and the brush re-stamps them; the picture is never
/// warped.
/// </summary>
public static class PinSkinning
{
    private const string Pending = "Puppet pins are designed and not built: docs/DESIGN-puppet-pins.md.";

    /// <summary>
    /// A render-ready copy of a stroke on a deformed mesh: moved
    /// <see cref="Stroke.Points"/>, the rest path in
    /// <see cref="Stroke.RestPoints"/> so every dab seeds where it was drawn
    /// and the mark does not boil, the same id. Densified before it is moved,
    /// so a straight line across a bend comes out as a curve. Returns the
    /// stroke itself, uncloned, when the mesh has not moved.
    /// </summary>
    public static Stroke PoseStroke(
        Stroke stroke, PinMesh mesh, ReadOnlySpan<(double X, double Y)> deformed) =>
        throw new NotImplementedException(Pending);
}

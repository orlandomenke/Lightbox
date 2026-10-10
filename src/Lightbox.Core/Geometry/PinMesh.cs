namespace Lightbox.Core.Geometry;

using Lightbox.Core.Documents;

/// <summary>
/// The mesh a puppet pin deforms: a regular triangle grid clipped to a
/// drawing's silhouette (Q239, <c>docs/DESIGN-puppet-pins.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Not built yet.</b> This is the surface the design note and
/// <c>PinMeshTests</c> agreed on, with nothing behind it: every member throws
/// until the implementation branch lands, and the tests are red on purpose.
/// </para>
/// <para>
/// <b>Why a clipped grid and not a triangulated outline.</b> A pin in the arm
/// must not pull the leg, so the mesh has to know the drawing's shape — but
/// line art has no filled silhouette to trace. The grid marks the cells ink
/// touches, floods the rest from outside, and keeps everything the flood
/// could not reach: the inside of a closed outline is inside, a gap narrower
/// than a cell is closed without being asked, and two shapes more than a cell
/// apart share no triangle. It is also regular, which is what makes the
/// solve banded and the lookup of a point's triangle a division rather than a
/// search.
/// </para>
/// <para>
/// <b>Derived, never stored.</b> The mesh is a pure function of the strokes
/// and the cell size — doubles in a fixed order, no tolerance loops
/// (invariant 2) — so the document records the cell size and the pins, and a
/// reload rebuilds the same mesh to the bit.
/// </para>
/// </remarks>
public sealed class PinMesh
{
    private const string Pending = "Puppet pins are designed and not built: docs/DESIGN-puppet-pins.md.";

    /// <summary>The finest grid a mesh may have, in document units.</summary>
    public const double MinCell = 4;

    /// <summary>The coarsest grid a mesh may have.</summary>
    public const double MaxCell = 128;

    /// <summary>Fine enough to bend a limb on a 1080p character, coarse enough to solve in a fraction of a frame.</summary>
    public const double DefaultCell = 24;

    /// <summary>
    /// The most vertices a mesh may hold. A drawing too large for its cell
    /// size gets a coarser grid rather than a bigger solve — the cap is what
    /// keeps a drag bounded work (invariant 6) whatever the canvas.
    /// </summary>
    public const int MaxVertices = 4000;

    private PinMesh() { }

    /// <summary>
    /// The mesh over a drawing: every cell ink touches or encloses, at
    /// <paramref name="cell"/> spacing — clamped to
    /// <see cref="MinCell"/>..<see cref="MaxCell"/>, and raised further when
    /// the drawing would otherwise exceed <see cref="MaxVertices"/>.
    /// </summary>
    public static PinMesh Build(IReadOnlyList<Stroke> strokes, double cell = DefaultCell) =>
        throw new NotImplementedException(Pending);

    /// <summary>The grid spacing actually used, after clamping and the vertex cap.</summary>
    public double Cell => throw new NotImplementedException(Pending);

    /// <summary>Where every vertex stands at rest, in document coordinates.</summary>
    public IReadOnlyList<(double X, double Y)> Vertices => throw new NotImplementedException(Pending);

    /// <summary>Vertex indices, three per triangle, wound the same way throughout.</summary>
    public IReadOnlyList<int> Triangles => throw new NotImplementedException(Pending);

    /// <summary>
    /// The edges on the silhouette — the ones only one triangle owns. What the
    /// overlay draws when the mesh is shown "as lines".
    /// </summary>
    public IReadOnlyList<(int A, int B)> Outline => throw new NotImplementedException(Pending);

    /// <summary>Whether the drawing had nothing to mesh.</summary>
    public bool IsEmpty => throw new NotImplementedException(Pending);

    /// <summary>How many separate pieces the mesh is in.</summary>
    public int ComponentCount => throw new NotImplementedException(Pending);

    /// <summary>Which piece a vertex belongs to, 0-based.</summary>
    public int ComponentOf(int vertex) => throw new NotImplementedException(Pending);

    /// <summary>Whether a point lies inside a triangle of the mesh.</summary>
    public bool Contains(double x, double y) => throw new NotImplementedException(Pending);

    /// <summary>The vertex nearest a point.</summary>
    public int NearestVertex(double x, double y) => throw new NotImplementedException(Pending);

    /// <summary>
    /// A point expressed in the mesh: its triangle and its barycentric
    /// weights there. A point outside the mesh takes the nearest triangle and
    /// extrapolates, so a brush's reach past the silhouette still follows.
    /// </summary>
    public Embedding Embed(double x, double y) => throw new NotImplementedException(Pending);

    /// <summary>A point's place in a triangle: weights on its three corners, summing to one.</summary>
    public readonly record struct Embedding(int Triangle, double U, double V, double W);
}

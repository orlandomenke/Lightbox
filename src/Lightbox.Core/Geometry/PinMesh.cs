namespace Lightbox.Core.Geometry;

using Lightbox.Core.Documents;

/// <summary>
/// The mesh a puppet pin deforms: a regular triangle grid clipped to a
/// drawing's silhouette (Q239, <c>docs/DESIGN-puppet-pins.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a clipped grid and not a triangulated outline.</b> A pin in the arm
/// must not pull the leg, so the mesh has to know the drawing's shape — but
/// line art has no filled silhouette to trace. The grid marks the cells a
/// stroke's path passes through, floods the rest from outside, and keeps
/// everything the flood could not reach: the inside of a closed outline is
/// inside, a gap narrower than a cell is closed without being asked, and two
/// shapes more than a cell apart share no triangle. It is also regular, which
/// is what makes the solve banded and the lookup of a point's triangle a
/// division rather than a search.
/// </para>
/// <para>
/// <b>The path, not the brush.</b> Cells are marked by where the pen went,
/// never by how wide the brush was: a brush's reach past its centreline rides
/// the nearest triangle (<see cref="Embed"/> extrapolates), and a mesh that
/// swelled with brush size would join two limbs the artist drew apart.
/// </para>
/// <para>
/// <b>Derived, never stored.</b> The mesh is a pure function of the strokes
/// and the cell size — doubles in a fixed order, no tolerance loops
/// (invariant 2) — so the document records the cell size and the pins, and a
/// reload rebuilds the same mesh to the bit. The grid is aligned to the
/// document's origin rather than to the drawing's corner, so two drawings of
/// the same figure a few pixels apart get cells in the same places.
/// </para>
/// </remarks>
public sealed class PinMesh
{
    /// <summary>The finest grid a mesh may have, in document units.</summary>
    public const double MinCell = 4;

    /// <summary>The coarsest grid a mesh may be asked for.</summary>
    public const double MaxCell = 128;

    /// <summary>Fine enough to bend a limb on a 1080p character, coarse enough to solve in a fraction of a frame.</summary>
    public const double DefaultCell = 24;

    /// <summary>
    /// The most vertices a mesh may hold. A drawing too large for its cell
    /// size gets a coarser grid rather than a bigger solve — the cap is what
    /// keeps a drag bounded work (invariant 6) whatever the canvas.
    /// </summary>
    public const int MaxVertices = 4000;

    private static readonly PinMesh Empty = new(
        DefaultCell, 0, 0, 0, 0, [], [], [], [], [], [], 0);

    private readonly (double X, double Y)[] _vertices;
    private readonly int[] _triangles;
    private readonly (int A, int B)[] _outline;
    private readonly int[] _component;

    // The grid the mesh was cut from: cell (i, j) covers
    // [originX + i·cell, originX + (i+1)·cell) × the same in y. _cellTriangle
    // is the index of the cell's first triangle (its second follows), or -1.
    private readonly double _originX, _originY;
    private readonly int _cols, _rows;
    private readonly int[] _cellTriangle;

    private PinMesh(
        double cell, double originX, double originY, int cols, int rows,
        (double X, double Y)[] vertices, int[] triangles, (int A, int B)[] outline,
        int[] component, int[] cellTriangle, int[] vertexGrid, int componentCount)
    {
        Cell = cell;
        _originX = originX;
        _originY = originY;
        _cols = cols;
        _rows = rows;
        _vertices = vertices;
        _triangles = triangles;
        _outline = outline;
        _component = component;
        _cellTriangle = cellTriangle;
        VertexGrid = vertexGrid;
        ComponentCount = componentCount;
    }

    /// <summary>
    /// The mesh over a drawing: every cell a stroke's path crosses or
    /// encloses, at <paramref name="cell"/> spacing — clamped to
    /// <see cref="MinCell"/>..<see cref="MaxCell"/>, and raised further when
    /// the drawing would otherwise exceed <see cref="MaxVertices"/>.
    /// </summary>
    public static PinMesh Build(IReadOnlyList<Stroke> strokes, double cell = DefaultCell)
    {
        cell = double.IsFinite(cell) ? Math.Clamp(cell, MinCell, MaxCell) : DefaultCell;

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var any = false;
        foreach (var stroke in strokes)
        {
            Extend(stroke.Points);
            if (stroke.Holes is { } holes)
                foreach (var hole in holes) Extend(hole);
        }
        if (!any) return Empty;

        // A first guess from the bounding box, so a 4K figure at the finest
        // cell does not allocate a million cells to learn it is too big.
        var boxVertices = ((maxX - minX) / cell + 2) * ((maxY - minY) / cell + 2);
        if (boxVertices > MaxVertices * 8.0) cell *= Math.Sqrt(boxVertices / (MaxVertices * 8.0));

        // Then exactly: build, count, and coarsen by the overshoot. A fixed
        // number of rounds; the last one always fits because the step is
        // taken from the count itself with a margin.
        PinMesh mesh;
        for (var round = 0; ; round++)
        {
            mesh = BuildAt(strokes, cell, minX, minY, maxX, maxY);
            if (mesh._vertices.Length <= MaxVertices || round == 12) break;
            cell *= Math.Sqrt((double)mesh._vertices.Length / MaxVertices) * 1.03;
        }
        return mesh;

        void Extend(IReadOnlyList<StrokePoint> points)
        {
            foreach (var p in points)
            {
                if (!double.IsFinite(p.X) || !double.IsFinite(p.Y)) continue;
                any = true;
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
        }
    }

    private static PinMesh BuildAt(
        IReadOnlyList<Stroke> strokes, double cell, double minX, double minY, double maxX, double maxY)
    {
        // One empty cell all round, so the flood has somewhere to start.
        var i0 = (long)Math.Floor(minX / cell) - 1;
        var j0 = (long)Math.Floor(minY / cell) - 1;
        var cols = (int)((long)Math.Floor(maxX / cell) + 1 - i0 + 1);
        var rows = (int)((long)Math.Floor(maxY / cell) + 1 - j0 + 1);
        var originX = i0 * cell;
        var originY = j0 * cell;

        // 1 = a path crosses it, 2 = the flood reached it from outside.
        var state = new byte[cols * rows];
        foreach (var stroke in strokes)
        {
            MarkPath(stroke.Points, closed: stroke.Tool.FillsAContour());
            if (stroke.Holes is { } holes)
                foreach (var hole in holes) MarkPath(hole, closed: true);
        }

        var stack = new Stack<int>();
        for (var i = 0; i < cols; i++)
        {
            Seed(i);
            Seed((rows - 1) * cols + i);
        }
        for (var j = 0; j < rows; j++)
        {
            Seed(j * cols);
            Seed(j * cols + cols - 1);
        }
        while (stack.Count > 0)
        {
            var c = stack.Pop();
            var (ci, cj) = (c % cols, c / cols);
            if (ci > 0) Seed(c - 1);
            if (ci < cols - 1) Seed(c + 1);
            if (cj > 0) Seed(c - cols);
            if (cj < rows - 1) Seed(c + cols);
        }

        // Vertices in reading order of the grid, so the numbering depends on
        // nothing but the cells kept.
        var vertexGrid = new int[(cols + 1) * (rows + 1)];
        Array.Fill(vertexGrid, -1);
        var kept = 0;
        for (var c = 0; c < state.Length; c++)
        {
            if (state[c] == 2) continue;
            kept++;
            var (ci, cj) = (c % cols, c / cols);
            vertexGrid[cj * (cols + 1) + ci] = 0;
            vertexGrid[cj * (cols + 1) + ci + 1] = 0;
            vertexGrid[(cj + 1) * (cols + 1) + ci] = 0;
            vertexGrid[(cj + 1) * (cols + 1) + ci + 1] = 0;
        }
        var vertices = new List<(double X, double Y)>();
        for (var g = 0; g < vertexGrid.Length; g++)
        {
            if (vertexGrid[g] < 0) continue;
            vertexGrid[g] = vertices.Count;
            vertices.Add((originX + g % (cols + 1) * cell, originY + g / (cols + 1) * cell));
        }

        var triangles = new int[kept * 6];
        var cellTriangle = new int[state.Length];
        Array.Fill(cellTriangle, -1);
        var t = 0;
        for (var c = 0; c < state.Length; c++)
        {
            if (state[c] == 2) continue;
            var (ci, cj) = (c % cols, c / cols);
            var v00 = vertexGrid[cj * (cols + 1) + ci];
            var v10 = vertexGrid[cj * (cols + 1) + ci + 1];
            var v01 = vertexGrid[(cj + 1) * (cols + 1) + ci];
            var v11 = vertexGrid[(cj + 1) * (cols + 1) + ci + 1];
            cellTriangle[c] = t / 3;
            // The cell's diagonal runs from its first corner to its last; the
            // two triangles are wound the same way.
            triangles[t++] = v00; triangles[t++] = v10; triangles[t++] = v11;
            triangles[t++] = v00; triangles[t++] = v11; triangles[t++] = v01;
        }

        // The silhouette: a cell side with a kept cell on one side only.
        var outline = new List<(int A, int B)>();
        for (var c = 0; c < state.Length; c++)
        {
            if (state[c] == 2) continue;
            var (ci, cj) = (c % cols, c / cols);
            var v00 = vertexGrid[cj * (cols + 1) + ci];
            var v10 = vertexGrid[cj * (cols + 1) + ci + 1];
            var v01 = vertexGrid[(cj + 1) * (cols + 1) + ci];
            var v11 = vertexGrid[(cj + 1) * (cols + 1) + ci + 1];
            if (cj == 0 || state[c - cols] == 2) outline.Add((v00, v10));
            if (ci == cols - 1 || state[c + 1] == 2) outline.Add((v10, v11));
            if (cj == rows - 1 || state[c + cols] == 2) outline.Add((v11, v01));
            if (ci == 0 || state[c - 1] == 2) outline.Add((v01, v00));
        }

        // Pieces: vertices joined through any triangle. Numbered by the
        // lowest vertex in each, so the numbering is the grid's too.
        var parent = new int[vertices.Count];
        for (var v = 0; v < parent.Length; v++) parent[v] = v;
        for (var k = 0; k < triangles.Length; k += 3)
        {
            Union(triangles[k], triangles[k + 1]);
            Union(triangles[k], triangles[k + 2]);
        }
        var component = new int[vertices.Count];
        var names = new Dictionary<int, int>();
        for (var v = 0; v < component.Length; v++)
        {
            var root = Find(v);
            if (!names.TryGetValue(root, out var name)) names[root] = name = names.Count;
            component[v] = name;
        }

        return new PinMesh(
            cell, originX, originY, cols, rows, [.. vertices], triangles, [.. outline],
            component, cellTriangle, vertexGrid, names.Count);

        void Seed(int c)
        {
            if (state[c] != 0) return;
            state[c] = 2;
            stack.Push(c);
        }

        int Find(int v)
        {
            while (parent[v] != v)
            {
                parent[v] = parent[parent[v]];
                v = parent[v];
            }
            return v;
        }

        void Union(int a, int b)
        {
            var (ra, rb) = (Find(a), Find(b));
            if (ra == rb) return;
            if (ra < rb) parent[rb] = ra;
            else parent[ra] = rb;
        }

        void Mark(double x, double y)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y)) return;
            var ci = (int)((long)Math.Floor(x / cell) - i0);
            var cj = (int)((long)Math.Floor(y / cell) - j0);
            if (ci < 0 || cj < 0 || ci >= cols || cj >= rows) return;
            state[cj * cols + ci] = 1;
        }

        void MarkPath(IReadOnlyList<StrokePoint> points, bool closed)
        {
            if (points.Count == 0) return;
            Mark(points[0].X, points[0].Y);
            var last = closed ? points.Count : points.Count - 1;
            for (var k = 0; k < last; k++)
            {
                var a = points[k];
                var b = points[(k + 1) % points.Count];
                var (dx, dy) = (b.X - a.X, b.Y - a.Y);
                if (!double.IsFinite(dx) || !double.IsFinite(dy)) continue;
                // Sampled at a quarter cell: a line cannot cross a cell
                // without a sample landing in it or in one beside it, which
                // is all a four-way flood needs to be stopped.
                var steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy) / (cell / 4)));
                for (var s = 1; s <= steps; s++) Mark(a.X + dx * s / steps, a.Y + dy * s / steps);
            }
        }
    }

    /// <summary>The grid spacing actually used, after clamping and the vertex cap.</summary>
    public double Cell { get; }

    /// <summary>Where every vertex stands at rest, in document coordinates.</summary>
    public IReadOnlyList<(double X, double Y)> Vertices => _vertices;

    /// <summary>Vertex indices, three per triangle, wound the same way throughout.</summary>
    public IReadOnlyList<int> Triangles => _triangles;

    /// <summary>
    /// The edges on the silhouette — the cell sides with the drawing on one
    /// side only. What the overlay draws when the mesh is shown "as lines".
    /// </summary>
    public IReadOnlyList<(int A, int B)> Outline => _outline;

    /// <summary>Whether the drawing had nothing to mesh.</summary>
    public bool IsEmpty => _vertices.Length == 0;

    /// <summary>How many separate pieces the mesh is in.</summary>
    public int ComponentCount { get; }

    /// <summary>Which piece a vertex belongs to, 0-based.</summary>
    public int ComponentOf(int vertex) => _component[vertex];

    /// <summary>Cells across the grid the mesh was cut from. For the solver's ordering.</summary>
    internal int GridCols => _cols;

    /// <summary>Cells down the grid the mesh was cut from.</summary>
    internal int GridRows => _rows;

    /// <summary>Vertex index at each grid corner, row by row, or -1 where the mesh has none.</summary>
    internal int[] VertexGrid { get; }

    /// <summary>The rest positions as a span, for copying without a walk through the interface.</summary>
    internal ReadOnlySpan<(double X, double Y)> RestSpan => _vertices;

    /// <summary>The triangle list as a span.</summary>
    internal ReadOnlySpan<int> TriangleSpan => _triangles;

    /// <summary>Whether a point lies inside a triangle of the mesh.</summary>
    public bool Contains(double x, double y) => CellAt(x, y) is var c && c >= 0 && _cellTriangle[c] >= 0;

    /// <summary>The vertex nearest a point.</summary>
    public int NearestVertex(double x, double y)
    {
        var best = -1;
        var bestDistance = double.MaxValue;
        for (var v = 0; v < _vertices.Length; v++)
        {
            var (dx, dy) = (_vertices[v].X - x, _vertices[v].Y - y);
            var d = dx * dx + dy * dy;
            if (d < bestDistance) (best, bestDistance) = (v, d);
        }
        return best;
    }

    /// <summary>
    /// A point expressed in the mesh: its triangle and its barycentric
    /// weights there. A point outside the mesh takes the nearest triangle and
    /// extrapolates, so a brush's reach past the silhouette still follows.
    /// </summary>
    /// <remarks>
    /// Never throws and never answers with a weight that is not a number: a
    /// coordinate that is not finite is read as the middle of the grid, and
    /// one absurdly far away as the edge of a wide margin round it. Stroke
    /// points come from files, and a renderer handed NaN weights paints NaN.
    /// </remarks>
    public Embedding Embed(double x, double y)
    {
        if (IsEmpty) return default;
        x = Tame(x, _originX, _cols);
        y = Tame(y, _originY, _rows);
        var c = CellAt(x, y);
        if (c < 0 || _cellTriangle[c] < 0) c = NearestCell(x, y);

        // Within the cell: u across, v down, both 0..1 inside it and beyond
        // that outside. The diagonal u = v splits the cell's two triangles.
        var u = (x - (_originX + c % _cols * Cell)) / Cell;
        var v = (y - (_originY + c / _cols * Cell)) / Cell;
        var first = _cellTriangle[c];
        return u >= v
            ? new Embedding(first, 1 - u, u - v, v)        // corners 00, 10, 11
            : new Embedding(first + 1, 1 - v, u, v - u);   // corners 00, 11, 01
    }

    /// <summary>A point's place in a triangle: weights on its three corners, summing to one.</summary>
    public readonly record struct Embedding(int Triangle, double U, double V, double W);

    /// <summary>A coordinate the grid arithmetic can square without overflowing.</summary>
    private double Tame(double value, double origin, int cells)
    {
        var (low, high) = (origin - 1e6 * Cell, origin + (cells + 1e6) * Cell);
        if (double.IsNaN(value)) return origin + cells * Cell / 2;
        return Math.Clamp(value, low, high);
    }

    private int CellAt(double x, double y)
    {
        if (IsEmpty || !double.IsFinite(x) || !double.IsFinite(y)) return -1;
        var i = Math.Floor((x - _originX) / Cell);
        var j = Math.Floor((y - _originY) / Cell);
        if (i < 0 || j < 0 || i >= _cols || j >= _rows) return -1;
        return (int)j * _cols + (int)i;
    }

    /// <summary>
    /// The kept cell whose rectangle is nearest a point, searched outward in
    /// rings from the cell the point is in. Ties go to the cell met first,
    /// and the rings are walked in a fixed order, so the answer is the same
    /// on every machine.
    /// </summary>
    private int NearestCell(double x, double y)
    {
        var ci = (int)Math.Clamp(Math.Floor((x - _originX) / Cell), 0, _cols - 1);
        var cj = (int)Math.Clamp(Math.Floor((y - _originY) / Cell), 0, _rows - 1);
        var best = -1;
        var bestDistance = double.MaxValue;
        var limit = Math.Max(_cols, _rows);
        for (var ring = 0; ring <= limit; ring++)
        {
            // Nothing in a farther ring can beat a hit already found once the
            // ring's own inner edge is farther away than that hit.
            if (best >= 0 && (ring - 1) * Cell > Math.Sqrt(bestDistance)) break;
            for (var j = cj - ring; j <= cj + ring; j++)
            {
                if (j < 0 || j >= _rows) continue;
                var edge = j == cj - ring || j == cj + ring;
                for (var i = ci - ring; i <= ci + ring; i += edge ? 1 : Math.Max(1, 2 * ring))
                {
                    if (i < 0 || i >= _cols) continue;
                    var c = j * _cols + i;
                    if (_cellTriangle[c] < 0) continue;
                    var left = _originX + i * Cell;
                    var top = _originY + j * Cell;
                    var dx = x < left ? left - x : x > left + Cell ? x - left - Cell : 0;
                    var dy = y < top ? top - y : y > top + Cell ? y - top - Cell : 0;
                    var d = dx * dx + dy * dy;
                    if (d < bestDistance) (best, bestDistance) = (c, d);
                }
            }
        }
        return best;
    }
}

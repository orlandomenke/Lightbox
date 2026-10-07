namespace Lightbox.Core.Geometry;

using Lightbox.Core.Documents;

/// <summary>
/// The cage warp: the transform box covered by a lattice of control points,
/// where dragging a point bends the drawing smoothly around it (Q199).
/// </summary>
/// <remarks>
/// <para>
/// <b>What a band scale cannot do.</b> A band moves space along one axis
/// between straight lines; a cage moves it in two dimensions between control
/// points, so a limb can be bent, a jaw pushed out or a shoulder dropped
/// without the rest of the figure knowing. Photoshop's Warp is the shape the
/// owner chose: a regular grid over the box, three by three by default,
/// adjustable from two to six.
/// </para>
/// <para>
/// <b>The map is bicubic over the moved control points.</b> Each stroke point
/// finds its cell in the <em>undeformed</em> grid, which is regular and needs
/// no search, and its position comes from a Catmull-Rom spline through the
/// moved points along one axis and then the other. Past the edge of the grid
/// the ghost control points are extrapolated rather than clamped, which is
/// what lets the spline reproduce a straight line exactly: a lattice nobody
/// has dragged is the identity to the last bit, and a corner dragged alone
/// bends only the cells around it.
/// </para>
/// <para>
/// <b>Outside the box nothing moves</b>, the promise the band scale already
/// makes: a stroke reaching past the box keeps the part that is outside where
/// it was. The box is the drawing's own visible bounds, so in practice only a
/// brush's reach lies outside it.
/// </para>
/// <para>
/// <b>Nothing here reaches the document until a commit</b>, and nothing here
/// is random or order-dependent: the spline is plain arithmetic over a fixed
/// grid (invariant 2), and the commit moves stroke points through
/// <see cref="TransformOps.TransformStroke"/> like every other transform
/// (invariant 1). There is no serialized form to keep optional because there
/// is no serialized form.
/// </para>
/// </remarks>
public static class CageWarp
{
    /// <summary>The fewest cells per axis a lattice may have.</summary>
    public const int MinGrid = 2;

    /// <summary>The most cells per axis — past this the handles crowd the drawing.</summary>
    public const int MaxGrid = 6;

    /// <summary>Three by three: enough to bend a limb, few enough to see the drawing through.</summary>
    public const int DefaultGrid = 3;

    /// <summary>
    /// A lattice over a box: how many cells each way, and where every control
    /// point stands now. The undeformed positions are implied by the box and
    /// the counts, so the identity is a lattice whose points are all at home.
    /// </summary>
    /// <param name="Points">
    /// <c>(Cols + 1) × (Rows + 1)</c> moved positions in reading order — row
    /// by row from the top, left to right.
    /// </param>
    public readonly record struct Lattice(
        double Left, double Top, double Right, double Bottom,
        int Cols, int Rows, IReadOnlyList<(double X, double Y)> Points)
    {
        /// <summary>A lattice nobody has touched: every point where the grid puts it.</summary>
        public static Lattice Identity(
            double left, double top, double right, double bottom, int cols = DefaultGrid, int rows = DefaultGrid)
        {
            cols = Math.Clamp(cols, MinGrid, MaxGrid);
            rows = Math.Clamp(rows, MinGrid, MaxGrid);
            var points = new (double X, double Y)[(cols + 1) * (rows + 1)];
            for (var j = 0; j <= rows; j++)
            {
                for (var i = 0; i <= cols; i++)
                {
                    points[j * (cols + 1) + i] = (
                        left + (right - left) * i / cols,
                        top + (bottom - top) * j / rows);
                }
            }
            return new Lattice(left, top, right, bottom, cols, rows, points);
        }

        /// <summary>The index of control point (i, j) in <see cref="Points"/>.</summary>
        public int IndexOf(int i, int j) => j * (Cols + 1) + i;

        /// <summary>Where control point (i, j) stands on the undeformed grid.</summary>
        public (double X, double Y) HomeOf(int i, int j) => (
            Left + (Right - Left) * i / Cols,
            Top + (Bottom - Top) * j / Rows);

        /// <summary>Whether any control point has left home — whether a commit would move anything.</summary>
        public bool Moves
        {
            get
            {
                if (Points.Count != (Cols + 1) * (Rows + 1)) return false;
                for (var j = 0; j <= Rows; j++)
                {
                    for (var i = 0; i <= Cols; i++)
                    {
                        var (hx, hy) = HomeOf(i, j);
                        var (px, py) = Points[IndexOf(i, j)];
                        if (Math.Abs(px - hx) > 1e-9 || Math.Abs(py - hy) > 1e-9) return true;
                    }
                }
                return false;
            }
        }
    }

    /// <summary>
    /// Move one control point and return the lattice that results. A point
    /// may go anywhere finite — a cage is allowed to fold, the way Warp is —
    /// and an index off the grid or a non-finite target changes nothing.
    /// </summary>
    public static Lattice Drag(Lattice lattice, int index, double x, double y)
    {
        if (index < 0 || index >= lattice.Points.Count) return lattice;
        if (!double.IsFinite(x) || !double.IsFinite(y)) return lattice;
        var points = lattice.Points.ToArray();
        points[index] = (x, y);
        return lattice with { Points = points };
    }

    /// <summary>
    /// The point map for a lattice: bicubic inside the box, the identity
    /// outside it. An untouched lattice returns a map that does nothing
    /// without evaluating the spline.
    /// </summary>
    public static TransformOps.PointMap Map(Lattice lattice)
    {
        if (!lattice.Moves) return (x, y) => (x, y);
        return (x, y) => MapPoint(lattice, x, y);
    }

    /// <summary>One point through the lattice — see <see cref="Map"/>.</summary>
    public static (double X, double Y) MapPoint(in Lattice lattice, double x, double y)
    {
        if (x < lattice.Left || x > lattice.Right || y < lattice.Top || y > lattice.Bottom) return (x, y);
        var w = lattice.Right - lattice.Left;
        var h = lattice.Bottom - lattice.Top;
        if (w <= 0 || h <= 0) return (x, y);

        // Cell and local coordinates on the undeformed grid: regular, so no
        // search. The top-right of the box lands in the last cell at s or t = 1.
        var u = (x - lattice.Left) / w * lattice.Cols;
        var v = (y - lattice.Top) / h * lattice.Rows;
        var i = Math.Clamp((int)Math.Floor(u), 0, lattice.Cols - 1);
        var j = Math.Clamp((int)Math.Floor(v), 0, lattice.Rows - 1);
        var s = u - i;
        var t = v - j;

        // Catmull-Rom along each of the four rows around the cell, then across
        // them — the usual bicubic, with ghost points extrapolated beyond the
        // grid so the spline through a straight row stays straight.
        Span<double> rx = stackalloc double[4];
        Span<double> ry = stackalloc double[4];
        for (var dj = -1; dj <= 2; dj++)
        {
            var row = j + dj;
            var (p0x, p0y) = At(lattice, i - 1, row);
            var (p1x, p1y) = At(lattice, i, row);
            var (p2x, p2y) = At(lattice, i + 1, row);
            var (p3x, p3y) = At(lattice, i + 2, row);
            rx[dj + 1] = Spline(p0x, p1x, p2x, p3x, s);
            ry[dj + 1] = Spline(p0y, p1y, p2y, p3y, s);
        }
        return (Spline(rx[0], rx[1], rx[2], rx[3], t), Spline(ry[0], ry[1], ry[2], ry[3], t));
    }

    /// <summary>
    /// A control point, with the ones past the grid's edge extrapolated from
    /// the two inside it. Linear extrapolation is what makes the spline
    /// reproduce a line through the last two points rather than curling back
    /// toward a duplicated end point.
    /// </summary>
    private static (double X, double Y) At(in Lattice l, int i, int j)
    {
        if (j < 0)
        {
            var (ax, ay) = At(l, i, 0);
            var (bx, by) = At(l, i, 1);
            return (2 * ax - bx, 2 * ay - by);
        }
        if (j > l.Rows)
        {
            var (ax, ay) = At(l, i, l.Rows);
            var (bx, by) = At(l, i, l.Rows - 1);
            return (2 * ax - bx, 2 * ay - by);
        }
        if (i < 0)
        {
            var (ax, ay) = l.Points[l.IndexOf(0, j)];
            var (bx, by) = l.Points[l.IndexOf(1, j)];
            return (2 * ax - bx, 2 * ay - by);
        }
        if (i > l.Cols)
        {
            var (ax, ay) = l.Points[l.IndexOf(l.Cols, j)];
            var (bx, by) = l.Points[l.IndexOf(l.Cols - 1, j)];
            return (2 * ax - bx, 2 * ay - by);
        }
        return l.Points[l.IndexOf(i, j)];
    }

    /// <summary>Uniform Catmull-Rom between p1 and p2 at t, with p0 and p3 shaping the tangents.</summary>
    private static double Spline(double p0, double p1, double p2, double p3, double t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return 0.5 * (
            2 * p1
            + (-p0 + p2) * t
            + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2
            + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
    }

    /// <summary>
    /// How far apart a stroke's points may stand inside the box for the warp to
    /// read as a curve: a quarter of a cell, so a segment that crosses a cell
    /// bends in at least four places.
    /// </summary>
    public static double ChordFor(in Lattice lattice) =>
        Math.Max(2.0, Math.Min(
            (lattice.Right - lattice.Left) / lattice.Cols,
            (lattice.Bottom - lattice.Top) / lattice.Rows) / 4);

    /// <summary>
    /// Insert points along a stroke's segments inside the box, before the map
    /// is applied, so a straight line can bend.
    /// </summary>
    /// <remarks>
    /// <b>The same reason the band scale inserts on its dividers, generalised.</b>
    /// A point map moves points; the segment between two of them stays
    /// straight whatever the map does in between. A band scale only kinks at
    /// its dividers, so one point there is enough; a cage curves everywhere, so
    /// a long segment inside it is divided to the lattice's chord. Every
    /// attribute of an inserted point is interpolated, not copied, as the band
    /// scale's are. <b>Only the part of a segment inside the box is divided</b>
    /// — the map leaves the rest alone, and a long line that merely grazes the
    /// box would otherwise gain a point every few pixels along its whole
    /// length, permanently (perf-warden's case: a 4000 px segment past a
    /// 100 px cage). A point is placed where the segment enters and leaves the
    /// box, so the bend starts and stops at the box's edge rather than at
    /// whichever sample happened to be nearest. Returns how many points were
    /// added.
    /// </remarks>
    public static int Insert(Stroke stroke, in Lattice lattice)
    {
        var added = InsertInto(stroke.Points, lattice);
        if (stroke.Holes is not null)
        {
            foreach (var hole in stroke.Holes) added += InsertInto(hole, lattice);
        }
        return added;
    }

    private static int InsertInto(List<StrokePoint> points, in Lattice lattice)
    {
        if (points.Count < 2) return 0;
        var chord = ChordFor(lattice);
        var result = new List<StrokePoint>(points.Count * 2);
        var added = 0;
        for (var i = 0; i < points.Count - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            result.Add(a);
            if (SpanInside(lattice, a, b) is not var (t0, t1)) continue;
            var length = GeometryOps.Dist(a, b) * (t1 - t0);
            var pieces = (int)Math.Ceiling(length / chord);
            // Entering point, the divisions between, leaving point — each only
            // when it is strictly inside the segment, so an endpoint already
            // on the box's edge is not doubled.
            var last = 0.0;
            void Place(double t)
            {
                if (t <= 1e-9 || t >= 1 - 1e-9 || t - last < 1e-9) return;
                result.Add(BandScale.Lerp(a, b, t));
                added++;
                last = t;
            }
            Place(t0);
            for (var k = 1; k < pieces; k++) Place(t0 + (t1 - t0) * k / pieces);
            Place(t1);
        }
        result.Add(points[^1]);
        if (added > 0)
        {
            points.Clear();
            points.AddRange(result);
        }
        return added;
    }

    /// <summary>
    /// The part of segment a→b that lies inside the box, as a parameter range,
    /// or null when none does — Liang–Barsky, the usual slab test.
    /// </summary>
    private static (double T0, double T1)? SpanInside(in Lattice l, StrokePoint a, StrokePoint b)
    {
        double t0 = 0, t1 = 1;
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        if (!Clip(-dx, a.X - l.Left, ref t0, ref t1)) return null;
        if (!Clip(dx, l.Right - a.X, ref t0, ref t1)) return null;
        if (!Clip(-dy, a.Y - l.Top, ref t0, ref t1)) return null;
        if (!Clip(dy, l.Bottom - a.Y, ref t0, ref t1)) return null;
        return t1 > t0 ? (t0, t1) : null;
    }

    private static bool Clip(double p, double q, ref double t0, ref double t1)
    {
        if (Math.Abs(p) < 1e-12) return q >= 0;
        var t = q / p;
        if (p < 0)
        {
            if (t > t1) return false;
            if (t > t0) t0 = t;
        }
        else
        {
            if (t < t0) return false;
            if (t < t1) t1 = t;
        }
        return true;
    }

    /// <summary>
    /// The lattice as a triangle mesh: where each vertex reads from on the
    /// undeformed drawing and where it lands. What the live preview draws and
    /// what a raster baseline is resampled through, so the two cannot disagree.
    /// </summary>
    /// <param name="Source">Vertex positions on the undeformed grid, in document pixels.</param>
    /// <param name="Target">The same vertices through the map.</param>
    /// <param name="Triangles">Three indices per triangle into the two arrays.</param>
    public readonly record struct Mesh(
        (double X, double Y)[] Source, (double X, double Y)[] Target, int[] Triangles, int Across, int Down);

    /// <summary>
    /// Tessellate a lattice: <paramref name="subdivisions"/> quads per cell per
    /// axis, two triangles each. Four per cell keeps a bicubic bulge smooth to
    /// the eye without the vertex count growing past a few hundred.
    /// </summary>
    /// <remarks>
    /// Plain doubles and ints rather than a rendering type because
    /// <c>Lightbox.Core</c> carries no rendering dependency; the caller turns
    /// these into whatever its rasterizer wants, as the band cells are.
    /// </remarks>
    public static Mesh MeshOf(in Lattice lattice, int subdivisions = 4)
    {
        subdivisions = Math.Max(1, subdivisions);
        var across = lattice.Cols * subdivisions + 1;
        var down = lattice.Rows * subdivisions + 1;
        var source = new (double X, double Y)[across * down];
        var target = new (double X, double Y)[across * down];
        var w = lattice.Right - lattice.Left;
        var h = lattice.Bottom - lattice.Top;
        for (var j = 0; j < down; j++)
        {
            for (var i = 0; i < across; i++)
            {
                var x = lattice.Left + w * i / (across - 1);
                var y = lattice.Top + h * j / (down - 1);
                source[j * across + i] = (x, y);
                target[j * across + i] = MapPoint(lattice, x, y);
            }
        }
        var triangles = new int[(across - 1) * (down - 1) * 6];
        var n = 0;
        for (var j = 0; j < down - 1; j++)
        {
            for (var i = 0; i < across - 1; i++)
            {
                var a = j * across + i;
                var b = a + 1;
                var c = a + across;
                var d = c + 1;
                triangles[n++] = a; triangles[n++] = b; triangles[n++] = c;
                triangles[n++] = b; triangles[n++] = d; triangles[n++] = c;
            }
        }
        return new Mesh(source, target, triangles, across, down);
    }
}

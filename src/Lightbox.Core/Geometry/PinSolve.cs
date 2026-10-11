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
/// factorised once when the pins are placed, then a matrix product and two
/// back-substitutions per drag (Q239, <c>docs/DESIGN-puppet-pins.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The method</b> is Igarashi, Moscovich and Hughes, <em>As-Rigid-As-Possible
/// Shape Manipulation</em> (2005), implemented from the paper. Two steps, each
/// a linear least-squares problem with a closed form:
/// </para>
/// <list type="number">
/// <item>
/// Every triangle tries to stay <em>similar</em> to itself — free to turn and
/// to scale, not to shear. That is quadratic in the vertex positions, so with
/// the pins held it is one linear solve, and it reproduces any turn of the
/// pins exactly, a half turn included, which no method that starts from the
/// rest pose and iterates can promise in a fixed number of rounds.
/// </item>
/// <item>
/// Each rest triangle is then fitted rigidly — turned, never scaled — onto
/// where step one put it, and the mesh is solved once more so that every edge
/// is as close as it can be to its fitted one. This is what takes the
/// swelling and shrinking out.
/// </item>
/// </list>
/// <para>
/// <b>The split is the performance design.</b> Everything that depends only
/// on the mesh and on where the pins were dropped is paid in
/// <see cref="Prepare"/>, once: both factorisations, and the pins' own
/// response (step one collapses to a single matrix times the pin targets).
/// <c>Solve</c> runs per pointer event and per rendered frame, writes into the
/// caller's buffer and allocates nothing — which also means one
/// <see cref="PinSolve"/> must not be solved on two threads at once.
/// </para>
/// <para>
/// <b>Pins are exact, not springs.</b> A pin anywhere in a triangle is a
/// linear constraint on its three corners, held through the constraint's own
/// small system rather than a stiff penalty, so a pin lands on its target to
/// rounding and the conditioning of the big system does not depend on how
/// hard the pins pull.
/// </para>
/// <para>
/// <b>Deterministic by construction</b>: direct factorisations in doubles
/// with a fixed elimination order, exactly two steps, no tolerance anywhere —
/// the same pins give the same vertices to the bit (invariant 2, and the
/// bones design's determinism rules).
/// </para>
/// </remarks>
public sealed class PinSolve
{
    /// <summary>How much stiffer a triangle at the centre of a starch pin is than one outside it.</summary>
    private const double StarchGain = 60;

    /// <summary>
    /// What holds a pin while the big systems are factorised. It cancels out
    /// of the answer — the constraint is enforced exactly afterwards — and is
    /// only there to make the systems definite, so it sits at the scale of
    /// the mesh's own stiffness.
    /// </summary>
    private const double Hold = 4;

    private readonly PinMesh _mesh;
    private readonly int _pinCount;

    // What each piece of the mesh does: -1 stays at rest, -2 is solved, and
    // anything else is the one pin that carries it bodily.
    private readonly int[] _componentRule;

    // The solved part. _local maps a mesh vertex to its row, or -1.
    private readonly int[] _local;
    private readonly int[] _global;
    private readonly int[] _solvedTriangles;      // local vertex indices, three per triangle
    private readonly double[] _triangleWeight;
    private readonly int[] _constraintPin;        // which pin each constraint is
    private readonly int[] _constraintVertex;     // three local vertices per constraint
    private readonly double[] _constraintWeight;  // their barycentric weights
    private readonly (double X, double Y)[] _pinRest;

    // Step one, whole: displacement = _response · (targets − rest), two rows
    // per solved vertex and two columns per constraint.
    private readonly double[] _response;

    // Step two: the factor of the edge system, the constraints' response in
    // it, and the inverse of their own small system.
    private readonly double[] _factor;
    private readonly int _band;
    private readonly double[] _constraintResponse;
    private readonly double[] _constraintInverse;

    // Scratch, sized once. Solve writes every element it reads.
    private readonly double[] _offset, _first, _force, _value, _residual, _multiplier;

    private PinSolve(PinMesh mesh, IReadOnlyList<PinHandle> pins)
    {
        _mesh = mesh;
        _pinCount = pins.Count;
        var rest = mesh.RestSpan;
        var triangles = mesh.TriangleSpan;

        // Where each position pin sits, and which piece it is on. A pin on
        // top of an earlier one adds no information and would make the
        // constraints' own system singular, so it is dropped: the first wins.
        _pinRest = new (double X, double Y)[pins.Count];
        var embedded = new PinMesh.Embedding[pins.Count];
        var pinComponent = new int[pins.Count];
        var perComponent = new List<int>[mesh.ComponentCount];
        for (var c = 0; c < perComponent.Length; c++) perComponent[c] = [];
        for (var p = 0; p < pins.Count; p++)
        {
            _pinRest[p] = (pins[p].X, pins[p].Y);
            pinComponent[p] = -1;
            if (pins[p].Kind != PinKind.Position || mesh.IsEmpty) continue;
            if (!double.IsFinite(pins[p].X) || !double.IsFinite(pins[p].Y)) continue;
            embedded[p] = mesh.Embed(pins[p].X, pins[p].Y);
            var component = mesh.ComponentOf(triangles[embedded[p].Triangle * 3]);
            var duplicate = false;
            foreach (var other in perComponent[component])
            {
                var (dx, dy) = (pins[other].X - pins[p].X, pins[other].Y - pins[p].Y);
                duplicate |= dx * dx + dy * dy < 1e-18;
            }
            if (duplicate) continue;
            pinComponent[p] = component;
            perComponent[component].Add(p);
        }

        _componentRule = new int[mesh.ComponentCount];
        for (var c = 0; c < _componentRule.Length; c++)
        {
            _componentRule[c] = perComponent[c].Count switch
            {
                0 => -1,
                1 => perComponent[c][0],
                _ => -2,
            };
        }

        // Rows for the solved vertices, numbered along the grid's short side
        // so that neighbours are close together and the systems are banded
        // as narrowly as the mesh allows.
        _local = new int[rest.Length];
        Array.Fill(_local, -1);
        var order = new List<int>();
        var (across, down) = (mesh.GridCols + 1, mesh.GridRows + 1);
        var grid = mesh.VertexGrid;
        if (across <= down)
        {
            for (var g = 0; g < grid.Length; g++) Take(grid[g]);
        }
        else
        {
            for (var i = 0; i < across; i++)
                for (var j = 0; j < down; j++) Take(grid[j * across + i]);
        }
        _global = [.. order];
        var n = _global.Length;

        var solved = new List<int>();
        for (var t = 0; t < triangles.Length; t += 3)
        {
            if (_local[triangles[t]] < 0) continue;
            solved.Add(_local[triangles[t]]);
            solved.Add(_local[triangles[t + 1]]);
            solved.Add(_local[triangles[t + 2]]);
        }
        _solvedTriangles = [.. solved];
        var band = 0;
        for (var t = 0; t < _solvedTriangles.Length; t += 3)
        {
            band = Math.Max(band, Math.Abs(_solvedTriangles[t] - _solvedTriangles[t + 1]));
            band = Math.Max(band, Math.Abs(_solvedTriangles[t + 1] - _solvedTriangles[t + 2]));
            band = Math.Max(band, Math.Abs(_solvedTriangles[t + 2] - _solvedTriangles[t]));
        }
        _band = band;

        // Starch: a triangle near a starch pin weighs more in both steps, so
        // bending it costs more than bending its neighbours and the bend
        // goes round it.
        _triangleWeight = new double[_solvedTriangles.Length / 3];
        for (var t = 0; t < _triangleWeight.Length; t++)
        {
            var a = rest[_global[_solvedTriangles[t * 3]]];
            var b = rest[_global[_solvedTriangles[t * 3 + 1]]];
            var c = rest[_global[_solvedTriangles[t * 3 + 2]]];
            var (cx, cy) = ((a.X + b.X + c.X) / 3, (a.Y + b.Y + c.Y) / 3);
            var stiff = 0.0;
            foreach (var pin in pins)
            {
                if (pin.Kind != PinKind.Starch || !(pin.Extent > 0)) continue;
                if (!double.IsFinite(pin.X) || !double.IsFinite(pin.Y)) continue;
                var d = Math.Sqrt((cx - pin.X) * (cx - pin.X) + (cy - pin.Y) * (cy - pin.Y)) / pin.Extent;
                if (d >= 1) continue;
                stiff = Math.Max(stiff, 1 - d * d * (3 - 2 * d));
            }
            _triangleWeight[t] = 1 + StarchGain * stiff;
        }

        var constraintPins = new List<int>();
        for (var p = 0; p < pins.Count; p++)
            if (pinComponent[p] >= 0 && _componentRule[pinComponent[p]] == -2) constraintPins.Add(p);
        _constraintPin = [.. constraintPins];
        var m = _constraintPin.Length;
        _constraintVertex = new int[m * 3];
        _constraintWeight = new double[m * 3];
        for (var k = 0; k < m; k++)
        {
            var e = embedded[_constraintPin[k]];
            for (var corner = 0; corner < 3; corner++)
                _constraintVertex[k * 3 + corner] = _local[triangles[e.Triangle * 3 + corner]];
            _constraintWeight[k * 3] = e.U;
            _constraintWeight[k * 3 + 1] = e.V;
            _constraintWeight[k * 3 + 2] = e.W;
        }

        _offset = new double[m * 2];
        _first = new double[n * 2];
        _force = new double[n];
        _value = new double[n];
        _residual = new double[m];
        _multiplier = new double[m];
        _response = BuildSimilarityResponse(rest, n, m);
        (_factor, _constraintResponse, _constraintInverse) = BuildEdgeSystem(n, m);

        void Take(int vertex)
        {
            if (vertex < 0 || _componentRule[mesh.ComponentOf(vertex)] != -2) return;
            _local[vertex] = order.Count;
            order.Add(vertex);
        }
    }

    /// <summary>
    /// Factorise for a mesh and the pins dropped on it. A pin off the mesh
    /// takes the nearest triangle; a piece of the mesh holding no position
    /// pin stays at rest, and a piece holding exactly one is carried by it
    /// without bending — one point cannot say which way to turn.
    /// </summary>
    public static PinSolve Prepare(PinMesh mesh, IReadOnlyList<PinHandle> pins) => new(mesh, pins);

    /// <summary>How many vertices a solve writes.</summary>
    public int VertexCount => _mesh.Vertices.Count;

    /// <summary>
    /// Where every vertex stands when each pin is at its target — one target
    /// per pin, in the order the pins were prepared. A starch pin's target is
    /// ignored, and a target that is not finite leaves its pin at rest. When
    /// no pin has left home the rest positions are copied without solving, so
    /// an untouched puppet is the identity to the last bit.
    /// </summary>
    public void Solve(ReadOnlySpan<(double X, double Y)> targets, Span<(double X, double Y)> deformed)
    {
        if (targets.Length != _pinCount)
            throw new ArgumentException($"{_pinCount} pins were prepared and {targets.Length} targets given.", nameof(targets));
        var rest = _mesh.RestSpan;
        if (deformed.Length < rest.Length)
            throw new ArgumentException($"The mesh has {rest.Length} vertices and the buffer holds {deformed.Length}.", nameof(deformed));

        rest.CopyTo(deformed);

        // One-pin pieces go with their pin.
        var moved = false;
        for (var v = 0; v < rest.Length; v++)
        {
            var rule = _componentRule[_mesh.ComponentOf(v)];
            if (rule < 0) continue;
            var (dx, dy) = OffsetOf(rule, targets);
            if (dx == 0 && dy == 0) continue;
            deformed[v] = (rest[v].X + dx, rest[v].Y + dy);
        }

        var m = _constraintPin.Length;
        for (var k = 0; k < m; k++)
        {
            var (dx, dy) = OffsetOf(_constraintPin[k], targets);
            _offset[k * 2] = dx;
            _offset[k * 2 + 1] = dy;
            moved |= dx != 0 || dy != 0;
        }
        if (!moved) return;

        var n = _global.Length;
        var columns = m * 2;

        // Step one: every solved vertex's displacement is a fixed combination
        // of the pins' displacements, worked out in Prepare.
        for (var row = 0; row < n * 2; row++)
        {
            var sum = 0.0;
            var at = row * columns;
            for (var c = 0; c < columns; c++) sum += _response[at + c] * _offset[c];
            _first[row] = sum;
        }
        for (var i = 0; i < n; i++)
        {
            var r = rest[_global[i]];
            _first[i * 2] += r.X;
            _first[i * 2 + 1] += r.Y;
        }

        // Step two, once per axis: fit each rest triangle rigidly onto its
        // step-one self, then put every edge as near its fitted one as the
        // pins allow.
        SolveAxis(0, rest, targets);
        for (var i = 0; i < n; i++) deformed[_global[i]] = (_value[i], deformed[_global[i]].Y);
        SolveAxis(1, rest, targets);
        for (var i = 0; i < n; i++) deformed[_global[i]] = (deformed[_global[i]].X, _value[i]);
    }

    /// <summary>The same solve into a new array. Convenience for tests and one-off callers.</summary>
    public (double X, double Y)[] Solve(IReadOnlyList<(double X, double Y)> targets)
    {
        var deformed = new (double X, double Y)[VertexCount];
        var given = targets as (double X, double Y)[] ?? [.. targets];
        Solve(given, deformed);
        return deformed;
    }

    /// <summary>
    /// Where a rest-space point lands on a deformed mesh: its
    /// <see cref="PinMesh.Embedding"/> applied to the moved corners.
    /// </summary>
    public static (double X, double Y) MapPoint(
        PinMesh mesh, ReadOnlySpan<(double X, double Y)> deformed, double x, double y)
    {
        if (mesh.IsEmpty) return (x, y);
        var e = mesh.Embed(x, y);
        var triangles = mesh.TriangleSpan;
        var a = deformed[triangles[e.Triangle * 3]];
        var b = deformed[triangles[e.Triangle * 3 + 1]];
        var c = deformed[triangles[e.Triangle * 3 + 2]];
        return (e.U * a.X + e.V * b.X + e.W * c.X, e.U * a.Y + e.V * b.Y + e.W * c.Y);
    }

    /// <summary>How far a pin's target is from where the pin was dropped; nothing for a target that is not a number.</summary>
    private (double X, double Y) OffsetOf(int pin, ReadOnlySpan<(double X, double Y)> targets)
    {
        var t = targets[pin];
        if (!double.IsFinite(t.X) || !double.IsFinite(t.Y)) return (0, 0);
        return (t.X - _pinRest[pin].X, t.Y - _pinRest[pin].Y);
    }

    private void SolveAxis(int axis, ReadOnlySpan<(double X, double Y)> rest, ReadOnlySpan<(double X, double Y)> targets)
    {
        var n = _global.Length;
        var m = _constraintPin.Length;
        Array.Clear(_force, 0, n);

        for (var t = 0; t < _triangleWeight.Length; t++)
        {
            var (i0, i1, i2) = (_solvedTriangles[t * 3], _solvedTriangles[t * 3 + 1], _solvedTriangles[t * 3 + 2]);
            var (q0, q1, q2) = (rest[_global[i0]], rest[_global[i1]], rest[_global[i2]]);
            var (p0x, p0y) = (_first[i0 * 2], _first[i0 * 2 + 1]);
            var (p1x, p1y) = (_first[i1 * 2], _first[i1 * 2 + 1]);
            var (p2x, p2y) = (_first[i2 * 2], _first[i2 * 2 + 1]);

            // The turn that best carries the rest triangle onto the step-one
            // triangle, about their centroids: the angle whose cosine and
            // sine are the summed dot and cross products.
            var (qcx, qcy) = ((q0.X + q1.X + q2.X) / 3, (q0.Y + q1.Y + q2.Y) / 3);
            var (pcx, pcy) = ((p0x + p1x + p2x) / 3, (p0y + p1y + p2y) / 3);
            var dot = (q0.X - qcx) * (p0x - pcx) + (q0.Y - qcy) * (p0y - pcy)
                    + (q1.X - qcx) * (p1x - pcx) + (q1.Y - qcy) * (p1y - pcy)
                    + (q2.X - qcx) * (p2x - pcx) + (q2.Y - qcy) * (p2y - pcy);
            var cross = (q0.X - qcx) * (p0y - pcy) - (q0.Y - qcy) * (p0x - pcx)
                      + (q1.X - qcx) * (p1y - pcy) - (q1.Y - qcy) * (p1x - pcx)
                      + (q2.X - qcx) * (p2y - pcy) - (q2.Y - qcy) * (p2x - pcx);
            var length = Math.Sqrt(dot * dot + cross * cross);
            var (cos, sin) = length > 1e-300 ? (dot / length, cross / length) : (1.0, 0.0);

            var w = _triangleWeight[t];
            Edge(i0, i1, q0.X - q1.X, q0.Y - q1.Y);
            Edge(i1, i2, q1.X - q2.X, q1.Y - q2.Y);
            Edge(i2, i0, q2.X - q0.X, q2.Y - q0.Y);

            void Edge(int i, int j, double ex, double ey)
            {
                var d = axis == 0 ? cos * ex - sin * ey : sin * ex + cos * ey;
                _force[i] += w * d;
                _force[j] -= w * d;
            }
        }

        for (var k = 0; k < m; k++)
        {
            var target = Target(k);
            for (var corner = 0; corner < 3; corner++)
                _force[_constraintVertex[k * 3 + corner]] += Hold * _constraintWeight[k * 3 + corner] * target;
        }

        Array.Copy(_force, _value, n);
        BandSolve(_factor, n, _band, _value);

        // The pins, exactly: what the held solve missed each target by,
        // through the constraints' own system, taken back out.
        for (var k = 0; k < m; k++)
        {
            var at = 0.0;
            for (var corner = 0; corner < 3; corner++)
                at += _constraintWeight[k * 3 + corner] * _value[_constraintVertex[k * 3 + corner]];
            _residual[k] = at - Target(k);
        }
        for (var k = 0; k < m; k++)
        {
            var sum = 0.0;
            for (var j = 0; j < m; j++) sum += _constraintInverse[k * m + j] * _residual[j];
            _multiplier[k] = sum;
        }
        for (var i = 0; i < n; i++)
        {
            var sum = 0.0;
            var at = i * m;
            for (var k = 0; k < m; k++) sum += _constraintResponse[at + k] * _multiplier[k];
            _value[i] -= sum;
        }

        double Target(int k)
        {
            var rest0 = _pinRest[_constraintPin[k]];
            return axis == 0 ? rest0.X + _offset[k * 2] : rest0.Y + _offset[k * 2 + 1];
        }
    }

    /// <summary>
    /// Step one, solved for every pin at once. The energy is quadratic in the
    /// vertices' displacement from rest and has no force of its own, so the
    /// displacement that minimises it with the pins held is linear in the
    /// pins' displacement: one matrix, built here, applied per drag.
    /// </summary>
    private double[] BuildSimilarityResponse(ReadOnlySpan<(double X, double Y)> rest, int n, int m)
    {
        if (n == 0 || m == 0) return [];
        var size = n * 2;
        var band = _band * 2 + 1;
        var stride = band + 1;
        var matrix = new double[size * stride];

        Span<int> unknown = stackalloc int[6];
        Span<double> ex = stackalloc double[6];
        Span<double> ey = stackalloc double[6];
        for (var t = 0; t < _triangleWeight.Length; t++)
        {
            var w = _triangleWeight[t];
            for (var apex = 0; apex < 3; apex++)
            {
                // The apex described from the edge opposite it: so far along
                // the edge, so far out from it. A triangle that only turned
                // and scaled keeps both numbers.
                var ia = _solvedTriangles[t * 3 + (apex + 1) % 3];
                var ib = _solvedTriangles[t * 3 + (apex + 2) % 3];
                var ic = _solvedTriangles[t * 3 + apex];
                var (a, b, c) = (rest[_global[ia]], rest[_global[ib]], rest[_global[ic]]);
                var (dx, dy) = (b.X - a.X, b.Y - a.Y);
                var squared = dx * dx + dy * dy;
                var x = ((c.X - a.X) * dx + (c.Y - a.Y) * dy) / squared;
                var y = ((c.X - a.X) * -dy + (c.Y - a.Y) * dx) / squared;

                unknown[0] = ia * 2; unknown[1] = ia * 2 + 1;
                unknown[2] = ib * 2; unknown[3] = ib * 2 + 1;
                unknown[4] = ic * 2; unknown[5] = ic * 2 + 1;
                ex[0] = -(1 - x); ex[1] = -y; ex[2] = -x; ex[3] = y; ex[4] = 1; ex[5] = 0;
                ey[0] = y; ey[1] = -(1 - x); ey[2] = -y; ey[3] = -x; ey[4] = 0; ey[5] = 1;
                for (var r = 0; r < 6; r++)
                {
                    for (var s = 0; s < 6; s++)
                    {
                        if (unknown[s] > unknown[r]) continue;
                        matrix[unknown[r] * stride + unknown[r] - unknown[s]] += w * (ex[r] * ex[s] + ey[r] * ey[s]);
                    }
                }
            }
        }

        // The pins hold the system definite while it is factorised.
        for (var k = 0; k < m; k++)
        {
            for (var axis = 0; axis < 2; axis++)
            {
                for (var r = 0; r < 3; r++)
                {
                    for (var s = 0; s < 3; s++)
                    {
                        var (ur, us) = (_constraintVertex[k * 3 + r] * 2 + axis, _constraintVertex[k * 3 + s] * 2 + axis);
                        if (us > ur) continue;
                        matrix[ur * stride + ur - us] += Hold * _constraintWeight[k * 3 + r] * _constraintWeight[k * 3 + s];
                    }
                }
            }
        }
        BandFactor(matrix, size, band);

        // Each constraint's response, then the constraints' own system.
        var columns = m * 2;
        var responses = new double[columns][];
        for (var c = 0; c < columns; c++)
        {
            var column = new double[size];
            for (var corner = 0; corner < 3; corner++)
                column[_constraintVertex[c / 2 * 3 + corner] * 2 + c % 2] = _constraintWeight[c / 2 * 3 + corner];
            BandSolve(matrix, size, band, column);
            responses[c] = column;
        }
        var own = new double[columns * columns];
        for (var r = 0; r < columns; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                var sum = 0.0;
                for (var corner = 0; corner < 3; corner++)
                    sum += _constraintWeight[r / 2 * 3 + corner] * responses[c][_constraintVertex[r / 2 * 3 + corner] * 2 + r % 2];
                own[r * columns + c] = sum;
            }
        }
        Invert(own, columns);

        // displacement = responses · own⁻¹ · (pin displacement); the hold
        // cancels, which is what makes the pins exact.
        var response = new double[size * columns];
        for (var row = 0; row < size; row++)
        {
            for (var c = 0; c < columns; c++)
            {
                var sum = 0.0;
                for (var k = 0; k < columns; k++) sum += responses[k][row] * own[k * columns + c];
                response[row * columns + c] = sum;
            }
        }
        return response;
    }

    /// <summary>Step two's system: every triangle edge as a spring, the pins held, factorised.</summary>
    private (double[] Factor, double[] Response, double[] Inverse) BuildEdgeSystem(int n, int m)
    {
        if (n == 0 || m == 0) return ([], [], []);
        var stride = _band + 1;
        var matrix = new double[n * stride];
        for (var t = 0; t < _triangleWeight.Length; t++)
        {
            var w = _triangleWeight[t];
            for (var edge = 0; edge < 3; edge++)
            {
                var i = _solvedTriangles[t * 3 + edge];
                var j = _solvedTriangles[t * 3 + (edge + 1) % 3];
                matrix[i * stride] += w;
                matrix[j * stride] += w;
                var (hi, lo) = i > j ? (i, j) : (j, i);
                matrix[hi * stride + hi - lo] -= w;
            }
        }
        for (var k = 0; k < m; k++)
        {
            for (var r = 0; r < 3; r++)
            {
                for (var s = 0; s < 3; s++)
                {
                    var (ur, us) = (_constraintVertex[k * 3 + r], _constraintVertex[k * 3 + s]);
                    if (us > ur) continue;
                    matrix[ur * stride + ur - us] += Hold * _constraintWeight[k * 3 + r] * _constraintWeight[k * 3 + s];
                }
            }
        }
        BandFactor(matrix, n, _band);

        var response = new double[n * m];
        var column = new double[n];
        for (var k = 0; k < m; k++)
        {
            Array.Clear(column);
            for (var corner = 0; corner < 3; corner++)
                column[_constraintVertex[k * 3 + corner]] = _constraintWeight[k * 3 + corner];
            BandSolve(matrix, n, _band, column);
            for (var i = 0; i < n; i++) response[i * m + k] = column[i];
        }
        var own = new double[m * m];
        for (var r = 0; r < m; r++)
        {
            for (var c = 0; c < m; c++)
            {
                var sum = 0.0;
                for (var corner = 0; corner < 3; corner++)
                    sum += _constraintWeight[r * 3 + corner] * response[_constraintVertex[r * 3 + corner] * m + c];
                own[r * m + c] = sum;
            }
        }
        Invert(own, m);
        return (matrix, response, own);
    }

    /// <summary>
    /// Cholesky in place on a symmetric banded matrix held by rows, each row
    /// its diagonal first and then the entries to its left.
    /// </summary>
    /// <remarks>
    /// A pivot that is not positive is floored rather than refused. It can
    /// only happen where the mesh leaves a direction truly free — two parts
    /// touching at a single corner — and there the honest answer is "does not
    /// move", which a huge pivot gives.
    /// </remarks>
    private static void BandFactor(double[] matrix, int n, int band)
    {
        var stride = band + 1;
        for (var i = 0; i < n; i++)
        {
            var first = Math.Max(0, i - band);
            for (var j = first; j <= i; j++)
            {
                var sum = matrix[i * stride + i - j];
                var from = Math.Max(first, j - band);
                for (var k = from; k < j; k++) sum -= matrix[i * stride + i - k] * matrix[j * stride + j - k];
                matrix[i * stride + i - j] = i == j
                    ? (sum > 1e-12 ? Math.Sqrt(sum) : 1e6)
                    : sum / matrix[j * stride];
            }
        }
    }

    /// <summary>Solve with a factor from <see cref="BandFactor"/>, in place.</summary>
    private static void BandSolve(double[] factor, int n, int band, double[] values)
    {
        var stride = band + 1;
        for (var i = 0; i < n; i++)
        {
            var sum = values[i];
            var first = Math.Max(0, i - band);
            for (var k = first; k < i; k++) sum -= factor[i * stride + i - k] * values[k];
            values[i] = sum / factor[i * stride];
        }
        for (var i = n - 1; i >= 0; i--)
        {
            var sum = values[i];
            var last = Math.Min(n - 1, i + band);
            for (var k = i + 1; k <= last; k++) sum -= factor[k * stride + k - i] * values[k];
            values[i] = sum / factor[i * stride];
        }
    }

    /// <summary>Invert a small dense matrix in place: Gauss-Jordan, pivoting on the largest entry in each column.</summary>
    private static void Invert(double[] a, int n)
    {
        var inverse = new double[n * n];
        for (var i = 0; i < n; i++) inverse[i * n + i] = 1;
        for (var column = 0; column < n; column++)
        {
            var pivot = column;
            for (var row = column + 1; row < n; row++)
                if (Math.Abs(a[row * n + column]) > Math.Abs(a[pivot * n + column])) pivot = row;
            if (pivot != column)
            {
                for (var k = 0; k < n; k++)
                {
                    (a[column * n + k], a[pivot * n + k]) = (a[pivot * n + k], a[column * n + k]);
                    (inverse[column * n + k], inverse[pivot * n + k]) = (inverse[pivot * n + k], inverse[column * n + k]);
                }
            }
            var scale = a[column * n + column];
            if (Math.Abs(scale) < 1e-300) scale = 1e-300;
            for (var k = 0; k < n; k++)
            {
                a[column * n + k] /= scale;
                inverse[column * n + k] /= scale;
            }
            for (var row = 0; row < n; row++)
            {
                if (row == column) continue;
                var factor = a[row * n + column];
                if (factor == 0) continue;
                for (var k = 0; k < n; k++)
                {
                    a[row * n + k] -= factor * a[column * n + k];
                    inverse[row * n + k] -= factor * inverse[column * n + k];
                }
            }
        }
        Array.Copy(inverse, a, n * n);
    }
}

/// <summary>
/// Carrying strokes on a deformed <see cref="PinMesh"/> — the pin half of
/// what <see cref="Skinning"/> does for bones, under the same rule: the
/// stroke's points move and the brush re-stamps them; the picture is never
/// warped.
/// </summary>
public static class PinSkinning
{
    /// <summary>
    /// A render-ready copy of a stroke on a deformed mesh: moved
    /// <see cref="Stroke.Points"/>, the rest path in
    /// <see cref="Stroke.RestPoints"/> so every dab seeds where it was drawn
    /// and the mark does not boil, the same id. Densified before it is moved,
    /// so a straight line across a bend comes out as a curve. Returns the
    /// stroke itself, uncloned, when the mesh has not moved.
    /// </summary>
    public static Stroke PoseStroke(
        Stroke stroke, PinMesh mesh, ReadOnlySpan<(double X, double Y)> deformed)
    {
        if (mesh.IsEmpty || stroke.Points.Count == 0) return stroke;
        var rest = mesh.RestSpan;
        var moved = false;
        for (var v = 0; v < rest.Length && !moved; v++) moved = deformed[v] != rest[v];
        if (!moved) return stroke;

        // The dab walk's own densification, and a two-point line subdivided
        // as well: a pair has nothing to curve between for a brush, but a
        // mesh bends the span between them (B404, for bones).
        const double maxChord = 2.0;
        IReadOnlyList<StrokePoint> dense;
        if (stroke.Points.Count == 2 && GeometryOps.Dist(stroke.Points[0], stroke.Points[1]) > maxChord)
        {
            var output = new List<StrokePoint> { stroke.Points[0] };
            GeometryOps.AppendSpan(output, stroke.Points, 0, maxChord);
            dense = output;
        }
        else
        {
            dense = GeometryOps.Densify(stroke.Points, maxChord);
        }

        var posed = new List<StrokePoint>(dense.Count);
        foreach (var p in dense)
        {
            var (x, y) = PinSolve.MapPoint(mesh, deformed, p.X, p.Y);
            posed.Add(p with { X = x, Y = y });
        }

        var copy = stroke.Clone(newId: false);
        copy.Points = posed;
        copy.RestPoints = [.. dense];
        // A path maps points; these points just moved without it (see Stroke.Path).
        copy.Path = null;
        if (stroke.Holes is { } holes)
        {
            copy.Holes = [];
            foreach (var hole in holes)
            {
                var moving = new List<StrokePoint>(hole.Count);
                foreach (var p in hole)
                {
                    var (x, y) = PinSolve.MapPoint(mesh, deformed, p.X, p.Y);
                    moving.Add(p with { X = x, Y = y });
                }
                copy.Holes.Add(moving);
            }
        }
        return copy;
    }
}

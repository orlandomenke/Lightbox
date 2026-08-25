using Lightbox.Core.Documents;

namespace Lightbox.Raster.Media;

/// <summary>
/// Iso-contours of a scalar field, as closed loops with the crossing placed
/// where the field actually crosses the level.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not <c>ContourTracer</c>.</b> The existing tracer walks a boolean mask
/// pixel by pixel, so every vertex lands on a pixel centre. That is right for a
/// selection, whose edge really is a pixel boundary, and wrong here: a 192×108
/// field traced into a 1920×1080 document would put every vertex on a 10 px
/// lattice and the staircase would be ten pixels wide. Interpolating along the
/// cell edge puts the vertex where the field crosses the level, so the line is
/// smooth at any output scale and the grid can stay coarse — which is the whole
/// reason the simulation is affordable.
/// </para>
/// <para>
/// <b>The field is padded with <c>outside</c>.</b> Samples sit at cell centres,
/// so the marching grid is offset half a cell and cannot otherwise reach the
/// border — a plume touching the edge of its element would trace an open curve,
/// and an open curve cannot be filled. Marching one cell beyond the field in
/// every direction, with anything off the grid reading as <c>outside</c>, makes
/// every loop closed by construction.
/// </para>
/// <para>
/// <b>Deterministic, like everything that reaches pixels.</b> Fixed traversal,
/// no RNG, and the saddle cases are decided by the cell's own average rather
/// than by a rule that could go either way. Loops come out in a fixed order —
/// the order their first crossed edge appears in the edge arrays — so two runs
/// produce identical stroke lists, not merely identical pictures.
/// </para>
/// <para>
/// Output is in <b>cell units</b>: sample <c>(i,j)</c> sits at
/// <c>(i+0.5, j+0.5)</c>, matching <see cref="FluidSolver"/>'s convention, so a
/// caller maps to the document with one affine transform.
/// </para>
/// </remarks>
public sealed class MarchingSquares
{
    // Corner grid is the field padded by one on every side, so corner (ci,cj)
    // is field sample (ci-1, cj-1) and out-of-range reads as `outside`.
    private int _w;
    private int _h;
    private int _cw;      // corner columns: _w + 2
    private int _ch;      // corner rows:    _h + 2
    private int _hCount;  // horizontal edge ids, which vertical ids follow

    private float[] _px = [];
    private float[] _py = [];
    private int[] _next = [];
    private bool[] _walked = [];

    /// <summary>
    /// Every closed loop where <paramref name="field"/> crosses
    /// <paramref name="level"/>, in cell units.
    /// </summary>
    /// <param name="outside">
    /// What lies beyond the field. Zero is right for density and temperature,
    /// both of which are zero in empty air — and it is what closes the loops.
    /// </param>
    public List<List<StrokePoint>> Trace(
        ReadOnlySpan<float> field, int width, int height, float level, float outside = 0f)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (field.Length < width * height)
        {
            throw new ArgumentException(
                $"Field must hold {width}×{height} = {width * height} samples, got {field.Length}.",
                nameof(field));
        }

        Size(width, height);

        var loops = new List<List<StrokePoint>>();
        var crossed = false;

        // One pass over the marching cells: interpolate the crossings and record,
        // per crossed edge, which edge the contour leaves for. Every crossed edge
        // is the source of exactly one directed segment, so a single `next` array
        // describes the whole contour set — including saddles, where a cell emits
        // two segments from two different sources.
        for (var cj = 0; cj < _ch - 1; cj++)
        {
            for (var ci = 0; ci < _cw - 1; ci++)
            {
                var a = Corner(field, ci, cj, outside);
                var b = Corner(field, ci + 1, cj, outside);
                var c = Corner(field, ci + 1, cj + 1, outside);
                var d = Corner(field, ci, cj + 1, outside);

                var code = (a >= level ? 1 : 0) | (b >= level ? 2 : 0)
                         | (c >= level ? 4 : 0) | (d >= level ? 8 : 0);
                if (code is 0 or 15) continue;
                crossed = true;

                var top = HEdge(ci, cj);
                var right = VEdge(ci + 1, cj);
                var bottom = HEdge(ci, cj + 1);
                var left = VEdge(ci, cj);

                // Interpolate only the edges this cell needs; an edge shared with
                // a neighbour is written twice with the same value, which is
                // cheaper than tracking whether it was done.
                if ((code & 1) != (code & 2) >> 1) Cross(top, ci, cj, ci + 1, cj, a, b, level);
                if ((code & 2) >> 1 != (code & 4) >> 2) Cross(right, ci + 1, cj, ci + 1, cj + 1, b, c, level);
                if ((code & 8) >> 3 != (code & 4) >> 2) Cross(bottom, ci, cj + 1, ci + 1, cj + 1, d, c, level);
                if ((code & 1) != (code & 8) >> 3) Cross(left, ci, cj, ci, cj + 1, a, d, level);

                // Walking direction keeps the inside on the left. Two cases are
                // ambiguous — opposite corners in, the other two out — and the
                // cell's own average decides whether the inside joins through the
                // middle. Deciding it by a fixed rule instead would connect a
                // pinch the wrong way round, which reads as two blobs fusing a
                // frame early or late.
                switch (code)
                {
                    case 1: Link(left, top); break;
                    case 2: Link(top, right); break;
                    case 3: Link(left, right); break;
                    case 4: Link(right, bottom); break;
                    case 6: Link(top, bottom); break;
                    case 7: Link(left, bottom); break;
                    case 8: Link(bottom, left); break;
                    case 9: Link(bottom, top); break;
                    case 11: Link(bottom, right); break;
                    case 12: Link(right, left); break;
                    case 13: Link(right, top); break;
                    case 14: Link(top, left); break;

                    case 5:
                        if ((a + b + c + d) * 0.25f >= level) { Link(left, bottom); Link(right, top); }
                        else { Link(left, top); Link(right, bottom); }
                        break;

                    case 10:
                        if ((a + b + c + d) * 0.25f >= level) { Link(top, right); Link(bottom, left); }
                        else { Link(top, left); Link(bottom, right); }
                        break;
                }
            }
        }

        if (!crossed) return loops;

        // Follow each chain back to where it started. A well-formed marching
        // squares field has no dangling ends, but the walk is bounded anyway:
        // a corrupt `next` would otherwise spin forever on a malformed field.
        var edges = _next.Length;
        for (var e = 0; e < edges; e++)
        {
            if (_next[e] < 0 || _walked[e]) continue;

            var loop = new List<StrokePoint>();
            var at = e;
            var guard = edges + 1;
            while (at >= 0 && !_walked[at] && guard-- > 0)
            {
                _walked[at] = true;
                loop.Add(new StrokePoint(_px[at], _py[at], 1));
                at = _next[at];
            }

            // Three points is the smallest thing that encloses any area; two is
            // a crossing the simplifier would drop anyway.
            if (loop.Count >= 3) loops.Add(loop);
        }

        return loops;
    }

    private void Size(int width, int height)
    {
        _w = width;
        _h = height;
        _cw = width + 2;
        _ch = height + 2;

        _hCount = (_cw - 1) * _ch;
        var total = _hCount + _cw * (_ch - 1);

        if (_next.Length < total)
        {
            _px = new float[total];
            _py = new float[total];
            _next = new int[total];
            _walked = new bool[total];
        }

        _next.AsSpan(0, total).Fill(-1);
        _walked.AsSpan(0, total).Clear();
    }

    /// <summary>Corner (ci,cj) is field sample (ci-1, cj-1); off the field reads as <paramref name="outside"/>.</summary>
    private float Corner(ReadOnlySpan<float> field, int ci, int cj, float outside)
    {
        var x = ci - 1;
        var y = cj - 1;
        return (uint)x >= (uint)_w || (uint)y >= (uint)_h ? outside : field[y * _w + x];
    }

    private int HEdge(int ci, int cj) => cj * (_cw - 1) + ci;

    private int VEdge(int ci, int cj) => _hCount + cj * _cw + ci;

    /// <summary>Place the crossing on an edge, in cell units.</summary>
    private void Cross(int edge, int ci0, int cj0, int ci1, int cj1, float v0, float v1, float level)
    {
        // Equal endpoints cannot be crossed by any level; halfway is the only
        // answer that does not depend on which way the subtraction went.
        var span = v1 - v0;
        var t = Math.Abs(span) < 1e-20f ? 0.5f : (level - v0) / span;
        t = t < 0f ? 0f : t > 1f ? 1f : t;

        // Corner (ci,cj) is sample (ci-1, cj-1), whose centre is at
        // (ci-1+0.5, cj-1+0.5) = (ci-0.5, cj-0.5) in cell units.
        var x0 = ci0 - 0.5f;
        var y0 = cj0 - 0.5f;
        _px[edge] = x0 + (ci1 - 0.5f - x0) * t;
        _py[edge] = y0 + (cj1 - 0.5f - y0) * t;
    }

    private void Link(int from, int to) => _next[from] = to;
}

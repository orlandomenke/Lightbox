using Lightbox.Core.Documents;

namespace Lightbox.Raster.Media;

/// <summary>
/// An incompressible fluid on a staggered grid, carrying density and
/// temperature: the solver behind fire, smoke and steam effects elements.
/// Stam's "Stable Fluids" (SIGGRAPH 1999) with Fedkiw's vorticity confinement,
/// and nothing else.
///
/// <para><b>This is not <see cref="FluidLattice"/>, and the difference is the
/// model rather than the speed.</b> That one is shallow water: a film flowing
/// down its own surface slope over paper, held by a capillary entry pressure,
/// trading pigment with the sheet. It is right for a wash sitting on paper and
/// wrong for a plume rising through air, which has no free surface, no
/// substrate and no pigment to strand. What carries over is everything that
/// took the time to get right — the staggered grid, the fixed-iteration
/// pressure solve, allocate-once-and-rent, and the determinism stance
/// below.</para>
///
/// <para><b>Momentum is resampled, matter is moved.</b> Velocity advects
/// semi-Lagrangian, which is unconditionally stable and where conservation buys
/// nothing; density and temperature move as face fluxes, which is exactly
/// conservative and where it is the whole point. The two halves are chosen for
/// different properties on purpose — see <see cref="TransportScalars"/> for the
/// measurement that forced the split.</para>
///
/// <para><b>Nothing here draws.</b> The solver produces fields; turning a field
/// into strokes is the next stage's job and lives elsewhere, because the
/// contour is the deliverable and the pixels never are. See
/// <c>docs/DESIGN-fluid-effects.md</c>.</para>
///
/// Three properties are load-bearing:
///
/// <list type="number">
/// <item><b>Determinism.</b> Fixed iteration counts, fixed row-major traversal,
/// no RNG, no clock, no parallelism. Variation comes from
/// <see cref="DeterministicHash"/> over cell position, which is invariant 2
/// exactly as the brush engine keeps it. Two runs with the same inputs are
/// bit-identical, so a re-bake, a reload and an AI inbetween agree.</item>
/// <item><b>Stepping is the unit, not the call.</b> The step counter is solver
/// state, so <c>Run(4)</c> then <c>Run(6)</c> leaves exactly what <c>Run(10)</c>
/// would. A bake can therefore be chunked for progress and cancellation without
/// changing a pixel — and, more to the point, cannot silently produce a
/// different picture depending on how it was chunked.</item>
/// <item><b>Bounded work.</b> O(width × height × steps) with every buffer taken
/// in the constructor. An element sizes the grid to the region it occupies, and
/// the grid is deliberately far coarser than the document: fluid is
/// low-frequency, and it is the traced contour that lands at full
/// resolution.</item>
/// </list>
///
/// <para><b>Grid.</b> Density and temperature live at cell centres; velocity
/// lives on cell faces (a MAC grid). <c>u[y·(w+1)+x]</c> is flow across the
/// boundary between cell <c>x−1</c> and cell <c>x</c>, positive toward +X;
/// <c>v[y·w+x]</c> is the same vertically, positive toward +Y. <see cref="FluidLattice"/>
/// makes the argument for faces over cell-centred vectors in full and it
/// applies here unchanged: a single vector per cell cannot represent fluid
/// leaving a local peak in all four directions at once. Cell <c>(x,y)</c>
/// spans <c>[x,x+1] × [y,y+1]</c> in the cell units every sample below uses,
/// so its centre is at <c>(x+0.5, y+0.5)</c>.</para>
///
/// <para><b>Walls are solid and free-slip on all four sides.</b> Normal
/// velocity is pinned to zero at the boundary faces, which makes the pressure
/// solve exactly solvable — the divergences telescope to zero — and stops an
/// element quietly leaking mass at its edge. An open top, so a plume can
/// actually leave, is an emitter-stage concern and is deliberately not built
/// speculatively here.</para>
/// </summary>
public sealed class FluidSolver
{
    // ---- solver constants ---------------------------------------------------
    // Compile-time on purpose, for the reason FluidLattice gives: these are the
    // parts of the model that must not vary at render time, because a document
    // that renders differently after a preference change is a defect
    // (invariant 4).

    /// <summary>
    /// Jacobi sweeps on the pressure. Fixed, never a convergence test — a solve
    /// that runs to tolerance is a solve whose output depends on floating-point
    /// luck, and two machines would then disagree about the same document.
    /// </summary>
    /// <remarks>
    /// <b>Sixteen because it was measured, not because it is round.</b> Residual
    /// divergence after ten steps, as a fraction of the mean velocity, against
    /// the cost of the advect-and-project pass at 192×108:
    ///
    /// <code>
    ///   12 sweeps   4.2%   2.7 ms
    ///   16 sweeps   3.6%   3.2 ms
    ///   24 sweeps   2.7%   4.0 ms
    ///   32 sweeps   2.1%   4.0 ms
    /// </code>
    ///
    /// The returns are plainly diminishing and the medium-simulation rule
    /// decides the rest: where a cheap approximation and an accurate one look
    /// the same to a person, the cheap one is correct. A few percent residual
    /// divergence reads as nothing in smoke — it is a slight compression, not a
    /// shape — while a third more solver time is a third more of an artist's
    /// wait on every bake. This is the honest place to say that <em>nothing has
    /// yet been looked at</em>: there is no renderer until the next step, so the
    /// number is chosen on the measurement above and is the first thing to
    /// revisit against an actual picture.
    /// </remarks>
    private const int PressureSweeps = 16;

    /// <summary>
    /// CFL bound, in cells per step. Semi-Lagrangian advection is stable past
    /// this, but a backtrace that reaches further than a cell steps over
    /// features it should have carried, and the result reads as the fluid
    /// teleporting. Callers buy speed with substeps instead.
    /// </summary>
    private const float MaxSpeed = 1f;

    /// <summary>Below this a curl is noise, and normalising it would amplify the noise.</summary>
    private const float CurlEps = 1e-6f;

    /// <summary>
    /// A cell may never send away more than this fraction of what it holds in
    /// one step. Keeping a tenth back is what makes the scalar transport
    /// unconditionally non-negative, so no quantity can ring negative and then
    /// blow up — <see cref="FluidLattice"/> takes the same precaution for the
    /// same reason. In a divergence-free flow inside the CFL bound the sum of a
    /// cell's outgoing fractions is typically well under this, so the limiter is
    /// a guarantee rather than a routine throttle.
    /// </summary>
    private const float MaxOutflow = 0.9f;

    private int _w;
    private int _h;
    private int _uw;      // faces per row: _w + 1

    /// <summary>
    /// Steps advanced since the last <see cref="Reset"/>. Solver state rather
    /// than a loop variable, because the turbulence seeds from it: were it a
    /// local, a run split for progress reporting would re-seed the noise from
    /// zero and produce a different picture from the same parameters.
    /// </summary>
    private int _step;

    /// <summary>Cells the buffers were sized for — see <see cref="Rent"/>.</summary>
    private readonly int _capacity;

    // Swapped, never reallocated: advection reads one buffer and writes the other.
    private float[] _u;
    private float[] _v;
    private float[] _uB;
    private float[] _vB;
    private float[] _density;
    private float[] _densityB;
    private float[] _heat;
    private float[] _heatB;

    // Ping-ponged by the Jacobi sweep rather than copied: twenty-four copies of
    // the pressure field per step is a megabyte of memmove that buys nothing.
    private float[] _p;
    private float[] _pB;
    private readonly float[] _div;
    private readonly float[] _curl;
    private readonly float[] _psi;   // curl-noise potential, one sample per cell
    private readonly float[] _fx;    // per-cell body force, X
    private readonly float[] _fy;    // per-cell body force, Y
    private readonly float[] _outflow;  // per-cell transport limiter

    public FluidSolver(int width, int height) : this(width, height, width * height) { }

    private FluidSolver(int width, int height, int capacity)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        _capacity = Math.Max(capacity, width * height);
        Resize(width, height);

        var n = _capacity;

        // Faces need one more column or row than cells, and the capacity has to
        // cover the worst shape it could later be asked for — a single row is
        // the extreme, where faces outnumber cells by one.
        var faces = _capacity + Math.Max(width, height) + 1;

        _u = new float[faces];
        _v = new float[faces];
        _uB = new float[faces];
        _vB = new float[faces];
        _density = new float[n];
        _densityB = new float[n];
        _heat = new float[n];
        _heatB = new float[n];
        _p = new float[n];
        _pB = new float[n];
        _div = new float[n];
        _curl = new float[n];
        _psi = new float[n];
        _fx = new float[n];
        _fy = new float[n];
        _outflow = new float[n];
    }

    private void Resize(int width, int height)
    {
        _w = width;
        _h = height;
        _uw = width + 1;
    }

    /// <summary>
    /// A solver of this size, reusing the last one when it is big enough.
    /// </summary>
    /// <remarks>
    /// Fourteen float buffers a cell puts a 320×180 solver over three megabytes
    /// of large-object-heap arrays, and a bake runs one solver for a whole
    /// element. Reused rather than pooled by size, per thread, and always
    /// cleared — a solver carrying the last element's smoke would bake a
    /// different picture, which is invariant 2 through the back door.
    /// <see cref="FluidLattice.Rent"/> paid for this lesson first.
    /// </remarks>
    public static FluidSolver Rent(int width, int height)
    {
        var cells = width * height;
        var faces = cells + Math.Max(width, height) + 1;
        var cached = _cache;
        if (cached is not null && cached._capacity >= cells && cached._u.Length >= faces)
        {
            cached.Resize(width, height);
            cached.Reset();
            return cached;
        }

        var solver = new FluidSolver(width, height, Math.Max(cells, 64 * 64));
        _cache = solver;
        return solver;
    }

    [ThreadStatic]
    private static FluidSolver? _cache;

    /// <summary>Back to still, cold, empty air — and back to step zero.</summary>
    public void Reset()
    {
        var n = _w * _h;
        var faces = Math.Min(_u.Length, n + Math.Max(_w, _h) + 1);

        _u.AsSpan(0, faces).Clear();
        _v.AsSpan(0, faces).Clear();
        _uB.AsSpan(0, faces).Clear();
        _vB.AsSpan(0, faces).Clear();
        _density.AsSpan(0, n).Clear();
        _densityB.AsSpan(0, n).Clear();
        _heat.AsSpan(0, n).Clear();
        _heatB.AsSpan(0, n).Clear();
        _p.AsSpan(0, n).Clear();
        _pB.AsSpan(0, n).Clear();
        _div.AsSpan(0, n).Clear();
        _curl.AsSpan(0, n).Clear();
        _psi.AsSpan(0, n).Clear();
        _fx.AsSpan(0, n).Clear();
        _fy.AsSpan(0, n).Clear();
        _outflow.AsSpan(0, n).Clear();

        _step = 0;
    }

    public int Width => _w;

    public int Height => _h;

    /// <summary>Steps advanced since the last <see cref="Reset"/>.</summary>
    public int Step => _step;

    // ---- seeding ------------------------------------------------------------
    // Out-of-range cells are ignored rather than throwing, so an emitter
    // straddling the edge does not have to clip itself first.

    /// <summary>Add smoke at a cell.</summary>
    public void AddDensity(int x, int y, float amount)
    {
        if ((uint)x >= (uint)_w || (uint)y >= (uint)_h) return;
        _density[y * _w + x] += Clamped(amount, 0f, float.MaxValue);
    }

    /// <summary>Add heat at a cell. Heat is what makes fluid rise.</summary>
    public void AddHeat(int x, int y, float amount)
    {
        if ((uint)x >= (uint)_w || (uint)y >= (uint)_h) return;
        _heat[y * _w + x] += Clamped(amount, 0f, float.MaxValue);
    }

    /// <summary>
    /// Push the fluid at a cell, in cells per step. Applied to both faces of the
    /// cell so the impulse lands on the cell rather than on one of its sides.
    /// </summary>
    /// <remarks>
    /// Deliberately does not enforce the walls: an emitter seeds thousands of
    /// cells a frame, and a boundary sweep per seeded cell would make seeding
    /// cost O(cells × (width + height)) for a correction the next step makes
    /// anyway. Both <see cref="Project"/> and the advection pass end with one.
    /// </remarks>
    public void AddVelocity(int x, int y, float vx, float vy)
    {
        if ((uint)x >= (uint)_w || (uint)y >= (uint)_h) return;
        _u[y * _uw + x] += vx;
        _u[y * _uw + x + 1] += vx;
        _v[y * _w + x] += vy;
        _v[(y + 1) * _w + x] += vy;
    }

    /// <summary>Advance the simulation. Cost is O(width × height × steps).</summary>
    /// <remarks>
    /// Takes the document's own <see cref="SimParams"/> rather than a type of its
    /// own, for the reason <c>BrushEngine</c> takes a <c>BrushSettings</c>: two
    /// copies of the same numbers would drift, and a solver that owned its
    /// parameters would make every tuning change a file-format change.
    /// </remarks>
    public void Run(int steps, SimParams p)
    {
        ArgumentNullException.ThrowIfNull(p);

        // Exact no-op, not "a step that happens to change nothing": callers
        // derive step counts from settings, and 0 has to mean untouched.
        if (steps <= 0) return;

        var buoyancy = Clamped((float)p.Buoyancy, 0f, 100f);
        var weight = Clamped((float)p.Weight, 0f, 100f);
        var vorticity = Clamped((float)p.Vorticity, 0f, 100f);
        var turbulence = Clamped((float)p.Turbulence, 0f, 100f);
        var scale = Clamped((float)p.TurbulenceScale, 1f, 4096f);
        var drift = Clamped((float)p.TurbulenceDrift, -16f, 16f);
        var dissipation = Clamped((float)p.Dissipation, 0f, 1f);
        var cooling = Clamped((float)p.Cooling, 0f, 1f);
        var drag = Clamped((float)p.Drag, 0f, 1f);

        // The order is Stam's advect-force-project and it is not a knob.
        // Confinement reads the field *after* advection because restoring the
        // curl advection just ate is the entire point of it, and projection
        // comes last so the scalars ride a divergence-free field — advecting
        // smoke through a compressible one is what makes it pile up and thin
        // out in patches.
        for (var s = 0; s < steps; s++)
        {
            AdvectVelocity();
            ApplyBuoyancy(buoyancy, weight);
            if (vorticity > 0 || turbulence > 0) ApplyBodyForces(vorticity, turbulence, scale, drift);
            if (drag > 0) Damp(drag);
            LimitSpeed();
            Project();
            TransportScalars();
            if (dissipation > 0 || cooling > 0) Dissipate(dissipation, cooling);
            _step++;
        }
    }

    // ---- steps --------------------------------------------------------------

    /// <summary>
    /// Bleed a fraction of the flow away each step: the air's own viscosity.
    /// </summary>
    /// <remarks>
    /// Before the projection, so what it leaves behind is still divergence-free.
    /// Uniform scaling cannot introduce divergence on its own, but the clamp
    /// beside it can, and one solve tidies up after both.
    /// </remarks>
    private void Damp(float drag)
    {
        var keep = 1f - drag;
        var uFaces = _uw * _h;
        for (var i = 0; i < uFaces; i++) _u[i] *= keep;

        var vFaces = _w * (_h + 1);
        for (var i = 0; i < vFaces; i++) _v[i] *= keep;
    }

    /// <summary>
    /// Hold every face inside the one-cell-per-step bound the advection is only
    /// valid within, before the pressure solve rather than after it, so the
    /// projection cleans up the divergence the clamp introduces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured, because leaving this out failed quietly.</b> The backtrace
    /// already clamped its own displacement, so a runaway field did not blow up
    /// or produce a NaN — it produced a velocity the transport then ignored,
    /// and the scalar step degenerated into many cells sampling the same source.
    /// A buoyant plume at these settings reached 2.4 cells per step and came out
    /// carrying <b>158%</b> of the density it started with; held to the bound it
    /// stays within a few percent. Mass appearing from nowhere is not a
    /// judgement call about a picture, and it is the kind of failure that would
    /// have been found much later and blamed on the emitter.
    /// </para>
    /// <para>
    /// A clamp rather than a drag term because it has an exact meaning — this is
    /// the solver's resolution limit, not a tunable — and because a drag would
    /// be one more knob whose right value nobody could state. <b>Speed is bought
    /// with substeps</b>: halve the step and the same motion fits inside the
    /// bound twice over.
    /// </para>
    /// </remarks>
    private void LimitSpeed()
    {
        var uFaces = _uw * _h;
        for (var i = 0; i < uFaces; i++) _u[i] = Clamped(_u[i], -MaxSpeed, MaxSpeed);

        var vFaces = _w * (_h + 1);
        for (var i = 0; i < vFaces; i++) _v[i] = Clamped(_v[i], -MaxSpeed, MaxSpeed);
    }

    private void AdvectVelocity()
    {
        // Faces first into the scratch buffers, then swapped — a face read
        // after it had been written would advect this step's answer through
        // itself, which reads as the fluid accelerating for free.
        for (var y = 0; y < _h; y++)
        {
            for (var x = 0; x <= _w; x++)
            {
                var i = y * _uw + x;
                float px = x, py = y + 0.5f;
                var (dx, dy) = Backtrace(_u[i], SampleV(px, py));
                _uB[i] = SampleU(px - dx, py - dy);
            }
        }

        for (var y = 0; y <= _h; y++)
        {
            for (var x = 0; x < _w; x++)
            {
                var i = y * _w + x;
                float px = x + 0.5f, py = y;
                var (dx, dy) = Backtrace(SampleU(px, py), _v[i]);
                _vB[i] = SampleV(px - dx, py - dy);
            }
        }

        (_u, _uB) = (_uB, _u);
        (_v, _vB) = (_vB, _v);
        EnforceWalls();
    }

    /// <summary>
    /// Heat lifts, smoke sinks. Applied to the vertical faces because that is
    /// where a vertical force belongs on a staggered grid; putting it at cell
    /// centres and averaging out would smear it over three rows.
    /// </summary>
    private void ApplyBuoyancy(float buoyancy, float weight)
    {
        if (buoyancy <= 0 && weight <= 0) return;

        // Interior faces only: the boundary ones are pinned to zero, and a
        // force written there would be erased by EnforceWalls anyway.
        for (var y = 1; y < _h; y++)
        {
            for (var x = 0; x < _w; x++)
            {
                var above = (y - 1) * _w + x;
                var below = y * _w + x;
                var t = 0.5f * (_heat[above] + _heat[below]);
                var d = 0.5f * (_density[above] + _density[below]);
                _v[y * _w + x] += weight * d - buoyancy * t;
            }
        }
    }

    /// <summary>
    /// Vorticity confinement and seeded turbulence, accumulated per cell and
    /// then handed to the faces in one pass. Both are body forces and neither
    /// needs its own traversal.
    /// </summary>
    private void ApplyBodyForces(float vorticity, float turbulence, float scale, float drift)
    {
        var n = _w * _h;
        _fx.AsSpan(0, n).Clear();
        _fy.AsSpan(0, n).Clear();

        if (vorticity > 0) AccumulateConfinement(vorticity);
        if (turbulence > 0) AccumulateTurbulence(turbulence, scale, drift);

        for (var y = 1; y < _h; y++)
        {
            for (var x = 0; x < _w; x++)
            {
                _v[y * _w + x] += 0.5f * (_fy[(y - 1) * _w + x] + _fy[y * _w + x]);
            }
        }

        for (var y = 0; y < _h; y++)
        {
            for (var x = 1; x < _w; x++)
            {
                _u[y * _uw + x] += 0.5f * (_fx[y * _w + x - 1] + _fx[y * _w + x]);
            }
        }
    }

    /// <summary>
    /// Fedkiw's vorticity confinement: find where the curl is concentrated,
    /// then push along the vector that spins fluid back into it. Semi-Lagrangian
    /// advection is unconditionally stable precisely because it averages, and
    /// averaging destroys small vortices — a plume without this smooths into a
    /// cone within a dozen steps and stops reading as fire at all.
    /// </summary>
    private void AccumulateConfinement(float strength)
    {
        for (var y = 0; y < _h; y++)
        {
            for (var x = 0; x < _w; x++)
            {
                _curl[y * _w + x] = CurlAt(x, y);
            }
        }

        // The boundary ring is skipped rather than one-sided: |curl| there is
        // dominated by the wall, so a gradient taken across it points into the
        // wall and confinement would pin the fluid to the edge.
        for (var y = 1; y < _h - 1; y++)
        {
            for (var x = 1; x < _w - 1; x++)
            {
                var i = y * _w + x;
                var gx = 0.5f * (Math.Abs(_curl[i + 1]) - Math.Abs(_curl[i - 1]));
                var gy = 0.5f * (Math.Abs(_curl[i + _w]) - Math.Abs(_curl[i - _w]));
                var len = MathF.Sqrt(gx * gx + gy * gy);
                if (len < CurlEps) continue;

                var nx = gx / len;
                var ny = gy / len;
                _fx[i] += strength * ny * _curl[i];
                _fy[i] -= strength * nx * _curl[i];
            }
        }
    }

    /// <summary>
    /// Seeded turbulence, as the curl of a noise potential rather than as noise
    /// added to the velocity directly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Curl noise is divergence-free by construction</b>, so the projection
    /// has nothing to remove and every bit of the force survives into the
    /// picture. Noise added straight to a velocity field is mostly divergence,
    /// which means the pressure solve spends its sweeps deleting the thing that
    /// was just added — the fluid does not move much and the solver looks
    /// slower than it is.
    /// </para>
    /// <para>
    /// <b>The potential moves through the noise rather than re-seeding.</b>
    /// Salting by step index would give a field uncorrelated with the last one,
    /// which is white noise in time: it reads as a shimmer rather than as
    /// turbulence, and it is exactly the flicker the charter forbids. Drifting
    /// the sample point instead keeps the field smooth in time and in space,
    /// while staying a pure function of the step number.
    /// </para>
    /// </remarks>
    private void AccumulateTurbulence(float strength, float scale, float drift)
    {
        var t = _step * drift;
        var inv = 1f / scale;

        // The four lattice hashes only change when the scan crosses an integer
        // noise cell, which at any useful scale is every eighth pixel or worse.
        // Recomputing them per cell made the noise cost as much as the whole
        // advection pass; the arithmetic per cell is unchanged.
        for (var y = 0; y < _h; y++)
        {
            var gy = (y + 0.5f) * inv;
            var y0 = MathF.Floor(gy);
            var ty = Smoothstep(gy - y0);

            var held = float.NaN;
            float a = 0f, b = 0f, c = 0f, d = 0f;

            for (var x = 0; x < _w; x++)
            {
                var gx = (x + 0.5f) * inv + t;
                var x0 = MathF.Floor(gx);
                if (x0 != held)
                {
                    a = Lattice(x0, y0);
                    b = Lattice(x0 + 1f, y0);
                    c = Lattice(x0, y0 + 1f);
                    d = Lattice(x0 + 1f, y0 + 1f);
                    held = x0;
                }

                var tx = Smoothstep(gx - x0);
                var top = a + (b - a) * tx;
                var bottom = c + (d - c) * tx;
                _psi[y * _w + x] = top + (bottom - top) * ty;
            }
        }

        // Central differences of the stored potential rather than four more
        // noise evaluations per face: the potential is the expensive part, and
        // one sample per cell is 8× cheaper than sampling per face per axis.
        for (var y = 1; y < _h - 1; y++)
        {
            for (var x = 1; x < _w - 1; x++)
            {
                var i = y * _w + x;
                _fx[i] += strength * 0.5f * (_psi[i + _w] - _psi[i - _w]);
                _fy[i] -= strength * 0.5f * (_psi[i + 1] - _psi[i - 1]);
            }
        }
    }

    /// <summary>
    /// Remove the divergence: solve ∇²p = ∇·u with Neumann boundaries, then
    /// subtract ∇p. This is what makes the fluid incompressible, and it is the
    /// difference between smoke that billows and smoke that inflates.
    /// </summary>
    /// <remarks>
    /// <b>Jacobi rather than Gauss-Seidel</b>, which is where this parts company
    /// with <see cref="FluidLattice"/>. Gauss-Seidel converges faster per sweep
    /// and reads its own partial results, so a row-major sweep biases the answer
    /// along the sweep direction: a left–right symmetric setup comes out
    /// slightly asymmetric. That is invisible in a watercolour bloom and it
    /// costs a test here — an exactly symmetric solver can be asserted exactly
    /// symmetric, which catches the index slips that are otherwise found by
    /// looking at a picture and thinking it seems a bit off. The extra sweeps
    /// are cheap at these grid sizes and the property is not.
    /// </remarks>
    private void Project()
    {
        var n = _w * _h;
        for (var y = 0; y < _h; y++)
        {
            for (var x = 0; x < _w; x++)
            {
                var i = y * _w + x;
                _div[i] = _u[y * _uw + x + 1] - _u[y * _uw + x]
                        + _v[(y + 1) * _w + x] - _v[y * _w + x];
            }
        }

        // Zeroed each step rather than warm-started from the last one. Pressure
        // under all-Neumann boundaries is only defined up to a constant, so a
        // carried-over field is free to wander off while describing the same
        // flow — harmless for the gradient and ruinous for a bit-identical
        // comparison, which is the one property most worth keeping.
        _p.AsSpan(0, n).Clear();

        // Split into an unbranched interior and its boundary ring. Jacobi reads
        // only the previous iterate, so cell order cannot affect the answer and
        // this is bit-identical to one branchy pass — it is simply where the
        // solver's time was going: four bounds tests per cell, twenty-four
        // sweeps a step, half a million cells a sweep at working sizes.
        for (var sweep = 0; sweep < PressureSweeps; sweep++)
        {
            float[] p = _p, pb = _pB, div = _div;

            for (var y = 1; y < _h - 1; y++)
            {
                var row = y * _w;
                for (var x = 1; x < _w - 1; x++)
                {
                    var i = row + x;
                    pb[i] = (p[i - 1] + p[i + 1] + p[i - _w] + p[i + _w] - div[i]) * 0.25f;
                }
            }

            RelaxEdges(p, pb, div);
            (_p, _pB) = (_pB, _p);
        }

        for (var y = 0; y < _h; y++)
        {
            for (var x = 1; x < _w; x++)
            {
                _u[y * _uw + x] -= _p[y * _w + x] - _p[y * _w + x - 1];
            }
        }

        for (var y = 1; y < _h; y++)
        {
            for (var x = 0; x < _w; x++)
            {
                _v[y * _w + x] -= _p[y * _w + x] - _p[(y - 1) * _w + x];
            }
        }

        EnforceWalls();
    }

    /// <summary>
    /// The boundary ring of one Jacobi sweep, where the Neumann condition needs
    /// a test. A neighbour outside the wall mirrors this cell, so it contributes
    /// <c>p[i]</c> and the five-point stencil stays balanced.
    /// </summary>
    private void RelaxEdges(float[] p, float[] pb, float[] div)
    {
        for (var x = 0; x < _w; x++)
        {
            RelaxCell(p, pb, div, x, 0);
            if (_h > 1) RelaxCell(p, pb, div, x, _h - 1);
        }

        for (var y = 1; y < _h - 1; y++)
        {
            RelaxCell(p, pb, div, 0, y);
            if (_w > 1) RelaxCell(p, pb, div, _w - 1, y);
        }
    }

    private void RelaxCell(float[] p, float[] pb, float[] div, int x, int y)
    {
        var i = y * _w + x;
        var l = x > 0 ? p[i - 1] : p[i];
        var r = x < _w - 1 ? p[i + 1] : p[i];
        var d = y > 0 ? p[i - _w] : p[i];
        var b = y < _h - 1 ? p[i + _w] : p[i];
        pb[i] = (l + r + d + b - div[i]) * 0.25f;
    }

    /// <summary>
    /// Carry density and temperature across the faces as fluxes: whatever leaves
    /// one cell arrives in exactly one other. Donor-cell upwinding — the value
    /// a face moves is the value of whichever cell it is flowing away from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Conservative by construction, and it had to be.</b> This started as
    /// semi-Lagrangian advection — the same backtrace-and-resample the velocity
    /// still uses — which is the textbook choice and is not conservative. It did
    /// not merely drift: the error compounds with flow speed, and measured on a
    /// closed swirl over a hundred steps the density came out at
    /// <b>107% / 141% / 219% / 293%</b> of what went in as the peak speed rose
    /// through 0.17 / 0.41 / 0.77 / 1.02 cells per step. An element that invents
    /// two thirds of its own smoke is not a tuning problem, and worse, it is one
    /// no assertion could have caught after the fact — every cell was a
    /// plausible number.
    /// </para>
    /// <para>
    /// Here a flux is subtracted from one cell and added to precisely one other,
    /// so the total cannot move at all and <c>TotalDensity</c> becomes an
    /// assertion rather than a diagnostic. That is <see cref="FluidLattice"/>'s
    /// argument for its own transport, restated: conservation catches indexing
    /// bugs that no visual check would.
    /// </para>
    /// <para>
    /// <b>What it costs is sharpness</b>, because first-order upwinding is
    /// diffusive — the field smooths as it travels. That is a far cheaper price
    /// here than in a renderer that shows the field as pixels: the deliverable
    /// is a traced iso-contour, so a smoother field yields a smoother line, and
    /// a smoother line boils less. Q116 chose per-frame tracing, which makes
    /// that a benefit rather than a consolation.
    /// </para>
    /// <para>
    /// <b>The walls need no special case.</b> Boundary faces carry zero velocity,
    /// so they carry zero flux, and nothing can cross them however hard the
    /// fluid is driven at them.
    /// </para>
    /// </remarks>
    private void TransportScalars()
    {
        var n = _w * _h;

        // How much of itself each cell would send away, before any limiting.
        // A face is this cell's outgoing one when the flow points away from it.
        for (var y = 0; y < _h; y++)
        {
            for (var x = 0; x < _w; x++)
            {
                var i = y * _w + x;
                var left = _u[y * _uw + x];
                var right = _u[y * _uw + x + 1];
                var top = _v[i];
                var bottom = _v[i + _w];

                var going = 0f;
                if (left < 0f) going -= left;
                if (right > 0f) going += right;
                if (top < 0f) going -= top;
                if (bottom > 0f) going += bottom;

                _outflow[i] = going > MaxOutflow ? MaxOutflow / going : 1f;
            }
        }

        _density.AsSpan(0, n).CopyTo(_densityB.AsSpan(0, n));
        _heat.AsSpan(0, n).CopyTo(_heatB.AsSpan(0, n));

        // Interior faces only — the boundary ones are pinned to zero velocity,
        // so they would move nothing anyway, and skipping them is what makes
        // "nothing crosses a wall" structural rather than checked.
        for (var y = 0; y < _h; y++)
        {
            for (var x = 1; x < _w; x++)
            {
                var flow = _u[y * _uw + x];
                if (flow == 0f) continue;

                var right = y * _w + x;
                var left = right - 1;
                var (donor, receiver) = flow > 0f ? (left, right) : (right, left);
                var share = Math.Abs(flow) * _outflow[donor];

                Move(donor, receiver, share);
            }
        }

        for (var y = 1; y < _h; y++)
        {
            for (var x = 0; x < _w; x++)
            {
                var flow = _v[y * _w + x];
                if (flow == 0f) continue;

                var below = y * _w + x;
                var above = below - _w;
                var (donor, receiver) = flow > 0f ? (above, below) : (below, above);
                var share = Math.Abs(flow) * _outflow[donor];

                Move(donor, receiver, share);
            }
        }

        (_density, _densityB) = (_densityB, _density);
        (_heat, _heatB) = (_heatB, _heat);
    }

    /// <summary>
    /// Move one share of a donor cell's contents into a receiver. Read from the
    /// pre-step field and written to the accumulator, so every face this step
    /// sees the same starting state and the order faces are visited in cannot
    /// change the answer.
    /// </summary>
    private void Move(int donor, int receiver, float share)
    {
        var d = _density[donor] * share;
        _densityB[donor] -= d;
        _densityB[receiver] += d;

        var h = _heat[donor] * share;
        _heatB[donor] -= h;
        _heatB[receiver] += h;
    }

    private void Dissipate(float dissipation, float cooling)
    {
        var n = _w * _h;
        var keepD = 1f - dissipation;
        var keepH = 1f - cooling;
        for (var i = 0; i < n; i++)
        {
            _density[i] *= keepD;
            _heat[i] *= keepH;
        }
    }

    /// <summary>
    /// Pin the normal velocity at every wall. Free-slip: the tangential
    /// component is left alone, because a no-slip boundary would grow a
    /// viscous layer along the edge of an element that has no physical reason
    /// to be there.
    /// </summary>
    private void EnforceWalls()
    {
        for (var y = 0; y < _h; y++)
        {
            _u[y * _uw] = 0f;
            _u[y * _uw + _w] = 0f;
        }

        for (var x = 0; x < _w; x++)
        {
            _v[x] = 0f;
            _v[_h * _w + x] = 0f;
        }
    }

    // ---- sampling -----------------------------------------------------------

    /// <summary>
    /// Where a parcel arriving here came from, as a displacement clamped to
    /// <see cref="MaxSpeed"/> cells. The clamp is per axis rather than on the
    /// magnitude, so a diagonal flow is not silently slowed relative to an
    /// axis-aligned one of the same speed.
    /// </summary>
    private static (float Dx, float Dy) Backtrace(float vx, float vy) =>
        (Clamped(vx, -MaxSpeed, MaxSpeed), Clamped(vy, -MaxSpeed, MaxSpeed));

    /// <summary>
    /// Bilinear sample of a cell-centred field at a point in cell units,
    /// clamped to the grid.
    /// </summary>
    /// <remarks>
    /// Clamping rather than wrapping or returning zero is what gives the
    /// advection its maximum principle: every output is a convex combination of
    /// four values that were already in the field, so nothing can exceed what
    /// was there before. That turns "did an index slip" into an assertion
    /// instead of a judgement about a picture.
    /// </remarks>
    private float SampleCell(float[] field, float px, float py) =>
        Bilinear(field, px - 0.5f, py - 0.5f, _w, _h, _w);

    private float SampleU(float px, float py) =>
        Bilinear(_u, px, py - 0.5f, _uw, _h, _uw);

    private float SampleV(float px, float py) =>
        Bilinear(_v, px - 0.5f, py, _w, _h + 1, _w);

    private static float Bilinear(float[] field, float gx, float gy, int nx, int ny, int stride)
    {
        gx = Clamped(gx, 0f, nx - 1f);
        gy = Clamped(gy, 0f, ny - 1f);

        var x0 = (int)gx;
        var y0 = (int)gy;
        var x1 = Math.Min(x0 + 1, nx - 1);
        var y1 = Math.Min(y0 + 1, ny - 1);
        var tx = gx - x0;
        var ty = gy - y0;

        var a = field[y0 * stride + x0];
        var b = field[y0 * stride + x1];
        var c = field[y1 * stride + x0];
        var d = field[y1 * stride + x1];

        var top = a + (b - a) * tx;
        var bottom = c + (d - c) * tx;
        return top + (bottom - top) * ty;
    }

    /// <summary>2D curl at a cell centre: ∂v/∂x − ∂u/∂y, one-sided at the walls.</summary>
    private float CurlAt(int x, int y)
    {
        var xl = Math.Max(x - 1, 0);
        var xr = Math.Min(x + 1, _w - 1);
        var yu = Math.Max(y - 1, 0);
        var yd = Math.Min(y + 1, _h - 1);

        var dvdx = (CellV(xr, y) - CellV(xl, y)) / Math.Max(xr - xl, 1);
        var dudy = (CellU(x, yd) - CellU(x, yu)) / Math.Max(yd - yu, 1);
        return dvdx - dudy;
    }

    private float CellU(int x, int y) => 0.5f * (_u[y * _uw + x] + _u[y * _uw + x + 1]);

    private float CellV(int x, int y) => 0.5f * (_v[y * _w + x] + _v[(y + 1) * _w + x]);

    /// <summary>
    /// The noise potential's value at one integer lattice point. Everything
    /// between them is a smoothstep blend of four of these, so the field and its
    /// first derivative are continuous and the curl taken from it does not show
    /// the lattice.
    /// </summary>
    private static float Lattice(float x, float y) => (float)DeterministicHash.Unit(x, y, 101u);

    private static float Smoothstep(float t) => t * t * (3f - 2f * t);

    // ---- reading ------------------------------------------------------------

    public float DensityAt(int x, int y) =>
        (uint)x >= (uint)_w || (uint)y >= (uint)_h ? 0f : _density[y * _w + x];

    public float TemperatureAt(int x, int y) =>
        (uint)x >= (uint)_w || (uint)y >= (uint)_h ? 0f : _heat[y * _w + x];

    /// <summary>Horizontal velocity at a cell centre, in cells per step.</summary>
    public float VelocityXAt(int x, int y) =>
        (uint)x >= (uint)_w || (uint)y >= (uint)_h ? 0f : CellU(x, y);

    /// <summary>Vertical velocity at a cell centre, in cells per step. Positive is down.</summary>
    public float VelocityYAt(int x, int y) =>
        (uint)x >= (uint)_w || (uint)y >= (uint)_h ? 0f : CellV(x, y);

    /// <summary>Density, row-major, length width*height — the field the contour tracer reads.</summary>
    public void ReadDensity(Span<float> into) => Read(_density, into);

    /// <summary>Temperature, row-major, length width*height. Fire bands from this, not from density.</summary>
    public void ReadTemperature(Span<float> into) => Read(_heat, into);

    private void Read(float[] field, Span<float> into)
    {
        var n = _w * _h;
        if (into.Length != n)
        {
            throw new ArgumentException(
                $"Buffer must be {_w}×{_h} = {n} floats, got {into.Length}.", nameof(into));
        }
        field.AsSpan(0, n).CopyTo(into);
    }

    /// <summary>
    /// Total density, summed in fixed row-major order in double so the sum is
    /// itself reproducible. <see cref="TransportScalars"/> only ever moves
    /// density between cells, so nothing but <c>Dissipation</c> may change this
    /// — which makes it the cheapest possible check that the solver is carrying
    /// smoke rather than inventing it.
    /// </summary>
    public double TotalDensity()
    {
        double total = 0;
        var n = _w * _h;
        for (var i = 0; i < n; i++) total += _density[i];
        return total;
    }

    /// <summary>The highest density anywhere on the grid.</summary>
    public float PeakDensity() => Peak(_density);

    /// <summary>The highest temperature anywhere on the grid.</summary>
    public float PeakTemperature() => Peak(_heat);

    private float Peak(float[] field)
    {
        var n = _w * _h;
        var peak = 0f;
        for (var i = 0; i < n; i++) if (field[i] > peak) peak = field[i];
        return peak;
    }

    /// <summary>
    /// Mean |∇·u| over the grid: how compressible the fluid still is after the
    /// pressure solve. Diagnostic rather than decorative — it is the one number
    /// that says whether <see cref="Project"/> is doing its job, and a
    /// projection that quietly stopped working looks like smoke that inflates.
    /// </summary>
    public double MeanAbsDivergence()
    {
        double total = 0;
        for (var y = 0; y < _h; y++)
        {
            for (var x = 0; x < _w; x++)
            {
                var d = _u[y * _uw + x + 1] - _u[y * _uw + x]
                      + _v[(y + 1) * _w + x] - _v[y * _w + x];
                total += Math.Abs(d);
            }
        }
        return total / (_w * _h);
    }

    private static float Clamped(float v, float lo, float hi) =>
        float.IsNaN(v) ? lo : v < lo ? lo : v > hi ? hi : v;
}

namespace Lightbox.Raster.Media;

/// <summary>
/// Tuning for one <see cref="FluidLattice"/> run. Every field is 0..1;
/// anything outside is clamped once at the top of <see cref="FluidLattice.Run"/>
/// rather than per cell.
/// </summary>
/// <param name="Viscosity">Resistance to flow. 0 is water, 1 is oil.</param>
/// <param name="Drag">Friction against the paper. Rough paper drags harder.</param>
/// <param name="Absorbency">How fast the paper pulls pigment out of suspension.</param>
/// <param name="EdgePull">Capillary pull toward the wet boundary — the darkened rim.</param>
/// <param name="Granularity">Bias toward settling in the paper's valleys.</param>
public readonly record struct FluidParams(
    float Viscosity, float Drag, float Absorbency,
    float EdgePull, float Granularity);

/// <summary>
/// A shallow-water layer carrying pigment over a paper height field: a
/// stripped-down Curtis et al. (SIGGRAPH 1997) watercolour model. Water flows
/// down its own surface slope, drags pigment with it, and the paper takes
/// pigment out of suspension and gives some back. Blooms, backruns and the
/// darkened rim all fall out of the flow — none of them is drawn on afterwards.
///
/// Three properties are load-bearing and every design choice below defers to
/// them:
///
/// <list type="number">
/// <item><b>Determinism.</b> Fixed iteration counts, fixed row-major traversal,
/// no RNG, no clock, no parallelism. Two runs with the same inputs are
/// bit-identical, so a reload and an AI inbetween render the same image.</item>
/// <item><b>Conservation.</b> Pigment is only ever <i>moved</i> — between cells,
/// and between suspension and the paper. Nothing in here creates or destroys
/// it, which is why the conservation assertion catches indexing bugs that no
/// visual check would.</item>
/// <item><b>Bounded work.</b> Cost is O(width × height × steps) with every
/// buffer allocated once in the constructor. A stroke sizes the lattice to the
/// region it can reach, so cost tracks the stroke, not the canvas.</item>
/// </list>
///
/// The step order is Curtis's and is fixed: velocity, wet boundary, divergence
/// relaxation, transport, capillary pull, deposition. Reordering it changes the
/// picture, so it is not a knob.
///
/// <para><b>Grid.</b> Water and pigment live at cell centres; velocity lives on
/// cell faces (a MAC grid): <c>u(x,y)</c> is the flow across the boundary
/// between cell <c>x-1</c> and cell <c>x</c>. Collocating velocity at centres
/// would be less code, but a single vector per cell cannot represent water
/// leaving a local peak in all four directions at once — a lone wet cell would
/// sit there forever instead of blooming. Faces get that right for free, and
/// they make the transport step exactly conservative, because a face flux is
/// subtracted from one cell and added to precisely one other.</para>
///
/// <para><b>Scale.</b> Water depth is in units the caller chooses, with one
/// exception: <see cref="EntryHead"/> is an absolute threshold, and a wash
/// seeded well below it never flows. Seed around 0.5–3 per cell and it behaves.
/// State is roughly 24 floats per cell, all of it taken in the constructor, so
/// a lattice should be sized to the region a stroke can reach and reused.</para>
/// </summary>
public sealed class FluidLattice
{
    // ---- solver constants ---------------------------------------------------
    // These are compile-time constants on purpose. They are the parts of the
    // model that must not vary at render time, because a document that renders
    // differently after a preference change is a defect (invariant 4).

    /// <summary>Below this depth a cell counts as dry and cannot push anything out.</summary>
    private const float WetEps = 1e-5f;

    /// <summary>
    /// Film depth below which the paper simply holds the water — the capillary
    /// entry pressure. Two films this thin do not flow into each other, though
    /// a deeper neighbour can still drive them.
    ///
    /// Without it the wet front creeps outward for as long as you run the
    /// solver, the pigment concentration stays monotone, and no rim can ever
    /// form; with it a wash spreads while it is deep and pins once it has
    /// thinned, which is the contact-line pinning that makes a coffee ring a
    /// ring rather than a stain.
    ///
    /// It is absolute, not relative, so it sets the depth scale callers should
    /// seed at: water depths well under this never move at all.
    /// </summary>
    private const float EntryHead = 0.15f;

    /// <summary>Water-surface slope to face velocity, in cells per step per unit of head.</summary>
    private const float FlowGain = 0.5f;

    /// <summary>
    /// Explicit Laplacian coefficient for momentum diffusion. 2D explicit
    /// diffusion diverges above 0.25, and viscosity scales this by up to 1, so
    /// 0.2 is the largest value that is stable at every parameter setting.
    /// </summary>
    private const float ViscCoef = 0.2f;

    /// <summary>How much of the velocity viscosity can eat per step at Viscosity 1.</summary>
    private const float ViscDamp = 0.6f;

    /// <summary>
    /// CFL bound: one cell per step. Faster than this and a cell would have to
    /// empty past its immediate neighbours, which face transport cannot
    /// express — the extra speed would be discarded downstream anyway.
    /// </summary>
    private const float MaxSpeed = 1f;

    /// <summary>
    /// A cell may never send away everything it holds. Keeping a tenth back is
    /// what makes transport unconditionally non-negative, so no quantity can
    /// ring negative and then blow up.
    /// </summary>
    private const float MaxOutflow = 0.9f;

    /// <summary>Sweeps of Gauss-Seidel on the pressure. Fixed — see <see cref="RelaxDivergence"/>.</summary>
    private const int GaussSeidelSweeps = 4;

    /// <summary>
    /// Fraction of an interior cell's mobile pigment the capillary term moves
    /// to the fringe in one step at EdgePull 1. It used to be 0.9, when the
    /// term walked pigment one cell per step and a rim twenty cells from the
    /// middle needed most of a cell's movement per step to be reached at all.
    /// The transfer is direct now (B431), so the rate is the share of a
    /// cell's mobile pigment that leaves per step, against the quarter that
    /// <see cref="Deposit"/> binds in place: at this value a dried disc at
    /// EdgePull 1 reads a rim 1.3× its core (0.46× with the pull off) and a
    /// stroke keeps 55 % of its middle, a drying puddle's look at the top of
    /// the slider. Half this value (0.056) never let the rim overtake the
    /// middle at any setting; left at 0.9 the interior emptied in two steps,
    /// measured as a centre of 24 against 92 with the pull off.
    /// </summary>
    private const float EdgeRate = 0.12f;

    /// <summary>
    /// How much of the wet region's deepest distance to dry paper is fringe:
    /// the band that receives what the interior gives (B431). A share rather
    /// than a count, so a broad wash grows a broader rim than a thin line.
    /// </summary>
    private const float RimShare = 0.2f;

    /// <summary>
    /// The fringe is never narrower than this many cells, so a thin line still
    /// has an edge to pool at. Compared in the chamfer's units, which are
    /// <see cref="ChamferOrth"/> per cell — a floor written in cells was 0.3
    /// of one, and a stroke under ten cells wide had no fringe at all, so the
    /// pull silently did nothing on exactly the lines it was meant to keep.
    /// </summary>
    private const float MinRimCells = 1.5f;

    /// <summary>One orthogonal step of <see cref="BuildDryDistance"/>'s chamfer, so its distances are in fifths of a cell.</summary>
    private const float ChamferOrth = 5f;

    private const float DepositBase = 0.25f;
    private const float LiftBase = 0.06f;

    /// <summary>Ceiling on any single deposit/lift fraction, for the same reason as <see cref="MaxOutflow"/>.</summary>
    private const float MaxTransfer = 0.9f;

    /// <summary>Water the paper soaks up per step at Absorbency 1. This is what dries a wash.</summary>
    private const float WaterDry = 0.03f;

    /// <summary>
    /// Depth at which water holds half the pigment it can. Deep water keeps
    /// pigment in suspension; a thin film strands it. This is the term the rim
    /// depends on — see <see cref="Deposit"/>.
    /// </summary>
    private const float WaterHold = 1f;

    /// <summary>
    /// Paper height that means "no surface at all". <see cref="SetPaper"/>
    /// lerps toward it, so influence 0 leaves granulation unbiased and drag
    /// mid-range instead of pinning the paper to a valley floor.
    /// </summary>
    private const float NeutralPaper = 0.5f;

    private int _w;
    private int _h;
    private int _uw;  // faces per row: _w + 1

    /// <summary>
    /// Cells the buffers below were sized for. A lattice may be used at any
    /// size up to this without reallocating — see <see cref="Rent"/>.
    /// </summary>
    private readonly int _capacity;

    private readonly float[] _paper;

    // Swapped, never reallocated: transport reads one buffer and writes the other.
    private float[] _water;
    private float[] _waterB;
    private float[] _u;   // (_w+1) × _h, vertical faces
    private float[] _v;   // _w × (_h+1), horizontal faces
    private float[] _uB;
    private float[] _vB;

    private readonly float[] _p;
    private readonly float[] _div;
    private readonly float[] _dist;      // chamfer distance to the dry boundary
    private readonly float[] _scale;     // per-cell outflow limiter

    // Pigment is carried premultiplied: channel 0 is the amount, 1..3 are
    // amount × linear colour. Every transport and transfer applies the same
    // fraction to all four, so the colour a cell carries stays consistent and
    // each channel is conserved independently.
    private readonly float[][] _susp = new float[4][];
    private readonly float[][] _suspB = new float[4][];
    private readonly float[][] _dep = new float[4][];

    public FluidLattice(int width, int height) : this(width, height, width * height) { }

    private FluidLattice(int width, int height, int capacity)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        _capacity = Math.Max(capacity, width * height);
        Resize(width, height);

        // Faces need one more column and one more row than cells, and the
        // capacity has to cover the worst shape it could be asked for — a
        // single row is the extreme, where faces outnumber cells by one.
        var n = _capacity;
        var faces = _capacity + Math.Max(width, height) + 1;

        _paper = new float[n];
        Array.Fill(_paper, NeutralPaper);

        _water = new float[n];
        _waterB = new float[n];
        _u = new float[faces];
        _uB = new float[faces];
        _v = new float[faces];
        _vB = new float[faces];
        _p = new float[n];
        _div = new float[n];
        _dist = new float[n];
        _scale = new float[n];

        for (var c = 0; c < 4; c++)
        {
            _susp[c] = new float[n];
            _suspB[c] = new float[n];
            _dep[c] = new float[n];
        }
    }

    private void Resize(int width, int height)
    {
        _w = width;
        _h = height;
        _uw = width + 1;
    }

    /// <summary>
    /// A lattice of this size, reusing the last one when it is big enough.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists.</b> A lattice is roughly twenty floats a cell, so a
    /// 320² one is nine megabytes in arrays that every single one goes straight
    /// onto the large object heap — and a fresh one was being built for every
    /// stroke. Measured at 9.6 MB per stroke, which is a few hundred strokes to
    /// a gigabyte of LOH churn, and the LOH is collected with a full blocking
    /// Gen2. Pauses while painting are the one performance failure an artist
    /// feels directly, and this was manufacturing them.
    /// </para>
    /// <para>
    /// Reused rather than pooled by size, because the rent is always for the
    /// region one stroke can reach and consecutive strokes with one brush are
    /// close in size but rarely equal. So the buffers are kept at the largest
    /// size asked for so far and a smaller rent simply uses less of them —
    /// which works because every loop strides by <c>_w</c> and nothing reads
    /// past <c>_w × _h</c>.
    /// </para>
    /// <para>
    /// <b>Per thread, and always cleared.</b> One field held across threads
    /// would let a live preview and a commit share a solver mid-run. Clearing
    /// is not an optimisation to skip: a lattice carrying the last stroke's
    /// water would render a different mark, and the same document would then
    /// reload differently — invariant 2, via the back door.
    /// </para>
    /// </remarks>
    public static FluidLattice Rent(int width, int height)
    {
        var cells = width * height;
        var cached = _cache;
        if (cached is not null && cached._capacity >= cells && cached._u.Length >= cells + Math.Max(width, height) + 1)
        {
            cached.Resize(width, height);
            cached.Reset();
            return cached;
        }

        // Round the capacity up so a run of gently growing strokes does not
        // reallocate on each one.
        var lattice = new FluidLattice(width, height, Math.Max(cells, 64 * 64));
        _cache = lattice;
        return lattice;
    }

    [ThreadStatic]
    private static FluidLattice? _cache;

    /// <summary>Back to bare dry paper, over the region currently in use.</summary>
    private void Reset()
    {
        var n = _w * _h;
        var faces = Math.Min(_u.Length, n + Math.Max(_w, _h) + 1);

        _paper.AsSpan(0, n).Fill(NeutralPaper);
        _water.AsSpan(0, n).Clear();
        _waterB.AsSpan(0, n).Clear();
        _u.AsSpan(0, faces).Clear();
        _uB.AsSpan(0, faces).Clear();
        _v.AsSpan(0, faces).Clear();
        _vB.AsSpan(0, faces).Clear();
        _p.AsSpan(0, n).Clear();
        _div.AsSpan(0, n).Clear();
        _dist.AsSpan(0, n).Clear();
        _scale.AsSpan(0, n).Clear();

        for (var c = 0; c < 4; c++)
        {
            _susp[c].AsSpan(0, n).Clear();
            _suspB[c].AsSpan(0, n).Clear();
            _dep[c].AsSpan(0, n).Clear();
        }
    }

    public int Width => _w;

    public int Height => _h;

    /// <summary>Paper height in 0..1, row-major, length width*height.</summary>
    /// <param name="influence">
    /// 0..1 blend toward a featureless surface. At 0 the paper stops affecting
    /// drag and granulation entirely instead of reading as one flat valley.
    /// </param>
    public void SetPaper(ReadOnlySpan<float> height, double influence)
    {
        var cells = _w * _h;
        if (height.Length != cells)
        {
            throw new ArgumentException(
                $"Paper height must be {_w}×{_h} = {cells} samples, got {height.Length}.",
                nameof(height));
        }

        var k = Clamped((float)influence, 0f, 1f);
        for (var i = 0; i < cells; i++)
        {
            _paper[i] = NeutralPaper + (Clamped(height[i], 0f, 1f) - NeutralPaper) * k;
        }
    }

    /// <summary>
    /// Add water and suspended pigment at a cell. Colour is linear 0..1.
    /// Out-of-range cells are ignored so a caller stamping a dab near the
    /// lattice edge does not have to clip it first.
    /// </summary>
    public void Seed(int x, int y, float water, float pigment, float r, float g, float b)
    {
        if ((uint)x >= (uint)_w || (uint)y >= (uint)_h) return;

        var i = y * _w + x;
        _water[i] += Clamped(water, 0f, float.MaxValue);

        var q = Clamped(pigment, 0f, float.MaxValue);
        if (q <= 0) return;

        _susp[0][i] += q;
        _susp[1][i] += q * Clamped(r, 0f, 1f);
        _susp[2][i] += q * Clamped(g, 0f, 1f);
        _susp[3][i] += q * Clamped(b, 0f, 1f);
    }

    /// <summary>Advance the simulation. Cost is O(width × height × steps).</summary>
    public void Run(int steps, in FluidParams p)
    {
        // Exact no-op, not "a step that happens to change nothing": callers
        // derive step counts from settings, and 0 has to mean untouched.
        if (steps <= 0) return;

        var visc = Clamped(p.Viscosity, 0f, 1f);
        var drag = Clamped(p.Drag, 0f, 1f);
        var absorb = Clamped(p.Absorbency, 0f, 1f);
        var edge = Clamped(p.EdgePull, 0f, 1f);
        var gran = Clamped(p.Granularity, 0f, 1f);

        for (var s = 0; s < steps; s++)
        {
            UpdateVelocities(visc, drag);
            EnforceWetBoundary();
            RelaxDivergence();
            Transport();
            if (edge > 0) CapillaryPull(edge);
            Deposit(absorb, gran);
        }
    }

    /// <summary>
    /// End the wash. The water is gone and every grain still in suspension is
    /// left on the paper where it stands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this, how dark a mark is depends on how long the solver ran.
    /// <see cref="Deposit"/> binds a fraction of the suspension per step, and
    /// <see cref="ReadDeposit"/> reports only what is bound — so the same brush
    /// at 4 flow steps painted a fifth of what it painted at 24, and at 0 steps
    /// painted nothing at all. Flow steps are a statement about how far pigment
    /// travels. They were also, silently, a statement about how much of it
    /// existed.
    /// </para>
    /// <para>
    /// Drying is the step that separates the two. What the brush carried is
    /// what ends up on the paper; the solver only decides where. That is also
    /// what really happens — a wash does not stop half-settled, it dries, and
    /// the residue is left behind.
    /// </para>
    /// <para>
    /// It binds in place rather than running the solver down to dryness,
    /// because a cell with no water has no velocity: the extra steps would move
    /// nothing and cost a full sweep each. The redistribution drying does in a
    /// real wash — the film retreating into the tooth, the contact line
    /// dragging pigment out to the rim — is <see cref="Deposit"/>'s granulation
    /// term and <see cref="CapillaryPull"/>, and both have already run.
    /// </para>
    /// <para>
    /// Idempotent, and safe to call on a lattice that was never run.
    /// </para>
    /// </remarks>
    public void Dry()
    {
        var cells = _w * _h;
        for (var c = 0; c < 4; c++)
        {
            float[] s = _susp[c], d = _dep[c];
            for (var i = 0; i < cells; i++)
            {
                d[i] += s[i];
                s[i] = 0f;
            }
        }

        _water.AsSpan(0, cells).Clear();
        _u.AsSpan(0, Math.Min(_u.Length, cells + Math.Max(_w, _h) + 1)).Clear();
        _v.AsSpan(0, Math.Min(_v.Length, cells + Math.Max(_w, _h) + 1)).Clear();
    }

    /// <summary>
    /// Pigment deposited on the paper, premultiplied linear RGBA, row-major,
    /// length width*height*4. Values are raw accumulated mass and can exceed 1
    /// where a caller seeded heavily; tone-mapping is the compositor's call,
    /// not the solver's.
    /// </summary>
    public void ReadDeposit(Span<float> rgba)
    {
        var n = _w * _h;
        if (rgba.Length != n * 4)
        {
            throw new ArgumentException(
                $"Deposit buffer must be {n * 4} floats (RGBA per cell), got {rgba.Length}.",
                nameof(rgba));
        }

        float[] a = _dep[0], r = _dep[1], g = _dep[2], b = _dep[3];
        for (int i = 0, o = 0; i < n; i++, o += 4)
        {
            rgba[o] = r[i];
            rgba[o + 1] = g[i];
            rgba[o + 2] = b[i];
            rgba[o + 3] = a[i];
        }
    }

    public float WaterAt(int x, int y) =>
        (uint)x >= (uint)_w || (uint)y >= (uint)_h ? 0f : _water[y * _w + x];

    /// <summary>Pigment still in suspension at a cell (the amount channel).</summary>
    public float SuspendedAt(int x, int y) =>
        (uint)x >= (uint)_w || (uint)y >= (uint)_h ? 0f : _susp[0][y * _w + x];

    /// <summary>Pigment bound to the paper at a cell (the amount channel).</summary>
    public float DepositAt(int x, int y) =>
        (uint)x >= (uint)_w || (uint)y >= (uint)_h ? 0f : _dep[0][y * _w + x];

    /// <summary>
    /// Suspended plus deposited pigment over the whole lattice, summed in fixed
    /// row-major order in double so the sum itself is reproducible. Nothing in
    /// the model may increase this — it is the cheapest possible check that the
    /// solver is moving matter rather than inventing it.
    /// </summary>
    public double TotalPigment(int channel = 0)
    {
        if ((uint)channel >= 4u) throw new ArgumentOutOfRangeException(nameof(channel));

        float[] s = _susp[channel], d = _dep[channel];
        double total = 0;
        var cells = _w * _h;
        for (var i = 0; i < cells; i++) total += s[i] + d[i];
        return total;
    }

    /// <summary>Total water on the lattice. Falls as the paper dries the wash.</summary>
    public double TotalWater()
    {
        double total = 0;
        var cells = _w * _h;
        for (var i = 0; i < cells; i++) total += _water[i];
        return total;
    }

    // ---- 1. velocity --------------------------------------------------------

    /// <summary>
    /// Water accelerates down the slope of its own surface, which here is the
    /// depth plus the paper under it — so a wash drains off the tooth and into
    /// the valleys before granulation ever gets a say.
    ///
    /// On faces the slope is a one-sided difference between the two cells the
    /// face separates, which is the whole reason for the staggered grid: a
    /// lone wet cell is downhill on every side at once, and every one of its
    /// faces picks that up independently.
    ///
    /// Viscosity acts twice, as it does physically: it diffuses momentum
    /// between neighbouring faces (which is what makes an oil film move as a
    /// sheet) and it damps the field outright. Drag is separate and depends on
    /// the paper under the face: rougher paper takes more velocity away.
    ///
    /// Velocity self-advection is deliberately absent. At the speeds a paint
    /// film reaches it changes nothing visible, and it would cost a second
    /// transport pass over the whole lattice every step.
    /// </summary>
    private void UpdateVelocities(float visc, float drag)
    {
        float[] water = _water, paper = _paper, u = _u, v = _v, un = _uB, vn = _vB;
        var viscKeep = 1f - visc * ViscDamp;

        // Vertical faces. x = 0 and x = _w are the lattice boundary and stay
        // zero, which is what makes the domain closed (nothing leaves).
        for (var y = 0; y < _h; y++)
        {
            var frow = y * _uw;
            var crow = y * _w;
            var up = y > 0 ? frow - _uw : frow;
            var dn = y < _h - 1 ? frow + _uw : frow;

            un[frow] = 0f;
            un[frow + _w] = 0f;

            for (var x = 1; x < _w; x++)
            {
                var f = frow + x;
                int cl = crow + x - 1, cr = crow + x;

                var dh = water[cr] + paper[cr] - (water[cl] + paper[cl]);
                var u0 = u[f];
                var lap = u[f - 1] + u[f + 1] + u[up + x] + u[dn + x] - 4f * u0;
                var damp = viscKeep * (1f - drag * (0.25f + 0.75f * (paper[cl] + paper[cr]) * 0.5f));

                un[f] = Clamped((u0 - dh * FlowGain + visc * ViscCoef * lap) * damp, -MaxSpeed, MaxSpeed);
            }
        }

        // Horizontal faces, same story rotated.
        for (var x = 0; x < _w; x++)
        {
            vn[x] = 0f;
            vn[_h * _w + x] = 0f;
        }

        for (var y = 1; y < _h; y++)
        {
            var frow = y * _w;
            var up = frow - _w;
            var dn = y < _h ? frow + _w : frow;

            for (var x = 0; x < _w; x++)
            {
                var f = frow + x;
                int ct = (y - 1) * _w + x, cb = y * _w + x;

                var dh = water[cb] + paper[cb] - (water[ct] + paper[ct]);
                var v0 = v[f];
                var xl = x > 0 ? f - 1 : f;
                var xr = x < _w - 1 ? f + 1 : f;
                var lap = v[xl] + v[xr] + v[up + x] + v[dn + x] - 4f * v0;
                var damp = viscKeep * (1f - drag * (0.25f + 0.75f * (paper[ct] + paper[cb]) * 0.5f));

                vn[f] = Clamped((v0 - dh * FlowGain + visc * ViscCoef * lap) * damp, -MaxSpeed, MaxSpeed);
            }
        }

        (_u, _uB) = (_uB, _u);
        (_v, _vB) = (_vB, _v);
    }

    // ---- 2. wet-region boundary --------------------------------------------

    /// <summary>
    /// A cell with no water has nothing to push, so no face may carry flow out
    /// of one. Water may still flow <i>into</i> a dry cell from a wet
    /// neighbour — that is how a bloom grows — but never back out until it has
    /// water of its own, which is what stops a region that has dried from
    /// dragging the pigment it was left holding.
    ///
    /// A film thinner than <see cref="EntryHead"/> is additionally held by the
    /// paper unless something deeper is driving it. Everything else the solver
    /// does would let a wash keep creeping for as long as you run it; this is
    /// the only thing that brings one to a stop.
    /// </summary>
    private void EnforceWetBoundary()
    {
        float[] water = _water, u = _u, v = _v;

        for (var y = 0; y < _h; y++)
        {
            var frow = y * _uw;
            var crow = y * _w;
            for (var x = 1; x < _w; x++)
            {
                var f = frow + x;
                var uu = u[f];
                if (uu == 0) continue;

                // The donor is the cell the flow leaves; the receiver is the
                // one it enters.
                var donor = uu > 0 ? crow + x - 1 : crow + x;
                var receiver = uu > 0 ? crow + x : crow + x - 1;
                if (Held(water[donor], water[receiver])) u[f] = 0f;
            }
        }

        for (var y = 1; y < _h; y++)
        {
            var frow = y * _w;
            for (var x = 0; x < _w; x++)
            {
                var f = frow + x;
                var vv = v[f];
                if (vv == 0) continue;

                var donor = vv > 0 ? frow - _w + x : frow + x;
                var receiver = vv > 0 ? frow + x : frow - _w + x;
                if (Held(water[donor], water[receiver])) v[f] = 0f;
            }
        }
    }

    /// <summary>
    /// Whether the paper holds this face shut. A cell with no water has nothing
    /// to push. Beyond that, two films both thinner than <see cref="EntryHead"/>
    /// are held in place by the paper rather than flowing into each other — a
    /// deeper neighbour can still drive them, which is what lets a wash spread
    /// while it is deep and stop once it is thin.
    /// </summary>
    private static bool Held(float donor, float receiver) =>
        donor <= WetEps || (donor < EntryHead && receiver < EntryHead);

    // ---- 3. divergence relaxation ------------------------------------------

    /// <summary>
    /// A fixed <see cref="GaussSeidelSweeps"/> sweeps of Gauss-Seidel on the
    /// pressure, then subtract its gradient. Fixed for two reasons, both of
    /// which matter more here than accuracy does:
    ///
    /// An iterate-until-converged loop would make the result depend on
    /// floating-point luck, and a document that re-renders differently is worse
    /// than one that re-renders imperfectly.
    ///
    /// And four sweeps is nowhere near converged, on purpose. A converged
    /// projection is exactly divergence-free, and a divergence-free field
    /// cannot pile water up anywhere — it would flatten the very pressure
    /// gradient that drives a bloom outward. The leftover divergence is the
    /// compressibility a shallow-water layer is supposed to have.
    /// </summary>
    private void RelaxDivergence()
    {
        float[] u = _u, v = _v, p = _p, div = _div;
        Array.Clear(p);

        for (var y = 0; y < _h; y++)
        {
            var frow = y * _uw;
            var crow = y * _w;
            for (var x = 0; x < _w; x++)
            {
                div[crow + x] = u[frow + x + 1] - u[frow + x]
                              + v[crow + _w + x] - v[crow + x];
            }
        }

        for (var sweep = 0; sweep < GaussSeidelSweeps; sweep++)
        {
            for (var y = 0; y < _h; y++)
            {
                var row = y * _w;
                var up = y > 0 ? row - _w : row;
                var dn = y < _h - 1 ? row + _w : row;
                for (var x = 0; x < _w; x++)
                {
                    var xl = x > 0 ? row + x - 1 : row;
                    var xr = x < _w - 1 ? row + x + 1 : row + _w - 1;
                    p[row + x] = (p[xl] + p[xr] + p[up + x] + p[dn + x] - div[row + x]) * 0.25f;
                }
            }
        }

        for (var y = 0; y < _h; y++)
        {
            var frow = y * _uw;
            var crow = y * _w;
            for (var x = 1; x < _w; x++)
            {
                var f = frow + x;
                u[f] = Clamped(u[f] - (p[crow + x] - p[crow + x - 1]), -MaxSpeed, MaxSpeed);
            }
        }

        for (var y = 1; y < _h; y++)
        {
            var row = y * _w;
            for (var x = 0; x < _w; x++)
            {
                v[row + x] = Clamped(v[row + x] - (p[row + x] - p[row - _w + x]), -MaxSpeed, MaxSpeed);
            }
        }
    }

    // ---- 4. transport -------------------------------------------------------

    /// <summary>
    /// Donor-cell upwind transport of water and of the pigment it carries.
    ///
    /// Semi-Lagrangian advection would hold an edge sharper, but it samples the
    /// field rather than moving it, and so conserves neither pigment nor
    /// non-negativity. Conservation is the invariant this module is judged on,
    /// so the flux form wins: every gram leaving a cell is added to exactly one
    /// neighbour, boundary faces carry nothing, and the total is unchanged by
    /// construction. The price is numerical diffusion, which in a wet-on-wet
    /// medium is indistinguishable from the medium.
    ///
    /// Pigment moves with the same fraction as the water above it, so
    /// concentration follows the flow instead of being transported separately.
    /// </summary>
    private void Transport()
    {
        float[] water = _water, wOut = _waterB, u = _u, v = _v, scale = _scale;

        // Pass one: how much each cell is trying to send out through its four
        // faces, and by how much that has to be scaled back. Without this a
        // cell with fast flow on every side could send away more than it holds
        // and go negative.
        for (var y = 0; y < _h; y++)
        {
            var frow = y * _uw;
            var crow = y * _w;
            for (var x = 0; x < _w; x++)
            {
                var i = crow + x;
                var uL = u[frow + x];
                var uR = u[frow + x + 1];
                var vT = v[crow + x];
                var vB = v[crow + _w + x];

                var outflow = (uL < 0 ? -uL : 0f) + (uR > 0 ? uR : 0f)
                            + (vT < 0 ? -vT : 0f) + (vB > 0 ? vB : 0f);

                scale[i] = outflow > MaxOutflow ? MaxOutflow / outflow : 1f;
            }
        }

        var cells = _w * _h;
        wOut.AsSpan(0, cells).Clear();
        for (var c = 0; c < 4; c++) _suspB[c].AsSpan(0, cells).Clear();

        // The pigment buffers are hoisted out of the loop rather than indexed
        // through the jagged array per cell: four extra indirections per cell
        // per step is measurable at this size.
        float[] s0 = _susp[0], s1 = _susp[1], s2 = _susp[2], s3 = _susp[3];
        float[] t0 = _suspB[0], t1 = _suspB[1], t2 = _suspB[2], t3 = _suspB[3];

        for (var y = 0; y < _h; y++)
        {
            var frow = y * _uw;
            var crow = y * _w;
            for (var x = 0; x < _w; x++)
            {
                var i = crow + x;
                var k = scale[i];
                var uL = u[frow + x];
                var uR = u[frow + x + 1];
                var vT = v[crow + x];
                var vB = v[crow + _w + x];

                var fL = uL < 0 ? -uL * k : 0f;
                var fR = uR > 0 ? uR * k : 0f;
                var fU = vT < 0 ? -vT * k : 0f;
                var fD = vB > 0 ? vB * k : 0f;

                var keep = 1f - (fL + fR + fU + fD);
                int iL = i - 1, iR = i + 1, iU = i - _w, iD = i + _w;

                Scatter(water, wOut, i, iL, iR, iU, iD, keep, fL, fR, fU, fD);

                // Standing still is the common case away from the wet fringe,
                // and it lets four buffers be skipped entirely.
                if (keep >= 1f)
                {
                    t0[i] += s0[i];
                    t1[i] += s1[i];
                    t2[i] += s2[i];
                    t3[i] += s3[i];
                    continue;
                }

                Scatter(s0, t0, i, iL, iR, iU, iD, keep, fL, fR, fU, fD);
                Scatter(s1, t1, i, iL, iR, iU, iD, keep, fL, fR, fU, fD);
                Scatter(s2, t2, i, iL, iR, iU, iD, keep, fL, fR, fU, fD);
                Scatter(s3, t3, i, iL, iR, iU, iD, keep, fL, fR, fU, fD);
            }
        }

        (_water, _waterB) = (_waterB, _water);
        for (var c = 0; c < 4; c++) (_susp[c], _suspB[c]) = (_suspB[c], _susp[c]);
    }

    /// <summary>
    /// Split one cell's worth of a quantity between itself and its four
    /// neighbours. The fractions sum to exactly 1, which is where conservation
    /// comes from, and they are computed once per cell and reused for water and
    /// all four pigment channels, which is where the speed comes from.
    /// </summary>
    private static void Scatter(
        float[] src, float[] dst, int i, int iL, int iR, int iU, int iD,
        float keep, float fL, float fR, float fU, float fD)
    {
        var q = src[i];
        if (q <= 0) return;
        dst[i] += q * keep;
        if (fL > 0) dst[iL] += q * fL;
        if (fR > 0) dst[iR] += q * fR;
        if (fU > 0) dst[iU] += q * fU;
        if (fD > 0) dst[iD] += q * fD;
    }

    // ---- 5. capillary pull --------------------------------------------------

    /// <summary>
    /// Capillary flow carries pigment toward the thin edge of the wet region.
    /// This is the step the rim comes from, and it moves pigment, not water.
    ///
    /// Moving water was the obvious reading and it is wrong: a fringe that
    /// receives water gets deeper, clears <see cref="EntryHead"/>, and
    /// advances, so EdgePull reads as "spreads further" rather than "darker at
    /// the edge". What a drying puddle does is circulate — water runs out to
    /// the fringe, is lost there into the paper, and the pigment it carried
    /// stays. The water makes a round trip and nets out; the pigment does not.
    /// Modelling only the residue is both cheaper and closer to what you see.
    ///
    /// <b>The interior drains evenly and the fringe receives (B431).</b> Two
    /// earlier readings each left a mark that was not in the water. Climbing
    /// toward thinner film was gradient ascent on a noisy surface, and raising
    /// EdgePull mottled the interior instead of building a rim. Walking pigment
    /// cell to cell downhill in the distance to dry paper fixed that and left a
    /// subtler one: the cells on the stroke's medial axis — where that distance
    /// peaks — give pigment outward to both sides and, having no neighbour
    /// further in, receive none back. A channel a few cells wide ran down the
    /// exact middle of every wash, at any pull above zero: the pale seam the
    /// owner saw, and the reason the Watercolor preset's EdgePull sat at 0.06
    /// where nothing pools. In a drying wash the outward flow is driven by
    /// evaporation at the contact line and carries pigment from the <em>whole</em>
    /// interior at once, so that is what this does: every interior cell gives a
    /// share of its mobile pigment, and the fringe band takes the sum, weighted
    /// toward the cells whose film is thinnest — the contact-line pinning that
    /// makes a coffee ring a ring. No cell is a source with no inflow, so there
    /// is nothing to carve.
    ///
    /// The fringe is the band of wet cells within a share of the region's
    /// deepest distance to dry paper, so a wide wash grows a wider rim than a
    /// thin line does. Mobility gates both halves: deep water gives, a film
    /// about to dry receives. Dry cells take nothing, because pigment must not
    /// be dragged onto dry paper.
    ///
    /// Determinism: two fixed-order passes over the cells and one total, with
    /// the same float arithmetic every run. Conservation: what the interior
    /// gives is exactly what the fringe receives, to rounding.
    /// </summary>
    private void CapillaryPull(float edge)
    {
        float[] water = _water, dist = _dist;
        var cells = _w * _h;
        var rate = Clamped(edge * EdgeRate, 0f, MaxTransfer);
        if (rate <= 0f) return;
        BuildDryDistance();

        // The fringe band, from the region's deepest point.
        var deepest = 0f;
        for (var i = 0; i < cells; i++)
        {
            if (water[i] > WetEps && dist[i] > deepest) deepest = dist[i];
        }
        if (deepest <= 0f) return;
        var band = Math.Max(MinRimCells * ChamferOrth, deepest * RimShare);

        // Receiving weight per fringe cell, and its total. Thin film pins.
        var weights = _suspB[3].AsSpan(0, cells);
        weights.Clear();
        var totalWeight = 0f;
        for (var i = 0; i < cells; i++)
        {
            var w = water[i];
            if (w <= WetEps || dist[i] > band) continue;
            var pinned = 1f - w / (w + WaterHold);
            weights[i] = pinned;
            totalWeight += pinned;
        }
        if (totalWeight <= 0f) return;

        // The mass (channel 0) and the three colour channels, through the same
        // weights. They live in channel 3's scratch, which nothing reads until
        // Transport clears it at the next step.
        for (var c = 0; c < 4; c++)
        {
            Drain(_susp[c], water, dist, band, rate, weights, totalWeight, cells);
        }
    }

    /// <summary>Interior cells give a share of their mobile pigment; the fringe takes the sum.</summary>
    private static void Drain(
        float[] susp, float[] water, float[] dist, float band, float rate,
        ReadOnlySpan<float> weights, float totalWeight, int cells)
    {
        var removed = 0f;
        for (var i = 0; i < cells; i++)
        {
            var w = water[i];
            if (w <= WetEps || dist[i] <= band) continue;
            var give = susp[i] * rate * (w / (w + WaterHold));
            if (give <= 0f) continue;
            susp[i] -= give;
            removed += give;
        }
        if (removed <= 0f) return;
        var perWeight = removed / totalWeight;
        for (var i = 0; i < cells; i++)
        {
            var k = weights[i];
            if (k > 0f) susp[i] += k * perWeight;
        }
    }

    /// <summary>
    /// Distance from each wet cell to the nearest dry one, by two raster
    /// sweeps. Dry cells are 0.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Chamfer 5-7-11 rather than 3-4: the level sets are what set the
    /// direction pigment travels, and 3-4's are visibly octagonal, which would
    /// grow a rim with corners on a round wash. Two sweeps, no queue, no
    /// allocation, and the same answer in the same order every time.
    /// </para>
    /// <para>
    /// The lattice border is <em>not</em> treated as dry. A stroke sizes its
    /// region to what it can reach, so wet touching the border means the region
    /// was clipped, not that the wash ended there — seeding distance from it
    /// would draw a rim along a rectangle that is not in the drawing. An
    /// entirely wet lattice therefore has no gradient anywhere and this step
    /// does nothing, which is the right answer for "no boundary exists".
    /// </para>
    /// </remarks>
    private void BuildDryDistance()
    {
        const float Orth = ChamferOrth, Diag = 7f, Knight = 11f;
        const float Far = 1e9f;

        float[] water = _water, d = _dist;
        var cells = _w * _h;
        for (var i = 0; i < cells; i++) d[i] = water[i] > WetEps ? Far : 0f;

        for (var y = 0; y < _h; y++)
        {
            var row = y * _w;
            for (var x = 0; x < _w; x++)
            {
                var i = row + x;
                var v = d[i];
                if (v == 0f) continue;
                if (y > 1)
                {
                    if (x > 0) v = Math.Min(v, d[i - 2 * _w - 1] + Knight);
                    if (x < _w - 1) v = Math.Min(v, d[i - 2 * _w + 1] + Knight);
                }
                if (y > 0)
                {
                    if (x > 1) v = Math.Min(v, d[i - _w - 2] + Knight);
                    if (x > 0) v = Math.Min(v, d[i - _w - 1] + Diag);
                    v = Math.Min(v, d[i - _w] + Orth);
                    if (x < _w - 1) v = Math.Min(v, d[i - _w + 1] + Diag);
                    if (x < _w - 2) v = Math.Min(v, d[i - _w + 2] + Knight);
                }
                if (x > 0) v = Math.Min(v, d[i - 1] + Orth);
                d[i] = v;
            }
        }

        for (var y = _h - 1; y >= 0; y--)
        {
            var row = y * _w;
            for (var x = _w - 1; x >= 0; x--)
            {
                var i = row + x;
                var v = d[i];
                if (v == 0f) continue;
                if (y < _h - 2)
                {
                    if (x > 0) v = Math.Min(v, d[i + 2 * _w - 1] + Knight);
                    if (x < _w - 1) v = Math.Min(v, d[i + 2 * _w + 1] + Knight);
                }
                if (y < _h - 1)
                {
                    if (x > 1) v = Math.Min(v, d[i + _w - 2] + Knight);
                    if (x > 0) v = Math.Min(v, d[i + _w - 1] + Diag);
                    v = Math.Min(v, d[i + _w] + Orth);
                    if (x < _w - 1) v = Math.Min(v, d[i + _w + 1] + Diag);
                    if (x < _w - 2) v = Math.Min(v, d[i + _w + 2] + Knight);
                }
                if (x < _w - 1) v = Math.Min(v, d[i + 1] + Orth);
                d[i] = v;
            }
        }
    }

    // ---- 6. deposition ------------------------------------------------------

    /// <summary>
    /// The paper takes pigment out of suspension and gives some back. Both are
    /// fractional transfers between two buffers, so this step moves pigment and
    /// cannot create it.
    ///
    /// Deposition is gated on how much water a cell is standing in. Deep water
    /// holds its pigment mobile; a thin film strands it. That single term is
    /// what turns the capillary pull of step 5 into a visible rim rather than a
    /// slow leak outward: pigment survives long enough to be carried to the
    /// edge, and the edge — where the film is thinnest and dries first — is
    /// where it is finally bound. Without it, pigment settles wherever it was
    /// laid down and the wash renders as a flat stain.
    ///
    /// Granularity tilts the exchange with the paper's height: valleys take
    /// more and release less, peaks the reverse, which is granulation. It is
    /// signed around the neutral surface, so with no paper set — or influence
    /// 0 — it biases nothing rather than treating the whole sheet as one
    /// valley. Absorbency raises deposition everywhere instead, and suppresses
    /// lift, because thirsty paper does not let go.
    ///
    /// Water itself is soaked up here too; that is what ends a wash. It moves
    /// no pigment, so conservation is untouched.
    /// </summary>
    /// <remarks>
    /// <b>One pass, not five.</b> This used to compute the two rates into
    /// scratch arrays and then walk the lattice again for each of the four
    /// pigment channels, which is five traversals of every cell and two arrays
    /// of pure round-trip traffic — written in one loop and read in the next,
    /// long after they had fallen out of cache. Both rates depend only on the
    /// cell, so they are computed where they are used and the scratch is gone.
    /// The arithmetic is identical; the result is bit-identical.
    /// </remarks>
    private void Deposit(float absorb, float gran)
    {
        float[] water = _water, paper = _paper;
        float[] s0 = _susp[0], s1 = _susp[1], s2 = _susp[2], s3 = _susp[3];
        float[] d0 = _dep[0], d1 = _dep[1], d2 = _dep[2], d3 = _dep[3];

        var depBase = DepositBase * (0.25f + 0.75f * absorb);
        var liftBase = LiftBase * (1f - absorb);
        var dryKeep = 1f - absorb * WaterDry;

        var cells = _w * _h;
        for (var i = 0; i < cells; i++)
        {
            var w = water[i];
            var mobile = w / (w + WaterHold);
            var valley = 1f - 2f * paper[i];
            var dep = Clamped(depBase * (1f - mobile) * (1f + gran * valley), 0f, MaxTransfer);
            var lift = Clamped(liftBase * mobile * (1f - gran * valley), 0f, MaxTransfer);
            water[i] = w * dryKeep;

            Settle(s0, d0, i, dep, lift);
            Settle(s1, d1, i, dep, lift);
            Settle(s2, d2, i, dep, lift);
            Settle(s3, d3, i, dep, lift);
        }
    }

    /// <summary>One channel's exchange with the paper at one cell.</summary>
    private static void Settle(float[] susp, float[] dep, int i, float depRate, float liftRate)
    {
        var s = susp[i];
        var settled = s * depRate;
        var bound = dep[i] + settled;
        var lifted = bound * liftRate;
        dep[i] = bound - lifted;
        susp[i] = s - settled + lifted;
    }

    /// <summary>
    /// NaN-safe clamp. The comparison order matters: a NaN fails
    /// <c>v &gt;= lo</c> and lands on <paramref name="lo"/>, so one bad value
    /// cannot spread through the lattice on the next step. That is the whole
    /// stability story at extreme parameters — clamp at every point a value
    /// could grow, and nothing downstream has to defend itself.
    /// </summary>
    private static float Clamped(float v, float lo, float hi) => v >= lo ? (v <= hi ? v : hi) : lo;
}

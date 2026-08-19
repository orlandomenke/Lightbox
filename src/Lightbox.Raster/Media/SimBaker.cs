using Lightbox.Core.Documents;

namespace Lightbox.Raster.Media;

/// <summary>
/// One ember's travel over one frame, in cell units, and how far through its
/// life it is.
/// </summary>
/// <remarks>
/// A <em>path</em> rather than two endpoints, because an ember covers a whole
/// frame's worth of flow — up to eight cells at ordinary substepping — and a
/// straight chord across that reads as a scratch rather than a spark. Sampled
/// once per solver step, so it curves the way the fluid does.
/// </remarks>
public sealed record EmberTrail(List<StrokePoint> Path, double Age);

/// <summary>What one exposed frame's simulation left behind.</summary>
public sealed class SolvedFrame
{
    public required int Frame { get; init; }

    /// <summary>The field the bands read — temperature for fire, density for smoke.</summary>
    public required float[] Band { get; init; }

    public required float[] VelocityX { get; init; }

    public required float[] VelocityY { get; init; }

    public required List<EmberTrail> Embers { get; init; }
}

/// <summary>
/// A whole element solved: the expensive half, kept so that restyling is cheap.
/// </summary>
/// <remarks>
/// Roughly 250 KB a frame at 192×108 — around 12 MB for a 48-frame element,
/// which Q118 accepted holding for the life of the session and declined saving.
/// It is derived data: throwing it away costs a re-solve and never costs work.
/// </remarks>
public sealed class SolvedElement
{
    public required string ElementId { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required List<SolvedFrame> Frames { get; init; }

    /// <summary>
    /// The highest value the band field reached anywhere in the element.
    /// </summary>
    /// <remarks>
    /// So a caller can offer "fit the bands to this element" without an artist
    /// having to guess what a temperature of 3.4 looks like. Taken over the
    /// <em>whole</em> element rather than per frame, and that is the load-bearing
    /// part: a range that followed each frame's own peak would rescale the bands
    /// every frame, and a band that moves because its scale moved is flicker —
    /// the failure the charter forbids, arriving through a convenience.
    /// </remarks>
    public required float PeakBand { get; init; }
}

/// <summary>One exposed frame's drawing.</summary>
public sealed record BakedFrame(int Frame, List<Stroke> Strokes);

/// <summary>
/// Runs an element and turns it into drawings — the two halves kept apart on
/// purpose.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Solve"/> is expensive and <see cref="Draw"/> is not</b>, and
/// that seam is what makes a live preview possible (Q123). Changing a line
/// treatment re-draws a 48-frame element in about 44 ms; changing a simulation
/// parameter costs a ~1437 ms re-solve. Fusing them would make every edit feel
/// like the slower one.
/// </para>
/// <para>
/// <b>Nothing here touches a document.</b> The strokes come back in a list and
/// <c>SimBakeOps</c> puts them somewhere, so the whole pipeline is testable
/// without a layer, a frame or a UI.
/// </para>
/// <para>
/// <b>Deterministic throughout.</b> The solver's own guarantees do most of it;
/// what is added here is that emitters stamp in a fixed order and embers are
/// seeded from <see cref="DeterministicHash"/> over their spawn position and
/// index, never an RNG. Two bakes of one element are identical stroke for
/// stroke — <c>SimBakerTests</c> is what says so.
/// </para>
/// </remarks>
public sealed class SimBaker
{
    /// <summary>Ember spawn positions are decorrelated from every other hash by this.</summary>
    private const uint EmberSalt = 7717u;

    private readonly FieldTracer _tracer = new();

    /// <summary>
    /// Run the element frame by frame, keeping the field on the frames it
    /// exposes.
    /// </summary>
    /// <remarks>
    /// The simulation advances on <em>every</em> frame even when
    /// <see cref="SimElement.ExposeOn"/> holds the drawing for two or three —
    /// holding a drawing is a statement about how often it is redrawn, never
    /// about how fast the fire moves.
    /// </remarks>
    public SolvedElement Solve(
        SimElement element, IProgress<double>? progress = null, CancellationToken cancel = default)
        => Solve(element, null, progress, cancel);

    /// <param name="masks">
    /// Where painted emitters emit, at grid resolution. Null leaves every masked
    /// emitter silent — which is what an element whose mask layer has been
    /// deleted should do, rather than throwing or falling back to a shape nobody
    /// authored.
    /// </param>
    public SolvedElement Solve(
        SimElement element, ISimMasks? masks,
        IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(element);

        var w = Math.Max(1, element.GridWidth);
        var h = Math.Max(1, element.GridHeight);
        var expose = Math.Max(1, element.ExposeOn);
        var frames = Math.Max(0, element.FrameCount);
        var substeps = Math.Max(0, element.Substeps);
        var preRoll = Math.Max(0, element.PreRoll ?? 0);

        var solver = FluidSolver.Rent(w, h);
        var embers = new List<Ember>();
        var solved = new List<SolvedFrame>();

        // Taken over every frame simulated, not only the ones drawn. Sampling it
        // from the kept frames made the peak depend on ExposeOn, so the same
        // element on 2s got different band levels from the same element on 1s —
        // and holding a drawing must say nothing at all about what is drawn.
        var peak = 0f;

        // Pre-roll runs the same frame the recorded ones do — emitters, embers,
        // wind and all — and simply keeps nothing. Anything cheaper would open
        // the element on a different fluid from the one the rest of it sees.
        for (var i = -preRoll; i < frames; i++)
        {
            cancel.ThrowIfCancellationRequested();

            var wind = element.WindAt(element.FirstFrame + Math.Max(i, 0));

            Emit(solver, element, i, masks);
            SpawnEmbers(embers, element, i);

            // Stepped one at a time so the embers can be carried between steps
            // and trace a curve. `FluidSolverTests.Splitting_A_Run_Matches_One_
            // Long_Run` is what makes this free: the solver's step counter is its
            // own state, so substeps taken singly leave exactly what one call
            // would.
            foreach (var ember in embers) ember.Begin();
            for (var step = 0; step < substeps; step++)
            {
                solver.Run(1, element.Params, wind.X, wind.Y);
                foreach (var ember in embers) ember.Carry(solver);
            }

            var trails = RetireEmbers(embers, solver, element);

            if (i < 0)
            {
                progress?.Report(0);
                continue;
            }

            var framePeak = element.BandsFromHeat ? solver.PeakTemperature() : solver.PeakDensity();
            if (framePeak > peak) peak = framePeak;

            if (i % expose == 0)
            {
                var band = new float[w * h];
                if (element.BandsFromHeat) solver.ReadTemperature(band);
                else solver.ReadDensity(band);

                var vx = new float[w * h];
                var vy = new float[w * h];
                for (var y = 0; y < h; y++)
                {
                    for (var x = 0; x < w; x++)
                    {
                        vx[y * w + x] = solver.VelocityXAt(x, y);
                        vy[y * w + x] = solver.VelocityYAt(x, y);
                    }
                }

                solved.Add(new SolvedFrame
                {
                    Frame = element.FirstFrame + i,
                    Band = band,
                    VelocityX = vx,
                    VelocityY = vy,
                    Embers = trails,
                });
            }

            progress?.Report(frames == 0 ? 1 : (i + 1) / (double)frames);
        }

        return new SolvedElement
        {
            ElementId = element.Id, Width = w, Height = h, Frames = solved, PeakBand = peak,
        };
    }

    /// <summary>
    /// Turn a solved element into strokes. Cheap, and re-runnable every time a
    /// treatment changes.
    /// </summary>
    public List<BakedFrame> Draw(
        SolvedElement solved, SimElement element, ResolvedTreatment treatment, BrushSettings? outlineBrush = null)
    {
        ArgumentNullException.ThrowIfNull(solved);
        ArgumentNullException.ThrowIfNull(element);

        var baked = new List<BakedFrame>(solved.Frames.Count);

        foreach (var frame in solved.Frames) baked.Add(DrawOne(solved, element, treatment, outlineBrush, frame));

        return baked;
    }

    /// <summary>
    /// Draw the one drawing exposed on a timeline frame — the nearest at or
    /// before it — or null if the element draws nothing there.
    /// </summary>
    /// <remarks>
    /// <b>For the preview, and it is a performance fix rather than a
    /// convenience.</b> Re-tracing a 48-frame element costs about 44 ms, which is
    /// what makes tuning a style live *for a bake*; paid per pointer event while
    /// somebody drags a slider it is three frames of lag on every tick. Scrubbing
    /// and restyling both need exactly one frame, so they ask for one.
    /// </remarks>
    public BakedFrame? DrawAt(
        SolvedElement solved, SimElement element, ResolvedTreatment treatment,
        BrushSettings? outlineBrush, int frame)
    {
        ArgumentNullException.ThrowIfNull(solved);
        ArgumentNullException.ThrowIfNull(element);

        SolvedFrame? found = null;
        foreach (var candidate in solved.Frames)
        {
            if (candidate.Frame <= frame && (found is null || candidate.Frame > found.Frame)) found = candidate;
        }

        return found is null ? null : DrawOne(solved, element, treatment, outlineBrush, found);
    }

    private BakedFrame DrawOne(
        SolvedElement solved, SimElement element, ResolvedTreatment treatment,
        BrushSettings? outlineBrush, SolvedFrame frame)
    {
        var strokes = _tracer.Trace(new FieldTraceRequest
        {
            Field = frame.Band,
            VelocityX = frame.VelocityX,
            VelocityY = frame.VelocityY,
            Width = solved.Width,
            Height = solved.Height,
            OriginX = element.OriginX,
            OriginY = element.OriginY,
            Scale = element.Scale,
            Treatment = treatment,
            Colors = element.BandColors,
            Low = (float)(element.BandLow * solved.PeakBand),
            High = (float)(element.BandHigh * solved.PeakBand),
            OutlineBrush = outlineBrush,
            OutlineColor = element.OutlineColor,
        });

        if (element.Particles is { } spec) strokes.AddRange(DrawEmbers(frame.Embers, element, spec));

        foreach (var stroke in strokes) stroke.SimId = element.Id;
        return new BakedFrame(frame.Frame, strokes);
    }

    // ---- emitters ---------------------------------------------------------------

    /// <summary>
    /// Feed the element. Emitters are stamped in list order so the result does
    /// not depend on how the collection happened to be built.
    /// </summary>
    private static void Emit(FluidSolver solver, SimElement element, int frame, ISimMasks? masks)
    {
        foreach (var emitter in element.Emitters)
        {
            // A blast is a frame or two of emission and then a lot of
            // consequences. An emitter that keeps feeding refuels its own
            // fireball every frame, so it never cools into smoke and never
            // disperses — Q125's finding arriving through a different door.
            if (!emitter.EmitsOn(frame)) continue;

            var (ox, oy) = emitter.OriginAt(frame);

            // A painted mask replaces the shape entirely — it *is* where this
            // emitter emits (Q125), and it is never intersected with whatever the
            // costume happens to be drawing. Alpha lock belongs to the painter.
            if (emitter.MaskLayerId is not null)
            {
                var mask = masks?.For(emitter, frame);
                if (mask is not null) StampMask(solver, emitter, mask, ox - emitter.X, oy - emitter.Y);
                continue;
            }

            switch (emitter.Shape)
            {
                case EmitterShape.Point:
                    Stamp(solver, emitter, ox, oy, 1, ox, oy);
                    break;

                case EmitterShape.Disc:
                    StampDisc(solver, emitter, ox, oy);
                    break;

                case EmitterShape.Segment:
                    // Stepped at half a cell so a near-horizontal burner lays a
                    // continuous line rather than a row of dots.
                    var dx = emitter.X2 - emitter.X;
                    var dy = emitter.Y2 - emitter.Y;
                    var steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy) * 2));
                    for (var s = 0; s <= steps; s++)
                    {
                        var t = s / (double)steps;
                        StampDisc(solver, emitter, ox + dx * t, oy + dy * t);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Feed the fluid wherever the mask has ink, shifted by however far this
    /// emitter has travelled from where it was placed.
    /// </summary>
    /// <remarks>
    /// The shift is the whole of what an origin does to a mask (Q125): with no
    /// motion keyed it is zero, and the mask emits exactly where it was painted.
    /// Sampled bilinearly rather than nearest, because a keyed origin lands on
    /// fractions of a cell and a nearest-neighbour shift would make a smoothly
    /// travelling emitter jump a cell at a time.
    /// </remarks>
    private static void StampMask(
        FluidSolver solver, Emitter emitter, float[] mask, double shiftX, double shiftY)
    {
        int w = solver.Width, h = solver.Height;
        if (mask.Length < w * h) return;

        // A painted mask has no centre of its own, so a burst on one radiates
        // from where the emitter was placed — which is the point the artist
        // dragged and the only one the record knows about.
        var cx = emitter.X + shiftX;
        var cy = emitter.Y + shiftY;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var coverage = SampleMask(mask, w, h, x - shiftX, y - shiftY);
                if (coverage > 0.001) Stamp(solver, emitter, x, y, coverage, cx, cy);
            }
        }
    }

    private static double SampleMask(float[] mask, int w, int h, double px, double py)
    {
        if (px <= -1 || py <= -1 || px >= w || py >= h) return 0;

        var gx = Math.Clamp(px, 0, w - 1.0);
        var gy = Math.Clamp(py, 0, h - 1.0);
        int x0 = (int)gx, y0 = (int)gy;
        int x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
        double tx = gx - x0, ty = gy - y0;

        var top = mask[y0 * w + x0] * (1 - tx) + mask[y0 * w + x1] * tx;
        var bottom = mask[y1 * w + x0] * (1 - tx) + mask[y1 * w + x1] * tx;
        return top * (1 - ty) + bottom * ty;
    }

    private static void StampDisc(FluidSolver solver, Emitter emitter, double cx, double cy)
    {
        var r = Math.Max(emitter.Radius, 0.5);
        var lo = (int)Math.Floor(-r);
        var hi = (int)Math.Ceiling(r);

        for (var oy = lo; oy <= hi; oy++)
        {
            for (var ox = lo; ox <= hi; ox++)
            {
                var x = (int)Math.Floor(cx) + ox;
                var y = (int)Math.Floor(cy) + oy;
                var d = Math.Sqrt(Math.Pow(x + 0.5 - cx, 2) + Math.Pow(y + 0.5 - cy, 2));
                if (d > r) continue;

                // Linear falloff rather than a hard disc: a step edge in the
                // source field puts a step edge in the contour, which reads as a
                // flame with a machined base.
                Stamp(solver, emitter, x, y, 1 - d / r, cx, cy);
            }
        }
    }

    /// <summary>
    /// Put one cell's worth of stuff in, and whatever push goes with it.
    /// </summary>
    /// <param name="originX">
    /// The emitter's centre this frame, which <see cref="Emitter.Burst"/>
    /// radiates from. Only read when there is a burst.
    /// </param>
    private static void Stamp(
        FluidSolver solver, Emitter emitter, double x, double y, double weight,
        double originX, double originY)
    {
        var cx = (int)Math.Floor(x);
        var cy = (int)Math.Floor(y);
        solver.AddDensity(cx, cy, (float)(emitter.Density * weight));
        solver.AddHeat(cx, cy, (float)(emitter.Heat * weight));

        var px = emitter.VelocityX;
        var py = emitter.VelocityY;

        // Expansion rather than an outward velocity: the projection forbids the
        // fluid occupying more room, so a radial push is served by displacement
        // and evacuates the middle, where this genuinely grows. Weighted by
        // coverage, so the emitter's own falloff shapes the front. See
        // `FluidSolver.AddExpansion` for the measurement, including the part
        // where the obvious reasoning about this turned out to be too strong.
        if (emitter.Burst is { } burst && burst != 0) solver.AddExpansion(cx, cy, (float)(burst * weight));

        if (px != 0 || py != 0)
        {
            solver.AddVelocity(cx, cy, (float)(px * weight), (float)(py * weight));
        }
    }

    // ---- embers ------------------------------------------------------------------

    private sealed class Ember
    {
        public double X, Y;
        public int Age;
        public readonly List<StrokePoint> Path = [];

        /// <summary>Start a fresh trail at wherever this frame finds it.</summary>
        public void Begin()
        {
            Path.Clear();
            Path.Add(new StrokePoint(X, Y, 1));
        }

        /// <summary>One solver step's worth of flow.</summary>
        public void Carry(FluidSolver solver)
        {
            var cx = (int)Math.Floor(X);
            var cy = (int)Math.Floor(Y);
            X += solver.VelocityXAt(cx, cy);
            Y += solver.VelocityYAt(cx, cy);
            Path.Add(new StrokePoint(X, Y, 1));
        }
    }

    /// <summary>
    /// Put this frame's embers into the world, spread along the emitters and
    /// seeded from position rather than rolled.
    /// </summary>
    private static void SpawnEmbers(List<Ember> embers, SimElement element, int frame)
    {
        if (element.Particles is not { PerFrame: > 0 } spec || element.Emitters.Count == 0) return;

        for (var i = 0; i < spec.PerFrame; i++)
        {
            var emitter = element.Emitters[i % element.Emitters.Count];
            var (ox, oy) = emitter.OriginAt(frame);

            // Two hashes of the same pair, differently salted: one places the
            // ember along the emitter, one across it. Seeded from the spawn
            // index and the frame, so a re-bake puts the same ember in the same
            // place — invariant 2, at the scale of a particle.
            var along = DeterministicHash.Unit(i, frame, EmberSalt);
            var across = DeterministicHash.Unit(frame, i, EmberSalt + 1);

            double x, y;
            if (emitter.Shape == EmitterShape.Segment)
            {
                x = ox + (emitter.X2 - emitter.X) * along;
                y = oy + (emitter.Y2 - emitter.Y) * along;
            }
            else
            {
                var angle = along * Math.PI * 2;
                var radius = emitter.Radius * Math.Sqrt(across);
                x = ox + Math.Cos(angle) * radius;
                y = oy + Math.Sin(angle) * radius;
            }

            embers.Add(new Ember { X = x, Y = y });
        }
    }

    /// <summary>Collect this frame's trails and retire the embers that are done.</summary>
    private static List<EmberTrail> RetireEmbers(List<Ember> embers, FluidSolver solver, SimElement element)
    {
        var trails = new List<EmberTrail>(embers.Count);
        if (element.Particles is not { } spec) { embers.Clear(); return trails; }

        var life = Math.Max(1, spec.Lifetime);

        for (var i = embers.Count - 1; i >= 0; i--)
        {
            var e = embers[i];
            e.Age++;
            if (e.Path.Count >= 2) trails.Add(new EmberTrail([.. e.Path], e.Age / (double)life));

            if (e.Age >= life || e.X < 0 || e.Y < 0 || e.X >= solver.Width || e.Y >= solver.Height)
            {
                embers.RemoveAt(i);
            }
        }

        // Retiring walks backwards, so the surviving order is reversed; restoring
        // it keeps the stroke list stable between bakes.
        trails.Reverse();
        return trails;
    }

    private static List<Stroke> DrawEmbers(List<EmberTrail> trails, SimElement element, ParticleSpec spec)
    {
        var strokes = new List<Stroke>(trails.Count);

        foreach (var t in trails)
        {
            // An ember fades as it ages rather than vanishing on a frame
            // boundary, which at 12 fps is the difference between sparks and
            // blinking dots.
            var pressure = Math.Clamp(1 - t.Age, 0.05, 1);

            strokes.Add(new Stroke
            {
                Tool = ToolKind.Brush,
                Color = spec.Color,
                Brush = new BrushSettings { Size = Math.Max(spec.Size, 0.1) },
                Points = [.. t.Path.Select(p => new StrokePoint(
                    element.OriginX + p.X * element.Scale,
                    element.OriginY + p.Y * element.Scale,
                    pressure))],
            });
        }

        return strokes;
    }
}

using Lightbox.Core.Documents;
using Lightbox.Raster.Media;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Fire, end to end: emitter, temperature field, heat ramp, embers, re-bake.
/// The first thing here that an artist would recognise as an effect.
/// </summary>
[Collection("Performance")]
public class SimBakerTests(ITestOutputHelper output)
{
    private static SimElement Fire(int frames = 20, int exposeOn = 1)
    {
        var element = new SimElement
        {
            Id = "fire1",
            Kind = "fire",
            FirstFrame = 0,
            FrameCount = frames,
            ExposeOn = exposeOn,
            GridWidth = 72,
            GridHeight = 96,
            OriginX = 0,
            OriginY = 0,
            Scale = 4,
            Substeps = 8,
            BandsFromHeat = true,
            BandColors = ["#3a1200", "#d95f18", "#ffe9a8"],
            OutlineColor = "#2b1400",
        };
        element.Emitters.Add(new Emitter
        {
            Id = "em1", Shape = EmitterShape.Disc, X = 36, Y = 90, Radius = 5, Density = 1, Heat = 1,
        });
        return element;
    }

    private static ResolvedTreatment Plain => LineTreatment.Resolve(null);

    private static List<BakedFrame> Bake(SimElement element, ResolvedTreatment? treatment = null)
    {
        var baker = new SimBaker();
        return baker.Draw(baker.Solve(element), element, treatment ?? Plain);
    }

    private static double MeanY(BakedFrame frame) =>
        frame.Strokes.SelectMany(s => s.Points).Average(p => p.Y);

    // ---- it makes drawings ------------------------------------------------------

    [Fact]
    public void A_Fire_Element_Draws_On_Every_Frame_It_Covers()
    {
        var element = Fire();
        var baked = Bake(element);

        Assert.Equal(element.FrameCount, baked.Count);
        Assert.Equal(Enumerable.Range(0, element.FrameCount), baked.Select(b => b.Frame));

        // Frame 0 has had one puff of heat and may still be under the lowest
        // band; by the time it is burning there is something to draw.
        var burning = baked.Skip(5).ToList();
        Assert.All(burning, b => Assert.NotEmpty(b.Strokes));

        var last = baked[^1];
        output.WriteLine($"{baked.Count} drawings; last has {last.Strokes.Count} strokes " +
                         $"({last.Strokes.Count(s => s.Tool == ToolKind.Fill)} fills)");
        Assert.Contains(last.Strokes, s => s.Tool == ToolKind.Fill);
        Assert.Contains(last.Strokes, s => s.Tool == ToolKind.Brush);
    }

    [Fact]
    public void Every_Stroke_Says_Which_Element_Made_It()
    {
        var baked = Bake(Fire(frames: 8));
        Assert.All(baked.SelectMany(b => b.Strokes), s => Assert.Equal("fire1", s.SimId));
    }

    [Fact]
    public void The_Flame_Rises()
    {
        var baked = Bake(Fire(frames: 24));

        var early = MeanY(baked.First(b => b.Strokes.Count > 0));
        var late = MeanY(baked[^1]);
        output.WriteLine($"mean Y {early:F1} → {late:F1} (up is smaller; the emitter is at Y {90 * 4})");
        Assert.True(late < early - 8, $"the flame did not climb: {early:F1} → {late:F1}");
    }

    [Fact]
    public void An_Element_With_No_Emitters_Draws_Nothing()
    {
        var element = Fire(frames: 6);
        element.Emitters.Clear();

        Assert.All(Bake(element), b => Assert.Empty(b.Strokes));
    }

    // ---- the heat ramp -----------------------------------------------------------

    /// <summary>
    /// Fire bands from <em>temperature</em> and smoke from density — the ramp is
    /// the drawing. Reading the wrong field is not a subtle error and does not
    /// look like one: the bands stop tracking the hot core.
    /// </summary>
    [Fact]
    public void Fire_Bands_From_Temperature_And_Smoke_From_Density()
    {
        var hot = Fire(frames: 16);
        hot.BandsFromHeat = true;
        hot.Params.Cooling = 0.06;      // heat fades fast…
        hot.Params.Dissipation = 0;     // …while the smoke it left does not

        var cool = hot.Clone();
        cool.BandsFromHeat = false;

        var hotArea = Bake(hot)[^1].Strokes.Count(s => s.Tool == ToolKind.Fill);
        var coolArea = Bake(cool)[^1].Strokes.Count(s => s.Tool == ToolKind.Fill);

        var baker = new SimBaker();
        output.WriteLine($"peak — heat {baker.Solve(hot).PeakBand:F3}, density {baker.Solve(cool).PeakBand:F3}; " +
                         $"fills {hotArea} vs {coolArea}");
        Assert.NotEqual(baker.Solve(hot).PeakBand, baker.Solve(cool).PeakBand);
    }

    [Fact]
    public void The_Peak_Is_Reported_So_A_Band_Range_Need_Not_Be_Guessed()
    {
        var solved = new SimBaker().Solve(Fire(frames: 12));

        // Over every frame simulated, so it is at least what the kept frames show.
        var highestKept = solved.Frames.SelectMany(f => f.Band).Max();
        output.WriteLine($"peak {solved.PeakBand:F3}, highest on a drawn frame {highestKept:F3}");
        Assert.True(solved.PeakBand >= highestKept);
        Assert.True(solved.PeakBand > 0.1, "a burning element should reach a usable peak");
    }

    // ---- holds ---------------------------------------------------------------------

    /// <summary>
    /// Holding a drawing is a statement about how often it is redrawn, never
    /// about how fast the fire moves — so the simulation still advances on every
    /// frame and only the tracing is skipped.
    /// </summary>
    [Fact]
    public void Exposing_On_Twos_Halves_The_Drawings_And_Not_The_Motion()
    {
        var ones = Bake(Fire(frames: 20));
        var twos = Bake(Fire(frames: 20, exposeOn: 2));

        Assert.Equal(20, ones.Count);
        Assert.Equal(10, twos.Count);
        Assert.Equal([0, 2, 4, 6, 8, 10, 12, 14, 16, 18], twos.Select(b => b.Frame));

        // Frame 18 is drawn by both, and the fire has had the same eighteen
        // frames of simulation either way, so it is in the same place.
        var a = MeanY(ones.Single(b => b.Frame == 18));
        var b = MeanY(twos.Single(x => x.Frame == 18));
        output.WriteLine($"frame 18 mean Y — on 1s {a:F2}, on 2s {b:F2}");
        Assert.Equal(a, b, 6);
        Assert.Equal(
            new SimBaker().Solve(Fire(frames: 20)).PeakBand,
            new SimBaker().Solve(Fire(frames: 20, exposeOn: 2)).PeakBand);
    }

    // ---- embers --------------------------------------------------------------------

    [Fact]
    public void Embers_Are_Absent_Until_Asked_For()
    {
        var plain = Fire(frames: 12);
        var withEmbers = Fire(frames: 12);
        withEmbers.Particles = new ParticleSpec { PerFrame = 20, Lifetime = 6, Size = 3, Color = "#ffcc66" };

        var before = Bake(plain)[^1].Strokes.Count;
        var after = Bake(withEmbers)[^1].Strokes.Count;

        output.WriteLine($"{before} strokes without embers, {after} with");
        Assert.True(after > before + 10, $"embers added {after - before} strokes");
        Assert.DoesNotContain(Bake(plain).SelectMany(b => b.Strokes), s => s.Color == "#ffcc66");
    }

    [Fact]
    public void Embers_Ride_The_Flow_Upward()
    {
        var element = Fire(frames: 24);
        element.Particles = new ParticleSpec { PerFrame = 16, Lifetime = 10, Color = "#ffcc66" };

        var baked = Bake(element);
        var embers = baked[^1].Strokes.Where(s => s.Color == "#ffcc66").ToList();

        Assert.NotEmpty(embers);
        var climbed = embers.Count(s => s.Points[1].Y < s.Points[0].Y);
        output.WriteLine($"{climbed} of {embers.Count} embers moved upward");
        Assert.True(climbed > embers.Count * 0.6, "embers should mostly be carried up by the plume");
    }

    [Fact]
    public void Embers_Do_Not_Outlive_Their_Lifetime()
    {
        var element = Fire(frames: 20);
        element.Particles = new ParticleSpec { PerFrame = 5, Lifetime = 3 };

        var baked = Bake(element);
        var embers = baked[^1].Strokes.Count(s => s.Color == element.Particles.Color);

        // Five a frame surviving three frames is fifteen at most; anything more
        // means retirement is not happening and the count grows without bound.
        output.WriteLine($"{embers} embers alive at 5 per frame over a lifetime of 3");
        Assert.True(embers <= 15, $"{embers} embers alive, which is more than can exist");
    }

    // ---- does it read as fire ------------------------------------------------------

    /// <summary>
    /// Measures the plume's centre of mass sideways from the emitter over the
    /// second half of the element.
    /// </summary>
    private static double WorstDrift(SimElement element, SolvedElement solved)
    {
        var worst = 0.0;
        var centre = element.Emitters[0].X;

        foreach (var frame in solved.Frames.Skip(solved.Frames.Count / 2))
        {
            double mx = 0, m = 0;
            for (var y = 0; y < solved.Height; y++)
            {
                for (var x = 0; x < solved.Width; x++)
                {
                    var v = frame.Band[y * solved.Width + x];
                    mx += v * x;
                    m += v;
                }
            }
            if (m > 0) worst = Math.Max(worst, Math.Abs(mx / m - centre));
        }

        return worst;
    }

    /// <summary>
    /// A flame stands over its emitter. This is the property the whole of step
    /// 4's tuning was about, and it is the one no other test could have caught:
    /// momentum was conserved, the fluid was incompressible and the smoke was
    /// neither created nor destroyed, and the flame still lay over and drifted
    /// off sideways.
    /// </summary>
    [Fact]
    public void A_Flame_Stands_Over_Its_Emitter_Rather_Than_Lying_Over()
    {
        var element = Fire(frames: 40);
        var drift = WorstDrift(element, new SimBaker().Solve(element));

        output.WriteLine($"worst sideways drift {drift:F1} cells from an emitter at {element.Emitters[0].X}");
        Assert.True(drift < 8, $"the flame wandered {drift:F1} cells off its emitter");
    }

    /// <summary>
    /// …and damping the flow is what keeps it there. Nothing bled momentum away
    /// at first, so a sustained plume in a box with four solid walls accumulated
    /// circulation until it toppled.
    /// </summary>
    [Fact]
    public void Damping_The_Flow_Is_What_Keeps_It_Standing()
    {
        var undamped = Fire(frames: 40);
        undamped.Params.Drag = 0;
        var damped = Fire(frames: 40);

        var loose = WorstDrift(undamped, new SimBaker().Solve(undamped));
        var held = WorstDrift(damped, new SimBaker().Solve(damped));

        output.WriteLine($"worst drift — undamped {loose:F1} cells, damped {held:F1}");
        Assert.True(held < loose * 0.75, $"drag barely helped: {held:F1} against {loose:F1}");
    }

    /// <summary>
    /// The outermost band has to reach the visible edge of the plume. Band
    /// levels are fractions of the element's peak, and a window set too high
    /// puts every band inside the brightest core — which draws a few scraps and
    /// looks like the simulation failing rather than the levels being wrong.
    /// </summary>
    [Fact]
    public void The_Outer_Band_Reaches_The_Edge_Of_The_Plume()
    {
        var element = Fire(frames: 30);
        var baker = new SimBaker();
        var solved = baker.Solve(element);

        var frame = solved.Frames[^1];
        var lit = frame.Band.Count(v => v > solved.PeakBand * 0.01);
        var outer = FieldTracer.LevelOf(0, element.Bands(), 
            (float)(element.BandLow * solved.PeakBand), (float)(element.BandHigh * solved.PeakBand), BandSpacing.Even);
        var inside = frame.Band.Count(v => v > outer);

        output.WriteLine($"the plume covers {lit} cells; the outermost band encloses {inside} " +
                         $"({inside * 100.0 / Math.Max(lit, 1):F0}% of it)");
        Assert.True(lit > 100, "test would be vacuous: there was barely a plume");
        Assert.True(inside > lit * 0.2, $"the outermost band caught only {inside} of {lit} lit cells");
    }

    // ---- determinism ------------------------------------------------------------------

    [Fact]
    public void Two_Bakes_Are_Identical_Stroke_For_Stroke()
    {
        var element = Fire(frames: 14);
        element.Particles = new ParticleSpec { PerFrame = 12, Lifetime = 5 };

        var a = Bake(element);
        var b = Bake(element);

        Assert.Equal(a.Count, b.Count);
        Assert.True(a.Sum(f => f.Strokes.Count) > 40, "test would be vacuous: barely anything was drawn");
        for (var i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Strokes.Count, b[i].Strokes.Count);
            for (var s = 0; s < a[i].Strokes.Count; s++)
            {
                Assert.Equal(a[i].Strokes[s].Points, b[i].Strokes[s].Points);
                Assert.Equal(a[i].Strokes[s].Color, b[i].Strokes[s].Color);
            }
        }
    }

    // ---- the seam the preview depends on -------------------------------------------------

    /// <summary>
    /// Q123's preview rests on this: changing a line treatment must not re-run
    /// the simulation. Solving once and drawing twice has to give exactly what
    /// solving twice would, or the cheap path is not the same feature as the
    /// expensive one.
    /// </summary>
    [Fact]
    public void Restyling_Draws_Again_Without_Solving_Again()
    {
        var element = Fire(frames: 12);
        var baker = new SimBaker();
        var solved = baker.Solve(element);

        var loose = LineTreatment.Resolve(new LineTreatment { Simplify = 1.2, Outlined = OutlinedBands.Every });
        var tight = LineTreatment.Resolve(new LineTreatment { Simplify = 0.05, Outlined = OutlinedBands.Silhouette });

        var a = baker.Draw(solved, element, loose);
        var b = baker.Draw(solved, element, tight);
        var againstAFreshSolve = new SimBaker().Draw(new SimBaker().Solve(element), element, tight);

        Assert.NotEqual(a.Sum(f => f.Strokes.Count), b.Sum(f => f.Strokes.Count));
        Assert.Equal(againstAFreshSolve.Sum(f => f.Strokes.Count), b.Sum(f => f.Strokes.Count));
        output.WriteLine($"loose {a.Sum(f => f.Strokes.Count)} strokes, tight {b.Sum(f => f.Strokes.Count)}");
    }

    // ---- progress and cancellation ---------------------------------------------------------

    [Fact]
    public void Solving_Reports_Progress_All_The_Way()
    {
        var seen = new List<double>();
        new SimBaker().Solve(Fire(frames: 10), new Progress<double>(seen.Add));

        // Progress<T> posts asynchronously, so the assertion is about the values
        // reported rather than about all of them having arrived.
        Assert.All(seen, p => Assert.InRange(p, 0, 1));
    }

    [Fact]
    public void A_Cancelled_Bake_Stops_Rather_Than_Finishing()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.Throws<OperationCanceledException>(() => new SimBaker().Solve(Fire(), cancel: cancel.Token));
    }

    // ---- budget -------------------------------------------------------------------------------

    /// <summary>
    /// The two halves measured side by side, because the whole preview design
    /// rests on them being different by an order of magnitude.
    /// </summary>
    [Fact]
    [Trait("Category", "Performance")]
    public void Solving_Costs_An_Order_Of_Magnitude_More_Than_Drawing()
    {
        var element = Fire(frames: 48);
        element.GridWidth = 192;
        element.GridHeight = 108;
        element.Emitters[0].X = 96;
        element.Emitters[0].Y = 102;
        element.Substeps = 8;

        var baker = new SimBaker();
        SolvedElement? solved = null;
        var solve = Bench.FastestMs(2, () => solved = baker.Solve(element), log: output);
        var draw = Bench.FastestMs(3, () => baker.Draw(solved!, element, Plain), log: output);

        output.WriteLine($"48 frames at 192×108 — solve {solve:F0} ms, draw {draw:F1} ms " +
                         $"({solve / Math.Max(draw, 0.01):F0}× apart)");
        Assert.True(solve > draw * 5, "the two halves are not far enough apart to be worth splitting");
        Assert.True(solve < 6000, $"solving took {solve:F0} ms");
        Assert.True(draw < 500, $"drawing took {draw:F0} ms");
    }
}

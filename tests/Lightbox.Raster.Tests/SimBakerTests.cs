using Lightbox.Core.Documents;
using Lightbox.Core.Effects;
using Lightbox.Core.Inbetween;
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

    // ---- wind and pre-roll (Q122) ---------------------------------------------------

    /// <summary>Where the plume's centre of mass sits, sideways, on the last frame.</summary>
    private static double FinalLean(SolvedElement solved, double centre)
    {
        var frame = solved.Frames[^1];
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
        return m > 0 ? mx / m - centre : 0;
    }

    [Fact]
    public void Wind_Blows_The_Plume_The_Way_It_Points()
    {
        static double Lean(double windX)
        {
            var element = Fire(frames: 30);
            element.WindX = new EffectParam(windX);
            return FinalLean(new SimBaker().Solve(element), element.Emitters[0].X);
        }

        var left = Lean(-0.5);
        var still = Lean(0);
        var right = Lean(0.5);

        output.WriteLine($"final lean — wind left {left:F1}, no wind {still:F1}, wind right {right:F1}");
        Assert.True(left < still - 2, $"a leftward wind did not push the plume left: {left:F1} against {still:F1}");
        Assert.True(right > still + 2, $"a rightward wind did not push the plume right: {right:F1} against {still:F1}");
    }

    /// <summary>
    /// The case Q122 says a simulation wins, and the measurement has to match
    /// the claim. Just after a turn, the smoke <em>high up</em> is still
    /// travelling the old way on its own momentum while the smoke leaving the
    /// emitter already goes the new way — so the plume is bent, top against
    /// bottom. Measuring the whole field's centre of mass instead says nothing:
    /// it is dominated by the dense core at the emitter, which turns within a
    /// frame.
    ///
    /// Baking a "run right" element and a "run left" element and cutting between
    /// them cannot produce this at all, because each starts from still air.
    /// </summary>
    [Fact]
    public void Just_After_A_Turn_The_Old_Smoke_Is_Still_Going_The_Old_Way()
    {
        var element = Fire(frames: 34);
        element.PreRoll = 10;
        element.WindX = new EffectParam
        {
            Keys =
            [
                new EffectKey { Frame = 0, Value = -0.6, Ease = Easing.Linear },
                new EffectKey { Frame = 16, Value = -0.6, Ease = Easing.Linear },
                new EffectKey { Frame = 18, Value = 0.6, Ease = Easing.Linear },
            ],
        };

        var solved = new SimBaker().Solve(element);
        var centre = element.Emitters[0].X;

        // Sideways centre of mass of the top half against the bottom half, two
        // frames after the wind reversed.
        static double LeanOf(SolvedFrame f, int width, int fromRow, int toRow, double centre)
        {
            double mx = 0, m = 0;
            for (var y = fromRow; y < toRow; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var v = f.Band[y * width + x];
                    mx += v * x;
                    m += v;
                }
            }
            return m > 0 ? mx / m - centre : 0;
        }

        var frame = solved.Frames.Single(f => f.Frame == 20);
        var risen = LeanOf(frame, solved.Width, 0, solved.Height / 2, centre);
        var fresh = LeanOf(frame, solved.Width, solved.Height / 2, solved.Height, centre);

        output.WriteLine($"two frames after the turn — risen smoke leans {risen:F1}, fresh smoke {fresh:F1}");
        Assert.True(fresh > risen + 1,
            $"the plume should be bent, with fresh smoke ahead of the old: {fresh:F1} against {risen:F1}");
    }

    [Fact]
    public void Wind_Is_Absent_Rather_Than_Zero_When_Nobody_Asked_For_It()
    {
        var element = Fire(frames: 12);
        Assert.False(element.HasWind);

        var withWind = Fire(frames: 12);
        withWind.WindX = new EffectParam(0);

        // A wind of zero must draw exactly what no wind at all draws, or the
        // key's presence is changing the picture.
        var plain = Bake(element);
        var zeroed = Bake(withWind);
        for (var i = 0; i < plain.Count; i++)
        {
            Assert.Equal(plain[i].Strokes.Count, zeroed[i].Strokes.Count);
        }
    }

    /// <summary>
    /// An element opens on an established plume rather than on still air, which
    /// is the commonest complaint about a fresh effect — the first half-second
    /// looking thin.
    /// </summary>
    [Fact]
    public void A_Pre_Roll_Means_The_First_Frame_Is_Already_Burning()
    {
        var cold = Fire(frames: 12);
        var warmed = Fire(frames: 12);
        warmed.PreRoll = 12;

        var coldStart = new SimBaker().Solve(cold).Frames[0].Band.Sum();
        var warmStart = new SimBaker().Solve(warmed).Frames[0].Band.Sum();

        output.WriteLine($"first frame carries {coldStart:F1} cold, {warmStart:F1} after a pre-roll");
        Assert.True(warmStart > coldStart * 2, $"the pre-roll barely helped: {warmStart:F1} against {coldStart:F1}");
    }

    [Fact]
    public void A_Pre_Roll_Of_Nothing_Changes_Nothing()
    {
        var element = Fire(frames: 10);
        var explicitZero = Fire(frames: 10);
        explicitZero.PreRoll = 0;

        Assert.Equal(
            new SimBaker().Solve(element).Frames[0].Band,
            new SimBaker().Solve(explicitZero).Frames[0].Band);
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

    // ---- drawing one frame ------------------------------------------------------------------

    /// <summary>
    /// The preview draws one frame rather than all of them, and it has to be
    /// <em>the same</em> drawing — a preview that quietly differs from the bake
    /// is worse than no preview, because tuning against it is wasted work.
    /// </summary>
    [Fact]
    public void Drawing_One_Frame_Gives_Exactly_What_The_Whole_Bake_Would()
    {
        var element = Fire(frames: 10);
        var baker = new SimBaker();
        var solved = baker.Solve(element);

        var whole = baker.Draw(solved, element, Plain);

        foreach (var expected in whole)
        {
            var one = baker.DrawAt(solved, element, Plain, null, expected.Frame);

            Assert.NotNull(one);
            Assert.Equal(expected.Frame, one!.Frame);
            Assert.Equal(Shape(expected), Shape(one));
        }

        output.WriteLine($"{whole.Count} frames, each matching stroke for stroke");

        static string Shape(BakedFrame frame) =>
            string.Join(";", frame.Strokes.Select(s =>
                $"{s.Tool}:{s.Color}:{s.Points.Count}:{s.Points[0].X:R},{s.Points[0].Y:R}"));
    }

    /// <summary>
    /// On 2s there is no drawing on the odd frames, and answering "nothing"
    /// would make the preview blink on every other frame of an element that is
    /// perfectly fine. The nearest drawing at or before the frame is what the
    /// timeline actually exposes there.
    /// </summary>
    [Fact]
    public void Drawing_Across_A_Hold_Answers_The_Drawing_That_Is_Exposed()
    {
        var element = Fire(frames: 10, exposeOn: 2);
        var baker = new SimBaker();
        var solved = baker.Solve(element);

        var onTheDrawing = baker.DrawAt(solved, element, Plain, null, 4);
        var acrossTheHold = baker.DrawAt(solved, element, Plain, null, 5);
        var earlier = baker.DrawAt(solved, element, Plain, null, 2);

        Assert.NotNull(onTheDrawing);
        Assert.NotEmpty(onTheDrawing!.Strokes);
        Assert.Equal(4, acrossTheHold!.Frame);
        Assert.Equal(2, earlier!.Frame);
    }

    /// <summary>
    /// Before the element starts there is nothing to show, and drawing frame
    /// zero's plume there would put fire on the screen a second early.
    /// </summary>
    [Fact]
    public void Drawing_Before_The_Element_Starts_Answers_Nothing()
    {
        var element = Fire(frames: 6);
        element.FirstFrame = 20;
        var baker = new SimBaker();
        var solved = baker.Solve(element);

        Assert.Null(baker.DrawAt(solved, element, Plain, null, 19));
        Assert.NotNull(baker.DrawAt(solved, element, Plain, null, 20));
    }

    // ---- bursts and timed emission -----------------------------------------------------------

    private static SimElement Blast(double burst, int? until = 2)
    {
        var element = new SimElement
        {
            Id = "blast", Kind = "smoke", FirstFrame = 0, FrameCount = 12,
            GridWidth = 64, GridHeight = 64, Scale = 4, Substeps = 8,
            BandColors = ["#222", "#666", "#aaa"],
            // Buoyancy off: a plume that rockets upward swamps the thing being
            // measured, which is exactly the confound that made the first
            // reading of this feature say it did nothing.
            Params = new SimParams { Buoyancy = 0, Weight = 0, Vorticity = 0, Turbulence = 0, Drag = 0 },
        };
        element.Emitters.Add(new Emitter
        {
            Id = "em1", Shape = EmitterShape.Disc, X = 32, Y = 32, Radius = 5,
            Density = 2, Heat = 0, Burst = burst, EmitUntil = until,
        });
        return element;
    }

    private static double Width(BakedFrame f) =>
        f.Strokes.Count == 0 ? 0
            : f.Strokes.SelectMany(s => s.Points).Max(p => p.X)
              - f.Strokes.SelectMany(s => s.Points).Min(p => p.X);

    /// <summary>
    /// A burst expands the front, and it is the emitter that carries it through.
    /// </summary>
    [Fact]
    public void A_Burst_Expands_The_Front()
    {
        var still = Bake(Blast(0));
        var blown = Bake(Blast(0.9));

        var a = Width(still[^1]);
        var b = Width(blown[^1]);
        output.WriteLine($"last frame: no burst {a:F0} px wide, burst {b:F0} px");
        Assert.True(b > a * 1.2, $"the burst widened the front from {a:F0} to only {b:F0}");
    }

    /// <summary>
    /// Emission stops when it is told to, and that is what makes a blast a blast.
    /// </summary>
    /// <remarks>
    /// An emitter that keeps feeding refuels its own fireball every frame, so it
    /// never cools into smoke and never disperses — the same failure a painted
    /// area mask has (Q125), arriving through a different door. Measured on the
    /// field's total rather than on the drawing, because a contour can move for
    /// several reasons and the mass can only move for one.
    /// </remarks>
    [Fact]
    public void A_Timed_Emitter_Stops_Feeding()
    {
        var timed = new SimBaker().Solve(Blast(0, until: 2));
        var forever = new SimBaker().Solve(Blast(0, until: null));

        output.WriteLine($"peak band: two frames of emission {timed.PeakBand:F2}, " +
                         $"the whole element {forever.PeakBand:F2}");
        Assert.True(forever.PeakBand > timed.PeakBand * 1.5,
            "an emitter told to stop went on feeding");
    }

    /// <summary>
    /// And it starts when it is told to: nothing is drawn before the emitter's
    /// first frame, so a blast can go off in the middle of a shot.
    /// </summary>
    [Fact]
    public void A_Timed_Emitter_Starts_When_It_Is_Told_To()
    {
        var element = Blast(0.5, until: 8);
        element.Emitters[0].EmitFrom = 5;

        var baked = Bake(element);

        Assert.All(baked.Take(5), f => Assert.Empty(f.Strokes));
        Assert.NotEmpty(baked[7].Strokes);
        output.WriteLine($"first drawing on frame {baked.First(f => f.Strokes.Count > 0).Frame}");
    }

    /// <summary>An emitter nobody bounded emits throughout, which is the default everything else relies on.</summary>
    [Fact]
    public void An_Unbounded_Emitter_Emits_On_Every_Frame()
    {
        var e = new Emitter();
        Assert.False(e.IsTimed);
        Assert.True(e.EmitsOn(0));
        Assert.True(e.EmitsOn(1000));
        Assert.True(e.EmitsOn(-5));

        var timed = new Emitter { EmitFrom = 4, EmitUntil = 6 };
        Assert.True(timed.IsTimed);
        Assert.False(timed.EmitsOn(3));
        Assert.True(timed.EmitsOn(4));
        Assert.True(timed.EmitsOn(5));
        Assert.False(timed.EmitsOn(6));
    }

    // ---- progress and cancellation ---------------------------------------------------------

    /// <summary>
    /// Progress is reported, every value is a fraction, and the last one is the
    /// end (B296).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This used to collect through <c>Progress&lt;double&gt;</c> and race
    /// itself.</b> That type posts each callback to the thread pool, so late
    /// callbacks were still calling <c>List.Add</c> while <c>Assert.All</c>
    /// enumerated — <c>Collection was modified; enumeration operation may not
    /// execute</c>, from <c>List.Enumerator.MoveNext</c>. It failed about one CI
    /// run in three and reproduces on every local Release run of this suite.
    /// </para>
    /// <para>
    /// <b>The fix is to collect synchronously rather than to tolerate the
    /// asynchrony</b>, which is a step past the snapshot B296 proposed and buys
    /// the assertion the old test could not make. <c>Solve</c> takes
    /// <c>IProgress&lt;double&gt;</c>, so a collector that records on the
    /// calling thread is a legitimate implementation of it — and with nothing
    /// arriving late, the test can assert that progress happened <em>at all</em>
    /// and that it reached the end. The old version passed on an empty list,
    /// which is the sanity-check-the-other-way trap: it would have gone green on
    /// a baker that reported nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public void Solving_Reports_Progress_Without_Racing_Its_Own_Callbacks()
    {
        var seen = new SynchronousProgress();
        new SimBaker().Solve(Fire(frames: 10), seen);

        Assert.NotEmpty(seen.Values);
        Assert.All(seen.Values, p => Assert.InRange(p, 0, 1));
        Assert.Equal(1.0, seen.Values[^1], 3);
    }

    /// <summary>
    /// An <see cref="IProgress{T}"/> that records on the calling thread, so a
    /// test can read what it collected without racing it.
    /// </summary>
    private sealed class SynchronousProgress : IProgress<double>
    {
        public List<double> Values { get; } = [];

        public void Report(double value) => Values.Add(value);
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

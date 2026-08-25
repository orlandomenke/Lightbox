using Lightbox.Core.Documents;
using Lightbox.Raster.Media;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The fluid solver behind effects elements. The assertions here are mostly
/// <em>properties</em> rather than pictures — determinism, a maximum principle,
/// symmetry, divergence removal — because a fluid solver is the kind of code
/// where an index slip produces something that still looks like smoke.
/// </summary>
[Collection("Performance")]
public class FluidSolverTests(ITestOutputHelper output)
{
    private static SimParams Typical => new()
    {
        Buoyancy = 0.06, Weight = 0.01, Vorticity = 0.35,
        Turbulence = 0.5, TurbulenceScale = 12, TurbulenceDrift = 0.05,
        Dissipation = 0.004, Cooling = 0.02, Drag = 0,
    };

    private static SimParams Inert => new()
    {
        Buoyancy = 0, Weight = 0, Vorticity = 0, Turbulence = 0,
        TurbulenceScale = 8, TurbulenceDrift = 0, Dissipation = 0, Cooling = 0,
        Drag = 0,   // inert means inert: the record's default damps the flow
    };

    /// <summary>A warm disc of smoke low in the frame — one puff about to rise.</summary>
    private static FluidSolver Puff(int w, int h, float density = 1f, float heat = 1f)
    {
        var s = new FluidSolver(w, h);
        Seed(s, w / 2f, h * 0.75f, Math.Min(w, h) / 6f, density, heat);
        return s;
    }

    /// <summary>
    /// A disc, centred in <em>continuous</em> cell coordinates — cell
    /// <c>x</c> spans <c>[x, x+1]</c>, so its centre is <c>x + 0.5</c> and the
    /// grid's own mirror line is at <c>w/2</c>.
    /// </summary>
    /// <remarks>
    /// Seeding on integer cell centres instead put the disc half a cell off the
    /// mirror line, which made the symmetry test below fail by 31% of peak
    /// against a solver that was in fact symmetric. Worth the comment: an
    /// asymmetric fixture reads exactly like an asymmetric solver.
    /// </remarks>
    private static void Seed(FluidSolver s, float cx, float cy, float r, float density, float heat)
    {
        for (var y = 0; y < s.Height; y++)
        {
            for (var x = 0; x < s.Width; x++)
            {
                var dx = x + 0.5f - cx;
                var dy = y + 0.5f - cy;
                if (dx * dx + dy * dy > r * r) continue;
                s.AddDensity(x, y, density);
                s.AddHeat(x, y, heat);
            }
        }
    }

    private static float[] Density(FluidSolver s)
    {
        var buf = new float[s.Width * s.Height];
        s.ReadDensity(buf);
        return buf;
    }

    private static float[] Velocity(FluidSolver s)
    {
        var buf = new float[s.Width * s.Height * 2];
        for (var y = 0; y < s.Height; y++)
        {
            for (var x = 0; x < s.Width; x++)
            {
                buf[(y * s.Width + x) * 2] = s.VelocityXAt(x, y);
                buf[(y * s.Width + x) * 2 + 1] = s.VelocityYAt(x, y);
            }
        }
        return buf;
    }

    /// <summary>Density-weighted centre of mass. Y grows downward, so rising means this falls.</summary>
    private static (double X, double Y) Centroid(FluidSolver s)
    {
        double mx = 0, my = 0, m = 0;
        for (var y = 0; y < s.Height; y++)
        {
            for (var x = 0; x < s.Width; x++)
            {
                var d = s.DensityAt(x, y);
                mx += d * x;
                my += d * y;
                m += d;
            }
        }
        Assert.True(m > 1e-6, "test would be vacuous: no density left to weigh");
        return (mx / m, my / m);
    }

    // ---- determinism (invariant 2) ------------------------------------------

    [Fact]
    public void Two_Runs_Are_Bit_Identical()
    {
        var a = Puff(48, 64);
        var b = Puff(48, 64);
        a.Run(60, Typical);
        b.Run(60, Typical);

        float[] da = Density(a), db = Density(b);
        Assert.True(da.Sum() > 100, $"test would be vacuous: only {da.Sum()} density");

        for (var i = 0; i < da.Length; i++)
        {
            Assert.True(BitConverter.SingleToInt32Bits(da[i]) == BitConverter.SingleToInt32Bits(db[i]),
                $"density diverged at cell {i}: {da[i]} vs {db[i]}");
        }

        float[] va = Velocity(a), vb = Velocity(b);
        for (var i = 0; i < va.Length; i++)
        {
            Assert.True(BitConverter.SingleToInt32Bits(va[i]) == BitConverter.SingleToInt32Bits(vb[i]),
                $"velocity diverged at {i}: {va[i]} vs {vb[i]}");
        }
    }

    /// <summary>
    /// The property that lets a bake report progress and be cancelled without
    /// changing what it produces. It is why the step counter is solver state
    /// rather than a loop variable — seed the turbulence from a local and this
    /// test fails, while nothing else does.
    /// </summary>
    [Fact]
    public void Splitting_A_Run_Matches_One_Long_Run()
    {
        var whole = Puff(40, 56);
        var split = Puff(40, 56);

        whole.Run(30, Typical);

        split.Run(7, Typical);
        split.Run(11, Typical);
        split.Run(12, Typical);

        Assert.Equal(whole.Step, split.Step);

        float[] a = Density(whole), b = Density(split);
        Assert.True(a.Sum() > 50, $"test would be vacuous: only {a.Sum()} density");
        for (var i = 0; i < a.Length; i++)
        {
            Assert.True(BitConverter.SingleToInt32Bits(a[i]) == BitConverter.SingleToInt32Bits(b[i]),
                $"cell {i}: whole run {a[i]}, split run {b[i]}");
        }
    }

    [Fact]
    public void Run_Zero_Changes_Nothing_At_All()
    {
        var s = Puff(32, 32);
        var before = Density(s);

        s.Run(0, Typical);
        s.Run(-5, Typical);

        Assert.Equal(0, s.Step);
        Assert.Equal(before, Density(s));
    }

    [Fact]
    public void Reset_Puts_It_Back_To_Empty_Still_Air()
    {
        var s = Puff(32, 32);
        s.Run(10, Typical);
        Assert.True(s.TotalDensity() > 0);

        s.Reset();

        Assert.Equal(0, s.Step);
        Assert.Equal(0, s.TotalDensity());
        Assert.All(Velocity(s), v => Assert.Equal(0f, v));
    }

    // ---- conservation --------------------------------------------------------

    /// <summary>
    /// Density is only ever <em>moved</em>: a flux leaves one cell and arrives in
    /// exactly one other. Nothing but <c>Dissipation</c> may change the total,
    /// and no cell may go negative.
    /// </summary>
    /// <remarks>
    /// This is the assertion the transport scheme was changed for. Under the
    /// semi-Lagrangian advection it started with, a closed swirl came out
    /// holding 293% of the density that went in — every individual cell a
    /// plausible number, and no test anywhere that could say so.
    /// </remarks>
    [Fact]
    public void Transport_Moves_Density_Without_Creating_Or_Losing_Any()
    {
        var s = Puff(56, 72, density: 0.8f);
        var sealed_ = Typical with { Dissipation = 0f, Cooling = 0f };

        var start = s.TotalDensity();
        Assert.True(start > 100, $"test would be vacuous: only {start} density");

        for (var i = 0; i < 12; i++)
        {
            s.Run(5, sealed_);
            var total = s.TotalDensity();
            Assert.True(Math.Abs(total - start) < start * 1e-4,
                $"total moved from {start:F4} to {total:F4} after {(i + 1) * 5} steps");
            Assert.True(Density(s).All(d => d >= 0f), $"a cell went negative after {(i + 1) * 5} steps");
        }

        // …and it did not simply sit still, which would make the above vacuous.
        var moved = Centroid(s);
        output.WriteLine($"total {start:F4} → {s.TotalDensity():F4}, centroid Y {72 * 0.75:F1} → {moved.Y:F1}");
        Assert.True(Math.Abs(moved.Y - 72 * 0.75) > 2, "nothing moved, so nothing was tested");
    }

    /// <summary>
    /// A fast swirl is where the old scheme failed worst, so it is the case
    /// worth pinning: a closed rotation for two hundred steps, and the total
    /// must not shift.
    /// </summary>
    [Fact]
    public void A_Fast_Closed_Swirl_Neither_Gains_Nor_Loses()
    {
        var s = new FluidSolver(64, 64);
        Seed(s, 32f, 20f, 5f, density: 1f, heat: 0f);
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                s.AddVelocity(x, y, -(y + 0.5f - 32f) * 0.02f, (x + 0.5f - 32f) * 0.02f);
            }
        }

        var start = s.TotalDensity();
        s.Run(200, Inert);
        var end = s.TotalDensity();

        output.WriteLine($"swirl total {start:F4} → {end:F4} ({end / start:P4})");
        Assert.True(Math.Abs(end - start) < start * 1e-4, $"{end / start:P2} of the density survived");
    }

    // ---- incompressibility ---------------------------------------------------

    [Fact]
    public void Projection_Removes_Most_Of_The_Divergence()
    {
        var s = new FluidSolver(48, 48);

        // A source in the middle: every cell around it pushed straight outward,
        // which is divergence and nothing else.
        for (var y = 20; y < 28; y++)
        {
            for (var x = 20; x < 28; x++)
            {
                s.AddVelocity(x, y, (x - 23.5f) * 0.15f, (y - 23.5f) * 0.15f);
                s.AddDensity(x, y, 1f);
            }
        }

        var before = s.MeanAbsDivergence();
        s.Run(1, Inert);
        var after = s.MeanAbsDivergence();

        output.WriteLine($"mean |div| {before:E3} → {after:E3} ({before / Math.Max(after, 1e-30):F1}× better)");
        Assert.True(before > 1e-3, $"test would be vacuous: the seed was already divergence-free ({before:E3})");
        Assert.True(after < before * 0.4, $"projection left {after:E3} of {before:E3}");
    }

    /// <summary>
    /// The property that actually matters, and the one a single-step reduction
    /// cannot express: as a plume accelerates, the divergence the solve leaves
    /// behind must stay a small and <em>shrinking</em> share of how fast the
    /// fluid is moving. A fixed number of sweeps never reaches zero — this is
    /// what says the residual is bounded rather than accumulating, which is the
    /// difference between smoke that billows and smoke that inflates.
    /// </summary>
    [Fact]
    public void The_Residual_Divergence_Stays_A_Small_Share_Of_The_Flow()
    {
        var s = Puff(48, 48);
        var worst = 0.0;

        for (var step = 1; step <= 20; step++)
        {
            s.Run(1, Typical);

            double speed = 0;
            for (var y = 0; y < s.Height; y++)
            {
                for (var x = 0; x < s.Width; x++)
                {
                    speed += Math.Abs(s.VelocityXAt(x, y)) + Math.Abs(s.VelocityYAt(x, y));
                }
            }
            speed /= s.Width * s.Height;
            Assert.True(speed > 1e-6, $"test would be vacuous: nothing was moving at step {step}");

            var ratio = s.MeanAbsDivergence() / speed;
            if (step >= 3) worst = Math.Max(worst, ratio);
            if (step % 5 == 0) output.WriteLine($"step {step}: |div|/|v| = {ratio:P2}");
        }

        Assert.True(worst < 0.10, $"the fluid stayed {worst:P2} compressible");
    }

    /// <summary>
    /// Smoke driven hard into the ceiling has to pile up against it, not vanish
    /// through it. A solver that loses mass at the frame edge does not look like
    /// a bug — it looks like an element that fades — which is why this is
    /// asserted rather than watched.
    /// </summary>
    [Fact]
    public void Nothing_Leaks_Out_Through_The_Walls()
    {
        var s = new FluidSolver(32, 40);
        Seed(s, 16f, 10f, 5f, density: 1f, heat: 0f);

        for (var y = 0; y < s.Height; y++)
        {
            for (var x = 0; x < s.Width; x++) s.AddVelocity(x, y, 0f, -0.9f);
        }

        var before = s.TotalDensity();
        Assert.True(before > 50, $"test would be vacuous: only {before} density seeded");

        s.Run(60, Typical with { Buoyancy = 0.4f, Dissipation = 0f, Cooling = 0f });
        var after = s.TotalDensity();

        // Piled against the ceiling, not spread through the frame.
        double top = 0;
        for (var x = 0; x < s.Width; x++) top += s.DensityAt(x, 0) + s.DensityAt(x, 1);

        output.WriteLine($"total density {before:F2} → {after:F2} ({after / before:P2}), " +
                         $"{top / after:P1} of it in the top two rows");
        Assert.True(Math.Abs(after - before) < before * 1e-4,
            $"{(1 - after / before):P2} of the smoke went through the wall");
        Assert.True(top > after * 0.1, "the smoke never reached the ceiling, so nothing was tested");
    }

    // ---- symmetry: the index-slip catcher -------------------------------------

    /// <summary>
    /// With the turbulence off, a setup symmetric about the vertical centre line
    /// must stay symmetric. This is the assertion Jacobi iteration was chosen
    /// for — Gauss-Seidel reads its own partial results, so a row-major sweep
    /// biases the answer along the sweep and this could only ever have been
    /// asserted loosely.
    ///
    /// Exact bit equality is still not available: mirrored bilinear weights are
    /// <c>a + (b−a)·t</c> against <c>b + (a−b)·(1−t)</c>, which agree in real
    /// arithmetic and can differ in the last place in floating point. The
    /// tolerance below is several orders above that and several below anything
    /// an index slip produces.
    /// </summary>
    [Fact]
    public void A_Symmetric_Setup_Stays_Symmetric()
    {
        const int w = 32, h = 44;
        var s = new FluidSolver(w, h);
        Seed(s, w / 2f, h * 0.75f, 6f, density: 1f, heat: 1f);

        s.Run(40, Typical with { Turbulence = 0f });

        var d = Density(s);
        var peak = d.Max();
        Assert.True(peak > 0.05f, $"test would be vacuous: peak density is only {peak}");

        double worst = 0;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w / 2; x++)
            {
                // Cell x mirrors cell w-1-x about the centre line.
                worst = Math.Max(worst, Math.Abs(d[y * w + x] - d[y * w + (w - 1 - x)]));
            }
        }

        output.WriteLine($"peak {peak:F5}, worst mirror difference {worst:E3} ({worst / peak:P4} of peak)");
        Assert.True(worst < peak * 1e-3, $"asymmetric by {worst:E3} against a peak of {peak:F5}");
    }

    /// <summary>
    /// The other half of the pair: the seeded turbulence has to actually do
    /// something, or the test above passes on a build where it is wired to
    /// nothing.
    /// </summary>
    [Fact]
    public void Turbulence_Breaks_The_Symmetry_And_Does_So_Reproducibly()
    {
        const int w = 32, h = 44;

        static FluidSolver Run(float turbulence)
        {
            var s = new FluidSolver(w, h);
            Seed(s, w / 2f, h * 0.75f, 6f, density: 1f, heat: 1f);
            s.Run(40, Typical with { Turbulence = turbulence });
            return s;
        }

        var stirred = Density(Run(2.5f));
        var still = Density(Run(0f));

        double stirredWorst = 0, stillWorst = 0;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w / 2; x++)
            {
                var m = w - 1 - x;
                stirredWorst = Math.Max(stirredWorst, Math.Abs(stirred[y * w + x] - stirred[y * w + m]));
                stillWorst = Math.Max(stillWorst, Math.Abs(still[y * w + x] - still[y * w + m]));
            }
        }

        output.WriteLine($"worst mirror difference — stirred {stirredWorst:E3}, still {stillWorst:E3}");
        Assert.True(stirredWorst > stillWorst * 1000,
            $"turbulence barely moved anything: {stirredWorst:E3} against {stillWorst:E3}");

        // Reproducibly: the asymmetry is seeded from position, not rolled.
        Assert.Equal(Density(Run(2.5f)), stirred);
    }

    // ---- does it behave like fluid --------------------------------------------

    [Fact]
    public void Heat_Rises()
    {
        var s = Puff(40, 64);
        var (_, y0) = Centroid(s);

        s.Run(60, Typical with { Weight = 0f, Dissipation = 0f, Cooling = 0f });

        var (_, y1) = Centroid(s);
        output.WriteLine($"centroid Y {y0:F2} → {y1:F2} (up is smaller)");
        Assert.True(y1 < y0 - 1.0, $"a hot puff did not rise: {y0:F2} → {y1:F2}");
    }

    [Fact]
    public void Cold_Smoke_Sinks_Under_Its_Own_Weight()
    {
        var s = new FluidSolver(40, 64);
        Seed(s, 20f, 20f, 6f, density: 1f, heat: 0f);
        var (_, y0) = Centroid(s);

        s.Run(60, Typical with { Buoyancy = 0f, Weight = 0.06f, Dissipation = 0f, Cooling = 0f });

        var (_, y1) = Centroid(s);
        output.WriteLine($"centroid Y {y0:F2} → {y1:F2} (down is larger)");
        Assert.True(y1 > y0 + 1.0, $"cold smoke did not sink: {y0:F2} → {y1:F2}");
    }

    /// <summary>
    /// Confinement exists because semi-Lagrangian advection is stable by
    /// averaging, and averaging eats small vortices — a plume without it smooths
    /// into a cone and stops reading as fire. Measured as total |curl|, which is
    /// the thing being eaten.
    /// </summary>
    [Fact]
    public void Vorticity_Confinement_Keeps_The_Curl_Alive()
    {
        static double Curl(float vorticity)
        {
            var s = new FluidSolver(48, 64);
            Seed(s, 24f, 48f, 8f, density: 1f, heat: 1f);
            s.Run(60, Typical with { Vorticity = vorticity });

            double total = 0;
            for (var y = 1; y < s.Height - 1; y++)
            {
                for (var x = 1; x < s.Width - 1; x++)
                {
                    var dvdx = (s.VelocityYAt(x + 1, y) - s.VelocityYAt(x - 1, y)) * 0.5;
                    var dudy = (s.VelocityXAt(x, y + 1) - s.VelocityXAt(x, y - 1)) * 0.5;
                    total += Math.Abs(dvdx - dudy);
                }
            }
            return total;
        }

        var without = Curl(0f);
        var with = Curl(1.2f);
        output.WriteLine($"total |curl| — without confinement {without:E3}, with {with:E3}");
        Assert.True(without > 0, "test would be vacuous: there was no curl either way");
        Assert.True(with > without * 1.2, $"confinement added nothing: {with:E3} against {without:E3}");
    }

    /// <summary>
    /// Turbulence that re-seeds every step is white noise in time: it reads as a
    /// shimmer rather than as motion, and it is exactly the flicker the charter
    /// forbids. Drifting the sample point through the noise keeps consecutive
    /// steps related, and this is what says so.
    /// </summary>
    [Fact]
    public void Turbulence_Evolves_Smoothly_Rather_Than_Flickering()
    {
        var s = new FluidSolver(48, 48);
        Seed(s, 24f, 36f, 8f, density: 1f, heat: 0.4f);
        var stirring = Typical with { Buoyancy = 0f, Vorticity = 0f, Turbulence = 2f, TurbulenceDrift = 0.05f };

        s.Run(20, stirring);
        var a = Velocity(s);
        s.Run(1, stirring);
        var b = Velocity(s);

        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        var correlation = dot / Math.Sqrt(Math.Max(na * nb, 1e-30));

        output.WriteLine($"step-to-step velocity correlation {correlation:F4} (|a|={Math.Sqrt(na):E2})");
        Assert.True(na > 1e-6, "test would be vacuous: the field was not moving at all");
        Assert.True(correlation > 0.9, $"the field was re-rolled rather than advanced: correlation {correlation:F4}");
    }

    // ---- robustness ------------------------------------------------------------

    [Fact]
    public void Extreme_Parameters_Do_Not_Produce_NaN()
    {
        double[] wild = [0, 1, 50, 1000, -20, double.NaN, double.PositiveInfinity];

        foreach (var b in wild)
        {
            foreach (var t in wild)
            {
                var s = Puff(24, 32, density: 5f, heat: 5f);
                s.Run(12, new SimParams
                {
                    Buoyancy = b, Weight = b, Vorticity = t,
                    Turbulence = t, TurbulenceScale = t, TurbulenceDrift = t,
                    Dissipation = 0, Cooling = 0,
                });

                foreach (var d in Density(s))
                {
                    Assert.True(float.IsFinite(d), $"density went to {d} at buoyancy {b}, turbulence {t}");
                }
                foreach (var v in Velocity(s))
                {
                    Assert.True(float.IsFinite(v), $"velocity went to {v} at buoyancy {b}, turbulence {t}");
                }
            }
        }
    }

    [Fact]
    public void A_Rented_Solver_Runs_Exactly_What_A_Fresh_One_Would()
    {
        // Dirty the cache with a bigger, hotter element first: a rent that did
        // not clear would carry that smoke into the next bake, and the same
        // document would then reload differently.
        var dirty = FluidSolver.Rent(64, 64);
        Seed(dirty, 32f, 32f, 20f, density: 4f, heat: 4f);
        dirty.Run(25, Typical);

        var rented = FluidSolver.Rent(40, 56);
        Seed(rented, 20f, 42f, 6f, density: 1f, heat: 1f);
        rented.Run(30, Typical);

        var fresh = new FluidSolver(40, 56);
        Seed(fresh, 20f, 42f, 6f, density: 1f, heat: 1f);
        fresh.Run(30, Typical);

        Assert.Equal(Density(fresh), Density(rented));
    }

    // ---- budget -----------------------------------------------------------------

    /// <summary>
    /// A whole element's bake: 48 frames at 192×108 on 8 substeps.
    /// </summary>
    /// <remarks>
    /// <b>The budget and the target are different numbers.</b>
    /// <c>docs/DESIGN-fluid-effects.md</c> sets a *product* target — two seconds
    /// from pressing Generate to seeing the drawings — and this machine bakes it
    /// in about 1.4 s. The number asserted below is a *regression* budget, set
    /// near the charter's usual 4× above the observed cost so a shared runner
    /// with fewer cores does not fail the build for being busy. It catches the
    /// solver going quadratic in the grid or growing an allocation per step; the
    /// product target is judged by measuring, not by this assertion.
    /// </remarks>
    [Fact]
    [Trait("Category", "Performance")]
    public void A_Whole_Element_Bakes_Inside_Its_Budget()
    {
        const int w = 192, h = 108, frames = 48, substeps = 8;

        var ms = Bench.FastestMs(3, () =>
        {
            var s = FluidSolver.Rent(w, h);
            for (var f = 0; f < frames; f++)
            {
                for (var x = w / 2 - 6; x < w / 2 + 6; x++)
                {
                    s.AddDensity(x, h - 4, 1f);
                    s.AddHeat(x, h - 4, 1f);
                }
                s.Run(substeps, Typical);
            }
        }, log: output);

        output.WriteLine($"{frames} frames × {substeps} substeps at {w}×{h}: {ms:F0} ms " +
                         $"({ms / (frames * substeps):F2} ms/step)");
        Assert.True(ms < 5500, $"a 48-frame bake took {ms:F0} ms");
    }

    // ---- expansion ----------------------------------------------------------------

    /// <summary>How far density has spread from a point, in cells.</summary>
    private static double Reach(FluidSolver s, int cx, int cy, float floor = 0.02f)
    {
        double max = 0;
        for (var y = 0; y < s.Height; y++)
        {
            for (var x = 0; x < s.Width; x++)
            {
                if (s.DensityAt(x, y) <= floor) continue;
                var r = Math.Sqrt(Math.Pow(x - cx, 2) + Math.Pow(y - cy, 2));
                if (r > max) max = r;
            }
        }
        return max;
    }

    private static FluidSolver WithABlock(int size = 10)
    {
        var s = new FluidSolver(64, 64);
        var lo = 32 - size / 2;
        for (var y = lo; y < lo + size; y++)
        {
            for (var x = lo; x < lo + size; x++) s.AddDensity(x, y, 1f);
        }
        return s;
    }

    /// <summary>
    /// <b>Why expansion is not a radial push, measured rather than asserted.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first version of this test claimed a radial push does *nothing*,
    /// because the projection removes divergence. It failed: pushing outward
    /// moves the front perfectly well. What the projection actually does is
    /// forbid the fluid from occupying more room, so the push is served by
    /// <em>displacement</em> instead — the fluid rolls outward and the middle is
    /// evacuated behind it. An expansion source has no such constraint to route
    /// around, so the blob grows while staying a blob.
    /// </para>
    /// <para>
    /// Both look like "it got bigger" in a single number, which is why the
    /// distinction is measured at the centre. Written down because the wrong
    /// version of this reasoning was believed for an hour on the strength of a
    /// confounded measurement — a burst tested against a plume whose buoyancy
    /// swamped it.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_Radial_Push_Hollows_The_Middle_Where_Expansion_Keeps_It()
    {
        var pushed = WithABlock();
        for (var y = 27; y < 37; y++)
        {
            for (var x = 27; x < 37; x++)
            {
                var ax = x + 0.5 - 32;
                var ay = y + 0.5 - 32;
                var len = Math.Sqrt(ax * ax + ay * ay);
                if (len > 1e-9) pushed.AddVelocity(x, y, (float)(0.9 * ax / len), (float)(0.9 * ay / len));
            }
        }
        pushed.Run(8, Inert);

        var expanded = WithABlock();
        for (var y = 27; y < 37; y++)
        {
            for (var x = 27; x < 37; x++) expanded.AddExpansion(x, y, 0.9f);
        }
        expanded.Run(8, Inert);

        var still = WithABlock();
        still.Run(8, Inert);

        double Middle(FluidSolver s)
        {
            double sum = 0;
            for (var y = 30; y < 34; y++)
            {
                for (var x = 30; x < 34; x++) sum += s.DensityAt(x, y);
            }
            return sum / 16;
        }

        output.WriteLine(
            $"still:    reach {Reach(still, 32, 32):F1}, middle {Middle(still):F3}\n" +
            $"pushed:   reach {Reach(pushed, 32, 32):F1}, middle {Middle(pushed):F3}\n" +
            $"expanded: reach {Reach(expanded, 32, 32):F1}, middle {Middle(expanded):F3}");

        // Both spread it — that is the part a single number cannot tell apart.
        Assert.True(Reach(pushed, 32, 32) > Reach(still, 32, 32));
        Assert.True(Reach(expanded, 32, 32) > Reach(still, 32, 32));

        // The middle is where they differ, and it is what an artist sees: a
        // fireball with a hole in it against one that fills out.
        Assert.True(Middle(pushed) < Middle(expanded),
            "a radial push should evacuate the middle more than an expansion does");
    }

    /// <summary>
    /// Expansion enters where divergence is decided, and there it works: the
    /// blob grows and thins.
    /// </summary>
    /// <remarks>
    /// Thinning is the half that says it is really expansion rather than a push
    /// — the same stuff occupying more room. A blob that grew without thinning
    /// would be one that had gained mass.
    /// </remarks>
    [Fact]
    public void Expansion_Grows_A_Blob_And_Thins_It()
    {
        var reaches = new List<double>();
        var peaks = new List<double>();

        foreach (var burst in new[] { 0f, 0.1f, 0.3f, 1f })
        {
            var s = WithABlock();
            if (burst > 0)
            {
                for (var y = 27; y < 37; y++)
                {
                    for (var x = 27; x < 37; x++) s.AddExpansion(x, y, burst);
                }
            }
            s.Run(8, Inert);

            reaches.Add(Reach(s, 32, 32));
            peaks.Add(s.PeakDensity());
            output.WriteLine($"burst {burst}: reach {reaches[^1]:F1} cells, peak {peaks[^1]:F3}, " +
                             $"total {s.TotalDensity():F1}");
        }

        for (var i = 1; i < reaches.Count; i++)
        {
            Assert.True(reaches[i] > reaches[i - 1], "more expansion has to reach further");
            Assert.True(peaks[i] < peaks[i - 1], "more expansion has to thin it more");
        }
    }

    /// <summary>
    /// Expansion moves matter about; it does not make any. The conservation the
    /// flux transport promises has to survive a source in the pressure solve.
    /// </summary>
    [Fact]
    public void Expansion_Makes_No_Matter()
    {
        var s = WithABlock();
        var before = s.TotalDensity();
        for (var y = 27; y < 37; y++)
        {
            for (var x = 27; x < 37; x++) s.AddExpansion(x, y, 1f);
        }
        s.Run(12, Inert);

        output.WriteLine($"total density {before:F4} → {s.TotalDensity():F4}");
        Assert.Equal(before, s.TotalDensity(), 3);
    }

    /// <summary>
    /// An impulse, not a standing source: one projection consumes it, so a
    /// second run with nothing added expands no further.
    /// </summary>
    /// <remarks>
    /// The failure this prevents is a fireball that keeps inflating for the rest
    /// of the shot, which is what a source left in the field would do.
    /// </remarks>
    [Fact]
    public void Expansion_Is_Spent_By_The_Step_That_Uses_It()
    {
        var s = WithABlock();
        for (var y = 27; y < 37; y++)
        {
            for (var x = 27; x < 37; x++) s.AddExpansion(x, y, 1f);
        }
        s.Run(1, Inert);
        var afterTheImpulse = s.MeanAbsDivergence();

        s.Run(1, Inert);
        var afterTheNextStep = s.MeanAbsDivergence();

        output.WriteLine($"divergence {afterTheImpulse:E2} → {afterTheNextStep:E2}");
        Assert.True(afterTheNextStep < afterTheImpulse * 0.5,
            "the source outlived the projection that was meant to consume it");
    }

    /// <summary>Expansion is deterministic like everything else here.</summary>
    [Fact]
    public void Two_Expanding_Runs_Are_Bit_Identical()
    {
        static float[] Once()
        {
            var s = WithABlock();
            for (var y = 27; y < 37; y++)
            {
                for (var x = 27; x < 37; x++) s.AddExpansion(x, y, 0.6f);
            }
            s.Run(10, Typical);

            var f = new float[s.Width * s.Height];
            for (var y = 0; y < s.Height; y++)
            {
                for (var x = 0; x < s.Width; x++) f[y * s.Width + x] = s.DensityAt(x, y);
            }
            return f;
        }

        Assert.Equal(Once(), Once());
    }
}

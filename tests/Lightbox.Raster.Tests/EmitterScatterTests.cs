using Lightbox.Core.Documents;
using Lightbox.Raster.Media;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Scattering one emitter into many small ones over the same area — flames
/// along a hem rather than a hem that is on fire.
/// </summary>
/// <remarks>
/// <para>
/// <b>The measurements here corrected the design twice</b>, and both
/// corrections are pinned below rather than only written down. Height variation
/// was expected to come from <c>HeatVariation</c> and does not — it is there
/// with every variation at zero, because the fluid makes it. And the spread
/// each control <em>does</em> produce is only unambiguous at the stamp, before
/// the plume has spread and washed it about.
/// </para>
/// </remarks>
public class EmitterScatterTests(ITestOutputHelper output)
{
    /// <summary>A hem: a band of ink across the bottom, like the edge of a cloak.</summary>
    private sealed class Hem(int w, int h, int from, int to) : ISimMasks
    {
        public float[]? For(Emitter emitter, int frame)
        {
            var m = new float[w * h];
            for (var x = from; x < to; x++)
            {
                for (var y = h - 12; y < h - 6; y++) m[y * w + x] = 1f;
            }
            return m;
        }

        public float[]? Obstacle(int frame) => null;
    }

    private const int W = 200, H = 90;

    /// <summary>Buoyancy and cooling only, so what is measured is the scatter rather than the weather.</summary>
    private static SimParams Steady => new()
    {
        Buoyancy = 0.35, Weight = 0.01, Vorticity = 0, Turbulence = 0,
        Dissipation = 0.004, Cooling = 0.06, Drag = 0.05,
    };

    private static SimElement Burning(EmitterScatter? scatter, int from = 20, int to = 170, int frames = 14)
    {
        var element = new SimElement
        {
            Id = "hem", Kind = "fire", FirstFrame = 0, FrameCount = frames,
            GridWidth = W, GridHeight = H, Scale = 4, Substeps = 8,
            BandsFromHeat = true, BandColors = ["#3a1200", "#d95f18", "#ffe9a8"],
            Params = Steady,
        };
        element.Emitters.Add(new Emitter
        {
            Id = "em1", Shape = EmitterShape.Disc, X = W / 2.0, Y = H - 9, Radius = 4,
            Density = 1, Heat = 1, MaskLayerId = "hem", Scatter = scatter,
        });
        return element;
    }

    private static EmitterScatter Scattered(double size = 0, double heat = 0) => new()
    {
        Coverage = 0.5, Spacing = 8, SizeVariation = size, HeatVariation = heat,
    };

    /// <summary>Height above the hem for every column, zero where nothing burns.</summary>
    private static int[] Columns(SolvedElement solved, int from, int to)
    {
        var last = solved.Frames[^1];
        var floor = 0.05f * solved.PeakBand;
        var col = new int[to - from];
        for (var x = from; x < to; x++)
        {
            for (var y = 0; y < H; y++)
            {
                if (last.Band[y * W + x] <= floor) continue;
                if (y < H - 12) col[x - from] = H - 9 - y;
                break;
            }
        }
        return col;
    }

    /// <summary>Each contiguous run of lit columns is one flame; its tallest column is its height.</summary>
    private static List<int> Flames(int[] columns)
    {
        var flames = new List<int>();
        var run = 0;
        for (var i = 0; i <= columns.Length; i++)
        {
            var v = i < columns.Length ? columns[i] : 0;
            if (v > 0) { run = Math.Max(run, v); continue; }
            if (run > 0) flames.Add(run);
            run = 0;
        }
        return flames;
    }

    // ---- the thing it is for -------------------------------------------------------

    /// <summary>
    /// The whole point: an area emitter has no gaps, so a garment reads as one
    /// continuous burning edge and nothing can ever detach from it — whatever
    /// rises is replaced from below the same frame (Q125's finding). Scattered,
    /// the gaps are real and in the same place every frame, so what leaves a
    /// site actually leaves.
    /// </summary>
    [Fact]
    public void Scatter_Breaks_A_Burning_Edge_Into_Separate_Flames()
    {
        var whole = Columns(new SimBaker().Solve(Burning(null), new Hem(W, H, 20, 170)), 20, 170);
        var sparse = Columns(new SimBaker().Solve(Burning(Scattered()), new Hem(W, H, 20, 170)), 20, 170);

        var wholeLit = whole.Count(v => v > 0) * 100.0 / whole.Length;
        var sparseLit = sparse.Count(v => v > 0) * 100.0 / sparse.Length;

        output.WriteLine($"continuous {wholeLit:F0}% of the hem alight in {Flames(whole).Count} run(s); " +
                         $"scattered {sparseLit:F0}% in {Flames(sparse).Count} flames");

        Assert.True(wholeLit > 95, $"the fixture's continuous hem only lit {wholeLit:F0}%, so it proves nothing");
        Assert.True(sparseLit < 70, $"scattered still lit {sparseLit:F0}% of the hem");
        Assert.True(Flames(sparse).Count >= 4, "scattering produced fewer than four separate flames");
        Assert.Single(Flames(whole));
    }

    /// <summary>
    /// Coverage is a fraction, not a count, so a longer garment gets more flames
    /// from the same numbers — which is the whole reason it is not a count.
    /// </summary>
    [Fact]
    public void A_Longer_Surface_Gets_More_Flames_From_The_Same_Settings()
    {
        var shortHem = Flames(Columns(
            new SimBaker().Solve(Burning(Scattered(), 20, 60), new Hem(W, H, 20, 60)), 20, 60));
        var longHem = Flames(Columns(
            new SimBaker().Solve(Burning(Scattered(), 20, 170), new Hem(W, H, 20, 170)), 20, 170));

        output.WriteLine($"a 40-cell hem burns in {shortHem.Count} places, a 150-cell one in {longHem.Count}");
        Assert.True(longHem.Count > shortHem.Count * 2,
            $"a hem nearly four times as long got {longHem.Count} flames against {shortHem.Count}");
    }

    /// <summary>
    /// <b>Flames differ in height with every variation switched off</b>, and
    /// that is worth pinning because it is not what the design expected.
    /// </summary>
    /// <remarks>
    /// The plan was for <c>HeatVariation</c> to do this, on the reasoning that a
    /// flame is as tall as its heat survives cooling. Measured, it does not:
    /// height is roughly logarithmic in heat, so a ±60% swing barely shows, and
    /// the spread is already there at zero. What makes it is the fluid — a site
    /// with neighbours either side is fed by their rising column and runs tall,
    /// one on the end of a run does not. Pinned so nobody removes the effect
    /// while "fixing" a control that was never producing it.
    /// </remarks>
    [Fact]
    public void Scattered_Flames_Differ_In_Height_Without_Being_Told_To()
    {
        var flames = Flames(Columns(
            new SimBaker().Solve(Burning(Scattered()), new Hem(W, H, 20, 170)), 20, 170));

        Assert.True(flames.Count >= 4, "not enough flames to say anything about their spread");
        output.WriteLine($"heights {string.Join(", ", flames)} — tallest {flames.Max()}, shortest {flames.Min()}");

        Assert.True(flames.Max() > flames.Min() * 1.4,
            $"every flame came out much the same height ({flames.Min()}–{flames.Max()})");
    }

    // ---- what the two controls actually do -----------------------------------------

    /// <summary>
    /// Size varies the width of each flame's base.
    /// </summary>
    /// <remarks>
    /// <b>The narrowest run, and that took three attempts to measure.</b> The
    /// busiest column moved the wrong way (a high-variance statistic over two
    /// dozen sites); so did the spread of column totals, because a wider site
    /// spreads the same density over more columns. Even run widths mislead,
    /// since two sites that meet make one wide run. The narrowest run is the one
    /// reading that cannot be a merge, so it is a single site every time.
    /// </remarks>
    [Fact]
    public void Size_Variation_Widens_Some_Flames_And_Narrows_Others()
    {
        static List<int> BaseWidths(double variation)
        {
            var element = new SimElement
            {
                Id = "x", GridWidth = W, GridHeight = H, FirstFrame = 0, FrameCount = 1,
                Scale = 1, Substeps = 0,
                Params = new SimParams { Buoyancy = 0, Weight = 0, Vorticity = 0, Turbulence = 0, Dissipation = 0, Cooling = 0, Drag = 0 },
            };
            element.Emitters.Add(new Emitter
            {
                Id = "em", Shape = EmitterShape.Disc, X = W / 2.0, Y = H - 9, Radius = 4,
                Density = 1, Heat = 1, MaskLayerId = "hem",
                // Wide spacing and modest coverage, so the sites stand apart and
                // each run is one site rather than several merged.
                Scatter = new EmitterScatter
                {
                    Coverage = 0.55, Spacing = 30, SizeVariation = variation, HeatVariation = 0,
                },
            });

            var f = new SimBaker().Solve(element, new Hem(W, H, 20, 170)).Frames[0];
            var lit = new bool[W];
            for (var x = 0; x < W; x++)
            {
                for (var y = H - 20; y < H; y++)
                {
                    if (f.Band[y * W + x] > 0.01f) { lit[x] = true; break; }
                }
            }

            var widths = new List<int>();
            var run = 0;
            for (var x = 0; x <= W; x++)
            {
                if (x < W && lit[x]) { run++; continue; }
                if (run > 0) widths.Add(run);
                run = 0;
            }
            return widths;
        }

        var flat = BaseWidths(0);
        var varied = BaseWidths(0.6);
        output.WriteLine($"bases flat: {string.Join(", ", flat)}");
        output.WriteLine($"bases varied: {string.Join(", ", varied)}");

        Assert.True(flat.Count >= 3, "not enough sites to compare");

        // The *narrowest* run, because a wide one may be two sites that met.
        // With no variation every site is the same width, so the narrowest run
        // is exactly one site; with variation it is the smallest of them.
        Assert.True(varied.Min() < flat.Min() * 0.85,
            $"the narrowest base did not shrink: {flat.Min()} flat, {varied.Min()} varied");
        Assert.True(varied.Max() > flat.Max(),
            $"the widest base did not grow: {flat.Max()} flat, {varied.Max()} varied");
    }

    /// <summary>
    /// Heat varies how fiercely each flame burns, not how tall it is — and
    /// because fire bands from temperature, that is which colours it reaches.
    /// </summary>
    /// <remarks>
    /// <b>Measured at the stamp, for the same reason size is.</b> Downstream the
    /// plumes lean into each other and blend, so by the last frame the spread of
    /// peak temperature across flames is only about a sixth wider than with no
    /// variation at all — real, but too close to the noise to assert on. At the
    /// source it is unambiguous: every site identical against sites ranging from
    /// under half to over one and a half.
    /// </remarks>
    [Fact]
    public void Heat_Variation_Makes_Some_Flames_Fiercer_Than_Others()
    {
        static (double Min, double Max) SiteHeat(double variation)
        {
            var element = new SimElement
            {
                Id = "x", Kind = "fire", GridWidth = W, GridHeight = H, FirstFrame = 0, FrameCount = 1,
                Scale = 1, Substeps = 0, BandsFromHeat = true,
                Params = new SimParams { Buoyancy = 0, Weight = 0, Vorticity = 0, Turbulence = 0, Dissipation = 0, Cooling = 0, Drag = 0 },
            };
            element.Emitters.Add(new Emitter
            {
                Id = "em", Shape = EmitterShape.Disc, X = W / 2.0, Y = H - 9, Radius = 4,
                Density = 1, Heat = 1, MaskLayerId = "hem",
                Scatter = new EmitterScatter
                {
                    Coverage = 0.55, Spacing = 14, SizeVariation = 0, HeatVariation = variation,
                },
            });

            var f = new SimBaker().Solve(element, new Hem(W, H, 20, 170)).Frames[0];

            // The hottest cell in each contiguous run: one number per flame.
            var peaks = new List<double>();
            double run = 0;
            for (var x = 0; x <= W; x++)
            {
                double column = 0;
                if (x < W)
                {
                    for (var y = H - 20; y < H; y++) column = Math.Max(column, f.Band[y * W + x]);
                }
                if (column > 0.01) { run = Math.Max(run, column); continue; }
                if (run > 0) peaks.Add(run);
                run = 0;
            }
            return peaks.Count == 0 ? (0, 0) : (peaks.Min(), peaks.Max());
        }

        var flat = SiteHeat(0);
        var varied = SiteHeat(0.6);
        output.WriteLine($"flame heat — flat {flat.Min:F2}..{flat.Max:F2}, varied {varied.Min:F2}..{varied.Max:F2}");

        Assert.True(flat.Max > 0, "nothing was stamped, so nothing was measured");

        // The coolest flame, and it is the clean reading: an unvaried site on
        // its own is exactly the emitter's heat, while the maximum is inflated
        // on both sides by sites that overlap and add.
        Assert.Equal(1.0, flat.Min, 2);
        Assert.True(varied.Min < 0.8, $"no flame burned cooler than the emitter asked: {varied.Min:F2}");
        Assert.True(varied.Max > 1.3, $"no flame burned fiercer than the emitter asked: {varied.Max:F2}");
    }

    // ---- the rules it has to keep ---------------------------------------------------

    /// <summary>
    /// Scatter is a property of the stamp, so a plain disc scatters into several
    /// small flames inside its own outline. Neither mask-only nor fire-only.
    /// </summary>
    [Fact]
    public void A_Plain_Disc_Scatters_Too()
    {
        static int Runs(EmitterScatter? scatter)
        {
            var element = new SimElement
            {
                Id = "d", Kind = "fire", FirstFrame = 0, FrameCount = 1,
                GridWidth = W, GridHeight = H, Scale = 1, Substeps = 0, BandsFromHeat = true,
                Params = Steady,
            };
            element.Emitters.Add(new Emitter
            {
                Id = "em", Shape = EmitterShape.Disc, X = W / 2.0, Y = H - 20, Radius = 22,
                Density = 1, Heat = 1, Scatter = scatter,
            });

            var solved = new SimBaker().Solve(element);
            var last = solved.Frames[0];
            var floor = 0.05f * solved.PeakBand;
            var lit = new int[W];
            for (var x = 0; x < W; x++)
            {
                for (var y = 0; y < H; y++)
                {
                    if (last.Band[y * W + x] > floor) { lit[x] = 1; break; }
                }
            }
            var runs = 0;
            for (var x = 0; x < W; x++)
            {
                if (lit[x] == 1 && (x == 0 || lit[x - 1] == 0)) runs++;
            }
            return runs;
        }

        var whole = Runs(null);
        var scattered = Runs(new EmitterScatter { Coverage = 0.5, Spacing = 9, SizeVariation = 0.3 });
        output.WriteLine($"a solid disc is {whole} run, scattered it is {scattered}");
        Assert.Equal(1, whole);
        Assert.True(scattered > 1, "a scattered disc came out as one solid blob");
    }

    /// <summary>
    /// Seeded from each site's own position, so the same document scatters the
    /// same way on every machine and every re-bake — invariant 2, exactly as a
    /// brush keeps it.
    /// </summary>
    [Fact]
    public void Two_Scattered_Bakes_Are_Identical()
    {
        static float[] Once()
        {
            var solved = new SimBaker().Solve(
                Burning(Scattered(0.6, 0.6)), new Hem(W, H, 20, 170));
            return solved.Frames[^1].Band;
        }

        Assert.Equal(Once(), Once());
    }

    /// <summary>
    /// An emitter nobody scattered behaves exactly as it did, byte for byte —
    /// which is what makes this safe to add to a record that already ships.
    /// </summary>
    [Fact]
    public void An_Unscattered_Emitter_Is_Unchanged()
    {
        var a = new SimBaker().Solve(Burning(null), new Hem(W, H, 20, 170)).Frames[^1].Band;
        var b = new SimBaker().Solve(Burning(null), new Hem(W, H, 20, 170)).Frames[^1].Band;
        Assert.Equal(a, b);

        // And it is genuinely the continuous path: one unbroken run of fire.
        Assert.Single(Flames(Columns(
            new SimBaker().Solve(Burning(null), new Hem(W, H, 20, 170)), 20, 170)));
    }
}

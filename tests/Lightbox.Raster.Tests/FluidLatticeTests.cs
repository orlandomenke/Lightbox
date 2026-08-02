using System.Diagnostics;
using Lightbox.Raster.Media;

namespace Lightbox.Raster.Tests;

[Collection("Performance")]
public class FluidLatticeTests
{
    private static readonly FluidParams Typical = new(
        Viscosity: 0.3f, Drag: 0.4f, Absorbency: 0.5f, EdgePull: 0.4f, Granularity: 0.3f);

    /// <summary>A filled disc of water and pigment — one wet mark on dry paper.</summary>
    private static FluidLattice Disc(int size, int radius, float water, float pigment)
    {
        var lat = new FluidLattice(size, size);
        var c = size / 2;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - c;
                var dy = y - c;
                if (dx * dx + dy * dy <= radius * radius) lat.Seed(x, y, water, pigment, 0.2f, 0.4f, 0.8f);
            }
        }
        return lat;
    }

    private static float[] DepositOf(FluidLattice lat)
    {
        var buf = new float[lat.Width * lat.Height * 4];
        lat.ReadDeposit(buf);
        return buf;
    }

    // ---- conservation -------------------------------------------------------

    [Fact]
    public void Pigment_IsConserved_AcrossEveryChannel()
    {
        var lat = Disc(48, 12, water: 1.0f, pigment: 0.7f);
        // A few off-centre blobs so the flow is asymmetric and any index slip
        // in the transport stencil has somewhere to leak to.
        lat.Seed(4, 4, 3f, 2f, 1f, 0f, 0f);
        lat.Seed(43, 9, 2f, 1.5f, 0f, 1f, 0.5f);
        lat.Seed(9, 41, 2.5f, 1f, 0.3f, 0.3f, 1f);

        var before = new double[4];
        for (var c = 0; c < 4; c++) before[c] = lat.TotalPigment(c);
        Assert.True(before[0] > 100, $"test would be vacuous: only {before[0]} pigment seeded");

        for (var i = 0; i < 10; i++)
        {
            lat.Run(5, Typical);
            for (var c = 0; c < 4; c++)
            {
                var now = lat.TotalPigment(c);
                Assert.True(now <= before[c] * (1 + 1e-4),
                    $"channel {c} grew from {before[c]} to {now} after {(i + 1) * 5} steps");
                Assert.True(now >= before[c] * (1 - 1e-4),
                    $"channel {c} shrank from {before[c]} to {now} after {(i + 1) * 5} steps");
            }
        }
    }

    [Fact]
    public void Conservation_HoldsAtEveryParameterCorner()
    {
        // The corners of the parameter cube are where a term that is normally
        // small takes over, so they are where a non-conservative term shows up.
        foreach (var visc in new[] { 0f, 1f })
        foreach (var drag in new[] { 0f, 1f })
        foreach (var absorb in new[] { 0f, 1f })
        foreach (var edge in new[] { 0f, 1f })
        foreach (var gran in new[] { 0f, 1f })
        {
            var lat = Disc(32, 8, water: 2f, pigment: 1f);
            var before = lat.TotalPigment();
            lat.Run(15, new FluidParams(visc, drag, absorb, edge, gran));
            var after = lat.TotalPigment();
            Assert.True(Math.Abs(after - before) <= before * 1e-4,
                $"visc={visc} drag={drag} absorb={absorb} edge={edge} gran={gran}: {before} -> {after}");
        }
    }

    [Fact]
    public void Deposit_NeverExceedsPigmentThatWasSeeded()
    {
        var lat = Disc(40, 10, water: 1.5f, pigment: 0.9f);
        var seeded = lat.TotalPigment();
        lat.Run(30, Typical);

        var rgba = DepositOf(lat);
        double alpha = 0;
        for (var i = 0; i < lat.Width * lat.Height; i++)
        {
            var o = i * 4;
            Assert.True(rgba[o + 3] >= 0, "negative deposited pigment");
            // Premultiplied: no channel may outrun its own alpha, or the colour
            // the cell reports is not a colour it was ever given.
            Assert.True(rgba[o] <= rgba[o + 3] + 1e-5f, "red exceeds alpha");
            Assert.True(rgba[o + 1] <= rgba[o + 3] + 1e-5f, "green exceeds alpha");
            Assert.True(rgba[o + 2] <= rgba[o + 3] + 1e-5f, "blue exceeds alpha");
            alpha += rgba[o + 3];
        }

        Assert.True(alpha > seeded * 0.5, $"only {alpha} of {seeded} pigment settled in 30 steps");
        Assert.True(alpha <= seeded * (1 + 1e-4), $"{alpha} deposited from {seeded} seeded");
    }

    // ---- no-op and determinism ---------------------------------------------

    [Fact]
    public void RunZero_ChangesNothingAtAll()
    {
        var lat = Disc(24, 7, water: 1f, pigment: 0.5f);
        lat.Run(3, Typical);

        var depositBefore = DepositOf(lat);
        var waterBefore = new float[24 * 24];
        for (var y = 0; y < 24; y++)
        for (var x = 0; x < 24; x++)
            waterBefore[y * 24 + x] = lat.WaterAt(x, y);

        lat.Run(0, Typical);
        lat.Run(-7, Typical);

        var depositAfter = DepositOf(lat);
        for (var i = 0; i < depositBefore.Length; i++)
        {
            Assert.Equal(BitConverter.SingleToInt32Bits(depositBefore[i]),
                         BitConverter.SingleToInt32Bits(depositAfter[i]));
        }

        for (var y = 0; y < 24; y++)
        for (var x = 0; x < 24; x++)
        {
            Assert.Equal(BitConverter.SingleToInt32Bits(waterBefore[y * 24 + x]),
                         BitConverter.SingleToInt32Bits(lat.WaterAt(x, y)));
        }
    }

    [Fact]
    public void TwoRuns_AreBitIdentical()
    {
        var paper = PaperField(36, 36);

        static FluidLattice Build(float[] paper)
        {
            var lat = Disc(36, 11, water: 1.3f, pigment: 0.8f);
            lat.SetPaper(paper, 0.7);
            lat.Seed(5, 30, 2f, 1f, 1f, 0.5f, 0f);
            return lat;
        }

        var a = Build(paper);
        var b = Build(paper);

        // Split into two calls on one side and one on the other: the step loop
        // must carry no hidden per-call state either.
        a.Run(9, Typical);
        b.Run(4, Typical);
        b.Run(5, Typical);

        var da = DepositOf(a);
        var db = DepositOf(b);
        for (var i = 0; i < da.Length; i++)
        {
            Assert.Equal(BitConverter.SingleToInt32Bits(da[i]), BitConverter.SingleToInt32Bits(db[i]));
        }

        for (var y = 0; y < 36; y++)
        for (var x = 0; x < 36; x++)
        {
            Assert.Equal(BitConverter.SingleToInt32Bits(a.WaterAt(x, y)),
                         BitConverter.SingleToInt32Bits(b.WaterAt(x, y)));
        }

        // And it has to have actually done something, or bit-equality is free.
        Assert.True(a.TotalPigment() > 0);
        Assert.Contains(da, f => f > 0.01f);
    }

    // ---- stability ----------------------------------------------------------

    [Fact]
    public void InviscidUndraggedDeluge_StaysFinite()
    {
        // Viscosity 0, drag 0, absurd water: nothing damps the velocity field,
        // so if any term can run away this is where it does.
        var lat = new FluidLattice(40, 40);
        for (var y = 14; y < 26; y++)
        for (var x = 14; x < 26; x++)
            lat.Seed(x, y, 500f, 5f, 1f, 1f, 1f);

        var seeded = lat.TotalPigment();
        lat.Run(300, new FluidParams(Viscosity: 0f, Drag: 0f, Absorbency: 0f, EdgePull: 1f, Granularity: 1f));

        var rgba = DepositOf(lat);
        foreach (var f in rgba)
        {
            Assert.True(float.IsFinite(f), "deposit went non-finite");
            Assert.InRange(f, 0f, (float)seeded);
        }

        double water = 0;
        for (var y = 0; y < 40; y++)
        for (var x = 0; x < 40; x++)
        {
            var w = lat.WaterAt(x, y);
            Assert.True(float.IsFinite(w), "water went non-finite");
            Assert.True(w >= 0, $"negative water depth {w}");
            water += w;
        }

        // Absorbency 0 means nothing dries, so the water must still all be here.
        Assert.Equal(500.0 * 144, water, 1);
        Assert.True(Math.Abs(lat.TotalPigment() - seeded) <= seeded * 1e-4);
    }

    [Fact]
    public void ExtremeParameters_DoNotProduceNaN()
    {
        foreach (var p in new[]
        {
            new FluidParams(0f, 0f, 0f, 0f, 0f),
            new FluidParams(1f, 1f, 1f, 1f, 1f),
            new FluidParams(-5f, -5f, -5f, -5f, -5f),   // clamped, not honoured
            new FluidParams(50f, 50f, 50f, 50f, 50f),
            new FluidParams(float.NaN, float.NaN, float.NaN, float.NaN, float.NaN),
        })
        {
            var lat = Disc(28, 9, water: 20f, pigment: 3f);
            lat.Run(40, p);
            foreach (var f in DepositOf(lat)) Assert.True(float.IsFinite(f), $"non-finite deposit at {p}");
            for (var y = 0; y < 28; y++)
            for (var x = 0; x < 28; x++)
                Assert.True(float.IsFinite(lat.WaterAt(x, y)), $"non-finite water at {p}");
        }
    }

    // ---- the two effects that have to emerge -------------------------------

    [Fact]
    public void EdgePull_ConcentratesDepositNearTheWetBoundary()
    {
        const int Size = 64;
        const int R = 12;

        static double RimOverCore(float edge)
        {
            var lat = Disc(Size, R, water: 1.0f, pigment: 0.6f);
            lat.Run(20, new FluidParams(
                Viscosity: 0.2f, Drag: 0.3f, Absorbency: 0.4f, EdgePull: edge, Granularity: 0f));

            var rgba = DepositOf(lat);
            var c = Size / 2;
            double rim = 0, core = 0;
            int rimN = 0, coreN = 0;
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var dx = x - c;
                var dy = y - c;
                var d = Math.Sqrt(dx * dx + dy * dy);
                var a = rgba[(y * Size + x) * 4 + 3];
                if (d >= R - 4 && d <= R + 2) { rim += a; rimN++; }
                else if (d <= 4) { core += a; coreN++; }
            }
            return rim / rimN / (core / coreN);
        }

        var without = RimOverCore(0f);
        var with = RimOverCore(0.9f);

        Assert.True(without > 0, "no deposit at all — test is measuring nothing");

        // Left alone, the deposit is heaviest in the middle and thins outward:
        // the ratio must be below 1 or there was nothing for EdgePull to invert.
        Assert.True(without < 1,
            $"deposit already favoured the edge without EdgePull: rim/core {without:F3}");

        // With it, the edge has to be genuinely darker than the middle — an
        // actual rim, not just a slightly fatter fringe.
        Assert.True(with > 1.15,
            $"EdgePull produced no rim: rim/core {with:F3}");
        Assert.True(with > without * 1.5,
            $"EdgePull barely moved the deposit: rim/core {without:F3} -> {with:F3}");
    }

    [Fact]
    public void Granularity_BiasesDepositIntoThePapersValleys()
    {
        const int Size = 48;

        static double LowOverHigh(float gran)
        {
            // Left half is valley floor, right half is tooth.
            var paper = new float[Size * Size];
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                paper[y * Size + x] = x < Size / 2 ? 0f : 1f;

            var lat = new FluidLattice(Size, Size);
            lat.SetPaper(paper, 1.0);
            for (var y = 4; y < Size - 4; y++)
            for (var x = 4; x < Size - 4; x++)
                lat.Seed(x, y, 0.6f, 0.5f, 0.1f, 0.1f, 0.1f);

            lat.Run(10, new FluidParams(
                Viscosity: 0.5f, Drag: 0.5f, Absorbency: 0.5f, EdgePull: 0f, Granularity: gran));

            var rgba = DepositOf(lat);
            double low = 0, high = 0;
            // Skip the columns either side of the seam, where the paper step
            // itself drives flow and would confound the measurement.
            for (var y = 6; y < Size - 6; y++)
            for (var x = 6; x < Size - 6; x++)
            {
                if (Math.Abs(x - Size / 2) < 4) continue;
                var a = rgba[(y * Size + x) * 4 + 3];
                if (x < Size / 2) low += a; else high += a;
            }
            return low / high;
        }

        var flat = LowOverHigh(0f);
        var grainy = LowOverHigh(1f);

        Assert.True(Math.Abs(flat - 1.0) < 0.15, $"deposit was already lopsided at Granularity 0: {flat:F3}");
        Assert.True(grainy > flat * 1.3,
            $"Granularity failed to favour valleys: low/high {flat:F3} -> {grainy:F3}");
    }

    [Fact]
    public void PaperInfluenceZero_MakesThePaperIrrelevant()
    {
        // Invariant 4 in miniature: the paper field must reach pixels only
        // through the influence that was passed with it.
        var rough = PaperField(32, 32);
        var flat = new float[32 * 32];

        static float[] RunWith(float[] paper)
        {
            var lat = Disc(32, 10, water: 1f, pigment: 0.6f);
            lat.SetPaper(paper, 0.0);
            lat.Run(12, Typical);
            return DepositOf(lat);
        }

        var a = RunWith(rough);
        var b = RunWith(flat);
        for (var i = 0; i < a.Length; i++)
        {
            Assert.Equal(BitConverter.SingleToInt32Bits(a[i]), BitConverter.SingleToInt32Bits(b[i]));
        }
    }

    [Fact]
    public void Water_SpreadsBeyondWhereItWasSeeded()
    {
        // Without this the lattice could pass conservation while transporting
        // nothing at all.
        var lat = new FluidLattice(32, 32);
        lat.Seed(16, 16, 40f, 4f, 0.5f, 0.5f, 0.5f);
        Assert.Equal(0f, lat.WaterAt(20, 16));

        lat.Run(25, new FluidParams(0.1f, 0.1f, 0.1f, 0.2f, 0.2f));

        Assert.True(lat.WaterAt(20, 16) > 0, "water never left the seeded cell");
        Assert.True(lat.DepositAt(19, 16) > 0, "pigment never travelled with the water");
        Assert.True(lat.WaterAt(16, 16) < 40f, "the seeded cell kept all its water");
    }

    [Fact]
    public void ThinWash_PinsInsteadOfCreepingForever()
    {
        // The capillary entry head is what stops a wash spreading, and the rim
        // depends on the front holding still. A front that creeps would also
        // make cost unbounded in practice: every step would wet more cells.
        var lat = Disc(64, 10, water: 0.4f, pigment: 0.4f);
        var calm = new FluidParams(0.3f, 0.3f, 0.3f, 0f, 0f);

        lat.Run(30, calm);
        var reachAt30 = WetRadius(lat);

        lat.Run(120, calm);
        var reachAt150 = WetRadius(lat);

        Assert.True(reachAt30 > 10, $"the wash never spread at all: reached {reachAt30}");
        Assert.True(reachAt150 <= reachAt30 + 1,
            $"the wet front kept creeping: {reachAt30} -> {reachAt150} over 120 further steps");

        static double WetRadius(FluidLattice lat)
        {
            var c = lat.Width / 2;
            double max = 0;
            for (var y = 0; y < lat.Height; y++)
            for (var x = 0; x < lat.Width; x++)
            {
                if (lat.WaterAt(x, y) <= 1e-4f) continue;
                var dx = x - c;
                var dy = y - c;
                max = Math.Max(max, Math.Sqrt(dx * dx + dy * dy));
            }
            return max;
        }
    }

    [Fact]
    public void MisSizedBuffers_AreRejected()
    {
        var lat = new FluidLattice(8, 4);
        Assert.Throws<ArgumentException>(() => lat.SetPaper(new float[31], 1.0));
        Assert.Throws<ArgumentException>(() => lat.ReadDeposit(new float[8 * 4 * 3]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FluidLattice(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => lat.TotalPigment(4));

        // Out-of-range seeds are dropped, not thrown at: a caller stamping a
        // dab near the edge should not have to clip it.
        var before = lat.TotalPigment();
        lat.Seed(-1, 2, 1f, 1f, 1f, 1f, 1f);
        lat.Seed(8, 2, 1f, 1f, 1f, 1f, 1f);
        lat.Seed(3, 4, 1f, 1f, 1f, 1f, 1f);
        Assert.Equal(before, lat.TotalPigment());
    }

    // ---- cost ---------------------------------------------------------------

    [Fact]
    [Trait("Category", "Performance")]
    public void FourHundredSquare_TwelveSteps_StaysWithinBudget()
    {
        // A fresh lattice for each run: Run mutates it, so timing the same one
        // twice would be timing two different simulations.
        FluidLattice? lat = null;
        var fastest = Bench.FastestMs(
            3,
            () => lat!.Run(12, Typical),
            before: () =>
            {
                lat = Disc(400, 150, water: 1.2f, pigment: 0.7f);
                lat.SetPaper(PaperField(400, 400), 0.5);
            });

        Assert.True(fastest < 2000, $"400x400 x 12 steps took {fastest:0} ms (budget 2000 ms)");
    }

    /// <summary>A deterministic tooth pattern — no RNG anywhere, including in tests.</summary>
    private static float[] PaperField(int w, int h)
    {
        var f = new float[w * h];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            f[y * w + x] = (float)(0.5 + 0.5 * Math.Sin(x * 0.7) * Math.Cos(y * 0.53));
        return f;
    }
}

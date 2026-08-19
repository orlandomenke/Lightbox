using Lightbox.Core.Documents;
using Lightbox.Core.Effects;
using Lightbox.Core.Inbetween;
using Lightbox.Raster.Media;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Emission painted onto a layer (Q125): the mask is the emission, whole, and
/// the emitter's origin is the only thing that moves it.
/// </summary>
[Collection("Performance")]
public class MaskEmitterTests(ITestOutputHelper output)
{
    /// <summary>A mask handed straight to the baker, with no document in the way.</summary>
    private sealed class FixedMask(float[] mask) : ISimMasks
    {
        public int Asked { get; private set; }

        public float[]? For(Emitter emitter, int frame)
        {
            Asked++;
            return mask;
        }
    }

    private const int W = 48;
    private const int H = 48;

    /// <summary>Ink in a band, so "where it emits" is somewhere specific.</summary>
    private static float[] Patch(int fromX, int toX, int fromY, int toY)
    {
        var m = new float[W * H];
        for (var y = fromY; y < toY; y++)
        {
            for (var x = fromX; x < toX; x++) m[y * W + x] = 1f;
        }
        return m;
    }

    private static SimElement Element(int frames = 8)
    {
        var e = new SimElement
        {
            Id = "e", Kind = "fire", FrameCount = frames,
            GridWidth = W, GridHeight = H, Scale = 4,
            Substeps = 4, BandsFromHeat = true,
        };
        e.Params.Buoyancy = 0;      // hold the plume still so placement is what is measured
        e.Params.Turbulence = 0;
        e.Params.Vorticity = 0;
        e.Params.Cooling = 0;
        return e;
    }

    private static Emitter Masked() => new()
    {
        Id = "em", MaskLayerId = "mask-layer", Density = 1, Heat = 1,
    };

    /// <summary>Sideways centre of mass of the heat, in cells.</summary>
    private static double CentreX(SolvedElement solved)
    {
        var f = solved.Frames[^1];
        double mx = 0, m = 0;
        for (var y = 0; y < solved.Height; y++)
        {
            for (var x = 0; x < solved.Width; x++)
            {
                var v = f.Band[y * solved.Width + x];
                mx += v * x;
                m += v;
            }
        }
        return m > 0 ? mx / m : -1;
    }

    // ---- the mask is the emission ---------------------------------------------

    [Fact]
    public void A_Masked_Emitter_Emits_Where_The_Mask_Has_Ink()
    {
        var element = Element();
        element.Emitters.Add(Masked());

        var solved = new SimBaker().Solve(element, new FixedMask(Patch(8, 16, 20, 28)));

        var centre = CentreX(solved);
        output.WriteLine($"heat sits at x {centre:F1}, mask spans 8..16");
        Assert.InRange(centre, 8, 16);
        Assert.True(solved.PeakBand > 0, "nothing was emitted at all");
    }

    [Fact]
    public void A_Masked_Emitter_Ignores_Its_Shape_Fields()
    {
        var element = Element();
        var emitter = Masked();
        emitter.Shape = EmitterShape.Disc;
        emitter.X = 40;               // nowhere near the mask
        emitter.Y = 40;
        emitter.Radius = 6;
        element.Emitters.Add(emitter);

        var centre = CentreX(new SimBaker().Solve(element, new FixedMask(Patch(6, 14, 20, 28))));

        output.WriteLine($"heat sits at x {centre:F1}; the disc would have put it at 40");
        Assert.InRange(centre, 6, 15);
    }

    /// <summary>
    /// Losing a layer should cost the effect, not the document.
    /// </summary>
    [Fact]
    public void A_Mask_That_Is_Gone_Leaves_Its_Emitter_Silent()
    {
        var element = Element();
        element.Emitters.Add(Masked());

        var solved = new SimBaker().Solve(element, masks: null);

        Assert.Equal(0, solved.PeakBand);
        Assert.All(solved.Frames, f => Assert.All(f.Band, v => Assert.Equal(0, v)));
    }

    [Fact]
    public void A_Shape_Emitter_Never_Asks_For_A_Mask()
    {
        var element = Element();
        element.Emitters.Add(new Emitter { Shape = EmitterShape.Disc, X = 24, Y = 24, Radius = 4 });

        var masks = new FixedMask(Patch(4, 8, 4, 8));
        var solved = new SimBaker().Solve(element, masks);

        Assert.Equal(0, masks.Asked);
        Assert.True(solved.PeakBand > 0, "the disc should still have emitted");
    }

    // ---- the origin is the only thing that moves it ------------------------------

    /// <summary>
    /// With nothing keyed the mask emits exactly where it was painted — the
    /// identity case, and the one an artist meets first.
    /// </summary>
    [Fact]
    public void An_Emitter_That_Has_Not_Moved_Emits_Where_The_Mask_Was_Painted()
    {
        var still = Element();
        still.Emitters.Add(Masked());

        var offsetByZero = Element();
        var emitter = Masked();
        emitter.MotionX = new EffectParam(0);
        offsetByZero.Emitters.Add(emitter);

        var mask = Patch(10, 18, 20, 28);
        Assert.Equal(
            new SimBaker().Solve(still, new FixedMask(mask)).Frames[^1].Band,
            new SimBaker().Solve(offsetByZero, new FixedMask(mask)).Frames[^1].Band);
    }

    [Fact]
    public void Keying_The_Origin_Slides_The_Emission()
    {
        static double Where(double shift)
        {
            var element = Element();
            var emitter = Masked();
            emitter.MotionX = new EffectParam(shift);
            element.Emitters.Add(emitter);
            return CentreX(new SimBaker().Solve(element, new FixedMask(Patch(8, 16, 20, 28))));
        }

        var put = Where(0);
        var moved = Where(16);

        output.WriteLine($"emission centre {put:F1} → {moved:F1} after a 16-cell offset");
        Assert.True(moved > put + 12, $"the mask did not travel: {put:F1} → {moved:F1}");
    }

    /// <summary>
    /// A travelling emitter lays a trail — smoke behind flying debris, which is
    /// the case this exists for as much as flames on a garment.
    /// </summary>
    [Fact]
    public void A_Travelling_Emitter_Leaves_A_Trail_Behind_It()
    {
        var element = Element(frames: 20);
        var emitter = Masked();
        emitter.MotionX = new EffectParam
        {
            Keys =
            [
                new EffectKey { Frame = 0, Value = 0, Ease = Easing.Linear },
                new EffectKey { Frame = 19, Value = 26, Ease = Easing.Linear },
            ],
        };
        element.Emitters.Add(emitter);

        var solved = new SimBaker().Solve(element, new FixedMask(Patch(6, 10, 22, 26)));
        var last = solved.Frames[^1];

        // Heat should be spread along the path travelled, not piled at one end.
        var lit = new List<int>();
        for (var x = 0; x < solved.Width; x++)
        {
            for (var y = 0; y < solved.Height; y++)
            {
                if (last.Band[y * solved.Width + x] > solved.PeakBand * 0.05) { lit.Add(x); break; }
            }
        }

        output.WriteLine($"trail spans x {lit.Min()}..{lit.Max()} over a 26-cell journey");
        Assert.True(lit.Max() - lit.Min() > 18, $"no trail: the heat only spans {lit.Max() - lit.Min()} cells");
    }

    // ---- coverage is a weight, not a switch ---------------------------------------

    [Fact]
    public void Softly_Painted_Ink_Emits_Less_Than_Solid_Ink()
    {
        static double Total(float ink)
        {
            var element = Element();
            element.Emitters.Add(Masked());
            var mask = new float[W * H];
            for (var y = 20; y < 28; y++)
            {
                for (var x = 8; x < 16; x++) mask[y * W + x] = ink;
            }
            return new SimBaker().Solve(element, new FixedMask(mask)).Frames[^1].Band.Sum();
        }

        var faint = Total(0.25f);
        var solid = Total(1f);

        output.WriteLine($"faint mask emits {faint:F2}, solid {solid:F2}");
        Assert.True(faint > 0, "a faintly painted mask should still emit something");
        Assert.True(solid > faint * 2.5, $"coverage is not weighting emission: {faint:F2} against {solid:F2}");
    }

    [Fact]
    public void Two_Bakes_Of_A_Masked_Element_Are_Identical()
    {
        var element = Element(frames: 12);
        var emitter = Masked();
        emitter.MotionX = new EffectParam { Keys = [new EffectKey { Frame = 0, Value = 0 }, new EffectKey { Frame = 11, Value = 9 }] };
        element.Emitters.Add(emitter);

        var mask = Patch(8, 16, 20, 28);
        Assert.Equal(
            new SimBaker().Solve(element, new FixedMask(mask)).Frames[^1].Band,
            new SimBaker().Solve(element, new FixedMask(mask)).Frames[^1].Band);
    }
}

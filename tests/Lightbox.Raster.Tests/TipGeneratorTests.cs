using Lightbox.Core.Documents;
using Lightbox.Raster.Tips;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The procedural half of the tip library. Tips are baked once and then only
/// looked up, so these assert the bake — what the engine sees afterwards is an
/// ordinary cached raster.
/// </summary>
public class TipGeneratorTests(ITestOutputHelper output)
{
    private static float[] Bake(TipShape shape, int size = 64, Action<TipRecipe>? tweak = null)
    {
        var recipe = new TipRecipe { Shape = shape, Size = size };
        tweak?.Invoke(recipe);
        return TipGenerator.Coverage(recipe, size);
    }

    private static float At(float[] a, int size, int x, int y) => a[y * size + x];

    [Fact]
    public void AGeneratedEdgeIsCoverageNotAStaircase()
    {
        // The rule for the whole generator. A binary `d <= Radius` stair-steps,
        // and on one drawing that is a curiosity — replayed across two hundred
        // frames the steps shift phase from mark to mark and the edge boils.
        // So the boundary has to carry intermediate values.
        const int Size = 64;
        var a = Bake(TipShape.HardCircle, Size);

        var partial = a.Count(v => v > 0.02f && v < 0.98f);
        output.WriteLine($"{partial} boundary pixels of {a.Length}");

        Assert.True(partial > 100, $"only {partial} pixels sit on the edge — that is a staircase");
        Assert.Equal(1f, At(a, Size, Size / 2, Size / 2), 1e-4f);
        Assert.Equal(0f, At(a, Size, 0, 0));
    }

    [Fact]
    public void AHardCircleIsRoundAndCentred()
    {
        // Sampled at pixel centres. Getting the half-pixel wrong biases every
        // tip half a pixel toward the top left, which is invisible on one stamp
        // and a drift on a scaled one — so the four cardinal points have to
        // agree with each other.
        const int Size = 64;
        var a = Bake(TipShape.HardCircle, Size);
        var c = Size / 2;

        var left = At(a, Size, 1, c);
        var right = At(a, Size, Size - 2, c);
        var top = At(a, Size, c, 1);
        var bottom = At(a, Size, c, Size - 2);

        Assert.Equal(left, right, 1e-4f);
        Assert.Equal(top, bottom, 1e-4f);
        Assert.Equal(left, top, 1e-4f);
    }

    [Fact]
    public void HardnessDecidesHowFarTheCoreReaches()
    {
        const int Size = 64;
        var soft = Bake(TipShape.SoftCircle, Size, r => r.Hardness = 0.1);
        var hard = Bake(TipShape.SoftCircle, Size, r => r.Hardness = 0.9);
        var c = Size / 2;

        // Halfway out: a hard brush is still solid there, a soft one is not.
        var q = Size / 4;
        Assert.True(At(hard, Size, c + q, c) > At(soft, Size, c + q, c) + 0.3f,
            $"hardness did nothing: soft {At(soft, Size, c + q, c):F2}, hard {At(hard, Size, c + q, c):F2}");
        Assert.Equal(1f, At(soft, Size, c, c), 1e-4f);
        Assert.Equal(1f, At(hard, Size, c, c), 1e-4f);
    }

    [Fact]
    public void ASoftTipFadesWithoutACrease()
    {
        // A straight ramp from the core to the rim leaves a visible kink where
        // the two meet, and an artist reads that kink as a second, smaller
        // brush inside the first. Smoothstep has no second derivative jump, so
        // the profile's slope changes gradually rather than in one step.
        const int Size = 128;
        var a = Bake(TipShape.SoftCircle, Size, r => r.Hardness = 0.4);
        var c = Size / 2;

        var slopes = new List<float>();
        for (var x = c + 2; x < Size - 3; x++)
        {
            slopes.Add(At(a, Size, x + 1, c) - At(a, Size, x, c));
        }

        var jumps = 0;
        for (var i = 1; i < slopes.Count; i++)
        {
            if (Math.Abs(slopes[i] - slopes[i - 1]) > 0.05f) jumps++;
        }

        Assert.True(jumps <= 1, $"the falloff has {jumps} kinks in it — that reads as a brush inside a brush");
    }

    [Fact]
    public void ARingIsHollow()
    {
        const int Size = 64;
        var a = Bake(TipShape.Ring, Size, r => r.InnerRadius = 0.6);
        var c = Size / 2;

        Assert.Equal(0f, At(a, Size, c, c));
        // Between the inner radius and the rim.
        Assert.True(At(a, Size, c + (int)(Size * 0.4), c) > 0.9f, "the ring itself is missing");
        Assert.Equal(0f, At(a, Size, 0, 0));
    }

    [Fact]
    public void AChiselIsFlatAcrossItsShortAxis()
    {
        const int Size = 64;
        var a = Bake(TipShape.Chisel, Size, r => r.Roundness = 0.2);
        var c = Size / 2;

        // Long axis reaches the rim; short axis has run out well before it.
        Assert.True(At(a, Size, c + Size / 4, c) > 0.9f, "the chisel is not long");
        Assert.True(At(a, Size, c, c + Size / 4) < 0.1f, "the chisel is not flat");
    }

    [Fact]
    public void AngleIsBakedIntoTheShape()
    {
        // Baked rather than applied per dab: a rotation the engine has to do
        // hundreds of times a stroke, when it could have been done once.
        const int Size = 64;
        var flat = Bake(TipShape.Chisel, Size, r => { r.Roundness = 0.2; r.Angle = 0; });
        var turned = Bake(TipShape.Chisel, Size, r => { r.Roundness = 0.2; r.Angle = 90; });
        var c = Size / 2;

        Assert.True(At(flat, Size, c + Size / 4, c) > 0.9f);
        Assert.True(At(turned, Size, c + Size / 4, c) < 0.1f);
        Assert.True(At(turned, Size, c, c + Size / 4) > 0.9f);
    }

    [Fact]
    public void HatchRulesAreDrawnAsWidthNotAsSinglePixels()
    {
        // `x % spacing == 0` gives a one-pixel hard line that aliases at every
        // scale and whose grid phase shifts between frames. Rules have a width,
        // and their edges are coverage like everything else here.
        const int Size = 128;
        var a = Bake(TipShape.Hatch, Size, r =>
        {
            r.Spacing = 16;
            r.LineWidth = 5;
        });

        var c = Size / 2;
        var row = Enumerable.Range(0, Size).Select(x => At(a, Size, x, c)).ToList();
        var solid = row.Count(v => v > 0.9f);
        var soft = row.Count(v => v > 0.05f && v < 0.9f);

        Assert.True(solid > 8, $"the rules are hairlines: {solid} solid pixels across the tip");
        Assert.True(soft > 4, $"the rules have hard edges: {soft} partial pixels");
        Assert.True(row.Count(v => v <= 0.05f) > solid, "there is no gap between the rules");
    }

    [Fact]
    public void AHatchStaysInsideTheRoundFootprint()
    {
        // Otherwise the tip stamps its own square, which is the same defect the
        // scan pipeline rejects a bad crop for.
        const int Size = 64;
        var a = Bake(TipShape.Hatch, Size, r => { r.Spacing = 8; r.LineWidth = 4; });

        Assert.Equal(0f, At(a, Size, 0, 0));
        Assert.Equal(0f, At(a, Size, Size - 1, 0));
        Assert.Equal(0f, At(a, Size, 0, Size - 1));
        Assert.Equal(0f, At(a, Size, Size - 1, Size - 1));
    }

    [Fact]
    public void CrossHatchRulesBothWays()
    {
        const int Size = 96;
        var single = Bake(TipShape.Hatch, Size, r => { r.Spacing = 12; r.LineWidth = 3; });
        var crossed = Bake(TipShape.Hatch, Size, r =>
        {
            r.Spacing = 12;
            r.LineWidth = 3;
            r.Crossed = true;
        });

        Assert.True(crossed.Sum() > single.Sum() * 1.5f, "crossing added no second set of rules");
    }

    [Fact]
    public void ABakedTipCarriesItsShapeInAlpha()
    {
        // What BrushTipRegistry expects, and what the importers already
        // produce — so a generated tip needs no special case anywhere
        // downstream.
        using var bitmap = TipGenerator.Bake(new TipRecipe { Shape = TipShape.HardCircle, Size = 32 });

        Assert.Equal(32, bitmap.Width);
        Assert.Equal(255, bitmap.GetPixel(16, 16).Alpha);
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
        Assert.Equal(255, bitmap.GetPixel(16, 16).Red);
    }

    [Fact]
    public void TheSameRecipeBakesTheSameTipEveryTime()
    {
        // Invariant 2 reaches here too, though for a different reason: a tip
        // that baked differently on two machines would make the same document
        // render differently on each.
        var recipe = new TipRecipe { Shape = TipShape.SoftCircle, Size = 64, Hardness = 0.37, Angle = 21 };
        var a = TipGenerator.Coverage(recipe, 64);
        var b = TipGenerator.Coverage(recipe, 64);

        for (var i = 0; i < a.Length; i++)
        {
            Assert.Equal(BitConverter.SingleToInt32Bits(a[i]), BitConverter.SingleToInt32Bits(b[i]));
        }
    }

    [Fact]
    public void ARecipeIsProvenanceAndTravelsWithTheTip()
    {
        // Kept so a tip can be reopened and adjusted — and never read while
        // drawing, because re-deriving pixels from a recipe at load would mean
        // improving a falloff curve silently repaints old drawings.
        var tip = TipGenerator.Create(
            new TipRecipe { Shape = TipShape.Ring, Size = 64, InnerRadius = 0.5 }, "My ring");

        Assert.Equal("My ring", tip.Name);
        Assert.NotEmpty(tip.Png);
        Assert.Equal(TipShape.Ring, tip.Recipe?.Shape);
        Assert.Equal(0.5, tip.Recipe!.InnerRadius);
        Assert.Equal(0.5, tip.PivotX);
    }

    [Fact]
    public void AnAbsurdSizeIsClampedRatherThanAllocated()
    {
        using var tiny = TipGenerator.Bake(new TipRecipe { Size = 1 });
        using var huge = TipGenerator.Bake(new TipRecipe { Size = 100_000 });

        Assert.Equal(TipGenerator.MinSize, tiny.Width);
        Assert.Equal(TipGenerator.MaxSize, huge.Width);
    }
}

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

    // ---- the five shapes a circle and a chisel cannot reach -----------------

    [Fact]
    public void ABristleTipIsCombedAtTheRimAndSolidInTheMiddle()
    {
        // What a round hog brush leaves when the paint thins. A comb that
        // reached the centre would stamp a star, not a brush — so the channels
        // have to be a rim effect, and the middle has to stay a disc.
        const int Size = 192;
        var a = Bake(TipShape.Bristle, Size, r => { r.Count = 12; r.Sharpness = 0.5; });
        var c = Size / 2;

        // Measured as a ratio, not a difference. The body is a soft disc, so
        // out near the rim its own coverage is low and an absolute threshold
        // would be measuring the falloff rather than the comb.
        var ring = Ring(a, Size, Size * 0.35f);
        float low = ring.Min(), high = ring.Max();
        output.WriteLine($"rim {low:F2}..{high:F2}");
        Assert.True(low < high * 0.35f, $"the rim is not combed: {low:F2}..{high:F2}");

        // The same ring taken near the middle must not be.
        var core = Ring(a, Size, Size * 0.12f);
        Assert.True(core.Max() - core.Min() < 0.05f,
            $"the comb reached the centre: {core.Min():F2}..{core.Max():F2} — that is a star");
        Assert.True(At(a, Size, c, c) > 0.95f, "the middle is not solid");
    }

    [Fact]
    public void MoreBristlesMeansMoreChannels()
    {
        // The control has to mean what it says: raising the count narrows the
        // channels rather than deepening them, which is why the comb is raised
        // to a power instead of being scaled.
        const int Size = 192;
        int Crossings(int count)
        {
            var a = Bake(TipShape.Bristle, Size, r => { r.Count = count; r.Sharpness = 0.6; });
            return Peaks(Ring(a, Size, Size * 0.35f));
        }

        int few = Crossings(6), many = Crossings(16);
        output.WriteLine($"6 bristles -> {few} channels, 16 -> {many}");
        Assert.Equal(6, few);
        Assert.Equal(16, many);
    }

    [Fact]
    public void OneExponentWalksTheWholeSuperellipseFamily()
    {
        // Lamé's curve: n = 2 is an ellipse, n → ∞ a rectangle, n < 1 an almond.
        // The family differs only in how much of the corner is filled, so area
        // at a fixed axis length is the measurement — and it has to be monotone
        // in the exponent, or the slider does not walk the family, it wanders
        // about in it.
        const int Size = 128;
        var c = Size / 2;

        var pointed = Bake(TipShape.Superellipse, Size, r => { r.Roundness = 1; r.Sharpness = 0; });
        var round = Bake(TipShape.Superellipse, Size, r => { r.Roundness = 1; r.Sharpness = 0.2; });
        var square = Bake(TipShape.Superellipse, Size, r => { r.Roundness = 1; r.Sharpness = 1; });

        float p = pointed.Sum(), m = round.Sum(), s = square.Sum();
        output.WriteLine($"area: almond {p:F0}, ellipse {m:F0}, square {s:F0} of {Size * Size}");

        Assert.True(p < m * 0.9f, $"a lower exponent did not pinch the corners: {p:F0} vs {m:F0}");
        Assert.True(s > m * 1.1f, $"a higher exponent did not fill the corners: {m:F0} vs {s:F0}");

        // And the corner is where they differ. At 80% of the way out along the
        // diagonal a square nib is solid and an ellipse has already ended —
        // the boundary sits at 2^(-1/n) of the axis, which is 0.71 at n = 2 and
        // 0.92 at n = 8.
        var q = (int)(Size * 0.40f);
        Assert.True(At(square, Size, c + q, c + q) > 0.9f, "the square nib has no corner");
        Assert.True(At(round, Size, c + q, c + q) < 0.1f, "the ellipse grew one");

        // Along the axes every member of the family reaches the same place.
        Assert.True(At(square, Size, c + q, c) > 0.9f && At(pointed, Size, c + q, c) > 0.9f);
    }

    [Fact]
    public void ASuperellipseIsStillFlattenedByRoundness()
    {
        // It shares the chisel's minor axis, so a flat marker nib is one shape
        // rather than a second one bolted on.
        const int Size = 128;
        var a = Bake(TipShape.Superellipse, Size, r => { r.Roundness = 0.25; r.Sharpness = 0.8; });
        var c = Size / 2;

        Assert.True(At(a, Size, c + Size / 4, c) > 0.9f, "the nib is not long");
        Assert.True(At(a, Size, c, c + Size / 4) < 0.1f, "the nib is not flat");
    }

    [Fact]
    public void APolygonHasCornersThatReachFurtherThanItsFlats()
    {
        // The one procedural shape with corners, and that is its whole reason
        // for existing: the boundary radius peaks at a vertex and dips at the
        // middle of a side, which no circle or ellipse can do.
        const int Size = 192;
        var a = Bake(TipShape.Polygon, Size, r => { r.Count = 5; r.Sharpness = 1; r.Angle = 0; });

        var ring = Ring(a, Size, Size * 0.44f);
        var corners = ring.Count(v => v > 0.5f);
        output.WriteLine($"{corners} of {ring.Count} samples inside at r=0.44");

        // Five wedges of solid, five gaps: a circle would give all or nothing.
        Assert.True(corners > 0 && corners < ring.Count,
            $"no corners at all — this is a disc ({corners}/{ring.Count})");
        Assert.Equal(5, Peaks(ring));
    }

    [Fact]
    public void RoundingAPolygonPullsItTowardTheCircleItSitsInside()
    {
        // Rounding must not grow the shape. Blending toward a *larger* circle
        // would be the obvious implementation and would make the corner
        // slider change the tip's size, which an artist reads as a bug in the
        // size slider rather than in this one.
        const int Size = 128;
        var sharp = Bake(TipShape.Polygon, Size, r => { r.Count = 5; r.Sharpness = 1; });
        var soft = Bake(TipShape.Polygon, Size, r => { r.Count = 5; r.Sharpness = 0; });

        Assert.True(soft.Sum() < sharp.Sum(), "rounding grew the polygon");

        // …and at zero sharpness it is the inscribed circle, so the four
        // cardinals agree the way a disc's do.
        var c = Size / 2;
        Assert.Equal(At(soft, Size, c + Size / 4, c), At(soft, Size, c, c + Size / 4), 1e-4f);
    }

    [Fact]
    public void SpatterGrainsHaveASizeRatherThanBeingFog()
    {
        // Worley rather than value noise, and this is the difference: cellular
        // noise gives grains with a characteristic size, and thresholded value
        // noise gives ragged blobs with none. Measured as run lengths across a
        // row — fog has runs of one or two pixels everywhere.
        const int Size = 256;
        var a = Bake(TipShape.Spatter, Size, r => { r.Count = 8; r.Sharpness = 0.5; });

        var runs = new List<int>();
        for (var y = Size / 3; y < Size * 2 / 3; y += 8)
        {
            var run = 0;
            for (var x = 0; x < Size; x++)
            {
                if (At(a, Size, x, y) > 0.5f) run++;
                else { if (run > 0) runs.Add(run); run = 0; }
            }
            if (run > 0) runs.Add(run);
        }

        Assert.NotEmpty(runs);
        var mean = runs.Average();
        output.WriteLine($"{runs.Count} grains, mean run {mean:F1}px of a {Size}px tip");

        // A cell is 256/8 = 32 px across and a grain fills a good part of one.
        Assert.True(mean > 6, $"the grains are dust, not grains: mean run {mean:F1}px");
        Assert.True(mean < 64, $"this is not a spatter, it is a disc with holes: mean run {mean:F1}px");
    }

    [Fact]
    public void SpatterCoverageIsMonotoneAndTheCellCountSetsTheGrainSize()
    {
        const int Size = 192;
        var sparse = Bake(TipShape.Spatter, Size, r => { r.Count = 8; r.Sharpness = 0.15; });
        var dense = Bake(TipShape.Spatter, Size, r => { r.Count = 8; r.Sharpness = 0.9; });
        Assert.True(dense.Sum() > sparse.Sum() * 1.5f, "the coverage slider did nothing");

        // More cells across the same tip means smaller grains, so at equal
        // coverage the number of separate grains goes up.
        int Grains(int cells)
        {
            var a = Bake(TipShape.Spatter, Size, r => { r.Count = cells; r.Sharpness = 0.5; });
            var y = Size / 2;
            var n = 0;
            for (var x = 1; x < Size; x++)
            {
                if (At(a, Size, x, y) > 0.5f && At(a, Size, x - 1, y) <= 0.5f) n++;
            }
            return n;
        }

        output.WriteLine($"4 cells -> {Grains(4)} grains, 20 -> {Grains(20)}");
        Assert.True(Grains(20) > Grains(4), "the cell count did not change the grain size");
    }

    [Fact]
    public void AHaloIsDenserAtItsRimThanInItsMiddle()
    {
        // The dried-puddle profile: pigment carried to the contact line and
        // stranded there. FluidLattice produces it from the flow; this is the
        // stamped counterpart for a brush that is not paying for a simulation.
        const int Size = 192;
        var a = Bake(TipShape.Halo, Size, r => r.Sharpness = 0.9);
        var c = Size / 2;

        float middle = At(a, Size, c, c), rim = At(a, Size, c + (int)(Size * 0.43f), c);
        output.WriteLine($"middle {middle:F2}, rim {rim:F2}");

        Assert.True(rim > middle + 0.3f, $"there is no wet edge: middle {middle:F2}, rim {rim:F2}");
        Assert.True(middle > 0.05f, "the middle is empty — this is a ring, not a wash");
        Assert.Equal(0f, At(a, Size, 0, 0));
    }

    [Fact]
    public void TheHaloRimSliderTakesItBackToAFlatWash()
    {
        // At zero the profile has to be flat, because "no wet edge" is a real
        // answer an artist wants — and a shape whose control cannot be turned
        // off is a shape with a personality you cannot escape.
        const int Size = 128;
        var flat = Bake(TipShape.Halo, Size, r => r.Sharpness = 0);
        var c = Size / 2;

        float middle = At(flat, Size, c, c), rim = At(flat, Size, c + (int)(Size * 0.4f), c);
        Assert.Equal(middle, rim, 0.05f);
    }

    [Fact]
    public void EveryShapeBakesSomethingAndStaysInsideTheMatrix()
    {
        // Cheap, and it catches the case a new shape is added to the enum and
        // to the picker but not to the switch — which bakes a blank tip and
        // reads to the artist as a broken brush.
        foreach (var shape in Enum.GetValues<TipShape>())
        {
            var a = Bake(shape, 96, r => { r.Count = 7; r.Sharpness = 0.6; r.Roundness = 0.5; });

            Assert.True(a.Sum() > 50, $"{shape} baked nothing");
            Assert.All(a, v => Assert.InRange(v, 0f, 1f));
            // Corners are outside every shape's round footprint.
            Assert.Equal(0f, At(a, 96, 0, 0));
            Assert.Equal(0f, At(a, 96, 95, 95));
        }
    }

    [Fact]
    public void EveryShapeBakesTheSameWayTwice()
    {
        // Spatter is the one that could have gone wrong — Worley noise is the
        // obvious place to reach for an RNG, and a tip that baked differently
        // on two machines would make the same document render differently on
        // each. It hashes the cell index instead, the same way the dab
        // dynamics hash a position.
        foreach (var shape in Enum.GetValues<TipShape>())
        {
            var recipe = new TipRecipe { Shape = shape, Size = 64, Count = 9, Sharpness = 0.4, Angle = 17 };
            var a = TipGenerator.Coverage(recipe, 64);
            var b = TipGenerator.Coverage(recipe, 64);
            for (var i = 0; i < a.Length; i++)
            {
                Assert.Equal(BitConverter.SingleToInt32Bits(a[i]), BitConverter.SingleToInt32Bits(b[i]));
            }
        }
    }

    /// <summary>
    /// Coverage sampled all the way round a circle of the given radius. The
    /// natural way to ask "is this shape the same in every direction", which is
    /// what separates a bristle, a polygon and a disc from each other.
    /// </summary>
    private static List<float> Ring(float[] a, int size, float radius)
    {
        // 720 samples, not 360: a bristle groove is about a tenth of the hair
        // spacing, and at 360 a sixteen-hair comb puts two samples in each one.
        // Undersampling here reads as "the shape has no grooves".
        const int Steps = 720;
        var c = size * 0.5f;
        var samples = new List<float>(Steps);
        for (var step = 0; step < Steps; step++)
        {
            var t = step * 2f * MathF.PI / Steps;
            var x = (int)(c + radius * MathF.Cos(t));
            var y = (int)(c + radius * MathF.Sin(t));
            if (x >= 0 && y >= 0 && x < size && y < size) samples.Add(At(a, size, x, y));
        }
        return samples;
    }

    /// <summary>
    /// How many times a ring rises and falls: bristles, or the corners of a
    /// polygon. Hysteresis rather than one threshold, because the ring is
    /// sampled off a pixel grid and a single level would count the same peak
    /// several times wherever the quantisation wobbles across it.
    /// </summary>
    private static int Peaks(List<float> ring)
    {
        float low = ring.Min(), high = ring.Max();
        float up = low + (high - low) * 0.7f, down = low + (high - low) * 0.3f;

        // Start from a known-low sample so the wrap-around is not a peak.
        var start = ring.FindIndex(v => v < down);
        if (start < 0) return 0;

        var peaks = 0;
        var inside = false;
        for (var i = 0; i < ring.Count; i++)
        {
            var v = ring[(start + i) % ring.Count];
            if (!inside && v > up) { inside = true; peaks++; }
            else if (inside && v < down) inside = false;
        }
        return peaks;
    }

    // ---- the drop, the crescent and the blot ---------------------------------

    [Fact]
    public void ADropIsACircleAtZeroAndPointedAtOne()
    {
        // Sharpness 0 must be exactly the disc — the degenerate case where the
        // belly and the point coincide — so the slider starts from a shape the
        // artist already knows.
        const int Size = 128;
        var zero = Bake(TipShape.Drop, Size, r => r.Sharpness = 0);
        var disc = Bake(TipShape.HardCircle, Size);
        for (var i = 0; i < zero.Length; i++)
        {
            Assert.Equal(BitConverter.SingleToInt32Bits(disc[i]), BitConverter.SingleToInt32Bits(zero[i]));
        }

        // At 1 the top quarter is a taper and the bottom quarter is a belly.
        var pointed = Bake(TipShape.Drop, Size, r => r.Sharpness = 1);
        int Width(int y) => Enumerable.Range(0, Size).Count(x => At(pointed, Size, x, y) > 0.5f);
        int neck = Width(Size / 4), belly = Width(Size * 3 / 4);
        output.WriteLine($"width at the neck {neck}px, at the belly {belly}px");

        Assert.True(neck > 0, "the point is missing entirely");
        Assert.True(belly > neck * 2, $"no taper: neck {neck}px vs belly {belly}px");
    }

    [Fact]
    public void ACrescentBitesDeeperAsSharpnessRises()
    {
        const int Size = 128;
        var c = Size / 2;
        var shallow = Bake(TipShape.Crescent, Size, r => r.Sharpness = 0.2);
        var deep = Bake(TipShape.Crescent, Size, r => r.Sharpness = 1);

        output.WriteLine($"area shallow {shallow.Sum():F0}, deep {deep.Sum():F0}");
        Assert.True(deep.Sum() < shallow.Sum() * 0.6f, "the bite slider did nothing");

        // At full bite the middle is gone and the horn side is still solid.
        Assert.True(At(deep, Size, c, c) < 0.05f, "the deep crescent still has a middle");
        Assert.True(At(deep, Size, (int)(Size * 0.14f), c) > 0.9f, "the crescent body is missing");
    }

    [Fact]
    public void ABlotIsLobedRatherThanRound()
    {
        // The whole reason the shape exists: a boundary that does not read as
        // geometry. Measured as the spread of the boundary radius by angle —
        // a circle's is zero.
        const int Size = 192;
        var a = Bake(TipShape.Blot, Size, r => { r.Count = 5; r.Sharpness = 0.8; });
        var c = Size * 0.5f;

        var radii = new List<float>();
        for (var step = 0; step < 16; step++)
        {
            var t = step * 2f * MathF.PI / 16;
            float r = 0;
            for (var d = 0f; d < c; d += 0.5f)
            {
                var x = (int)(c + d * MathF.Cos(t));
                var y = (int)(c + d * MathF.Sin(t));
                if (At(a, Size, x, y) > 0.5f) r = d;
            }
            radii.Add(r);
        }

        output.WriteLine($"boundary radius {radii.Min():F0}..{radii.Max():F0}px");
        Assert.True(radii.Max() - radii.Min() > Size * 0.04f,
            $"the blot is a circle: radii {radii.Min():F0}..{radii.Max():F0}");

        // And the lobe count is a real control, not a label.
        var other = Bake(TipShape.Blot, Size, r => { r.Count = 8; r.Sharpness = 0.8; });
        Assert.False(a.SequenceEqual(other), "changing the lobe count changed nothing");
    }

    // ---- alpha gradients ------------------------------------------------------

    [Fact]
    public void FadePullsTheGradientInFromTheBoundary()
    {
        // Fade 0 is the crisp edge every recipe baked before the control
        // existed; above it the alpha climbs from the boundary to a solid core.
        const int Size = 128;
        var c = Size / 2;
        var crisp = Bake(TipShape.Chisel, Size, r => r.Roundness = 0.4);
        var faded = Bake(TipShape.Chisel, Size, r => { r.Roundness = 0.4; r.Fade = 0.7; });

        var minor = Size / 2 * 0.4f;
        var midBand = (int)(minor * 0.7f);
        float crispMid = At(crisp, Size, c, c + midBand), fadedMid = At(faded, Size, c, c + midBand);
        output.WriteLine($"mid-band alpha: crisp {crispMid:F2}, faded {fadedMid:F2}");

        Assert.Equal(1f, At(crisp, Size, c, c + midBand), 1e-3f);
        Assert.InRange(fadedMid, 0.05f, 0.95f);
        Assert.Equal(1f, At(faded, Size, c, c), 1e-3f); // the core is still solid

        // The gradient descends monotonically across the band.
        var run = new[] { 0, midBand, (int)(minor - 2) }.Select(y => At(faded, Size, c, c + y)).ToList();
        Assert.True(run[0] > run[1] && run[1] > run[2],
            $"the fade is not a gradient: {run[0]:F2}, {run[1]:F2}, {run[2]:F2}");
    }

    [Fact]
    public void AFadedChiselFadesEvenlyAroundItsBoundaryNotIntoABone()
    {
        // The ray-scaled distance the crisp edge uses over-reports on the
        // flanks, and a fade band built on it pinches the solid core into a
        // bone — two dark lobes and a waist. The band uses the gradient
        // distance instead, so at the same fraction of the local thickness the
        // alpha is the same wherever you are along the shape.
        const int Size = 256;
        var a = Bake(TipShape.Chisel, Size, r => { r.Roundness = 0.4; r.Fade = 0.8; });
        var c = Size / 2;
        var radius = Size / 2f;
        var minor = radius * 0.4f;

        float AtFraction(int xOff)
        {
            // 60% of the way across the local half-thickness.
            var local = minor * MathF.Sqrt(1f - xOff * xOff / (radius * radius));
            return At(a, Size, c + xOff, c + (int)(local * 0.6f));
        }

        float middle = AtFraction(0), flank = AtFraction(Size / 4);
        output.WriteLine($"at 60% of local thickness: middle {middle:F2}, flank {flank:F2}");
        Assert.True(MathF.Abs(middle - flank) < 0.15f,
            $"the fade pinches: {middle:F2} at the middle vs {flank:F2} on the flank");
    }

    [Fact]
    public void TheFourFalloffCurvesBakeFourDifferentEdges()
    {
        // Probed a quarter of the way up the band, where the four curves are
        // furthest apart: the airbrush's tail is faintest, the dome fullest.
        const int Size = 128;
        var c = Size / 2;
        var minor = Size / 2 * 0.4f;
        var band = 0.8f * minor;
        var probe = c + (int)(minor - band * 0.25f);

        float Probe(TipFalloff curve)
        {
            var a = Bake(TipShape.Chisel, Size, r =>
            {
                r.Roundness = 0.4;
                r.Fade = 0.8;
                r.FadeProfile = curve;
            });
            return At(a, Size, c, probe);
        }

        float smooth = Probe(TipFalloff.Smooth), linear = Probe(TipFalloff.Linear);
        float airbrush = Probe(TipFalloff.Airbrush), dome = Probe(TipFalloff.Dome);
        output.WriteLine($"airbrush {airbrush:F2} < smooth {smooth:F2} < linear {linear:F2} < dome {dome:F2}");

        Assert.True(airbrush < smooth && smooth < linear && linear < dome,
            $"the curves are not distinct: airbrush {airbrush:F2}, smooth {smooth:F2}, "
            + $"linear {linear:F2}, dome {dome:F2}");
    }

    [Fact]
    public void TheSoftCircleFollowsTheChosenFalloffToo()
    {
        // The falloff choice applies wherever a band exists — the soft
        // circle's hardness band included, or the same control would mean
        // different things on different shapes.
        const int Size = 128;
        var c = Size / 2;
        var smooth = Bake(TipShape.SoftCircle, Size, r => r.Hardness = 0.2);
        var dome = Bake(TipShape.SoftCircle, Size, r =>
        {
            r.Hardness = 0.2;
            r.FadeProfile = TipFalloff.Dome;
        });

        var probe = c + Size / 4;
        output.WriteLine($"mid-band: smooth {At(smooth, Size, probe, c):F2}, dome {At(dome, Size, probe, c):F2}");
        Assert.True(MathF.Abs(At(smooth, Size, probe, c) - At(dome, Size, probe, c)) > 0.05f,
            "the falloff choice does nothing on a soft circle");
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

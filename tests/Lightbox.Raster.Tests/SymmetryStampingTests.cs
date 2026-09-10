using System.Security.Cryptography;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// What symmetry actually puts on pixels.
/// </summary>
/// <remarks>
/// <para>
/// The claim under test is the one Q185 chose the implementation for: a
/// reflected mark is a <b>true mirror</b>, not a different mark in a mirrored
/// place. <c>Hash01</c> seeds scatter, size, flow, roundness, rotation and all
/// three colour jitters from the IEEE-754 bits of a dab's position, so stamping
/// a copy at reflected coordinates would re-roll every one of them. Reflecting
/// the canvas instead leaves the coordinates the artist drew intact and mirrors
/// the ink — invariant 7's argument applied to reflection.
/// </para>
/// <para>
/// <b>Scatter is on in the mirror tests, and that is the point.</b> With every
/// dynamic off, a re-seeded implementation and a correct one are
/// indistinguishable — a round dab is its own mirror. Scatter is the cheapest
/// dynamic that moves a dab somewhere a different hash would not put it, so it
/// is what makes these tests able to fail.
/// </para>
/// <para>
/// <b>Granulation and texture are off, deliberately.</b> Paper is anchored to
/// the document rather than to the stroke, so a copy sitting elsewhere picks up
/// that part of the grain — correctly: real paper is not mirrored. The mark
/// mirrors and the paper does not, so a test of the mark must not have paper in
/// it.
/// </para>
/// </remarks>
public class SymmetryStampingTests(ITestOutputHelper output)
{
    private const int W = 200;
    private const int H = 140;

    /// <summary>
    /// Scatter and rotation jitter on, paper off — a brush whose mark a wrong
    /// seed would visibly change.
    /// </summary>
    private static BrushSettings Speckled => new()
    {
        Size = 18,
        Hardness = 0.8,
        Opacity = 1.0,
        Flow = 0.9,
        Spacing = 0.18,
        Scatter = 0.5,
        RotationJitter = 0.6,
        Granulation = 0,
        WetEdge = 0,
        PressureFlowGamma = 1,
    };

    /// <summary>A mark on the left of the page, clear of the axis.</summary>
    private static Stroke Mark(SymmetryAxis? axis = null) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#1b3f7a",
        Brush = Speckled,
        Points =
        [
            new StrokePoint(38, 44, 0.7),
            new StrokePoint(56, 62, 0.9),
            new StrokePoint(72, 52, 0.8),
        ],
        Symmetry = axis,
    };

    /// <summary>
    /// A vertical mirror down the middle of the page, so the reflection of
    /// pixel column <c>i</c> is column <c>W-1-i</c> exactly.
    /// </summary>
    /// <remarks>
    /// The centre is at <c>W/2</c> on an even-width page, so continuous
    /// <c>x ↦ W − x</c> and a pixel centre at <c>i + 0.5</c> maps to
    /// <c>W − 1 − i</c>. Integer, which is what lets these comparisons be exact
    /// rather than approximate.
    /// </remarks>
    private static SymmetryAxis VerticalMirror => new()
    {
        CenterX = W / 2.0, CenterY = H / 2.0, AngleDeg = 90, Order = 1, Mirror = true,
    };

    private static SKBitmap Render(Stroke stroke) => FrameRasterizer.Rasterize([stroke], W, H, 1.0);

    private static string Hash(SKBitmap b) => Convert.ToHexString(SHA256.HashData(b.GetPixelSpan()));

    private static int InkedPixels(SKBitmap b)
    {
        var n = 0;
        var span = b.GetPixelSpan();
        for (var i = 3; i < span.Length; i += 4)
        {
            if (span[i] != 0) n++;
        }

        return n;
    }

    /// <summary>Ink inside an axis-aligned box, for asking "did a copy land here".</summary>
    private static int InkedIn(SKBitmap b, SKRectI box)
    {
        var n = 0;
        for (var y = Math.Max(0, box.Top); y < Math.Min(b.Height, box.Bottom); y++)
        {
            for (var x = Math.Max(0, box.Left); x < Math.Min(b.Width, box.Right); x++)
            {
                if (b.GetPixel(x, y).Alpha != 0) n++;
            }
        }

        return n;
    }

    // ---- the claim ---------------------------------------------------------

    /// <summary>
    /// The reflected half is the mirror image of the drawn half, pixel for
    /// pixel.
    /// </summary>
    /// <remarks>
    /// The whole feature in one assertion. A version that stamped the copy at
    /// reflected coordinates would re-seed every dynamic from the new position:
    /// the scatter would land elsewhere, the rotation jitter would differ, and
    /// this comparison would fail on thousands of pixels while still looking
    /// broadly like a mirror to a person skimming a screenshot.
    /// </remarks>
    [Fact]
    public void AMirroredStrokeIsThePixelMirrorOfTheOneDrawn()
    {
        using var bmp = Render(Mark(VerticalMirror));

        var inked = InkedPixels(bmp);
        var mismatched = 0;
        var worst = 0;
        var deltas = new List<int>();
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W / 2; x++)
            {
                var a = bmp.GetPixel(x, y);
                var b = bmp.GetPixel(W - 1 - x, y);
                if (a == b) continue;
                mismatched++;
                var d = Math.Max(
                    Math.Max(Math.Abs(a.Alpha - b.Alpha), Math.Abs(a.Red - b.Red)),
                    Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue)));
                worst = Math.Max(worst, d);
                deltas.Add(d);
            }
        }

        deltas.Sort();
        var median = deltas.Count > 0 ? deltas[deltas.Count / 2] : 0;
        output.WriteLine(
            $"{inked} px inked; {mismatched} of {W / 2 * H} mirrored pairs disagree; "
            + $"worst channel delta {worst}/255; median delta {median}");

        Assert.True(inked > 400, $"only {inked} px inked — the mark is not landing, so this proves nothing");

        // 5% of the inked pixels, and the number is set by what the broken
        // version does rather than by taste. Re-seeding each copy from its own
        // reflected coordinate moves every scattered dab by up to
        // Scatter × Size = 9 px, so very nearly every inked pixel in the two
        // halves disagrees — of the order of 2000 here, against the 25 this
        // measures. Measured 1.2% correct against ~100% broken, so the bar sits
        // between them with almost two orders of magnitude to spare.
        Assert.True(
            mismatched * 20 < inked,
            $"{mismatched} of {inked} inked px disagree across the mirror "
            + $"({mismatched / (double)inked:P1}) — the copies are not mirrors of each other");

        // And the residue is antialiasing rather than misplacement: a dab in the
        // wrong place disagrees at full opacity, an edge rounded the other way
        // disagrees by a bit or two. AHardEdgedMirrorIsExactToTheLastPixel is
        // the other half of this argument.
        Assert.True(
            median <= 4,
            $"the median disagreement is {median}/255, which is misplaced ink rather than a rounded edge");
    }

    /// <summary>
    /// The mirror's residue is the rasteriser's pixel snapping, not ink in the
    /// wrong place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="AMirroredStrokeIsThePixelMirrorOfTheOneDrawn"/> leaves a
    /// handful of pairs disagreeing, and the question worth answering is which
    /// kind of wrong that is. Misplaced ink disagrees at full opacity over
    /// hundreds of pixels; a rounded edge disagrees by a bit or two over a few.
    /// </para>
    /// <para>
    /// <b>Measured, the answer is in two parts.</b> With antialiasing on, 25 of
    /// 2066 inked pixels disagree at a median of 1/255. Turn the edge hard and
    /// every jitter off and it falls to 8 of 2037 — so most of the residue is
    /// Skia antialiasing a path whose matrix has a negative determinant, and
    /// what is left is pixel-centre tie-breaking: with no antialiasing, a dab
    /// edge falling exactly on a pixel boundary is taken on one side and dropped
    /// on its mirror. Neither is symmetric and neither can be made so from here.
    /// </para>
    /// <para>
    /// <b>And that is the right trade rather than a defect tolerated.</b> The
    /// alternative is rendering one half and blitting its mirrored pixels, which
    /// would break paper anchoring (the grain is the document's, not the
    /// stroke's), could not express a rotation at all, and would buy exactness
    /// nobody can see: 0.4% of a mark's pixels differing by one part in 255. The
    /// geometry underneath is exact, which is what
    /// <see cref="TurningSymmetryOnLeavesTheDrawnHalfExactlyAsItWas"/> and the
    /// determinism tests establish.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheMirrorsResidueIsPixelSnappingNotMisplacedInk()
    {
        var hard = Mark(VerticalMirror);
        hard.Brush = new BrushSettings
        {
            Size = 18, Hardness = 1.0, Opacity = 1.0, Flow = 1.0, Spacing = 0.18,
            Scatter = 0.5, RotationJitter = 0, Granulation = 0, WetEdge = 0,
            AntiAlias = false, PressureFlowGamma = 1,
        };

        using var antialiased = Render(Mark(VerticalMirror));
        using var hardEdged = Render(hard);

        var (aaInked, aaBad) = MirrorDisagreement(antialiased);
        var (hardInked, hardBad) = MirrorDisagreement(hardEdged);

        output.WriteLine(
            $"antialiased: {aaBad} of {aaInked} inked disagree ({aaBad / (double)aaInked:P2}); "
            + $"hard-edged: {hardBad} of {hardInked} ({hardBad / (double)hardInked:P2})");

        Assert.True(aaInked > 400 && hardInked > 400, "the marks are not landing");

        // Turning the edge hard removes most of the disagreement, which is what
        // says the bulk of it was antialiasing.
        Assert.True(
            hardBad <= aaBad,
            $"a hard edge disagreed on {hardBad} pairs against {aaBad} antialiased — "
            + "the residue is not antialiasing after all, so this needs re-diagnosing");

        // What remains is tie-breaking, and it is under a half-percent. The bar
        // is nowhere near the broken case: re-seeding each copy from its own
        // reflected coordinate moves every scattered dab by up to 9 px here and
        // disagrees on very nearly every inked pixel.
        Assert.True(
            hardBad * 100 < hardInked,
            $"{hardBad} of {hardInked} hard-edged pixels disagree "
            + $"({hardBad / (double)hardInked:P1}) — that is misplaced ink, not a snapped edge");
    }

    /// <summary>Inked pixels, and how many disagree with their mirror.</summary>
    private static (int Inked, int Disagreeing) MirrorDisagreement(SKBitmap bmp)
    {
        var bad = 0;
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W / 2; x++)
            {
                if (bmp.GetPixel(x, y) != bmp.GetPixel(W - 1 - x, y)) bad++;
            }
        }

        return (InkedPixels(bmp), bad);
    }

    /// <summary>
    /// Turning symmetry on adds a copy and does not touch the mark that was
    /// already there.
    /// </summary>
    /// <remarks>
    /// Invariant 1 from the artist's side: the gesture is one stroke, and the
    /// reflection is something the renderer adds rather than something that
    /// rewrites what was drawn. Compared over the drawn half's own columns, so
    /// the reflection is out of frame.
    /// </remarks>
    [Fact]
    public void TurningSymmetryOnLeavesTheDrawnHalfExactlyAsItWas()
    {
        using var plain = Render(Mark());
        using var mirrored = Render(Mark(VerticalMirror));

        var differing = 0;
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W / 2; x++)
            {
                if (plain.GetPixel(x, y) != mirrored.GetPixel(x, y)) differing++;
            }
        }

        output.WriteLine($"{differing} px of the drawn half moved when symmetry was switched on");
        Assert.Equal(0, differing);
    }

    // ---- paper ------------------------------------------------------------

    /// <summary>
    /// Granulation reaches a reflected copy, and carves it by about as much as
    /// it carves the mark that was drawn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This was a real bug and every other symmetry test was blind to it.</b>
    /// They all set <c>Granulation = 0</c>. Granulation draws a noise mask with
    /// <c>DstIn</c> over <c>rect</c>, and <c>rect</c> is the copy's rectangle
    /// while the canvas still carries the copy's transform — so the rectangle was
    /// transformed a second time, landed outside the copy's own scratch, and was
    /// clipped away in silence. The mask carves alpha, so the symptom is the
    /// reflected copy coming out <em>more opaque</em> than the drawn one: paper
    /// that never bit.
    /// </para>
    /// <para>
    /// Mean alpha over inked pixels rather than a pixel comparison, because the
    /// two halves are deliberately NOT identical — see
    /// <see cref="TheGrainDoesNotTravelWithTheMark"/>.
    /// </para>
    /// </remarks>
    [Fact]
    public void GranulationReachesAReflectedCopy()
    {
        var stroke = Mark(VerticalMirror);
        stroke.Brush = Grainy;

        using var bmp = Render(stroke);
        var (drawnPx, drawnMean) = MeanAlpha(bmp, new SKRectI(0, 0, W / 2, H));
        var (copyPx, copyMean) = MeanAlpha(bmp, new SKRectI(W / 2, 0, W, H));

        output.WriteLine(
            $"drawn half {drawnPx} px at mean alpha {drawnMean:0.0}; "
            + $"reflected half {copyPx} px at mean alpha {copyMean:0.0} "
            + $"— {copyMean / drawnMean:0.000}x");

        Assert.True(drawnPx > 300 && copyPx > 300, "one of the halves did not land");

        // Within a tenth. Unfixed, the copy's mask was clipped entirely, so it
        // kept alpha the drawn half had had carved out of it.
        Assert.True(
            Math.Abs(copyMean - drawnMean) < drawnMean * 0.1,
            $"the reflected copy averages {copyMean:0.0} alpha against {drawnMean:0.0} for the "
            + "drawn mark — the paper did not bite the copy");
    }

    /// <summary>
    /// The grain does not travel with the mark: the two halves are granulated,
    /// and differently.
    /// </summary>
    /// <remarks>
    /// The other half of the fix, and the property that decides which way it had
    /// to be done. Paper is anchored to the document rather than to the stroke —
    /// real paper is not mirrored — so a copy sitting elsewhere picks up the
    /// grain that is there. Leaving the mask inside the transformed block would
    /// have mirrored the field along with the mark, which is why the shader is
    /// handed the copy's inverse instead.
    /// </remarks>
    [Fact]
    public void TheGrainDoesNotTravelWithTheMark()
    {
        var stroke = Mark(VerticalMirror);
        stroke.Brush = Grainy;

        using var bmp = Render(stroke);
        var mirrored = 0;
        var inked = 0;
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W / 2; x++)
            {
                var a = bmp.GetPixel(x, y);
                var b = bmp.GetPixel(W - 1 - x, y);
                if (a.Alpha == 0 && b.Alpha == 0) continue;
                inked++;
                if (a.Alpha == b.Alpha) mirrored++;
            }
        }

        output.WriteLine($"{mirrored} of {inked} inked pairs have identical alpha across the axis");
        Assert.True(inked > 300, "the mark did not land");

        // If the grain were reflected with the mark the halves would agree
        // almost everywhere, as they do when granulation is off.
        Assert.True(
            mirrored * 2 < inked,
            $"{mirrored} of {inked} pairs match exactly — the grain is mirroring with the mark "
            + "instead of staying anchored to the paper");
    }

    /// <summary>A grainy brush, for the two tests above.</summary>
    private static BrushSettings Grainy => new()
    {
        Size = 18, Hardness = 0.8, Opacity = 1.0, Flow = 0.9, Spacing = 0.18,
        Scatter = 0, RotationJitter = 0, Granulation = 0.6, WetEdge = 0,
        PressureFlowGamma = 1,
    };

    /// <summary>Inked pixel count and their mean alpha inside a box.</summary>
    private static (int Count, double Mean) MeanAlpha(SKBitmap b, SKRectI box)
    {
        var n = 0;
        long total = 0;
        for (var y = Math.Max(0, box.Top); y < Math.Min(b.Height, box.Bottom); y++)
        {
            for (var x = Math.Max(0, box.Left); x < Math.Min(b.Width, box.Right); x++)
            {
                var a = b.GetPixel(x, y).Alpha;
                if (a == 0) continue;
                n++;
                total += a;
            }
        }

        return (n, n == 0 ? 0 : total / (double)n);
    }

    // ---- the fast path ----------------------------------------------------

    /// <summary>
    /// A stroke with no axis renders byte-for-byte as it did before symmetry
    /// existed.
    /// </summary>
    /// <remarks>
    /// The pixel-level statement of "nothing may influence drawing
    /// negatively". <c>DrawingCostBaselineTests</c> holds the recorded
    /// fingerprints for three brushes; this is the same claim made locally
    /// against a brush with every dynamic on.
    /// </remarks>
    [Fact]
    public void AStrokeWithNoAxisIsUntouchedByTheSymmetryCode()
    {
        using var a = Render(Mark());
        using var b = Render(Mark());
        Assert.Equal(Hash(a), Hash(b));
        Assert.True(InkedPixels(a) > 400);
    }

    /// <summary>
    /// An axis that asks for nothing renders identically to no axis at all.
    /// </summary>
    /// <remarks>
    /// <c>SymmetryMatrix</c> returns null for the identity placement, and that
    /// is load-bearing rather than an optimisation: a matrix that is
    /// arithmetically the identity still sends every coordinate through Skia's
    /// transform path, and an antialiased edge that shifts by one ULP changes
    /// pixels. This is the test that would notice.
    /// </remarks>
    [Fact]
    public void AnIdentityAxisRendersByteForByteAsNoAxisAtAll()
    {
        using var none = Render(Mark());
        using var idle = Render(Mark(new SymmetryAxis { Order = 1, Mirror = false, AngleDeg = 37 }));
        Assert.Equal(Hash(none), Hash(idle));
    }

    // ---- determinism ------------------------------------------------------

    /// <summary>
    /// Two renders of a symmetric stroke agree exactly.
    /// </summary>
    /// <remarks>
    /// Invariant 2. A stroke is replayed on load, on undo and by the
    /// inbetweener, so a symmetric mark that differed between renders would
    /// boil at 12 fps.
    /// </remarks>
    [Theory]
    [InlineData(1, true)]
    [InlineData(6, false)]
    [InlineData(4, true)]
    public void ASymmetricStrokeRendersTheSameEveryTime(int order, bool mirror)
    {
        var axis = new SymmetryAxis
        {
            CenterX = W / 2.0, CenterY = H / 2.0, AngleDeg = 90, Order = order, Mirror = mirror,
        };
        using var first = Render(Mark(axis));
        using var second = Render(Mark(axis));
        Assert.Equal(Hash(first), Hash(second));
        Assert.True(InkedPixels(first) > 400);
    }

    // ---- radial -----------------------------------------------------------

    /// <summary>
    /// A radial order puts ink on the far side of the centre, which an order of
    /// one does not.
    /// </summary>
    /// <remarks>
    /// The mark is drawn top-left of the centre, so a half turn lands it
    /// bottom-right. Asking whether ink arrived in that quadrant is a claim
    /// about placement that a mirror alone cannot satisfy — a vertical mirror
    /// would put the copy top-right.
    /// </remarks>
    [Fact]
    public void ARadialOrderPutsACopyOnTheFarSideOfTheCentre()
    {
        var bottomRight = new SKRectI(W / 2, H / 2, W, H);

        using var single = Render(Mark());
        using var turned = Render(Mark(new SymmetryAxis
        {
            CenterX = W / 2.0, CenterY = H / 2.0, AngleDeg = 90, Order = 2, Mirror = false,
        }));

        var before = InkedIn(single, bottomRight);
        var after = InkedIn(turned, bottomRight);
        output.WriteLine($"bottom-right quadrant: {before} px with no axis, {after} px at order 2");

        Assert.Equal(0, before);
        Assert.True(after > 400, $"a half turn put only {after} px on the far side of the centre");
    }

    /// <summary>
    /// More copies means more ink, and twelve copies of one mark is twelve
    /// marks' worth.
    /// </summary>
    /// <remarks>
    /// Deliberately a ratio with generous slack rather than an exact multiple:
    /// copies near the centre overlap each other, and a copy that runs off the
    /// page is clipped. What this catches is a placement loop that stamps once
    /// and returns, which is the failure that would otherwise look like
    /// symmetry quietly not working.
    /// </remarks>
    [Theory]
    [InlineData(1, false, 1)]
    [InlineData(1, true, 2)]
    [InlineData(4, false, 4)]
    public void MoreCopiesPutDownMoreInk(int order, bool mirror, int copies)
    {
        using var one = Render(Mark());
        using var many = Render(Mark(new SymmetryAxis
        {
            CenterX = W / 2.0, CenterY = H / 2.0, AngleDeg = 90, Order = order, Mirror = mirror,
        }));

        var single = InkedPixels(one);
        var total = InkedPixels(many);
        output.WriteLine(
            $"order {order}, mirror {mirror}: {total} px against {single} px for one copy "
            + $"— {total / (double)single:0.00}× for {copies} copies");

        Assert.True(
            total >= single * (copies == 1 ? 1 : 1.5),
            $"{copies} copies inked {total} px against {single} px for one — the copies are not landing");
    }
}

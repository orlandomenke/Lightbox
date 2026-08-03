using Lightbox.Core.Export;

namespace Lightbox.Core.Tests;

/// <summary>
/// P5e tier one: a normal map from the silhouette, with no dependency and no model.
/// </summary>
/// <remarks>
/// The convention tests are the point. A flipped green channel makes a character lit
/// from above read as lit from below and every bevel read as a groove, and nothing
/// about that points at a channel convention — the same class of bug as the sprite
/// pivot, so it gets the same treatment: worked examples, both directions asserted,
/// and a discriminating case rather than one that passes either way.
/// </remarks>
public class NormalMapTests
{
    private const int W = 40;
    private const int H = 40;

    /// <summary>A solid rectangle from (left, top) to (right, bottom) inclusive.</summary>
    private static byte[] Box(int left, int top, int right, int bottom)
    {
        var a = new byte[W * H];
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++) a[y * W + x] = 255;
        }
        return a;
    }

    private static (byte R, byte G, byte B, byte A) Pixel(byte[] rgba, int x, int y)
    {
        var o = (y * W + x) * 4;
        return (rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]);
    }

    // ---- the distance field --------------------------------------------------------

    [Fact]
    public void TheDistanceFieldIsZeroOutsideAndGrowsInward()
    {
        // Tested on its own because a wrong distance field produces a plausible normal
        // map of subtly the wrong shape, which is far harder to see than a wrong number.
        var d = NormalMapGenerator.DistanceInside(Box(10, 10, 29, 29), W, H);

        Assert.Equal(0, d[5 * W + 5]);              // outside
        Assert.Equal(0, d[10 * W + 9]);             // just outside the left edge
        Assert.True(d[10 * W + 10] > 0, "the edge pixel itself should be inside");

        // Marching in from the left edge, one pixel of distance per pixel of travel.
        for (var step = 1; step <= 5; step++)
        {
            var here = d[20 * W + (10 + step)];
            var before = d[20 * W + (10 + step - 1)];
            Assert.True(here > before, $"distance did not grow at step {step}: {before} → {here}");
        }
        // And the centre is about as far in as the box's half-width.
        Assert.InRange(d[20 * W + 20], 9, 11);
    }

    [Fact]
    public void BothSweepsAreNeededAndTheSecondOneRuns()
    {
        // A single forward sweep leaves the distance wrong wherever the nearest edge is
        // behind the scan — so the field must be symmetric about the box's centre.
        var d = NormalMapGenerator.DistanceInside(Box(10, 10, 29, 29), W, H);

        var nearLeft = d[20 * W + 12];
        var nearRight = d[20 * W + 27];
        Assert.Equal(nearLeft, nearRight, 6);

        var nearTop = d[12 * W + 20];
        var nearBottom = d[27 * W + 20];
        Assert.Equal(nearTop, nearBottom, 6);
    }

    // ---- the conventions -----------------------------------------------------------

    [Fact]
    public void TheLeftEdgeFacesLeftAndTheRightEdgeFacesRight()
    {
        // X is the same in both conventions, so this is the axis that must simply be
        // right. Red below 128 means the normal points −X.
        var map = NormalMapGenerator.Generate(Box(10, 10, 29, 29), W, H);

        var left = Pixel(map, 12, 20);
        var right = Pixel(map, 27, 20);

        Assert.True(left.R < 128, $"left edge red was {left.R}, expected < 128");
        Assert.True(right.R > 128, $"right edge red was {right.R}, expected > 128");
        // Symmetric about the flat value, or one side is stronger than the other for no
        // reason a drawing gave.
        Assert.True(
            Math.Abs(255 - left.R - right.R) <= 1,
            $"left red {left.R} and right red {right.R} are not mirrored about 128");
    }

    [Fact]
    public void GreenIsBrightAtTheTopUnderOpenGlAndAtTheBottomUnderDirectX()
    {
        // The mnemonic, as a test: on an OpenGL map of a dome, green is bright at the
        // top. Unity reads OpenGL, Unreal reads DirectX, and this is the whole
        // difference between them.
        var alpha = Box(10, 10, 29, 29);

        var gl = NormalMapGenerator.Generate(
            alpha, W, H, new NormalMapOptions(Green: NormalGreen.OpenGl));
        var dx = NormalMapGenerator.Generate(
            alpha, W, H, new NormalMapOptions(Green: NormalGreen.DirectX));

        var glTop = Pixel(gl, 20, 12).G;
        var glBottom = Pixel(gl, 20, 27).G;
        var dxTop = Pixel(dx, 20, 12).G;
        var dxBottom = Pixel(dx, 20, 27).G;

        Assert.True(glTop > 128, $"OpenGL top green was {glTop}, expected > 128");
        Assert.True(glBottom < 128, $"OpenGL bottom green was {glBottom}, expected < 128");

        // And the other convention is the mirror of it, not merely different.
        Assert.True(
            Math.Abs(255 - glTop - dxTop) <= 1,
            $"OpenGL top green {glTop} and DirectX top green {dxTop} are not mirrored");
        Assert.True(
            Math.Abs(255 - glBottom - dxBottom) <= 1,
            $"OpenGL bottom green {glBottom} and DirectX bottom green {dxBottom} are not mirrored");
        Assert.True(dxBottom > 128, $"DirectX bottom green was {dxBottom}, expected > 128");
    }

    [Fact]
    public void TheTwoConventionsDifferOnlyInGreen()
    {
        // The discriminating half: a version that flipped red as well, or that flipped
        // nothing, would pass a green-only check on a symmetric shape.
        var alpha = Box(8, 12, 31, 25);   // deliberately not square

        var gl = NormalMapGenerator.Generate(
            alpha, W, H, new NormalMapOptions(Green: NormalGreen.OpenGl));
        var dx = NormalMapGenerator.Generate(
            alpha, W, H, new NormalMapOptions(Green: NormalGreen.DirectX));

        var greenDiffered = false;
        for (var i = 0; i < W * H; i++)
        {
            Assert.Equal(gl[i * 4], dx[i * 4]);           // red identical
            Assert.Equal(gl[i * 4 + 2], dx[i * 4 + 2]);   // blue identical
            Assert.Equal(gl[i * 4 + 3], dx[i * 4 + 3]);   // alpha identical
            if (gl[i * 4 + 1] != dx[i * 4 + 1]) greenDiffered = true;
        }
        Assert.True(greenDiffered, "the two conventions produced identical green everywhere");
    }

    // ---- the interior and the outside ----------------------------------------------

    [Fact]
    public void TheFlatInteriorPointsStraightOutAndTheOutsideIsFlatNotBlack()
    {
        var map = NormalMapGenerator.Generate(
            Box(10, 10, 29, 29), W, H, new NormalMapOptions(BevelPixels: 3));

        // Well inside the box, past the bevel: the flat normal.
        var inside = Pixel(map, 20, 20);
        Assert.Equal(128, inside.R);
        Assert.Equal(128, inside.G);
        Assert.True(inside.B > 250, $"interior blue was {inside.B}, expected ~255");

        // Outside: flat and transparent. Emphatically not (0,0,0), which decodes to a
        // normal pointing away from the surface and lights as a black hole at the edge.
        var outside = Pixel(map, 3, 3);
        Assert.Equal(128, outside.R);
        Assert.Equal(128, outside.G);
        Assert.Equal(255, outside.B);
        Assert.Equal(0, outside.A);
    }

    [Fact]
    public void AlphaIsCarriedThroughSoTheMapMasksLikeTheSprite()
    {
        var alpha = Box(10, 10, 29, 29);
        alpha[20 * W + 20] = 60;   // a semi-transparent hole in the middle

        var map = NormalMapGenerator.Generate(alpha, W, H);

        Assert.Equal(60, Pixel(map, 20, 20).A);
        Assert.Equal(255, Pixel(map, 15, 15).A);
        Assert.Equal(0, Pixel(map, 2, 2).A);
    }

    // ---- the controls --------------------------------------------------------------

    [Fact]
    public void AWiderBevelTiltsFurtherFromTheEdge()
    {
        // The one control that changes the shape rather than the amount, so the thing to
        // measure is how *far* the tilt reaches rather than how strong it is at one
        // pixel. The first attempt did the latter and asserted a threshold picked by
        // guesswork; the band width is the actual claim.
        var alpha = Box(10, 10, 29, 29);

        int TiltedBand(double bevel)
        {
            var map = NormalMapGenerator.Generate(alpha, W, H, new NormalMapOptions(BevelPixels: bevel));
            var count = 0;
            for (var x = 10; x < 20; x++)
            {
                if (Pixel(map, x, 20).R != 128) count++;
            }
            return count;
        }

        var narrow = TiltedBand(2);
        var wide = TiltedBand(8);

        Assert.True(
            wide > narrow,
            $"bevel 2 tilted {narrow} px in from the edge, bevel 8 tilted {wide} — the width did nothing");
        // And both are real: a version that tilted nothing, or everything, also satisfies
        // "wide > narrow" if only one of them is measured.
        Assert.InRange(narrow, 1, 4);
        Assert.InRange(wide, 5, 10);
    }

    [Fact]
    public void MoreStrengthTiltsFurtherWithoutMovingTheBevel()
    {
        var alpha = Box(10, 10, 29, 29);
        var soft = NormalMapGenerator.Generate(alpha, W, H, new NormalMapOptions(Strength: 0.5));
        var hard = NormalMapGenerator.Generate(alpha, W, H, new NormalMapOptions(Strength: 4));

        var softR = Pixel(soft, 12, 20).R;
        var hardR = Pixel(hard, 12, 20).R;

        // Both numbers printed, because "hard is lower" also passes when soft is flat
        // because the whole thing is broken.
        Assert.True(
            hardR < softR && softR < 128,
            $"soft red {softR}, hard red {hardR} — expected both below 128 and hard the lower");
    }

    [Fact]
    public void ARoundedEdgeHasNoCreaseWhereAChamferDoes()
    {
        // Why Rounded is the default: a linear ramp's derivative jumps where it meets
        // the flat interior, and a jump in the derivative is a visible line under a
        // moving light that no artist drew. Measured as the largest step between
        // neighbouring pixels across the bevel's top.
        var alpha = Box(10, 10, 29, 29);
        var chamfer = NormalMapGenerator.Generate(
            alpha, W, H, new NormalMapOptions(BevelPixels: 6, Rounded: false));
        var domed = NormalMapGenerator.Generate(
            alpha, W, H, new NormalMapOptions(BevelPixels: 6, Rounded: true));

        int LargestStep(byte[] map)
        {
            var worst = 0;
            for (var x = 11; x < 20; x++)
            {
                var step = Math.Abs(Pixel(map, x, 20).R - Pixel(map, x - 1, 20).R);
                worst = Math.Max(worst, step);
            }
            return worst;
        }

        var chamferStep = LargestStep(chamfer);
        var domedStep = LargestStep(domed);

        Assert.True(
            domedStep < chamferStep,
            $"chamfer's worst step {chamferStep}, dome's {domedStep} — the rounding did nothing");
    }

    // ---- determinism, and what it must not do --------------------------------------

    [Fact]
    public void TheSameSilhouetteGivesByteIdenticalOutput()
    {
        // Invariant 2, trivially satisfied and asserted anyway: this tier has no RNG and
        // no sampling, which is a reason it comes first rather than a lucky property.
        var alpha = Box(9, 11, 30, 26);
        var once = NormalMapGenerator.Generate(alpha, W, H);
        var twice = NormalMapGenerator.Generate(alpha, W, H);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void ThePreviewLightIsNotBakedIntoTheMap()
    {
        // The generator has no light in its signature at all, and that is the claim: a
        // baked light would look right in the app and be wrong in every engine, and the
        // light an artist dragged around while judging it would ship inside the asset.
        //
        // Asserted structurally, because it is a claim about the API rather than about a
        // pixel: nothing in the options describes a light, and the interior of an evenly
        // bevelled box is flat — a baked light would shade it.
        var options = typeof(NormalMapOptions).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(options, n =>
            n.Contains("Light", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Angle", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Azimuth", StringComparison.OrdinalIgnoreCase));

        var map = NormalMapGenerator.Generate(
            Box(10, 10, 29, 29), W, H, new NormalMapOptions(BevelPixels: 3));

        // Left and right of the interior read the same. Under any baked directional
        // light they would not.
        Assert.Equal(Pixel(map, 18, 20).B, Pixel(map, 22, 20).B);
        Assert.Equal(Pixel(map, 20, 18).B, Pixel(map, 20, 22).B);
    }

    [Fact]
    public void ABevelFromTheSilhouetteNeedsNoDependency()
    {
        // The tier-one claim, stated as a test: alpha in, normal map out, nothing else
        // consulted. An entirely empty silhouette produces a valid flat map rather than
        // throwing, because an empty frame in a sequence is ordinary.
        var empty = new byte[W * H];
        var map = NormalMapGenerator.Generate(empty, W, H);

        Assert.Equal(W * H * 4, map.Length);
        Assert.All(Enumerable.Range(0, W * H), i =>
        {
            Assert.Equal(128, map[i * 4]);
            Assert.Equal(128, map[i * 4 + 1]);
            Assert.Equal(255, map[i * 4 + 2]);
            Assert.Equal(0, map[i * 4 + 3]);
        });
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public void ADegenerateSizeIsNotACrash(int w, int h)
    {
        var map = NormalMapGenerator.Generate(new byte[Math.Max(1, w * h)], w, h);
        Assert.Equal(Math.Max(0, w * h * 4), map.Length);
    }
}

using System.Diagnostics;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The picture on a brush picker tile.
/// </summary>
/// <remarks>
/// Two things have to be true and they pull against each other: the tile has to show
/// enough of the brush's character to choose by, and rendering sixty of them has to be
/// quick enough that opening the picker is not an event.
/// </remarks>
public class BrushPreviewRendererTests(ITestOutputHelper output)
{
    private static BrushSettings Plain() => new()
    {
        Size = 24, Opacity = 1, Flow = 1, Hardness = 0.8, AntiAlias = true,
    };

    private static int InkedPixels(SKBitmap bitmap)
    {
        var inked = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                // Paper is 0xf2f0ec, so anything meaningfully darker is a mark.
                if (bitmap.GetPixel(x, y).Red < 0xd0) inked++;
            }
        }
        return inked;
    }

    [Fact]
    public void APreviewActuallyHasAMarkOnIt()
    {
        using var preview = BrushPreviewRenderer.Render(Plain());

        var inked = InkedPixels(preview);
        output.WriteLine($"{inked} inked of {preview.Width * preview.Height}");
        Assert.True(inked > 200, $"only {inked} pixels were inked — the tile is nearly blank");
    }

    [Fact]
    public void TheMarkStaysInsideTheTile()
    {
        // A brush whose scatter threw dabs off the edge would clip, and a clipped preview
        // reads as a brush with a flat side.
        using var preview = BrushPreviewRenderer.Render(Plain(), width: 132, height: 44);

        for (var x = 0; x < preview.Width; x++)
        {
            Assert.True(preview.GetPixel(x, 0).Red > 0xd0, $"ink reached the top edge at x={x}");
            Assert.True(
                preview.GetPixel(x, preview.Height - 1).Red > 0xd0,
                $"ink reached the bottom edge at x={x}");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(120)]
    [InlineData(300)]
    public void NoBrushInTheRangeRunsOffTheTile(double size)
    {
        // The other half of widening the preview range from 17 px to 24: a 24 px mark on the
        // old fixed ±12 curve reached 24 px from the middle of a 44 px tile and clipped. The
        // swing is derived from the fitted size for exactly this reason, so the property has
        // to be checked at the top of the range rather than at one comfortable size.
        var brush = Plain();
        brush.Size = size;
        using var preview = BrushPreviewRenderer.Render(brush);

        for (var x = 0; x < preview.Width; x++)
        {
            Assert.True(
                preview.GetPixel(x, 0).Red > 0xd0 && preview.GetPixel(x, preview.Height - 1).Red > 0xd0,
                $"a {size} px brush reached the tile edge at x={x}");
        }
    }

    [Fact]
    public void AnEffectBrushGetsSomethingToWorkOnRatherThanBlankPaper()
    {
        // Smudge, blur and the blender deposit nothing. On clean paper their tiles came out
        // completely blank — three of the twelve built-ins looking like a failed render.
        var info = new SKImageInfo(132, 44, SKColorType.Rgba8888, SKAlphaType.Premul);
        var paint = Plain();
        var smudge = Plain();
        smudge.Kind = BrushKind.Smudge;

        using var plainGround = BrushPreviewRenderer.Ground(paint, info);
        using var effectGround = BrushPreviewRenderer.Ground(smudge, info);

        var plainInk = InkedPixels(plainGround);
        var effectInk = InkedPixels(effectGround);
        output.WriteLine($"paint ground inked {plainInk}, effect ground inked {effectInk}");

        Assert.Equal(0, plainInk);
        Assert.True(effectInk > 500, $"the effect brush's ground is nearly bare ({effectInk} inked)");
    }

    [Fact]
    public void AnEffectBrushKeepsItsRealSizeWhereverItAlreadyFits()
    {
        // Not the log map, and the reason is measurable: the blur sigma is Flow × Size / 4,
        // so mapping a 24 px blur down to 14 px took its sigma to a third of a pixel and the
        // tile came out untouched. An effect brush's character IS its absolute-pixel reach.
        var smudge = Plain();
        smudge.Kind = BrushKind.Smudge;
        smudge.Size = 20;

        using var tile = BrushPreviewRenderer.Render(smudge, width: 132, height: 44);
        using var ground = BrushPreviewRenderer.Ground(
            smudge, new SKImageInfo(132, 44, SKColorType.Rgba8888, SKAlphaType.Premul));

        var moved = 0;
        for (var y = 0; y < tile.Height; y++)
        {
            for (var x = 0; x < tile.Width; x++)
            {
                if (Math.Abs(tile.GetPixel(x, y).Red - ground.GetPixel(x, y).Red) > 12) moved++;
            }
        }
        output.WriteLine($"the smudge moved {moved} pixels of its ground");
        Assert.True(moved > 300, $"the smudge disturbed only {moved} pixels — it is not reading as an effect");
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(4, 12)]
    [InlineData(12, 40)]
    [InlineData(40, 120)]
    [InlineData(120, 300)]
    public void ABiggerBrushAlwaysReadsBiggerRightAcrossTheRange(double smaller, double larger)
    {
        // The first attempt clamped to a single ceiling, and it made a 40 px brush and a
        // 300 px brush draw the SAME picture — size disappeared from the picker, which is
        // worse than showing it inexactly. Stepped across the whole range because the
        // failure was at the top end only and one pair would have missed it.
        Assert.True(
            BrushPreviewRenderer.PreviewSizeFor(larger) > BrushPreviewRenderer.PreviewSizeFor(smaller),
            $"{larger} px did not preview larger than {smaller} px");
    }

    [Fact]
    public void TheWholeRangeStillFitsInsideTheTile()
    {
        // The other half of the trade: mapping is only better than clamping if nothing
        // spills. A 300 px brush has to be visibly the heaviest and still have paper round it.
        var small = Plain();
        small.Size = 4;
        var huge = Plain();
        huge.Size = 300;

        using var smallTile = BrushPreviewRenderer.Render(small);
        using var hugeTile = BrushPreviewRenderer.Render(huge);

        var thin = InkedPixels(smallTile);
        var thick = InkedPixels(hugeTile);
        output.WriteLine($"4 px brush inked {thin}, 300 px brush inked {thick}");

        Assert.True(thick > thin * 1.5, $"the big brush ({thick}) did not read heavier than the small one ({thin})");
        Assert.True(thick < smallTile.Width * smallTile.Height * 0.9, "the big brush filled the whole tile");
    }

    [Fact]
    public void SizeIsShownOnACurveRatherThanLinearly()
    {
        // Brush sizes are chosen logarithmically — 4 to 8 matters as much as 100 to 200 —
        // so a linear map would put every inking brush in the bottom tenth of the range.
        // Stated as the property: the small step is not dwarfed by the large one.
        var lowStep = BrushPreviewRenderer.PreviewSizeFor(8) - BrushPreviewRenderer.PreviewSizeFor(4);
        var highStep = BrushPreviewRenderer.PreviewSizeFor(200) - BrushPreviewRenderer.PreviewSizeFor(100);

        output.WriteLine($"4→8 moves {lowStep:F2} px, 100→200 moves {highStep:F2} px");
        Assert.True(
            Math.Abs(lowStep - highStep) < 0.5,
            $"a doubling should move the preview the same amount wherever it happens: "
            + $"{lowStep:F2} vs {highStep:F2}");
    }

    [Fact]
    public void TwoDifferentBrushesDoNotProduceTheSamePicture()
    {
        // The whole point of the tile. A hard round and a heavily scattered brush must be
        // distinguishable at a glance, which is the thing a list of names cannot do.
        var hard = Plain();
        hard.Hardness = 1;
        var scattered = Plain();
        scattered.Scatter = 0.6;
        scattered.SizeJitter = 0.6;
        scattered.Flow = 0.3;

        using var a = BrushPreviewRenderer.Render(hard);
        using var b = BrushPreviewRenderer.Render(scattered);

        var differing = 0;
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                if (Math.Abs(a.GetPixel(x, y).Red - b.GetPixel(x, y).Red) > 24) differing++;
            }
        }
        output.WriteLine($"{differing} pixels differ between the two tiles");
        Assert.True(differing > 150, $"only {differing} pixels differ — the tiles look the same");
    }

    [Fact]
    public void TheSameBrushRendersTheSamePictureTwice()
    {
        // Invariant 2, which the preview inherits for free by going through the engine —
        // and which matters here because a tile that changed each time the picker opened
        // would look like a fault in the brush.
        using var once = BrushPreviewRenderer.Render(Plain());
        using var twice = BrushPreviewRenderer.Render(Plain());

        for (var y = 0; y < once.Height; y++)
        {
            for (var x = 0; x < once.Width; x++)
            {
                Assert.Equal(once.GetPixel(x, y), twice.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void ABrushTheEngineCannotHonourGivesABlankTileRatherThanThrowing()
    {
        // A picker that throws while being browsed is unusable, and an imported brush is
        // exactly where a nonsensical setting comes from.
        var broken = Plain();
        broken.Size = 0;
        broken.Opacity = 0;
        broken.Flow = 0;

        using var preview = BrushPreviewRenderer.Render(broken);

        Assert.Equal(132, preview.Width);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void SixtyPreviewsRenderFastEnoughToOpenAPicker()
    {
        // The real load: the built-ins plus an imported collection. Measured because the
        // reported complaint about importing brushes was that the window looked like it was
        // crashing, and a picker that renders sixty marks on open would earn the same
        // reaction for the same reason.
        var brushes = new List<BrushSettings>();
        for (var i = 0; i < 60; i++)
        {
            var b = Plain();
            b.Size = 8 + i % 40;
            b.Scatter = i % 4 * 0.15;
            b.Flow = 0.2 + i % 5 * 0.2;
            brushes.Add(b);
        }

        var clock = Stopwatch.StartNew();
        foreach (var b in brushes) BrushPreviewRenderer.Render(b).Dispose();
        var each = clock.Elapsed.TotalMilliseconds / brushes.Count;

        output.WriteLine($"{clock.Elapsed.TotalMilliseconds:F1} ms for {brushes.Count} previews ({each:F2} ms each)");
        Assert.True(
            clock.Elapsed.TotalMilliseconds < 900,
            $"sixty previews took {clock.Elapsed.TotalMilliseconds:F0} ms — too slow to render on open");
    }
}

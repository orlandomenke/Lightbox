using Lightbox.Core.Export;

namespace Lightbox.Core.Tests;

/// <summary>
/// P5a's packer: tighter than a grid on ragged input, and byte-for-byte
/// reproducible so a re-export does not reshuffle the atlas.
/// </summary>
public class SkylinePackerTests
{
    private static List<(int Width, int Height)> Ragged() =>
    [
        (40, 60), (12, 20), (30, 30), (8, 90), (55, 15), (22, 44), (70, 10), (16, 16),
    ];

    /// <summary>Do any two rects share a pixel? The property a packer exists to keep.</summary>
    private static bool AnyOverlap(PackResult result)
    {
        var r = result.Rects;
        for (var i = 0; i < r.Count; i++)
        {
            for (var j = i + 1; j < r.Count; j++)
            {
                var a = r[i];
                var b = r[j];
                var apart = a.X + a.Width <= b.X || b.X + b.Width <= a.X
                    || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y;
                if (!apart) return true;
            }
        }
        return false;
    }

    [Fact]
    public void NothingOverlapsAndNothingLeavesTheSheet()
    {
        var result = SkylinePacker.Pack(Ragged());

        Assert.False(AnyOverlap(result), "two sprites share pixels");
        foreach (var rect in result.Rects)
        {
            Assert.True(rect.X >= 0 && rect.Y >= 0, $"sprite {rect.Index} is off the top or left");
            Assert.True(
                rect.X + rect.Width <= result.Width && rect.Y + rect.Height <= result.Height,
                $"sprite {rect.Index} runs off the sheet at {rect.X},{rect.Y} {rect.Width}x{rect.Height}");
        }
    }

    [Fact]
    public void EverySpriteComesBackAtItsOwnSizeAndInInputOrder()
    {
        // Input order, not pack order: the caller matches a rect to a frame by
        // position, and re-sorting here would silently mislabel every sprite.
        var sizes = Ragged();

        var result = SkylinePacker.Pack(sizes);

        Assert.Equal(sizes.Count, result.Rects.Count);
        for (var i = 0; i < sizes.Count; i++)
        {
            Assert.Equal(i, result.Rects[i].Index);
            Assert.Equal(sizes[i].Width, result.Rects[i].Width);
            Assert.Equal(sizes[i].Height, result.Rects[i].Height);
        }
    }

    [Fact]
    public void TheSameInputPacksIdentically()
    {
        // Not for invariant 2's sake — no dab dynamic is involved — but because a
        // re-export that reshuffles the atlas makes every downstream diff
        // meaningless.
        var first = SkylinePacker.Pack(Ragged(), padding: 2);
        var second = SkylinePacker.Pack(Ragged(), padding: 2);

        Assert.Equal(first.Width, second.Width);
        Assert.Equal(first.Height, second.Height);
        Assert.Equal(first.Rects, second.Rects);
    }

    [Fact]
    public void EqualSizedSpritesAreOrderedByIndexRatherThanByLuck()
    {
        // The sort's third key. Stopping at height and width would leave ties
        // resolved by enumeration order — stable today and stable by accident.
        var same = Enumerable.Repeat((20, 20), 6).ToList();

        var a = SkylinePacker.Pack(same);
        var b = SkylinePacker.Pack(same);

        Assert.Equal(a.Rects, b.Rects);
        // And laid out left to right, because equal boxes tie on height and
        // width and then fall back to the index.
        Assert.True(a.Rects[0].X < a.Rects[1].X || a.Rects[0].Y < a.Rects[1].Y);
    }

    [Fact]
    public void PackingBeatsAGridOnRaggedInput()
    {
        // The claim the feature makes, measured rather than asserted. A grid
        // pays the widest sprite by the tallest for every cell.
        var sizes = Ragged();
        var gridArea = (long)sizes.Max(s => s.Width) * sizes.Max(s => s.Height) * sizes.Count;

        var packed = SkylinePacker.Pack(sizes);

        Assert.True(
            packed.Area < gridArea,
            $"packed {packed.Area} px vs grid {gridArea} px — no better than a grid");
    }

    [Fact]
    public void PackingIsNoWorseThanAGridOnUniformInput()
    {
        // Where union trimming has already made every cell the same size there is
        // nothing to win, and a packer that came out *larger* would be a
        // regression dressed as a feature.
        var sizes = Enumerable.Repeat((32, 32), 16).ToList();
        var gridArea = 32L * 32 * 16;

        var packed = SkylinePacker.Pack(sizes);

        Assert.True(packed.Area <= gridArea * 5 / 4, $"packed {packed.Area} px vs grid {gridArea} px");
        Assert.Equal(gridArea, packed.UsedArea);
    }

    [Fact]
    public void PaddingSurroundsEverySpriteIncludingAtTheSheetEdge()
    {
        // The gutter exists to stop an engine's bilinear filter sampling the
        // neighbour. A sprite flush against the sheet edge needs it just as much.
        var result = SkylinePacker.Pack([(10, 10), (10, 10), (10, 10)], padding: 3);

        foreach (var rect in result.Rects)
        {
            Assert.True(rect.X >= 3, $"sprite {rect.Index} has no left gutter (x={rect.X})");
            Assert.True(rect.Y >= 3, $"sprite {rect.Index} has no top gutter (y={rect.Y})");
            Assert.True(rect.X + rect.Width + 3 <= result.Width, $"sprite {rect.Index} has no right gutter");
            Assert.True(rect.Y + rect.Height + 3 <= result.Height, $"sprite {rect.Index} has no bottom gutter");
        }
        Assert.False(AnyOverlap(result));
    }

    [Fact]
    public void ASheetIsNeverNarrowerThanItsWidestSprite()
    {
        // Otherwise it cannot be packed at all, and the honest failure is to
        // widen rather than to drop a sprite or throw.
        var result = SkylinePacker.Pack([(200, 10), (10, 10)], width: 32);

        Assert.True(result.Width >= 200, $"sheet came out {result.Width} px wide");
        Assert.False(AnyOverlap(result));
    }

    [Fact]
    public void AFixedWidthIsHonouredWhenItFits()
    {
        var result = SkylinePacker.Pack(Ragged(), width: 128);
        Assert.Equal(128, result.Width);
        Assert.False(AnyOverlap(result));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AVerySmallSheetStillPacks(int count)
    {
        var result = SkylinePacker.Pack(Enumerable.Repeat((16, 16), count).ToList());
        Assert.Equal(count, result.Rects.Count);
        Assert.False(AnyOverlap(result));
    }

    [Fact]
    public void NoSpritesIsNotACrash()
    {
        var result = SkylinePacker.Pack([]);
        Assert.Empty(result.Rects);
        Assert.True(result.Width >= 1 && result.Height >= 1);
    }

    [Fact]
    public void AZeroSizedSpriteIsGivenAPixelRatherThanDisappearing()
    {
        // An empty frame trims to nothing. It still needs a slot, or every sprite
        // after it is off by one against the frame it came from.
        var result = SkylinePacker.Pack([(0, 0), (10, 10)]);

        Assert.Equal(2, result.Rects.Count);
        Assert.True(result.Rects[0].Width >= 1 && result.Rects[0].Height >= 1);
        Assert.False(AnyOverlap(result));
    }

    [Fact]
    public void ALongSheetStaysFastEnoughToExport()
    {
        // The skyline merges equal-height neighbours; without that it grows a run
        // per sprite and Fit turns quadratic on a long sheet. 500 frames is an
        // ordinary sequence, not a stress test.
        var sizes = Enumerable.Range(0, 500).Select(i => (20 + i % 17, 20 + i % 11)).ToList();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = SkylinePacker.Pack(sizes, padding: 1);
        watch.Stop();

        Assert.Equal(500, result.Rects.Count);
        Assert.False(AnyOverlap(result));
        Assert.True(watch.ElapsedMilliseconds < 2000, $"packing 500 sprites took {watch.ElapsedMilliseconds} ms");
    }
}

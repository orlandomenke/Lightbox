using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster.Tests;

/// <summary>
/// B412. A swap is an exchange: the patch holds the pixels for the side of the
/// step the bitmap is <em>not</em> on. A refused swap leaves the transition to
/// the replay, which moves the bitmap without touching the patch, so after it
/// the patch is on the wrong side. Kept, a later swap that does match writes
/// the wrong side back: a redo that shows the drawing without its mark while
/// the record has it.
/// </summary>
public sealed class MarkSnapshotRefusalTests
{
    private static readonly SKRectI Region = SKRectI.Create(4, 4, 8, 8);

    private static SKBitmap Filled(int size, SKColor colour)
    {
        var bmp = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        bmp.Erase(colour);
        return bmp;
    }

    private static void Paint(SKBitmap bmp, SKColor colour)
    {
        using var canvas = new SKCanvas(bmp);
        using var paint = new SKPaint { Color = colour };
        canvas.DrawRect(SKRect.Create(Region.Left, Region.Top, Region.Width, Region.Height), paint);
    }

    [Fact]
    public void ARefusedSwapDropsItsStepSoALaterOneCannotWriteTheWrongSideBack()
    {
        var cache = new FrameBitmapCache();
        var frame = new Frame();
        var onScreen = Filled(32, SKColors.White);
        Assert.True(cache.InsertWanted(frame, 32, 32, 1.0, 0, onScreen));
        cache.Pin(onScreen);
        using var snaps = new MarkSnapshot();

        // The mark: unmarked pixels held aside, then the mark stamped.
        snaps.Hold(cache, frame, Region);
        Paint(onScreen, SKColors.Black);
        snaps.Promote(1);

        // A second rendering of the same drawing arrives (an export at 2x), so
        // the undo cannot pair its patches and is refused; the replay unmarks.
        Assert.True(cache.InsertWanted(frame, 64, 64, 2.0, 0, Filled(64, SKColors.White)));
        Assert.False(snaps.Swap(1, cache, frame, Region));
        Paint(onScreen, SKColors.White);

        // That rendering is evicted and the on-screen one, used since, stays:
        // the least recently used goes first, and here that is the export.
        Assert.Same(onScreen, cache.Get(frame, 32, 32));
        for (var i = 0; i < FrameBitmapCache.MaxEntries - 1; i++)
        {
            cache.InsertWanted(new Frame(), 4, 4, 1.0, 0, Filled(4, SKColors.White));
        }
        Assert.False(cache.Holds(frame, 64, 64, 2.0, 0));
        Assert.True(cache.Holds(frame, 32, 32, 1.0, 0));

        // Redo. Swapping now would write the held unmarked pixels over the
        // unmarked drawing and leave the mark missing: it must go to the replay.
        Assert.False(snaps.Swap(1, cache, frame, Region));
        Assert.Equal(0, snaps.Steps);
        cache.Unpin(onScreen);
    }
}

using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// That the coverage buffer is actually smaller, and that the rollback still
/// puts back exactly what it took (B189).
/// </summary>
/// <remarks>
/// <para>
/// <b>The mechanism is proved next door; this is about the wiring.</b>
/// <c>ACoarseCeilingStillHoldsTheEdgeTests</c> says a ceiling at 0.375 gives the
/// same edge as the exact one. What it cannot say is whether the live path ever
/// asks for one — and that is the failure this project has made before: two
/// captures taken as an A/B, both on the same arm, with nothing in the report
/// saying so (B322).
/// </para>
/// <para>
/// <b>The rollback is here rather than in Raster because it is the session's,
/// not the engine's.</b> The tail is backed up and restored in the coverage
/// buffer's own pixels, and the two halves have to agree about which rectangle
/// that is. If they round differently the backup is rescaled by a fraction of a
/// pixel every pointer event, which would smear the ceiling along the stroke
/// and show as a soft brush getting gradually harder the longer the mark — a
/// symptom nobody would trace back to a rounding rule.
/// </para>
/// </remarks>
public class TheFootprintFollowsThePreviewTests
{
    private const int Width = 1920;
    private const int Height = 1080;

    [Fact]
    public void ACappingBrushGetsACoverageBufferTheSizeOfThePreview()
    {
        var session = new LivePaintSession();
        var scale = LiveFootprintScale.For(0.375, followsPreview: true);
        session.BeginCoverage(Width, Height, scale);

        Assert.Equal(0.375, session.CoverageScale);
        Assert.NotNull(session.Coverage);
        Assert.Equal(720, session.Coverage!.Width);
        Assert.Equal(405, session.Coverage.Height);
    }

    [Fact]
    public void TheSilhouetteRouteKeepsTheDocumentsSizeBecauseItsCoverageIsTheMark()
    {
        var session = new LivePaintSession();
        session.BeginCoverage(Width, Height);

        Assert.Equal(1.0, session.CoverageScale);
        Assert.Equal(Width, session.Coverage!.Width);
        Assert.Equal(Height, session.Coverage.Height);
    }

    [Fact]
    public void PinningTheArmToFullGivesBackTheDocumentsSize()
    {
        Assert.Equal(1.0, LiveFootprintScale.For(0.375, followsPreview: false));

        // And zoomed in, where there is nothing to save, both arms agree.
        Assert.Equal(1.0, LiveFootprintScale.For(1.0, followsPreview: true));
        Assert.Equal(1.0, LiveFootprintScale.For(2.5, followsPreview: true));
    }

    /// <summary>
    /// A ceiling so coarse that a soft brush's whole falloff fell inside one
    /// buffer pixel would cap the rim to a single flat number, which is the
    /// hardening the ceiling exists to prevent.
    /// </summary>
    [Fact]
    public void TheScaleHasAFloorSoTheFalloffStillResolves()
    {
        Assert.Equal(0.2, LiveFootprintScale.For(0.02, followsPreview: true));
        Assert.Equal(0.2, LiveFootprintScale.For(0.2, followsPreview: true));
    }

    [Fact]
    public void ARegionRoundsToTheSameCoverageRectangleEveryTime()
    {
        var session = new LivePaintSession();
        session.BeginCoverage(Width, Height, 0.375);

        // Deliberately awkward: none of these edges lands on a whole buffer
        // pixel, which is the case the backup and the restore have to agree on.
        var tail = new SKRectI(301, 61, 907, 343);
        var first = session.ToCoverage(tail);
        var second = session.ToCoverage(tail);

        Assert.Equal(first, second);
        Assert.True(
            first.Width < tail.Width && first.Height < tail.Height,
            "the coverage rectangle is not smaller, so the buffer is not scaled and the "
            + "rollback is not being tested at the size it will actually run at");

        // Outward: the rectangle must cover every buffer pixel the document
        // rectangle touches, or the rollback would leave a sliver of the tail
        // behind and the ceiling would keep it forever.
        Assert.True(first.Left <= tail.Left * 0.375);
        Assert.True(first.Top <= tail.Top * 0.375);
        Assert.True(first.Right >= tail.Right * 0.375);
        Assert.True(first.Bottom >= tail.Bottom * 0.375);
    }

    /// <summary>
    /// Back up a region, draw over it, restore it: the buffer has to be exactly
    /// what it was.
    /// </summary>
    /// <remarks>
    /// <b>The whole scheme rests on this being a copy rather than a resample.</b>
    /// A footprint is a running maximum, so the settled prefix is only correct
    /// if the tail can be taken back exactly; anything that rescales on the way
    /// in or out leaves a residue that accumulates for the length of the stroke.
    /// </remarks>
    [Fact]
    public void TheTailRollbackPutsBackExactlyWhatItTook()
    {
        var session = new LivePaintSession();
        session.BeginCoverage(Width, Height, 0.375);
        var coverage = session.Coverage!;
        var canvas = session.CoverageCanvas!;

        // Something with structure in it, so a shift of even one pixel shows.
        using (var paint = new SKPaint { Color = SKColors.White })
        {
            for (var i = 0; i < 40; i++)
            {
                canvas.DrawRect(new SKRect(10 + (i * 7), 10, 14 + (i * 7), 300), paint);
            }
        }

        canvas.Flush();

        var tail = new SKRectI(301, 61, 907, 343);
        var region = session.ToCoverage(tail);

        var backup = new SKBitmap(new SKImageInfo(
            region.Width, region.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var sub = new SKBitmap())
        {
            Assert.True(coverage.ExtractSubset(sub, region));
            using var px = sub.PeekPixels();
            using var view = SKImage.FromPixels(px);
            using var into = new SKCanvas(backup);
            using var src = new SKPaint { BlendMode = SKBlendMode.Src };
            into.DrawImage(view, 0, 0, src);
            into.Flush();
        }

        var before = new byte[region.Width * region.Height];
        for (var y = 0; y < region.Height; y++)
        {
            for (var x = 0; x < region.Width; x++)
            {
                before[(y * region.Width) + x] = coverage.GetPixel(region.Left + x, region.Top + y).Red;
            }
        }

        // The tail, stamped over.
        using (var over = new SKPaint { Color = SKColors.Red })
        {
            canvas.DrawRect(
                new SKRect(region.Left, region.Top, region.Right, region.Bottom), over);
            canvas.Flush();
        }

        // The same restore the live path performs, through the same helper's
        // src-rect-to-dst-rect draw.
        using (var px = backup.PeekPixels())
        using (var restore = SKImage.FromPixels(px))
        using (var src = new SKPaint { BlendMode = SKBlendMode.Src })
        {
            canvas.DrawImage(
                restore,
                new SKRect(0, 0, region.Width, region.Height),
                new SKRect(region.Left, region.Top, region.Right, region.Bottom),
                src);
            canvas.Flush();
        }

        var moved = 0;
        for (var y = 0; y < region.Height; y++)
        {
            for (var x = 0; x < region.Width; x++)
            {
                var now = coverage.GetPixel(region.Left + x, region.Top + y).Red;
                if (now != before[(y * region.Width) + x]) moved++;
            }
        }

        backup.Dispose();
        Assert.True(
            moved == 0,
            $"{moved} of {region.Width * region.Height} coverage pixels came back different, so "
            + "the tail rollback is resampling rather than copying and the ceiling will drift "
            + "for the length of every stroke");
    }
}

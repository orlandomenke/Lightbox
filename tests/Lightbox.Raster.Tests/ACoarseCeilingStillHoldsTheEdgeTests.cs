using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// What a soft brush's edge looks like when the footprint capping it is
/// accumulated at preview resolution rather than the document's (B189).
/// </summary>
/// <remarks>
/// <para>
/// <b>The footprint exists to stop exactly one thing.</b> Dabs are stepped far
/// tighter than their own falloff, so alpha saturates down the middle of a
/// stroke and reaches the rim only once or twice — a soft brush comes out
/// harder than a single dab of it. Q157 measured the loss at <b>45-50%</b> of
/// the edge width at hardness 0.10-0.35, and chose a running-maximum ceiling to
/// undo it.
/// </para>
/// <para>
/// <b>So the only question this scaling raises is whether the edge survives.</b>
/// The saving is not in doubt — <c>FootprintCostsAsMuchAsTheMarkTests</c> puts
/// it at 4.1x on the walk and 39% of an event's stamping. What is in doubt is
/// whether a ceiling reconstructed from samples 2.67 document pixels apart
/// still holds the rim, or whether it gives back the hardening the ceiling was
/// built to prevent.
/// </para>
/// <para>
/// <b>The comparison is three-way on purpose.</b> Against the exact ceiling
/// alone, any difference looks like damage; against the uncapped mark alone,
/// any capping at all looks like success. The number that means something is
/// where the coarse ceiling sits <em>between</em> them.
/// </para>
/// </remarks>
public class ACoarseCeilingStillHoldsTheEdgeTests(ITestOutputHelper output)
{
    private const int Width = 1200;
    private const int Height = 400;

    /// <summary>The compose scale the owner draws at, fit-to-window at 4K.</summary>
    private const double PreviewScale = 0.375;

    /// <summary>
    /// Hardness 0.35 and flow 1: the settings Q157 measured the worst loss at,
    /// and the flow at which a ceiling binds everywhere off-centre rather than
    /// only at the rim.
    /// </summary>
    private static BrushSettings Soft() => new()
    {
        Size = 70, Hardness = 0.35, Flow = 1.0, Opacity = 1,
    };

    /// <summary>A straight horizontal stroke, so a vertical cut is a clean profile.</summary>
    private static Stroke Straight(BrushSettings brush)
    {
        var pts = new List<StrokePoint>();
        for (double x = 200; x <= 1000; x += 4.2) pts.Add(new StrokePoint(x, Height / 2.0, 1));
        return new Stroke { Tool = ToolKind.Brush, Color = "#000000", Brush = brush, Points = pts };
    }

    private static SKBitmap Mark(Stroke stroke, IReadOnlyList<BrushEngine.Dab> dabs)
    {
        var bmp = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        BrushEngine.StampDabRange(canvas, stroke, dabs, 0, dabs.Count);
        canvas.Flush();
        return bmp;
    }

    private static SKBitmap Footprint(Stroke stroke, IReadOnlyList<BrushEngine.Dab> dabs, double scale)
    {
        var (w, h) = FootprintSpace.BufferSize(Width, Height, scale);
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        BrushEngine.AccumulateFootprint(canvas, stroke, dabs, 0, dabs.Count, scale);
        canvas.Flush();
        return bmp;
    }

    /// <summary>Alpha down a vertical cut through the middle of the stroke.</summary>
    private static byte[] Profile(SKBitmap mark, int x)
    {
        var column = new byte[Height];
        for (var y = 0; y < Height; y++) column[y] = mark.GetPixel(x, y).Alpha;
        return column;
    }

    /// <summary>
    /// How many pixels the edge takes to climb from nearly nothing to most of
    /// its peak — the quantity Q157 tabulated, measured on one side of the cut.
    /// </summary>
    private static int EdgeWidth(byte[] column)
    {
        var peak = column.Max();
        if (peak == 0) return 0;
        int high = -1, low = -1;
        for (var y = 0; y < column.Length; y++)
        {
            if (low < 0 && column[y] >= peak * 0.1) low = y;
            if (high < 0 && column[y] >= peak * 0.9) high = y;
        }

        return high < 0 || low < 0 ? 0 : high - low;
    }

    [Fact]
    public void ThePreviewCeilingKeepsTheEdgeTheExactOneGives()
    {
        var brush = Soft();
        Assert.True(
            BrushEngine.NeedsFootprintCap(brush),
            "this brush is not capped at all, so the test measures nothing");

        var stroke = Straight(brush);
        var dabs = BrushEngine.WalkDabs(stroke);
        var band = new SKRectI(0, 0, Width, Height);

        using var uncapped = Mark(stroke, dabs);
        using var exact = Mark(stroke, dabs);
        using var coarse = Mark(stroke, dabs);

        using (var full = Footprint(stroke, dabs, 1.0))
        {
            BrushEngine.CapToFootprintBand(exact, full, band);
        }

        using (var small = Footprint(stroke, dabs, PreviewScale))
        {
            Assert.True(
                small.Width < Width,
                "the preview footprint is not actually smaller, so nothing was saved and "
                + "nothing is being tested");
            BrushEngine.CapToFootprintBand(
                coarse, small, band, new FootprintSpace(PreviewScale, 0, 0));
        }

        // The single dab this brush is supposed to keep the shape of: the
        // reference Q157 measured against, not an opinion about what is soft.
        using var oneDab = new SKBitmap(
            new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(oneDab))
        {
            canvas.Clear(SKColors.Transparent);
            var single = new Stroke
            {
                Tool = ToolKind.Brush, Color = "#000000", Brush = brush,
                Points = [new StrokePoint(600, Height / 2.0, 1)],
            };
            var one = BrushEngine.WalkDabs(single);
            BrushEngine.StampDabRange(canvas, single, one, 0, one.Count);
            canvas.Flush();
        }

        const int Cut = 600;
        var dabEdge = EdgeWidth(Profile(oneDab, Cut));
        var rawEdge = EdgeWidth(Profile(uncapped, Cut));
        var exactEdge = EdgeWidth(Profile(exact, Cut));
        var coarseEdge = EdgeWidth(Profile(coarse, Cut));

        output.WriteLine($"  edge width at hardness {brush.Hardness}, size {brush.Size}:");
        output.WriteLine($"    one dab, which is the shape to keep   {dabEdge,4} px");
        output.WriteLine($"    the stroke, uncapped                  {rawEdge,4} px");
        output.WriteLine($"    capped by the exact footprint         {exactEdge,4} px");
        output.WriteLine($"    capped at {PreviewScale} of that            {coarseEdge,4} px");

        // Alpha, everywhere the mark is, against the exact ceiling — and against
        // no ceiling at all, which is what the number has to be read next to.
        double sumCoarse = 0, sumRaw = 0;
        int worst = 0, counted = 0;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var e = exact.GetPixel(x, y).Alpha;
                var c = coarse.GetPixel(x, y).Alpha;
                var r = uncapped.GetPixel(x, y).Alpha;
                if (e == 0 && c == 0 && r == 0) continue;
                counted++;
                sumCoarse += Math.Abs(c - e);
                sumRaw += Math.Abs(r - e);
                worst = Math.Max(worst, Math.Abs(c - e));
            }
        }

        var meanCoarse = sumCoarse / Math.Max(1, counted);
        var meanRaw = sumRaw / Math.Max(1, counted);
        output.WriteLine("");
        output.WriteLine($"  against the exact ceiling, over {counted} pixels of mark:");
        output.WriteLine($"    the {PreviewScale} ceiling is off by  {meanCoarse,7:0.##} alpha on average, {worst} at worst");
        output.WriteLine($"    no ceiling at all is off by     {meanRaw,7:0.##} alpha on average");
        output.WriteLine($"    so it recovers {100 * (1 - (meanCoarse / meanRaw)):0.#}% of what the exact ceiling does");

        // **The claim.** The edge is what the ceiling is for, so the edge is what
        // is asserted — within a pixel of the exact one, and nothing like the
        // uncapped stroke's.
        Assert.True(
            Math.Abs(coarseEdge - exactEdge) <= 1,
            $"the preview ceiling gives a {coarseEdge} px edge where the exact one gives "
            + $"{exactEdge} px, so scaling the footprint changes the very thing it exists "
            + "to protect and this is not a preview-only trade after all");

        Assert.True(
            rawEdge < exactEdge,
            $"the uncapped stroke's edge is {rawEdge} px against the capped {exactEdge} px, so "
            + "this brush does not harden without a ceiling and the whole comparison is "
            + "measuring noise — pick a softer one");

        // **Twenty, not five, and the number was measured rather than picked.**
        // Two ways of getting the reconstruction wrong were tried on purpose and
        // both still cleared a fifth: reading the nearest footprint pixel instead
        // of interpolating gives 9.32 alpha (81.6% recovered), and dropping the
        // half-pixel that centres the sample gives 5.82 (88.5%). Bilinear, done
        // right, gives 0.35 — so a threshold that admits the first two is not
        // guarding the thing this file is about.
        Assert.True(
            meanCoarse < meanRaw / 20,
            $"the preview ceiling is off by {meanCoarse:0.##} alpha where having no ceiling is "
            + $"off by {meanRaw:0.##}. Interpolating correctly measures 0.35 here; nearest "
            + "neighbour measures 9.32 and a dropped half-pixel 5.82, so a number in that "
            + "range means the reconstruction is wrong rather than merely coarse");
    }

    /// <summary>
    /// The same ceiling, read through a crop whose left edge does not land on a
    /// whole footprint pixel.
    /// </summary>
    /// <remarks>
    /// <b>This is the half with no visible symptom until it has one.</b> The
    /// live post-process hands its worker a crop of the coverage buffer, and a
    /// crop can only start on a whole pixel — so at 0.375 the remainder is worth
    /// up to 2.67 document pixels of ceiling, sliding the cap off the rim it is
    /// meant to sit on. The offset travels with the crop for that reason, and
    /// this is what says so.
    /// </remarks>
    [Fact]
    public void ACroppedCeilingLandsWhereTheUncroppedOneDid()
    {
        var brush = Soft();
        var stroke = Straight(brush);
        var dabs = BrushEngine.WalkDabs(stroke);

        using var whole = Footprint(stroke, dabs, PreviewScale);

        // A region whose left and top are deliberately awkward: 301 * 0.375 is
        // 112.875, so the crop starts at 112 and owes 0.875 of a pixel.
        var region = new SKRectI(301, 61, 901, 341);
        var cropLeft = (int)Math.Floor(region.Left * PreviewScale);
        var cropTop = (int)Math.Floor(region.Top * PreviewScale);
        var cropRight = (int)Math.Ceiling(region.Right * PreviewScale);
        var cropBottom = (int)Math.Ceiling(region.Bottom * PreviewScale);

        using var crop = new SKBitmap(new SKImageInfo(
            cropRight - cropLeft, cropBottom - cropTop, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(crop))
        using (var sub = new SKBitmap())
        {
            Assert.True(whole.ExtractSubset(
                sub, new SKRectI(cropLeft, cropTop, cropRight, cropBottom)));
            using var px = sub.PeekPixels();
            using var view = SKImage.FromPixels(px);
            using var replace = new SKPaint { BlendMode = SKBlendMode.Src };
            canvas.DrawImage(view, 0, 0, replace);
            canvas.Flush();
        }

        var offsetX = (region.Left * PreviewScale) - cropLeft;
        var offsetY = (region.Top * PreviewScale) - cropTop;
        Assert.True(offsetX > 0.5, "the region chosen does not actually straddle a pixel");

        using var wholePix = whole.PeekPixels();
        using var cropPix = crop.PeekPixels();
        var wholeSpan = wholePix.GetPixelSpan<byte>();
        var cropSpan = cropPix.GetPixelSpan<byte>();

        var uncropped = new FootprintSpace(PreviewScale, 0, 0);
        var cropped = new FootprintSpace(PreviewScale, offsetX, offsetY);
        var dropped = new FootprintSpace(PreviewScale, 0, 0); // the offset thrown away

        int worstKept = 0, worstDropped = 0;
        for (var y = 0; y < region.Height; y++)
        {
            for (var x = 0; x < region.Width; x++)
            {
                var truth = uncropped.CeilingAt(
                    wholeSpan, wholePix.RowBytes, whole.Width, whole.Height,
                    region.Left + x, region.Top + y);
                var kept = cropped.CeilingAt(
                    cropSpan, cropPix.RowBytes, crop.Width, crop.Height, x, y);
                var lost = dropped.CeilingAt(
                    cropSpan, cropPix.RowBytes, crop.Width, crop.Height, x, y);
                worstKept = Math.Max(worstKept, Math.Abs(kept - truth));
                worstDropped = Math.Max(worstDropped, Math.Abs(lost - truth));
            }
        }

        output.WriteLine($"  crop owes {offsetX:0.###} x {offsetY:0.###} of a footprint pixel");
        output.WriteLine($"    carrying the offset, worst ceiling error  {worstKept,4}");
        output.WriteLine($"    dropping it,         worst ceiling error  {worstDropped,4}");

        Assert.True(
            worstKept <= 1,
            $"a cropped ceiling that carries its offset is still off by {worstKept}, so the "
            + "crop arithmetic is wrong rather than merely lossy");

        Assert.True(
            worstDropped > worstKept,
            $"dropping the sub-pixel offset costs nothing here ({worstDropped} against "
            + $"{worstKept}), so this test is not exercising the thing it was written for");
    }
}

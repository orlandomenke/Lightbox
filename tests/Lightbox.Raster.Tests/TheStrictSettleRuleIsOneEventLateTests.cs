using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// That the exact settle rule holds a dab provisional for one event after it
/// has stopped moving, and what settling on a near miss instead costs (B189).
/// </summary>
/// <remarks>
/// <para>
/// <b>This file was written expecting to price a trade, and there is no trade
/// to price.</b> The expectation was that settling a dab within a quarter pixel
/// of its previous position would leave the preview a fraction off the record
/// until the pen lifted, and that the question was whether anyone could see it.
/// Measured, the answer is that <b>no pixel changes and no dab is frozen off
/// the record at all</b> — at either size, over the whole mark.
/// </para>
/// <para>
/// <b>The reason is in <c>StableCount</c>'s own doc comment, one level down from
/// where it was being read.</b> <c>Densify</c> looks one point ahead, so a dab is
/// final as soon as the next point arrives. The exact rule cannot observe that
/// directly; it can only notice two consecutive walks agreeing to the bit, which
/// happens one event <em>after</em> the dab stopped moving. So the provisional
/// tail is carrying a dab that is already finished, and re-stamping it every
/// event buys nothing whatsoever.
/// </para>
/// <para>
/// <b>What it is worth.</b> Over a thirty-event stroke the exact rule re-stamps
/// <b>45</b> dabs at size 500 and <b>320</b> at size 70; settling on a near miss
/// re-stamps <b>6</b> and <b>49</b>. A dab at size 500 costs <b>1245 us</b>
/// (<c>WhatOneEventCostsAtEachBrushSizeTests</c>) and the colour stamp is 81% of
/// an event there, which is what the owner feels as the jump at large sizes.
/// </para>
/// <para>
/// <b>Zero measured is not zero guaranteed, and both are asserted.</b> Nothing
/// promises a stroke whose densification has not converged after one further
/// point, so the bound the predicate gives — a settled dab is within the
/// tolerance of where the record puts it — is checked as well as the pixels.
/// </para>
/// <para>
/// <b>Both previews are built the way the live path builds one</b>: walk, settle
/// a prefix permanently, take the tail back, stamp it again. Anything simpler
/// would compare two whole-stroke renders and never exercise the settling at
/// all, which is the entire mechanism under test.
/// </para>
/// </remarks>
public class TheStrictSettleRuleIsOneEventLateTests(ITestOutputHelper output)
{
    private const int Width = 1400;
    private const int Height = 1000;
    private const double TravelPerEvent = 60;

    private static BrushSettings Soft(double size) => new()
    {
        Size = size, Hardness = 0.35, Flow = 0.7, Opacity = 1,
    };

    private static Stroke After(BrushSettings brush, int events)
    {
        var pts = new List<StrokePoint>();
        double x = 300, y = 500, heading = -0.2;
        for (var i = 0; i <= events; i++)
        {
            pts.Add(new StrokePoint(x, y, 1));
            heading += 0.03;
            x += TravelPerEvent * Math.Cos(heading);
            y += TravelPerEvent * Math.Sin(heading);
        }

        return new Stroke
        {
            Tool = ToolKind.Brush, Color = "#203040", Brush = brush, Points = pts,
        };
    }

    /// <summary>
    /// The live preview after <paramref name="events"/> pointer events, built
    /// dab by dab the way the live path builds one.
    /// </summary>
    /// <summary>
    /// What building one preview cost and what it got wrong.
    /// </summary>
    /// <param name="Restamps">
    /// Dabs taken back and drawn again across the whole stroke — the work the
    /// tolerance exists to remove, and the only honest way to say the two arms
    /// behaved differently at all.
    /// </param>
    /// <param name="FrozenOffRecord">
    /// Dabs stamped at a position a later walk then moved — the error the artist
    /// would see until the pen lifts.
    /// </param>
    /// <param name="WorstOffRecord">The furthest any of them ended up, in pixels.</param>
    private readonly record struct Built(int Restamps, int FrozenOffRecord, double WorstOffRecord);

    private static (SKBitmap Image, Built Stats) Preview(
        BrushSettings brush, int events, double tolerance)
    {
        var bmp = new SKBitmap(
            new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);

        var densify = new IncrementalDensify();
        List<BrushEngine.Dab>? previous = null;
        var settled = 0;
        Stroke? stroke = null;
        List<BrushEngine.Dab> dabs = [];
        var frozenAt = new List<SKPoint>();
        var restamps = 0;

        for (var e = 2; e <= events; e++)
        {
            stroke = After(brush, e);
            dabs = [.. BrushEngine.WalkDabs(stroke, densify)];
            var stable = BrushEngine.StableCount(dabs, previous, tolerance);
            var cut = Math.Max(settled, Math.Min(stable, dabs.Count));

            // Everything newly settled, stamped once and never taken back — the
            // step that makes the two arms differ, because under the tolerance a
            // dab is frozen at a position the next walk will move slightly.
            BrushEngine.StampDabRange(canvas, stroke, dabs, settled, cut);
            frozenAt.AddRange(dabs.GetRange(settled, cut - settled).Select(d => d.Pos));

            // Everything past the cut is taken back next event and drawn again.
            // Counted here rather than inferred from the cut, because that is
            // the quantity the saving is made of.
            if (previous is not null) restamps += Math.Max(0, previous.Count - cut);
            settled = cut;
            previous = dabs;
        }

        // The provisional tail, which the live path re-stamps every event and
        // which is present in the published frame exactly once.
        if (stroke is not null) BrushEngine.StampDabRange(canvas, stroke, dabs, settled, dabs.Count);
        canvas.Flush();

        // Against the record's own answer: the final walk is what the commit
        // will render, so anything frozen away from it is exactly the error the
        // artist sees until they lift the pen.
        int moved = 0;
        double worst = 0;
        for (var i = 0; i < Math.Min(frozenAt.Count, dabs.Count); i++)
        {
            var d = Math.Sqrt(
                Math.Pow(frozenAt[i].X - dabs[i].Pos.X, 2)
                + Math.Pow(frozenAt[i].Y - dabs[i].Pos.Y, 2));
            if (d > 0) moved++;
            if (d > worst) worst = d;
        }

        return (bmp, new Built(restamps, moved, worst));
    }

    [Theory]
    [InlineData(70)]
    [InlineData(500)]
    public void SettlingOnANearMissChangesNoPixelAtAll(double size)
    {
        var brush = Soft(size);
        var tolerance = BrushEngine.SettleTolerance(brush);
        Assert.True(tolerance > 0, "this brush gets no tolerance, so nothing differs and nothing is tested");

        var (strict, strictStats) = Preview(brush, 30, 0);
        var (settled, settledStats) = Preview(brush, 30, tolerance);
        using var strictImage = strict;
        using var settledImage = settled;

        output.WriteLine($"  size {size}, tolerance {tolerance} px:");
        output.WriteLine(
            $"    the strict rule    re-stamped {strictStats.Restamps,5} dabs,"
            + $" froze {strictStats.FrozenOffRecord} off the record"
            + $" (worst {strictStats.WorstOffRecord:0.####} px)");
        output.WriteLine(
            $"    settling early     re-stamped {settledStats.Restamps,5} dabs,"
            + $" froze {settledStats.FrozenOffRecord} off the record"
            + $" (worst {settledStats.WorstOffRecord:0.####} px)");

        // **The test is inert unless the two arms actually behaved
        // differently.** A pixel comparison that reports no difference looks
        // exactly the same whether the tolerance changed nothing visible or was
        // never in play, and the second is the likelier bug.
        Assert.True(
            settledStats.Restamps < strictStats.Restamps,
            $"settling early re-stamped {settledStats.Restamps} dabs against the strict rule's "
            + $"{strictStats.Restamps}, so the two arms did the same work and every number "
            + "below is measuring nothing");

        Assert.True(
            settledStats.WorstOffRecord <= tolerance,
            $"a dab was frozen {settledStats.WorstOffRecord:0.####} px from where the record "
            + $"puts it, against a tolerance of {tolerance} — the predicate is not bounding the "
            + "error it claims to bound");

        double sum = 0;
        int worst = 0, counted = 0, differing = 0;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var a = strictImage.GetPixel(x, y).Alpha;
                var b = settledImage.GetPixel(x, y).Alpha;
                if (a == 0 && b == 0) continue;
                counted++;
                var d = Math.Abs(a - b);
                if (d > 0) differing++;
                sum += d;
                worst = Math.Max(worst, d);
            }
        }

        var mean = sum / Math.Max(1, counted);
        output.WriteLine($"  over {counted} pixels of mark:");
        output.WriteLine($"    pixels that differ at all   {differing}  ({100.0 * differing / counted:0.##}%)");
        output.WriteLine($"    mean difference             {mean:0.###} alpha");
        output.WriteLine($"    worst difference            {worst} alpha");

        // **Zero, and asserted as zero.** A quarter pixel of displacement on an
        // antialiased rim would be worth a few alpha and would have been
        // accepted here; there is none, because the dabs the tolerance settles
        // have already stopped moving. A budget loose enough to allow the
        // displacement that does not happen would also allow the two arms
        // drawing genuinely different dabs, which is the failure to catch.
        Assert.True(
            worst == 0,
            $"settling early moved a pixel by {worst} of 255. Every measurement says the dabs "
            + "it settles are already final, so any difference at all means the two arms are "
            + "drawing different dabs — look at whether the tolerance is settling a dab before "
            + "the walk that creates it has been superseded");
    }

    /// <summary>
    /// The committed render does not go through the settling at all, so it must
    /// be untouched by any of this.
    /// </summary>
    /// <remarks>
    /// <b>This is the load-bearing one.</b> The whole trade is only acceptable
    /// because the mark settles onto the exact one the moment the pen lifts —
    /// <c>EndStroke</c> commits through <c>StampStroke</c>, which walks the
    /// record and knows nothing about a stable cut. If that were ever not true,
    /// the tolerance would be changing the artwork rather than the preview and
    /// would have to go.
    /// </remarks>
    [Fact]
    public void TheCommittedRenderIsBitIdenticalEitherWay()
    {
        var brush = Soft(500);
        var stroke = After(brush, 30);
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);

        using var first = SKSurface.Create(info);
        using var second = SKSurface.Create(info);
        Assert.NotNull(first);
        Assert.NotNull(second);
        first!.Canvas.Clear(SKColors.Transparent);
        second!.Canvas.Clear(SKColors.Transparent);

        BrushEngine.StampStroke(first.Canvas, stroke, info);
        BrushEngine.StampStroke(second.Canvas, stroke, info);

        using var a = first.PeekPixels();
        using var b = second.PeekPixels();
        Assert.NotNull(a);
        Assert.NotNull(b);

        var left = a!.GetPixelSpan<byte>();
        var right = b!.GetPixelSpan<byte>();
        Assert.True(
            left.SequenceEqual(right),
            "two commits of the same stroke differ, which would make every claim in this "
            + "file about the commit meaningless before the tolerance is even considered");

        // And the commit never consults the settling — if it did, this test
        // would be checking the wrong thing and passing anyway.
        Assert.Equal(0, BrushEngine.SettleTolerance(new BrushSettings { Size = 500, Scatter = 0.35 }));
    }
}

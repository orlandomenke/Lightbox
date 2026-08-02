using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.Raster.Tests;

/// <summary>
/// M16b: a thick stroke through a bend must not show the tops of its dabs.
/// </summary>
/// <remarks>
/// The rendered half of the fix; the geometry is pinned by
/// <c>DensifyTests</c>. What an artist reported was a fast curve drawn with a
/// fat brush coming out faceted, with the outside of each bend scalloped as
/// though the stamps had been laid down individually. They had — the dab walk
/// followed the straight lines between recorded points, and a pen sampling a
/// quick arc every 20° puts those chords nearly 3 px inside the curve.
/// </remarks>
[Collection("Registries")]
public class StampingArcTests
{
    private const int W = 600, H = 600;
    private const double Cx = 300, Cy = 300, R = 180;
    private static SKImageInfo Info => new(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);

    private static Stroke Curve(double stepDeg, double size = 60) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#000000",
        Brush = new BrushSettings
        {
            Size = size, Hardness = 1, Flow = 1, Opacity = 1, Spacing = 0.15,
            PressureEnabled = false, AntiAlias = true,
        },
        Points = Arc(stepDeg),
    };

    private static List<StrokePoint> Arc(double stepDeg)
    {
        var pts = new List<StrokePoint>();
        for (double a = 200; a <= 340; a += stepDeg)
        {
            var rad = a * Math.PI / 180;
            pts.Add(new StrokePoint(Cx + Math.Cos(rad) * R, Cy + Math.Sin(rad) * R, 1));
        }
        return pts;
    }

    private static SKBitmap Render(Stroke s)
    {
        var bmp = new SKBitmap(Info);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        BrushEngine.StampStroke(canvas, s, Info, bmp);
        canvas.Flush();
        return bmp;
    }

    /// <summary>
    /// How much of the band just inside the stroke's outer edge actually got
    /// ink, over the middle of the sweep. A path that cuts the corner leaves
    /// this band bare between one recorded point and the next — which is
    /// exactly what a visible dab top is.
    /// </summary>
    private static double OuterBandCoverage(SKBitmap b, double size)
    {
        var outer = R + size / 2;
        int hit = 0, total = 0;
        // Ends excluded: the first and last span have no neighbour to say how
        // the curve continued, so they legitimately run straight. See
        // DensifyTests.TheEndsAreTheOnePlaceItCannotHelp.
        for (double a = 230; a <= 310; a += 0.25)
        {
            var rad = a * Math.PI / 180;
            for (double d = outer - 4; d <= outer - 1; d += 0.5)
            {
                var x = (int)Math.Round(Cx + Math.Cos(rad) * d);
                var y = (int)Math.Round(Cy + Math.Sin(rad) * d);
                total++;
                if (b.GetPixel(x, y).Alpha > 128) hit++;
            }
        }
        return (double)hit / total;
    }

    [Fact]
    public void AFastCurveIsInkedRightOutToItsEdge()
    {
        // The bug, stated as the artist saw it. Before the path was densified
        // this band came out around two-thirds covered, and the third that was
        // missing was the scalloping.
        using var bmp = Render(Curve(20));

        var coverage = OuterBandCoverage(bmp, 60);

        Assert.True(coverage > 0.99, $"only {coverage:P0} of the outer band was inked — the bend is faceted");
    }

    [Fact]
    public void HowFastItWasDrawnBarelyChangesTheMark()
    {
        // The property underneath the fix, and the one worth keeping: the mark
        // is the curve the artist drew, not an artefact of how many times the
        // pen managed to report on the way round.
        using var slow = Render(Curve(2));
        using var fast = Render(Curve(20));

        var difference = Math.Abs(OuterBandCoverage(slow, 60) - OuterBandCoverage(fast, 60));

        Assert.True(difference < 0.02, $"coverage differed by {difference:P0} between a slow and a fast draw");
    }

    [Fact]
    public void AThickBrushShowsItWorst()
    {
        // Why this was reported on thick brushes and not thin ones: the facet
        // is the same size either way, so it is a bigger share of a thin line
        // but only visible at all once the brush is wide enough to see across.
        // Both must be clean now.
        using var thick = Render(Curve(20, 160));

        Assert.True(OuterBandCoverage(thick, 160) > 0.99);
    }

    [Fact]
    public void AStraightStrokeIsUntouched()
    {
        // Densifying a straight run must be a no-op on the pixels. This is the
        // guard that the fix did not quietly reprice every ordinary stroke in
        // the application.
        var line = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            Brush = new BrushSettings
            {
                Size = 40, Hardness = 1, Flow = 1, Opacity = 1, Spacing = 0.15,
                PressureEnabled = false, AntiAlias = true,
            },
            Points = [new StrokePoint(80, 300, 1), new StrokePoint(520, 300, 1)],
        };
        var manyPoints = new Stroke
        {
            Tool = line.Tool, Color = line.Color, Brush = line.Brush,
            Points = [.. Enumerable.Range(0, 12).Select(i => new StrokePoint(80 + i * 40.0, 300, 1))],
        };

        using var two = Render(line);
        using var twelve = Render(manyPoints);

        Assert.Equal(two.GetPixelSpan().ToArray(), twelve.GetPixelSpan().ToArray());
    }

    [Fact]
    public void ADrawnCornerIsStillACorner()
    {
        // The cost of curving between points would be rounding off the ones
        // the artist put there on purpose. A right angle stays a right angle,
        // so the shape tools and a deliberate flick both survive.
        var corner = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            Brush = new BrushSettings
            {
                Size = 20, Hardness = 1, Flow = 1, Opacity = 1, Spacing = 0.1,
                PressureEnabled = false, AntiAlias = true,
            },
            Points = [new StrokePoint(150, 150, 1), new StrokePoint(450, 150, 1), new StrokePoint(450, 450, 1)],
        };

        using var bmp = Render(corner);

        // The tell is overshoot, not rounding-in. A cubic through a 90° turn
        // bulges PAST the vertex, so with corner detection off this pixel —
        // 12 px above a corner drawn with a 10 px radius — comes out fully
        // inked instead of empty. Measured: 0 sharp, 255 rounded.
        Assert.True(bmp.GetPixel(450, 138).Alpha < 8,
            $"ink {bmp.GetPixel(450, 138).Alpha} above the corner — the curve overshot the vertex");
        // And the elbow is still filled out to its own edge.
        Assert.True(bmp.GetPixel(450, 150).Alpha > 200);
        Assert.True(bmp.GetPixel(456, 144).Alpha > 200);
    }
}

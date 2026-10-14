using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Q269: tracing flattened line art back into strokes. A line becomes one
/// brush stroke down its middle, with pressure read from its width; a
/// crossing is two lines, not four; a closed loop is one stroke.
/// </summary>
/// <remarks>
/// <para>
/// The input is drawn by the brush engine, so the answer is known: the
/// strokes that made it. The measure is the overlap of the two pictures' ink
/// — traced and re-rendered against the original — printed beside the stroke
/// count, because either alone can be satisfied by a wrong tracer (one fat
/// blob per frame overlaps well; a hundred fragments counts badly).
/// </para>
/// <para>
/// Lossy by nature, and said so: the exact edge, the order the lines were
/// drawn in and the pressure the pen really had are not in pixels.
/// </para>
/// </remarks>
public class LineTracerTests(ITestOutputHelper output)
{
    private const int W = 240;
    private const int H = 240;

    private static Stroke Line(string color, double size, params (double X, double Y, double P)[] points) => new()
    {
        Color = color,
        Points = [.. points.Select(p => new StrokePoint(p.X, p.Y, p.P))],
        Brush = new BrushSettings { Size = size, Hardness = 0.9, Opacity = 1, Flow = 1, Spacing = 0.05 },
    };

    private static (double X, double Y, double P)[] Arc(double cx, double cy, double r, double from, double to, int n, double p = 1) =>
        [.. Enumerable.Range(0, n).Select(i =>
        {
            var a = from + (to - from) * i / (n - 1);
            return (cx + r * Math.Cos(a), cy + r * Math.Sin(a), p);
        })];

    private static SKBitmap Render(IReadOnlyList<Stroke> strokes) => FrameRasterizer.Rasterize(strokes, W, H);

    /// <summary>Intersection over union of the two pictures' ink, at a quarter opacity.</summary>
    private static double Overlap(SKBitmap a, SKBitmap b)
    {
        int both = 0, either = 0;
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            var inA = a.GetPixel(x, y).Alpha >= 64;
            var inB = b.GetPixel(x, y).Alpha >= 64;
            if (inA && inB) both++;
            if (inA || inB) either++;
        }
        return either == 0 ? 1 : (double)both / either;
    }

    private List<Stroke> TraceAndReport(IReadOnlyList<Stroke> drawn, string what, out double overlap)
    {
        using var original = Render(drawn);
        var traced = LineTracer.Trace(original);
        using var again = Render(traced);
        overlap = Overlap(original, again);
        output.WriteLine($"{what}: {drawn.Count} drawn, {traced.Count} traced, overlap {overlap:P1}");
        return traced;
    }

    [Fact]
    public void AStraightLineIsOneStrokeDownItsMiddle()
    {
        var traced = TraceAndReport([Line("#202020", 8, (30, 120, 1), (210, 120, 1))], "straight", out var overlap);

        var stroke = Assert.Single(traced);
        Assert.True(overlap > 0.85);
        // Down the middle: every point within a pixel of y = 120.
        Assert.All(stroke.Points, p => Assert.InRange(p.Y, 119, 121));
        Assert.InRange(stroke.Points.Min(p => p.X), 28, 40);
        Assert.InRange(stroke.Points.Max(p => p.X), 200, 212);
    }

    [Fact]
    public void ACurveStaysACurve()
    {
        var traced = TraceAndReport([Line("#202020", 6, Arc(120, 140, 80, Math.PI * 1.1, Math.PI * 1.9, 40))], "curve", out var overlap);

        Assert.Single(traced);
        Assert.True(overlap > 0.85);
    }

    [Fact]
    public void ACrossingIsTwoLinesNotFour()
    {
        var traced = TraceAndReport(
            [Line("#202020", 6, (30, 30, 1), (210, 210, 1)), Line("#202020", 6, (210, 30, 1), (30, 210, 1))],
            "crossing", out var overlap);

        Assert.Equal(2, traced.Count);
        Assert.True(overlap > 0.8);
    }

    [Fact]
    public void ATJunctionIsTheBarAndTheStem()
    {
        var traced = TraceAndReport(
            [Line("#202020", 6, (30, 60, 1), (210, 60, 1)), Line("#202020", 6, (120, 60, 1), (120, 210, 1))],
            "T", out var overlap);

        Assert.Equal(2, traced.Count);
        Assert.True(overlap > 0.8);
        // One of them is the whole bar.
        Assert.Contains(traced, s => s.Points.Max(p => p.X) - s.Points.Min(p => p.X) > 160);
    }

    [Fact]
    public void AClosedLoopIsOneStroke()
    {
        var traced = TraceAndReport([Line("#202020", 6, Arc(120, 120, 70, 0, Math.PI * 2, 80))], "loop", out var overlap);

        var stroke = Assert.Single(traced);
        Assert.True(overlap > 0.85);
        var first = stroke.Points[0];
        var last = stroke.Points[^1];
        Assert.True(Math.Abs(first.X - last.X) < 4 && Math.Abs(first.Y - last.Y) < 4, "the loop is not closed");
    }

    [Fact]
    public void ALineThatTapersCarriesItsWidthAsPressure()
    {
        // Pressure 0.2 at the left, 1 at the right: the traced stroke's
        // pressure rises along it, and its brush is about as big as the line
        // was at its widest.
        var traced = TraceAndReport(
            [Line("#202020", 16, (30, 120, 0.2), (120, 120, 0.6), (210, 120, 1))], "taper", out var overlap);

        var stroke = Assert.Single(traced);
        Assert.True(overlap > 0.8);
        var leftToRight = stroke.Points.OrderBy(p => p.X).ToList();
        var left = leftToRight.Take(3).Average(p => p.Pressure);
        var right = leftToRight.TakeLast(3).Average(p => p.Pressure);
        output.WriteLine($"pressure left {left:F2}, right {right:F2}, brush {stroke.Brush.Size:F1}");
        // The ends read wider than the pen was there — the round cap and the
        // smoothing both pull towards the middle — so the claim is the rise.
        Assert.True(right > left * 1.6, $"pressure {left:F2} → {right:F2} does not follow the width");
        Assert.InRange(stroke.Brush.Size, 12, 20);
    }

    [Fact]
    public void ATracedLineIsTheColourItWasDrawnIn()
    {
        var traced = TraceAndReport([Line("#c02040", 8, (30, 120, 1), (210, 120, 1))], "colour", out _);

        var colour = BrushEngine.ParseColor(Assert.Single(traced).Color);
        Assert.InRange(colour.Red, 0xb0, 0xd0);
        Assert.InRange(colour.Green, 0x10, 0x30);
        Assert.InRange(colour.Blue, 0x30, 0x50);
    }

    [Fact]
    public void ADotIsAStrokeAndSpecksAreNot()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawCircle(60, 60, 5, paint);      // a dot an artist drew
            canvas.DrawRect(150, 150, 1, 1, paint);   // a speck of noise
        }

        var traced = LineTracer.Trace(bitmap);

        var dot = Assert.Single(traced);
        Assert.InRange(dot.Points[0].X, 57, 63);
        Assert.InRange(dot.Points[0].Y, 57, 63);
    }

    [Fact]
    public void AFaintLineIsInkOnlyWhenTheThresholdSaysSo()
    {
        // The owner's arm layer: thin pale lines beside dark ones. How faint
        // still counts as ink is the artist's call, so it is a setting.
        var faint = Line("#202020", 6, (30, 80, 1), (210, 80, 1));
        faint.Brush.Opacity = 0.2;
        var dark = Line("#202020", 6, (30, 160, 1), (210, 160, 1));
        using var picture = Render([faint, dark]);

        var strict = LineTracer.Trace(picture, new LineTraceOptions { Threshold = 0.4 });
        var loose = LineTracer.Trace(picture, new LineTraceOptions { Threshold = 0.1 });

        output.WriteLine($"strict {strict.Count}, loose {loose.Count}");
        Assert.Single(strict);
        Assert.Equal(2, loose.Count);
    }

    [Fact]
    public void AScanOnWhitePaperIsReadByDarkness()
    {
        // An imported scan or photo has no transparency: every pixel is
        // opaque, and the ink is what is dark. Read by alpha it would be one
        // page-sized blob.
        using var line = Render([Line("#202020", 8, (30, 120, 1), (210, 120, 1))]);
        using var scan = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(scan))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(line, 0, 0);
        }

        var traced = LineTracer.Trace(scan);

        var stroke = Assert.Single(traced);
        Assert.All(stroke.Points, p => Assert.InRange(p.Y, 118, 122));
        var colour = BrushEngine.ParseColor(stroke.Color);
        Assert.True(colour.Red < 0x60, $"the line came back as {stroke.Color}, not dark");
    }

    [Fact]
    public void LessDetailDropsShortMarks()
    {
        using var picture = Render([
            Line("#202020", 6, (30, 120, 1), (210, 120, 1)),
            Line("#202020", 4, (60, 60, 1), (66, 62, 1)),   // a short tick
        ]);

        Assert.Equal(2, LineTracer.Trace(picture, new LineTraceOptions { MinimumLength = 2 }).Count);
        Assert.Single(LineTracer.Trace(picture, new LineTraceOptions { MinimumLength = 20 }));
    }

    [Fact]
    public void NothingDrawnTracesToNothing()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);

        Assert.Empty(LineTracer.Trace(bitmap));
    }

    [Fact]
    public void ADrawingOfManyLinesComesBackAsAbout()
    {
        // A loose figure: head, body, two arms, two legs, a hand-ish loop.
        List<Stroke> figure =
        [
            Line("#202020", 5, Arc(120, 50, 22, 0, Math.PI * 2, 50)),
            Line("#202020", 6, (120, 72, 1), (118, 150, 0.8)),
            Line("#202020", 5, (120, 90, 1), (70, 120, 0.6), (50, 150, 0.4)),
            Line("#202020", 5, (120, 90, 1), (170, 110, 0.7), (195, 90, 0.5)),
            Line("#202020", 6, (118, 150, 1), (90, 200, 0.7), (80, 230, 0.5)),
            Line("#202020", 6, (118, 150, 1), (150, 195, 0.7), (165, 228, 0.5)),
        ];

        var traced = TraceAndReport(figure, "figure", out var overlap);

        Assert.True(overlap > 0.75, $"overlap {overlap:P1}");
        // Between three (both arms one line through the shoulders, a leg
        // carried on from the body) and a dozen (every junction a break).
        Assert.InRange(traced.Count, 3, 12);
    }

    // ---- a layer traced --------------------------------------------------------

    private static Frame Flattened(params Stroke[] strokes)
    {
        using var pixels = Render(strokes);
        return new Frame { PngBase64 = PngCodec.Encode(pixels) };
    }

    [Fact]
    public void ALayerIsTracedOntoANewLayerAboveAndTheOriginalHidden()
    {
        var below = new Layer { Name = "Ink", Cels = [new Cel { Frame = new Frame { Strokes = [Line("#2040c0", 4, (10, 10, 1), (50, 10, 1))] } }] };
        var shared = Flattened(Line("#202020", 6, (30, 120, 1), (210, 120, 1)));
        var drawnOver = Flattened(Line("#202020", 6, (30, 60, 1), (210, 60, 1)));
        var mark = Line("#c02040", 4, (100, 200, 1), (140, 200, 1));
        drawnOver.Strokes.Add(mark);
        var flat = new Layer
        {
            Name = "ArmLines",
            BlendMode = LayerBlendMode.Multiply,
            Opacity = 0.8,
            GroupId = "folder-1",
            Cels = [new Cel { Frame = shared }, new Cel(), new Cel { Frame = drawnOver }, new Cel { Frame = shared }],
        };
        var scene = new Scene { Width = W, Height = H, Layers = [below, flat] };

        var result = LayerTrace.Trace(scene, flat);

        Assert.NotNull(result);
        Assert.Equal(3, scene.Layers.Count);
        Assert.Same(result!.Layer, scene.Layers[2]);
        Assert.False(flat.Visible);
        Assert.Contains(flat.Cels, c => c.Frame?.HasBaseline == true);   // the way back is untouched
        var traced = result.Layer;
        Assert.Equal("ArmLines (traced)", traced.Name);
        Assert.Equal((LayerBlendMode.Multiply, 0.8, "folder-1"), (traced.BlendMode, traced.Opacity, traced.GroupId));
        Assert.True(traced.Visible);
        Assert.Equal(2, result.Drawings);
        // Timing kept: the hold holds, the shared drawing is still one.
        Assert.Null(traced.Cels[1].Frame);
        Assert.Same(traced.Cels[0].Frame, traced.Cels[3].Frame);
        Assert.All(traced.Cels.Select(c => c.Frame).OfType<Frame>(), f => Assert.False(f.HasBaseline));
        // Traced lines under the strokes the drawing already had.
        var third = traced.Cels[2].Frame!;
        Assert.Equal(2, third.Strokes.Count);
        Assert.Equal(mark.Color, third.Strokes[^1].Color);
        Assert.NotEqual(mark.Id, third.Strokes[^1].Id);
    }

    [Fact]
    public void ALayerWithNoPixelsHasNothingToTrace()
    {
        var layer = new Layer { Cels = [new Cel { Frame = new Frame { Strokes = [Line("#202020", 4, (10, 10, 1), (50, 10, 1))] } }] };
        var scene = new Scene { Width = W, Height = H, Layers = [layer] };

        Assert.Null(LayerTrace.Trace(scene, layer));
        Assert.Single(scene.Layers);
        Assert.True(layer.Visible);
    }
}

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
    public void ALineRunningOffTheEdgeIsTracedDownItsMiddle()
    {
        // The adversary: thinning skipped the outermost rows and columns, so
        // ink touching the edge stayed there and a line running off the page
        // came back as a sliver along the border.
        var off = TraceAndReport([Line("#202020", 16, (-20, 100, 1), (170, 100, 1))], "off the edge", out _);
        var top = TraceAndReport([Line("#202020", 7, (28, 2, 1), (172, 2, 1))], "along the top", out _);

        var stroke = Assert.Single(off);
        Assert.All(stroke.Points, p => Assert.InRange(p.Y, 97, 103));
        Assert.InRange(Assert.Single(top).Points.Average(p => p.Y), 1.0, 4.0);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void ABusyPictureCostsWhatItsLinesDo()
    {
        // The adversary: a 1080×1080 grid took 15.6 s and 7.8 GB — the joiner
        // started again after every join. Four times the grid should cost
        // about four times as much, not sixteen.
        static SKBitmap Grid(int size)
        {
            var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(bitmap);
            using var ink = new SKPaint { Color = SKColors.Black, StrokeWidth = 3 };
            canvas.Clear(SKColors.Transparent);
            for (var x = 10; x < size; x += 20) canvas.DrawLine(x, 0, x, size, ink);
            for (var y = 10; y < size; y += 20) canvas.DrawLine(0, y, size, y, ink);
            return bitmap;
        }
        double Best(SKBitmap picture)
        {
            var best = double.MaxValue;
            for (var i = 0; i < 3; i++)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                LineTracer.Trace(picture);
                best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
            }
            return best;
        }
        using var small = Grid(240);
        using var large = Grid(480);

        var (a, b) = (Best(small), Best(large));

        output.WriteLine($"grid 240: {a:F0} ms, grid 480: {b:F0} ms, ratio {b / a:F1}x");
        Assert.True(b < a * 8 + 50, $"four times the grid cost {b / a:F1} times as much");
    }

    [Fact]
    public void APictureThatIsMostlyInkIsNotTraced()
    {
        // A page filled with ink has no lines in it to find, and thinning it
        // costs one pass of the page per pixel of its depth.
        using var full = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        full.Erase(SKColors.Black);

        Assert.NotNull(LineTracer.WhyNot(full, new LineTraceOptions { Source = InkSource.Transparency }));
        Assert.Empty(LineTracer.Trace(full, new LineTraceOptions { Source = InkSource.Transparency }));
        using var line = Render([Line("#202020", 8, (30, 120, 1), (210, 120, 1))]);
        Assert.Null(LineTracer.WhyNot(line, new LineTraceOptions()));
    }

    [Fact]
    public void ATraceCanBeStopped()
    {
        using var picture = Render([Line("#202020", 8, (30, 120, 1), (210, 120, 1))]);
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => LineTracer.Trace(picture, null, stop.Token));
    }

    [Fact]
    public void NoTracedStrokeIsAZeroLengthLine()
    {
        using var noise = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        noise.Erase(SKColors.Transparent);
        var seed = 12345u;
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            seed = seed * 1664525 + 1013904223;
            if (seed >> 24 < 60) noise.SetPixel(x, y, SKColors.Black);
        }

        var traced = LineTracer.Trace(noise);

        Assert.All(traced.Where(s => s.Points.Count > 1), s =>
            Assert.True(s.Points.Any(p => p.X != s.Points[0].X || p.Y != s.Points[0].Y), "a line with no length"));
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
    public void ATracedLayerIsANewLayerNotATwin()
    {
        // The adversary: identity fields a duplicate clears were copied — a
        // second background layer, a second layer answering to a simulation,
        // a mask drawing sharing its id — and keys a newer build wrote on a cel
        // were dropped.
        var flat = new Layer
        {
            Name = "Scan",
            IsBackground = true,
            SimId = "sim1",
            Mask = new LayerMask { Frame = new Frame { Strokes = [Line("#000000", 10, (0, 0, 1), (100, 100, 1))] } },
            Cels = [new Cel { Frame = Flattened(Line("#202020", 6, (30, 120, 1), (210, 120, 1))) }],
        };
        flat.Cels[0].Unknown = new() { ["later"] = System.Text.Json.JsonDocument.Parse("1").RootElement.Clone() };
        var scene = new Scene { Width = W, Height = H, Layers = [flat] };

        var traced = LayerTrace.Trace(scene, flat)!.Layer;

        Assert.False(traced.IsBackground);
        Assert.Null(traced.SimId);
        Assert.NotEqual(flat.Mask.Frame.Id, traced.Mask!.Frame.Id);
        Assert.NotNull(traced.Cels[0].Unknown);
    }

    [Fact]
    public void ADrawingWhosePixelsWillNotReadIsLeftAsItIs()
    {
        var good = Flattened(Line("#202020", 6, (30, 120, 1), (210, 120, 1)));
        var bad = new Frame { PngBase64 = "bm90IGEgcGljdHVyZQ==" };
        var flat = new Layer { Cels = [new Cel { Frame = good }, new Cel { Frame = bad }] };
        var scene = new Scene { Width = W, Height = H, Layers = [flat] };

        var result = LayerTrace.Trace(scene, flat);

        Assert.Equal(1, result!.Drawings);
        Assert.Equal(1, result.Unreadable);
        Assert.True(result.Layer.Cels[1].Frame!.HasBaseline, "the unreadable drawing was emptied rather than kept");
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

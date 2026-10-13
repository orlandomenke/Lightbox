using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Merge down can keep strokes where an eraser used to force pixels: the
/// eraser is applied to the upper drawing's own strokes as a cut, the way a
/// vector eraser works, and then dropped — so the merged drawing is still a
/// stroke record an artist can edit and the inbetweener can read.
/// </summary>
/// <remarks>
/// <para>
/// It is <b>nearly</b> exact, and these tests measure how nearly rather than
/// asserting equality: the owner chose this knowing a cut end is a brush's
/// round end rather than the eraser's bite. Every render comparison prints the
/// number it measured and is bracketed by the two ways of getting it wrong —
/// dropping the eraser (the gap closes) and keeping it (it eats the lower
/// layer) — so a threshold a broken version also clears cannot pass unnoticed.
/// </para>
/// </remarks>
public class MergeKeepsStrokesTests(ITestOutputHelper output)
{
    private const int W = 200;
    private const int H = 200;

    private static Scene TestScene() => new() { Width = W, Height = H };

    private static BrushSettings Hard(double size) =>
        new() { Size = size, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.1, AntiAlias = false };

    private static Stroke Bar(double y, string color) => new()
    {
        Color = color,
        Points = [new StrokePoint(20, y, 1), new StrokePoint(180, y, 1)],
        Brush = Hard(24),
    };

    /// <summary>An eraser dragged straight down the page at x, through both bars.</summary>
    private static Stroke EraserDown(double x, double size = 30) => new()
    {
        Tool = ToolKind.Eraser,
        Points = [new StrokePoint(x, 20, 1), new StrokePoint(x, 180, 1)],
        Brush = Hard(size),
    };

    private static Layer LayerOf(params Stroke[] strokes) =>
        new() { Cels = [new Cel { Frame = new Frame { Strokes = [.. strokes] } }] };

    private static SKBitmap Composite(Layer lower, Layer upper)
    {
        var bottom = FrameRasterizer.Materialize(lower.Cels[0].Frame!, W, H);
        using var top = FrameRasterizer.Materialize(upper.Cels[0].Frame!, W, H);
        using var canvas = new SKCanvas(bottom);
        canvas.DrawBitmap(top, 0, 0);
        canvas.Flush();
        return bottom;
    }

    private static SKBitmap Render(Layer layer) => FrameRasterizer.Materialize(layer.Cels[0].Frame!, W, H);

    private static (int Differing, int Painted) Compare(SKBitmap expected, SKBitmap actual)
    {
        int differing = 0, painted = 0;
        var a = expected.GetPixelSpan();
        var b = actual.GetPixelSpan();
        for (var i = 0; i < a.Length; i += 4)
        {
            var inA = a[i + 3] > 127;
            var inB = b[i + 3] > 127;
            if (inA) painted++;
            if (inA != inB || (inA && (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2]))) differing++;
        }
        return (differing, painted);
    }

    private static int Covered(SKBitmap bitmap, int x0, int y0, int x1, int y1)
    {
        var n = 0;
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
                if (bitmap.GetPixel(x, y).Alpha > 127) n++;
        return n;
    }

    [Fact]
    public void AnEraserOnTheUpperLayerCutsItsOwnStrokesAndLeavesTheLowerAlone()
    {
        var lower = LayerOf(Bar(60, "#ff0000"));
        var upper = LayerOf(Bar(120, "#0000ff"), EraserDown(100));
        using var expected = Composite(lower, upper);

        LayerMerge.MergeDown(TestScene(), upper, lower, EraserMerge.KeepStrokes);

        var frame = lower.Cels[0].Frame!;
        Assert.False(frame.HasBaseline);                                    // still a stroke record
        Assert.DoesNotContain(frame.Strokes, s => s.Tool == ToolKind.Eraser); // its effect is in the geometry
        Assert.Equal(3, frame.Strokes.Count);                               // red, and blue in two pieces

        using var actual = Render(lower);
        // Both directions, in pixels the picture is about:
        Assert.True(Covered(actual, 92, 50, 108, 70) > 200, "the lower layer's bar must survive where the eraser crossed");
        Assert.Equal(0, Covered(actual, 92, 110, 108, 130));                // and the upper bar has its gap

        var (differing, painted) = Compare(expected, actual);
        output.WriteLine($"kept strokes: {differing} of {painted} painted pixels differ ({100.0 * differing / painted:F1}%)");
        Assert.True(differing < painted * 0.04, $"{differing} of {painted} differ");
    }

    [Fact]
    public void TheTwoWaysOfGettingItWrongAreBothFarOutsideTheThreshold()
    {
        // What the threshold above has to tell apart. Printed, because a limit
        // the broken versions also clear would be decoration.
        var lower = LayerOf(Bar(60, "#ff0000"));
        var upper = LayerOf(Bar(120, "#0000ff"), EraserDown(100));
        using var expected = Composite(lower, upper);

        using var dropped = Render(LayerOf(Bar(60, "#ff0000"), Bar(120, "#0000ff")));
        using var kept = Render(LayerOf(Bar(60, "#ff0000"), Bar(120, "#0000ff"), EraserDown(100)));
        var (dropDiff, painted) = Compare(expected, dropped);
        var (keepDiff, _) = Compare(expected, kept);
        output.WriteLine($"eraser dropped: {dropDiff} of {painted} ({100.0 * dropDiff / painted:F1}%); "
                         + $"eraser kept: {keepDiff} of {painted} ({100.0 * keepDiff / painted:F1}%)");

        Assert.True(dropDiff > painted * 0.06);
        Assert.True(keepDiff > painted * 0.06);
    }

    [Fact]
    public void AThinEraserThroughAThickLineStillLeavesAGap()
    {
        // A piece ending exactly at the eraser's edge would bulge back into the
        // gap by the line's half-width, and close it: the cut is pulled back.
        var lower = LayerOf(Bar(60, "#ff0000"));
        var upper = LayerOf(Bar(120, "#0000ff"), EraserDown(100, size: 8));

        LayerMerge.MergeDown(TestScene(), upper, lower, EraserMerge.KeepStrokes);

        using var actual = Render(lower);
        Assert.Equal(0, Covered(actual, 98, 112, 102, 128));
    }

    [Fact]
    public void AnEraserThatOnlyShavesTheEdgeDoesNotBreakTheLine()
    {
        // The centre of the line was never reached, so it is one line still.
        var shave = new Stroke
        {
            Tool = ToolKind.Eraser,
            Points = [new StrokePoint(60, 136, 1), new StrokePoint(140, 136, 1)],
            Brush = Hard(12),
        };
        var lower = LayerOf(Bar(60, "#ff0000"));
        var upper = LayerOf(Bar(120, "#0000ff"), shave);

        LayerMerge.MergeDown(TestScene(), upper, lower, EraserMerge.KeepStrokes);

        Assert.Equal(2, lower.Cels[0].Frame!.Strokes.Count);
    }

    [Fact]
    public void AStrokeErasedEntirelyIsDropped()
    {
        var over = new Stroke
        {
            Tool = ToolKind.Eraser,
            Points = [new StrokePoint(10, 120, 1), new StrokePoint(190, 120, 1)],
            Brush = Hard(40),
        };
        var lower = LayerOf(Bar(60, "#ff0000"));
        var upper = LayerOf(Bar(120, "#0000ff"), over);

        LayerMerge.MergeDown(TestScene(), upper, lower, EraserMerge.KeepStrokes);

        var stroke = Assert.Single(lower.Cels[0].Frame!.Strokes);
        Assert.Equal("#ff0000", stroke.Color);
    }

    [Fact]
    public void AClearedRegionCutsTheSameWay()
    {
        var cleared = new Stroke
        {
            Tool = ToolKind.ClearRegion,
            Points = [new(85, 90, 1), new(115, 90, 1), new(115, 150, 1), new(85, 150, 1)],
        };
        var lower = LayerOf(Bar(60, "#ff0000"));
        var upper = LayerOf(Bar(120, "#0000ff"), cleared);

        LayerMerge.MergeDown(TestScene(), upper, lower, EraserMerge.KeepStrokes);

        var frame = lower.Cels[0].Frame!;
        Assert.False(frame.HasBaseline);
        Assert.Equal(3, frame.Strokes.Count);
        Assert.DoesNotContain(frame.Strokes, s => s.Tool == ToolKind.ClearRegion);
    }

    [Fact]
    public void AStrokeDrawnAfterTheEraserIsNotCutByIt()
    {
        // Order is meaning: the artist erased, then drew across the gap.
        var lower = LayerOf(Bar(60, "#ff0000"));
        var upper = LayerOf(Bar(120, "#0000ff"), EraserDown(100), Bar(120, "#00ff00"));

        LayerMerge.MergeDown(TestScene(), upper, lower, EraserMerge.KeepStrokes);

        var frame = lower.Cels[0].Frame!;
        Assert.Equal(4, frame.Strokes.Count);
        Assert.Equal(2, frame.Strokes[^1].Points.Count);   // the green bar, whole
    }

    [Fact]
    public void TheLowerLayersOwnErasersStayWhereTheyWere()
    {
        // They come before the upper strokes in the record, so they still only
        // touch what they touched.
        var lower = LayerOf(Bar(60, "#ff0000"), EraserDown(50, size: 10));
        var upper = LayerOf(Bar(120, "#0000ff"));
        using var expected = Composite(lower, upper);

        LayerMerge.MergeDown(TestScene(), upper, lower, EraserMerge.KeepStrokes);

        using var actual = Render(lower);
        Assert.True(expected.Bytes.AsSpan().SequenceEqual(actual.Bytes));
        Assert.Contains(lower.Cels[0].Frame!.Strokes, s => s.Tool == ToolKind.Eraser);
    }

    [Fact]
    public void AFillUnderAnEraserIsNotCutAndKeepsItsEraser()
    {
        // A fill is an area, and an area with a bite out of it is not a list of
        // shorter fills. That drawing keeps the exact answer — once pixels, now
        // its own strokes, eraser included, in a group (Q265).
        var fill = new Stroke
        {
            Tool = ToolKind.Fill,
            Color = "#0000ff",
            Points = [new(40, 100, 1), new(160, 100, 1), new(160, 140, 1), new(40, 140, 1)],
        };
        var lower = LayerOf(Bar(60, "#ff0000"));
        var upper = LayerOf(fill, EraserDown(100));

        LayerMerge.MergeDown(TestScene(), upper, lower, EraserMerge.KeepStrokes);

        var frame = lower.Cels[0].Frame!;
        Assert.False(frame.HasBaseline);
        Assert.Equal([ToolKind.Fill, ToolKind.Eraser], frame.Strokes[^1].Group!.Strokes.Select(s => s.Tool));
    }

    [Fact]
    public void ABlendModeStillNeedsAGroupWhateverWasChosenForErasers()
    {
        var lower = LayerOf(Bar(60, "#ff0000"));
        var upper = LayerOf(Bar(120, "#0000ff"));
        upper.BlendMode = LayerBlendMode.Multiply;

        LayerMerge.MergeDown(TestScene(), upper, lower, EraserMerge.KeepStrokes);

        var frame = lower.Cels[0].Frame!;
        Assert.False(frame.HasBaseline);
        Assert.Equal(LayerBlendMode.Multiply, frame.Strokes[^1].Group!.Blend);
    }

    [Fact]
    public void ExactIsStillTheDefault()
    {
        var lower = LayerOf(Bar(60, "#ff0000"));
        var upper = LayerOf(Bar(120, "#0000ff"), EraserDown(100));
        using var expected = Composite(lower, upper);

        LayerMerge.MergeDown(TestScene(), upper, lower);

        // Exact, and no longer pixels: the erasers stay, in a group.
        var frame = lower.Cels[0].Frame!;
        Assert.False(frame.HasBaseline);
        Assert.Contains(frame.Strokes[^1].Group!.Strokes, s => s.Tool == ToolKind.Eraser);
        using var actual = Render(lower);
        Assert.True(expected.Bytes.AsSpan().SequenceEqual(actual.Bytes));
    }

    [Fact]
    public void TheChoiceIsOnlyOfferedWhenItChangesSomething()
    {
        var scene = TestScene();
        // Erasers are the only reason this pair would flatten: the choice matters.
        Assert.True(LayerMerge.ErasersDecide(LayerOf(Bar(120, "#0000ff"), EraserDown(100)), LayerOf(Bar(60, "#ff0000")), scene));
        // No erasers: nothing to choose.
        Assert.False(LayerMerge.ErasersDecide(LayerOf(Bar(120, "#0000ff")), LayerOf(Bar(60, "#ff0000")), scene));
        // A blend mode flattens it either way: nothing to choose.
        var blended = LayerOf(Bar(120, "#0000ff"), EraserDown(100));
        blended.BlendMode = LayerBlendMode.Multiply;
        Assert.False(LayerMerge.ErasersDecide(blended, LayerOf(Bar(60, "#ff0000")), scene));
    }

    /// <summary>
    /// Pixels the cut pieces paint inside what the eraser swept — ink where the
    /// artist removed it.
    /// </summary>
    /// <remarks>
    /// Measured inside the eraser's sweep and nowhere else, because that is the
    /// claim. Counting every pixel that differs also counts the edge of the
    /// whole line, which shifts by a pixel here and there when a line of
    /// varying width restarts its dabs at a cut — real, accepted, and nothing
    /// to do with ink coming back: it read as 136 "pixels of ink" on a line
    /// with none.
    /// </remarks>
    private static int InkWhereItWasErased(Stroke stroke, Stroke eraser)
    {
        var cut = EraserCut.Resolve([stroke, eraser])!;
        using var pieces = FrameRasterizer.Materialize(new Frame { Strokes = [.. cut] }, W, H);
        var radius = BrushEngine.RadiusAt(eraser.Brush, 1);
        var path = eraser.Points;
        var n = 0;
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                if (pieces.GetPixel(x, y).Alpha <= 127) continue;
                var p = new StrokePoint(x + 0.5, y + 0.5, 0);
                for (var i = 1; i < path.Count; i++)
                {
                    // Half a pixel inside the sweep's edge: a pixel straddling it is not "inside".
                    if (Lightbox.Core.Geometry.GeometryOps.DistToSegment(p, path[i - 1], path[i]) <= radius - 0.5)
                    {
                        n++;
                        break;
                    }
                }
            }
        }
        return n;
    }

    private static Stroke Eraser(double size, params (double X, double Y)[] path) => new()
    {
        Tool = ToolKind.Eraser,
        Points = [.. path.Select(p => new StrokePoint(p.X, p.Y, 1))],
        Brush = Hard(size),
    };

    [Theory]
    [InlineData("straight across", 100, 60, 100, 140)]
    [InlineData("at 45 degrees", 60, 60, 140, 140)]
    [InlineData("at a shallow slant", 40, 80, 160, 120)]
    public void ACrossingAtAnyOrdinaryAngleLeavesNoInkWhereItWasErased(
        string how, double x0, double y0, double x1, double y1)
    {
        // The adversary's finding: pulling a cut back along the line by the
        // line's half-width is only right for a square crossing. At a slant
        // the piece's round end still reached into the erased area — 94 pixels
        // at 45 degrees, 339 at this shallow one.
        var ink = InkWhereItWasErased(Bar(100, "#0000ff"), Eraser(10, (x0, y0), (x1, y1)));

        output.WriteLine($"{how}: {ink} pixels of ink inside the erased area");
        Assert.True(ink <= 8, $"{how}: {ink} pixels of ink inside the erased area");
    }

    [Fact]
    public void ALineThatThinsTowardTheCutIsPulledBackByItsOwnWidthThere()
    {
        // Sized from the piece that is kept, not from the point that was erased.
        var line = new Stroke
        {
            Color = "#0000ff",
            Points = [new StrokePoint(20, 100, 1), new StrokePoint(100, 100, 0.05), new StrokePoint(180, 100, 1)],
            Brush = Hard(24),
        };

        var ink = InkWhereItWasErased(line, Eraser(10, (60, 60), (60, 140)));

        output.WriteLine($"varying pressure: {ink} pixels of ink inside the erased area");
        Assert.True(ink <= 8, $"{ink} pixels");
    }

    [Fact]
    public void AnEraserUsedInsideASelectionFlattens()
    {
        // The renderer clips that eraser to its selection. Cutting by its path
        // alone would cut lines outside the selection that were never erased.
        var eraser = EraserDown(100);
        eraser.ClipId = "clip_1";

        Assert.Null(EraserCut.Resolve([Bar(100, "#0000ff"), eraser]));
        Assert.False(EraserCut.CanCut([Bar(100, "#0000ff"), eraser]));
    }

    [Fact]
    public void AMirroredOrWrappedEraserFlattens()
    {
        // Its copies erase too, and a cut at the path alone would bring that ink back.
        var mirrored = EraserDown(100);
        mirrored.Symmetry = new SymmetryAxis();
        var wrapped = EraserDown(40);
        wrapped.Wrap = new TileWrap { Left = 0, Top = 0, Width = 120, Height = 200 };

        Assert.Null(EraserCut.Resolve([Bar(100, "#0000ff"), mirrored]));
        Assert.Null(EraserCut.Resolve([Bar(100, "#0000ff"), wrapped]));
    }

    [Fact]
    public void AMirroredStrokeWithAnyLaterEraserFlattens()
    {
        // The eraser may have crossed the mirrored copy, which lies nowhere
        // near the stroke's own points — so its position is not asked.
        var line = Bar(100, "#0000ff");
        line.Symmetry = new SymmetryAxis();
        var far = Eraser(4, (5, 5), (8, 5));

        Assert.Null(EraserCut.Resolve([line, far]));
    }

    [Theory]
    [InlineData("blur")]
    [InlineData("alpha lock")]
    [InlineData("medium")]
    [InlineData("rest points")]
    public void AStrokeThatReadsWhatIsUnderItIsNeverCut(string kind)
    {
        var line = Bar(100, "#0000ff");
        switch (kind)
        {
            case "blur": line.Brush.Kind = BrushKind.Blur; break;
            case "alpha lock": line.AlphaLocked = true; break;
            case "medium": line.Brush.Medium = new MediumSettings { Kind = MediumKind.Watercolour }; break;
            case "rest points": line.RestPoints = [.. line.Points]; break;
        }

        Assert.Null(EraserCut.Resolve([line, EraserDown(100)]));
        Assert.False(EraserCut.CanCut([line, EraserDown(100)]));
    }

    [Fact]
    public void AFillTheEraserNeverReachesIsLeftAsItIs()
    {
        // Refusing is for an erasure that reaches something uncuttable — not for
        // one elsewhere on the page.
        var fill = new Stroke
        {
            Tool = ToolKind.Fill,
            Color = "#00ff00",
            Points = [new(10, 10, 1), new(40, 10, 1), new(40, 30, 1), new(10, 30, 1)],
        };
        var record = new List<Stroke> { fill, Bar(120, "#0000ff"), Eraser(10, (100, 100), (100, 140)) };

        var cut = EraserCut.Resolve(record);

        Assert.NotNull(cut);
        Assert.Same(fill, cut![0]);
        Assert.True(EraserCut.CanCut(record));
    }

    [Fact]
    public void ACutKeepsWhatThePenRecordedAtEachPoint()
    {
        var line = new Stroke
        {
            Color = "#0000ff",
            Points =
            [
                new StrokePoint(20, 100, 1, TiltX: 10, TiltY: 20, Speed: 300),
                new StrokePoint(180, 100, 1, TiltX: 30, TiltY: 40, Speed: 500),
            ],
            Brush = Hard(10),
        };

        var cut = EraserCut.Resolve([line, EraserDown(100, size: 10)])!;

        Assert.Equal(10, cut[0].Points[0].TiltX);          // an original point, untouched
        Assert.Equal(300, cut[0].Points[0].Speed);
        Assert.InRange(cut[0].Points[^1].TiltX!.Value, 15, 25);   // the cut end, where the pen had got to
        Assert.InRange(cut[0].Points[^1].Speed!.Value, 350, 450);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void ErasersElsewhereOnThePageCostAlmostNothing()
    {
        // A ratio, not a time: forty lines and two dozen long erasers, once
        // with the erasers across the lines and once with the same erasers
        // moved off them. The off-page run must be a small fraction of the
        // other, or every eraser is being walked for every point of every line.
        static List<Stroke> Drawing(double eraserOffset)
        {
            var record = new List<Stroke>();
            for (var i = 0; i < 40; i++)
            {
                record.Add(new Stroke
                {
                    Points = [.. Enumerable.Range(0, 200).Select(k => new StrokePoint(20 + k * 4, 20 + i * 10 + Math.Sin(k / 9.0) * 4, 0.7))],
                    Brush = Hard(4),
                });
            }
            for (var e = 0; e < 24; e++)
            {
                record.Add(new Stroke
                {
                    Tool = ToolKind.Eraser,
                    Points = [.. Enumerable.Range(0, 400).Select(k => new StrokePoint(40 + e * 30 + Math.Sin(k / 7.0) * 6, eraserOffset + k, 0.8))],
                    Brush = Hard(8),
                });
            }
            return record;
        }

        static double Best(List<Stroke> record)
        {
            var best = double.MaxValue;
            for (var run = 0; run < 3; run++)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                EraserCut.Resolve(record);
                best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
            }
            return best;
        }

        var across = Best(Drawing(eraserOffset: 20));
        var elsewhere = Best(Drawing(eraserOffset: 5000));
        output.WriteLine($"erasers across the lines: {across:F1} ms; the same erasers off the drawing: {elsewhere:F1} ms");

        Assert.True(elsewhere < across * 0.25, $"{elsewhere:F1} ms against {across:F1} ms");
        // And the crossing itself has a ceiling rather than a second ratio. The
        // first cutter walked every eraser segment, with a pressure curve behind
        // each, for every pixel of every line: 43 seconds here. With the
        // segments prepared once and boxed it measures 0.3 to 0.5 seconds. A
        // ratio against the run above would divide by a sub-millisecond number
        // and fail on a fast machine with nothing wrong (the perf warden
        // measured 530 against a first limit of 1,000); five seconds is ten
        // times clear of both the cutter that works and the one that did not.
        Assert.True(across < 5000, $"{across:F1} ms");
    }

    [Fact]
    public void ACutPieceCarriesTheBrushAndTheLabelStaysOnTheLongerPiece()
    {
        var line = new Stroke
        {
            Label = "near-arm",
            Color = "#0000ff",
            Points = [new StrokePoint(20, 120, 0.4), new StrokePoint(180, 120, 0.8)],
            Brush = new BrushSettings { Size = 10, Hardness = 1, TipId = "pencil", AntiAlias = false },
        };

        var cut = EraserCut.Resolve([line, EraserDown(60, size: 10)])!;

        Assert.Equal(2, cut.Count);
        Assert.All(cut, s => Assert.Equal("pencil", s.Brush.TipId));
        Assert.NotEqual(cut[0].Id, cut[1].Id);
        // One label per drawing part: two strokes sharing it would both claim
        // the inbetweener's match.
        var labelled = Assert.Single(cut, s => s.Label == "near-arm");
        Assert.True(labelled.Points[^1].X - labelled.Points[0].X > 80);
        // Pressure at the cut is where the line had got to, not an endpoint's.
        var shortPiece = cut.Single(s => s.Label is null);
        Assert.InRange(shortPiece.Points[^1].Pressure, 0.42, 0.52);
    }
}

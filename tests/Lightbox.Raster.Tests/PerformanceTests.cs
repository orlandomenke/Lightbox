using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Performance regression budgets for the drawing hot paths, run on every
/// build. Budgets are deliberately generous (shared CI runners are slow and
/// noisy) — they exist to catch order-of-magnitude regressions like a live
/// preview going full-canvas again, not 10% drift.
/// </summary>
/// <remarks>
/// Each budget compares the <em>fastest</em> of several runs, not the median.
/// See <see cref="Bench"/> for why: a median measures the machine as much as
/// the code, and these budgets were flaking on loaded runners because of it.
/// </remarks>
[Trait("Category", "Performance")]
[Collection("Performance")]
public class PerformanceTests(ITestOutputHelper output)
{
    private const int W = 960;
    private const int H = 540;

    private static BrushSettings Watercolor => new()
    {
        Size = 22, Hardness = 0.4, Opacity = 0.65, Flow = 0.55, Spacing = 0.12,
        WetEdge = 0.55, Granulation = 0.35, PressureFlowGamma = 1,
    };

    private static Stroke Segment(BrushSettings brush, double x0, double y, int points = 4) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#2a4a8a",
        Brush = brush,
        Points = Enumerable.Range(0, points).Select(i => new StrokePoint(x0 + i * 3, y, 0.7)).ToList(),
    };

    private double FastestMs(int runs, Action action) => Bench.FastestMs(runs, action, log: output);

    [Fact]
    public void LivePreview_EffectBrushSegment_IsBoundedToTheSegment()
    {
        // One pointer event during a watercolor stroke: must NOT pay for the
        // whole canvas. Pre-fix this was a full-canvas scratch + two
        // full-canvas effect passes per event (the stutter).
        using var layer = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        layer.Erase(SKColors.Transparent);
        var brush = Watercolor;
        var x = 100.0;
        var fastest = FastestMs(60, () =>
        {
            FrameRasterizer.AppendDraft(layer, Segment(brush, x, 200));
            x = x < 800 ? x + 12 : 100;
        });
        Assert.True(fastest < 12.0, $"live-preview segment took {fastest:0.00} ms (budget 12 ms) — is it full-canvas again?");
    }

    [Fact]
    public void LivePreview_PlainBrushSegment_StaysCheap()
    {
        using var layer = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        layer.Erase(SKColors.Transparent);
        var brush = new BrushSettings { Size = 12, Hardness = 0.9 };
        var fastest = FastestMs(60, () => FrameRasterizer.AppendDraft(layer, Segment(brush, 300, 260)));
        Assert.True(fastest < 10.0, $"plain segment took {fastest:0.00} ms (budget 10 ms)");
    }

    [Fact]
    public void StrokeCommit_ExactAppend_IsIndependentOfFrameComplexity()
    {
        // Pen lift: the committed stroke stamps once onto the cached frame.
        // The frame already holding many strokes must not matter (the old
        // invalidate-and-replay path scaled with stroke count).
        using var layer = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        layer.Erase(SKColors.Transparent);
        var brush = Watercolor;
        var fastest = FastestMs(30, () => FrameRasterizer.Append(layer, Segment(brush, 200, 300, points: 40)));
        Assert.True(fastest < 150.0, $"exact stroke commit took {fastest:0.00} ms (budget 150 ms)");
    }

    [Fact]
    public void LivePreview_LargeBrushSegment_StaysInteractive()
    {
        // A 180 px effect brush: the bounded scratch is big, but one event
        // must still be far under a frame budget.
        using var layer = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        layer.Erase(SKColors.Transparent);
        var brush = Watercolor;
        brush.Size = 180;
        var fastest = FastestMs(30, () => FrameRasterizer.AppendDraft(layer, Segment(brush, 400, 270)));
        Assert.True(fastest < 25.0, $"large-brush segment took {fastest:0.00} ms (budget 25 ms)");
    }

    [Fact]
    public void FloodFill_FullCanvasRegion_MeetsBudget()
    {
        using var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(bmp))
        using (var paint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 8 })
        {
            canvas.DrawCircle(W / 2f, H / 2f, 180, paint); // a closed shape to respect
        }

        var fastest = FastestMs(10, () =>
            FloodFill.Fill(bmp, 30, 30, new FloodFill.Options(Tolerance: 32, GapPx: 4, GrowPx: 2)));
        Assert.True(fastest < 250.0, $"flood fill took {fastest:0.00} ms (budget 250 ms)");
    }

    [Fact]
    public void FloodFill_InsideRegionWithHole_MeetsBudget()
    {
        using var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(bmp))
        using (var paint = new SKPaint { Color = SKColors.Black })
        {
            canvas.DrawCircle(W / 2f, H / 2f, 200, paint);
            paint.Color = SKColors.Transparent;
            paint.BlendMode = SKBlendMode.Src;
            canvas.DrawCircle(W / 2f, H / 2f, 80, paint);
        }
        var fastest = FastestMs(10, () =>
            FloodFill.Fill(bmp, W / 2, H / 2 - 140, new FloodFill.Options(Tolerance: 32)));
        Assert.True(fastest < 250.0, $"donut fill took {fastest:0.00} ms (budget 250 ms)");
    }
}

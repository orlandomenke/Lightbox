using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Merging a layer down keeps the stroke record wherever the merge can be
/// exact without pixels, and bakes to a pixel baseline only where blend,
/// opacity, erasers or sampling strokes make appending strokes a different
/// picture. These are render comparisons where rendering is the claim, and
/// shape checks where the claim is about the record.
/// </summary>
public class LayerMergeTests
{
    private const int W = 200;
    private const int H = 200;

    private static Scene TestScene() => new() { Width = W, Height = H };

    private static Stroke Bar(double y, string color, ToolKind tool = ToolKind.Brush) => new()
    {
        Tool = tool,
        Color = color,
        Points = [new StrokePoint(20, y, 1), new StrokePoint(180, y, 1)],
        Brush = new BrushSettings { Size = 24, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.1, AntiAlias = false },
    };

    private static Cel Key(params Stroke[] strokes) =>
        new() { Frame = new Frame { Strokes = [.. strokes] } };

    private static Cel Hold() => new();

    private static Layer LayerOf(params Cel[] cels) => new() { Cels = [.. cels] };

    /// <summary>The pair as the app composites it: lower, then upper through its blend and opacity.</summary>
    private static SKBitmap Composite(Layer lower, Layer upper, int t)
    {
        var below = Lightbox.Core.Timeline.ExposureSheet.ExposedFrame(lower, t);
        var above = Lightbox.Core.Timeline.ExposureSheet.ExposedFrame(upper, t);
        var bottom = below is null
            ? new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul))
            : FrameRasterizer.Materialize(below, W, H, celIndex: t);
        if (above is not null)
        {
            using var top = FrameRasterizer.Materialize(above, W, H, celIndex: t);
            using var canvas = new SKCanvas(bottom);
            using var paint = new SKPaint
            {
                BlendMode = BlendModes.ToSkia(upper.BlendMode),
                Color = SKColors.White.WithAlpha((byte)Math.Round(upper.Opacity * 255)),
            };
            canvas.DrawBitmap(top, 0, 0, paint);
            canvas.Flush();
        }
        return bottom;
    }

    private static void AssertRendersIdentically(SKBitmap expected, Layer merged, int t)
    {
        var frame = Lightbox.Core.Timeline.ExposureSheet.ExposedFrame(merged, t);
        using var actual = frame is null
            ? new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul))
            : FrameRasterizer.Materialize(frame, W, H, celIndex: t);
        Assert.True(expected.Bytes.AsSpan().SequenceEqual(actual.Bytes),
            $"the merged layer must render exactly what the pair composited at frame {t}");
    }

    [Fact]
    public void PlainStrokes_MergeByConcatenation_AndRenderIdentically()
    {
        var scene = TestScene();
        var lower = LayerOf(Key(Bar(80, "#2040c0")));
        var upper = LayerOf(Key(Bar(100, "#c02040")));
        using var expected = Composite(lower, upper, 0);

        Assert.False(LayerMerge.WouldBakePixels(upper, lower));
        LayerMerge.MergeDown(scene, upper, lower);

        var frame = (Frame)lower.Cels[0].Frame!;
        Assert.False(frame.HasBaseline);
        Assert.Equal(2, frame.Strokes.Count);
        Assert.Equal("#2040c0", frame.Strokes[0].Color); // lower first, upper stamped after
        AssertRendersIdentically(expected, lower, 0);
    }

    [Fact]
    public void UpperBlendMode_Bakes_AndRenderMatchesTheComposite()
    {
        var scene = TestScene();
        // Overlapping bars, so the blend actually changes pixels.
        var lower = LayerOf(Key(Bar(100, "#808080")));
        var upper = LayerOf(Key(Bar(100, "#808080")));
        upper.BlendMode = LayerBlendMode.Multiply;
        using var expected = Composite(lower, upper, 0);

        Assert.True(LayerMerge.WouldBakePixels(upper, lower));
        LayerMerge.MergeDown(scene, upper, lower);

        var frame = (Frame)lower.Cels[0].Frame!;
        Assert.True(frame.HasBaseline);
        Assert.Empty(frame.Strokes);
        AssertRendersIdentically(expected, lower, 0);
    }

    [Fact]
    public void UpperOpacity_Bakes_AndRenderMatchesTheComposite()
    {
        var scene = TestScene();
        var lower = LayerOf(Key(Bar(100, "#2040c0")));
        var upper = LayerOf(Key(Bar(100, "#c02040")));
        upper.Opacity = 0.5;
        using var expected = Composite(lower, upper, 0);

        Assert.True(LayerMerge.WouldBakePixels(upper, lower));
        LayerMerge.MergeDown(scene, upper, lower);

        Assert.True(((Frame)lower.Cels[0].Frame!).HasBaseline);
        AssertRendersIdentically(expected, lower, 0);
    }

    [Fact]
    public void UpperEraser_Bakes_SoItCannotStartEatingTheLowerLayer()
    {
        var scene = TestScene();
        var lower = LayerOf(Key(Bar(100, "#2040c0")));
        // A mark and an eraser crossing it — on its own layer the eraser only
        // ever removed the red bar, and the merge must keep it that way.
        var upper = LayerOf(Key(Bar(100, "#c02040"), Bar(100, "#000000", ToolKind.Eraser)));
        using var expected = Composite(lower, upper, 0);

        Assert.True(LayerMerge.WouldBakePixels(upper, lower));
        LayerMerge.MergeDown(scene, upper, lower);

        var frame = (Frame)lower.Cels[0].Frame!;
        Assert.True(frame.HasBaseline);
        AssertRendersIdentically(expected, lower, 0);
        // The blue bar survived where the eraser crossed it — the proof the
        // eraser did not leak downward.
        using var merged = FrameRasterizer.Materialize(frame, W, H);
        Assert.NotEqual(0, merged.GetPixel(100, 100).Alpha);
    }

    [Fact]
    public void EraserOverNothing_StaysLossless()
    {
        var scene = TestScene();
        var lower = LayerOf(Hold()); // nothing underneath at all
        var upper = LayerOf(Key(Bar(100, "#c02040"), Bar(100, "#000000", ToolKind.Eraser)));

        Assert.False(LayerMerge.WouldBakePixels(upper, lower));
        LayerMerge.MergeDown(scene, upper, lower);

        var frame = (Frame)lower.Cels[0].Frame!;
        Assert.False(frame.HasBaseline);
        Assert.Equal(2, frame.Strokes.Count);
    }

    [Fact]
    public void HoldPatterns_MergeAtExposureBoundaries_AndHoldEverywhereElse()
    {
        var scene = TestScene();
        var lower = LayerOf(Key(Bar(80, "#2040c0")), Hold(), Hold(), Hold());
        var upper = LayerOf(Hold(), Hold(), Key(Bar(120, "#c02040")), Hold());

        LayerMerge.MergeDown(scene, upper, lower);

        Assert.Equal(4, lower.Cels.Count);
        Assert.NotNull(lower.Cels[0].Frame); // lower's key carries over
        Assert.Null(lower.Cels[1].Frame);    // both held — the merge holds
        Assert.NotNull(lower.Cels[2].Frame); // upper keyed — new merged drawing
        Assert.Null(lower.Cels[3].Frame);
        Assert.Single(((Frame)lower.Cels[0].Frame!).Strokes);
        Assert.Equal(2, ((Frame)lower.Cels[2].Frame!).Strokes.Count);
    }

    [Fact]
    public void MergedDrawings_GetFreshFrameAndStrokeIds()
    {
        // The same held drawing lands in two merged frames (frames 0 and 2),
        // so carrying ids would duplicate them across the document — and ids
        // key the render caches.
        var scene = TestScene();
        var held = Bar(80, "#2040c0");
        var lower = LayerOf(Key(held), Hold(), Hold());
        var upper = LayerOf(Hold(), Hold(), Key(Bar(120, "#c02040")));
        var originalFrameId = lower.Cels[0].Frame!.Id;

        LayerMerge.MergeDown(scene, upper, lower);

        var first = (Frame)lower.Cels[0].Frame!;
        var third = (Frame)lower.Cels[2].Frame!;
        Assert.NotEqual(originalFrameId, first.Id);
        Assert.NotEqual(first.Id, third.Id);
        Assert.NotEqual(held.Id, first.Strokes[0].Id);
        Assert.NotEqual(first.Strokes[0].Id, third.Strokes[0].Id);
    }

    /// <summary>A stroke covering only the left span of a bar, as mask coverage.</summary>
    private static Stroke LeftCover(double y) => new()
    {
        Color = "#ffffff",
        Points = [new StrokePoint(20, y, 1), new StrokePoint(90, y, 1)],
        Brush = new BrushSettings { Size = 24, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.1, AntiAlias = false },
    };

    private static SKBitmap Render(Layer layer, int t)
    {
        var frame = Lightbox.Core.Timeline.ExposureSheet.ExposedFrame(layer, t)!;
        return FrameRasterizer.Materialize(frame, W, H, celIndex: t);
    }

    [Fact]
    public void AMaskedUpperLayerBakesWithItsMaskApplied()
    {
        // The upper bar runs 20..180; its mask covers 20..90 — so the merged
        // drawing carries the bar's left span and nothing of its right.
        var scene = TestScene();
        var lower = LayerOf(Key(Bar(60, "#2040c0")));
        var upper = LayerOf(Key(Bar(120, "#c02040")));
        upper.Mask = new LayerMask();
        upper.Mask.Frame.Strokes.Add(LeftCover(120));

        Assert.True(LayerMerge.WouldBakePixels(upper, lower));
        LayerMerge.MergeDown(scene, upper, lower);

        using var merged = Render(lower, 0);
        Assert.True(merged.GetPixel(50, 120).Alpha > 0, "the covered span of the bar must survive");
        Assert.Equal(0, merged.GetPixel(160, 120).Alpha);
        Assert.True(merged.GetPixel(160, 60).Alpha > 0, "the lower drawing is untouched by the upper's mask");
    }

    [Fact]
    public void AMaskedLowerLayerBakesItsMaskAndClearsIt()
    {
        // Even the frames the upper layer never touches are baked — the merge
        // clears the mask, so a cleared mask must already be in the pixels.
        var scene = TestScene();
        var lower = LayerOf(Key(Bar(60, "#2040c0")));
        lower.Mask = new LayerMask();
        lower.Mask.Frame.Strokes.Add(LeftCover(60));
        var upper = LayerOf(Key(Bar(120, "#c02040")));

        Assert.True(LayerMerge.WouldBakePixels(upper, lower));
        LayerMerge.MergeDown(scene, upper, lower);

        Assert.Null(lower.Mask);
        using var merged = Render(lower, 0);
        Assert.True(merged.GetPixel(50, 60).Alpha > 0, "the covered span survives");
        Assert.Equal(0, merged.GetPixel(160, 60).Alpha);
        Assert.True(merged.GetPixel(160, 120).Alpha > 0, "the upper drawing is untouched by the lower's mask");
    }

    [Fact]
    public void AClippedUpperLayerBakesCarvedToTheLowersContent()
    {
        // The bars cross only where both have ink (same row): the clipped
        // upper survives on the lower's bar and vanishes off it.
        var scene = TestScene();
        var lower = LayerOf(Key(Bar(100, "#2040c0")));
        var upper = LayerOf(Key(Bar(100, "#c02040")), Key(Bar(40, "#c02040")));
        upper.ClipToBelow = true;

        Assert.True(LayerMerge.WouldBakePixels(upper, lower));
        LayerMerge.MergeDown(scene, upper, lower);

        using var first = Render(lower, 0);
        var over = first.GetPixel(100, 100);
        Assert.True(over.Alpha > 0 && over.Red > over.Blue,
            $"where the bars coincide the upper's colour wins ({over})");

        using var second = Render(lower, 1);
        Assert.Equal(0, second.GetPixel(100, 40).Alpha); // off the base: carved away
        Assert.True(second.GetPixel(100, 100).Alpha > 0, "the held lower bar still shows");
    }
}

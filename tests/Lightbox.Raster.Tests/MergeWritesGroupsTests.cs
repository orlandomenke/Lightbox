using Lightbox.Core.Documents;
using Lightbox.Core.Effects;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Timeline;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Q265: a merge keeps strokes. Where appending the upper drawing's strokes
/// would be a different picture, the merged drawing holds them in a group
/// instead of being flattened — and renders exactly what the two layers did.
/// </summary>
/// <remarks>
/// Each test makes both claims: the record (no baseline was made, the strokes
/// are still there) and the picture (byte for byte the composite from before
/// the merge). Either alone is the old behaviour: pixels that match, or
/// strokes that do not.
/// </remarks>
public class MergeWritesGroupsTests(ITestOutputHelper output)
{
    private const int W = 200;
    private const int H = 200;

    private static Scene SceneOf(params Layer[] layers) => new() { Width = W, Height = H, Layers = [.. layers] };

    private static Stroke Bar(double y, string color, ToolKind tool = ToolKind.Brush, double size = 24) => new()
    {
        Tool = tool,
        Color = color,
        Points = [new StrokePoint(20, y, 1), new StrokePoint(180, y, 1)],
        Brush = new BrushSettings { Size = size, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.1, AntiAlias = false },
    };

    private static Stroke Soft(double x, string color) => new()
    {
        Color = color,
        Points = [new StrokePoint(x, 30, 0.8), new StrokePoint(x + 20, 170, 1)],
        Brush = new BrushSettings { Size = 30, Hardness = 0.4, Opacity = 0.8, Flow = 0.6, Spacing = 0.1 },
    };

    private static Stroke Down(double x, ToolKind tool, double size = 30) => new()
    {
        Tool = tool,
        Color = "#000000",
        Points = [new StrokePoint(x, 20, 1), new StrokePoint(x, 180, 1)],
        Brush = new BrushSettings { Size = size, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.1, AntiAlias = false },
    };

    private static Layer LayerOf(params Stroke[] strokes) =>
        new() { Cels = [new Cel { Frame = new Frame { Strokes = [.. strokes] } }] };

    private static SKBitmap Blank() => new(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));

    /// <summary>
    /// The pair as the compositor draws it, through each layer's own pipeline:
    /// content → filter → mask → (clip) → style, then blend and opacity.
    /// </summary>
    private static SKBitmap Composite(Layer lower, Layer upper, int t = 0, bool clipped = false)
    {
        var bottom = Pipeline(lower, t, clipBase: null);
        using var clipBase = clipped ? ContentAndMask(lower, t) : null;
        using var top = Pipeline(upper, t, clipBase);
        using var canvas = new SKCanvas(bottom);
        using var paint = new SKPaint
        {
            BlendMode = BlendModes.ToSkia(upper.BlendMode),
            Color = SKColors.White.WithAlpha((byte)Math.Round(upper.Opacity * 255)),
        };
        canvas.DrawBitmap(top, 0, 0, paint);
        canvas.Flush();
        return bottom;
    }

    private static SKBitmap ContentAndMask(Layer layer, int t)
    {
        var bitmap = ExposureSheet.ExposedFrame(layer, t) is { } frame
            ? FrameRasterizer.Materialize(frame, W, H, celIndex: t)
            : Blank();
        if (layer.Mask is { Applies: true } mask)
        {
            using var coverage = FrameRasterizer.Materialize(mask.Frame, W, H);
            Carve(bitmap, coverage, mask.IsInverted);
        }
        return bitmap;
    }

    private static SKBitmap Pipeline(Layer layer, int t, SKBitmap? clipBase)
    {
        var bitmap = ExposureSheet.ExposedFrame(layer, t) is { } frame
            ? FrameRasterizer.Materialize(frame, W, H, celIndex: t)
            : Blank();
        var stack = layer.HasLiveEffects ? layer.Effects : null;
        Filter(bitmap, Effects.EffectRegistry.FilterFor(stack, t));
        if (layer.Mask is { Applies: true } mask)
        {
            using var coverage = FrameRasterizer.Materialize(mask.Frame, W, H);
            Carve(bitmap, coverage, mask.IsInverted);
        }
        if (clipBase is not null) Carve(bitmap, clipBase, inverted: false);
        Filter(bitmap, Effects.EffectRegistry.StyleFor(stack, t));
        return bitmap;
    }

    private static void Filter(SKBitmap target, SKImageFilter? filter)
    {
        if (filter is null) return;
        using var source = target.Copy();
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { ImageFilter = filter };
        canvas.DrawBitmap(source, 0, 0, paint);
        canvas.Flush();
    }

    private static void Carve(SKBitmap target, SKBitmap coverage, bool inverted)
    {
        using var canvas = new SKCanvas(target);
        using var paint = new SKPaint { BlendMode = inverted ? SKBlendMode.DstOut : SKBlendMode.DstIn };
        canvas.DrawBitmap(coverage, 0, 0, paint);
        canvas.Flush();
    }

    private void AssertMergedIs(SKBitmap expected, Layer merged, string what, int t = 0)
    {
        var frame = ExposureSheet.ExposedFrame(merged, t);
        using var actual = frame is null ? Blank() : FrameRasterizer.Materialize(frame, W, H, celIndex: t);
        var a = expected.Bytes;
        var b = actual.Bytes;
        var differing = 0;
        var worst = 0;
        var painted = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (i % 4 == 3 && a[i] != 0) painted++;
            if (a[i] == b[i]) continue;
            differing++;
            worst = Math.Max(worst, Math.Abs(a[i] - b[i]));
        }
        output.WriteLine($"{what}: {painted} painted pixels, {differing} differing bytes, worst {worst}");
        Assert.True(painted > 500, $"{what}: the expected picture is almost empty ({painted} pixels)");
        Assert.True(differing == 0, $"{what}: {differing} bytes differ, worst by {worst}");
    }

    private static Frame Merged(Layer lower, int t = 0) => (Frame)lower.Cels[t].Frame!;

    private static void AssertNoPixelsWereMade(Frame frame)
    {
        Assert.False(frame.HasBaseline, "the merge flattened this drawing");
        Assert.All(StrokeGroups.Flatten(frame.Strokes), s => Assert.NotEmpty(s.Points));
        foreach (var stroke in frame.Strokes)
        {
            if (stroke.Group is { } group) Assert.True(string.IsNullOrEmpty(group.PngBase64), "a merge never creates a baseline");
        }
    }

    [Fact]
    public void AnEraserOnTheUpperLayerIsKeptInAGroup()
    {
        var lower = LayerOf(Bar(80, "#2040c0"));
        var upper = LayerOf(Bar(100, "#c02040"), Down(100, ToolKind.Eraser, size: 40));
        var scene = SceneOf(lower, upper);
        using var expected = Composite(lower, upper);

        Assert.False(LayerMerge.WouldBakePixels(upper, lower, scene));
        LayerMerge.MergeDown(scene, upper, lower);

        var frame = Merged(lower);
        AssertNoPixelsWereMade(frame);
        AssertMergedIs(expected, lower, "eraser");
        // The lower drawing's stroke is still an ordinary stroke; the upper
        // drawing is one group after it, eraser and all.
        Assert.Equal(2, frame.Strokes.Count);
        Assert.Null(frame.Strokes[0].Group);
        var group = frame.Strokes[1].Group!;
        Assert.Equal([ToolKind.Brush, ToolKind.Eraser], group.Strokes.Select(s => s.Tool));
        // Nothing it does not need: a plain layer's group says only its strokes.
        Assert.Null(group.Blend);
        Assert.Null(group.Opacity);
        Assert.Null(group.Clip);
        Assert.Null(group.Mask);
        Assert.Null(group.Effects);
    }

    [Theory]
    [InlineData(LayerBlendMode.Multiply, 1.0)]
    [InlineData(LayerBlendMode.Screen, 0.6)]
    [InlineData(LayerBlendMode.Normal, 0.4)]
    public void ABlendModeOrOpacityIsCarriedByTheGroup(LayerBlendMode blend, double opacity)
    {
        var lower = LayerOf(Bar(80, "#2040c0"), Soft(40, "#806020"));
        var upper = LayerOf(Bar(100, "#c02040"), Soft(120, "#20a0a0"));
        upper.BlendMode = blend;
        upper.Opacity = opacity;
        var scene = SceneOf(lower, upper);
        using var expected = Composite(lower, upper);

        LayerMerge.MergeDown(scene, upper, lower);

        var frame = Merged(lower);
        AssertNoPixelsWereMade(frame);
        AssertMergedIs(expected, lower, $"{blend} at {opacity}");
        var group = frame.Strokes[^1].Group!;
        Assert.Equal(blend == LayerBlendMode.Normal ? null : blend, group.Blend);
        Assert.Equal(opacity < 1 ? opacity : null, group.Opacity);
    }

    [Theory]
    [InlineData("alpha lock")]
    [InlineData("smudge")]
    [InlineData("blur")]
    [InlineData("clear region")]
    public void AStrokeThatReadsOrRemovesWhatIsUnderItIsKeptInAGroup(string kind)
    {
        var reader = Down(100, ToolKind.Brush, size: 50);
        reader.Color = "#f0e020";
        switch (kind)
        {
            case "alpha lock": reader.AlphaLocked = true; break;
            case "smudge": reader.Brush.Kind = BrushKind.Smudge; break;
            case "blur": reader.Brush.Kind = BrushKind.Blur; break;
            case "clear region":
                reader.Tool = ToolKind.ClearRegion;
                reader.Points = [new(70, 60, 1), new(130, 60, 1), new(130, 140, 1), new(70, 140, 1)];
                break;
        }
        var lower = LayerOf(Bar(80, "#2040c0"));
        var upper = LayerOf(Bar(110, "#c02040"), reader);
        var scene = SceneOf(lower, upper);
        using var expected = Composite(lower, upper);

        Assert.False(LayerMerge.WouldBakePixels(upper, lower, scene));
        LayerMerge.MergeDown(scene, upper, lower);

        AssertNoPixelsWereMade(Merged(lower));
        AssertMergedIs(expected, lower, kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMaskOnTheUpperLayerIsCarriedByTheGroup(bool inverted)
    {
        var lower = LayerOf(Bar(60, "#2040c0"));
        var upper = LayerOf(Bar(100, "#c02040"), Bar(130, "#208040"));
        upper.Mask = new LayerMask { Frame = new Frame { Strokes = [Soft(90, "#000000")] }, Inverted = inverted ? true : null };
        var scene = SceneOf(lower, upper);
        using var expected = Composite(lower, upper);

        LayerMerge.MergeDown(scene, upper, lower);

        var frame = Merged(lower);
        AssertNoPixelsWereMade(frame);
        AssertMergedIs(expected, lower, inverted ? "inverted upper mask" : "upper mask");
        var mask = frame.Strokes[^1].Group!.Mask!;
        Assert.Equal(inverted, mask.IsInverted);
        // A copy: the layer it came from is about to be deleted, and undo
        // holds the original.
        Assert.NotSame(upper.Mask.Frame, mask.Frame);
    }

    [Fact]
    public void AMaskOnTheLowerLayerMovesIntoAGroupAndOffTheLayer()
    {
        var lower = LayerOf(Bar(60, "#2040c0"), Bar(140, "#806020"));
        lower.Mask = new LayerMask { Frame = new Frame { Strokes = [Soft(90, "#000000")] } };
        var upper = LayerOf(Bar(100, "#c02040"));
        var scene = SceneOf(lower, upper);
        using var expected = Composite(lower, upper);

        LayerMerge.MergeDown(scene, upper, lower);

        var frame = Merged(lower);
        AssertNoPixelsWereMade(frame);
        AssertMergedIs(expected, lower, "lower mask");
        // Kept on the layer it would carve the upper drawing too.
        Assert.Null(lower.Mask);
        Assert.NotNull(frame.Strokes[0].Group!.Mask);
        // The upper drawing needed nothing: plain strokes after the group.
        Assert.Null(frame.Strokes[1].Group);
    }

    [Fact]
    public void ALowerDrawingTheUpperLayerNeverTouchesStillGetsTheMask()
    {
        // The mask leaves the layer, so every drawing on it has to carry it —
        // including the ones the upper layer has no drawing over.
        var lower = new Layer
        {
            Cels =
            [
                new Cel { Frame = new Frame { Strokes = [Bar(60, "#2040c0")] } },
                new Cel { Frame = new Frame { Strokes = [Bar(140, "#806020")] } },
            ],
            Mask = new LayerMask { Frame = new Frame { Strokes = [Soft(90, "#000000")] } },
        };
        var upper = new Layer { Cels = [new Cel(), new Cel()] };
        var scene = SceneOf(lower, upper);
        using var expected = Composite(lower, upper, t: 1);

        LayerMerge.MergeDown(scene, upper, lower);

        AssertNoPixelsWereMade(Merged(lower, 1));
        AssertMergedIs(expected, lower, "untouched lower drawing", t: 1);
    }

    [Fact]
    public void AClippedUpperLayerIsCarriedAsAClippedGroup()
    {
        var lower = LayerOf(Soft(80, "#2040c0"));
        var upper = LayerOf(Bar(100, "#c02040", size: 60));
        upper.ClipToBelow = true;
        var scene = SceneOf(lower, upper);
        using var expected = Composite(lower, upper, clipped: true);

        Assert.False(LayerMerge.WouldBakePixels(upper, lower, scene));
        LayerMerge.MergeDown(scene, upper, lower);

        var frame = Merged(lower);
        AssertNoPixelsWereMade(frame);
        AssertMergedIs(expected, lower, "clip");
        Assert.True(frame.Strokes[^1].Group!.Clip);
    }

    private static EffectStack StackOf(string kind, params (string Key, double Value)[] values)
    {
        var use = new EffectUse { Kind = kind };
        foreach (var (key, value) in values) use.Params[key] = new EffectParam(value);
        return new EffectStack { Uses = [use] };
    }

    [Theory]
    [InlineData("blur.gaussian", "radius", 6.0)]
    [InlineData("style.outerGlow", "size", 12.0)]
    public void AnEffectStackOnEitherLayerIsCarriedByAGroup(string kind, string key, double value)
    {
        foreach (var onLower in new[] { false, true })
        {
            var lower = LayerOf(Bar(60, "#2040c0"));
            var upper = LayerOf(Bar(130, "#c02040"));
            (onLower ? lower : upper).Effects = StackOf(kind, (key, value));
            var scene = SceneOf(lower, upper);
            using var expected = Composite(lower, upper);

            Assert.False(LayerMerge.WouldBakePixels(upper, lower, scene));
            LayerMerge.MergeDown(scene, upper, lower);

            var frame = Merged(lower);
            AssertNoPixelsWereMade(frame);
            AssertMergedIs(expected, lower, $"{kind} on the {(onLower ? "lower" : "upper")} layer");
            // On the layer it would now filter both drawings.
            Assert.Null(lower.Effects);
            Assert.NotNull(frame.Strokes[onLower ? 0 : ^1].Group!.Effects);
        }
    }

    [Fact]
    public void AnUpperDrawingWithPixelsOfItsOwnKeepsThemUnderItsStrokes()
    {
        using var imported = FrameRasterizer.Materialize(new Frame { Strokes = [Soft(120, "#806020")] }, W, H);
        var lower = LayerOf(Bar(60, "#2040c0"));
        var upper = LayerOf(Bar(100, "#c02040"));
        upper.Cels[0].Frame!.PngBase64 = PngCodec.Encode(imported);
        var scene = SceneOf(lower, upper);
        using var expected = Composite(lower, upper);

        LayerMerge.MergeDown(scene, upper, lower);

        var frame = Merged(lower);
        Assert.False(frame.HasBaseline, "the upper drawing's pixels were flattened into the lower drawing");
        AssertMergedIs(expected, lower, "upper baseline");
        var group = frame.Strokes[^1].Group!;
        Assert.Equal(upper.Cels[0].Frame!.PngBase64, group.PngBase64);
        Assert.Single(group.Strokes);
    }

    [Fact]
    public void MergingTwiceNestsAndStillRendersThePicture()
    {
        var bottom = LayerOf(Bar(60, "#2040c0"));
        var middle = LayerOf(Soft(60, "#c02040"));
        var top = LayerOf(Soft(120, "#208040"), Bar(110, "#000000", ToolKind.Eraser, size: 30));
        var scene = SceneOf(bottom, middle, top);
        // Top into middle first, then the pair into bottom at Multiply.
        using var pair = Composite(middle, top);
        middle.BlendMode = LayerBlendMode.Multiply;
        using var expected = Pipeline(bottom, 0, null);
        using (var canvas = new SKCanvas(expected))
        using (var paint = new SKPaint { BlendMode = SKBlendMode.Multiply })
        {
            canvas.DrawBitmap(pair, 0, 0, paint);
        }

        LayerMerge.MergeDown(scene, top, middle);
        scene.Layers.Remove(top);
        LayerMerge.MergeDown(scene, middle, bottom);

        var frame = Merged(bottom);
        AssertNoPixelsWereMade(frame);
        AssertMergedIs(expected, bottom, "merged twice");
        var outer = frame.Strokes[^1].Group!;
        Assert.Equal(LayerBlendMode.Multiply, outer.Blend);
        Assert.Contains(outer.Strokes, s => s.Group is not null);
    }

    [Fact]
    public void TheInbetweenerCanReadAMergedDrawing()
    {
        // The reason this exists: merged line art used to reach the AI as
        // nothing at all, because it was pixels.
        var lower = LayerOf(Bar(60, "#2040c0"));
        var upper = LayerOf(Bar(100, "#c02040"), Bar(140, "#208040"), Down(100, ToolKind.Eraser, size: 10));
        var scene = SceneOf(lower, upper);

        LayerMerge.MergeDown(scene, upper, lower);

        var read = StrokeRecordCleaner.EffectiveStrokes(Merged(lower).Strokes);
        Assert.Equal(3, read.Count);
        Assert.All(read, s => Assert.Equal(ToolKind.Brush, s.Tool));
        Assert.All(read, s => Assert.Equal(2, s.Points.Count));
    }

    [Fact]
    public void AMergedDocumentIsMarkedAsNeedingABuildThatReadsGroups()
    {
        var lower = LayerOf(Bar(80, "#2040c0"));
        var upper = LayerOf(Bar(100, "#c02040"), Down(100, ToolKind.Eraser));
        var doc = new Doc { Scene = SceneOf(lower, upper) };
        Assert.Equal(1, doc.RequiredVersion);

        LayerMerge.MergeDown(doc.Scene, upper, lower);
        doc.Scene.Layers.Remove(upper);

        Assert.Equal(2, doc.RequiredVersion);
    }

    [Fact]
    public void PlainStrokesStillMergeAsPlainStrokes()
    {
        // The common case must not start paying for a group it does not need.
        var lower = LayerOf(Bar(80, "#2040c0"));
        var upper = LayerOf(Bar(100, "#c02040"));
        var doc = new Doc { Scene = SceneOf(lower, upper) };

        LayerMerge.MergeDown(doc.Scene, upper, lower);
        doc.Scene.Layers.Remove(upper);

        Assert.All(Merged(lower).Strokes, s => Assert.Null(s.Group));
        Assert.Equal(1, doc.RequiredVersion);
    }

    // ---- what still flattens, each for a reason the design note names ----------

    [Fact]
    public void ASymbolPlacedOnADrawingThatNeedsAGroupStillFlattens()
    {
        // A placement lives on the frame and draws over every stroke of it; a
        // group has no slot for one, so the upper layer's blend would stop
        // applying to its symbol.
        var lower = LayerOf(Bar(80, "#2040c0"));
        var upper = LayerOf(Bar(100, "#c02040"));
        upper.BlendMode = LayerBlendMode.Multiply;
        upper.Cels[0].Frame!.Placements = [new SymbolPlacement { SymbolId = "missing" }];
        var scene = SceneOf(lower, upper);

        Assert.True(LayerMerge.WouldBakePixels(upper, lower, scene));
        LayerMerge.MergeDown(scene, upper, lower);

        Assert.True(Merged(lower).HasBaseline);
    }

    [Fact]
    public void AStrokeSamplingTheLayersBeneathItLiveStillFlattens()
    {
        // "Beneath" included the lower layer. Inside the merged drawing it no
        // longer does, and the stroke would smudge a different picture.
        var smudge = Down(100, ToolKind.Brush);
        smudge.Brush.Kind = BrushKind.Smudge;
        smudge.Brush.SampleSource = SampleSource.AllLayersLive;
        var lower = LayerOf(Bar(80, "#2040c0"));
        var upper = LayerOf(Bar(100, "#c02040"), smudge);
        var scene = SceneOf(lower, upper);

        Assert.True(LayerMerge.WouldBakePixels(upper, lower, scene));
        LayerMerge.MergeDown(scene, upper, lower);

        Assert.True(Merged(lower).HasBaseline);
    }

    [Fact]
    public void AClipOntoALayerWithEffectsStillFlattens()
    {
        // The compositor carves a clipped layer by its base's content, before
        // the base's effects. A clipped group is carved by what is on the
        // drawing — after them. Not the same shape, so not a group.
        var lower = LayerOf(Soft(80, "#2040c0"));
        lower.Effects = StackOf("blur.gaussian", ("radius", 8.0));
        var upper = LayerOf(Bar(100, "#c02040", size: 60));
        upper.ClipToBelow = true;
        var scene = SceneOf(lower, upper);

        Assert.True(LayerMerge.WouldBakePixels(upper, lower, scene));
        LayerMerge.MergeDown(scene, upper, lower);

        Assert.True(Merged(lower).HasBaseline);
    }
}

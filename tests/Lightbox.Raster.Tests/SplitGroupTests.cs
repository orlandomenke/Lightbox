using Lightbox.Core.Documents;
using Lightbox.Core.Effects;
using Lightbox.Core.Timeline;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Q268: Split group takes a merge back out. Every cel's group from one merged
/// layer goes onto a new layer directly above, carrying what that layer did —
/// and the pair of layers composites to what the merged drawing showed.
/// </summary>
/// <remarks>
/// The oracle is the same as the merge's: the two layers composited by hand
/// through each layer's own pipeline. A split that is exact composites the
/// split pair exactly as the merged drawing rendered, byte for byte.
/// </remarks>
public class SplitGroupTests(ITestOutputHelper output)
{
    private const int W = 200;
    private const int H = 200;

    private static Stroke Bar(double y, string color, ToolKind tool = ToolKind.Brush, double size = 24) => new()
    {
        Tool = tool,
        Color = color,
        Points = [new StrokePoint(20, y, 1), new StrokePoint(180, y, 1)],
        Brush = new BrushSettings { Size = size, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.1, AntiAlias = false },
    };

    private static Stroke Down(double x, ToolKind tool = ToolKind.Brush, double size = 30) => new()
    {
        Tool = tool,
        Color = "#208040",
        Points = [new StrokePoint(x, 20, 1), new StrokePoint(x, 180, 1)],
        Brush = new BrushSettings { Size = size, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.1, AntiAlias = false },
    };

    private static Stroke Soft(double x, string color) => new()
    {
        Color = color,
        Points = [new StrokePoint(x, 30, 0.8), new StrokePoint(x + 20, 170, 1)],
        Brush = new BrushSettings { Size = 30, Hardness = 0.4, Opacity = 0.8, Flow = 0.6, Spacing = 0.1 },
    };

    private static Layer LayerOf(string name, params Cel[] cels) => new() { Name = name, Cels = [.. cels] };

    private static Cel Key(params Stroke[] strokes) => new() { Frame = new Frame { Strokes = [.. strokes] } };

    private static Scene SceneOf(params Layer[] layers) => new() { Width = W, Height = H, Layers = [.. layers] };

    private static SKBitmap Blank() => new(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));

    /// <summary>A stack of layers as the compositor draws it, onto transparent paper.</summary>
    private static SKBitmap Stack(IEnumerable<Layer> layers, int t)
    {
        var result = Blank();
        SKBitmap? below = null;
        foreach (var layer in layers)
        {
            using var content = ExposureSheet.ExposedFrame(layer, t) is { } frame
                ? FrameRasterizer.Materialize(frame, W, H, celIndex: t)
                : Blank();
            using var pass = content.Copy();
            var stack = layer.HasLiveEffects ? layer.Effects : null;
            Filter(pass, Effects.EffectRegistry.FilterFor(stack, t));
            if (layer.Mask is { Applies: true } mask)
            {
                using var coverage = FrameRasterizer.Materialize(mask.Frame, W, H);
                Carve(pass, coverage, mask.IsInverted);
            }
            if (layer.IsClipped && below is not null) Carve(pass, below, inverted: false);
            Filter(pass, Effects.EffectRegistry.StyleFor(stack, t));
            using (var canvas = new SKCanvas(result))
            using (var paint = new SKPaint
            {
                BlendMode = BlendModes.ToSkia(layer.BlendMode),
                Color = SKColors.White.WithAlpha((byte)Math.Round(layer.Opacity * 255)),
            })
            {
                canvas.DrawBitmap(pass, 0, 0, paint);
            }
            // A clip's base is the layer's content and mask, before its effects.
            below?.Dispose();
            below = content.Copy();
            if (layer.Mask is { Applies: true } baseMask)
            {
                using var coverage = FrameRasterizer.Materialize(baseMask.Frame, W, H);
                Carve(below, coverage, baseMask.IsInverted);
            }
        }
        below?.Dispose();
        return result;
    }

    private static void Filter(SKBitmap target, SKImageFilter? filter)
    {
        if (filter is null) return;
        using var source = target.Copy();
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { ImageFilter = filter };
        canvas.DrawBitmap(source, 0, 0, paint);
    }

    private static void Carve(SKBitmap target, SKBitmap coverage, bool inverted)
    {
        using var canvas = new SKCanvas(target);
        using var paint = new SKPaint { BlendMode = inverted ? SKBlendMode.DstOut : SKBlendMode.DstIn };
        canvas.DrawBitmap(coverage, 0, 0, paint);
    }

    private void AssertSame(SKBitmap expected, SKBitmap actual, string what)
    {
        var a = expected.Bytes;
        var b = actual.Bytes;
        var differing = 0;
        var painted = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (i % 4 == 3 && a[i] != 0) painted++;
            if (a[i] != b[i]) differing++;
        }
        output.WriteLine($"{what}: {painted} painted pixels, {differing} differing bytes");
        Assert.True(painted > 500, $"{what}: the expected picture is almost empty");
        Assert.True(differing == 0, $"{what}: {differing} bytes differ");
    }

    private static Stroke GroupIn(Layer layer, int cel = 0) =>
        layer.Cels[cel].Frame!.Strokes.Last(s => s.Group is not null);

    /// <summary>Merge <paramref name="upper"/> into <paramref name="lower"/> and take it out of the scene, as the command does.</summary>
    private static void Merge(Scene scene, Layer upper, Layer lower)
    {
        LayerMerge.MergeDown(scene, upper, lower);
        scene.Layers.Remove(upper);
    }

    [Fact]
    public void AGroupRemembersTheLayerItCameFrom()
    {
        var lower = LayerOf("Ink", Key(Bar(60, "#2040c0")));
        var upper = LayerOf("Lines-Back", Key(Bar(100, "#c02040"), Down(100, ToolKind.Eraser)));
        var scene = SceneOf(lower, upper);

        Merge(scene, upper, lower);

        var group = GroupIn(lower).Group!;
        Assert.Equal(upper.Id, group.From);
        Assert.Equal("Lines-Back", group.Name);
    }

    [Fact]
    public void SplittingAMergeGivesBackTheLayerAndThePicture()
    {
        var lower = LayerOf("Ink", Key(Bar(60, "#2040c0"), Soft(40, "#806020")));
        var upper = LayerOf("Shade", Key(Bar(100, "#c02040"), Soft(120, "#20a0a0"), Down(130, ToolKind.Eraser, size: 10)));
        upper.BlendMode = LayerBlendMode.Multiply;
        upper.Opacity = 0.7;
        upper.Mask = new LayerMask { Frame = new Frame { Strokes = [Soft(90, "#000000")] } };
        var scene = SceneOf(lower, upper);
        using var before = Stack(scene.Layers, 0);
        Merge(scene, upper, lower);

        var result = GroupSplit.Split(scene, lower, GroupIn(lower));

        Assert.NotNull(result);
        Assert.Equal(2, scene.Layers.Count);
        var split = scene.Layers[1];
        Assert.Same(result!.Layer, split);
        Assert.Equal("Shade", split.Name);
        Assert.Equal(LayerBlendMode.Multiply, split.BlendMode);
        Assert.Equal(0.7, split.Opacity);
        Assert.NotNull(split.Mask);
        // Loose strokes again, not a group on a layer of its own.
        Assert.All(split.Cels[0].Frame!.Strokes, s => Assert.Null(s.Group));
        Assert.Equal(3, split.Cels[0].Frame!.Strokes.Count);
        Assert.All(lower.Cels[0].Frame!.Strokes, s => Assert.Null(s.Group));
        Assert.Equal(0, result.MarksNowUnder);

        using var after = Stack(scene.Layers, 0);
        AssertSame(before, after, "split");
    }

    [Fact]
    public void ASplitTakesTheMergeOutOfEveryCelAndKeepsTheTiming()
    {
        // Lower keys on 1s, upper on 2s: the merge keyed every cel, and the
        // split layer keys where the merged one did and holds where it held.
        var lower = LayerOf("Ink", Key(Bar(40, "#2040c0")), Key(Bar(60, "#2040c0")), Key(Bar(80, "#2040c0")), Key(Bar(100, "#2040c0")));
        var upper = LayerOf("Arm", Key(Down(60, ToolKind.Eraser, 10), Bar(120, "#c02040")), new Cel(), Key(Down(80, ToolKind.Eraser, 10), Bar(140, "#c02040")), new Cel());
        upper.Opacity = 0.5;
        var scene = SceneOf(lower, upper);
        var pictures = Enumerable.Range(0, 4).Select(t => Stack(scene.Layers, t)).ToList();
        Merge(scene, upper, lower);

        var result = GroupSplit.Split(scene, lower, GroupIn(lower));

        Assert.Equal(4, result!.Cels);
        var split = scene.Layers[1];
        Assert.Equal(lower.Cels.Count, split.Cels.Count);
        for (var t = 0; t < 4; t++)
        {
            using var after = Stack(scene.Layers, t);
            AssertSame(pictures[t], after, $"cel {t}");
            pictures[t].Dispose();
            Assert.DoesNotContain(lower.Cels[t].Frame?.Strokes ?? [], s => s.Group is not null);
        }
    }

    [Fact]
    public void OnlyTheGroupsFromThatMergeComeOut()
    {
        var bottom = LayerOf("Ink", Key(Bar(40, "#2040c0")));
        var shade = LayerOf("Shade", Key(Bar(100, "#c02040")));
        shade.BlendMode = LayerBlendMode.Multiply;
        var light = LayerOf("Light", Key(Bar(140, "#f0e020")));
        light.BlendMode = LayerBlendMode.Screen;
        var scene = SceneOf(bottom, shade, light);
        Merge(scene, light, shade);
        Merge(scene, shade, bottom);

        // The outer group came from Shade and holds Light's group inside it.
        var result = GroupSplit.Split(scene, bottom, GroupIn(bottom));

        Assert.Equal("Shade", result!.Layer.Name);
        Assert.Equal(LayerBlendMode.Multiply, result.Layer.BlendMode);
        Assert.Contains(result.Layer.Cels[0].Frame!.Strokes, s => s.Group?.Name == "Light");
        Assert.DoesNotContain(bottom.Cels[0].Frame!.Strokes, s => s.Group is not null);
    }

    [Fact]
    public void AnEraserDrawnAfterTheMergeKeepsErasingBoth()
    {
        // Rubbed out across both drawings after the merge: split, the line
        // must stay rubbed out on both layers.
        var lower = LayerOf("Ink", Key(Bar(80, "#2040c0")));
        var upper = LayerOf("Lines", Key(Bar(120, "#c02040"), Down(150, ToolKind.Eraser, 10)));
        var scene = SceneOf(lower, upper);
        Merge(scene, upper, lower);
        lower.Cels[0].Frame!.Strokes.Add(Down(100, ToolKind.Eraser, 30));
        using var before = Stack(scene.Layers, 0);

        var result = GroupSplit.Split(scene, lower, GroupIn(lower));

        Assert.Equal(0, result!.MarksNowUnder);
        Assert.Contains(result.Layer.Cels[0].Frame!.Strokes, s => s.Tool == ToolKind.Eraser && s.Points[0].X == 100);
        using var after = Stack(scene.Layers, 0);
        AssertSame(before, after, "eraser after the merge");
    }

    [Fact]
    public void AMarkDrawnOverTheGroupAfterTheMergeIsCounted()
    {
        var lower = LayerOf("Ink", Key(Bar(80, "#2040c0")));
        var upper = LayerOf("Lines", Key(Bar(120, "#c02040"), Down(150, ToolKind.Eraser, 10)));
        var scene = SceneOf(lower, upper);
        Merge(scene, upper, lower);
        lower.Cels[0].Frame!.Strokes.Add(Down(100));                 // over the group
        lower.Cels[0].Frame!.Strokes.Add(new Stroke
        {
            Color = "#000000",
            Points = [new StrokePoint(4, 4, 1), new StrokePoint(10, 4, 1)],
            Brush = new BrushSettings { Size = 4 },
        });                                                          // nowhere near it

        var result = GroupSplit.Split(scene, lower, GroupIn(lower));

        Assert.Equal(1, result!.MarksNowUnder);
        Assert.Equal(3, lower.Cels[0].Frame!.Strokes.Count);
    }

    [Fact]
    public void AClippedLayerSplitsBackClipped()
    {
        var lower = LayerOf("Skin", Key(Soft(80, "#2040c0")));
        var upper = LayerOf("Shadow", Key(Bar(100, "#c02040", size: 60)));
        upper.ClipToBelow = true;
        var scene = SceneOf(lower, upper);
        using var before = Stack(scene.Layers, 0);
        Merge(scene, upper, lower);

        var result = GroupSplit.Split(scene, lower, GroupIn(lower));

        Assert.True(result!.Layer.IsClipped);
        using var after = Stack(scene.Layers, 0);
        AssertSame(before, after, "clip");
    }

    [Fact]
    public void AMaskThatDiffersBetweenCelsStaysWithEachDrawing()
    {
        // A transform of one cel moves that cel's copy of the mask; the
        // layer has one mask. Lifting either copy would carve the other cel
        // by the wrong shape, so each drawing keeps its own in a group.
        var lower = LayerOf("Ink", Key(Bar(60, "#2040c0")), Key(Bar(60, "#2040c0")));
        var upper = LayerOf("Lines", Key(Bar(100, "#c02040")), Key(Bar(120, "#c02040")));
        upper.Mask = new LayerMask { Frame = new Frame { Strokes = [Soft(90, "#000000")] } };
        var scene = SceneOf(lower, upper);
        Merge(scene, upper, lower);
        var moved = GroupIn(lower, 1);
        Lightbox.Core.Geometry.TransformOps.TransformStroke(moved, (x, y) => (x + 30, y));
        using var before0 = Stack(scene.Layers, 0);
        using var before1 = Stack(scene.Layers, 1);

        var result = GroupSplit.Split(scene, lower, GroupIn(lower));

        Assert.Null(result!.Layer.Mask);
        Assert.All(new[] { 0, 1 }, t => Assert.NotNull(result.Layer.Cels[t].Frame!.Strokes.Single().Group!.Mask));
        using var after0 = Stack(scene.Layers, 0);
        using var after1 = Stack(scene.Layers, 1);
        AssertSame(before0, after0, "cel 0");
        AssertSame(before1, after1, "cel 1");
    }

    [Fact]
    public void AGroupNestedInsideAnotherIsNotSplitFromHere()
    {
        // Only a group at the top of a drawing's list is a layer's worth of
        // drawing; one inside another is part of that one.
        var bottom = LayerOf("Ink", Key(Bar(40, "#2040c0")));
        var shade = LayerOf("Shade", Key(Bar(100, "#c02040")));
        shade.BlendMode = LayerBlendMode.Multiply;
        var light = LayerOf("Light", Key(Bar(140, "#f0e020")));
        light.BlendMode = LayerBlendMode.Screen;
        var scene = SceneOf(bottom, shade, light);
        Merge(scene, light, shade);
        Merge(scene, shade, bottom);
        var inner = GroupIn(bottom).Group!.Strokes.Last(s => s.Group is not null);

        Assert.Null(GroupSplit.Split(scene, bottom, inner));
        Assert.Single(scene.Layers);
    }
}

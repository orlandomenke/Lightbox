using Lightbox.Core.Documents;
using Lightbox.Core.Effects;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Q265, drawing a group: a group in a stroke list renders as the layer it was
/// did — its strokes on a surface of their own, then that surface put onto the
/// drawing through the blend, opacity, clip, mask and effects it carries.
/// </summary>
/// <remarks>
/// The oracle throughout is the composite built by hand from two separate
/// renders, which is what two layers are. Every comparison is byte for byte:
/// a group that is nearly the layer it replaced is a merge that changed the
/// picture.
/// </remarks>
public class StrokeGroupRenderTests(ITestOutputHelper output)
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

    /// <summary>A soft, anti-aliased mark, so a blend or a carve has partial alpha to get wrong.</summary>
    private static Stroke Soft(double x, string color) => new()
    {
        Color = color,
        Points = [new StrokePoint(x, 30, 0.8), new StrokePoint(x + 20, 170, 1)],
        Brush = new BrushSettings { Size = 30, Hardness = 0.4, Opacity = 0.8, Flow = 0.6, Spacing = 0.1 },
    };

    private static Stroke Group(StrokeGroup group) => new() { Tool = ToolKind.Group, Group = group };

    private static Stroke Group(params Stroke[] strokes) => Group(new StrokeGroup { Strokes = [.. strokes] });

    private static SKBitmap Render(params Stroke[] strokes) =>
        FrameRasterizer.Materialize(new Frame { Strokes = [.. strokes] }, W, H);

    /// <summary>
    /// What two layers composite to: the lower render, the upper render run
    /// through the layer pipeline (filter, mask, clip, style), then drawn over
    /// with the blend and opacity.
    /// </summary>
    private static SKBitmap Composite(
        Stroke[] lower, Stroke[] upper, LayerBlendMode blend = LayerBlendMode.Normal, double opacity = 1,
        bool clip = false, LayerMask? mask = null, EffectStack? effects = null, int t = 0,
        double scale = 1, SKPointI origin = default)
    {
        var bottom = FrameRasterizer.Materialize(new Frame { Strokes = [.. lower] }, W, H, scale, t, origin: origin);
        using var top = FrameRasterizer.Materialize(new Frame { Strokes = [.. upper] }, W, H, scale, t, origin: origin);
        Filter(top, Effects.EffectRegistry.FilterFor(effects, t, (float)scale));
        if (mask is not null)
        {
            using var coverage = FrameRasterizer.Materialize(mask.Frame, W, H, scale, origin: origin);
            Carve(top, coverage, mask.IsInverted);
        }
        if (clip) Carve(top, bottom, inverted: false);
        Filter(top, Effects.EffectRegistry.StyleFor(effects, t, (float)scale));
        using var canvas = new SKCanvas(bottom);
        using var paint = new SKPaint
        {
            BlendMode = BlendModes.ToSkia(blend),
            Color = SKColors.White.WithAlpha((byte)Math.Round(opacity * 255)),
        };
        canvas.DrawBitmap(top, 0, 0, paint);
        canvas.Flush();
        return bottom;
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

    private void AssertSame(SKBitmap expected, SKBitmap actual, string what)
    {
        var a = expected.Bytes;
        var b = actual.Bytes;
        Assert.Equal(a.Length, b.Length);
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
        // Present as well as equal: two empty pictures are also the same.
        Assert.True(painted > 500, $"{what}: the expected picture is almost empty ({painted} pixels)");
        Assert.True(differing == 0, $"{what}: {differing} bytes differ, worst by {worst}");
    }

    private static int AlphaAt(SKBitmap bitmap, int x, int y) => bitmap.GetPixel(x, y).Alpha;

    [Fact]
    public void AGroupOfPlainStrokesIsThoseStrokes()
    {
        using var inline = Render(Bar(60, "#2040c0"), Soft(60, "#c02040"), Bar(140, "#208040"));
        using var grouped = Render(Bar(60, "#2040c0"), Group(Soft(60, "#c02040"), Bar(140, "#208040")));

        AssertSame(inline, grouped, "plain group");
    }

    [Fact]
    public void AnEraserInsideAGroupReachesOnlyThatGroup()
    {
        // The whole reason erasing forced pixels: appended after the lower
        // drawing's strokes, the upper layer's eraser eats them too. Inside a
        // group it has only the group's strokes to reach.
        var eraser = Bar(100, "#000000", ToolKind.Eraser, size: 40);
        eraser.Points = [new StrokePoint(100, 20, 1), new StrokePoint(100, 180, 1)];
        Stroke[] lower = [Bar(80, "#2040c0")];
        Stroke[] upper = [Bar(100, "#c02040"), eraser];

        using var expected = Composite(lower, upper);
        using var grouped = Render([.. lower, Group(upper)]);
        using var appended = Render([.. lower, .. upper]);

        AssertSame(expected, grouped, "eraser in a group");
        // And the difference is real: where the eraser crosses the lower bar
        // the lower ink is still there in the group and gone when appended.
        output.WriteLine($"lower ink under the eraser: grouped {AlphaAt(grouped, 100, 74)}, appended {AlphaAt(appended, 100, 74)}");
        Assert.Equal(255, AlphaAt(grouped, 100, 74));
        Assert.Equal(0, AlphaAt(appended, 100, 74));
    }

    [Theory]
    [InlineData(LayerBlendMode.Multiply, 1.0)]
    [InlineData(LayerBlendMode.Screen, 0.6)]
    [InlineData(LayerBlendMode.Normal, 0.35)]
    public void AGroupLandsThroughItsBlendAndOpacity(LayerBlendMode blend, double opacity)
    {
        Stroke[] lower = [Bar(80, "#2040c0"), Soft(40, "#806020")];
        Stroke[] upper = [Bar(100, "#c02040"), Soft(120, "#20a0a0")];

        using var expected = Composite(lower, upper, blend, opacity);
        using var grouped = Render([.. lower, Group(new StrokeGroup
        {
            Strokes = [.. upper],
            Blend = blend == LayerBlendMode.Normal ? null : blend,
            Opacity = opacity < 1 ? opacity : null,
        })]);

        AssertSame(expected, grouped, $"{blend} at {opacity}");
    }

    [Fact]
    public void AnAlphaLockedStrokeInAGroupPaintsOnlyOnTheGroupsOwnInk()
    {
        var locked = Bar(90, "#f0e020", size: 60);
        locked.AlphaLocked = true;
        Stroke[] lower = [Bar(60, "#2040c0")];
        Stroke[] upper = [Bar(110, "#c02040"), locked];

        using var expected = Composite(lower, upper);
        using var grouped = Render([.. lower, Group(upper)]);

        AssertSame(expected, grouped, "alpha lock in a group");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AGroupIsCarvedByItsMask(bool inverted)
    {
        var mask = new LayerMask { Frame = new Frame { Strokes = [Soft(90, "#000000")] }, Inverted = inverted ? true : null };
        Stroke[] lower = [Bar(60, "#2040c0")];
        Stroke[] upper = [Bar(100, "#c02040"), Bar(130, "#208040")];

        using var expected = Composite(lower, upper, mask: mask);
        using var grouped = Render([.. lower, Group(new StrokeGroup { Strokes = [.. upper], Mask = mask })]);

        AssertSame(expected, grouped, inverted ? "inverted mask" : "mask");
    }

    [Fact]
    public void ADisabledMaskCarvesNothing()
    {
        var mask = new LayerMask { Frame = new Frame { Strokes = [Soft(90, "#000000")] }, Disabled = true };
        Stroke[] upper = [Bar(100, "#c02040")];

        using var expected = Render(upper);
        using var grouped = Render(Group(new StrokeGroup { Strokes = [.. upper], Mask = mask }));

        AssertSame(expected, grouped, "disabled mask");
    }

    [Theory]
    [InlineData(LayerBlendMode.Normal)]
    [InlineData(LayerBlendMode.Multiply)]
    public void AClippedGroupLandsOnlyWhereThereIsPaintBeneathIt(LayerBlendMode blend)
    {
        Stroke[] lower = [Soft(80, "#2040c0")];
        Stroke[] upper = [Bar(100, "#c02040", size: 60)];

        using var expected = Composite(lower, upper, blend, clip: true);
        using var grouped = Render([.. lower, Group(new StrokeGroup
        {
            Strokes = [.. upper],
            Clip = true,
            Blend = blend == LayerBlendMode.Normal ? null : blend,
        })]);

        AssertSame(expected, grouped, $"clip, {blend}");
        // Off the lower mark there is nothing to land on.
        Assert.Equal(0, AlphaAt(grouped, 20, 100));
    }

    private static EffectStack Blur(double radius, double? radiusLater = null)
    {
        var param = new EffectParam(radius);
        if (radiusLater is { } later)
        {
            param.Keys = [new EffectKey { Frame = 0, Value = radius }, new EffectKey { Frame = 10, Value = later }];
        }
        var use = new EffectUse { Kind = "blur.gaussian" };
        use.Params["radius"] = param;
        return new EffectStack { Uses = [use] };
    }

    [Fact]
    public void AGroupsEffectsFilterItsOwnResultOnly()
    {
        var effects = Blur(6);
        Stroke[] lower = [Bar(60, "#2040c0")];
        Stroke[] upper = [Bar(120, "#c02040")];

        using var expected = Composite(lower, upper, effects: effects);
        using var grouped = Render([.. lower, Group(new StrokeGroup { Strokes = [.. upper], Effects = effects })]);
        using var plain = Render([.. lower, Group(upper)]);

        AssertSame(expected, grouped, "blurred group");
        // The effect did something, and not to the lower mark: its hard edge
        // is still hard.
        Assert.False(plain.Bytes.AsSpan().SequenceEqual(grouped.Bytes), "the blur changed nothing");
        Assert.Equal(AlphaAt(plain, 100, 47), AlphaAt(grouped, 100, 47));
        Assert.Equal(AlphaAt(plain, 100, 49), AlphaAt(grouped, 100, 49));
    }

    [Fact]
    public void AnAnimatedEffectInAGroupIsReadAtTheCelBeingDrawn()
    {
        var effects = Blur(1, radiusLater: 12);
        Stroke[] upper = [Bar(120, "#c02040")];
        var frame = new Frame { Strokes = [Group(new StrokeGroup { Strokes = [.. upper], Effects = effects })] };

        using var expectedLate = Composite([], upper, effects: effects, t: 10);
        using var early = FrameRasterizer.Materialize(frame, W, H, celIndex: 0);
        using var late = FrameRasterizer.Materialize(frame, W, H, celIndex: 10);

        AssertSame(expectedLate, late, "animated blur at cel 10");
        Assert.False(early.Bytes.AsSpan().SequenceEqual(late.Bytes), "the two cels drew the same blur");
    }

    [Fact]
    public void AGroupCarriesItsOwnBaselineUnderItsStrokes()
    {
        using var under = Render(Soft(60, "#806020"));
        var png = PngCodec.Encode(under);
        Stroke[] upper = [Bar(100, "#c02040", ToolKind.Eraser)];

        using var expected = FrameRasterizer.Materialize(new Frame { PngBase64 = png, Strokes = [.. upper] }, W, H);
        using var grouped = Render(Group(new StrokeGroup { PngBase64 = png, Strokes = [.. upper] }));

        AssertSame(expected, grouped, "group baseline");
    }

    [Fact]
    public void AGroupInsideAGroupIsALayerMergedTwice()
    {
        Stroke[] bottom = [Bar(60, "#2040c0")];
        Stroke[] middle = [Soft(60, "#c02040")];
        Stroke[] top = [Bar(110, "#f0e020", ToolKind.Eraser, size: 30), Soft(120, "#208040")];

        // Top merged into middle (it erases, so a group), then that into bottom at Multiply.
        using var pair = Composite(middle, top);
        using var expected = FrameRasterizer.Materialize(new Frame { Strokes = [.. bottom] }, W, H);
        using (var canvas = new SKCanvas(expected))
        using (var paint = new SKPaint { BlendMode = SKBlendMode.Multiply })
        {
            canvas.DrawBitmap(pair, 0, 0, paint);
        }

        using var grouped = Render([.. bottom, Group(new StrokeGroup
        {
            Strokes = [.. middle, Group(top)],
            Blend = LayerBlendMode.Multiply,
        })]);

        AssertSame(expected, grouped, "nested group");
    }

    [Fact]
    public void AStrokeAfterAGroupActsOnTheWholeDrawing()
    {
        // Erasing "the merged layer" erases all of it — both drawings.
        var eraser = Bar(100, "#000000", ToolKind.Eraser, size: 30);
        eraser.Points = [new StrokePoint(100, 20, 1), new StrokePoint(100, 180, 1)];

        using var grouped = Render(Bar(80, "#2040c0"), Group(Bar(120, "#c02040")), eraser);

        Assert.Equal(0, AlphaAt(grouped, 100, 80));
        Assert.Equal(0, AlphaAt(grouped, 100, 120));
        Assert.Equal(255, AlphaAt(grouped, 40, 80));
        Assert.Equal(255, AlphaAt(grouped, 40, 120));
    }

    [Theory]
    [InlineData(2.0, 0, 0)]
    [InlineData(1.5, 0, 0)]
    [InlineData(1.0, -30, 20)]
    [InlineData(2.0, -30, 20)]
    public void AGroupRendersSharpAtAnOutputScaleAndOnGrownPaper(double scale, int left, int top)
    {
        var origin = new SKPointI(left, top);
        var mask = new LayerMask { Frame = new Frame { Strokes = [Soft(90, "#000000")] } };
        Stroke[] lower = [Bar(80, "#2040c0"), Soft(40, "#806020")];
        Stroke[] upper = [Bar(100, "#c02040"), Soft(120, "#20a0a0")];

        using var expected = Composite(lower, upper, LayerBlendMode.Multiply, 0.7, mask: mask, scale: scale, origin: origin);
        using var grouped = FrameRasterizer.Materialize(
            new Frame
            {
                Strokes =
                [
                    .. lower,
                    Group(new StrokeGroup { Strokes = [.. upper], Blend = LayerBlendMode.Multiply, Opacity = 0.7, Mask = mask }),
                ],
            },
            W, H, scale, origin: origin);

        AssertSame(expected, grouped, $"scale {scale}, origin {left},{top}");
    }

    [Fact]
    public void RepaintingARegionOfADrawingWithAGroupGivesTheSamePixels()
    {
        // Invariant 6, and the reason undo is bounded (B327): a rectangle is
        // rebuilt from the strokes that reach it. A group has to take part in
        // that — and land exactly what a full render lands.
        var eraser = Bar(100, "#000000", ToolKind.Eraser, size: 30);
        eraser.Points = [new StrokePoint(100, 20, 1), new StrokePoint(100, 180, 1)];
        var frame = new Frame
        {
            Strokes =
            [
                Bar(80, "#2040c0"), Soft(40, "#806020"),
                Group(new StrokeGroup { Strokes = [Bar(100, "#c02040"), eraser, Soft(120, "#20a0a0")], Blend = LayerBlendMode.Multiply, Opacity = 0.7 }),
                Soft(150, "#a02080"),
            ],
        };
        using var full = FrameRasterizer.Materialize(frame, W, H);
        using var patched = full.Copy();
        var region = new SKRectI(70, 60, 150, 140);
        using (var canvas = new SKCanvas(patched))
        using (var paint = new SKPaint { Color = SKColors.Lime, BlendMode = SKBlendMode.Src })
        {
            canvas.DrawRect(SKRect.Create(region.Left, region.Top, region.Width, region.Height), paint);
        }

        Assert.True(FrameRasterizer.CanRepaintRegion(frame));
        Assert.True(FrameRasterizer.RepaintRegion(patched, frame, region, W, H));

        AssertSame(full, patched, "region repaint through a group");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void RepaintingARegionThroughAGroupCostsTheRegionNotTheDrawing()
    {
        // Invariant 6, measured rather than hoped: undo on a merged drawing
        // rebuilds a rectangle, and a group in it must not turn that back
        // into a replay of every stroke the group holds (B327's 3 s undo).
        // A wide page of marks, a small rectangle in one corner.
        const int w = 960, h = 540;
        var marks = new List<Stroke>();
        for (var i = 0; i < 400; i++)
        {
            var x = 20 + (i % 40) * 23;
            var y = 20 + (i / 40) * 50;
            marks.Add(new Stroke
            {
                Color = "#203040",
                Points = [new StrokePoint(x, y, 1), new StrokePoint(x + 15, y + 30, 0.7)],
                Brush = new BrushSettings { Size = 8, Hardness = 0.8, Opacity = 1, Flow = 1, Spacing = 0.1 },
            });
        }
        var frame = new Frame
        {
            Strokes = [Bar(80, "#2040c0"), Group(new StrokeGroup { Strokes = marks, Blend = LayerBlendMode.Multiply })],
        };
        using var full = FrameRasterizer.Materialize(frame, w, h);
        using var patched = full.Copy();
        var region = new SKRectI(40, 40, 120, 120);

        double Best(Action work)
        {
            var best = double.MaxValue;
            for (var i = 0; i < 5; i++)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                work();
                best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
            }
            return best;
        }

        var whole = Best(() => FrameRasterizer.Materialize(frame, w, h).Dispose());
        var part = Best(() => Assert.True(FrameRasterizer.RepaintRegion(patched, frame, region, w, h)));

        output.WriteLine($"whole drawing {whole:F2} ms, one 80 px rectangle {part:F2} ms, ratio {whole / part:F1}x");
        Assert.True(full.Bytes.AsSpan().SequenceEqual(patched.Bytes), "the repainted rectangle is not the full render's");
        // Loose on purpose: a replay of everything is ~1x, a bounded one is
        // many times faster. Three is far from both.
        Assert.True(part * 3 < whole, $"repainting a rectangle cost {part:F2} ms against {whole:F2} ms for the whole drawing");
    }

    [Fact]
    public void AGroupReachesAsFarAsItsStrokesDo()
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        var a = Bar(60, "#2040c0");
        var b = Soft(150, "#c02040");
        var union = SKRectI.Union(BrushEngine.CommitRegion(a, info)!.Value, BrushEngine.CommitRegion(b, info)!.Value);

        Assert.Equal(union, BrushEngine.CommitRegion(Group(a, Group(b)), info));
        Assert.Null(BrushEngine.CommitRegion(Group(), info));
        // Stored pixels and a filter can land anywhere: the whole surface.
        var whole = SKRectI.Create(0, 0, W, H);
        Assert.Equal(whole, BrushEngine.CommitRegion(Group(new StrokeGroup { Strokes = [a], PngBase64 = "AAAA" }), info));
        Assert.Equal(whole, BrushEngine.CommitRegion(Group(new StrokeGroup { Strokes = [a], Effects = Blur(6) }), info));
    }

    [Fact]
    public void TheFastPathsRefuseWhatAGroupHides()
    {
        // Each fast path decides by scanning strokes. A smudge inside a group
        // is still a smudge, and a group is not something a tile can hold.
        var smudge = Bar(100, "#000000");
        smudge.Brush.Kind = BrushKind.Smudge;
        var live = Bar(100, "#000000");
        live.Brush.SampleSource = SampleSource.AllLayersLive;

        Assert.False(TiledRasterizer.CanTile([Group(Bar(60, "#2040c0"))]));
        Assert.False(FrameRasterizer.CanRepaintRegion(new Frame { Strokes = [Group(live)] }));
        Assert.False(FrameRasterizer.CanRepaintRegion(
            new Frame { Strokes = [Group(new StrokeGroup { Strokes = [Bar(60, "#2040c0")], Effects = Blur(6) })] }));
        Assert.False(FrameRasterizer.CanRepaintRegion(
            new Frame { Strokes = [Group(new StrokeGroup { Strokes = [Bar(60, "#2040c0")], PngBase64 = "AAAA" })] }));

        var frame = new Frame { Strokes = [Bar(60, "#2040c0"), Group(Bar(100, "#c02040"), smudge)] };
        using var bitmap = FrameRasterizer.Materialize(frame, W, H);
        Assert.False(FrameRasterizer.RepaintRegion(bitmap, frame, new SKRectI(60, 60, 140, 140), W, H));
    }
}

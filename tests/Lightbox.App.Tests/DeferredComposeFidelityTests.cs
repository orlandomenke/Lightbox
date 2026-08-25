using Lightbox.App.Rendering;
using Lightbox.Core.Effects;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The deferred culled composite draws every field a pass carries, measured
/// against <see cref="SceneRenderer"/> rather than against a copy of itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>B309, and the reference is the whole point.</b> <see cref="DeferredComposeTests"/>
/// measures the moved composite against the publisher's own body, reproduced
/// literally — which is the right test for "did the move change anything" and
/// is blind to the thing that was actually wrong: both bodies read five fields
/// of a thirteen-field record and dropped the rest, so they agreed with each
/// other while both disagreed with the compositor that decides what a mask, an
/// effect, a style and an adjustment layer look like. Two implementations
/// copied from each other are one implementation with two names.
/// </para>
/// <para>
/// So every case here composes the same pass list twice — once through
/// <see cref="SceneRenderer.Compose"/>, once through
/// <see cref="DeferredCompose"/> — and demands the bytes agree. The viewport is
/// the whole document, which isolates <em>the drop</em>: culling geometry is
/// already held by <see cref="DeferredComposeTests"/> and by
/// <c>ComposeIdentityTests</c>, and mixing the two questions into one assertion
/// is how a failure stops naming its own cause.
/// <see cref="ACulledViewportStillCarriesTheGrade"/> is the one offset case,
/// with a point grade, because a kernel's reach past the cull is a property of
/// culling rather than of this fix.
/// </para>
/// </remarks>
public class DeferredComposeFidelityTests(ITestOutputHelper output)
{
    private const int W = 160, H = 120;
    private static readonly SKColor Paper = new(0xf2, 0xf0, 0xea);

    private static SKBitmap Filled(SKColor colour, SKRect rect)
    {
        var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = colour };
        canvas.DrawRect(rect, paint);
        return bmp;
    }

    private static SKBitmap Content() =>
        Filled(new SKColor(30, 90, 200, 255), SKRect.Create(20, 20, 90, 70));

    private static SKBitmap Mask() =>
        Filled(SKColors.White, SKRect.Create(20, 20, 45, 70));

    private static EffectStack Stack(string kind, params (string Key, double Value)[] values)
    {
        var use = new EffectUse { Kind = kind };
        foreach (var (key, value) in values) use.Params[key] = new EffectParam(value);
        var stack = new EffectStack();
        stack.Uses.Add(use);
        return stack;
    }

    private static byte[] Bytes(SKImage image, SKImageInfo info)
    {
        var buffer = new byte[info.BytesSize];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(
            buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            Assert.True(image.ReadPixels(info, handle.AddrOfPinnedObject(), info.RowBytes, 0, 0));
        }
        finally { handle.Free(); }
        return buffer;
    }

    private static int Differences(byte[] a, byte[] b)
    {
        var n = 0;
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++) if (a[i] != b[i]) n++;
        return n;
    }

    /// <summary>
    /// Compose the list both ways over the whole document and report how many
    /// bytes disagree.
    /// </summary>
    private int DisagreementOver(string what, List<RenderPass> passes)
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var reference = SceneRenderer.Compose(W, H, passes, Paper);
        var deferred = new DeferredCompose(passes, Paper, 1.0, info, SKRectI.Create(0, 0, W, H));
        using var moved = deferred.Compose(null, out var gpuBacked);
        var diff = Differences(Bytes(reference, info), Bytes(moved, info));
        output.WriteLine($"{what}: {diff} differing bytes of {info.BytesSize}, gpu={gpuBacked}");
        return diff;
    }

    /// <summary>
    /// The honesty check on the harness above. If a plain pass disagreed, the
    /// reference would be wrong and every other case here would be measuring
    /// the wrong difference.
    /// </summary>
    [Fact]
    public void AnOrdinaryPassAlreadyAgreedAndStillDoes()
    {
        var passes = new List<RenderPass> { new(Content(), null, 1.0) };
        Assert.Equal(0, DisagreementOver("plain", passes));
    }

    /// <summary>A layer mask, or a clipping base: the pass keeps only what the shape covers.</summary>
    [Fact]
    public void AMaskedPassIsCarved()
    {
        var passes = new List<RenderPass>
        {
            new(Content(), null, 1.0, SKBlendMode.SrcOver, Shapes: [new PassShape(Mask())]),
        };
        Assert.Equal(0, DisagreementOver("shapes", passes));
    }

    /// <summary>An inverted shape subtracts, which is the other half of a mask.</summary>
    [Fact]
    public void AnInvertedShapeSubtracts()
    {
        var passes = new List<RenderPass>
        {
            new(Content(), null, 1.0, SKBlendMode.SrcOver,
                Shapes: [new PassShape(Mask(), Inverted: true)]),
        };
        Assert.Equal(0, DisagreementOver("shapes inverted", passes));
    }

    /// <summary>The layer's own effect stack, baked to a filter on the pass.</summary>
    [Fact]
    public void ALayersOwnEffectApplies()
    {
        var passes = new List<RenderPass>
        {
            new(Content(), null, 1.0, SKBlendMode.SrcOver,
                Effect: SKImageFilter.CreateBlur(6f, 6f)),
        };
        Assert.Equal(0, DisagreementOver("effect", passes));
    }

    /// <summary>A layer style decorates the silhouette outside the mask carve (Q155).</summary>
    [Fact]
    public void ALayerStyleDecorates()
    {
        var style = Lightbox.Raster.Effects.EffectRegistry.StyleFor(
            Stack("style.outerGlow", ("size", 8), ("opacity", 90)), frame: 0);
        Assert.NotNull(style);
        var passes = new List<RenderPass>
        {
            new(Content(), null, 1.0, SKBlendMode.SrcOver, Style: style),
        };
        Assert.Equal(0, DisagreementOver("style", passes));
    }

    /// <summary>
    /// An adjustment layer filters the composite beneath it and carries no
    /// content of its own — the pass whose <c>Bitmap</c> is null, which is
    /// exactly the shape the dropped loop skipped first.
    /// </summary>
    [Theory]
    [InlineData("grade.invert")]
    [InlineData("grade.hsl")]
    public void AnAdjustmentLayerGradesTheBackdrop(string kind)
    {
        var stack = kind == "grade.hsl" ? Stack(kind, ("hue", 120)) : Stack(kind);
        var passes = new List<RenderPass>
        {
            new(Content(), null, 1.0),
            new(null, null, 1.0, SKBlendMode.SrcOver, AdjustStack: stack),
        };
        Assert.Equal(0, DisagreementOver($"adjust {kind}", passes));
    }

    /// <summary>The scene grade is the same pass with nothing carving it.</summary>
    [Fact]
    public void ACarvedAdjustmentGradesOnlyWhereItsShapeCovers()
    {
        var passes = new List<RenderPass>
        {
            new(Content(), null, 1.0),
            new(null, null, 0.75, SKBlendMode.SrcOver,
                Shapes: [new PassShape(Mask())], AdjustStack: Stack("grade.invert")),
        };
        Assert.Equal(0, DisagreementOver("adjust carved", passes));
    }

    /// <summary>The transform tool's live preview: the pixels exist, the matrix moves them.</summary>
    [Fact]
    public void APassUnderItsOwnMatrixMoves()
    {
        var passes = new List<RenderPass>
        {
            new(Content(), null, 1.0, SKBlendMode.SrcOver,
                Matrix: SKMatrix.CreateTranslation(30, 10)),
        };
        Assert.Equal(0, DisagreementOver("matrix", passes));
    }

    /// <summary>A reference cell: one window of a sheet holding a whole run cycle.</summary>
    [Fact]
    public void AWindowedReferenceCellShowsItsOwnCell()
    {
        var passes = new List<RenderPass>
        {
            new(Content(), null, 1.0, SKBlendMode.SrcOver,
                Source: SKRectI.Create(20, 20, 60, 50)),
        };
        Assert.Equal(0, DisagreementOver("source", passes));
    }

    /// <summary>
    /// An alpha-locked live stroke keeps to the pixels already on its layer.
    /// The publisher asserts no overlay reaches this route, but that assert is
    /// compiled out of the build an artist runs.
    /// </summary>
    [Fact]
    public void AnAlphaLockedLiveStrokeStaysMasked()
    {
        var scratch = Filled(new SKColor(240, 60, 60, 255), SKRect.Create(0, 0, W, H));
        var passes = new List<RenderPass>
        {
            new(Content(), null, 1.0, SKBlendMode.SrcOver,
                Overlay: new StrokeOverlay(scratch, 1.0, Erases: false, AlphaLocked: true)),
        };
        Assert.Equal(0, DisagreementOver("overlay alpha-locked", passes));
    }

    /// <summary>
    /// The one offset case: a culled surface covering part of the document
    /// still carries the grade, and lands it in the right place.
    /// </summary>
    /// <remarks>
    /// A <em>point</em> grade, deliberately. A kernel reaches past the cull and
    /// so reads pixels a culled surface does not hold — that is a property of
    /// culling, true of the ring's windowed compose too, and not something this
    /// fix changes or could.
    /// </remarks>
    [Fact]
    public void ACulledViewportStillCarriesTheGrade()
    {
        var viewport = SKRectI.Create(24, 16, 96, 72);
        var info = new SKImageInfo(
            viewport.Width, viewport.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var passes = new List<RenderPass>
        {
            new(Content(), null, 1.0),
            new(null, null, 1.0, SKBlendMode.SrcOver, AdjustStack: Stack("grade.invert")),
        };

        using var whole = SceneRenderer.Compose(W, H, passes, Paper);
        using var cropped = whole.Subset(viewport);
        var deferred = new DeferredCompose(passes, Paper, 1.0, info, viewport);
        using var moved = deferred.Compose(null, out _);

        var diff = Differences(Bytes(cropped!, info), Bytes(moved, info));
        output.WriteLine($"culled adjust: {diff} differing bytes of {info.BytesSize}");
        Assert.Equal(0, diff);
    }
}

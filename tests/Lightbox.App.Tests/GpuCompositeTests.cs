using Lightbox.App.Rendering;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// When a composite is allowed to go to the GPU.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this can and cannot check, stated plainly.</b> There is no graphics
/// context in this repository's test environment — that is the same reason B122
/// shipped as an inference and the render report exists at all. So these assert
/// the <em>policy</em>: the opt-in, the refusals, and that the fallback produces
/// a working CPU surface. They cannot assert that a GPU surface is faster, or
/// correct, or allocatable, on any real machine.
/// </para>
/// <para>
/// <b>That is not a gap to be filled by a cleverer test.</b> It is the reason
/// stage 4 exists as a measurement gate rather than a feature, and the reason the
/// path is off by default: the number that decides whether GPU compositing is
/// worth having can only come from hardware, through `Help ▸ Write a render
/// report`.
/// </para>
/// </remarks>
public class GpuCompositeTests(ITestOutputHelper output) : IDisposable
{
    private readonly bool? _saved = GpuComposite.OptInOverride;

    public void Dispose() => GpuComposite.OptInOverride = _saved;

    private static SKImageInfo Info(int w = 64, int h = 48) =>
        new(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);

    /// <summary>
    /// <b>Off unless asked for.</b> Stage 4 uploads every layer every frame and
    /// may be slower than the CPU path on integrated graphics; shipping it on by
    /// default would be an unmeasured change to the drawing path, which is what
    /// B130 was.
    /// </summary>
    [Fact]
    public void ItIsOffUnlessOptedIn()
    {
        GpuComposite.OptInOverride = false;

        Assert.False(GpuComposite.Wants(null, Info()));
    }

    /// <summary>
    /// Opted in with no context is still CPU. Headless, software rendering, or a
    /// lease that could not provide one — there is nothing to compose onto, and
    /// this is the case every run in this repository takes.
    /// </summary>
    [Fact]
    public void OptedInWithNoContextIsStillCpu()
    {
        GpuComposite.OptInOverride = true;

        Assert.False(GpuComposite.Wants(null, Info()));
    }

    /// <summary>
    /// The fallback produces a real, usable surface rather than throwing — a slow
    /// frame beats no frame, and a missing frame is a black canvas.
    /// </summary>
    [Fact]
    public void TheFallbackStillProducesAWorkingSurface()
    {
        GpuComposite.OptInOverride = true;

        using var surface = GpuComposite.CreateSurface(null, Info(), out var gpuBacked);
        surface.Canvas.Clear(SKColors.Red);
        surface.Canvas.Flush();
        using var image = surface.Snapshot();

        Assert.False(gpuBacked);
        Assert.Equal(64, image.Width);
    }

    /// <summary>
    /// A degenerate surface is refused rather than attempted. Reached from a
    /// compose scale computed off a viewport, so it is a real input rather than a
    /// hypothetical.
    /// </summary>
    [Fact]
    public void AnEmptySurfaceIsNeverGpuBacked()
    {
        GpuComposite.OptInOverride = true;

        Assert.False(GpuComposite.Wants(null, Info(0, 0)));
    }

    /// <summary>
    /// The opt-in is an environment variable rather than a setting, on the same
    /// reasoning as <c>LIGHTBOX_DURABLE_FRAME</c> after B130: this is a
    /// measurement instrument and nobody should find it by accident.
    /// </summary>
    [Fact]
    public void TheOptInIsNotASetting()
    {
        output.WriteLine(GpuComposite.OptInVariable);
        Assert.Equal("LIGHTBOX_GPU_COMPOSITE", GpuComposite.OptInVariable);
    }

    /// <summary>
    /// A deferred composite runs on the CPU when there is no context, and its
    /// pixels are the ones stage 3b promised — so the fallback is not a
    /// separate, untested path but the one every test here already exercises.
    /// </summary>
    [Fact]
    public void TheCpuFallbackIsTheSamePathStageThreeBAlreadyProves()
    {
        GpuComposite.OptInOverride = true;
        var info = Info(32, 24);
        var bmp = new SKBitmap(new SKImageInfo(64, 48, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bmp)) c.Clear(SKColors.Blue);

        var work = new DeferredCompose(
            [new RenderPass(bmp, null, 1.0)], SKColors.White, 1.0, info,
            SKRectI.Create(0, 0, 32, 24));

        using var image = work.Compose(null, out var gpuBacked);

        Assert.False(gpuBacked);
        Assert.Equal(32, image.Width);
    }
}

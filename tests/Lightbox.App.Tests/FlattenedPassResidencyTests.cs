using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The flattened tile passes playback draws must be able to come from a resident
/// texture rather than being uploaded again every frame.
/// </summary>
/// <remarks>
/// <para>
/// <b>These exist because a wiring gap survived every existing test and was found
/// by a render report instead (2026-08-12).</b> The capture read
/// <c>310 publishes composited on the card</c> and, three sections later,
/// <c>no layer textures were asked for</c>. Both were true: the composite was on
/// the GPU and the texture cache was never asked a single question, because every
/// flattened pass carries a placement matrix and the matrix arm of
/// <c>SceneRenderer.ComposeTiled</c> called <c>DrawWhole</c>, which uploaded
/// unconditionally. Only the ordinary-layer arm consulted residency, and on the
/// route playback takes nothing reaches it.
/// </para>
/// <para>
/// <b>So the two facts that have to hold are tested separately</b>, because the
/// bug was the *join* between them rather than either one: that the passes carry
/// a matrix (which decides the arm), and that the arm asks the cache.
/// </para>
/// <para>
/// <b>What cannot be tested here, said rather than implied.</b> There is no
/// graphics context in this repository, so an upload cannot run and
/// <c>ComposeTiled</c> can never produce a GPU-backed surface — which is
/// exactly why the gap was invisible. <c>LayerTextureCache</c>'s injectable
/// uploader is the seam that makes the question askable at all, and a
/// <c>GRContext</c> of <c>null!</c> is safe for the same reason it is in
/// <see cref="LayerTextureCacheTests"/>: the stand-in never dereferences it.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class FlattenedPassResidencyTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>A stand-in for an upload, counting how many were made.</summary>
    private sealed class Counting
    {
        public int Uploads;

        public SKImage? Upload(GRContext gpu, SKBitmap bitmap)
        {
            Uploads++;
            return SKImage.FromBitmap(bitmap);
        }
    }

    private static SKBitmap Bitmap(int w = 64, int h = 64)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Red);
        return bmp;
    }

    /// <summary>
    /// <b>The regression itself.</b> A whole pass drawn with a residency cache
    /// available must ask it — and ask it once, not once per frame.
    /// </summary>
    [Fact]
    public void AWholePassAsksTheResidencyCacheAndReusesTheAnswer()
    {
        var counting = new Counting();
        using var cache = new LayerTextureCache(counting.Upload);
        using var bmp = Bitmap();
        var pass = new RenderPass(bmp, null, 1.0, SKBlendMode.SrcOver);

        using var surface = SKSurface.Create(
            new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul));

        for (var i = 0; i < 5; i++)
            SceneRenderer.DrawWhole(surface.Canvas, pass, null, cache, gpu: null!);

        output.WriteLine($"{counting.Uploads} upload(s) across 5 draws");
        Assert.Equal(1, counting.Uploads);
    }

    /// <summary>
    /// With no cache it still draws — the CPU surface path must not depend on
    /// residency existing, since that is what every machine with the toggle off
    /// runs.
    /// </summary>
    [Fact]
    public void WithoutAResidencyCacheItStillDraws()
    {
        using var bmp = Bitmap();
        var pass = new RenderPass(bmp, null, 1.0, SKBlendMode.SrcOver);
        using var surface = SKSurface.Create(
            new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul));

        SceneRenderer.DrawWhole(surface.Canvas, pass, null);

        using var image = surface.Snapshot();
        using var drawn = SKBitmap.FromImage(image);
        Assert.Equal(SKColors.Red, drawn.GetPixel(32, 32));
    }

    /// <summary>
    /// A resident texture and a fresh upload must produce the same pixels — the
    /// point of the cache is that nobody can tell which one they got.
    /// </summary>
    [Fact]
    public void AResidentTextureDrawsTheSamePixelsAsAnUpload()
    {
        var counting = new Counting();
        using var cache = new LayerTextureCache(counting.Upload);
        using var bmp = Bitmap();
        var pass = new RenderPass(bmp, null, 1.0, SKBlendMode.SrcOver);
        var info = new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul);

        using var viaCache = SKSurface.Create(info);
        SceneRenderer.DrawWhole(viaCache.Canvas, pass, null, cache, gpu: null!);

        using var viaUpload = SKSurface.Create(info);
        SceneRenderer.DrawWhole(viaUpload.Canvas, pass, null);

        using var a = SKBitmap.FromImage(viaCache.Snapshot());
        using var b = SKBitmap.FromImage(viaUpload.Snapshot());
        for (var y = 0; y < 64; y += 8)
        {
            for (var x = 0; x < 64; x += 8)
            {
                Assert.Equal(b.GetPixel(x, y), a.GetPixel(x, y));
            }
        }
    }

    /// <summary>
    /// <b>The other half of the join: every flattened tile pass carries a
    /// matrix.</b> That is what routes it to the arm above, and it is the fact
    /// that made a gap there invisible on the bounded path. If this ever stops
    /// being true, the residency wiring above is guarding the wrong arm.
    /// </summary>
    [AvaloniaFact]
    public void EveryFlattenedTilePassCarriesAPlacementMatrix()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 12;
        vm.SetViewport(SKRectI.Create(0, 0, 960, 540));

        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(150, 150, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // The tiled route — and with it a flattened tile pass — is playback's.
        // Inspected before pausing: the pause publishes the canonical bounded
        // snapshot over this one.
        vm.TogglePlaybackCommand.Execute(null);
        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(latest);
        var withBitmaps = latest!.Passes!.Where(p => p.Bitmap is not null).ToList();
        vm.TogglePlaybackCommand.Execute(null);
        output.WriteLine(
            $"{withBitmaps.Count} pass(es) with a bitmap, " +
            $"{withBitmaps.Count(p => p.Matrix is not null)} carrying a matrix");

        Assert.NotEmpty(withBitmaps);
        Assert.All(withBitmaps, p => Assert.NotNull(p.Matrix));
    }
}

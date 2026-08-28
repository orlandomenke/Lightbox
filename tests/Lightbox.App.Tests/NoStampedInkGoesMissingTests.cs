using Lightbox.App.Services;
using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Ink that has been stamped is never absent from the screen, however little of
/// it a pass has processed (B332).
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect these pin, measured on the owner's machine 2026-08-28.</b> An
/// ink audit comparing what was stamped against what the compositor shows found
/// <b>three publishes in eleven missing ink, worst 4.5% of everything stamped</b>
/// — while every timing counter in the render report read healthy, including
/// <c>live tip drawn 505 of 505</c> and zero stalls. The artist's words were
/// *"the first dabs are visible but stop and disappear soon thereafter … they
/// stay hidden for quite some time as long as I am drawing, but will jump into
/// view at some point."*
/// </para>
/// <para>
/// <b>The mechanism, and why the dab arithmetic hides it.</b> A live pass writes
/// only the band it processed (B313) and then records <c>PostStampedDabs =
/// dabsAtPass</c>, claiming every dab that existed when it was queued. The tip
/// begins exactly where that claim ends, so no <em>dab</em> falls between the
/// two — the accounting is airtight and the pixels are not. A dab below the
/// claim whose pixels no band ever wrote is in neither the body nor the tip, and
/// stays invisible until some later pass happens to cover that region. That is
/// the mark vanishing and jumping back.
/// </para>
/// </remarks>
public class NoStampedInkGoesMissingTests
{
    private const int W = 64, H = 48;

    /// <summary>Ink the pass has processed, and which its band covers.</summary>
    private static readonly SKRectI Covered = new(4, 20, 24, 28);

    /// <summary>
    /// Ink that was stamped before the pass was queued and lies OUTSIDE the band
    /// it processed. The pass counts these dabs as done; nothing holds them.
    /// </summary>
    private static readonly SKRectI Orphaned = new(30, 4, 46, 12);

    /// <summary>The newest dabs, which the tip carries.</summary>
    private static readonly SKRectI Tip = new(48, 20, 60, 28);

    private static SKBitmap Filled(params SKRectI[] marks)
    {
        var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
        foreach (var m in marks) canvas.DrawRect(SKRect.Create(m.Left, m.Top, m.Width, m.Height), paint);
        return bmp;
    }

    private static bool AnyInk(SKBitmap bmp, SKRectI where)
    {
        for (var y = where.Top; y < where.Bottom; y++)
        {
            for (var x = where.Left; x < where.Right; x++)
            {
                if (bmp.GetPixel(x, y).Alpha > 0) return true;
            }
        }

        return false;
    }

    private static SKBitmap Composed(ScenePassBuilder.LiveEdit live)
    {
        var layer = new Layer { Name = "art" };
        layer.Cels.Add(new Cel { Frame = new Frame() });
        var scene = new Scene { Width = W, Height = H, FrameCount = 1 };
        scene.Layers.Add(layer);

        var state = new ScenePassBuilder.State(
            0, layer.Id, false, false, false, new OnionSettings { Enabled = false }, false);

        var result = ScenePassBuilder.Build(
            scene, state, new FrameBitmapCache(), new TileFallbackTally(), live);

        using var image = SceneRenderer.Compose(W, H, result.Passes, SKColors.Transparent);
        var shot = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        Assert.True(image.ReadPixels(shot.Info, shot.GetPixels(), shot.RowBytes, 0, 0));
        return shot;
    }

    /// <summary>
    /// <b>The defect itself.</b> The stroke stamped three marks; the pass covered
    /// one band and claimed them all. Before this fix the middle one was on
    /// nothing that reached the screen.
    /// </summary>
    [Fact]
    public void InkThePassClaimedButNeverCoveredIsStillShown()
    {
        using var stamped = Filled(Covered, Orphaned, Tip);
        using var processed = Filled(Covered);
        using var tipDabs = Filled(Tip);

        using var screen = Composed(new ScenePassBuilder.LiveEdit(
            Scratch: stamped,
            PostScratch: processed,
            PostStampedCount: 1,
            PostUsed: Covered,
            TipScratch: tipDabs,
            TipBounds: Tip,
            BrushStroke: new Stroke { Tool = ToolKind.Brush }));

        Assert.True(AnyInk(screen, Covered), "the processed body is not being shown");
        Assert.True(AnyInk(screen, Tip), "the tip is not being shown");
        Assert.True(
            AnyInk(screen, Orphaned),
            "ink that was stamped, claimed by the pass and covered by no band is missing "
            + "from the screen — that is the mark vanishing mid-stroke (B332)");
    }

    /// <summary>
    /// <b>And the processed result still wins where the pass HAS covered.</b> Raw
    /// ink underneath must not show through a mark an effect deliberately
    /// thinned, which is why the body replaces rather than blends over.
    /// </summary>
    [Fact]
    public void WhereThePassHasCoveredTheProcessedResultReplacesTheRawInk()
    {
        // The raw dabs are solid; the pass thinned that region to nothing, which
        // is what an effect at its extreme does.
        using var stamped = Filled(Covered, Orphaned);
        using var processed = Filled();

        using var screen = Composed(new ScenePassBuilder.LiveEdit(
            Scratch: stamped,
            PostScratch: processed,
            PostStampedCount: 1,
            PostUsed: Covered,
            BrushStroke: new Stroke { Tool = ToolKind.Brush }));

        Assert.False(
            AnyInk(screen, Covered),
            "raw ink is showing through a region the pass processed away, so the body is "
            + "blending over the raw dabs instead of replacing them");
        Assert.True(
            AnyInk(screen, Orphaned),
            "ink outside the processed band must still be shown");
    }

    /// <summary>
    /// <b>Before any pass lands nothing changes</b>, because the raw scratch was
    /// always the whole mark there. A fix that altered this case would be
    /// changing behaviour nobody complained about.
    /// </summary>
    [Fact]
    public void BeforeAPassLandsTheRawScratchIsStillTheWholeMark()
    {
        using var stamped = Filled(Covered, Orphaned, Tip);

        using var screen = Composed(new ScenePassBuilder.LiveEdit(
            Scratch: stamped,
            PostStampedCount: -1,
            BrushStroke: new Stroke { Tool = ToolKind.Brush }));

        Assert.True(AnyInk(screen, Covered));
        Assert.True(AnyInk(screen, Orphaned));
        Assert.True(AnyInk(screen, Tip));
    }
}

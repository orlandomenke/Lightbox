using Lightbox.App.Services;
using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B322, demonstrated at the seam where it happens rather than through a screen.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect had no repro for a day and a half, and three attempted fixes
/// were judged without one.</b> Two throwaway pixel harnesses were built to see
/// it end to end; both had the bug rather than the app — one drew off a canvas
/// it never resized and read its own crop edges as artifacts, the other
/// measured a straight-edge detector against a scribble that clamped to the
/// margin, so it found the straight line it had itself drawn, in the baseline
/// too. With a harness that did neither, the three candidate fixes were
/// indistinguishable and <em>none</em> of them, including no fix at all, showed
/// any unrendered mark. A harness that cannot see the defect cannot judge a fix
/// for it.
/// </para>
/// <para>
/// <b>So this does not go through a screen.</b> B322 is not a timing failure and
/// not a rendering failure — it is a choice, made on one line of
/// <c>OverlayFor</c>: the live overlay carries either the processed buffer or
/// the raw dab scratch, never both. Everything stamped since the last completed
/// pass is in the scratch and nowhere else, so it is not dimmed or approximate,
/// it is <b>absent</b>. Constructing that state directly and asking which mark
/// reaches the overlay is deterministic, needs no worker, no clock and no
/// window, and fails for exactly one reason.
/// </para>
/// <para>
/// These are written to FAIL on the current build. That is the point: the
/// repro comes first, and the fix is what turns them green.
/// </para>
/// </remarks>
public class LiveTipOverlayTests(ITestOutputHelper output)
{
    private const int W = 64, H = 48;

    /// <summary>Where the settled body of the stroke is — in both buffers.</summary>
    private static readonly SKRectI Body = new(4, 20, 24, 28);

    /// <summary>
    /// Where the newest dabs are — stamped since the last pass completed, so
    /// they exist in the raw scratch and cannot exist in the processed one.
    /// </summary>
    private static readonly SKRectI Tip = new(40, 20, 56, 28);

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

    /// <summary>
    /// A stroke mid-flight on a brush with an effect: the pass has completed
    /// once for the body, and the pen has moved on since.
    /// </summary>
    private static ScenePassBuilder.LiveEdit MidStroke(
        SKBitmap raw, SKBitmap processed, SKBitmap? tip = null) =>
        new(
            Scratch: raw,
            PostScratch: processed,
            TipScratch: tip,
            // Above zero is what makes OverlayFor prefer the processed buffer.
            // Below it — the first events of a stroke, before any pass has
            // landed — the raw scratch is used and the tip is present, which is
            // why the defect only shows once a stroke is under way.
            PostStampedCount: 1,
            BrushStroke: new Stroke { Tool = ToolKind.Brush });

    /// <summary>
    /// Build the pass list and <b>compose it the way the canvas does</b>.
    /// </summary>
    /// <remarks>
    /// Deliberately not an assertion about which bitmap the overlay carries.
    /// The first draft of these tests read <c>overlay.Scratch</c> directly,
    /// which bakes today's one-bitmap design into the test and would have to be
    /// rewritten by the very fix it is meant to judge — and a test rewritten to
    /// suit a fix has stopped being evidence. What an artist sees is the
    /// composite, so that is what is measured, and any fix that puts the tip on
    /// screen passes regardless of how it carries it there.
    /// </remarks>
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
        Assert.Contains(result.Passes, p => p.Overlay is not null);

        using var image = SceneRenderer.Compose(W, H, result.Passes, SKColors.Transparent);
        var shot = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        Assert.True(image.ReadPixels(shot.Info, shot.GetPixels(), shot.RowBytes, 0, 0));
        return shot;
    }

    /// <summary>
    /// <b>The defect, stated as the artist sees it.</b> The body of the mark is
    /// on screen and the newest dabs are not — not faint, not unprocessed,
    /// absent. Measured on the owner's machine at 511 passes over 4,720 events,
    /// which is the mark standing still for about nine events and then jumping.
    /// </summary>
    [Fact]
    public void TheNewestDabsReachTheOverlayWhileAPassIsStillOutstanding()
    {
        using var raw = Filled(Body, Tip);
        using var processed = Filled(Body);
        // What the app builds at publish time: the dabs the pass has not seen,
        // stamped fresh rather than copied out of the shared scratch.
        using var tipDabs = Filled(Tip);

        using var screen = Composed(MidStroke(raw, processed, tipDabs));

        var body = AnyInk(screen, Body);
        var tip = AnyInk(screen, Tip);
        output.WriteLine($"body on screen: {body}, tip on screen: {tip}");

        Assert.True(body, "the settled body of the stroke is not being shown at all");
        Assert.True(
            tip,
            "the dabs stamped since the last pass are not on screen — the overlay is "
            + "showing the processed buffer alone, which is B322");
    }

    /// <summary>
    /// <b>The discriminating case, and the reason the cheap fix may not be the
    /// right one.</b> Every effect here <em>reduces</em> alpha somewhere against
    /// the raw dabs — the footprint ceiling caps coverage, the wet edge lightens
    /// the interior to darken its rim, granulation modulates it down, a medium
    /// erodes. So a fix that simply shows raw dabs wherever the processed buffer
    /// is thin will fill those back in and the effect will look weaker while the
    /// pen moves, then snap when the pass lands. That is the same class of fault
    /// as the tiles, just soft-edged, and it is what tells a one-blit fix apart
    /// from one that tracks which dabs are new.
    /// </summary>
    [Fact]
    public void WhereTheEffectThinnedTheMarkTheRawDabsDoNotFillItBackIn()
    {
        using var raw = Filled(Body, Tip);

        // The pass has been here and made the mark HALF as opaque — the shape a
        // ceiling or a wet-edge interior leaves behind.
        using var processed = new SKBitmap(new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(processed))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint
            {
                Color = SKColors.Black.WithAlpha(128),
                IsAntialias = false,
                BlendMode = SKBlendMode.Src,
            };
            canvas.DrawRect(SKRect.Create(Body.Left, Body.Top, Body.Width, Body.Height), paint);
        }

        using var tip = Filled(Tip);
        using var screen = Composed(MidStroke(raw, processed, tip));
        var centre = screen.GetPixel(Body.Left + (Body.Width / 2), Body.Top + (Body.Height / 2));
        output.WriteLine($"body centre: alpha {centre.Alpha} (the pass left 128)");

        Assert.True(
            centre.Alpha <= 160,
            $"the body reads alpha {centre.Alpha} where the pass left 128 — raw dabs are "
            + "showing through what the effect deliberately thinned, so the mark will "
            + "look wrong while the pen moves and snap when the pass lands");
    }

    /// <summary>
    /// <b>And the processed body has to survive whatever shows the tip.</b> This
    /// is the assertion the first attempted fix would have failed: copying the
    /// raw dabs forward over a bounding rectangle overwrote finished wet edge
    /// and granulation with flat ink, in rectangles, several times a second.
    /// The owner's verdict was that it was worse than the bug.
    /// </summary>
    [Fact]
    public void ShowingTheTipDoesNotPaintRawDabsOverTheProcessedBody()
    {
        // The body differs between the buffers: processed is what the pass
        // made of it, raw is the flat dabs underneath. If the fix lets raw
        // pixels win anywhere in the body, this sees it.
        using var raw = Filled(Body, Tip);
        using var processed = new SKBitmap(new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(processed))
        {
            canvas.Clear(SKColors.Transparent);
            // A distinguishable "processed" colour: what the effect made of the
            // body. Raw ink is black, so any black inside the body is the fix
            // having overwritten the pass's work.
            using var paint = new SKPaint { Color = new SKColor(0, 0, 255), IsAntialias = false };
            canvas.DrawRect(SKRect.Create(Body.Left, Body.Top, Body.Width, Body.Height), paint);
        }

        using var tip = Filled(Tip);
        using var screen = Composed(MidStroke(raw, processed, tip));
        var centre = screen.GetPixel(Body.Left + (Body.Width / 2), Body.Top + (Body.Height / 2));
        output.WriteLine($"body centre after the fix: {centre}");

        Assert.True(centre.Alpha > 0, "the body vanished");
        Assert.True(
            centre.Blue > centre.Red,
            $"the body reads {centre} — the processed pixels have been overwritten by raw "
            + "dabs, which is the artifact the owner reported as worse than the bug");
    }
}

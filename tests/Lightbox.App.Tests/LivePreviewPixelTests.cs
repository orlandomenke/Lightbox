using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Drives the real live-preview pipeline (BeginStroke → MoveStrokeBatch →
/// published RenderSnapshot) and inspects the pixels the canvas would show —
/// the headless equivalent of watching the screen while drawing.
/// </summary>
/// <remarks>
/// In the <c>BrushState</c> collection because it sets brush parameters, and
/// those live in a process-wide store: running beside a test that assumes
/// defaults hands it this one’s brush. It opted out until a CI run caught
/// it — the live-preview pixel check went red on a loaded machine and was
/// green on every local run, which is what this collection exists to stop.
/// </remarks>
[Collection("BrushState")]
public class LivePreviewPixelTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel PinnedVm(double opacity = 1) => new(null)
    {
        SmoothStrokes = false,
        ColorHex = "#000000",
        BrushSize = 24,
        BrushHardness = 1,
        BrushOpacity = opacity,
        BrushFlow = 1,
        BrushWetEdge = 0,
        BrushGranulation = 0,
        BrushScatter = 0,
    };

    private static SKBitmap LatestPixels(RenderSnapshot snapshot)
    {
        var bmp = SKBitmap.FromImage(snapshot.Image);
        Assert.NotNull(bmp);
        return bmp!;
    }

    [AvaloniaFact]
    public void MidStroke_ThePublishedSnapshot_ShowsTheLine()
    {
        var vm = PinnedVm();
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(300, 100, 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // flush the coalesced publish

        Assert.NotNull(latest);
        using var bmp = LatestPixels(latest!);
        Assert.True(bmp.GetPixel(200, 100).Red < 100,
            "the live preview did not ink the stroke midpoint while drawing");

        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var committed = LatestPixels(latest!);
        Assert.True(committed.GetPixel(200, 100).Red < 100,
            "the committed stroke vanished from the snapshot after pen lift");
    }

    [AvaloniaFact]
    public void SelfCrossing_LooksTheSame_LiveAndCommitted()
    {
        var vm = PinnedVm(opacity: 0.5);
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        // An X drawn as one stroke: down-right, then jump up, then down-left,
        // crossing at (200,150).
        vm.BeginStroke(100, 50, 1);
        vm.MoveStroke(300, 250, 1);
        vm.MoveStroke(300, 50, 1);
        vm.MoveStroke(100, 250, 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(latest);
        byte liveAtCrossing;
        using (var bmp = LatestPixels(latest!))
        {
            liveAtCrossing = bmp.GetPixel(200, 150).Red;
        }

        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        byte committedAtCrossing;
        using (var bmp = LatestPixels(latest!))
        {
            committedAtCrossing = bmp.GetPixel(200, 150).Red;
        }

        // 50% black over white paper is mid-grey in BOTH: the preview must not
        // double-darken the crossing and then "fade" on pen lift.
        Assert.True(committedAtCrossing is > 100 and < 160,
            $"committed crossing should be ~50% grey, was {committedAtCrossing}");
        Assert.True(Math.Abs(liveAtCrossing - committedAtCrossing) <= 10,
            $"preview ({liveAtCrossing}) and commit ({committedAtCrossing}) diverge at the crossing");
    }

    /// <summary>
    /// B69/B89 through the real pipeline, which is the half an engine test cannot reach.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>EffectPreviewMatchesCommitTests</c> proves the engine's resumable range reproduces a
    /// single pass exactly. It cannot prove the <em>application</em> feeds it that way — and
    /// feeding was the entire bug, since the engine was always correct when handed a whole
    /// stroke. This drives BeginStroke → several MoveStroke events → EndStroke and compares the
    /// published frame either side of the pen lift.
    /// </para>
    /// <para>
    /// Several separate <c>MoveStroke</c> calls on purpose: one call would be a single segment
    /// and would have passed against the broken code.
    /// </para>
    /// </remarks>
    [AvaloniaTheory]
    [InlineData("builtin-smudge")]
    [InlineData("builtin-blender")]
    public void AnEffectStroke_LooksTheSame_LiveAndCommitted(string presetId)
    {
        var vm = PinnedVm();
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        // Something to push around. Smudging bare paper is a no-op by construction.
        vm.ColorHex = "#101010";
        vm.BrushSize = 60;
        vm.BeginStroke(80, 90, 1);
        vm.MoveStroke(320, 90, 1);
        vm.MoveStroke(320, 170, 1);
        vm.MoveStroke(80, 170, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.ApplyPreset(vm.BrushPresetChoices.First(p => p.Id == presetId));
        vm.BrushSize = 40;

        // A drag across the block's edge, one event per move — the feed that used to
        // restart the walk phase and the carried colour on every one of them.
        vm.BeginStroke(120, 110, 1);
        vm.MoveStroke(160, 118, 1);
        vm.MoveStroke(205, 126, 1);
        vm.MoveStroke(250, 134, 1);
        vm.MoveStroke(292, 142, 1);
        vm.MoveStroke(330, 150, 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(latest);
        using var live = LatestPixels(latest!);
        var liveCopy = live.Copy();

        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var committed = LatestPixels(latest!);

        int differing = 0, worst = 0;
        var region = new SKRectI(60, 60, Math.Min(380, committed.Width), Math.Min(200, committed.Height));
        for (var y = region.Top; y < region.Bottom; y++)
        for (var x = region.Left; x < region.Right; x++)
        {
            var a = liveCopy.GetPixel(x, y);
            var b = committed.GetPixel(x, y);
            var d = Math.Max(Math.Abs(a.Red - b.Red), Math.Abs(a.Alpha - b.Alpha));
            if (d == 0) continue;
            differing++;
            worst = Math.Max(worst, d);
        }

        // Not vacuous: the smudge has to have marked the block for "identical" to mean
        // anything. A brush that drew nothing would otherwise match perfectly.
        var moved = 0;
        for (var x = region.Left; x < region.Right; x++)
        {
            if (committed.GetPixel(x, 130).Red != committed.GetPixel(x, 62).Red) moved++;
        }
        Assert.True(moved > 40, $"the smudge barely marked the block ({moved} px) — nothing is being measured");

        Assert.True(
            differing == 0,
            $"{presetId}: preview and commit differ over {differing} px, worst {worst}/255 — "
            + "the stroke settled when the pen lifted");

        liveCopy.Dispose();
    }

    // ---- B169: the live eraser and the layers beneath it -----------------------

    private static SKBitmap Filled(int width, int height, SKColor color)
    {
        var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(color);
        return bmp;
    }

    /// <summary>
    /// The scene of the bug: paper is a real pass (a document with a Background
    /// layer composes over transparent — <c>SceneRenderer.BackgroundOf</c>),
    /// black art above it, and a live eraser stroke over the art.
    /// </summary>
    private static (SKBitmap Paper, SKBitmap Art, SKBitmap Scratch) EraseScene()
    {
        var paper = Filled(400, 300, SKColors.White);
        var art = Filled(400, 300, SKColors.Transparent);
        using (var c = new SKCanvas(art))
        using (var black = new SKPaint { Color = SKColors.Black })
        {
            c.DrawRect(SKRect.Create(100, 100, 100, 100), black);
        }
        // The eraser's dabs, as the engine accumulates them: opaque coverage in
        // the scratch, applied DstOut at composite time.
        var scratch = Filled(400, 300, SKColors.Transparent);
        using (var c = new SKCanvas(scratch))
        using (var dab = new SKPaint { Color = SKColors.White })
        {
            c.DrawCircle(150, 150, 25, dab);
        }
        return (paper, art, scratch);
    }

    /// <summary>
    /// <b>B169.</b> An eraser overlay drawn <c>DstOut</c> straight onto the
    /// shared composite surface removes the paper as well as the art, so a live
    /// erase showed the transparency checkerboard where the paper should stay.
    /// The overlay has to combine with its own layer in isolation before that
    /// layer meets the stack — which the ring compositor always did, and the
    /// culled route (this one) lost when the compose moved to the render thread.
    /// </summary>
    [AvaloniaFact]
    public void ErasingDoesNotCutThroughTheLayersBeneath()
    {
        var (paper, art, scratch) = EraseScene();
        using (paper)
        using (art)
        using (scratch)
        {
            var passes = new[]
            {
                new RenderPass(paper, null, 1),
                new RenderPass(art, null, 1, Overlay: new StrokeOverlay(scratch, 1, Erases: true)),
            };
            var info = new SKImageInfo(400, 300, SKColorType.Rgba8888, SKAlphaType.Premul);
            var compose = new DeferredCompose(
                passes, SKColors.Transparent, 1.0, info, SKRectI.Create(0, 0, 400, 300));

            using var image = compose.Compose(null, out _);
            using var bmp = SKBitmap.FromImage(image);

            var erased = bmp.GetPixel(150, 150);
            output.WriteLine($"erased pixel: R{erased.Red} A{erased.Alpha}");
            // The erase must reveal the paper, not the void beneath it.
            Assert.True(erased.Alpha > 250, $"the eraser cut through the paper (alpha {erased.Alpha})");
            Assert.True(erased.Red > 250, $"the erased area should show white paper (red {erased.Red})");

            // And it must still erase: the art outside the dab is untouched,
            // the art under the dab is gone.
            Assert.True(bmp.GetPixel(110, 110).Red < 5, "the art outside the erase should stay black");
        }
    }

    /// <summary>
    /// The live composite and the committed one are the same picture. Committed,
    /// the erase is <c>DstOut</c> against the layer's own bitmap alone — so that
    /// is what the live overlay must amount to, whatever surface it lands on.
    /// </summary>
    [AvaloniaFact]
    public void TheLiveEraserAgreesWithTheCommittedOne()
    {
        var (paper, art, scratch) = EraseScene();
        using (paper)
        using (art)
        using (scratch)
        {
            var info = new SKImageInfo(400, 300, SKColorType.Rgba8888, SKAlphaType.Premul);
            var viewport = SKRectI.Create(0, 0, 400, 300);

            // Live: the erase is still an overlay on its pass.
            var live = new DeferredCompose(
                [
                    new RenderPass(paper, null, 1),
                    new RenderPass(art, null, 1, Overlay: new StrokeOverlay(scratch, 1, Erases: true)),
                ],
                SKColors.Transparent, 1.0, info, viewport);

            // Committed: the erase has been applied to the layer itself, which
            // is what EndStroke writes.
            using var committedArt = art.Copy();
            using (var c = new SKCanvas(committedArt))
            using (var erase = new SKPaint { BlendMode = SKBlendMode.DstOut })
            {
                c.DrawBitmap(scratch, 0, 0, erase);
            }
            var committed = new DeferredCompose(
                [new RenderPass(paper, null, 1), new RenderPass(committedArt, null, 1)],
                SKColors.Transparent, 1.0, info, viewport);

            using var liveImage = live.Compose(null, out _);
            using var committedImage = committed.Compose(null, out _);
            using var liveBmp = SKBitmap.FromImage(liveImage);
            using var committedBmp = SKBitmap.FromImage(committedImage);

            var differing = 0;
            var worst = 0;
            for (var y = 90; y < 210; y++)
            for (var x = 90; x < 210; x++)
            {
                var a = liveBmp.GetPixel(x, y);
                var b = committedBmp.GetPixel(x, y);
                var d = Math.Max(
                    Math.Max(Math.Abs(a.Red - b.Red), Math.Abs(a.Green - b.Green)),
                    Math.Max(Math.Abs(a.Blue - b.Blue), Math.Abs(a.Alpha - b.Alpha)));
                if (d == 0) continue;
                differing++;
                worst = Math.Max(worst, d);
            }
            output.WriteLine($"{differing} px differ, worst {worst}/255");
            Assert.True(differing == 0,
                $"live erase and committed erase disagree over {differing} px (worst {worst}/255)");
        }
    }
}

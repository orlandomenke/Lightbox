using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Projects;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Visual regression tests for unbounded canvas tiled rendering. Verifies that:
/// - Tiles render without visible seams
/// - Strokes maintain correct positions across tile boundaries
/// - Panning and zooming preserve stroke appearance
/// </summary>
[Collection("BrushState")]
public class UnboundedCanvasPixelTests : BrushStateIsolated
{
    /// <remarks>
    /// Two arrangement bugs hid in the previous version of this helper, and
    /// both let every test here pass against the NORMAL compositor while the
    /// tiled one was broken. <c>Doc.Features</c> is null until something
    /// writes a feature (absent-unless-used), and the old
    /// <c>if (Features != null)</c> guard therefore never enabled the
    /// feature; and nothing set a viewport, without which the unbounded path
    /// refuses to run at all. The tiled compositor's first real caller was
    /// the running application, where its use-after-dispose showed up as
    /// "the infinite canvas does not work at all".
    /// </remarks>
    private MainViewModel CreateVmWithUnboundedCanvas()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 12;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        vm.BrushWetEdge = 0;
        vm.BrushGranulation = 0;
        vm.BrushScatter = 0;

        // Viewport first — in the app the canvas reports one before the
        // Configure window can be opened — then the real toggle path, which
        // clears renders made under the old setting.
        vm.SetViewport(SKRectI.Create(0, 0, 960, 540));
        vm.SetDocumentFeature(FeatureKey.UnboundedCanvas, true, projectDefault: false);
        return vm;
    }

    private static SKBitmap GetLatestPixels(RenderSnapshot snapshot)
    {
        var bmp = SKBitmap.FromImage(snapshot.Image);
        Assert.NotNull(bmp);
        return bmp!;
    }

    [AvaloniaFact]
    public void TiledRendering_StrokeAcrossTileBoundary_HasNoVisibleSeam()
    {
        var vm = CreateVmWithUnboundedCanvas();
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        // Draw a horizontal line that crosses a tile boundary (256px).
        // Position it so it crosses at x=256 (tile boundary)
        vm.BeginStroke(200, 256, 1);
        vm.MoveStroke(312, 256, 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(latest);
        using var bmp = GetLatestPixels(latest!);

        // Sample pixels along the stroke across the tile boundary
        var pixelsBefore = bmp.GetPixel(250, 256).Red;
        var pixelAtBoundary = bmp.GetPixel(256, 256).Red;
        var pixelsAfter = bmp.GetPixel(262, 256).Red;

        // All should be dark (stroke color), indicating no seam
        Assert.True(pixelsBefore < 100, "Stroke before boundary should be visible");
        Assert.True(pixelAtBoundary < 100, "Stroke at tile boundary should be visible (no seam)");
        Assert.True(pixelsAfter < 100, "Stroke after boundary should be visible");

        // Verify continuity: shouldn't have light pixels at the boundary
        Assert.True(Math.Abs(pixelsBefore - pixelAtBoundary) < 50,
            "Stroke continuity broken at tile boundary");
    }

    [AvaloniaFact]
    public void TiledRendering_MultipleTiles_StrokeMaintainsRelativePosition()
    {
        var vm = CreateVmWithUnboundedCanvas();
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        // Draw strokes in different tiles to verify each is positioned correctly
        // Tile 1 (0-256)
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(150, 100, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Tile 2 (256-512)
        vm.BeginStroke(300, 300, 1);
        vm.MoveStroke(350, 300, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(latest);
        using var bmp = GetLatestPixels(latest!);

        // Verify first stroke is visible at its position
        Assert.True(bmp.GetPixel(125, 100).Red < 100,
            "First stroke should be visible in its tile");

        // Verify second stroke is visible at its position
        Assert.True(bmp.GetPixel(325, 300).Red < 100,
            "Second stroke should be visible in its tile");

        // Verify they don't interfere (different tiles)
        Assert.True(bmp.GetPixel(100, 300).Red > 200,
            "Area without stroke should remain clear");
    }

    [AvaloniaFact]
    public void TiledRendering_WithViewportOffset_StrokesRenderCorrectly()
    {
        var vm = CreateVmWithUnboundedCanvas();
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        // Draw a stroke at canvas position (300, 300)
        vm.BeginStroke(300, 300, 1);
        vm.MoveStroke(350, 300, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(latest);
        using var bmp = GetLatestPixels(latest!);

        // The stroke should be visible in the rendered output
        // (exact pixel position depends on viewport translation)
        var hasStroke = false;
        for (int x = 0; x < Math.Min(bmp.Width, 400); x++)
        {
            for (int y = 0; y < Math.Min(bmp.Height, 350); y++)
            {
                if (bmp.GetPixel(x, y).Red < 100)
                {
                    hasStroke = true;
                    break;
                }
            }
            if (hasStroke) break;
        }

        Assert.True(hasStroke, "Stroke should be visible with viewport offset");
    }

    [AvaloniaFact]
    public void TiledRendering_MultipleRenders_ProducesConsistentOutput()
    {
        var vm = CreateVmWithUnboundedCanvas();
        RenderSnapshot? first = null;
        vm.SnapshotChanged += s => first = s;

        // Draw a stroke
        vm.BeginStroke(200, 200, 1);
        vm.MoveStroke(250, 200, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(first);
        using var bmp1 = GetLatestPixels(first!);

        // Verify stroke was painted
        var strokeVisible1 = false;
        for (int x = 190; x < 260; x++)
        {
            if (bmp1.GetPixel(x, 200).Red < 100)
            {
                strokeVisible1 = true;
                break;
            }
        }
        Assert.True(strokeVisible1, "Stroke should be visible in first render");

        // Draw another stroke and verify both are present
        vm.BeginStroke(200, 300, 1);
        vm.MoveStroke(250, 300, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        using var bmp2 = GetLatestPixels(first!);

        // Both strokes should still be visible
        var firstStrokeStill = false;
        var secondStrokeVisible = false;
        for (int x = 190; x < 260; x++)
        {
            if (bmp2.GetPixel(x, 200).Red < 100)
                firstStrokeStill = true;
            if (bmp2.GetPixel(x, 300).Red < 100)
                secondStrokeVisible = true;
        }
        Assert.True(firstStrokeStill, "First stroke should persist");
        Assert.True(secondStrokeVisible, "Second stroke should be visible");
    }

    [AvaloniaFact]
    public void TiledRendering_LargeDocument_RendersTilesCorrectly()
    {
        var vm = CreateVmWithUnboundedCanvas();
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        // Draw strokes in multiple tiles (creating a large document)
        for (int tileX = 0; tileX < 3; tileX++)
        {
            for (int tileY = 0; tileY < 3; tileY++)
            {
                int x = tileX * 256 + 100;
                int y = tileY * 256 + 100;
                vm.BeginStroke(x, y, 1);
                vm.MoveStroke(x + 50, y, 1);
                vm.EndStroke();
            }
        }
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(latest);
        using var bmp = GetLatestPixels(latest!);

        // Verify we rendered multiple tiles without crashing
        Assert.True(bmp.Width > 0 && bmp.Height > 0, "Should produce valid bitmap");

        // Count dark pixels (strokes)
        int darkPixels = 0;
        for (int x = 0; x < bmp.Width; x++)
        {
            for (int y = 0; y < bmp.Height; y++)
            {
                if (bmp.GetPixel(x, y).Red < 100)
                    darkPixels++;
            }
        }

        Assert.True(darkPixels > 100, "Should have rendered strokes across multiple tiles");
    }

    /// <summary>
    /// The repaint after the repaint. The tiled compositor cached each
    /// layer's tile store and then disposed it in the same loop, so the first
    /// publish drew and every later one drew from freed tiles — reported as
    /// "the infinite canvas does not work at all". Nothing in this class
    /// caught it, because nothing here published twice.
    /// </summary>
    [AvaloniaFact]
    public void ASecondPublishShowsTheSamePictureAsTheFirst()
    {
        var vm = CreateVmWithUnboundedCanvas();
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(200, 100, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.NotNull(latest);
        using var first = GetLatestPixels(latest!);

        vm.PublishSnapshot();
        using var second = GetLatestPixels(latest!);

        Assert.True(first.GetPixel(150, 100).Red < 100, "the stroke is missing from the first publish");
        Assert.True(second.GetPixel(150, 100).Red < 100,
            "the stroke vanished on the second publish — a cache is serving freed or stale content");
    }

    /// <summary>
    /// A committed stroke reaches the screen even though the commit mutates
    /// the cached layer bitmap in place rather than replacing it — the
    /// identity-versus-content trap BitmapVersion exists for.
    /// </summary>
    [AvaloniaFact]
    public void ACommittedStrokeSurvivesManyPublishes()
    {
        var vm = CreateVmWithUnboundedCanvas();
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        // Publish before drawing, so any per-bitmap cache is warm with the
        // empty layer.
        vm.PublishSnapshot();
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(200, 100, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.PublishSnapshot();
        vm.PublishSnapshot();

        Assert.NotNull(latest);
        using var bmp = GetLatestPixels(latest!);
        Assert.True(bmp.GetPixel(150, 100).Red < 100,
            "the committed stroke is not on screen — a cache keyed on bitmap identity missed the in-place commit");
    }

    /// <summary>
    /// Zoomed out (a fractional compose scale), the stroke is where it
    /// should be, at the size it should be — the transform maths of the
    /// direct-draw path.
    /// </summary>
    [AvaloniaFact]
    public void AZoomedOutPublishKeepsTheStrokeInPlace()
    {
        var vm = CreateVmWithUnboundedCanvas();
        vm.SetDisplayScale(0.5); // Display quality: compose at half size
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(200, 100, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(latest);
        using var bmp = GetLatestPixels(latest!);
        Assert.Equal(480, bmp.Width); // 960-wide viewport at 0.5
        Assert.True(bmp.GetPixel(75, 50).Red < 128,
            $"the stroke is not at half-scale coordinates (px(75,50)={bmp.GetPixel(75, 50)})");
        Assert.True(bmp.GetPixel(75, 150).Red > 200, "ink where there should be paper");
    }

    /// <summary>
    /// A viewport hanging off the canvas — the whole point of an unbounded
    /// document — renders background there rather than garbage, and keeps the
    /// on-canvas ink aligned.
    /// </summary>
    [AvaloniaFact]
    public void AViewportPastTheCanvasEdgeRendersCalmly()
    {
        var vm = CreateVmWithUnboundedCanvas();
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.BeginStroke(50, 50, 1);
        vm.MoveStroke(120, 50, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Pan up-left: half the view is off the paper.
        vm.SetViewport(SKRectI.Create(-480, -270, 960, 540));
        vm.PublishSnapshot();

        Assert.NotNull(latest);
        using var bmp = GetLatestPixels(latest!);
        // Document (50,50) now sits at image (530, 320); (85,50) mid-stroke.
        Assert.True(bmp.GetPixel(565, 320).Red < 100,
            $"the stroke did not follow the pan (px(565,320)={bmp.GetPixel(565, 320)})");
        // Off-canvas space is empty, not noise: alpha 0 (transparent clear)
        // or the scene background — never leftover pixels.
        var off = bmp.GetPixel(100, 100);
        Assert.True(off.Alpha == 0 || (off.Red > 200 && off.Green > 200),
            $"off-canvas space shows garbage ({off})");
    }

    /// <summary>
    /// The point of the tile store, asserted in bytes: an unbounded document
    /// costs its ink, and never materialises a document-sized layer bitmap.
    /// </summary>
    /// <remarks>
    /// On a transparent document, because paper is a full-canvas fill stroke:
    /// it inks every tile of the nominal canvas by definition, so a papered
    /// document's tile cost approximates its paper — the sparsity the store
    /// buys is for the ink layers above it and for everything beyond the
    /// paper's edge, and a transparent document is where it shows cleanly.
    /// </remarks>
    [AvaloniaFact]
    public void AnUnboundedFrameCostsItsInkNotItsCanvas()
    {
        var vm = VmLayers.BareVm(); // transparent: no paper fill
        vm.SmoothStrokes = false;
        vm.SetViewport(SKRectI.Create(0, 0, 960, 540));
        vm.SetDocumentFeature(FeatureKey.UnboundedCanvas, true, projectDefault: false);

        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(300, 120, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.PublishSnapshot();
        vm.PublishSnapshot();

        var docBytes = (long)vm.Doc.Scene.Width * vm.Doc.Scene.Height * 4;
        var tileBytes = vm.TileFrames.AllocatedBytes;
        var bitmapBytes = vm.BitmapCacheBytes;

        Assert.True(vm.TileFrames.CachedFrames > 0, "no frame is held as tiles — the tile-native path never ran");
        Assert.True(tileBytes > 0 && tileBytes < docBytes / 2,
            $"tiles cost {tileBytes} bytes against a {docBytes}-byte canvas — memory is following the paper, not the ink");
        // Thumbnails legitimately cache small renders; what must not exist is
        // a document-sized bitmap per layer.
        Assert.True(bitmapBytes < docBytes,
            $"the bitmap cache holds {bitmapBytes} bytes — a document-sized layer bitmap was materialised for an unbounded document");
    }

    /// <summary>
    /// The tile-native render and the bitmap render are the same picture — the
    /// parity that lets one document opt in without changing what it looks like.
    /// </summary>
    [AvaloniaFact]
    public void AnUnboundedDocumentRendersWhatABoundedOneRenders()
    {
        static SKBitmap Render(bool unbounded)
        {
            var vm = VmLayers.PaperVm();
            vm.SmoothStrokes = false;
            vm.SetViewport(SKRectI.Create(0, 0, 960, 540));
            if (unbounded)
            {
                vm.SetDocumentFeature(FeatureKey.UnboundedCanvas, true, projectDefault: false);
            }

            RenderSnapshot? latest = null;
            vm.SnapshotChanged += s => latest = s;
            vm.BeginStroke(100, 100, 1);
            vm.MoveStroke(500, 260, 1);
            vm.EndStroke();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            vm.PublishSnapshot();
            Assert.NotNull(latest);
            using var img = latest!.Materialise(null);
            var bmp = new SKBitmap(img.Width, img.Height);
            img.ReadPixels(bmp.Info, bmp.GetPixels(), bmp.RowBytes, 0, 0);
            return bmp;
        }

        using var bounded = Render(unbounded: false);
        using var tiled = Render(unbounded: true);

        Assert.Equal(bounded.Width, tiled.Width);
        var worst = 0;
        for (var y = 0; y < bounded.Height; y += 2)
        {
            for (var x = 0; x < bounded.Width; x += 2)
            {
                var p = bounded.GetPixel(x, y);
                var q = tiled.GetPixel(x, y);
                worst = Math.Max(worst, Math.Abs(p.Red - q.Red));
                worst = Math.Max(worst, Math.Abs(p.Green - q.Green));
                worst = Math.Max(worst, Math.Abs(p.Blue - q.Blue));
            }
        }
        Assert.True(worst <= 1,
            $"tile-native differs from the bitmap render by {worst} — the two paths disagree about the same record");
    }
}

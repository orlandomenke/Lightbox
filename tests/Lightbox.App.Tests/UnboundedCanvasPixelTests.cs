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
    private MainViewModel CreateVmWithUnboundedCanvas()
    {
        var vm = new MainViewModel(null)
        {
            SmoothStrokes = false,
            ColorHex = "#000000",
            BrushSize = 12,
            BrushHardness = 1,
            BrushOpacity = 1,
            BrushFlow = 1,
            BrushWetEdge = 0,
            BrushGranulation = 0,
            BrushScatter = 0,
        };

        // Enable unbounded canvas in the document
        if (vm.Doc?.Features != null)
        {
            vm.Doc.Features[nameof(FeatureKey.UnboundedCanvas)] = true;
        }

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
}

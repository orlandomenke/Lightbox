using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.Docking;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The seamless-tile toggle (Q192): what the next stroke records, how a press
/// in a neighbour of the tiled preview is recorded, and that the surface is
/// registered where the configuration system can see it.
/// </summary>
public class TileWrapControlsTests(ITestOutputHelper output) : BrushStateIsolated
{
    private const int W = 400;
    private const int H = 300;

    private static MainViewModel Vm()
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("tiles", W, H, 12, 72, "#ffffff", false));
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 16;
        vm.BrushHardness = 0.9;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        vm.BrushScatter = 0;
        vm.BrushGranulation = 0;
        vm.BrushWetEdge = 0;
        return vm;
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static Stroke LastStroke(MainViewModel vm) =>
        vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame!.Strokes.Last();

    [AvaloniaFact]
    public void TheNextStrokeCarriesThePageAsItsTileAndTurningItOffDoesNotReachIt()
    {
        var vm = Vm();
        vm.TileWrapEnabled = true;

        vm.BeginStroke(60, 120, 1);
        vm.MoveStroke(90, 140, 0.9);
        vm.EndStroke();
        Pump();

        var tiled = LastStroke(vm);
        var tile = Assert.IsType<TileWrap>(tiled.Wrap);
        Assert.Equal(0, tile.Left);
        Assert.Equal(W, tile.Width);
        Assert.Equal(H, tile.Height);
        Assert.Null(tiled.Symmetry);

        vm.TileWrapEnabled = false;
        vm.BeginStroke(60, 120, 1);
        vm.MoveStroke(90, 140, 0.9);
        vm.EndStroke();
        Pump();

        Assert.Null(LastStroke(vm).Wrap);
        Assert.NotNull(tiled.Wrap);
    }

    [AvaloniaFact]
    public void APressInANeighbourOfTheTiledPreviewIsRecordedOnThePage()
    {
        // The canvas shows the page's neighbours; a stroke begun in the one
        // to the right is the same stroke begun on the page, shifted by a
        // whole tile — and the shift holds for every point after, so a stroke
        // that then crosses an edge stays one continuous mark.
        var vm = Vm();
        vm.TileWrapEnabled = true;

        vm.BeginStroke(W + 30, H + 20, 1);
        vm.MoveStroke(W + 60, H + 40, 0.9);
        vm.MoveStroke(W + 410, H + 40, 0.9); // past the next edge again: not re-shifted
        vm.EndStroke();
        Pump();

        var stroke = LastStroke(vm);
        Assert.Equal(30, stroke.Points[0].X, 6);
        Assert.Equal(20, stroke.Points[0].Y, 6);
        Assert.Equal(60, stroke.Points[1].X, 6);
        Assert.Equal(410, stroke.Points[2].X, 6);

        // And with tiling off, a press off the page is recorded where it was.
        vm.TileWrapEnabled = false;
        vm.BeginStroke(W + 30, 20, 1);
        vm.EndStroke();
        Pump();
        Assert.Equal(W + 30, LastStroke(vm).Points[0].X, 6);
    }

    [AvaloniaFact]
    public void ANeighbourPressStaysOnThePageUnderAStabiliser()
    {
        // The EMA filter is started on the shifted start point; fed the raw
        // pointer it would pull the mark a whole tile towards the hand, and
        // the shift added after would smear the stroke across the page.
        var vm = Vm();
        vm.TileWrapEnabled = true;
        vm.SmoothStrokes = true;
        vm.SmoothingMode = Lightbox.Core.Documents.SmoothingMode.Ema;
        vm.SmoothingStrength = 0.5;

        vm.BeginStroke(W + 30, 20, 1);
        for (var i = 1; i <= 8; i++) vm.MoveStroke(W + 30 + i * 4, 20, 1);
        vm.EndStroke();
        Pump();

        var points = LastStroke(vm).Points;
        output.WriteLine($"recorded x: {string.Join(", ", points.Select(p => p.X.ToString("0.#")))}");
        Assert.All(points, p => Assert.InRange(p.X, 29, 63));
        Assert.All(points, p => Assert.InRange(p.Y, 19, 21));
    }

    [AvaloniaFact]
    public void AJoinAcrossTheSeamIsAShortHopNotALineBackAcrossThePage()
    {
        // The last stroke ended near the right edge; a Shift+click just past
        // it, in the neighbour, joins with a ten-pixel segment the copies
        // draw — not a segment from the right edge back to the left edge.
        var vm = Vm();
        vm.TileWrapEnabled = true;
        vm.BeginStroke(W - 40, 100, 1);
        vm.MoveStroke(W - 5, 100, 1);
        vm.EndStroke();
        Pump();

        vm.BeginStroke(W + 5, 100, 1, eraseWithCurrentBrush: false, joinFromLast: true);
        vm.EndStroke();
        Pump();

        var joined = LastStroke(vm);
        output.WriteLine($"joined stroke x: {string.Join(", ", joined.Points.Select(p => p.X.ToString("0.#")))}");
        Assert.Equal(W - 5, joined.Points[0].X, 6);
        Assert.Equal(W + 5, joined.Points[1].X, 6);
    }

    [AvaloniaFact]
    public void AMarkNearTheRightEdgeIsOnTheLeftWhileItIsStillBeingDrawn()
    {
        var vm = Vm();
        vm.TileWrapEnabled = true;
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.BeginStroke(W - 20, 120, 1);
        Pump();
        for (var i = 1; i <= 6; i++)
        {
            vm.MoveStroke(W - 20 + i * 6, 120 + i * 4, 0.9);
            Pump();
        }

        Assert.NotNull(latest);
        using var live = SKBitmap.FromImage(latest!.Image);
        var left = 0;
        for (var y = 100; y < 170; y++)
        {
            for (var x = 0; x < 30; x++)
            {
                if (live.GetPixel(x, y).Red < 128) left++;
            }
        }
        output.WriteLine($"mid-stroke, left strip: {left} px inked");
        Assert.True(left > 40, $"the wrapped copy is not on screen while drawing ({left} px)");

        vm.EndStroke();
        Pump();
    }

    [Fact]
    public void TheToggleIsInTheShortcutMapAndSharesTheSymmetrySection()
    {
        var map = new ShortcutMap();
        var def = Assert.Single(map.Definitions, d => d.Id == "brush.tileWrap");
        Assert.Equal("Tools", def.Category);
        Assert.NotNull(def.Current);
        Assert.Null(map.ConflictWith(def.Id, def.Current!));

        // The Tile toggle lives in the symmetry section, so that section's
        // registration is what makes it reachable and removable.
        Assert.Contains(QuickBarCatalog.All, o => o.Id == QuickBarCatalog.BrushSymmetry && o.Label.Contains("tile", StringComparison.OrdinalIgnoreCase));
    }
}

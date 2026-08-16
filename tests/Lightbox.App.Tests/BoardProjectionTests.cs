using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Projecting a reference-board tile onto the canvas: the tile becomes a
/// pinned <see cref="ReferenceStrip"/> — over the paper, under every drawing
/// layer, never a layer of its own — linked back to the tile so projecting is
/// a toggle, and movable through the reference stack from a right-click.
/// </summary>
public class BoardProjectionTests
{
    private static byte[] Png(int width = 24, int height = 16, byte red = 200)
    {
        // Content on a plain ground rather than a flat colour: the import
        // slicer reads the background off the corners, and a flat image is
        // all background — it would import as nothing.
        using var bmp = new SKBitmap(width, height);
        bmp.Erase(SKColors.White);
        using (var canvas = new SKCanvas(bmp))
        using (var paint = new SKPaint { Color = new SKColor(red, 80, 60) })
        {
            canvas.DrawRect(SKRect.Create(4, 4, width - 8, height - 8), paint);
        }
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static BoardTile ImageTile(string name = "Pose") => new()
    {
        Name = name,
        Png = Convert.ToBase64String(Png()),
    };

    [AvaloniaFact]
    public void ProjectingAnImageTile_TapesItsPictureToTheCanvas_Linked()
    {
        var vm = VmLayers.PaperVm();
        var tile = ImageTile();

        var strip = vm.ToggleTileOnCanvas(tile, Png());

        Assert.NotNull(strip);
        Assert.Equal(tile.Id, strip!.BoardTileId);
        Assert.True(strip.Pinned);
        Assert.Equal("Pose", strip.Name);
        Assert.True(vm.IsTileOnCanvas(tile));
        // A projection, not a layer: the layer stack is exactly as it was.
        Assert.Contains(strip, vm.Doc.Scene.References!);
        Assert.DoesNotContain(vm.Doc.Scene.Layers, l => l.Name == "Pose");
    }

    [AvaloniaFact]
    public void ProjectingTheSameTileAgain_TakesTheProjectionDown()
    {
        var vm = VmLayers.PaperVm();
        var tile = ImageTile();
        vm.ToggleTileOnCanvas(tile, Png());

        var second = vm.ToggleTileOnCanvas(tile, Png());

        Assert.Null(second);
        Assert.False(vm.IsTileOnCanvas(tile));
        // Absent, not empty: the last projection leaving nulls the key.
        Assert.Null(vm.Doc.Scene.References);
    }

    [AvaloniaFact]
    public void BytesThatAreNotAPicture_ProjectNothing()
    {
        var vm = VmLayers.PaperVm();

        Assert.Null(vm.ToggleTileOnCanvas(ImageTile(), [1, 2, 3]));
        Assert.Null(vm.Doc.Scene.References);
    }

    [AvaloniaFact]
    public void TheStackMenuMovesAReferenceForwardAndBack()
    {
        var vm = VmLayers.PaperVm();
        var back = vm.ToggleTileOnCanvas(ImageTile("Back"), Png())!;
        var front = vm.ToggleTileOnCanvas(ImageTile("Front"), Png(red: 90))!;
        Assert.Equal([back, front], vm.Doc.Scene.References!);

        vm.MoveReferenceInStack(0, 1);

        Assert.Equal([front, back], vm.Doc.Scene.References!);
        // The selection follows the strip the artist moved.
        Assert.Equal(1, vm.ActiveReferenceIndex);

        // Clamped, not thrown: "bring forward" on the frontmost is a no-op.
        vm.MoveReferenceInStack(1, 5);
        Assert.Equal([front, back], vm.Doc.Scene.References!);
    }

    [AvaloniaFact]
    public void RightClickFindsTheTopmostReferenceUnderThePoint()
    {
        var vm = VmLayers.PaperVm();
        vm.ToggleTileOnCanvas(ImageTile("Back"), Png());
        vm.ToggleTileOnCanvas(ImageTile("Front"), Png(red: 90));
        var strips = vm.Doc.Scene.References!;
        // Both are centred on the canvas, so the centre hits the later one —
        // the topmost, exactly as the compositor draws them.
        var centreX = vm.Doc.Scene.Width / 2.0;
        var centreY = vm.Doc.Scene.Height / 2.0;

        Assert.Equal(1, vm.ReferenceStripAt(centreX, centreY));

        // Off the sheet entirely: no reference, so no menu.
        Assert.Equal(-1, vm.ReferenceStripAt(-50, -50));

        // An invisible strip is not under the pointer, whatever its bounds.
        strips[1].Visible = false;
        Assert.Equal(0, vm.ReferenceStripAt(centreX, centreY));
    }

    [AvaloniaFact]
    public void AStripThatDidNotComeOffTheBoard_WritesNoBoardKey()
    {
        var vm = VmLayers.PaperVm();
        Assert.True(vm.ImportReferenceImageBytes("plain", Png()));

        var json = DocJson.Serialize(vm.Doc);

        Assert.Contains("\"references\"", json);
        Assert.DoesNotContain("\"boardTileId\"", json);
    }

    /// <summary>A little sprite sheet: two figures side by side on a plain ground.</summary>
    private static byte[] SheetPng()
    {
        using var bmp = new SKBitmap(64, 20);
        bmp.Erase(SKColors.White);
        using (var canvas = new SKCanvas(bmp))
        using (var paint = new SKPaint { Color = new SKColor(40, 40, 40) })
        {
            canvas.DrawRect(SKRect.Create(4, 4, 22, 12), paint);
            canvas.DrawRect(SKRect.Create(38, 4, 22, 12), paint);
        }
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [AvaloniaFact]
    public void LayingATileOntoTheTimeline_SlicesFramesFromThePlayhead()
    {
        // The Reference docker's analysis, reachable from the wall: the same
        // slicer, the same layout from the playhead, the same timeline
        // extension the docker's ＋ import does.
        var vm = VmLayers.PaperVm();
        var tile = new BoardTile { Name = "Run", Png = Convert.ToBase64String(SheetPng()) };

        var strip = vm.ImportBoardTileAsAnimation(tile, SheetPng());

        Assert.NotNull(strip);
        Assert.Equal(2, strip!.Cells.Count);
        Assert.Equal(0, strip.FirstAssignedSlot());
        Assert.True(vm.Doc.Scene.FrameCount >= 2);
        // An import, not a projection: no pin, no board link — taking a
        // projection down must never delete a laid-out animation.
        Assert.False(strip.Pinned);
        Assert.Null(strip.BoardTileId);
    }

    [AvaloniaFact]
    public void LayingATileWithNoPicture_ImportsNothing()
    {
        var vm = VmLayers.PaperVm();

        Assert.Null(vm.ImportBoardTileAsAnimation(ImageTile(), null));
        Assert.Null(vm.ImportBoardTileAsAnimation(ImageTile(), [9, 9, 9]));
        Assert.Null(vm.Doc.Scene.References);
    }

    [AvaloniaFact]
    public void TheProjectionRoundTrips_LinkIntact()
    {
        var vm = VmLayers.PaperVm();
        var tile = ImageTile();
        vm.ToggleTileOnCanvas(tile, Png());

        var back = DocJson.Deserialize(DocJson.Serialize(vm.Doc));

        var strip = Assert.Single(back.Scene.References!);
        Assert.Equal(tile.Id, strip.BoardTileId);
        Assert.True(strip.Pinned);
        Assert.NotEmpty(strip.Png);
    }
}

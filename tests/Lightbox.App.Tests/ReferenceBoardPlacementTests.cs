using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Where a picture lands when it arrives on the board (B243), that the board can
/// be reached and used with no character sheet at all (B245), and that its keys
/// stay inside its own window (B244).
/// </summary>
[Collection("BrushState")]
public class ReferenceBoardPlacementTests : BrushStateIsolated
{
    private static string TempImage(int width = 80, int height = 60)
    {
        using var bmp = new SKBitmap(width, height);
        bmp.Erase(SKColors.White);
        using (var canvas = new SKCanvas(bmp))
        using (var paint = new SKPaint { Color = new SKColor(40, 90, 200) })
        {
            canvas.DrawRect(SKRect.Create(8, 8, width - 16, height - 16), paint);
        }
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var path = Path.Combine(Path.GetTempPath(), $"lightbox-board-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    private static MainViewModel VmWithASheet()
    {
        var vm = VmLayers.PaperVm();
        var sheet = vm.AddReferenceSheet("Hero")!;
        vm.AddReferenceView(sheet);
        return vm;
    }

    [AvaloniaFact]
    public void ADroppedPictureLandsWhereItWasDropped()
    {
        var vm = VmWithASheet();
        var board = new ReferenceBoardViewModel(vm);
        board.AutoLoad(900, 600);
        var path = TempImage();
        try
        {
            var tile = board.AddImageFile(path, (400, 300));

            Assert.NotNull(tile);
            // Centred on the point, which is where the pointer let go — not below
            // the bottom-most tile, which on an arranged wall is off the window.
            Assert.Equal(400 - tile!.Width / 2, tile.X, 3);
            Assert.Equal(300 - tile.Height / 2, tile.Y, 3);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void APictureAddedFromThePickerLandsInView()
    {
        var vm = VmWithASheet();
        var window = new ReferenceBoardWindow(vm);
        var path = TempImage();
        try
        {
            // What the picker and the drop both do: place it against the visible
            // board rather than under everything on it.
            var tile = window.BoardModel.AddImageFile(path, (450, 300))!;

            var bottomOfTheSheets = window.BoardModel.Tiles
                .Where(t => t.IsView)
                .Max(t => t.Y + t.Height);
            Assert.True(
                tile.Y < bottomOfTheSheets,
                $"landed at {tile.Y}, below the wall's bottom edge at {bottomOfTheSheets} — off screen (B243)");
        }
        finally
        {
            File.Delete(path);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void FittingBringsAPictureDraggedIntoTheDistanceBack()
    {
        var vm = VmWithASheet();
        var window = new ReferenceBoardWindow(vm);
        var path = TempImage();
        try
        {
            var far = window.BoardModel.AddImageFile(path, (9000, 7000))!;

            window.FitToView();

            // Fit is the way back: everything on the board is inside the window
            // afterwards, whatever was done to it before.
            foreach (var tile in window.BoardModel.Tiles)
            {
                var (x, y) = ScreenPositionOf(window, tile);
                Assert.InRange(x, -1, window.Width + 1);
                Assert.InRange(y, -1, window.Height + 1);
            }
            Assert.Equal(9000 - far.Width / 2, far.X, 3);
        }
        finally
        {
            File.Delete(path);
            window.Close();
        }
    }

    /// <summary>A tile's top-left in window coordinates, through the board's own transform.</summary>
    private static (double X, double Y) ScreenPositionOf(ReferenceBoardWindow window, Core.Documents.BoardTile tile)
    {
        var transform = window.Surface.RenderTransform!.Value;
        var point = new Avalonia.Point(tile.X, tile.Y).Transform(transform);
        return (point.X, point.Y);
    }

    // ---- reachable without a sheet (B245) ---------------------------------------------

    [AvaloniaFact]
    public void TheBoardOpensWithNoSheetsAtAll()
    {
        // The wall of photographs case: no character sheet has ever been made, and
        // the board is still the thing an artist wants.
        var vm = VmLayers.PaperVm();
        var window = new ReferenceBoardWindow(vm);
        var path = TempImage();
        try
        {
            Assert.Empty(window.BoardModel.Tiles);

            var tile = window.BoardModel.AddImageFile(path, (200, 150));

            Assert.NotNull(tile);
            Assert.Single(window.Surface.Children);
        }
        finally
        {
            File.Delete(path);
            window.Close();
        }
    }

    // ---- the board's keys stay in the board (B244) -------------------------------------

    [AvaloniaFact]
    public void TheBoardsKeysDoNotAnswerOutsideTheBoard()
    {
        var map = new Lightbox.App.Services.ShortcutMap();
        static Avalonia.Input.KeyEventArgs Key(
            Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers = Avalonia.Input.KeyModifiers.None) =>
            new() { Key = key, KeyModifiers = modifiers, RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent };

        // Inside the board they mean what they obviously mean…
        Assert.Equal("reference.remove", map.IdFor(Key(Avalonia.Input.Key.Delete), Services.ShortcutScope.Board));
        Assert.Equal(
            "reference.paste",
            map.IdFor(Key(Avalonia.Input.Key.V, Avalonia.Input.KeyModifiers.Control), Services.ShortcutScope.Board));

        // …and nowhere else, or the board would have quietly taken Delete from the
        // canvas and Ctrl+V from the timeline.
        Assert.Equal("select.clear", map.IdFor(Key(Avalonia.Input.Key.Delete), Services.ShortcutScope.Canvas));
        Assert.Null(map.IdFor(Key(Avalonia.Input.Key.Delete), Services.ShortcutScope.General));
        Assert.Equal(
            "timeline.pasteCel",
            map.IdFor(Key(Avalonia.Input.Key.V, Avalonia.Input.KeyModifiers.Control), Services.ShortcutScope.General));
    }

    [AvaloniaFact]
    public void TheBoardIsReachableFromTheViewMenu()
    {
        // The doorway B245 was about: opening the board must not require a sheet
        // to exist, so the command that opens it is registered rather than living
        // only on a button beside a view.
        var map = new Lightbox.App.Services.ShortcutMap();

        var definition = map.Find("reference.board");

        Assert.NotNull(definition);
        Assert.Equal(Services.ShortcutContext.Global, definition!.Context);
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The reference board window (Q87): a wall of reference beside the art,
/// arranged by hand and remembered.
/// </summary>
/// <remarks>
/// The first four tests are the promises Q69's single-view window made, carried
/// over to the window that replaced it — it shows the picture, it follows edits,
/// it does not churn on unrelated ones, and a closed one stops listening. The
/// rest are the board's own.
/// </remarks>
[Collection("BrushState")]
public class ReferenceBoardWindowTests : BrushStateIsolated
{
    private static (MainViewModel Vm, ReferenceView View) VmWithAnInkedView()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 12;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;

        var sheet = vm.AddReferenceSheet("Hero")!;
        vm.AddReferenceView(sheet);
        var view = sheet.Views[0];
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(300, 200, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (vm, view);
    }

    private static Image PictureOf(ReferenceBoardWindow window) =>
        window.Surface.Children.OfType<Border>().Select(b => b.Child).OfType<Image>().First();

    [AvaloniaFact]
    public void TheBoardPinsUpEverySheetInScope_AndShowsItsPicture()
    {
        var (vm, view) = VmWithAnInkedView();

        var window = new ReferenceBoardWindow(vm);

        var tile = Assert.Single(window.BoardModel.Tiles);
        Assert.Equal(view.Id, tile.ViewId);
        Assert.Contains("Hero", tile.Name);
        Assert.NotNull(PictureOf(window).Source);
        window.Close();
    }

    [AvaloniaFact]
    public void EditingASheetUpdatesItsTile()
    {
        var (vm, _) = VmWithAnInkedView();
        var window = new ReferenceBoardWindow(vm);
        var before = PictureOf(window).Source;

        vm.BeginStroke(400, 300, 1);
        vm.MoveStroke(600, 400, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotSame(before, PictureOf(window).Source);
        window.Close();
    }

    [AvaloniaFact]
    public void AnUnrelatedEditDoesNotChurnTheBoard()
    {
        var (vm, _) = VmWithAnInkedView();
        var window = new ReferenceBoardWindow(vm);
        var before = PictureOf(window).Source;

        // The funnel fires, the picture did not change — the bitmap instance must
        // not be replaced, or every edit anywhere flickers the whole wall.
        vm.MarkDocumentEditedForTests();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Same(before, PictureOf(window).Source);
        window.Close();
    }

    [AvaloniaFact]
    public void AClosedBoardStopsListening()
    {
        var (vm, _) = VmWithAnInkedView();
        var window = new ReferenceBoardWindow(vm);
        var image = PictureOf(window);
        window.Close();
        var after = image.Source;

        vm.BeginStroke(500, 300, 1);
        vm.MoveStroke(700, 300, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // A window that keeps rendering after it is gone is a leak wearing a
        // feature's name.
        Assert.Same(after, image.Source);
    }

    [AvaloniaFact]
    public void TheTopmostTileIsTheOneUnderThePointer()
    {
        var (vm, _) = VmWithAnInkedView();
        var window = new ReferenceBoardWindow(vm);
        var lower = window.BoardModel.Tiles[0];
        lower.X = 0;
        lower.Y = 0;
        lower.Width = 200;
        lower.Height = 200;

        var sheet = vm.ReferenceSheetsView[0];
        vm.AddReferenceView(sheet);
        var upper = window.BoardModel.Pin(sheet, sheet.Views[^1])!;
        upper.X = 50;
        upper.Y = 50;
        upper.Width = 200;
        upper.Height = 200;

        // Where they overlap, the front one wins; where only the back one is,
        // it is still pickable.
        Assert.Same(upper, window.TileAt(new Point(100, 100)));
        Assert.Same(lower, window.TileAt(new Point(10, 10)));
        Assert.Null(window.TileAt(new Point(900, 900)));
        window.Close();
    }

    [AvaloniaFact]
    public void RearrangingTheWallDoesNotReRenderItsPictures()
    {
        var (vm, _) = VmWithAnInkedView();
        var window = new ReferenceBoardWindow(vm);
        var before = PictureOf(window).Source;

        window.BoardModel.Arrange(800, 600);

        // Rendering a view tile is a compose, a PNG encode and a base64 — 52 ms
        // for one 960×540 view (B31). Moving pictures around must not pay it: the
        // same bitmap instance survives the rebuild.
        Assert.Same(before, PictureOf(window).Source);
        window.Close();
    }

    [AvaloniaFact]
    public void TakingATileOffTheWallLetsGoOfItsPicture()
    {
        var (vm, _) = VmWithAnInkedView();
        var window = new ReferenceBoardWindow(vm);
        Assert.Single(window.Surface.Children);

        window.BoardModel.Remove(window.BoardModel.Tiles[0]);

        // A cache that only ever grows is a leak wearing a feature's name.
        Assert.Empty(window.Surface.Children);
        Assert.Empty(window.BoardModel.Tiles);
        window.Close();
    }

    [AvaloniaFact]
    public void TheBoardsCommandsAreInTheShortcutRegistry()
    {
        var map = new Lightbox.App.Services.ShortcutMap();

        // A command wired straight to the window's key handler works perfectly and
        // is invisible to the Configure window's editor — it cannot be found,
        // searched or rebound. Being here is what makes it configurable.
        foreach (var id in new[] { "reference.arrange", "reference.front", "reference.back" })
        {
            var definition = map.Find(id);
            Assert.NotNull(definition);
            Assert.NotNull(definition!.Default);
        }
    }

    [AvaloniaFact]
    public void ATileWhoseViewHasGoneIsNotDrawn()
    {
        var (vm, _) = VmWithAnInkedView();
        var window = new ReferenceBoardWindow(vm);
        window.BoardModel.Board.Tiles.Add(new BoardTile { ViewId = "view-that-never-existed" });

        // Redrawn by any change; the dead tile contributes no control rather than
        // throwing or leaving a blank frame on the wall.
        window.BoardModel.Arrange(800, 600);

        Assert.Equal(2, window.BoardModel.Tiles.Count);
        Assert.Single(window.Surface.Children);
        window.Close();
    }
}

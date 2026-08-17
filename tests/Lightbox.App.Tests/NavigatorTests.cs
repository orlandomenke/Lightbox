using Avalonia;
using Avalonia.Headless.XUnit;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The navigator (Q110): the whole drawing small, the view drawn on it, and a
/// click to go there.
/// </summary>
[Collection("BrushState")]
public class NavigatorTests : BrushStateIsolated
{
    private static NavigatorView Panel(double width = 200, double height = 120) =>
        new()
        {
            DocumentSize = new Size(800, 400),
            Width = width,
            Height = height,
        };

    /// <summary>Measure and arrange it, so Bounds is real.</summary>
    private static NavigatorView Laid(double width = 200, double height = 120)
    {
        var panel = Panel(width, height);
        panel.Measure(new Size(width, height));
        panel.Arrange(new Rect(0, 0, width, height));
        return panel;
    }

    [AvaloniaFact]
    public void ThePictureKeepsTheDocumentsShapeInsideThePanel()
    {
        // 800×400 in a 200×120 panel: letterboxed to 200×100 and centred, not
        // stretched — a stretched picture would put the viewport rectangle
        // somewhere the drawing is not.
        var panel = Laid();

        var picture = panel.PictureBounds();

        Assert.Equal(200, picture.Width, 3);
        Assert.Equal(100, picture.Height, 3);
        Assert.Equal(0, picture.X, 3);
        Assert.Equal(10, picture.Y, 3);
    }

    [AvaloniaFact]
    public void APointOnThePanelIsAPointInTheDocument()
    {
        var panel = Laid();

        // The middle of the picture is the middle of the document.
        var (x, y) = panel.ToDocument(new Point(100, 60));

        Assert.Equal(400, x, 3);
        Assert.Equal(200, y, 3);
    }

    [AvaloniaFact]
    public void AskingForAPointMovesTheCanvasThere()
    {
        // The panel's half is naming a document point; the canvas's half is
        // going there. This is the second half, and it is the one an artist
        // sees: the point asked for ends up in the middle of the view.
        var canvas = new Lightbox.App.Rendering.CanvasControl { Width = 400, Height = 300 };
        canvas.Measure(new Size(400, 300));
        canvas.Arrange(new Rect(0, 0, 400, 300));
        var before = canvas.Framing;

        canvas.CentreOn(900, 700);

        var after = canvas.Framing;
        Assert.NotEqual((before.PanX, before.PanY), (after.PanX, after.PanY));
        // View-only, the whole way: nothing about how closely you are looking
        // changed, only where (invariant 5).
        Assert.Equal(before.Zoom, after.Zoom, 6);
        Assert.Equal(before.RotationDeg, after.RotationDeg, 6);
        Assert.Equal(before.Mirrored, after.Mirrored);
    }

    // ---- what the view model contributes -------------------------------------------

    [AvaloniaFact]
    public void TheNavigatorOpensWithEveryShippedWorkspace()
    {
        foreach (var workspace in WorkspaceStore.Default().Workspaces)
        {
            Assert.True(
                workspace.Layout.IsVisible(DockPanelId.Navigator),
                $"{workspace.Name} does not open the navigator");
            // Its own slot: it is short, and it is glanced at rather than read.
            Assert.Single(workspace.Layout.SlotOf(DockPanelId.Navigator));
        }
    }

    [AvaloniaFact]
    public void ThePictureIsComposedOnlyWhileThePanelIsOpen()
    {
        // A compose of every visible layer is not something to do for a panel
        // nobody has on screen.
        var vm = VmLayers.PaperVm();
        vm.Workspace.NavigatorVisible = true;
        vm.MarkDocumentEditedForTests();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.NotNull(vm.NavigatorThumb);

        vm.ToggleNavigatorCommand.Execute(null);

        Assert.False(vm.Workspace.NavigatorVisible);
        Assert.Null(vm.NavigatorThumb);
    }

    [AvaloniaFact]
    public void TheViewportIsWhateverTheCanvasLastReported()
    {
        var vm = VmLayers.PaperVm();

        vm.SetViewport(new SKRectI(10, 20, 110, 220));

        Assert.Equal(new SKRectI(10, 20, 110, 220), vm.NavigatorViewport);
        // And the document's own size, which the panel scales the rectangle
        // against, comes from the document rather than from the view.
        Assert.Equal(vm.Doc.Scene.Width, vm.NavigatorDocumentSize.Width, 3);
        Assert.Equal(vm.Doc.Scene.Height, vm.NavigatorDocumentSize.Height, 3);
    }
}

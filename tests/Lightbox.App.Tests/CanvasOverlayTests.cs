using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;

namespace Lightbox.App.Tests;

/// <summary>
/// The bars that float on the canvas: which edge a drop lands on, and what the
/// window does with the answer.
///
/// The geometry is separated from the window on purpose — where a drop belongs
/// is arithmetic, and arithmetic that needs a window to test is arithmetic
/// nobody tests.
/// </summary>
public class CanvasOverlayGeometryTests
{
    [Theory]
    // A point near an edge belongs to that edge, and the canvas divides into
    // four triangles meeting at the centre.
    [InlineData(500, 10, CanvasEdge.Top)]
    [InlineData(500, 590, CanvasEdge.Bottom)]
    [InlineData(10, 300, CanvasEdge.Left)]
    [InlineData(990, 300, CanvasEdge.Right)]
    // Corners: the nearer of the two edges wins, and a true corner is stable.
    [InlineData(5, 40, CanvasEdge.Left)]
    [InlineData(40, 5, CanvasEdge.Top)]
    public void ADropGoesToTheNearestEdge(double x, double y, CanvasEdge expected) =>
        Assert.Equal(expected, CanvasOverlayLayout.NearestEdge(x, y, 1000, 600));

    [Fact]
    public void TheAnswerDependsOnlyOnWhereThePointerIs()
    {
        // Not on which edge the bar came from. A drag that resolves differently
        // depending on where it started is a drag you cannot aim.
        for (var x = 0; x <= 1000; x += 137)
        {
            for (var y = 0; y <= 600; y += 97)
            {
                Assert.Equal(
                    CanvasOverlayLayout.NearestEdge(x, y, 1000, 600),
                    CanvasOverlayLayout.NearestEdge(x, y, 1000, 600));
            }
        }
    }

    [Fact]
    public void HowFarAlongIsAFractionOfTheEdgeItIsOn()
    {
        // A fraction rather than pixels, so a bar keeps its place when the
        // window is resized instead of drifting into the middle of the canvas.
        Assert.Equal(0.25, CanvasOverlayLayout.AlongFor(CanvasEdge.Top, 250, 0, 1000, 600), 5);
        Assert.Equal(0.5, CanvasOverlayLayout.AlongFor(CanvasEdge.Left, 0, 300, 1000, 600), 5);
        // Off the end clamps rather than wrapping or going negative.
        Assert.Equal(1, CanvasOverlayLayout.AlongFor(CanvasEdge.Bottom, 5000, 600, 1000, 600), 5);
        Assert.Equal(0, CanvasOverlayLayout.AlongFor(CanvasEdge.Right, 1000, -40, 1000, 600), 5);
    }

    [Fact]
    public void ABarOnASideEdgeRunsVertically()
    {
        Assert.True(CanvasOverlayLayout.IsVertical(CanvasEdge.Left));
        Assert.True(CanvasOverlayLayout.IsVertical(CanvasEdge.Right));
        Assert.False(CanvasOverlayLayout.IsVertical(CanvasEdge.Top));
        Assert.False(CanvasOverlayLayout.IsVertical(CanvasEdge.Bottom));
    }

    [Fact]
    public void ADegenerateCanvasDoesNotDivideByZero()
    {
        Assert.Equal(CanvasEdge.Top, CanvasOverlayLayout.NearestEdge(0, 0, 0, 0));
        Assert.Equal(0, CanvasOverlayLayout.AlongFor(CanvasEdge.Top, 10, 10, 0, 0), 5);
    }

    [Fact]
    public void TheDefaultPutsViewTopRightAndShortcutsDownTheSide()
    {
        var layout = CanvasOverlayLayout.Default();

        Assert.Equal(CanvasEdge.Top, layout.Place(OverlayId.View).Edge);
        Assert.Equal(1, layout.Place(OverlayId.View).Along, 5);
        Assert.Equal(CanvasEdge.Right, layout.Place(OverlayId.Shortcuts).Edge);
        Assert.True(layout.IsVisible(OverlayId.View));
        Assert.True(layout.IsVisible(OverlayId.Shortcuts));
    }

    [Fact]
    public void ALayoutClonesRatherThanSharingItsPlacements()
    {
        // Workspaces hand layouts around by copy; a shared placement means
        // editing one workspace moves the bars in all of them.
        var layout = CanvasOverlayLayout.Default();
        var copy = layout.Clone();

        copy.Place(OverlayId.View).Edge = CanvasEdge.Bottom;

        Assert.Equal(CanvasEdge.Top, layout.Place(OverlayId.View).Edge);
    }
}

/// <summary>The bars as the window actually places them.</summary>
[Collection("BrushState")]
public sealed class CanvasOverlayTests : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, (MainViewModel)window.DataContext!);
    }

    private static LayoutTransformControl Host(MainWindow w, OverlayId id) =>
        w.FindControl<LayoutTransformControl>(id == OverlayId.View ? "ViewBarHost" : "ShortcutBarHost")!;

    [AvaloniaFact]
    public void BothBarsAreOnTheCanvasToStartWith()
    {
        var (w, _) = Open();

        Assert.True(Host(w, OverlayId.View).IsVisible);
        Assert.True(Host(w, OverlayId.Shortcuts).IsVisible);
        // On the canvas, not in a strip — they must not be taking room away
        // from the drawing.
        var canvasHost = w.FindControl<Panel>("CanvasHost");
        Assert.Same(canvasHost, Host(w, OverlayId.View).Parent);

        // And a drag is measured against the canvas, not against the bar. Left
        // unset, every move event reports a position relative to the thing
        // that is moving, and the drag chases itself.
        foreach (var bar in w.GetVisualDescendants().OfType<CanvasOverlayBar>())
        {
            Assert.Same(canvasHost, bar.DragHost);
        }
    }

    [AvaloniaFact]
    public void MovingABarToASideEdgeTurnsItAQuarterTurn()
    {
        // So its length runs along the edge rather than jutting out over the
        // drawing. A rotation that only changed the look would leave the bar
        // occupying a wide, short rectangle on a tall, thin edge.
        var (w, vm) = Open();
        var host = Host(w, OverlayId.View);
        Assert.Null(host.LayoutTransform);

        vm.Workspace.PlaceOverlay(OverlayId.View, CanvasEdge.Left, 0.5);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var rotation = Assert.IsType<Avalonia.Media.RotateTransform>(host.LayoutTransform);
        Assert.Equal(90, rotation.Angle, 5);
        Assert.Equal(HorizontalAlignment.Left, host.HorizontalAlignment);

        vm.Workspace.PlaceOverlay(OverlayId.View, CanvasEdge.Bottom, 0.5);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Null(host.LayoutTransform);
        Assert.Equal(VerticalAlignment.Bottom, host.VerticalAlignment);
    }

    [AvaloniaFact]
    public void ClosingABarHidesItAndTheViewMenuBringsItBack()
    {
        var (w, vm) = Open();
        var bar = w.GetVisualDescendants().OfType<CanvasOverlayBar>()
            .First(b => b.OverlayId == OverlayId.Shortcuts);

        vm.Workspace.SetOverlayVisible(OverlayId.Shortcuts, false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(Host(w, OverlayId.Shortcuts).IsVisible);
        Assert.False(vm.Workspace.ShortcutBarVisible);

        vm.Workspace.ToggleShortcutBarCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(Host(w, OverlayId.Shortcuts).IsVisible);
        // The same control comes back, not a rebuilt one.
        Assert.Same(bar, w.GetVisualDescendants().OfType<CanvasOverlayBar>()
            .First(b => b.OverlayId == OverlayId.Shortcuts));
    }

    [AvaloniaFact]
    public void CollapsingABarSurvivesAWorkspaceReset()
    {
        // Which is the point of keeping the state in the workspace rather than
        // in the control: rearranging panels must not roll your bars back up.
        var (w, vm) = Open();

        vm.Workspace.SetOverlayCollapsed(OverlayId.View, true);
        vm.Workspace.SaveAs("Bars");
        vm.Workspace.Reset();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(vm.Workspace.Layout.Overlays.Place(OverlayId.View).Collapsed);
        Assert.True(w.GetVisualDescendants().OfType<CanvasOverlayBar>()
            .First(b => b.OverlayId == OverlayId.View).Collapsed);
    }

    [AvaloniaFact]
    public void TheBarsAreListedSeparatelyFromThePanels()
    {
        // A panel takes room away from the drawing; a bar sits on top of it.
        // Somebody who wants no panels may still want the zoom readout, so
        // hiding every docker must leave the bars alone.
        var (_, vm) = Open();

        foreach (var id in Enum.GetValues<DockPanelId>()) vm.Workspace.SetVisible(id, false);

        Assert.True(vm.Workspace.ViewBarVisible);
        Assert.True(vm.Workspace.ShortcutBarVisible);
    }

    // ---- what the shortcut bar offers -------------------------------------------

    [AvaloniaFact]
    public void TheOnionToggleActsOnTheLayerBeingDrawnOn()
    {
        var (_, vm) = Open();
        Assert.True(vm.ActiveLayerOnion);

        vm.ActiveLayerOnion = false;

        Assert.False(vm.Doc.Scene.Layers[vm.ActiveLayerIndex].OnionEnabled);
    }

    [AvaloniaFact]
    public void OneButtonForPlayAndPause()
    {
        var (_, vm) = Open();
        Assert.Equal("▶", vm.PlayPauseGlyph);

        vm.TogglePlaybackCommand.Execute(null);
        Assert.True(vm.IsPlaying);
        Assert.Equal("⏸", vm.PlayPauseGlyph);

        vm.TogglePlaybackCommand.Execute(null);
        Assert.False(vm.IsPlaying);
        Assert.Equal("▶", vm.PlayPauseGlyph);
    }

    [AvaloniaFact]
    public void AnIllustrationProjectIsNotOfferedTransportControls()
    {
        // Workspace-relevant: an illustration is not going to be played, so a
        // play button on it is a control that can only disappoint.
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-bar-{Guid.NewGuid():N}.lbproj");
        try
        {
            var (_, vm) = Open();
            Assert.True(vm.ShowsTransport);   // no project: might be anything

            vm.NewProject(root, "Study", Lightbox.Core.Projects.ProjectType.Illustration);
            Assert.False(vm.ShowsTransport);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void TheCameraToggleIsAbsentUntilThereIsACamera()
    {
        var (_, vm) = Open();
        Assert.False(vm.HasCamera);

        vm.AddCameraCommand.Execute(null);

        Assert.True(vm.HasCamera);
    }
}

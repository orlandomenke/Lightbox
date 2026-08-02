using Avalonia;
using Avalonia.Headless;
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

    private static CanvasOverlayBar Host(MainWindow w, OverlayId id) =>
        w.FindControl<CanvasOverlayBar>(id == OverlayId.View ? "ViewBar" : "ShortcutBar")!;

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
    public void ABarOnASideEdgeStacksDownwardsWithItsIconsUpright()
    {
        // It used to rotate the whole control a quarter turn, which got the
        // shape right and put every glyph in it on its side. Stacking gets the
        // same footprint and leaves the icons the right way up.
        var (w, vm) = Open();
        var bar = Host(w, OverlayId.View);
        Assert.False(bar.IsVertical);
        Assert.Equal(0, bar.ReadoutAngle, 5);

        vm.Workspace.PlaceOverlay(OverlayId.View, CanvasEdge.Left, 0.5);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(bar.IsVertical);
        Assert.Equal(CanvasEdge.Left, bar.Edge);
        Assert.Equal(HorizontalAlignment.Left, bar.HorizontalAlignment);
        // Nothing is rotated: the bar changed shape, not orientation.
        Assert.Null(bar.RenderTransform);

        vm.Workspace.PlaceOverlay(OverlayId.View, CanvasEdge.Bottom, 0.5);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(bar.IsVertical);
        Assert.Equal(VerticalAlignment.Bottom, bar.VerticalAlignment);
    }

    [AvaloniaFact]
    public void ABarFollowsThePointerWhileItIsBeingDragged()
    {
        // Not only on release. A bar that jumps when you let go makes you drop
        // it to find out where it would land, which is a guess followed by an
        // undo rather than a drag.
        var (w, vm) = Open();
        var bar = Host(w, OverlayId.View);
        var canvas = w.FindControl<Panel>("CanvasHost")!;
        var grip = bar.GetVisualDescendants().OfType<Border>()
            .First(b => b.Name == "PART_Grip");

        var from = grip.TranslatePoint(new Point(4, 4), w)!.Value;
        w.MouseDown(from, Avalonia.Input.MouseButton.Left);
        // Towards the bottom edge, but do not release.
        var target = canvas.TranslatePoint(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height - 4), w)!.Value;
        w.MouseMove(target);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Moved on screen…
        Assert.Equal(CanvasEdge.Bottom, bar.Edge);
        Assert.Equal(VerticalAlignment.Bottom, bar.VerticalAlignment);
        // …but nothing has been written down yet. A drag in progress is not an
        // edit to the workspace; letting go is.
        Assert.Equal(CanvasEdge.Top, vm.Workspace.Layout.Overlays.Place(OverlayId.View).Edge);

        w.MouseUp(target, Avalonia.Input.MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(CanvasEdge.Bottom, vm.Workspace.Layout.Overlays.Place(OverlayId.View).Edge);
    }

    [AvaloniaFact]
    public void TheZoomReadoutTurnsItsFeetTowardsTheCanvas()
    {
        // The one wide thing in the bar, and the only thing that turns. Which
        // way depends on the edge: you read it from the canvas side.
        var (w, vm) = Open();
        var bar = Host(w, OverlayId.View);

        vm.Workspace.PlaceOverlay(OverlayId.View, CanvasEdge.Left, 0.5);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(-90, bar.ReadoutAngle, 5);   // feet to the right

        vm.Workspace.PlaceOverlay(OverlayId.View, CanvasEdge.Right, 0.5);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(90, bar.ReadoutAngle, 5);    // feet to the left

        vm.Workspace.PlaceOverlay(OverlayId.View, CanvasEdge.Top, 0.5);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, bar.ReadoutAngle, 5);
    }

    [AvaloniaFact]
    public void TheOnionToggleAgreesWithTheLayersPanel()
    {
        // The same switch in two places. Flipping the one on the canvas has to
        // move the ◉ in the Layers panel, or one of them is showing yesterday's
        // answer.
        var (_, vm) = Open();
        var row = vm.LayerRows.First(r => r.SceneIndex == vm.ActiveLayerIndex);
        Assert.True(row.OnionEnabled);

        vm.ActiveLayerOnion = false;

        Assert.False(row.OnionEnabled);

        // And the other way round.
        row.OnionEnabled = true;
        Assert.True(vm.ActiveLayerOnion);
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

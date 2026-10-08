using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// B417: the arrow keys flip the animation. Reported as "arrow keys do not
/// work to scrub the timeline" — with the pointer on the drawing they nudged
/// a selection that was not there, and a docker tab that had been clicked took
/// Left/Right for itself. Driven through the headless keyboard, not a
/// hand-raised event, because the defect was in who receives the key.
/// </summary>
[Collection("BrushState")]
public class ArrowKeyScrubTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow { Width = 1400, Height = 900 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Untitled-1", 960, 540, 12, 72, "#ffffff", false));
        while (vm.Doc.Scene.FrameCount < 12) vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 4;
        Pump();
        return (window, vm);
    }

    private static void Pump()
    {
        for (var i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static Point Centre(Visual item, Visual root) =>
        item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), root)!.Value;

    private static Docker DockerFor(MainWindow window, DockPanelId id) =>
        window.GetVisualDescendants().OfType<Docker>().First(d => d.PanelId == id);

    private static void Press(MainWindow window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
        Pump();
    }

    [AvaloniaFact]
    public void OnTheCanvasWithNothingSelected_LeftAndRightStepAFrame()
    {
        var (window, vm) = Open();
        var canvas = window.GetVisualDescendants().OfType<Lightbox.App.Rendering.CanvasControl>().First();
        window.MouseMove(Centre(canvas, window));
        Pump();

        Press(window, PhysicalKey.ArrowRight);
        Assert.Equal(5, vm.CurrentFrameIndex);
        Press(window, PhysicalKey.ArrowLeft);
        Press(window, PhysicalKey.ArrowLeft);
        Assert.Equal(3, vm.CurrentFrameIndex);
    }

    [AvaloniaFact]
    public void OnTheCanvasWithNothingSelected_UpAndDownGoToTheNeighbouringDrawing()
    {
        var (window, vm) = Open();
        var layer = vm.Doc.Scene.Layers[vm.ActiveLayerIndex];
        // Drawings at 0 and 8 only; everything between holds.
        for (var i = 1; i < layer.Cels.Count; i++) layer.Cels[i] = i == 8 ? new Cel { Frame = new Frame() } : new Cel();
        var canvas = window.GetVisualDescendants().OfType<Lightbox.App.Rendering.CanvasControl>().First();
        window.MouseMove(Centre(canvas, window));
        Pump();

        Press(window, PhysicalKey.ArrowDown);
        output.WriteLine($"Down from 4: {vm.CurrentFrameIndex}");
        Assert.Equal(8, vm.CurrentFrameIndex);
        Press(window, PhysicalKey.ArrowUp);
        Assert.Equal(0, vm.CurrentFrameIndex);
    }

    [AvaloniaFact]
    public void WithSomethingSelected_TheArrowsStillNudgeIt()
    {
        var (window, vm) = Open();
        vm.SelectAllCommand.Execute(null);
        Assert.True(vm.HasSelection);
        var canvas = window.GetVisualDescendants().OfType<Lightbox.App.Rendering.CanvasControl>().First();
        window.MouseMove(Centre(canvas, window));
        Pump();

        Press(window, PhysicalKey.ArrowRight);
        Assert.Equal(4, vm.CurrentFrameIndex); // the selection moved, not the playhead
    }

    [AvaloniaFact]
    public void AFocusedDockerTabNoLongerSwallowsTheArrows()
    {
        // The tab strip is a ListBox, and a focused tab handled Left/Right
        // itself — so after clicking a tab the timeline's step never arrived.
        var (window, vm) = Open();
        var timeline = DockerFor(window, DockPanelId.Timeline);
        var tab = timeline.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "PART_Tabs")
            .GetVisualDescendants().OfType<ListBoxItem>().First();
        Assert.False(tab.Focusable);
        tab.Focus();
        Pump();
        window.MouseMove(Centre(timeline, window));
        Pump();

        Press(window, PhysicalKey.ArrowRight);
        Assert.Equal(5, vm.CurrentFrameIndex);
    }

    [AvaloniaFact]
    public void ClickingADockerTabStillPicksIt()
    {
        // Unfocusable is not unclickable: selection comes from the press.
        var (window, _) = Open();
        var timeline = DockerFor(window, DockPanelId.Timeline);
        var strip = timeline.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "PART_Tabs");
        var other = strip.GetVisualDescendants().OfType<ListBoxItem>().First(i => !i.IsSelected);
        var at = Centre(other, window);
        window.MouseMove(at); Pump();
        window.MouseDown(at, MouseButton.Left); Pump();
        window.MouseUp(at, MouseButton.Left); Pump();
        Assert.Same(other.DataContext, strip.SelectedItem);
    }
}

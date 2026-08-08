using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The header's ⧉ button floats a panel; the same button on a floating panel
/// (now ⇱) docks it back where it came from.
/// </summary>
public class FloatRedockTests
{
    // ---- the layout's memory, no window needed -------------------------------

    [Fact]
    public void RedockPutsThePanelBackWhereItFloatedFrom()
    {
        var layout = DockLayout.Default();
        Assert.Equal(DockSide.Right, layout.SideOf(DockPanelId.Layers));
        var order = layout.Place(DockPanelId.Layers).Order;

        layout.Float(DockPanelId.Layers, 100, 100, 320, 400);
        Assert.Equal(DockSide.Floating, layout.SideOf(DockPanelId.Layers));

        layout.Redock(DockPanelId.Layers);
        Assert.Equal(DockSide.Right, layout.SideOf(DockPanelId.Layers));
        Assert.Equal(order, layout.Place(DockPanelId.Layers).Order);
    }

    [Fact]
    public void APanelThatOnlyEverFloatedRedocksToTheRight()
    {
        var layout = DockLayout.Default();
        // Palette starts hidden — float it straight from nowhere.
        layout.Float(DockPanelId.Palette, 100, 100, 320, 400);

        layout.Redock(DockPanelId.Palette);
        Assert.Equal(DockSide.Right, layout.SideOf(DockPanelId.Palette));
    }

    [Fact]
    public void RedockingADockedPanelChangesNothing()
    {
        var layout = DockLayout.Default();
        layout.Redock(DockPanelId.Layers);
        Assert.Equal(DockSide.Right, layout.SideOf(DockPanelId.Layers));
    }

    // ---- through the real chrome ---------------------------------------------

    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, (MainViewModel)window.DataContext!);
    }

    private static Button FloatButtonOf(Docker docker) =>
        docker.GetVisualDescendants().OfType<Button>().First(b => b.Name == "PART_Float");

    [AvaloniaFact]
    public void TheFloatButtonFloatsAndTheSameButtonDocksBack()
    {
        var (w, vm) = Open();
        var docker = w.FindControl<DockStrip>("RightStrip")!.Children.OfType<Docker>()
            .First(d => d.PanelId == DockPanelId.Layers);

        FloatButtonOf(docker).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(DockSide.Floating, vm.Workspace.Layout.SideOf(DockPanelId.Layers));
        Assert.True(docker.IsFloating, "the docker does not know it is floating, so its button still says float");

        // The same instance, in its own window now; the button docks it back.
        FloatButtonOf(docker).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(DockSide.Right, vm.Workspace.Layout.SideOf(DockPanelId.Layers));
        Assert.False(docker.IsFloating);
    }

    [AvaloniaFact]
    public void TheTimelineOffersNoFloatButton()
    {
        var (w, _) = Open();
        var timeline = w.FindControl<Docker>("TimelineDocker")!;
        Assert.False(timeline.CanFloat);
        Assert.False(FloatButtonOf(timeline).IsEffectivelyVisible,
            "the timeline cannot leave the bottom, so it must not offer to");
    }
}

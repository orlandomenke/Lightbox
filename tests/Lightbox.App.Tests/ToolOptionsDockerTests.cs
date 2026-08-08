using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The tool options docker — the gear flyout grown into a panel, per the
/// owner: "create a tool options docker instead of keeping it in the rail."
/// </summary>
[Collection("BrushState")]
public sealed class ToolOptionsDockerTests : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, (MainViewModel)window.DataContext!);
    }

    [AvaloniaFact]
    public void AbsentUntilAskedFor()
    {
        // The same rule the palette and the camera follow: a session that
        // never opens it pays nothing for it.
        var (_, vm) = Open();
        Assert.False(vm.Workspace.ToolOptionsDockerVisible);
    }

    [AvaloniaFact]
    public void TheGearOpensThePanelOnTheRight()
    {
        var (w, vm) = Open();

        vm.OpenToolOptionsCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(vm.Workspace.ToolOptionsDockerVisible);
        var docker = w.FindControl<DockStrip>("RightStrip")!.Children.OfType<Docker>()
            .FirstOrDefault(d => d.PanelId == DockPanelId.ToolOptions);
        Assert.NotNull(docker);

        // And it is the real settings panel, not an empty shell: the brush
        // category pages moved with it.
        var categories = docker!.GetVisualDescendants().OfType<ListBox>()
            .FirstOrDefault(l => l.Name == "BrushCategoryList");
        Assert.NotNull(categories);
    }

    [AvaloniaFact]
    public void TheGearNeverCloses()
    {
        // A gear that closed the panel you were looking at reads as broken.
        var (_, vm) = Open();

        vm.OpenToolOptionsCommand.Execute(null);
        vm.OpenToolOptionsCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(vm.Workspace.ToolOptionsDockerVisible);
    }
}

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
    public void ShipsDockedButBehindTheProjectTree()
    {
        // It used to be hidden until the gear asked, on the rule the palette
        // and the camera follow. Q109 moved it: a tab costs a word in a header
        // rather than a strip of sidebar, which is the same trade that put the
        // palette and the gradient in front of people. So it is open — and it
        // is not the tab you are looking at, which is how it costs nothing.
        var (_, vm) = Open();

        Assert.True(vm.Workspace.ToolOptionsDockerVisible);
        Assert.False(vm.Workspace.IsActiveInItsSlot(DockPanelId.ToolOptions));
        Assert.Contains(DockPanelId.Sheets, vm.Workspace.Layout.SlotOf(DockPanelId.ToolOptions));
    }

    [AvaloniaFact]
    public void TheGearBringsItForwardRatherThanOnlyMarkingItVisible()
    {
        // The bug the docking change created and this pins: the panel is
        // already visible, so a gear that only set visibility did nothing at
        // all — for everybody, since every built-in now groups it.
        var (_, vm) = Open();

        vm.OpenToolOptionsCommand.Execute(null);

        Assert.True(vm.Workspace.IsActiveInItsSlot(DockPanelId.ToolOptions));
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

        // And it is the real settings panel, not an empty shell: a paint tool's
        // behaviour (smoothing, the pen, edges) moved with it. The brush itself
        // is in the brush editor since Q211.
        var behaviour = docker!.GetVisualDescendants().OfType<ScrollViewer>()
            .FirstOrDefault(l => l.Name == "PaintToolOptions");
        Assert.NotNull(behaviour);
    }

    [AvaloniaFact]
    public void ThePanelFollowsTheActiveTool()
    {
        // Owner: "the tool options docker should be dynamic." The panel shows
        // the active tool's options the way the bar does — how a paint tool
        // behaves for the paint tools (Q211), the tool's own controls for the rest.
        var (w, vm) = Open();
        vm.OpenToolOptionsCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var brushPages = w.FindControl<ScrollViewer>("PaintToolOptions")!;
        var toolPanel = w.FindControl<ScrollViewer>("NonPaintToolOptions")!;

        // Brush in hand: the paint tool's behaviour, not the tool panel.
        Assert.Equal(ToolId.Brush, vm.ActiveTool);
        Assert.True(brushPages.IsEffectivelyVisible);
        Assert.False(toolPanel.IsEffectivelyVisible);

        // Fill in hand: the tool panel, named for the tool.
        vm.ActiveTool = ToolId.Fill;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(brushPages.IsEffectivelyVisible);
        Assert.True(toolPanel.IsEffectivelyVisible);
        Assert.Equal("Fill", w.FindControl<TextBlock>("ToolOptionsToolName")!.Text);

        // And back: switching tools never loses the paint tool's panel.
        vm.ActiveTool = ToolId.Eraser;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(brushPages.IsEffectivelyVisible);
        Assert.False(toolPanel.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void AToolWithNothingToConfigureSaysSoInsteadOfGoingBlank()
    {
        var (w, vm) = Open();
        vm.OpenToolOptionsCommand.Execute(null);
        vm.ActiveTool = ToolId.Move;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(vm.ActiveToolHasNoPanelOptions);
        var note = w.FindControl<ScrollViewer>("NonPaintToolOptions")!
            .GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Text?.Contains("no panel options") == true);
        Assert.NotNull(note);
        Assert.True(note!.IsEffectivelyVisible);
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

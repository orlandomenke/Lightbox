using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;

namespace Lightbox.App.Tests;

/// <summary>
/// B230's fix, proven through the window rather than the view model: the
/// menu handlers the XAML actually wires. The view-model tests in
/// <see cref="WorkspaceStoreTests"/> passed while the owner still saw a new
/// workspace appear, so the remaining suspects were the wiring above the
/// view model — these hold that layer to the same promise.
/// </summary>
[Collection("BrushState")]
public sealed class WorkspaceMenuTests : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, (MainViewModel)window.DataContext!);
    }

    private static void Invoke(MainWindow window, string handler)
    {
        var method = typeof(MainWindow).GetMethod(
            handler, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(window, [null, new RoutedEventArgs()]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void TheSaveCurrentMenuItem_OverwritesInPlace_AndAddsNoTab()
    {
        var (window, vm) = Open();
        var names = vm.Workspace.Store.Workspaces.Select(w => w.Name).ToList();
        var selected = vm.Workspace.SelectedName;
        vm.Workspace.SetVisible(DockPanelId.Reference, true);

        Invoke(window, "OnSaveWorkspace");

        // Same workspaces, same names, same order — nothing created, nothing
        // renamed, and the picker's rows (the tabs at the top right) agree.
        Assert.Equal(names, vm.Workspace.Store.Workspaces.Select(w => w.Name).ToList());
        Assert.Equal(selected, vm.Workspace.SelectedName);
        Assert.Equal(names.Count, vm.Workspace.WorkspaceChoices.Count);
        Assert.True(vm.Workspace.Store.Find(selected)!.Layout.IsVisible(DockPanelId.Reference));
        Assert.False(vm.Workspace.IsDirty);
        window.Close();
    }

    [AvaloniaFact]
    public void TheResetMenuItem_TakesABuiltInBackToHowItShipped()
    {
        var (window, vm) = Open();
        var selected = vm.Workspace.SelectedName;
        Assert.True(vm.Workspace.Store.Find(selected)!.BuiltIn);
        vm.Workspace.SetVisible(DockPanelId.Reference, true);
        Invoke(window, "OnSaveWorkspace");

        Invoke(window, "OnResetWorkspace");

        Assert.False(vm.Workspace.Layout.IsVisible(DockPanelId.Reference));
        Assert.False(vm.Workspace.Store.Find(selected)!.Layout.IsVisible(DockPanelId.Reference));
        window.Close();
    }
}

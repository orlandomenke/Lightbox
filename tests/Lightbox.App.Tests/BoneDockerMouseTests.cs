using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lightbox.App.Docking;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The bone panel under a real pointer — B236's regression tests. Every
/// control in the panel was correctly wired and every prior test passed,
/// because the tests drove properties; a mouse could reach none of it. The
/// generic tool-options page (<c>NonPaintToolOptions</c>) is a later sibling
/// in the same host, was visible for every non-paint tool, had nothing to
/// show for the Bone tool — and still hit-tested, an invisible sheet over
/// the whole panel. These tests click.
/// </summary>
public class BoneDockerMouseTests
{
    private static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow { Width = 1400, Height = 900 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Rig", 400, 300, 12, 72, "#ffffff", false));
        vm.SelectToolCommand.Execute(ToolId.Bone);
        vm.Workspace.SetVisible(DockPanelId.ToolOptions, true);
        Pump();
        vm.CreateBoneFromDrag(100, 150, 200, 150);
        Pump();
        return (window, vm);
    }

    private static void Click(MainWindow window, Visual control, Point? inside = null)
    {
        var at = control.TranslatePoint(inside ?? new Point(6, 6), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Pump();
    }

    [AvaloniaFact]
    public void ClickingTheModeRadiosActuallySwitchesTheMode()
    {
        var (window, vm) = Open();
        var bar = window.GetVisualDescendants().OfType<BoneOptionsBar>().First();
        var radios = bar.GetVisualDescendants().OfType<RadioButton>().ToList();

        Click(window, radios.First(r => (string?)r.Content == "Pose"));
        Assert.True(vm.PosingMode, "a mouse click on Pose did not reach the radio");

        Click(window, radios.First(r => (string?)r.Content == "Weights"));
        Assert.True(vm.WeightPainting);

        Click(window, radios.First(r => (string?)r.Content == "Bind"));
        Assert.True(vm.IsBindMode);
    }

    [AvaloniaFact]
    public void ClickingABoneRowSelectsThatBone()
    {
        var (window, vm) = Open();
        vm.CreateBoneFromDrag(200, 150, 260, 150);
        var second = vm.SelectedBoneId!;
        Pump();

        var list = window.GetVisualDescendants().OfType<ListBox>()
            .First(l => l.Name == "BoneList");
        var firstRow = list.GetVisualDescendants().OfType<ListBoxItem>().First();

        Click(window, firstRow);

        Assert.NotNull(vm.SelectedBoneId);
        Assert.NotEqual(second, vm.SelectedBoneId);
    }

    [AvaloniaFact]
    public void TheBoneListCollapsesFromItsHeader()
    {
        var (window, vm) = Open();
        var bar = window.GetVisualDescendants().OfType<BoneOptionsBar>().First();
        var list = bar.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "BoneList");
        var toggle = bar.GetVisualDescendants().OfType<ToggleButton>()
            .First(t => t.Name == "BoneListToggle");

        Assert.True(list.IsEffectivelyVisible);
        Click(window, toggle);
        Assert.False(list.IsEffectivelyVisible, "the list did not fold away");
        Click(window, toggle);
        Assert.True(list.IsEffectivelyVisible);
        _ = vm;
    }

    [AvaloniaFact]
    public void TheGenericOptionsPageStaysOffTheBonePanel()
    {
        var (window, vm) = Open();
        var generic = window.FindControl<ScrollViewer>("NonPaintToolOptions")!;

        // The Bone tool owns its panel; the generic page must be absent, not
        // an invisible sheet on top of it.
        Assert.False(generic.IsEffectivelyVisible);

        // And it comes back for the tools that actually use it.
        vm.SelectToolCommand.Execute(ToolId.Fill);
        Pump();
        Assert.True(generic.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void LeavingWeightsDisarmsTheCanvasBrush()
    {
        var (window, vm) = Open();
        var canvas = window.FindControl<CanvasControl>("Canvas")!;

        vm.WeightPainting = true;
        Pump();
        Assert.True(canvas.WeightPainting, "arming the brush never reached the canvas");

        // Leaving by any exit — posing here — must disarm the canvas too, or
        // the next press paints weights while the panel says Pose.
        vm.PosingMode = true;
        Pump();
        Assert.False(vm.WeightPainting);
        Assert.False(canvas.WeightPainting, "the canvas kept painting weights after the mode left");
    }
}

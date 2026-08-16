using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The Bone tool's panel inside the real Tool options docker — the wiring a
/// view-model test cannot see.
/// </summary>
/// <remarks>
/// Reported 2026-08-16: "there is no clear indication of what bone is
/// selected … I am not able to delete it … I am not seeing a list of bones."
/// Every operation existed on the view model and its tests were green — the
/// failures lived between the XAML and the docker: a two-way combo binding
/// clearing the selection it was showing, and a toolbar-shaped row clipped by
/// a 300px-wide panel.
/// </remarks>
public sealed class BoneDockerTests
{
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
        return (window, vm);
    }

    private static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    [AvaloniaFact]
    public void SelectingABoneSurvivesThePanelShowingIt()
    {
        var (window, vm) = Open();

        // The gesture the canvas makes: create, which selects the new bone.
        vm.CreateBoneFromDrag(100, 150, 200, 150);
        Pump();

        // The failure mode: the panel's list is two-way bound, so refreshing
        // its items wrote null back into the selection it was meant to show —
        // the bone deselected itself the moment the panel heard about it.
        Assert.NotNull(vm.SelectedBoneId);
        Assert.True(vm.HasSelectedBone, "the selection did not survive the panel refresh");

        // And selecting by hand sticks too.
        var id = vm.Doc.Armature!.Bones[0].Id;
        vm.SelectedBoneId = id;
        Pump();
        Assert.Equal(id, vm.SelectedBoneId);
    }

    [AvaloniaFact]
    public void ThePanelListsTheBonesAndTheDeleteButtonIsReachable()
    {
        var (window, vm) = Open();
        vm.CreateBoneFromDrag(100, 150, 200, 150);
        vm.CreateBoneFromDrag(200, 150, 260, 150);
        Pump();

        var bar = window.GetVisualDescendants().OfType<BoneOptionsBar>().First();

        // The list of bones an artist asked for: present, populated, visible.
        var list = bar.GetVisualDescendants().OfType<ListBox>()
            .FirstOrDefault(l => l.Name == "BoneList");
        Assert.NotNull(list);
        Assert.Equal(2, list!.ItemCount);
        Assert.True(list.IsEffectivelyVisible, "the bone list is clipped or collapsed");

        // Delete is an operation on the selected bone, so with one selected
        // the button must be on screen — not parked off the docker's right edge.
        var delete = bar.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.Name == "DeleteBoneButton");
        Assert.NotNull(delete);
        Assert.True(delete!.IsEffectivelyVisible, "the delete button is not reachable");
        var origin = delete.TranslatePoint(new Point(0, 0), window);
        Assert.NotNull(origin);
        Assert.True(origin!.Value.X + delete.Bounds.Width <= window.Width,
            $"the delete button sits at x={origin.Value.X}, outside a {window.Width}px window");
    }

    [AvaloniaFact]
    public void PickingARowInTheListSelectsTheBoneAndARefreshDoesNotUnpickIt()
    {
        var (window, vm) = Open();
        vm.CreateBoneFromDrag(100, 150, 200, 150);
        var first = vm.SelectedBoneId!;
        vm.CreateBoneFromDrag(200, 150, 260, 150);
        Pump();

        var list = window.GetVisualDescendants().OfType<ListBox>()
            .First(l => l.Name == "BoneList");

        // The artist picks the first bone by name.
        list.SelectedItem = ((IEnumerable<BoneRow>)list.ItemsSource!).First(r => r.Id == first);
        Pump();
        Assert.Equal(first, vm.SelectedBoneId);

        // A rig edit refreshes the rows; the pick must ride through it.
        vm.RenameSelectedBone("hip.l");
        Pump();
        Assert.Equal(first, vm.SelectedBoneId);
        Assert.Equal(first, (list.SelectedItem as BoneRow)?.Id);
    }

    [AvaloniaFact]
    public void TheModeRadiosDriveTheViewModelAndFollowIt()
    {
        var (window, vm) = Open();
        vm.CreateBoneFromDrag(100, 150, 200, 150);
        Pump();

        var bar = window.GetVisualDescendants().OfType<BoneOptionsBar>().First();
        var radios = bar.GetVisualDescendants().OfType<RadioButton>().ToList();
        var pose = radios.First(r => (string?)r.Content == "Pose");
        var weights = radios.First(r => (string?)r.Content == "Weights");
        var bind = radios.First(r => (string?)r.Content == "Bind");

        // Checking a radio is the artist switching modes.
        pose.IsChecked = true;
        Pump();
        Assert.True(vm.PosingMode, "the Pose radio did not reach the view model");

        weights.IsChecked = true;
        Pump();
        Assert.True(vm.WeightPainting);
        Assert.False(vm.PosingMode, "weights must leave posing (Q81)");

        bind.IsChecked = true;
        Pump();
        Assert.True(vm.IsBindMode);

        // And the other direction: the shortcut moves the mode, the radio follows.
        vm.PosingMode = true;
        Pump();
        Assert.True(pose.IsChecked);
        Assert.NotEqual(true, bind.IsChecked);
    }

    [AvaloniaFact]
    public void DeleteWithABoneInHandDeletesTheBoneNotTheDrawing()
    {
        var (_, vm) = Open();
        vm.CreateBoneFromDrag(100, 150, 200, 150);
        Pump();
        Assert.True(vm.HasSelectedBone);

        // The Delete key over the canvas lands on this command; with the Bone
        // tool armed it must mean the bone, not the lines underneath it.
        vm.DeleteSelectionContentsCommand.Execute(null);

        Assert.False(vm.HasArmature, "the bone survived Delete");
        Assert.Null(vm.SelectedBoneId);
    }

    [AvaloniaFact]
    public void ARigRefreshDoesNotDeleteTheIkPole()
    {
        var (_, vm) = Open();
        vm.CreateBoneFromDrag(100, 100, 160, 100);
        var root = vm.SelectedBoneId!;
        vm.CreateBoneFromDrag(160, 100, 220, 100);
        var tip = vm.SelectedBoneId!;
        vm.AddIkChainCommand.Execute(null);
        vm.SelectedBoneId = tip;
        vm.SelectedChainPoleKey = root;
        Assert.Equal(root, vm.SelectedBoneChain!.PoleBoneId);

        // The combo clears itself (null, not "") whenever its items rebuild.
        // Null is the panel refreshing; only "" — the "(no pole)" row — is
        // the artist. The pole has to survive the refresh.
        vm.SelectedChainPoleKey = null;
        Assert.Equal(root, vm.SelectedBoneChain!.PoleBoneId);

        vm.SelectedChainPoleKey = "";
        Assert.Null(vm.SelectedBoneChain!.PoleBoneId);
    }
}

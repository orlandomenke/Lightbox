using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// B399 / Q213: Ctrl+click picks folders the way it picks layers — one at a
/// time, each one lit, and a second Ctrl+click takes it back out.
/// </summary>
/// <remarks>
/// The owner's own trace (alpha.117, a mouse): Ctrl+click on four folders in
/// turn, then again on folders already picked. The selection only ever grew,
/// and no folder header showed as picked, because Ctrl cleared the one picked
/// folder the model could hold. With the folders collapsed nothing on screen
/// changed at all — which is what "Ctrl+click on a folder does not work" was.
/// </remarks>
[Collection("BrushState")]
public sealed class FolderMultiSelectTests : BrushStateIsolated
{
    /// <summary>Paper, a, b, c, d, e — "Folder 1" holds a and b, "Folder 2" holds c and d, e is loose.</summary>
    private static MainViewModel Vm()
    {
        var vm = VmLayers.PaperVm();
        while (vm.Doc.Scene.Layers.Count < 6) vm.AddPaintedLayerCommand.Execute(null);
        string[] names = ["a", "b", "c", "d", "e"];
        for (var i = 0; i < names.Length; i++) vm.Doc.Scene.Layers[i + 1].Name = names[i];
        Group(vm, "a", "b");
        Group(vm, "c", "d");
        vm.SelectLayer(Row(vm, "e"), toggle: false, range: false);
        return vm;
    }

    private static void Group(MainViewModel vm, string first, string second)
    {
        vm.SelectLayer(Row(vm, first), toggle: false, range: false);
        vm.SelectLayer(Row(vm, second), toggle: true, range: false);
        vm.GroupLayersCommand.Execute(null);
    }

    private static GroupRow Header(MainViewModel vm, string name) =>
        vm.LayerPanelItems.OfType<GroupRow>().Single(h => h.Group.Name == name);

    private static LayerRow Row(MainViewModel vm, string name) =>
        vm.LayerRows.Single(r => r.Layer.Name == name);

    private static string Picked(MainViewModel vm) =>
        string.Join(" ", vm.SelectedLayers.Select(l => l.Name).Order());

    private static string Lit(MainViewModel vm) =>
        string.Join(" ", vm.LayerPanelItems.OfType<GroupRow>().Where(h => h.IsSelected).Select(h => h.Group.Name).Order());

    [AvaloniaFact]
    public void CtrlClickedFoldersAreAllPickedAndAllLit()
    {
        var vm = Vm();

        vm.SelectGroup(Header(vm, "Folder 1"), toggle: false, range: false);
        vm.SelectGroup(Header(vm, "Folder 2"), toggle: true, range: false);

        Assert.Equal("a b c d", Picked(vm));
        Assert.Equal("Folder 1 Folder 2", Lit(vm));
    }

    [AvaloniaFact]
    public void CtrlClickOnAPickedFolderTakesItBackOut()
    {
        var vm = Vm();
        vm.SelectGroup(Header(vm, "Folder 1"), toggle: false, range: false);
        vm.SelectGroup(Header(vm, "Folder 2"), toggle: true, range: false);

        vm.SelectGroup(Header(vm, "Folder 2"), toggle: true, range: false);

        Assert.Equal("a b", Picked(vm));
        Assert.Equal("Folder 1", Lit(vm));
        Assert.Contains(vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Name, new[] { "a", "b" });
    }

    /// <summary>Picked one layer at a time is picked all the same: the folder is whole.</summary>
    [AvaloniaFact]
    public void CtrlClickOnAFolderWhoseLayersAreAllSelectedTakesThemOut()
    {
        var vm = Vm();
        vm.SelectLayer(Row(vm, "c"), toggle: true, range: false);
        vm.SelectLayer(Row(vm, "d"), toggle: true, range: false);
        Assert.Equal("c d e", Picked(vm));

        vm.SelectGroup(Header(vm, "Folder 2"), toggle: true, range: false);

        Assert.Equal("e", Picked(vm));
    }

    /// <summary>The selection is never empty — as for the last layer, so for the last folder.</summary>
    [AvaloniaFact]
    public void TheOnlyPickedFolderStaysPicked()
    {
        var vm = Vm();
        vm.SelectGroup(Header(vm, "Folder 1"), toggle: false, range: false);

        vm.SelectGroup(Header(vm, "Folder 1"), toggle: true, range: false);

        Assert.Equal("a b", Picked(vm));
        Assert.Equal("Folder 1", Lit(vm));
    }

    [AvaloniaFact]
    public void CtrlClickingALayerOutOfAPickedFolderUnlightsTheFolder()
    {
        var vm = Vm();
        vm.SelectGroup(Header(vm, "Folder 1"), toggle: false, range: false);
        vm.SelectGroup(Header(vm, "Folder 2"), toggle: true, range: false);

        vm.SelectLayer(Row(vm, "c"), toggle: true, range: false);

        Assert.Equal("a b d", Picked(vm));
        Assert.Equal("Folder 1", Lit(vm));
    }

    [AvaloniaFact]
    public void CtrlClickingALayerKeepsThePickedFolders()
    {
        var vm = Vm();
        vm.SelectGroup(Header(vm, "Folder 1"), toggle: false, range: false);

        vm.SelectLayer(Row(vm, "e"), toggle: true, range: false);

        Assert.Equal("a b e", Picked(vm));
        Assert.Equal("Folder 1", Lit(vm));
    }

    [AvaloniaFact]
    public void APlainClickOnALayerDropsEveryPickedFolder()
    {
        var vm = Vm();
        vm.SelectGroup(Header(vm, "Folder 1"), toggle: false, range: false);
        vm.SelectGroup(Header(vm, "Folder 2"), toggle: true, range: false);

        vm.SelectLayer(Row(vm, "e"), toggle: false, range: false);

        Assert.Equal("e", Picked(vm));
        Assert.Equal("", Lit(vm));
    }

    /// <summary>Delete takes the folders, not their layers — no empty folders are left behind.</summary>
    [AvaloniaFact]
    public void DeleteWithTwoFoldersPickedDeletesBothFolders_AndOneUndoBringsThemBack()
    {
        var vm = Vm();
        vm.SelectGroup(Header(vm, "Folder 1"), toggle: false, range: false);
        vm.SelectGroup(Header(vm, "Folder 2"), toggle: true, range: false);

        vm.DeleteActiveLayerCommand.Execute(null);

        Assert.Empty(vm.Doc.Scene.LayerGroups);
        Assert.Equal(new[] { "Background", "e" }, vm.Doc.Scene.Layers.Select(l => l.Name));

        vm.UndoCommand.Execute(null);

        Assert.Equal(2, vm.Doc.Scene.LayerGroups.Count);
        Assert.Equal(6, vm.Doc.Scene.Layers.Count);
    }

    [AvaloniaFact]
    public void DeleteTakesAPickedLayerOutsideThePickedFoldersWithThem()
    {
        var vm = Vm();
        vm.SelectGroup(Header(vm, "Folder 1"), toggle: false, range: false);
        vm.SelectLayer(Row(vm, "e"), toggle: true, range: false);

        vm.DeleteActiveLayerCommand.Execute(null);

        Assert.Equal("Folder 2", Assert.Single(vm.Doc.Scene.LayerGroups).Name);
        Assert.Equal(new[] { "Background", "c", "d" }, vm.Doc.Scene.Layers.Select(l => l.Name));
    }

    [AvaloniaFact]
    public void HidingOnePickedFolderHidesEveryPickedFolder_AsOneStep()
    {
        var vm = Vm();
        vm.SelectGroup(Header(vm, "Folder 1"), toggle: false, range: false);
        vm.SelectGroup(Header(vm, "Folder 2"), toggle: true, range: false);

        vm.SetGroupVisible(Header(vm, "Folder 2").Group, false);

        Assert.All(vm.Doc.Scene.LayerGroups, g => Assert.False(g.Visible));
        vm.UndoCommand.Execute(null);
        Assert.All(vm.Doc.Scene.LayerGroups, g => Assert.True(g.Visible));
    }

    [AvaloniaFact]
    public void LockingOnePickedFolderLocksEveryPickedFolder()
    {
        var vm = Vm();
        vm.SelectGroup(Header(vm, "Folder 1"), toggle: false, range: false);
        vm.SelectGroup(Header(vm, "Folder 2"), toggle: true, range: false);

        vm.SetGroupLocked(Header(vm, "Folder 1").Group, true);

        Assert.All(vm.Doc.Scene.LayerGroups, g => Assert.True(g.Locked));
    }

    /// <summary>A folder outside the pick is acted on alone — the row the artist pointed at.</summary>
    [AvaloniaFact]
    public void HidingAFolderOutsideThePickHidesOnlyThatFolder()
    {
        var vm = Vm();
        vm.SelectGroup(Header(vm, "Folder 1"), toggle: false, range: false);

        vm.SetGroupVisible(Header(vm, "Folder 2").Group, false);

        Assert.True(Header(vm, "Folder 1").Group.Visible);
        Assert.False(Header(vm, "Folder 2").Group.Visible);
    }

    /// <summary>
    /// The adversary's first find: a Shift range that crosses a folder's header
    /// but not all of its layers must not pick the folder — Delete would take
    /// the layers left out of the range with it.
    /// </summary>
    [AvaloniaFact]
    public void AShiftRangeOverPartOfAFolderDoesNotPickThatFolder_SoDeleteKeepsWhatWasNotPicked()
    {
        var vm = Vm();
        vm.SelectLayer(Row(vm, "b"), toggle: false, range: false);

        vm.SelectGroup(Header(vm, "Folder 2"), toggle: false, range: true);

        Assert.Equal("b c d", Picked(vm));
        Assert.Equal("Folder 2", Lit(vm));

        vm.DeleteActiveLayerCommand.Execute(null);

        Assert.Contains(vm.Doc.Scene.Layers, l => l.Name == "a");
        Assert.Contains(vm.Doc.Scene.LayerGroups, g => g.Name == "Folder 1");
    }

    /// <summary>The adversary's second: an outer and an inner folder picked, Delete takes the outer.</summary>
    [AvaloniaFact]
    public void DeleteWithAnOuterAndAnInnerFolderPickedDeletesTheOuter()
    {
        var vm = Vm();
        vm.SelectGroup(Header(vm, "Folder 1"), toggle: false, range: false);
        vm.SelectGroup(Header(vm, "Folder 2"), toggle: true, range: false);
        vm.GroupLayersCommand.Execute(null); // both into Folder 3, which is picked
        vm.SelectLayer(Row(vm, "a"), toggle: false, range: false);
        vm.SelectGroup(Header(vm, "Folder 3"), toggle: false, range: true);
        Assert.Contains("Folder 3", Lit(vm));

        vm.DeleteActiveLayerCommand.Execute(null);

        Assert.Empty(vm.Doc.Scene.LayerGroups);
        Assert.Equal(new[] { "Background", "e" }, vm.Doc.Scene.Layers.Select(l => l.Name));
    }

    /// <summary>A lock anywhere in the pick refuses the whole delete and says which layer.</summary>
    [AvaloniaFact]
    public void ALockedLayerInThePickRefusesTheWholeDelete()
    {
        var vm = Vm();
        Row(vm, "e").Layer.Locked = true;
        vm.SelectLayer(Row(vm, "e"), toggle: false, range: false);
        vm.SelectGroup(Header(vm, "Folder 1"), toggle: true, range: false);

        vm.DeleteActiveLayerCommand.Execute(null);

        Assert.Equal(2, vm.Doc.Scene.LayerGroups.Count);
        Assert.Equal(6, vm.Doc.Scene.Layers.Count);
        Assert.Contains("e", vm.AiStatus);
    }
}

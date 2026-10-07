using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Q204 in the docker: the owner's report, one sentence at a time.
/// </summary>
/// <remarks>
/// "Empty folder can exist in the layers docker. Currently a new folder always
/// needs content or if a layer is selected overwrites the pre-existing folder.
/// A folder is just an container object for grouping layers. It can be empty
/// it can be selected separately and it is always created above the current
/// selection or at the top. But can be rearranged just like layers."
/// </remarks>
[Collection("BrushState")]
public sealed class EmptyAndNestedFolderTests : BrushStateIsolated
{
    /// <summary>Paper, a, b — bottom first — with "b" active.</summary>
    private static MainViewModel Vm()
    {
        var vm = VmLayers.PaperVm();
        while (vm.Doc.Scene.Layers.Count < 3) vm.AddPaintedLayerCommand.Execute(null);
        vm.Doc.Scene.Layers[1].Name = "a";
        vm.Doc.Scene.Layers[2].Name = "b";
        vm.ActiveLayerIndex = 2;
        return vm;
    }

    /// <summary>The docker as text, top first, a dot per level of nesting.</summary>
    private static string Docker(MainViewModel vm) => string.Join(" ", vm.LayerPanelItems.Select(item => item switch
    {
        GroupRow g => new string('.', g.Depth) + $"[{g.Group.Name}]",
        LayerRow r => new string('.', r.Depth) + r.Layer.Name,
        _ => "?",
    }));

    private static GroupRow Header(MainViewModel vm, string name) =>
        vm.LayerPanelItems.OfType<GroupRow>().Single(h => h.Group.Name == name);

    private static LayerRow Row(MainViewModel vm, string name) =>
        vm.LayerRows.Single(r => r.Layer.Name == name);

    [AvaloniaFact]
    public void ANewFolderIsEmpty_ShownAboveTheActiveLayer_AndPickedOnItsOwn()
    {
        var vm = Vm();

        vm.CreateLayerFolderCommand.Execute(null);

        var folder = vm.Doc.Scene.LayerGroups.Single();
        Assert.Empty(FolderTree.SubtreeLayers(vm.Doc.Scene, folder));
        Assert.Equal("[Folder 1] b a Background", Docker(vm));
        Assert.Equal(folder.Id, vm.SelectedGroup?.Id);
        Assert.True(Header(vm, "Folder 1").IsSelected);
    }

    [AvaloniaFact]
    public void ANewFolderWithALayerInAFolderSelectedGoesInsideThatFolder_AndTheOldFolderKeepsItsLayer()
    {
        var vm = Vm();
        vm.GroupLayersCommand.Execute(null); // "b" into Folder 1
        vm.SelectLayer(Row(vm, "b"), toggle: false, range: false);

        vm.CreateLayerFolderCommand.Execute(null);

        Assert.Equal("[Folder 1] .[Folder 2] .b a Background", Docker(vm));
        Assert.Equal("Folder 1", FolderTree.Folder(vm.Doc.Scene, Row(vm, "b").Layer.GroupId)?.Name);
    }

    [AvaloniaFact]
    public void WithNothingPickedTheFolderGoesOnTop()
    {
        var vm = VmLayers.BareVm();
        vm.ActiveLayerIndex = -1;

        vm.CreateLayerFolderCommand.Execute(null);

        Assert.StartsWith("[Folder 1]", Docker(vm));
    }

    [AvaloniaFact]
    public void AnEmptyFolderCanBePicked_FilledByANewLayer_AndDeletedWithTheDeleteKeysCommand()
    {
        var vm = Vm();
        vm.CreateLayerFolderCommand.Execute(null);
        vm.SelectLayer(Row(vm, "a"), toggle: false, range: false);
        Assert.Null(vm.SelectedGroup);

        vm.SelectGroup(Header(vm, "Folder 1"), toggle: false, range: false);
        Assert.Equal("Folder 1", vm.SelectedGroup?.Name);
        Assert.Equal(0, vm.SelectedLayerCount);

        vm.AddPaintedLayerCommand.Execute(null);
        Assert.Equal("[Folder 1] .Paint 4 b a Background", Docker(vm));

        vm.SelectGroup(Header(vm, "Folder 1"), toggle: false, range: false);
        vm.DeleteActiveLayerCommand.Execute(null);
        Assert.Equal("b a Background", Docker(vm));
        Assert.Empty(vm.Doc.Scene.LayerGroups);

        vm.UndoCommand.Execute(null);
        Assert.Equal("[Folder 1] .Paint 4 b a Background", Docker(vm));
    }

    /// <summary>
    /// A lock on an outer folder protects what is inside it from a delete of an
    /// inner folder too (sensitivity-guardian, W4) — and Copy with an empty
    /// folder picked copies that folder, never the layer behind it.
    /// </summary>
    [AvaloniaFact]
    public void AnInnerFolderUnderALockedOneIsNotDeleted_AndAnEmptyPickCopiesTheFolder()
    {
        var vm = Vm();
        vm.GroupLayersCommand.Execute(null);                 // Folder 1 holds "b"
        vm.SelectLayer(Row(vm, "b"), toggle: false, range: false);
        vm.GroupLayersCommand.Execute(null);                 // Folder 2 inside it, holding "b"
        Assert.Equal("[Folder 1] .[Folder 2] ..b a Background", Docker(vm));
        Header(vm, "Folder 1").Locked = true;

        vm.SelectGroup(Header(vm, "Folder 2"), toggle: false, range: false);
        vm.DeleteActiveLayerCommand.Execute(null);
        Assert.Equal("[Folder 1] .[Folder 2] ..b a Background", Docker(vm));
        Assert.Contains("Folder 1", vm.AiStatus);

        Header(vm, "Folder 1").Locked = false;
        vm.SelectLayer(Row(vm, "a"), toggle: false, range: false);
        vm.CreateLayerFolderCommand.Execute(null);           // an empty "Folder 3", picked
        Assert.True(vm.CopyLayers());
        Assert.Equal("Folder “Folder 3” copied.", vm.AiStatus);
    }

    /// <summary>
    /// A picked folder copies with everything inside it — layers, a folder in
    /// it, an empty folder in its place — and pastes as a new, independent
    /// folder where a new layer would go. Twice is two copies; undo takes one.
    /// </summary>
    [AvaloniaFact]
    public void AFolderCopiesWholeAndPastesAsANewFolder()
    {
        var vm = Vm();
        vm.SelectLayer(Row(vm, "a"), toggle: false, range: false);
        vm.SelectLayer(Row(vm, "b"), toggle: true, range: false);
        vm.GroupLayersCommand.Execute(null);                 // Folder 1: b, a
        vm.SelectLayer(Row(vm, "a"), toggle: false, range: false);
        vm.GroupLayersCommand.Execute(null);                 // Folder 2 inside, around a
        vm.SelectLayer(Row(vm, "b"), toggle: false, range: false);
        vm.CreateLayerFolderCommand.Execute(null);           // an empty Folder 3 above b
        Assert.Equal("[Folder 1] .[Folder 3] .b .[Folder 2] ..a Background", Docker(vm));
        var layersBefore = vm.Doc.Scene.Layers.Count;

        vm.SelectGroup(Header(vm, "Folder 1"), toggle: false, range: false);
        Assert.True(vm.CopyLayers());
        vm.SelectLayer(Row(vm, "Background"), toggle: false, range: false);
        Assert.True(vm.PasteLayers());

        Assert.Equal(
            "[Folder 1] .[Folder 3] .b .[Folder 2] ..a [Folder 1 copy] .[Folder 3] .b copy .[Folder 2] ..a copy Background",
            Docker(vm));
        Assert.Equal("Folder 1 copy", vm.SelectedGroup?.Name);
        var ids = vm.Doc.Scene.LayerGroups.Select(g => g.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(layersBefore + 2, vm.Doc.Scene.Layers.Count);

        vm.PasteLayers();
        Assert.Contains("[Folder 1 copy 2]", Docker(vm));

        vm.UndoCommand.Execute(null);
        Assert.DoesNotContain("[Folder 1 copy 2]", Docker(vm));
        Assert.Contains("[Folder 1 copy]", Docker(vm));
    }

    [AvaloniaFact]
    public void GroupLayersWrapsTheSelection_AndIsCtrlGInTheShortcutRegistry()
    {
        var vm = Vm();
        vm.SelectLayer(Row(vm, "a"), toggle: false, range: false);
        vm.SelectLayer(Row(vm, "b"), toggle: true, range: false);

        vm.GroupLayersCommand.Execute(null);

        Assert.Equal("[Folder 1] .b .a Background", Docker(vm));
        var entry = new ShortcutMap().Definitions.Single(s => s.Id == "docker.groupLayers");
        Assert.Equal(Key.G, entry.Default?.Key);
        Assert.Equal(KeyModifiers.Control, entry.Default?.KeyModifiers);
    }

    [AvaloniaFact]
    public void AnEmptyFolderIsDraggedLikeALayer_AndAFolderIntoAFolderNests()
    {
        var vm = Vm();
        vm.CreateLayerFolderCommand.Execute(null);          // Folder 1, on top
        vm.SelectLayer(Row(vm, "a"), toggle: false, range: false);
        vm.CreateLayerFolderCommand.Execute(null);          // Folder 2, above "a"
        Assert.Equal("[Folder 1] b [Folder 2] a Background", Docker(vm));

        var two = Header(vm, "Folder 2");
        var hint = vm.LayerDropHintFor(two, Row(vm, "b"), 0.1);
        Assert.Equal(LayerDropHint.Above, hint);
        vm.DropOnLayerPanel(two, Row(vm, "b"), hint);
        Assert.Equal("[Folder 1] [Folder 2] b a Background", Docker(vm));

        vm.DropOnLayerPanel(Header(vm, "Folder 2"), Header(vm, "Folder 1"), LayerDropHint.Into);
        Assert.Equal("[Folder 1] .[Folder 2] b a Background", Docker(vm));

        vm.DropOnLayerPanel(Row(vm, "b"), Header(vm, "Folder 2"), LayerDropHint.Into);
        Assert.Equal("[Folder 1] .[Folder 2] ..b a Background", Docker(vm));
    }
}

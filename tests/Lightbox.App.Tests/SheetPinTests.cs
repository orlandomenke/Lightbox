using Avalonia.Headless.XUnit;
using Lightbox.App.Controls;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Pinning rows to the Timeline and the X-sheet (Q227): pin layers and
/// folders, and <em>Pinned only</em> shows just those, the layer being drawn
/// on, and the folders they sit in — on both surfaces.
/// </summary>
[Collection("BrushState")]
public class SheetPinTests : BrushStateIsolated
{
    private static LayerRow Row(MainViewModel vm, string name) =>
        vm.LayerRows.Single(r => r.Layer.Name == name);

    private static SheetFolderRow Folder(MainViewModel vm, string name) =>
        vm.SheetRows.OfType<SheetFolderRow>().Single(f => f.Name == name);

    private static string Sheet(MainViewModel vm) => string.Join(" ", vm.SheetRows.Select(r => r switch
    {
        SheetFolderRow f => $"[{f.Name}]",
        LayerRow l => l.Name,
        _ => "?",
    }));

    private static string Tracks(MainViewModel vm) => string.Join(" ", vm.TimelineTracks.Select(t =>
        t.Kind == TrackKind.Folder ? $"[{t.Name.Trim()}]" : t.Name.Trim()));

    private static void Activate(MainViewModel vm, string name) =>
        vm.ActiveLayerIndex = Row(vm, name).SceneIndex;

    /// <summary>Top to bottom: f, [Outer: [Inner: e, d], c, b], a, paper. The active layer is a.</summary>
    private static MainViewModel Nested()
    {
        var vm = VmLayers.PaperVm();
        while (vm.Doc.Scene.Layers.Count < 7) vm.AddPaintedLayerCommand.Execute(null);
        string[] names = ["a", "b", "c", "d", "e", "f"];
        for (var i = 1; i < 7; i++) vm.LayerRows.Single(r => r.SceneIndex == i).Name = names[i - 1];
        vm.SelectLayer(Row(vm, "b"), toggle: false, range: false);
        vm.SelectLayer(Row(vm, "c"), toggle: true, range: false);
        vm.GroupLayersCommand.Execute(null);
        vm.SelectLayer(Row(vm, "d"), toggle: false, range: false);
        vm.SelectLayer(Row(vm, "e"), toggle: true, range: false);
        vm.GroupLayersCommand.Execute(null);
        var headers = vm.LayerPanelItems.OfType<GroupRow>().ToList();
        var inner = headers.Single(h => h.Group.Id == Row(vm, "d").Layer.GroupId);
        var outer = headers.Single(h => h != inner);
        outer.Name = "Outer";
        inner.Name = "Inner";
        vm.DropOnLayerPanel(inner, outer, LayerDropHint.Into);
        vm.SelectLayer(Row(vm, "a"), toggle: false, range: false);
        Activate(vm, "a");
        vm.ClearCelRange();
        return vm;
    }

    [AvaloniaFact]
    public void PinnedOnlyShowsThePinnedRowsAndTheActiveLayer_OnBothSurfaces()
    {
        var vm = Nested();
        var everything = Sheet(vm);
        Row(vm, "f").SheetPinned = true;
        Row(vm, "d").SheetPinned = true;
        Assert.Equal(everything, Sheet(vm)); // pins alone change nothing

        vm.SheetPinnedOnly = true;

        Assert.Equal("f [Outer] [Inner] d a", Sheet(vm));
        Assert.Equal(Sheet(vm), Tracks(vm));

        vm.SheetPinnedOnly = false;
        Assert.Equal(everything, Sheet(vm));
    }

    [AvaloniaFact]
    public void TheActiveLayersRowFollowsTheActiveLayer()
    {
        var vm = Nested();
        Row(vm, "f").SheetPinned = true;
        vm.SheetPinnedOnly = true;
        Assert.Equal("f a", Sheet(vm));

        Activate(vm, "c");

        // c comes in with the folder it is in; a, no longer drawn on and not pinned, goes.
        Assert.Equal("f [Outer] c", Sheet(vm));
    }

    [AvaloniaFact]
    public void PinningAFolderFromItsSheetRowBringsEverythingInside()
    {
        var vm = Nested();
        Activate(vm, "f");

        Folder(vm, "Inner").Pinned = true;
        vm.SheetPinnedOnly = true;

        Assert.Equal("f [Outer] [Inner] e d", Sheet(vm));
        Assert.True(vm.Doc.Scene.LayerGroups.Single(g => g.Name == "Inner").SheetPinned);
    }

    /// <summary>The Layers docker's header pins the same folder: one fact, two places to set it.</summary>
    [AvaloniaFact]
    public void TheLayersDockersFolderHeaderPinsTheSameFolder()
    {
        var vm = Nested();

        vm.LayerPanelItems.OfType<GroupRow>().Single(h => h.Name == "Outer").SheetPinned = true;

        Assert.True(Folder(vm, "Outer").Pinned);
    }

    /// <summary>How the sheet is looked at, not an edit: nothing for Ctrl+Z to find.</summary>
    [AvaloniaFact]
    public void PinningAndTheSwitchAreNotUndoSteps()
    {
        var vm = Nested();
        var depth = vm.UndoDepth;

        Row(vm, "f").SheetPinned = true;
        Folder(vm, "Outer").Pinned = true;
        vm.SheetPinnedOnly = true;

        Assert.Equal(depth, vm.UndoDepth);
    }

    [AvaloniaFact]
    public void RowsThatLeaveTheSheetLeaveTheSelection()
    {
        var vm = Nested();
        vm.ToggleCelSelection(Row(vm, "b").Cells[0]);
        vm.ToggleCelSelection(Row(vm, "f").Cells[0]);
        Row(vm, "f").SheetPinned = true;
        var b = Row(vm, "b").SceneIndex;
        var f = Row(vm, "f").SceneIndex;

        vm.SheetPinnedOnly = true;

        Assert.DoesNotContain((b, 0), vm.CelSelection);
        Assert.Contains((f, 0), vm.CelSelection);
    }

    [AvaloniaFact]
    public void ThePinsSurviveAnEditThatRebuildsTheRows()
    {
        var vm = Nested();
        Row(vm, "f").SheetPinned = true;
        vm.SheetPinnedOnly = true;

        vm.AddFrameCommand.Execute(null);

        Assert.True(vm.SheetPinnedOnly);
        Assert.True(Row(vm, "f").SheetPinned);
        Assert.Equal("f a", Sheet(vm));
    }

    /// <summary>Both verbs can be given a key: a command outside the registry cannot be seen, searched or bound.</summary>
    [Fact]
    public void BothVerbsAreInTheShortcutRegistry()
    {
        var ids = new ShortcutMap().Definitions.Select(c => c.Id).ToHashSet();

        Assert.Contains("timeline.pinnedOnly", ids);
        Assert.Contains("timeline.pinLayer", ids);
    }
}

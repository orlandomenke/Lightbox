using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Folders inside folders, and the four things around them that were wrong.
/// </summary>
/// <remarks>
/// <para>
/// The report was "we have layers and layer grouping, but the logic surrounding
/// it is off", and it came with a list. Measured before anything was changed,
/// on the stack <see cref="Stacked"/> builds:
/// </para>
/// <list type="bullet">
/// <item>a new layer made while a layer <em>inside</em> a folder was active
/// landed at the top of the whole stack, ungrouped — it ignored the folder and
/// the active layer both;</item>
/// <item>a folder header could not be pointed at at all, so "make a layer in
/// this folder" had no gesture;</item>
/// <item>dragging the last layer out of a folder left the folder in the
/// document and out of the docker, unreachable forever;</item>
/// <item>four folder operations went through <c>Perform</c> without
/// <c>frameContentUnchanged</c>, so filing a layer away threw out every cached
/// frame render and re-rendered every thumbnail.</item>
/// </list>
/// <para>
/// The rules themselves are tested in <c>Lightbox.Core.Tests.LayerTreeTests</c>,
/// against the lists, where a failure points at the rule. These are here for
/// what only the view model can answer: which command lands where, what the
/// docker ends up showing, and what an edit costs.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class NestedFolderTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>Bottom to top: paper, loose, [folder: two layers], loose.</summary>
    private static MainViewModel Stacked()
    {
        var vm = VmLayers.PaperVm();
        while (vm.Doc.Scene.Layers.Count < 5) vm.AddPaintedLayerCommand.Execute(null);
        var layers = vm.Doc.Scene.Layers;
        layers[1].Name = "under";
        layers[2].Name = "in a";
        layers[3].Name = "in b";
        layers[4].Name = "over";

        // Through the app's own paths rather than by writing GroupId directly:
        // those are what rebuild the panel, and a test that reaches past them is
        // testing a docker nobody will ever see.
        vm.ActiveLayerIndex = 2;
        vm.CreateLayerFolderCommand.Execute(null);
        vm.MoveLayerIntoGroup(layers.Single(l => l.Name == "in b"), vm.Doc.Scene.LayerGroups[0]);
        return vm;
    }

    /// <summary>What the docker shows, topmost first, folders as indented headers.</summary>
    private static string Panel(MainViewModel vm) =>
        string.Join(
            " | ",
            vm.LayerPanelItems.Select(i => i switch
            {
                GroupRow g => new string(' ', g.Depth * 2) + $"<{g.Name}>",
                LayerRow r => new string(' ', r.Depth * 2) + r.Name,
                _ => "?",
            }));

    private static LayerRow Row(MainViewModel vm, string name) =>
        vm.LayerPanelItems.OfType<LayerRow>().Single(r => r.Layer.Name == name);

    private static GroupRow Header(MainViewModel vm, string name) =>
        vm.LayerPanelItems.OfType<GroupRow>().Single(g => g.Name == name);

    // ---- a new layer knows where it is ------------------------------------------

    /// <summary>
    /// The first thing on the list. A new layer used to go to the very top of the
    /// stack with no folder, whatever was active.
    /// </summary>
    [AvaloniaFact]
    public void ANewLayerMadeInsideAFolderStaysInThatFolder()
    {
        var vm = Stacked();
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.FindIndex(l => l.Name == "in a");
        vm.AddPaintedLayerCommand.Execute(null);

        var made = vm.Doc.Scene.Layers[vm.ActiveLayerIndex];
        output.WriteLine(Panel(vm));
        Assert.Equal(vm.Doc.Scene.LayerGroups[0].Id, made.GroupId);
        // Directly above the layer it was made from, not at the top of the folder.
        Assert.Equal("in a", vm.Doc.Scene.Layers[vm.ActiveLayerIndex - 1].Name);
    }

    [AvaloniaFact]
    public void ANewLayerMadeOnALooseLayerStaysLoose()
    {
        var vm = Stacked();
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.FindIndex(l => l.Name == "under");
        vm.AddPaintedLayerCommand.Execute(null);
        Assert.Null(vm.Doc.Scene.Layers[vm.ActiveLayerIndex].GroupId);
        Assert.Equal("under", vm.Doc.Scene.Layers[vm.ActiveLayerIndex - 1].Name);
    }

    /// <summary>
    /// Pointing at a folder is the second thing on the list, and it is a
    /// different question from which layer is active — so it must not move the
    /// active layer.
    /// </summary>
    [AvaloniaFact]
    public void PointingAtAFolderSendsTheNextLayerIntoItWithoutMovingTheBrush()
    {
        var vm = Stacked();
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.FindIndex(l => l.Name == "under");
        var paintingOn = vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Id;

        vm.FocusLayerGroupCommand.Execute(Header(vm, "Folder 1"));
        Assert.Equal(vm.Doc.Scene.LayerGroups[0].Id, vm.FocusedGroupId);
        // Pointing at it changed nothing about where a stroke would land.
        Assert.Equal(paintingOn, vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Id);

        vm.AddPaintedLayerCommand.Execute(null);
        Assert.Equal(vm.Doc.Scene.LayerGroups[0].Id, vm.Doc.Scene.Layers[vm.ActiveLayerIndex].GroupId);
    }

    [AvaloniaFact]
    public void PointingAtTheSameFolderAgainLetsGoOfIt()
    {
        var vm = Stacked();
        var header = Header(vm, "Folder 1");
        vm.FocusLayerGroupCommand.Execute(header);
        vm.FocusLayerGroupCommand.Execute(header);
        Assert.Null(vm.FocusedGroupId);
    }

    /// <summary>A folder made while pointing at a folder is a folder inside it.</summary>
    [AvaloniaFact]
    public void AnEmptyFolderMadeWhilePointingAtAFolderIsNestedInIt()
    {
        var vm = Stacked();
        vm.FocusLayerGroupCommand.Execute(Header(vm, "Folder 1"));
        vm.CreateEmptyLayerFolderCommand.Execute(null);

        var outer = vm.Doc.Scene.LayerGroups.Single(g => g.Name == "Folder 1");
        var inner = vm.Doc.Scene.LayerGroups.Single(g => g.Name == "Folder 2");
        Assert.Equal(outer.Id, inner.ParentId);
        output.WriteLine(Panel(vm));
        // The empty folder sits at the top of the folder that contains it,
        // which is where LayerTree.Walk puts a folder with nothing to be
        // ordered by — and where you can see the thing you just made.
        Assert.Equal("over | <Folder 1> |   <Folder 2> |   in b |   in a | under | Background", Panel(vm));
    }

    // ---- an empty folder is a real folder ---------------------------------------

    /// <summary>
    /// Third on the list. There was no way to make an empty folder — New Folder
    /// always swallowed the active layer — so "make a folder, then drag things
    /// into it" could not be started.
    /// </summary>
    [AvaloniaFact]
    public void AnEmptyFolderCanBeMadeAndIsShown()
    {
        var vm = Stacked();
        vm.CreateEmptyLayerFolderCommand.Execute(null);
        var header = Header(vm, "Folder 2");
        Assert.True(header.IsEmpty);
        Assert.Contains(header, vm.LayerPanelItems);
        Assert.Equal(5, vm.Doc.Scene.Layers.Count); // it took nothing with it
    }

    /// <summary>
    /// And it stays shown once its last layer leaves, which is the half that was
    /// silently wrong: the folder survived in the file and vanished from the
    /// docker, so it could never be refilled, renamed or deleted again.
    /// </summary>
    [AvaloniaFact]
    public void AFolderEmptiedByDraggingStaysInTheDocker()
    {
        var vm = Stacked();
        foreach (var name in new[] { "in a", "in b" })
        {
            vm.RemoveLayerFromGroupCommand.Execute(Row(vm, name));
        }
        output.WriteLine(Panel(vm));
        Assert.Single(vm.Doc.Scene.LayerGroups);
        var header = Header(vm, "Folder 1");
        Assert.True(header.IsEmpty);

        // And it can be filled again, which is the point of keeping it.
        vm.MoveLayerIntoGroup(vm.Doc.Scene.Layers.Single(l => l.Name == "over"), header.Group);
        Assert.False(Header(vm, "Folder 1").IsEmpty);
    }

    // ---- nesting -----------------------------------------------------------------

    [AvaloniaFact]
    public void AFolderDroppedOnAnotherFolderGoesInsideIt()
    {
        var vm = Stacked();
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.FindIndex(l => l.Name == "over");
        vm.CreateLayerFolderCommand.Execute(null);

        var inner = vm.Doc.Scene.LayerGroups.Single(g => g.Name == "Folder 2");
        var outer = vm.Doc.Scene.LayerGroups.Single(g => g.Name == "Folder 1");
        vm.DropGroupIntoGroup(inner, outer);

        output.WriteLine(Panel(vm));
        Assert.Equal(outer.Id, vm.Doc.Scene.LayerGroups.Single(g => g.Name == "Folder 2").ParentId);
        Assert.Equal("<Folder 1> |   <Folder 2> |     over |   in b |   in a | under | Background", Panel(vm));
    }

    /// <summary>Nesting is one undo step, and it comes back.</summary>
    [AvaloniaFact]
    public void NestingAFolderIsOneStepAndUndoPutsItBack()
    {
        var vm = Stacked();
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.FindIndex(l => l.Name == "over");
        vm.CreateLayerFolderCommand.Execute(null);
        var before = Panel(vm);

        vm.DropGroupIntoGroup(
            vm.Doc.Scene.LayerGroups.Single(g => g.Name == "Folder 2"),
            vm.Doc.Scene.LayerGroups.Single(g => g.Name == "Folder 1"));
        Assert.NotEqual(before, Panel(vm));

        vm.UndoCommand.Execute(null);
        Assert.Equal(before, Panel(vm));
    }

    /// <summary>
    /// The refusal that matters: filing a folder into its own child would cut
    /// both out of the tree. It has to decline without leaving an undo step
    /// behind, or Ctrl+Z visibly does nothing and the artist presses it again.
    /// </summary>
    [AvaloniaFact]
    public void AFolderRefusedEntryLeavesNoUndoStep()
    {
        var vm = Stacked();
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.FindIndex(l => l.Name == "over");
        vm.CreateLayerFolderCommand.Execute(null);
        var outer = vm.Doc.Scene.LayerGroups.Single(g => g.Name == "Folder 1");
        var inner = vm.Doc.Scene.LayerGroups.Single(g => g.Name == "Folder 2");
        vm.DropGroupIntoGroup(inner, outer);

        var steps = vm.UndoMenuHeader;
        var shape = Panel(vm);
        vm.DropGroupIntoGroup(outer, inner); // the parent into its own child
        Assert.Equal(shape, Panel(vm));
        Assert.Equal(steps, vm.UndoMenuHeader);
    }

    /// <summary>Hiding an outer folder hides what is in the folder inside it.</summary>
    [AvaloniaFact]
    public void HidingAnOuterFolderHidesTheNestedLayers()
    {
        var vm = Stacked();
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.FindIndex(l => l.Name == "over");
        vm.CreateLayerFolderCommand.Execute(null);
        vm.DropGroupIntoGroup(
            vm.Doc.Scene.LayerGroups.Single(g => g.Name == "Folder 2"),
            vm.Doc.Scene.LayerGroups.Single(g => g.Name == "Folder 1"));

        var deep = vm.Doc.Scene.Layers.Single(l => l.Name == "over");
        Assert.True(vm.Doc.Scene.IsLayerVisible(deep));
        Header(vm, "Folder 1").Visible = false;
        Assert.False(vm.Doc.Scene.IsLayerVisible(deep));
    }

    // ---- the docker's own rows ----------------------------------------------------

    /// <summary>
    /// A header used to be rebuilt from scratch on every sync, which ended a
    /// rename the moment anything else touched the stack — and pointed a live
    /// drag's hints at rows that were no longer in the panel.
    /// </summary>
    [AvaloniaFact]
    public void AHeaderSurvivesAnUnrelatedEditAndKeepsARenameGoing()
    {
        var vm = Stacked();
        var header = Header(vm, "Folder 1");
        header.IsRenaming = true;

        vm.SetLayerVisible(vm.Doc.Scene.Layers.Single(l => l.Name == "under"), false);

        Assert.Same(header, Header(vm, "Folder 1"));
        Assert.True(header.IsRenaming);
    }

    /// <summary>
    /// The panel is patched rather than cleared and refilled. Measured before
    /// the change: one visibility toggle on a 40-layer stack raised a Reset and
    /// 41 Adds, which makes the <c>ItemsControl</c> re-template every row in the
    /// docker for an edit that moved nothing.
    /// </summary>
    [AvaloniaFact]
    public void AnEditThatMovesNoRowChangesNothingInThePanel()
    {
        var vm = VmLayers.PaperVm();
        while (vm.Doc.Scene.Layers.Count < 40) vm.AddPaintedLayerCommand.Execute(null);
        vm.ActiveLayerIndex = 5;
        vm.CreateLayerFolderCommand.Execute(null);

        var changes = 0;
        vm.LayerPanelItems.CollectionChanged += (_, _) => changes++;
        vm.SetLayerVisible(vm.Doc.Scene.Layers[10], false);
        output.WriteLine($"visibility toggle on 40 layers => {changes} collection changes");
        Assert.Equal(0, changes);
    }

    /// <summary>And one added row is one change, not a whole rebuild.</summary>
    [AvaloniaFact]
    public void AddingOneLayerIsOneChangeToThePanel()
    {
        var vm = VmLayers.PaperVm();
        while (vm.Doc.Scene.Layers.Count < 40) vm.AddPaintedLayerCommand.Execute(null);

        var changes = 0;
        vm.LayerPanelItems.CollectionChanged += (_, _) => changes++;
        vm.AddPaintedLayerCommand.Execute(null);
        output.WriteLine($"one new layer => {changes} collection changes");
        Assert.Equal(1, changes);
        Assert.Equal(41, vm.LayerPanelItems.Count);
    }

    /// <summary>
    /// Filing a layer into a folder moves no pixels, so it must not throw out
    /// the cached frame renders. Four folder operations did.
    /// </summary>
    [AvaloniaFact]
    public void FolderOperationsDeclareThatTheyChangeNoDrawing()
    {
        var vm = Stacked();
        var folder = vm.Doc.Scene.LayerGroups[0];
        foreach (var (name, act) in new (string, Action)[]
        {
            ("into a folder", () => vm.MoveLayerIntoGroup(
                vm.Doc.Scene.Layers.Single(l => l.Name == "over"), folder)),
            ("out of a folder", () => vm.RemoveLayerFromGroupCommand.Execute(Row(vm, "in a"))),
            ("new empty folder", () => vm.CreateEmptyLayerFolderCommand.Execute(null)),
            ("dissolve", () => vm.DissolveGroupCommand.Execute(Header(vm, "Folder 1"))),
        })
        {
            vm.ResetFrameRenderInvalidations();
            act();
            output.WriteLine($"{name}: {vm.FrameRenderInvalidations} full invalidation(s)");
            Assert.Equal(0, vm.FrameRenderInvalidations);
        }
    }

    /// <summary>
    /// The ▲/▼ buttons step one row of the docker and enter the folder they step
    /// into. Swapping two entries of the layer list instead — which is what they
    /// did — left the layer visibly inside a folder it was not in, and split
    /// that folder across the compositing order.
    /// </summary>
    [AvaloniaFact]
    public void MovingALayerUpIntoAFolderPutsItInTheFolder()
    {
        var vm = Stacked();
        var loose = Row(vm, "under");
        vm.MoveLayer(loose, +1); // the row above "under" is the folder header

        output.WriteLine(Panel(vm));
        Assert.Equal(vm.Doc.Scene.LayerGroups[0].Id, vm.Doc.Scene.Layers.Single(l => l.Name == "under").GroupId);
        EveryFolderIsOneRun(vm);
    }

    [AvaloniaFact]
    public void MovingALayerOffTheTopOfAFolderTakesItOut()
    {
        var vm = Stacked();
        vm.MoveLayer(Row(vm, "in b"), +1); // "in b" is the top of the folder
        output.WriteLine(Panel(vm));
        Assert.Null(vm.Doc.Scene.Layers.Single(l => l.Name == "in b").GroupId);
        EveryFolderIsOneRun(vm);
    }

    /// <summary>
    /// The invariant the docker depends on: a folder is one unbroken run of
    /// <c>Scene.Layers</c>, so what composites is what the panel draws.
    /// </summary>
    private static void EveryFolderIsOneRun(MainViewModel vm)
    {
        var layers = vm.Doc.Scene.Layers;
        var byId = vm.Doc.Scene.GroupMap;
        foreach (var group in vm.Doc.Scene.LayerGroups)
        {
            var inside = LayerTree.SubtreeGroupIds(group, byId);
            var hits = Enumerable.Range(0, layers.Count)
                .Where(i => layers[i].GroupId is { } id && inside.Contains(id))
                .ToList();
            if (hits.Count == 0) continue;
            Assert.Equal(Enumerable.Range(hits[0], hits.Count).ToList(), hits);
        }
    }
}

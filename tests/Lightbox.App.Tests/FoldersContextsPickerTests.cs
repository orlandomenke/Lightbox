using Lightbox.App.Docking;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

public class ContextShortcutTests
{
    [Fact]
    public void SameKey_MeansDifferentThings_PerContext()
    {
        var map = new ShortcutMap();
        var e = new KeyEventArgs { Key = Key.I, KeyModifiers = KeyModifiers.None };

        Assert.Equal("canvas.pickColor", map.IdFor(e, ShortcutScope.Canvas));
        Assert.Equal("timeline.insertKey", map.IdFor(e, ShortcutScope.In(DockPanelId.Timeline)));
        // The picker is general, so a docker with no I of its own gets it. That
        // is the asked-for behaviour: I inserts a key over the timeline and
        // reaches for the eyedropper everywhere else.
        Assert.Equal("canvas.pickColor", map.IdFor(e, ShortcutScope.In(DockPanelId.Layers)));
    }

    [Fact]
    public void GlobalBindings_FireInEveryContext_UnlessShadowed()
    {
        var map = new ShortcutMap();
        var b = new KeyEventArgs { Key = Key.B, KeyModifiers = KeyModifiers.None };
        Assert.Equal("tool.brush", map.IdFor(b, ShortcutScope.Canvas));
        Assert.Equal("tool.brush", map.IdFor(b, ShortcutScope.In(DockPanelId.Timeline)));

        // Delete is a context twin rather than a docker-only binding. It was the
        // latter until the Arrow tool gained something to delete; this assertion
        // used to read `Assert.Null(... Canvas)` and was correct at the time.
        // Both halves are asserted so neither can quietly take the other's key.
        //
        // B173 moved the canvas half from `lines.delete` to `select.clear`,
        // which asks whether a region is selected and falls back to the lines
        // when none is. The twin arrangement is unchanged — one id per context,
        // never two on the same key — and the precedence lives in the command
        // rather than in a second binding, which is what keeps this assertion
        // able to say what Delete means from one place.
        var delete = new KeyEventArgs { Key = Key.Delete, KeyModifiers = KeyModifiers.None };
        Assert.Equal("docker.deleteLayer", map.IdFor(delete, ShortcutScope.In(DockPanelId.Layers)));
        Assert.Equal("select.clear", map.IdFor(delete, ShortcutScope.Canvas));
        // And neither is global, or it would fire over the other's area too.
        Assert.Null(map.IdFor(delete, ShortcutScope.In(DockPanelId.Timeline)));

        // Backspace is the same shape: the layer docker blanks a layer, the
        // canvas floods the selection with the background.
        var back = new KeyEventArgs { Key = Key.Back, KeyModifiers = KeyModifiers.None };
        Assert.Equal("docker.clearLayer", map.IdFor(back, ShortcutScope.In(DockPanelId.Layers)));
        Assert.Equal("select.fillBackground", map.IdFor(back, ShortcutScope.Canvas));
        Assert.Null(map.IdFor(back, ShortcutScope.In(DockPanelId.Timeline)));
    }

    [Fact]
    public void Conflicts_OnlyCountWhenContextsOverlap()
    {
        var map = new ShortcutMap();
        // The general picker and the timeline's insert-key coexist on I: the
        // resolver takes the more specific, so there is nothing to resolve.
        Assert.Null(map.ConflictWith("canvas.pickColor", new KeyGesture(Key.I)));
        // A SECOND general command taking I is the tie nothing can break.
        Assert.Equal("canvas.pickColor", map.ConflictWith("tool.brush", new KeyGesture(Key.I))?.Id);
    }
}

/// <remarks>
/// In the <c>BrushState</c> collection because it sets brush parameters, and
/// those live in a process-wide store — running beside a test that assumes
/// defaults hands it this one’s brush.
/// </remarks>
[Collection("BrushState")]
public class PickerToolTests : BrushStateIsolated
{
    [AvaloniaFact]
    public void PickColorAt_ReadsTheCompositedColor_AndPaperWhenEmpty()
    {
        var vm = new MainViewModel(null)
        {
            SmoothStrokes = false,
            ColorHex = "#e04040",
            BrushSize = 24,
            BrushHardness = 1,
            BrushOpacity = 1,
            BrushFlow = 1,
            BrushGranulation = 0,
            BrushWetEdge = 0,
        };
        vm.BeginStroke(200, 200, 1);
        vm.EndStroke();

        vm.ColorHex = "#123456";                 // change away, then pick it back
        vm.PickColorAt(200, 200);
        Assert.Equal("#e04040", vm.ColorHex);

        vm.PickColorAt(700, 400);                // empty area → paper color
        Assert.Equal(vm.Doc.Scene.BackgroundColor, vm.ColorHex);
    }

    [AvaloniaFact]
    public void InsertKeyframeAtPlayhead_KeysTheActiveCel()
    {
        var vm = new MainViewModel(null);
        vm.AddFrameCommand.Execute(null);
        vm.ClearCelAt(vm.LayerRows[0].Cells.First(c => c.Index == 1)); // make cel 1 a hold
        Assert.Null(vm.PaintLayer().Cels[1].Frame);

        vm.CurrentFrameIndex = 1;
        vm.InsertKeyframeAtPlayhead();
        Assert.NotNull(vm.PaintLayer().Cels[1].Frame);
    }
}

public class NudgeSelectionTests
{
    [AvaloniaFact]
    public void Nudge_ShiftsEveryContourPoint_ByWholePixels()
    {
        var vm = new MainViewModel(null);
        vm.SelectAllCommand.Execute(null);
        var before = vm.SelectionContours[0][0];

        vm.NudgeSelection(3, -2);
        var after = vm.SelectionContours[0][0];
        Assert.Equal(before.X + 3, after.X);
        Assert.Equal(before.Y - 2, after.Y);
    }

    [AvaloniaFact]
    public void Nudge_WithoutASelection_IsANoOp()
    {
        var vm = new MainViewModel(null);
        vm.NudgeSelection(1, 0); // must not throw or create a selection
        Assert.False(vm.HasSelection);
    }
}

public class LayerFolderTests
{
    /// <summary>
    /// Two ordinary drawing layers and no paper. Folder grouping is about
    /// contiguity and indices; a locked Background at index 0 would shift
    /// every number here without changing what is being tested.
    /// </summary>
    private static MainViewModel VmWithTwoLayers()
    {
        var vm = VmLayers.BareVm();
        vm.AddPaintedLayerCommand.Execute(null); // layer 1 active (top)
        return vm;
    }

    [AvaloniaFact]
    public void CreateFolder_GroupsTheActiveLayer_AndShowsAHeaderRow()
    {
        var vm = VmWithTwoLayers();
        vm.CreateLayerFolderCommand.Execute(null);

        var group = Assert.Single(vm.Doc.Scene.LayerGroups);
        Assert.Equal(group.Id, vm.Doc.Scene.Layers[1].GroupId);
        Assert.Contains(vm.LayerPanelItems, i => i is GroupRow g && g.Group.Id == group.Id);

        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.Doc.Scene.LayerGroups);
        Assert.Null(vm.Doc.Scene.Layers[1].GroupId);
    }

    [AvaloniaFact]
    public void FolderVisibility_GatesItsMembers_InCompositingAndPainting()
    {
        var vm = VmWithTwoLayers();
        vm.CreateLayerFolderCommand.Execute(null);
        var header = vm.LayerPanelItems.OfType<GroupRow>().Single();

        header.Visible = false;
        var layer = vm.Doc.Scene.Layers[1];
        Assert.True(layer.Visible);                              // the layer's own flag is untouched
        Assert.False(vm.Doc.Scene.IsLayerVisible(layer));        // but compositing sees it hidden

        // painting on a folder-hidden layer is blocked like a hidden layer
        vm.BeginStroke(50, 50, 1);
        vm.EndStroke();
        Assert.Empty(((Frame)layer.Cels[0].Frame!).Strokes);
        Assert.Contains("hidden", vm.AiStatus);
    }

    [AvaloniaFact]
    public void Collapse_HidesMemberRows_FromTheDockerPanelOnly()
    {
        var vm = VmWithTwoLayers();
        vm.CreateLayerFolderCommand.Execute(null);
        Assert.Equal(3, vm.LayerPanelItems.Count); // header + grouped layer + ungrouped layer

        var header = vm.LayerPanelItems.OfType<GroupRow>().Single();
        header.Collapsed = true;
        Assert.Equal(2, vm.LayerPanelItems.Count); // member row hidden
        Assert.Equal(2, vm.LayerRows.Count);        // the timeline still shows every layer
    }

    [AvaloniaFact]
    public void AddAndRemove_KeepTheFolderContiguous()
    {
        var vm = VmWithTwoLayers();
        vm.CreateLayerFolderCommand.Execute(null);   // top layer grouped
        var header = vm.LayerPanelItems.OfType<GroupRow>().Single();

        vm.ActiveLayerIndex = 0;                     // the bottom, ungrouped layer
        vm.AddActiveLayerToGroupCommand.Execute(header);
        Assert.All(vm.Doc.Scene.Layers, l => Assert.Equal(header.Group.Id, l.GroupId));

        var row = vm.LayerRows.First(r => r.SceneIndex == vm.ActiveLayerIndex);
        vm.RemoveLayerFromGroupCommand.Execute(row);
        Assert.Null(row.Layer.GroupId);
        Assert.Single(vm.Doc.Scene.Layers, l => l.GroupId == header.Group.Id);
    }

    [AvaloniaFact]
    public void FolderColor_IsUndoable_AndSerializes()
    {
        var vm = VmWithTwoLayers();
        vm.CreateLayerFolderCommand.Execute(null);
        var header = vm.LayerPanelItems.OfType<GroupRow>().Single();

        header.Color = "#c25050";
        Assert.Equal("#c25050", vm.Doc.Scene.LayerGroups[0].Color);

        var restored = Lightbox.Core.Serialization.DocJson.Deserialize(
            Lightbox.Core.Serialization.DocJson.Serialize(vm.Doc));
        Assert.Equal("#c25050", restored.Scene.LayerGroups[0].Color);

        vm.UndoCommand.Execute(null);
        Assert.Equal("#4a6ea9", vm.Doc.Scene.LayerGroups[0].Color); // back to the default
    }

    [AvaloniaFact]
    public void Dissolve_UngroupsEverything_AndFoldersSerialize()
    {
        var vm = VmWithTwoLayers();
        vm.CreateLayerFolderCommand.Execute(null);
        var group = vm.Doc.Scene.LayerGroups[0];
        group.Name = "Character";

        var restored = Lightbox.Core.Serialization.DocJson.Deserialize(
            Lightbox.Core.Serialization.DocJson.Serialize(vm.Doc));
        Assert.Equal("Character", restored.Scene.LayerGroups[0].Name);
        Assert.Equal(group.Id, restored.Scene.Layers[1].GroupId);

        var header = vm.LayerPanelItems.OfType<GroupRow>().Single();
        vm.DissolveGroupCommand.Execute(header);
        Assert.Empty(vm.Doc.Scene.LayerGroups);
        Assert.All(vm.Doc.Scene.Layers, l => Assert.Null(l.GroupId));
    }

    /// <summary>
    /// <b>The gap this redesign closed.</b> A docker could only own a binding
    /// once somebody had added an enum member and a hover flag for it, so eleven
    /// of the twelve could not have one at all. The scope now carries a
    /// <see cref="DockPanelId"/>, which every docker already has.
    /// </summary>
    [Theory]
    [InlineData(DockPanelId.Palette)]
    [InlineData(DockPanelId.Color)]
    [InlineData(DockPanelId.Reference)]
    [InlineData(DockPanelId.GraphEditor)]
    public void ADockerWithNoBindingOfItsOwnStillGetsTheGeneralOne(DockPanelId panel)
    {
        var map = new ShortcutMap();

        // Delete is bound on the canvas (lines.delete) and in the layers docker
        // (docker.deleteLayer), and neither is general — so over any other
        // docker it must fall through to nothing rather than silently borrowing
        // one of them. A scoped binding stays in its scope.
        var del = new KeyEventArgs { Key = Key.Delete, KeyModifiers = KeyModifiers.None };
        Assert.Null(map.IdFor(del, ShortcutScope.In(panel)));

        // A general binding, however, reaches every docker. That is the half of
        // the rule an artist notices: B is the brush wherever they are.
        var b = new KeyEventArgs { Key = Key.B, KeyModifiers = KeyModifiers.None };
        Assert.Equal("tool.brush", map.IdFor(b, ShortcutScope.In(panel)));

        // And the case that named this test: I is general, so a docker with no
        // I of its own gets the eyedropper — which is the behaviour that was
        // asked for, and the reason canvas.pickColor is no longer canvas-scoped.
        var i = new KeyEventArgs { Key = Key.I, KeyModifiers = KeyModifiers.None };
        Assert.Equal("canvas.pickColor", map.IdFor(i, ShortcutScope.In(panel)));
    }

    /// <summary>
    /// Two dockers cannot both be under the pointer, so a key bound in one is
    /// free in the other — which is what makes per-docker bindings worth having
    /// rather than a second global namespace to keep clear.
    /// </summary>
    [Fact]
    public void BindingsInDifferentDockersDoNotConflict()
    {
        var map = new ShortcutMap();
        var i = new KeyGesture(Key.I);

        // timeline.insertKey owns I over the timeline. Asking whether that
        // clashes with a layers-docker command is asking whether the pointer can
        // be in two places.
        Assert.Null(map.ConflictWith("docker.deleteLayer", i));

        // Nor does it clash with the general I: the resolver takes the more
        // specific, so insert-key wins over the timeline and the eyedropper
        // applies everywhere else. That is a resolution, not a collision.
        Assert.Null(map.ConflictWith("timeline.insertKey", i));

        // Two GENERAL commands on one gesture is the tie nothing can break, and
        // that is what a conflict means.
        Assert.Equal("canvas.pickColor", map.ConflictWith("tool.brush", i)?.Id);
    }

    /// <summary>
    /// The resolution the conflict rule leans on, pinned from the artist's
    /// side: the same key, two answers, and which one you get depends only on
    /// where the pointer is.
    /// </summary>
    [Fact]
    public void AScopedBindingShadowsTheGeneralOneInItsOwnAreaAndNowhereElse()
    {
        var map = new ShortcutMap();
        var i = new KeyEventArgs { Key = Key.I, KeyModifiers = KeyModifiers.None };

        Assert.Equal("timeline.insertKey", map.IdFor(i, ShortcutScope.In(DockPanelId.Timeline)));
        Assert.Equal("canvas.pickColor", map.IdFor(i, ShortcutScope.In(DockPanelId.Layers)));
        Assert.Equal("canvas.pickColor", map.IdFor(i, ShortcutScope.Canvas));
    }

    /// <summary>
    /// The canvas scope is everywhere outside a docker — the canvas, the bars,
    /// the rail, the menu — which is what makes a general shortcut work in all
    /// of them.
    /// </summary>
    [Fact]
    public void OutsideEveryDockerTheCanvasBindingApplies()
    {
        var map = new ShortcutMap();
        var i = new KeyEventArgs { Key = Key.I, KeyModifiers = KeyModifiers.None };
        Assert.Equal("canvas.pickColor", map.IdFor(i, ShortcutScope.Canvas));
    }
}

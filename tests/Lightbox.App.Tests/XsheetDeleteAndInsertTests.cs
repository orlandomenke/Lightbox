using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.Docking;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.Tests;

/// <summary>
/// The X-sheet's structural verbs (Q196): <b>Delete</b> (the drawings go, the
/// slots stay), <b>Delete and pull</b> (the cels go and what follows moves
/// back — and a column selection takes the frames out of the scene), and
/// <b>Insert blank frame</b> (a hold goes in at the cel).
/// </summary>
/// <remarks>
/// This file was <c>DeleteColumnTests</c> (Q88/Q108). The column delete it
/// guarded is still here and still covered — every layer loses the cel, undo
/// restores it, a locked layer refuses, the paper does not — but it is reached
/// by selecting the column and choosing Delete and pull, because the owner
/// removed <em>Delete column</em> as a verb of its own.
/// </remarks>
[Collection("BrushState")]
public class XsheetDeleteAndInsertTests : BrushStateIsolated
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is { Length: > 0 } && !File.Exists(Path.Combine(dir, "Lightbox.sln")))
        {
            dir = Path.GetDirectoryName(dir)!;
        }
        return dir;
    }

    private static FrameCell Cell(MainViewModel vm, int sceneLayer, int index) =>
        vm.LayerRows.First(r => r.SceneIndex == sceneLayer).Cells.First(c => c.Index == index);

    /// <summary>Paper, then two drawing layers, <paramref name="frames"/> keyed frames long.</summary>
    private static MainViewModel TwoLayers(int frames)
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        vm.AddPaintedLayerCommand.Execute(null);
        for (var i = 1; i < frames; i++) vm.AddFrameCommand.Execute(null);
        Assert.Equal(3, vm.Doc.Scene.Layers.Count);
        Assert.True(vm.Doc.Scene.Layers[0].IsBackground);
        Assert.Equal(frames, vm.Doc.Scene.FrameCount);
        return vm;
    }

    private static List<List<string?>> Ids(MainViewModel vm) =>
        vm.Doc.Scene.Layers.Select(l => l.Cels.Select(c => c.Frame?.Id).ToList()).ToList();

    /// <summary>Select frames on every drawing layer — a column selection.</summary>
    private static void SelectColumns(MainViewModel vm, params int[] frames)
    {
        for (var layer = 0; layer < vm.Doc.Scene.Layers.Count; layer++)
        {
            if (vm.Doc.Scene.Layers[layer].IsBackground) continue;
            foreach (var f in frames) vm.ToggleCelSelection(Cell(vm, layer, f));
        }
    }

    // ---- Delete and pull: a column selection shortens the scene ----------------

    [AvaloniaFact]
    public void AColumnSelection_TakesTheFramesOutOfEveryLayer_AsOneUndoStep()
    {
        var vm = TwoLayers(4);
        var before = Ids(vm);
        SelectColumns(vm, 1, 2);

        vm.DeleteCelAt(Cell(vm, 1, 1));

        Assert.Equal(2, vm.Doc.Scene.FrameCount);
        // Both drawing layers lost frames 1 and 2 and kept 0 and 3 — a column,
        // not the row pull the same verb does on a partial selection.
        foreach (var layer in new[] { 1, 2 })
        {
            Assert.Equal([before[layer][0], before[layer][3]], vm.Doc.Scene.Layers[layer].Cels.Select(c => c.Frame?.Id));
        }

        vm.UndoCommand.Execute(null);
        Assert.Equal(4, vm.Doc.Scene.FrameCount);
        Assert.Equal(before, Ids(vm));
    }

    [AvaloniaFact]
    public void AColumnDelete_KeepsThePaperOnEveryFrameThatIsLeft()
    {
        // The paper's one drawing lives on frame 0 and every other cel holds it,
        // so a column delete at 0 that removed the paper's cel like any other
        // took the paper out of the whole scene.
        var vm = TwoLayers(3);
        var paper = vm.Doc.Scene.Layers[0];
        var paperId = paper.Cels[0].Frame!.Id;
        SelectColumns(vm, 0);

        vm.DeleteCelAt(Cell(vm, 1, 0));

        Assert.Equal(2, vm.Doc.Scene.FrameCount);
        for (var f = 0; f < vm.Doc.Scene.FrameCount; f++)
        {
            Assert.Equal(paperId, ExposureSheet.ExposedFrame(vm.Doc.Scene.Layers[0], f)?.Id);
        }
    }

    [AvaloniaFact]
    public void OneDrawingLayer_MakesEverySelectionAColumn()
    {
        // With paper and one drawing layer there is nothing else on the sheet to
        // keep in step with, so "remove it and pull the rest back" shortens the
        // scene rather than leaving a hold at the tail.
        var vm = VmLayers.PaperVm();
        vm.AddFrameCommand.Execute(null);
        vm.AddFrameCommand.Execute(null);
        var paint = vm.ActiveLayerIndex;
        var ids = vm.PaintLayer().Cels.Select(c => c.Frame?.Id).ToList();

        vm.DeleteCelAt(Cell(vm, paint, 1)); // nothing selected: the cel alone

        Assert.Equal(2, vm.Doc.Scene.FrameCount);
        Assert.Equal([ids[0], ids[2]], vm.PaintLayer().Cels.Select(c => c.Frame?.Id));
    }

    [AvaloniaFact]
    public void ALockedLayerRefusesTheWholeColumn()
    {
        // Deleting the frame from four layers and not the fifth would slide
        // those four out of step with it — worse than not deleting at all, and
        // the reason this refuses rather than skipping the locked one.
        var vm = TwoLayers(3);
        var before = Ids(vm);
        SelectColumns(vm, 1);
        vm.SetLayerLocked(vm.Doc.Scene.Layers[2], true);

        vm.DeleteCelAt(Cell(vm, 1, 1));

        Assert.Equal(3, vm.Doc.Scene.FrameCount);
        Assert.Equal(before, Ids(vm));
        Assert.Contains("locked", vm.AiStatus, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void ALayerInALockedFolderRefusesTheWholeColumn()
    {
        // The layer's own flag is clear; its folder's is not. A row edit
        // already refuses such a layer, so a column edit that pushed it along
        // anyway would be the one path that ignored the folder.
        var vm = TwoLayers(3);
        var group = new LayerGroup { Name = "Backgrounds" };
        vm.Doc.Scene.LayerGroups.Add(group);
        vm.Doc.Scene.Layers[2].GroupId = group.Id;
        group.Locked = true;
        var before = Ids(vm);
        SelectColumns(vm, 1);

        vm.DeleteCelAt(Cell(vm, 1, 1));
        Assert.Equal(3, vm.Doc.Scene.FrameCount);
        Assert.Equal(before, Ids(vm));
        Assert.Contains("Backgrounds", vm.AiStatus);

        vm.InsertBlankFrameAt(Cell(vm, 1, 1));
        Assert.Equal(3, vm.Doc.Scene.FrameCount);
        Assert.Equal(before, Ids(vm));
    }

    [AvaloniaFact]
    public void ASelectionOfOnlyCameraKeysDoesNotFallBackToThePlayheadsCel()
    {
        // The keys go through the selection when there is one; a selection
        // with no cel in it names nothing to delete, and the playhead's
        // drawing is not what the artist picked.
        var vm = TwoLayers(3);
        vm.AddCameraCommand.Execute(null);
        vm.AddCameraKeyAt(1);
        vm.CurrentFrameIndex = 1;
        vm.SelectTrackKey(TimelineKey.Camera(1), toggle: false, range: false);
        var before = Ids(vm);

        vm.ClearCelAtPlayhead();
        vm.DeleteCelAtPlayhead();
        vm.InsertBlankFrameAtPlayhead();

        Assert.Equal(3, vm.Doc.Scene.FrameCount);
        Assert.Equal(before, Ids(vm));
        Assert.Contains("no cels", vm.AiStatus);
    }

    [AvaloniaFact]
    public void ThePaperBeingLockedDoesNotRefuseIt()
    {
        // The trap in the rule above: a document with paper has a locked layer
        // in it from the moment it is created, so a lock check that did not
        // exempt the paper would refuse every column delete in the ordinary case.
        var vm = TwoLayers(2);
        Assert.Contains(vm.Doc.Scene.Layers, l => l.IsBackground && l.Locked);
        SelectColumns(vm, 0);

        vm.DeleteCelAt(Cell(vm, 1, 0));

        Assert.Equal(1, vm.Doc.Scene.FrameCount);
    }

    [AvaloniaFact]
    public void ASceneIsNeverShorterThanOneFrame()
    {
        var vm = TwoLayers(3);
        SelectColumns(vm, 0, 1, 2);

        vm.DeleteCelAt(Cell(vm, 1, 0));

        Assert.Equal(1, vm.Doc.Scene.FrameCount);
        Assert.Contains("one frame", vm.AiStatus);
        Assert.True(vm.CurrentFrameIndex < vm.Doc.Scene.FrameCount);
    }

    [AvaloniaFact]
    public void ThePlayheadNeverLandsPastTheEnd()
    {
        var vm = TwoLayers(3);
        vm.CurrentFrameIndex = 2;
        SelectColumns(vm, 2);

        vm.DeleteCelAt(Cell(vm, 1, 2));

        Assert.True(vm.CurrentFrameIndex < vm.Doc.Scene.FrameCount);
    }

    // ---- Delete and pull: a partial selection pulls rows -----------------------

    [AvaloniaFact]
    public void APartialSelection_PullsItsRows_KeepsTheLength_InOneUndoStep()
    {
        var vm = TwoLayers(4);
        var before = Ids(vm);
        // Frame 1 on the first drawing layer, frame 2 on the second: two layers,
        // but not the same frames on both, so not a column.
        vm.ToggleCelSelection(Cell(vm, 1, 1));
        vm.ToggleCelSelection(Cell(vm, 2, 2));

        vm.DeleteCelAt(Cell(vm, 1, 1));

        Assert.Equal(4, vm.Doc.Scene.FrameCount);
        Assert.Equal([before[1][0], before[1][2], before[1][3], null], vm.Doc.Scene.Layers[1].Cels.Select(c => c.Frame?.Id));
        Assert.Equal([before[2][0], before[2][1], before[2][3], null], vm.Doc.Scene.Layers[2].Cels.Select(c => c.Frame?.Id));

        vm.UndoCommand.Execute(null); // once, for both rows
        Assert.Equal(before, Ids(vm));
    }

    // ---- Delete -----------------------------------------------------------------

    [AvaloniaFact]
    public void Delete_TurnsTheSelectedDrawingsIntoHolds_OnEveryLayer_InOneUndoStep()
    {
        var vm = TwoLayers(3);
        var before = Ids(vm);
        SelectColumns(vm, 1);

        vm.ClearCelAt(Cell(vm, 1, 1));

        // The slots stay — this is the delete that does not move anything.
        Assert.Equal(3, vm.Doc.Scene.FrameCount);
        Assert.Null(vm.Doc.Scene.Layers[1].Cels[1].Frame);
        Assert.Null(vm.Doc.Scene.Layers[2].Cels[1].Frame);
        Assert.Equal(before[1][2], vm.Doc.Scene.Layers[1].Cels[2].Frame?.Id);

        vm.UndoCommand.Execute(null);
        Assert.Equal(before, Ids(vm));
    }

    [AvaloniaFact]
    public void Delete_OnAHold_SaysThereIsNothingToDelete()
    {
        var vm = TwoLayers(3);
        var drawing = vm.Doc.Scene.Layers[1].Cels[1].Frame!.Id;
        vm.ClearCelAt(Cell(vm, 1, 1));

        vm.ClearCelAt(Cell(vm, 1, 1));

        Assert.Contains("hold", vm.AiStatus);
        // Nothing was recorded for the second press: one undo brings the
        // drawing back, rather than taking back an empty step first.
        vm.UndoCommand.Execute(null);
        Assert.Equal(drawing, vm.Doc.Scene.Layers[1].Cels[1].Frame?.Id);
    }

    // ---- Insert blank frame -----------------------------------------------------

    [AvaloniaFact]
    public void InsertBlank_OnAKey_HoldsThePreviousDrawing_AndMovesTheKeyRight()
    {
        var vm = TwoLayers(3);
        var before = Ids(vm);
        var layer = vm.Doc.Scene.Layers[1];

        vm.InsertBlankFrameAt(Cell(vm, 1, 1));

        layer = vm.Doc.Scene.Layers[1];
        Assert.Null(layer.Cels[1].Frame);
        Assert.Equal(before[1][0], ExposureSheet.ExposedFrame(layer, 1)?.Id);
        Assert.Equal(before[1][1], layer.Cels[2].Frame?.Id);
        Assert.Equal(before[1][2], layer.Cels[3].Frame?.Id);
        // The row ran past the end, so the scene grew; the other row did not move.
        Assert.Equal(4, vm.Doc.Scene.FrameCount);
        Assert.Equal(before[2], vm.Doc.Scene.Layers[2].Cels.Take(3).Select(c => c.Frame?.Id));

        vm.UndoCommand.Execute(null);
        Assert.Equal(3, vm.Doc.Scene.FrameCount);
        Assert.Equal(before, Ids(vm));
    }

    [AvaloniaFact]
    public void InsertBlank_OnARunOfN_InsertsNAtItsStart()
    {
        var vm = TwoLayers(4);
        var before = Ids(vm);
        vm.ToggleCelSelection(Cell(vm, 1, 1));
        vm.ToggleCelSelection(Cell(vm, 1, 2));

        vm.InsertBlankFrameAt(Cell(vm, 1, 1));

        Assert.Equal(
            [before[1][0], null, null, before[1][1], before[1][2], before[1][3]],
            vm.Doc.Scene.Layers[1].Cels.Select(c => c.Frame?.Id));
    }

    [AvaloniaFact]
    public void InsertBlank_OnAColumn_PushesEveryLayer_AndThePaperHolds()
    {
        var vm = TwoLayers(2);
        var before = Ids(vm);
        var paperId = before[0][0];
        SelectColumns(vm, 0);

        vm.InsertBlankFrameAt(Cell(vm, 1, 0));

        Assert.Equal(3, vm.Doc.Scene.FrameCount);
        foreach (var layer in new[] { 1, 2 })
        {
            Assert.Equal([null, before[layer][0], before[layer][1]], vm.Doc.Scene.Layers[layer].Cels.Select(c => c.Frame?.Id));
        }
        // Inserting in front of the paper's only drawing would have left frame 0
        // with no paper at all.
        for (var f = 0; f < 3; f++)
        {
            Assert.Equal(paperId, ExposureSheet.ExposedFrame(vm.Doc.Scene.Layers[0], f)?.Id);
        }

        vm.UndoCommand.Execute(null);
        Assert.Equal(before, Ids(vm));
    }

    [AvaloniaFact]
    public void InsertBlank_OnALockedColumn_IsRefused()
    {
        var vm = TwoLayers(2);
        SelectColumns(vm, 1);
        vm.SetLayerLocked(vm.Doc.Scene.Layers[1], true);

        vm.InsertBlankFrameAt(Cell(vm, 2, 1));

        Assert.Equal(2, vm.Doc.Scene.FrameCount);
        Assert.Contains("locked", vm.AiStatus, StringComparison.OrdinalIgnoreCase);
    }

    // ---- keys and menus -----------------------------------------------------------

    [AvaloniaFact]
    public void TheKeys_ActOnTheSelection_WhereverThePlayheadIs()
    {
        // Ctrl+click does not move the playhead, so a key that took the
        // playhead's cel whenever it was outside the selection would ignore a
        // selection the artist can see.
        var vm = TwoLayers(4);
        var before = Ids(vm);
        vm.CurrentFrameIndex = 0;
        vm.ToggleCelSelection(Cell(vm, 1, 2));

        vm.DeleteCelAtPlayhead();

        Assert.Equal(before[1][0], vm.Doc.Scene.Layers[1].Cels[0].Frame?.Id);
        Assert.Equal(before[1][3], vm.Doc.Scene.Layers[1].Cels[2].Frame?.Id);
    }

    [AvaloniaFact]
    public void TheTimelineBarsBin_IsAColumnDeleteThatKeepsThePaper_AndRespectsLocks()
    {
        var vm = TwoLayers(3);
        var paperId = vm.Doc.Scene.Layers[0].Cels[0].Frame!.Id;
        vm.CurrentFrameIndex = 0;

        vm.DeleteFrameCommand.Execute(null);

        Assert.Equal(2, vm.Doc.Scene.FrameCount);
        Assert.Equal(paperId, ExposureSheet.ExposedFrame(vm.Doc.Scene.Layers[0], 0)?.Id);

        vm.SetLayerLocked(vm.Doc.Scene.Layers[2], true);
        vm.DeleteFrameCommand.Execute(null);
        Assert.Equal(2, vm.Doc.Scene.FrameCount);
    }

    [Fact]
    public void TheVerbsAreInTheShortcutRegistry_ScopedToTheXsheet()
    {
        var map = new ShortcutMap();
        var delete = map.Find("xsheet.delete");
        var pull = map.Find("xsheet.deleteAndPull");
        var insert = map.Find("xsheet.insertBlankFrame");
        Assert.NotNull(delete);
        Assert.NotNull(pull);
        Assert.NotNull(insert);
        Assert.Equal(new ShortcutScope(ShortcutContext.Panel, DockPanelId.Xsheet), delete!.Scope);
        Assert.Equal("Delete", delete.Default?.ToString());
        Assert.Equal("Shift+Delete", pull!.Default?.ToString());
        Assert.Null(insert!.Default);
        // Retired, not re-pointed: see the comment in ShortcutMap.
        Assert.Null(map.Find("timeline.deleteColumn"));
    }

    [Fact]
    public void DeleteOverTheXsheet_DoesNotTakeDeleteFromAnywhereElse()
    {
        var map = new ShortcutMap();
        var delete = new KeyEventArgs { Key = Key.Delete, KeyModifiers = KeyModifiers.None };
        var shiftDelete = new KeyEventArgs { Key = Key.Delete, KeyModifiers = KeyModifiers.Shift };

        Assert.Equal("xsheet.delete", map.IdFor(delete, ShortcutScope.In(DockPanelId.Xsheet)));
        Assert.Equal("xsheet.deleteAndPull", map.IdFor(shiftDelete, ShortcutScope.In(DockPanelId.Xsheet)));
        Assert.Equal("select.clear", map.IdFor(delete, ShortcutScope.Canvas));
        Assert.Equal("docker.deleteLayer", map.IdFor(delete, ShortcutScope.In(DockPanelId.Layers)));
        Assert.Equal("reference.remove", map.IdFor(delete, ShortcutScope.Board));
        Assert.Null(map.IdFor(shiftDelete, ShortcutScope.Canvas));
        Assert.Null(map.ConflictWith("xsheet.delete", new KeyGesture(Key.Delete)));
    }

    [Fact]
    public void TheMenusOfferDeleteDeleteAndPullAndInsertBlank_AndNoDeleteColumn()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src/Lightbox.App/Views/MainWindow.axaml"));
        Assert.DoesNotContain("Delete column", xaml);
        Assert.DoesNotContain("OnDeleteColumn", xaml);
        Assert.DoesNotContain("OnMenuDeleteColumn", xaml);
        Assert.DoesNotContain("Clear cel", xaml);
        // Cel context menu.
        Assert.Matches(new Regex(@"<MenuItem Header=""Delete"" Click=""OnClearCel"""), xaml);
        Assert.Matches(new Regex(@"<MenuItem Header=""Delete and pull"" Click=""OnDeleteCel"""), xaml);
        Assert.Matches(new Regex(@"<MenuItem Header=""Insert blank frame"" Click=""OnInsertBlankFrame"""), xaml);
        // Animation menu.
        Assert.Matches(new Regex(@"<MenuItem Header=""De_lete"" Click=""OnMenuClearCel"""), xaml);
        Assert.Matches(new Regex(@"<MenuItem Header=""Delete and p_ull"" Click=""OnMenuDeleteCel"""), xaml);
        Assert.Matches(new Regex(@"<MenuItem Header=""Insert blank _frame"" Click=""OnMenuInsertBlankFrame"""), xaml);
    }
}

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Folders on the Timeline and the X-sheet (Q227): a folder is a row there as
/// it is in the Layers docker, folds with a state of its own, and while folded
/// shows one summary row for everything inside it.
/// </summary>
/// <remarks>
/// Both surfaces used to list every layer flat, whatever the Layers docker's
/// folders said — so a character built as sketch, line and colour layers cost
/// a row apiece on the sheet until they were merged.
/// </remarks>
[Collection("BrushState")]
public class SheetFolderTests(ITestOutputHelper output) : BrushStateIsolated
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
        t.Kind == TrackKind.Folder ? $"[{t.Name.Trim()}{(t.Folded ? "+" : "")}]" : t.Name.Trim()));

    private static FrameCell Cell(MainViewModel vm, string layer, int index) =>
        Row(vm, layer).Cells.First(c => c.Index == index);

    /// <summary>Top to bottom: f, [Outer: [Inner: e, d], c, b], a, paper. Six frames.</summary>
    private static MainViewModel Nested(MainViewModel? into = null)
    {
        var vm = into ?? VmLayers.PaperVm();
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
        Assert.Equal(outer.Group.Id, inner.Group.ParentId);
        while (vm.Doc.Scene.FrameCount < 6) vm.AddFrameCommand.Execute(null);
        vm.ClearCelRange();
        return vm;
    }

    private static string Paper(MainViewModel vm) => vm.LayerRows[^1].Name;

    // ---- the rows ------------------------------------------------------------------

    [AvaloniaFact]
    public void AFolderIsARowOnTheSheet_AboveWhatIsInsideIt()
    {
        var vm = Nested();

        output.WriteLine(Sheet(vm));
        Assert.Equal($"f [Outer] [Inner] e d c b a {Paper(vm)}", Sheet(vm));
        Assert.Equal(Sheet(vm), Tracks(vm));
    }

    [AvaloniaFact]
    public void FoldingOnTheSheetHidesWhatIsInside_OnBothSurfaces_AndLeavesTheLayersDockerAlone()
    {
        var vm = Nested();
        var docker = vm.LayerPanelItems.Count;

        vm.ToggleSheetFold(Folder(vm, "Inner"));
        Assert.Equal($"f [Outer] [Inner] c b a {Paper(vm)}", Sheet(vm));
        Assert.Equal($"f [Outer] [Inner+] c b a {Paper(vm)}", Tracks(vm));

        vm.ToggleSheetFold(Folder(vm, "Outer"));
        Assert.Equal($"f [Outer] a {Paper(vm)}", Sheet(vm));
        Assert.Equal($"f [Outer+] a {Paper(vm)}", Tracks(vm));

        Assert.Equal(docker, vm.LayerPanelItems.Count);
        Assert.All(vm.Doc.Scene.LayerGroups, g => Assert.False(g.Collapsed));

        vm.ToggleSheetFold(Folder(vm, "Outer"));
        Assert.Equal($"f [Outer] [Inner] c b a {Paper(vm)}", Sheet(vm)); // Inner is still folded
    }

    [AvaloniaFact]
    public void CollapsingInTheLayersDockerDoesNotFoldTheSheet()
    {
        var vm = Nested();
        var before = Sheet(vm);

        vm.LayerPanelItems.OfType<GroupRow>().Single(h => h.Name == "Outer").Collapsed = true;

        Assert.Equal(before, Sheet(vm));
    }

    /// <summary>
    /// Folding is how the sheet is looked at, not an edit to the drawing: it
    /// adds nothing for Ctrl+Z to find, as collapsing in the Layers docker adds
    /// nothing.
    /// </summary>
    [AvaloniaFact]
    public void FoldingIsNotAnUndoStep()
    {
        var vm = Nested();
        var depth = vm.UndoDepth;

        vm.ToggleSheetFold(Folder(vm, "Outer"));

        Assert.Equal(depth, vm.UndoDepth);
        Assert.True(vm.Doc.Scene.LayerGroups.Single(g => g.Name == "Outer").SheetCollapsed);
    }

    // ---- the summary ---------------------------------------------------------------

    [AvaloniaFact]
    public void AFolderRowMarksEveryFrameAnythingInsideItIsDrawnOn_AtAnyDepth()
    {
        var vm = Nested();
        var keyed = new SortedSet<int>();
        foreach (var name in new[] { "b", "c", "d", "e" })
        {
            foreach (var cell in Row(vm, name).Cells.Where(c => c.IsKeyed && !c.IsVirtual)) keyed.Add(cell.Index);
        }
        // Break the tie that would make this pass by accident: every layer is
        // drawn on frame 3, so carry that drawing off it on all but the one two
        // folders down.
        Assert.Contains(3, keyed);
        foreach (var name in new[] { "b", "c", "e" }) vm.MoveCel(Cell(vm, name, 3), Cell(vm, name, 8), copy: false);
        Assert.True(Cell(vm, "d", 3).IsKeyed);
        Assert.False(Cell(vm, "b", 3).IsKeyed);

        var outer = Folder(vm, "Outer").Cells.Where(c => c.IsKeyed).Select(c => c.Index).ToList();
        var inner = Folder(vm, "Inner").Cells.Where(c => c.IsKeyed).Select(c => c.Index).ToList();
        output.WriteLine($"outer {string.Join(",", outer)} inner {string.Join(",", inner)}");

        Assert.Contains(3, outer); // d is two folders down from Outer
        Assert.Contains(3, inner);
        Assert.Contains(8, outer); // b and c, directly inside
        Assert.Contains(8, inner); // e

        vm.MoveCel(Cell(vm, "d", 3), Cell(vm, "d", 9), copy: false);
        Assert.DoesNotContain(3, Folder(vm, "Outer").Cells.Where(c => c.IsKeyed).Select(c => c.Index));
        Assert.DoesNotContain(3, Folder(vm, "Inner").Cells.Where(c => c.IsKeyed).Select(c => c.Index));
        Assert.Contains(9, Folder(vm, "Outer").Cells.Where(c => c.IsKeyed).Select(c => c.Index));
    }

    [AvaloniaFact]
    public void TheFolderTrackCarriesTheSameMarks_AndTheChevron()
    {
        var vm = Nested();
        var index = vm.TimelineTracks.ToList().FindIndex(t => t.Kind == TrackKind.Folder && t.Name.Trim() == "Outer");
        var track = vm.TimelineTracks[index];

        Assert.True(track.HasChildren);
        Assert.False(track.Folded);
        Assert.Equal(
            Folder(vm, "Outer").Cells.Where(c => c.IsKeyed && !c.IsVirtual).Select(c => c.Index),
            track.Keys);

        vm.ToggleTrackFold(index);
        Assert.True(vm.TimelineTracks[index].Folded);
        Assert.Equal($"f [Outer] a {Paper(vm)}", Sheet(vm));
    }

    // ---- row arithmetic --------------------------------------------------------------

    /// <summary>
    /// Folder rows sit between the layer rows, so a row index is no longer a
    /// layer index plus an offset. Get this wrong and a drag retimes the layer
    /// above the one that was grabbed.
    /// </summary>
    [AvaloniaFact]
    public void ATrackRowStillNamesTheLayerDrawnOnIt_WithFoldersBetween()
    {
        var vm = Nested();
        vm.ToggleSheetFold(Folder(vm, "Inner"));
        var tracks = vm.TimelineTracks;

        for (var t = 0; t < tracks.Count; t++)
        {
            var key = vm.TrackKeyAt(t, 2);
            if (tracks[t].Kind == TrackKind.Folder)
            {
                Assert.Null(key);
                continue;
            }
            Assert.NotNull(key);
            Assert.Equal(tracks[t].Name.Trim(), vm.Doc.Scene.Layers[key.Value.LayerIndex].Name);
        }
    }

    [AvaloniaFact]
    public void ASelectedCelLightsTheRowItIsDrawnOn()
    {
        var vm = Nested();
        vm.ToggleCelSelection(Cell(vm, "c", 2));

        var dot = Assert.Single(vm.TimelineSelectedDots);

        Assert.Equal("c", vm.TimelineTracks[dot.Row].Name.Trim());
        Assert.Equal(2, dot.Frame);
    }

    // ---- what is out of sight is out of reach ---------------------------------------

    [AvaloniaFact]
    public void FoldingDropsTheSelectionOfCelsThatWentOutOfSight()
    {
        var vm = Nested();
        vm.ToggleCelSelection(Cell(vm, "d", 1));
        vm.ToggleCelSelection(Cell(vm, "a", 1));
        var d = vm.Doc.Scene.Layers.IndexOf(Row(vm, "d").Layer);
        var a = vm.Doc.Scene.Layers.IndexOf(Row(vm, "a").Layer);

        vm.ToggleSheetFold(Folder(vm, "Inner"));

        // A Delete over the sheet must not reach a cel nobody can see is selected.
        Assert.DoesNotContain((d, 1), vm.CelSelection);
        Assert.Contains((a, 1), vm.CelSelection);
    }

    [AvaloniaFact]
    public void ABlockSweptAcrossAFoldedFolderLeavesItsLayersOut()
    {
        var vm = Nested();
        vm.ToggleSheetFold(Folder(vm, "Outer"));

        vm.DragSelectTo(Cell(vm, "f", 4), Cell(vm, "a", 5));

        var layers = vm.CelSelection.Select(c => vm.Doc.Scene.Layers[c.Layer].Name).Distinct().OrderBy(n => n).ToList();
        Assert.Equal(["a", "f"], layers);
    }

    // ---- as drawn ------------------------------------------------------------------

    [AvaloniaFact]
    public void TheXsheetDrawsAFolderRow_WithACellForEveryFrame()
    {
        var window = new Views.MainWindow { Width = 1600, Height = 1000 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        try
        {
            Nested(vm);
            vm.Workspace.SetVisible(Docking.DockPanelId.Xsheet, true);
            vm.Workspace.Activate(Docking.DockPanelId.Xsheet);
            for (var i = 0; i < 6; i++) Dispatcher.UIThread.RunJobs();

            var list = window.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.Name == "XsheetLayerList");
            Assert.Equal(vm.SheetRows.Count, list.ItemCount);
            var folder = Folder(vm, "Outer");
            var row = list.ContainerFromItem(folder)!.GetVisualDescendants().OfType<Views.XsheetFolderRow>().Single();
            var cells = row.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("folderCel"));
            Assert.Equal(folder.Cells.Count, cells);
            Assert.True(cells > 0);
        }
        finally
        {
            window.Close();
        }
    }
}

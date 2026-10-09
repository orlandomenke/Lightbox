using Avalonia.Headless.XUnit;
using Lightbox.App.Controls;
using Lightbox.App.Input;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Dragging a folder's summary mark (Q227) retimes every drawing inside the
/// folder on that frame together — a whole pose moved by one handle.
/// </summary>
/// <remarks>
/// The drag reaches layers whose rows may be folded away, so it is stricter
/// than dragging one cel: where a single cel replaces what it lands on, this
/// refuses to land on a drawing at all, and refuses if anything inside is
/// locked. A pose moves whole or not at all.
/// </remarks>
[Collection("BrushState")]
public class FolderSummaryDragTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static LayerRow Row(MainViewModel vm, string name) =>
        vm.LayerRows.Single(r => r.Layer.Name == name);

    private static SheetFolderRow Folder(MainViewModel vm, string name) =>
        vm.SheetRows.OfType<SheetFolderRow>().Single(f => f.Name == name);

    private static FrameCell Cell(MainViewModel vm, string layer, int index) =>
        Row(vm, layer).Cells.First(c => c.Index == index);

    /// <summary>The frame id of every drawing on a layer, by index.</summary>
    private static Dictionary<int, string> Drawn(MainViewModel vm, string layer) =>
        Row(vm, layer).Layer.Cels
            .Select((c, i) => (c.Frame, i))
            .Where(x => x.Frame is not null)
            .ToDictionary(x => x.i, x => x.Frame!.Id);

    private static List<int> Marks(MainViewModel vm, string folder) =>
        Folder(vm, folder).Cells.Where(c => c.IsKeyed).Select(c => c.Index).ToList();

    /// <summary>
    /// Top to bottom: f, [Outer: [Inner: e, d], c, b], a, paper. Every drawing
    /// layer is drawn on frames 0–3, and nowhere after.
    /// </summary>
    private static MainViewModel Nested()
    {
        var vm = VmLayers.PaperVm();
        while (vm.Doc.Scene.Layers.Count < 7) vm.AddPaintedLayerCommand.Execute(null);
        string[] names = ["a", "b", "c", "d", "e", "f"];
        for (var i = 1; i < 7; i++) vm.LayerRows.Single(r => r.SceneIndex == i).Name = names[i - 1];
        foreach (var name in names)
        {
            for (var i = 0; i < 4; i++)
            {
                if (!Drawn(vm, name).ContainsKey(i)) vm.InsertFrameAt(Cell(vm, name, i), FrameRole.Key);
            }
            Assert.Equal([0, 1, 2, 3], Drawn(vm, name).Keys.OrderBy(i => i));
        }
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
        vm.ClearCelRange();
        return vm;
    }

    [AvaloniaFact]
    public void DraggingAFoldersMarkMovesEveryDrawingInsideOnThatFrame_AtAnyDepth()
    {
        var vm = Nested();
        var before = new[] { "b", "c", "d", "e" }.ToDictionary(n => n, n => Drawn(vm, n));
        var outside = new[] { "a", "f" }.ToDictionary(n => n, n => Drawn(vm, n));

        var moved = vm.RetimeFolder(Folder(vm, "Outer"), 3, 6);

        output.WriteLine($"moved {moved}; marks {string.Join(",", Marks(vm, "Outer"))}");
        Assert.Equal(4, moved);
        foreach (var name in before.Keys)
        {
            var after = Drawn(vm, name);
            Assert.Equal([0, 1, 2, 6], after.Keys.OrderBy(i => i));
            Assert.Equal(before[name][3], after[6]); // the same drawing, not a copy
        }
        foreach (var name in outside.Keys) Assert.Equal(outside[name], Drawn(vm, name));
        Assert.Equal([0, 1, 2, 6], Marks(vm, "Outer"));
        Assert.Equal([0, 1, 2, 6], Marks(vm, "Inner"));
    }

    [AvaloniaFact]
    public void AnInnerFoldersMarkMovesOnlyWhatIsInsideIt()
    {
        var vm = Nested();
        var b = Drawn(vm, "b");

        var moved = vm.RetimeFolder(Folder(vm, "Inner"), 3, 6);

        Assert.Equal(2, moved);
        Assert.Equal(b, Drawn(vm, "b"));
        Assert.Equal([0, 1, 2, 6], Drawn(vm, "d").Keys.OrderBy(i => i));
        Assert.Equal([0, 1, 2, 3, 6], Marks(vm, "Outer")); // b and c are still on 3
    }

    [AvaloniaFact]
    public void ItIsOneUndoStep()
    {
        var vm = Nested();
        var before = new[] { "b", "c", "d", "e" }.ToDictionary(n => n, n => Drawn(vm, n));

        vm.RetimeFolder(Folder(vm, "Outer"), 3, 6);
        vm.UndoCommand.Execute(null);

        foreach (var name in before.Keys) Assert.Equal(before[name], Drawn(vm, name));
    }

    [AvaloniaFact]
    public void ALayerInsideWithNoDrawingOnThatFrameIsLeftAlone()
    {
        var vm = Nested();
        vm.MoveCel(Cell(vm, "c", 3), Cell(vm, "c", 8), copy: false); // c: 0,1,2,8
        var c = Drawn(vm, "c");

        var moved = vm.RetimeFolder(Folder(vm, "Outer"), 3, 6);

        Assert.Equal(3, moved);
        Assert.Equal(c, Drawn(vm, "c"));
    }

    /// <summary>
    /// One cel dragged onto another replaces it, in plain sight. A folder's
    /// drag reaches rows that may be folded away, and must not overwrite a
    /// drawing nobody is looking at.
    /// </summary>
    [AvaloniaFact]
    public void ItRefusesToLandOnADrawing_AndMovesNothing()
    {
        var vm = Nested();
        vm.MoveCel(Cell(vm, "d", 0), Cell(vm, "d", 6), copy: false); // d already has a drawing on 6
        var before = new[] { "b", "c", "d", "e" }.ToDictionary(n => n, n => Drawn(vm, n));
        var depth = vm.UndoDepth;

        var moved = vm.RetimeFolder(Folder(vm, "Outer"), 3, 6);

        Assert.Equal(0, moved);
        foreach (var name in before.Keys) Assert.Equal(before[name], Drawn(vm, name));
        Assert.Equal(depth, vm.UndoDepth);
        Assert.Contains("d", vm.AiStatus);
    }

    [AvaloniaFact]
    public void ItRefusesWhileAnythingInsideIsLocked()
    {
        var vm = Nested();
        Row(vm, "e").Locked = true;
        var before = new[] { "b", "c", "d", "e" }.ToDictionary(n => n, n => Drawn(vm, n));

        var moved = vm.RetimeFolder(Folder(vm, "Outer"), 3, 6);

        Assert.Equal(0, moved);
        foreach (var name in before.Keys) Assert.Equal(before[name], Drawn(vm, name));
    }

    [AvaloniaFact]
    public void ADragOffTheFrontOrOntoItselfDoesNothing()
    {
        var vm = Nested();
        var depth = vm.UndoDepth;

        Assert.Equal(0, vm.RetimeFolder(Folder(vm, "Outer"), 3, 3));
        Assert.Equal(0, vm.RetimeFolder(Folder(vm, "Outer"), 3, -1));
        Assert.Equal(0, vm.RetimeFolder(Folder(vm, "Outer"), 5, 7)); // no mark there to pick up

        Assert.Equal(depth, vm.UndoDepth);
    }

    /// <summary>The Timeline's drag on a folder's track is the same verb.</summary>
    [AvaloniaFact]
    public void TheTimelinesDragOnAFolderTrackRetimesTheFolder_AndALayerTrackStillRetimesItsLayer()
    {
        var vm = Nested();
        var tracks = vm.TimelineTracks.ToList();
        var folder = tracks.FindIndex(t => t.Kind == TrackKind.Folder && t.Name.Trim() == "Inner");
        var layer = tracks.FindIndex(t => t.Kind == TrackKind.Layer && t.Name.Trim() == "a");

        vm.DragTrackKey(folder, 3, 6);
        Assert.Equal([0, 1, 2, 6], Drawn(vm, "d").Keys.OrderBy(i => i));
        Assert.Equal([0, 1, 2, 6], Drawn(vm, "e").Keys.OrderBy(i => i));
        Assert.Equal([0, 1, 2, 3], Drawn(vm, "b").Keys.OrderBy(i => i));

        vm.DragTrackKey(layer, 3, 5);
        Assert.Equal([0, 1, 2, 5], Drawn(vm, "a").Keys.OrderBy(i => i));
    }

    /// <summary>The selection follows the drawings it was on, so it never points at an empty cel.</summary>
    [AvaloniaFact]
    public void ASelectedCelThatMovedIsStillSelectedWhereItLanded()
    {
        var vm = Nested();
        vm.ToggleCelSelection(Cell(vm, "b", 3));
        var b = Row(vm, "b").SceneIndex;

        vm.RetimeFolder(Folder(vm, "Outer"), 3, 6);

        Assert.Contains((b, 6), vm.CelSelection);
        Assert.DoesNotContain((b, 3), vm.CelSelection);
    }

    // ---- the X-sheet's arithmetic ----------------------------------------------------

    /// <summary>Which frame a pointer is over, along a strip of cells with gaps between them.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(19.9, 0)]
    [InlineData(21, 0)]    // in the gap after cell 0: still cell 0's
    [InlineData(22, 1)]
    [InlineData(65.5, 2)]
    [InlineData(-5, 0)]    // off the left end
    [InlineData(9999, 11)] // off the right end
    public void ThePointerNamesTheCellItIsOver(double x, int expected)
    {
        Assert.Equal(expected, FolderSummaryDrag.FrameAt(x, cellWidth: 20, gap: 2, count: 12));
    }

    [Fact]
    public void AWobbleIsNotADrag()
    {
        Assert.False(FolderSummaryDrag.IsDrag(new Avalonia.Point(10, 10), new Avalonia.Point(13, 12)));
        Assert.True(FolderSummaryDrag.IsDrag(new Avalonia.Point(10, 10), new Avalonia.Point(17, 10)));
    }
}

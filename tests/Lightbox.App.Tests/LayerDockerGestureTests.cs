using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;

namespace Lightbox.App.Tests;

/// <summary>
/// The layer docker under a real pointer: selection clicks, dragging rows and
/// folders, and where a new layer lands.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these click rather than call.</b> The view-model half of every one of
/// these gestures was already tested and green while an artist reported all of
/// them broken: Ctrl/Shift+clicks landed on the row's toggles and were eaten,
/// drops between rows and in a folder's indent went nowhere, and the drag ran
/// through the operating system's modal drag loop. None of that is visible to a
/// test that calls <c>SelectLayer</c> or <c>DropLayerOnRow</c> directly.
/// </para>
/// <para>
/// The drag is a pointer captured by the list (no OS drag-and-drop), which is
/// what makes it drivable by the headless mouse at all.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class LayerDockerGestureTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    /// <summary>Paper plus three paint layers, bottom-first names: Background, a, b, c.</summary>
    private static (MainWindow Window, MainViewModel Vm) Open(int extra = 3)
    {
        var window = new MainWindow { Width = 1400, Height = 1600 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Layers", 400, 300, 12, 72, "#ffffff", false));
        while (vm.Doc.Scene.Layers.Count < 1 + extra) vm.AddPaintedLayerCommand.Execute(null);
        string[] names = ["a", "b", "c", "d", "e"];
        for (var i = 1; i < vm.Doc.Scene.Layers.Count; i++) vm.Doc.Scene.Layers[i].Name = names[i - 1];
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.Count - 1;
        Pump();
        return (window, vm);
    }

    /// <summary>Bottom to top: Background, a, [Folder: b, c], d.</summary>
    private static (MainWindow Window, MainViewModel Vm) OpenWithFolder()
    {
        var (window, vm) = Open(extra: 4);
        var layers = vm.Doc.Scene.Layers;
        vm.SelectLayer(RowOf(vm, "b"), toggle: false, range: false);
        vm.SelectLayer(RowOf(vm, "c"), toggle: true, range: false);
        vm.CreateLayerFolderCommand.Execute(null);
        Pump();
        return (window, vm);
    }

    private static string Order(MainViewModel vm) =>
        string.Join(",", vm.Doc.Scene.Layers.Select(l => l.GroupId is null ? l.Name : $"[{l.Name}]"));

    private static LayerRow RowOf(MainViewModel vm, string name) =>
        vm.LayerRows.First(r => r.Layer.Name == name);

    private static GroupRow HeaderOf(MainViewModel vm) =>
        vm.LayerPanelItems.OfType<GroupRow>().Single();

    /// <summary>The docker's control for an item — the row's Border.</summary>
    private static Border ControlOf(Window window, object item) =>
        window.GetVisualDescendants().OfType<Border>()
            .First(b => (b.Classes.Contains("layerRow") || b.Classes.Contains("groupRow"))
                        && ReferenceEquals(b.DataContext, item));

    private static Point At(Window window, Visual control, double fx, double fy) =>
        control.TranslatePoint(new Point(control.Bounds.Width * fx, control.Bounds.Height * fy), window)!.Value;

    private static Point Centre(Window window, Visual control) => At(window, control, 0.6, 0.5);

    private static void Click(Window window, Point at, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.MouseDown(at, MouseButton.Left, mods);
        window.MouseUp(at, MouseButton.Left, mods);
        Pump();
        // Off the docker, so the next click is never read as a double-click.
        window.MouseMove(new Point(700, 5));
        Pump();
    }

    private static void Drag(Window window, Point from, Point to)
    {
        window.MouseDown(from, MouseButton.Left);
        Pump();
        const int steps = 6;
        for (var i = 1; i <= steps; i++)
        {
            var p = new Point(from.X + (to.X - from.X) * i / steps, from.Y + (to.Y - from.Y) * i / steps);
            window.MouseMove(p, RawInputModifiers.LeftMouseButton);
            Pump();
        }
        window.MouseUp(to, MouseButton.Left);
        Pump();
    }

    // ---- selection clicks ----------------------------------------------------

    /// <summary>
    /// Ctrl+click on a row's eye adds the row to the selection — and leaves the eye alone.
    /// </summary>
    [AvaloniaFact]
    public void CtrlClickOnARowsToggleSelectsTheRowInsteadOfFlippingTheToggle()
    {
        var (window, vm) = Open();
        var row = RowOf(vm, "a");
        var eye = ControlOf(window, row).GetVisualDescendants().OfType<ToggleButton>().First();

        Click(window, Centre(window, eye), RawInputModifiers.Control);

        output.WriteLine($"selected {vm.SelectedLayerCount}, a visible {row.Layer.Visible}");
        Assert.Equal(2, vm.SelectedLayerCount);
        Assert.Contains(row.Layer.Id, vm.SelectedLayerIds);
        Assert.True(row.Layer.Visible, "the Ctrl+click flipped the eye instead of selecting");
    }

    [AvaloniaFact]
    public void ShiftClickOnTheReorderArrowsTakesTheRange()
    {
        var (window, vm) = Open();
        var arrows = ControlOf(window, RowOf(vm, "a")).GetVisualDescendants().OfType<Button>().Last();
        var before = Order(vm);

        Click(window, Centre(window, arrows), RawInputModifiers.Shift);

        Assert.Equal(3, vm.SelectedLayerCount); // c (active) through a
        Assert.Equal(before, Order(vm));
    }

    /// <summary>Ctrl on the thumbnail keeps its own meaning: select the layer's pixels.</summary>
    [AvaloniaFact]
    public void CtrlClickOnTheThumbnailStillMeansItsPixels()
    {
        var (window, vm) = Open();
        var thumb = ControlOf(window, RowOf(vm, "a")).GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("layerThumb"));

        Click(window, Centre(window, thumb), RawInputModifiers.Control);

        Assert.Equal(1, vm.SelectedLayerCount);
    }

    // ---- dragging ------------------------------------------------------------

    [AvaloniaFact]
    public void DraggingARowWithTheMouseReordersTheStack()
    {
        var (window, vm) = Open();
        var c = ControlOf(window, RowOf(vm, "c"));
        var a = ControlOf(window, RowOf(vm, "a"));

        Drag(window, Centre(window, c), At(window, a, 0.6, 0.8));

        output.WriteLine(Order(vm));
        Assert.Equal("Background,c,a,b", Order(vm));
    }

    /// <summary>
    /// The two pixels between rows used to belong to no row, and a release
    /// there was thrown away.
    /// </summary>
    [AvaloniaFact]
    public void ReleasingInTheGapBetweenRowsStillDrops()
    {
        var (window, vm) = Open();
        var c = ControlOf(window, RowOf(vm, "c"));
        var b = ControlOf(window, RowOf(vm, "b"));
        var a = ControlOf(window, RowOf(vm, "a"));
        var bBottom = b.TranslatePoint(new Point(0, b.Bounds.Height), window)!.Value.Y;
        var aTop = a.TranslatePoint(new Point(0, 0), window)!.Value.Y;
        output.WriteLine($"gap {bBottom}..{aTop}");

        Drag(window, Centre(window, c), new Point(Centre(window, a).X, (bBottom + aTop) / 2));

        output.WriteLine(Order(vm));
        Assert.Equal("Background,a,c,b", Order(vm));
    }

    [AvaloniaFact]
    public void AFolderCanBeDraggedByItsHeader()
    {
        var (window, vm) = OpenWithFolder();
        Assert.Equal("Background,a,[b],[c],d", Order(vm));
        var header = ControlOf(window, HeaderOf(vm));
        var d = ControlOf(window, RowOf(vm, "d"));

        Drag(window, Centre(window, header), At(window, d, 0.6, 0.15));

        output.WriteLine(Order(vm));
        Assert.Equal("Background,a,d,[b],[c]", Order(vm));
    }

    [AvaloniaFact]
    public void DroppingALayerOnAnOpenHeaderFilesItAtTheTopOfTheFolder()
    {
        var (window, vm) = OpenWithFolder();
        var header = ControlOf(window, HeaderOf(vm));
        var a = ControlOf(window, RowOf(vm, "a"));

        // The lower part of an open header: under the header is inside the folder.
        Drag(window, Centre(window, a), At(window, header, 0.6, 0.9));

        output.WriteLine(Order(vm));
        Assert.Equal("Background,[b],[c],[a],d", Order(vm));
    }

    /// <summary>
    /// A grouped row is indented; the indent used to belong to nothing, so a
    /// drop aimed at the left of a member went nowhere.
    /// </summary>
    [AvaloniaFact]
    public void ALayerReordersInsideAFolderEvenWhenReleasedInTheIndent()
    {
        var (window, vm) = OpenWithFolder();
        var c = ControlOf(window, RowOf(vm, "c"));
        var b = ControlOf(window, RowOf(vm, "b"));
        var indent = b.TranslatePoint(new Point(-6, b.Bounds.Height * 0.8), window)!.Value;

        Drag(window, Centre(window, c), indent);

        output.WriteLine(Order(vm));
        Assert.Equal("Background,a,[c],[b],d", Order(vm));
    }

    [AvaloniaFact]
    public void ReleasingOutsideTheDockerCancelsTheDrag()
    {
        var (window, vm) = Open();
        var before = Order(vm);
        var c = ControlOf(window, RowOf(vm, "c"));

        Drag(window, Centre(window, c), new Point(700, Centre(window, c).Y + 40));

        Assert.Equal(before, Order(vm));
        Assert.All(vm.LayerRows, r => Assert.Equal(LayerDropHint.None, r.DropHint));
    }

    // ---- the docker is not rebuilt for every edit ----------------------------

    /// <summary>
    /// A reorder or an eye toggle keeps every row's control; only a real change
    /// in the list's shape touches it.
    /// </summary>
    [AvaloniaFact]
    public void AReorderKeepsTheRowControls()
    {
        var (window, vm) = OpenWithFolder();
        var changes = new List<string>();
        vm.LayerPanelItems.CollectionChanged += (_, e) => changes.Add(e.Action.ToString());
        var controls = vm.LayerPanelItems.Select(i => ControlOf(window, i)).ToList();

        // Rows are re-pointed by position, so the only real change in the
        // list's shape here is the folder header moving down one place.
        vm.DropLayerOnRow(RowOf(vm, "d"), RowOf(vm, "a"), above: false);
        Pump();
        output.WriteLine($"reorder: {string.Join(",", changes)}");
        Assert.DoesNotContain("Reset", changes);
        Assert.True(changes.Count <= 2, $"a one-row move made {changes.Count} list changes");

        changes.Clear();
        vm.SetLayerVisible(vm.Doc.Scene.Layers[1], false);
        Pump();
        Assert.Empty(changes);
        var kept = vm.LayerPanelItems.Select(i => ControlOf(window, i)).Count(c => controls.Contains(c));
        output.WriteLine($"{kept} of {controls.Count} row controls survived");
        Assert.True(kept >= controls.Count - 1);
    }

    // ---- a picked folder, and where a new layer goes -------------------------

    [AvaloniaFact]
    public void ANewLayerWithTheFolderPickedGoesInsideIt()
    {
        var (window, vm) = OpenWithFolder();
        Click(window, Centre(window, ControlOf(window, HeaderOf(vm))));
        Assert.True(HeaderOf(vm).IsSelected);

        vm.AddPaintedLayerCommand.Execute(null);

        var added = vm.Doc.Scene.Layers[vm.ActiveLayerIndex];
        output.WriteLine(Order(vm));
        Assert.Equal(vm.Doc.Scene.LayerGroups.Single().Id, added.GroupId);
        Assert.Equal("Background,a,[b],[c],[" + added.Name + "],d", Order(vm));
    }

    [AvaloniaFact]
    public void ANewLayerGoesAboveTheActiveLayerAndIntoItsFolder()
    {
        var (_, vm) = OpenWithFolder();
        vm.SelectLayer(RowOf(vm, "b"), toggle: false, range: false);

        vm.AddPaintedLayerCommand.Execute(null);

        var added = vm.Doc.Scene.Layers[vm.ActiveLayerIndex];
        Assert.Equal("Background,a,[b],[" + added.Name + "],[c],d", Order(vm));
    }

    [AvaloniaFact]
    public void ANewLayerOnALooseLayerStaysLooseAndLandsAboveIt()
    {
        var (_, vm) = OpenWithFolder();
        vm.SelectLayer(RowOf(vm, "a"), toggle: false, range: false);

        vm.AddPaintedLayerCommand.Execute(null);

        var added = vm.Doc.Scene.Layers[vm.ActiveLayerIndex];
        Assert.Equal("Background,a," + added.Name + ",[b],[c],d", Order(vm));

        vm.UndoCommand.Execute(null);
        Assert.Equal("Background,a,[b],[c],d", Order(vm));
    }

    // ---- what the adversarial review found -----------------------------------

    /// <summary>A layer added to a collapsed folder opens it, or it has no row to show.</summary>
    [AvaloniaFact]
    public void ANewLayerInACollapsedFolderOpensIt()
    {
        var (window, vm) = OpenWithFolder();
        Click(window, Centre(window, ControlOf(window, HeaderOf(vm))));
        HeaderOf(vm).Collapsed = true;
        Pump();

        vm.AddPaintedLayerCommand.Execute(null);

        var added = vm.Doc.Scene.Layers[vm.ActiveLayerIndex];
        Assert.False(vm.Doc.Scene.LayerGroups.Single().Collapsed);
        Assert.Contains(vm.LayerPanelItems, i => i is LayerRow r && r.Layer.Id == added.Id);
    }

    /// <summary>
    /// A drop that leaves the stack as it was is not an undo step — measuring
    /// every row makes one easy to make: a row onto the upper half of the row
    /// under it is where it already is.
    /// </summary>
    [AvaloniaFact]
    public void ADropThatMovesNothingIsNotAnUndoStep()
    {
        var (_, vm) = OpenWithFolder();
        var steps = vm.RecordedStepCount;
        var before = Order(vm);

        vm.DropOnLayerPanel(RowOf(vm, "a"), RowOf(vm, "Background"), LayerDropHint.Above);
        vm.DropOnLayerPanel(HeaderOf(vm), RowOf(vm, "d"), LayerDropHint.Below);

        Assert.Equal(before, Order(vm));
        Assert.Equal(steps, vm.RecordedStepCount);
    }

    /// <summary>
    /// With no paper and a folder at the bottom, the last row's bottom quarter
    /// is the only place left that can mean "under the folder, outside it".
    /// </summary>
    [AvaloniaFact]
    public void ALayerCanStillLeaveAFolderAtTheBottomOfAPaperlessStack()
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("Clear", 400, 300, 12, 72, "#ffffff", TransparentBackground: true));
        vm.AddPaintedLayerCommand.Execute(null);
        var layers = vm.Doc.Scene.Layers;
        Assert.DoesNotContain(layers, l => l.IsBackground);
        var bottom = layers[0];
        var top = layers[^1];
        vm.ActiveLayerIndex = 0;
        vm.CreateLayerFolderCommand.Execute(null);
        var bottomRow = vm.LayerPanelItems.OfType<LayerRow>().Single(r => r.Layer.Id == bottom.Id);
        var topRow = vm.LayerPanelItems.OfType<LayerRow>().Single(r => r.Layer.Id == top.Id);

        Assert.Equal(LayerDropTarget.BottomGroupedLayer, vm.LayerDropTargetOf(bottomRow));
        var hint = vm.LayerDropHintFor(topRow, bottomRow, 0.95);
        Assert.Equal(LayerDropHint.BelowFolder, hint);

        vm.DropOnLayerPanel(topRow, bottomRow, hint);

        Assert.Equal(top.Id, vm.Doc.Scene.Layers[0].Id);
        Assert.Null(vm.Doc.Scene.Layers[0].GroupId);
        // The upper three quarters still mean inside, beside that row.
        Assert.Equal(LayerDropHint.Below, LayerDropPlan.Resolve(0.6, LayerDropTarget.BottomGroupedLayer, false));
    }

    /// <summary>Pressing the right button mid-drag does not drop.</summary>
    [AvaloniaFact]
    public void ARightClickMidDragDoesNotDrop()
    {
        var (window, vm) = Open();
        var before = Order(vm);
        var c = ControlOf(window, RowOf(vm, "c"));
        var a = ControlOf(window, RowOf(vm, "a"));
        var from = Centre(window, c);
        var to = At(window, a, 0.6, 0.8);

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point(from.X, from.Y + 20), RawInputModifiers.LeftMouseButton);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseDown(to, MouseButton.Right, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Right, RawInputModifiers.LeftMouseButton);
        Pump();
        Assert.Equal(before, Order(vm));

        window.MouseUp(to, MouseButton.Left);
        Pump();
        Assert.Equal("Background,c,a,b", Order(vm));
    }

    /// <summary>Shift+click inside a name being edited stays in the edit.</summary>
    [AvaloniaFact]
    public void AModifiedClickInsideARenameIsTheRenamesOwn()
    {
        var (window, vm) = Open();
        var row = RowOf(vm, "b");
        row.IsRenaming = true;
        Pump();
        var box = ControlOf(window, row).GetVisualDescendants().OfType<TextBox>().First(t => t.IsVisible);
        box.Focus();
        Pump();
        var selectedBefore = vm.SelectedLayerCount;

        var at = Centre(window, box);
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.Shift);
        Pump();

        output.WriteLine($"renaming {row.IsRenaming}, box focused {box.IsFocused}, selected {vm.SelectedLayerCount}");
        Assert.True(row.IsRenaming);
        Assert.Equal(selectedBefore, vm.SelectedLayerCount);
    }
}

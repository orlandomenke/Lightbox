using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.Docking;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Tests;

/// <summary>
/// The symmetry controls: the toggle that places an axis, the fields and the
/// gizmo drags that edit it, and what the record says about any of it.
/// </summary>
/// <remarks>
/// The engine behind these landed on 2026-09-10 with nothing an artist could
/// reach — <c>ActiveSymmetry</c> was set by tests and by nothing else. These
/// tests guard the surface: that turning it on does the obvious thing, that
/// every edit is one undo step, that the next stroke carries what the bar
/// shows, and that a document which never used it writes no key.
/// </remarks>
public class SymmetryControlsTests : BrushStateIsolated
{
    private const int W = 400;
    private const int H = 300;

    private static MainViewModel Vm()
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("controls", W, H, 12, 72, "#ffffff", false));
        vm.SmoothStrokes = false;
        return vm;
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static void DrawShortStroke(MainViewModel vm)
    {
        vm.BeginStroke(60, 120, 1);
        vm.MoveStroke(90, 140, 0.9);
        vm.EndStroke();
        Pump();
    }

    [AvaloniaFact]
    public void TurningSymmetryOnPlacesAVerticalMirrorAtTheCentreOfThePageAsOneUndoStep()
    {
        var vm = Vm();
        Assert.Null(vm.Doc.Scene.Symmetry);
        Assert.False(vm.SymmetryEnabled);

        vm.SymmetryEnabled = true;

        var axis = Assert.IsType<SymmetryAxis>(vm.Doc.Scene.Symmetry);
        Assert.Equal(W / 2.0, axis.CenterX);
        Assert.Equal(H / 2.0, axis.CenterY);
        Assert.Equal(90, axis.AngleDeg);
        Assert.Equal(1, axis.Order);
        Assert.True(axis.Mirror);
        Assert.Same(axis, vm.ActiveSymmetry);
        Assert.Equal(2, vm.SymmetryCopyCount);

        // Placing the axis changed the document, so it is a step — and taking
        // it back leaves nothing on the scene and the toggle reading off.
        vm.UndoCommand.Execute(null);
        Assert.Null(vm.Doc.Scene.Symmetry);
        Assert.False(vm.SymmetryEnabled);
        Assert.Null(vm.ActiveSymmetry);
    }

    /// <summary>
    /// B415. A drag moves the axis in place; after a structural undo swaps the
    /// document, undoing the drag moves a copy back. Redoing the placement must
    /// put back the axis as undo took it out, not the dragged original.
    /// </summary>
    [AvaloniaFact]
    public void RedoingTheAxisPlacementPutsItWhereItWasPlaced()
    {
        var vm = Vm();
        vm.SymmetryEnabled = true;
        vm.BeginSymmetryDrag();
        vm.DragSymmetryCentreBy(20, 0);
        vm.EndSymmetryDrag();

        vm.PanelEditor.Perform(d => d.Scene.Layers[0].Name += "!", "Rename");
        vm.UndoCommand.Execute(null); // the rename: the document is swapped
        vm.UndoCommand.Execute(null); // the drag
        vm.UndoCommand.Execute(null); // the placement
        vm.RedoCommand.Execute(null);

        Assert.Equal(W / 2.0, vm.Doc.Scene.Symmetry!.CenterX, 6);
        vm.RedoCommand.Execute(null);
        Assert.Equal(W / 2.0 + 20, vm.Doc.Scene.Symmetry!.CenterX, 6);
    }

    [AvaloniaFact]
    public void TheAxisCanBeDraggedAndTurned()
    {
        var vm = Vm();
        vm.SymmetryEnabled = true;
        var fired = 0;
        vm.SymmetryChanged += () => fired++;

        // One gesture: take the centre, move it, take the handle, turn it.
        vm.BeginSymmetryDrag();
        vm.DragSymmetryCentreBy(10, -5);
        vm.DragSymmetryCentreBy(10, -5);
        vm.EndSymmetryDrag();

        var axis = vm.Doc.Scene.Symmetry!;
        Assert.Equal(W / 2.0 + 20, axis.CenterX, 6);
        Assert.Equal(H / 2.0 - 10, axis.CenterY, 6);
        Assert.True(fired >= 2, "the canvas was not told the axis moved");

        vm.BeginSymmetryDrag();
        // The rotate handle dragged to the right of the centre is 0°; with
        // Shift it snaps, so a point just off the diagonal lands on 45.
        vm.DragSymmetryAngleTowards(axis.CenterX + 50, axis.CenterY + 48, snap: true);
        vm.EndSymmetryDrag();
        Assert.Equal(45, axis.AngleDeg, 6);

        // Each drag is ONE step, however many pointer events it took.
        vm.UndoCommand.Execute(null);
        Assert.Equal(90, axis.AngleDeg, 6);
        Assert.Equal(W / 2.0 + 20, axis.CenterX, 6);
        vm.UndoCommand.Execute(null);
        Assert.Equal(W / 2.0, axis.CenterX, 6);
        Assert.Equal(H / 2.0, axis.CenterY, 6);
        // And the undo reached the controls without a setter being touched.
        Assert.Equal(90, vm.SymmetryAngleDeg, 6);
    }

    [AvaloniaFact]
    public void TheFieldsEditTheAxisInPlaceAndUndoReachesThem()
    {
        var vm = Vm();
        vm.SymmetryEnabled = true;
        var axis = vm.Doc.Scene.Symmetry!;

        vm.SymmetryOrder = 6;
        vm.SymmetryMirror = false;
        Assert.Equal(6, axis.Order);
        Assert.False(axis.Mirror);
        Assert.Same(axis, vm.ActiveSymmetry);
        Assert.Equal(6, vm.SymmetryCopyCount);

        // Clamped, never thrown: the field's bounds are the record's.
        vm.SymmetryOrder = 99;
        Assert.Equal(MainViewModel.MaxSymmetryOrder, axis.Order);

        var fired = 0;
        vm.SymmetryChanged += () => fired++;
        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        Assert.Equal(6, vm.SymmetryOrder);
        Assert.True(vm.SymmetryMirror);
        Assert.True(fired >= 1, "an undo moved the axis and the canvas was not told");
    }

    [AvaloniaFact]
    public void TheNextStrokeCarriesTheAxisAndTurningItOffDoesNotReachIt()
    {
        var vm = Vm();
        vm.SymmetryEnabled = true;
        vm.SymmetryOrder = 3;

        DrawShortStroke(vm);
        var painted = vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame!.Strokes.Last();
        var carried = Assert.IsType<SymmetryAxis>(painted.Symmetry);
        Assert.Equal(3, carried.Order);
        Assert.True(carried.Mirror);
        // A clone, not the scene's object — turning the axis later must not
        // reach art already made (Q15, invariant 4).
        Assert.NotSame(vm.Doc.Scene.Symmetry, carried);

        vm.SymmetryEnabled = false;
        Assert.NotNull(vm.Doc.Scene.Symmetry);
        Assert.Equal(3, painted.Symmetry!.Order);

        DrawShortStroke(vm);
        Assert.Null(vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame!.Strokes.Last().Symmetry);

        // Back on: the axis is still where it was left, not a fresh one.
        vm.SymmetryEnabled = true;
        Assert.Equal(3, vm.SymmetryOrder);
    }

    [AvaloniaFact]
    public void ASceneThatNeverPlacedAnAxisWritesNoSymmetryKeyAndOneThatDidSurvivesReopening()
    {
        var vm = Vm();
        DrawShortStroke(vm);
        Assert.DoesNotContain("\"symmetry\"", vm.SerializeDocument());

        vm.SymmetryEnabled = true;
        vm.SymmetryAngleDeg = 30;
        var json = vm.SerializeDocument();
        Assert.Contains("\"symmetry\"", json);

        var reopened = Vm();
        reopened.OpenDocumentTab(DocJson.Deserialize(json), null);
        var axis = Assert.IsType<SymmetryAxis>(reopened.Doc.Scene.Symmetry);
        Assert.Equal(30, axis.AngleDeg);
        // Kept, but not in use: reopening never starts reflecting marks the
        // artist has not asked to reflect.
        Assert.False(reopened.SymmetryEnabled);
    }

    [AvaloniaFact]
    public void SwitchingToADocumentWithoutAnAxisReadsOffAndPlacesNothing()
    {
        var vm = Vm();
        vm.SymmetryEnabled = true;
        // The view model opens on a blank startup tab, so "the first" is the
        // one Vm() made, not Tabs[0].
        var first = vm.ActiveTab!;

        vm.NewDocument(new NewDocumentSettings("plain", W, H, 12, 72, "#ffffff", false));
        Assert.False(vm.SymmetryEnabled);
        Assert.Null(vm.Doc.Scene.Symmetry);
        Assert.Equal(0, vm.ActiveTab!.Editor.Revision);

        // And back to the first: its axis is still there and in use again.
        vm.ActiveTab = first;
        Assert.True(vm.SymmetryEnabled);
        Assert.Same(vm.Doc.Scene.Symmetry, vm.ActiveSymmetry);
    }

    /// <summary>
    /// Undoing something unrelated must not switch symmetry off, and must not
    /// strand the axis's own undo steps.
    /// </summary>
    /// <remarks>
    /// Adding a layer is a snapshot step: undoing it swaps the whole document
    /// for a clone, so the scene's axis becomes a different object with the
    /// same numbers. The first version compared references and read that as
    /// "the axis was undone" — symmetry went off under the artist for undoing
    /// a layer — and its delta steps had captured the old object, so the next
    /// undo wrote into an orphan and moved nothing. The adversary found both.
    /// </remarks>
    [AvaloniaFact]
    public void AnUnrelatedUndoKeepsSymmetryOnAndItsOwnStepsStillWork()
    {
        var vm = Vm();
        vm.SymmetryEnabled = true;
        var cx = vm.Doc.Scene.Symmetry!.CenterX;

        vm.BeginSymmetryDrag();
        vm.DragSymmetryCentreBy(30, 0);
        vm.EndSymmetryDrag();
        vm.SymmetryOrder = 4;

        var layers = vm.Doc.Scene.Layers.Count;
        vm.AddPaintedLayerCommand.Execute(null);
        Assert.Equal(layers + 1, vm.Doc.Scene.Layers.Count);
        vm.UndoCommand.Execute(null);
        Assert.Equal(layers, vm.Doc.Scene.Layers.Count);

        // Still on, and on THIS document's axis.
        Assert.True(vm.SymmetryEnabled);
        Assert.Same(vm.Doc.Scene.Symmetry, vm.ActiveSymmetry);
        Assert.Equal(4, vm.SymmetryOrder);
        Assert.Equal(cx + 30, vm.Doc.Scene.Symmetry!.CenterX, 6);

        // And the axis's own steps undo against the live scene, not an orphan.
        vm.UndoCommand.Execute(null);
        Assert.Equal(1, vm.Doc.Scene.Symmetry!.Order);
        Assert.Equal(1, vm.SymmetryOrder);
        vm.UndoCommand.Execute(null);
        Assert.Equal(cx, vm.Doc.Scene.Symmetry!.CenterX, 6);
        Assert.True(vm.SymmetryEnabled);
    }

    [AvaloniaFact]
    public void ReplacingTheDocumentFollowsTheNewScene()
    {
        var vm = Vm();
        vm.SymmetryEnabled = true;
        var stale = vm.ActiveSymmetry;

        // The MCP surface and tests open files this way, without a tab switch.
        var plain = Lightbox.Core.Documents.DocumentFactory.CreateDoc(W, H, 12, "#ffffff");
        vm.ReplaceDocument(plain);
        Assert.False(vm.SymmetryEnabled);
        Assert.Null(vm.ActiveSymmetry);

        var withAxis = Lightbox.Core.Documents.DocumentFactory.CreateDoc(W, H, 12, "#ffffff");
        withAxis.Scene.Symmetry = new SymmetryAxis { CenterX = 10, CenterY = 20, AngleDeg = 45, Order = 2, Mirror = true };
        vm.ReplaceDocument(withAxis);
        Assert.True(vm.SymmetryEnabled);
        Assert.Same(withAxis.Scene.Symmetry, vm.ActiveSymmetry);
        Assert.NotSame(stale, vm.ActiveSymmetry);
    }

    [Fact]
    public void TheToggleIsInTheShortcutMapAndTheQuickBarCatalogue()
    {
        var map = new ShortcutMap();
        var def = Assert.Single(map.Definitions, d => d.Id == "brush.symmetry");
        Assert.Equal("Tools", def.Category);
        Assert.NotNull(def.Current);
        Assert.Null(map.ConflictWith(def.Id, def.Current!));

        Assert.Contains(QuickBarCatalog.All, o => o.Id == QuickBarCatalog.BrushSymmetry);
        Assert.Contains(QuickBarCatalog.BrushSymmetry, QuickBarCatalog.ToolDefaults);
        Assert.True(WorkspaceViewModel.QuickNames.ContainsKey(QuickBarCatalog.BrushSymmetry));
    }
}

using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.Tests;

/// <summary>
/// Moving a <em>group</em> of anchors and collision shapes.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RigEditingTests"/> covers placing marks and dragging one. What these
/// guard is B110: the group path called <c>_editor.Perform</c> from inside the apply
/// and revert lambdas it handed to <c>_editor.PerformDelta</c>, and <c>Perform</c>
/// pushes a snapshot step of its own. One drag therefore pushed two entries — the
/// delta step, then a snapshot on top of it — so the first undo popped the snapshot
/// and looked right, and the second popped the delta, ran <c>revert</c>, and nested
/// another <c>Perform</c>: mutating the document and writing history while the undo
/// system was replaying.
/// </para>
/// <para>
/// The trap is that <em>one</em> undo restored the marks in the broken version too,
/// which is why it survived review. So these follow the idiom
/// <see cref="RigEditingTests.EveryRigEditIsOneUndoStep"/> uses and keep undoing
/// past the drag: one step back must reach the positions, and the next must reach
/// the <em>declaration</em>. Against the nested version the second undo ran the
/// revert instead and moved the marks a second time, leaving them declared.
/// </para>
/// <para>
/// Anchors and collision boxes are a game-export deliverable, so history corrupted
/// here ships into sprite data rather than staying on screen.
/// </para>
/// </remarks>
public class RigMarkEditTests
{
    private static MainViewModel OnOnes(int frames = 4)
    {
        var vm = new MainViewModel(artist: null) { SmoothStrokes = false };
        vm.NewDocument(new NewDocumentSettings("Rig", 200, 160, 12, 72, "#ffffff", false));
        while (vm.Doc.Scene.FrameCount < frames) vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 0;
        vm.RigEditMode = true;
        return vm;
    }

    private static (string A, string B) TwoAnchors(MainViewModel vm)
    {
        var a = vm.AddAnchorAt("hand", 40, 40);
        var b = vm.AddAnchorAt("foot", 90, 120);
        vm.Selection.SelectAnchor(a);
        vm.Selection.AddAnchorToSelection(b);
        return (a, b);
    }

    private static (string A, string B) TwoBoxes(MainViewModel vm)
    {
        var a = vm.AddShapeAt("body", 20, 20, 40, 60);
        var b = vm.AddShapeAt("head", 80, 10, 30, 30);
        vm.Selection.SelectShape(a);
        vm.Selection.AddShapeToSelection(b);
        return (a, b);
    }

    private static AnchorPoint AnchorAt(MainViewModel vm, string id) =>
        Anchors.ResolvedAt(vm.Doc.Scene, vm.CurrentFrameIndex)[id];

    private static ShapeBox BoxAt(MainViewModel vm, string id) =>
        CollisionShapes.ResolvedAt(vm.Doc.Scene, vm.CurrentFrameIndex)[id];

    private static int Declared(MainViewModel vm) => vm.Doc.Scene.Anchors?.Count ?? 0;

    private static int DeclaredShapes(MainViewModel vm) => vm.Doc.Scene.Shapes?.Count ?? 0;

    // ---- anchors ------------------------------------------------------------------

    [AvaloniaFact]
    public void MovingAGroupOfAnchorsIsOneUndoStep()
    {
        var vm = OnOnes();
        var (a, b) = TwoAnchors(vm);

        // Several pointer events, because the accumulate-then-commit shape is what
        // makes a long drag one step rather than fifty.
        vm.BeginAnchorsMove();
        vm.UpdateAnchorsMove(6, 4);
        vm.UpdateAnchorsMove(6, 4);
        vm.EndAnchorsMove();

        Assert.Equal(new AnchorPoint(52, 48), AnchorAt(vm, a));
        Assert.Equal(new AnchorPoint(102, 128), AnchorAt(vm, b));

        vm.UndoCommand.Execute(null);
        Assert.Equal(new AnchorPoint(40, 40), AnchorAt(vm, a));
        Assert.Equal(new AnchorPoint(90, 120), AnchorAt(vm, b));

        // The assertion B110 fails on. One more undo must reach the second
        // declaration; against the nested Perform it ran the drag's revert again,
        // so both anchors were still declared and had moved a second time.
        vm.UndoCommand.Execute(null);
        Assert.Equal(1, Declared(vm));
    }

    [AvaloniaFact]
    public void UndoingAnAnchorGroupMovePutsThemAllBack()
    {
        var vm = OnOnes();
        var (a, b) = TwoAnchors(vm);

        vm.BeginAnchorsMove();
        vm.UpdateAnchorsMove(15, -10);
        vm.EndAnchorsMove();
        vm.UndoCommand.Execute(null);

        // Every one of them, not just the last: the whole point of a group move.
        Assert.Equal(new AnchorPoint(40, 40), AnchorAt(vm, a));
        Assert.Equal(new AnchorPoint(90, 120), AnchorAt(vm, b));

        // And the stack is still walkable afterwards, rather than holding a step
        // that rewrites the document when it is popped.
        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        Assert.Equal(0, Declared(vm));
    }

    [AvaloniaFact]
    public void RedoingAnAnchorGroupMoveReappliesItOnce()
    {
        var vm = OnOnes();
        var (a, _) = TwoAnchors(vm);

        vm.BeginAnchorsMove();
        vm.UpdateAnchorsMove(12, 8);
        vm.EndAnchorsMove();
        vm.UndoCommand.Execute(null);
        vm.RedoCommand.Execute(null);

        // Once, not twice. `apply` is re-run on redo, and it reads the document and
        // adds to what it finds, so a redo that fired twice would compound.
        Assert.Equal(new AnchorPoint(52, 48), AnchorAt(vm, a));
    }

    [AvaloniaFact]
    public void AnAnchorGroupDragThatWentNowhereIsNotAnEdit()
    {
        var vm = OnOnes();
        TwoAnchors(vm);

        vm.BeginAnchorsMove();
        vm.EndAnchorsMove();

        // The press that did not move must not spend a step, so the first undo
        // still reaches the declaration.
        vm.UndoCommand.Execute(null);
        Assert.Equal(1, Declared(vm));
    }

    // ---- collision shapes ---------------------------------------------------------

    [AvaloniaFact]
    public void MovingAGroupOfCollisionBoxesIsOneUndoStep()
    {
        var vm = OnOnes();
        var (a, b) = TwoBoxes(vm);

        vm.BeginShapesMove();
        vm.UpdateShapesMove(5, 5);
        vm.UpdateShapesMove(5, 5);
        vm.EndShapesMove();

        Assert.Equal(new ShapeBox(30, 30, 40, 60), BoxAt(vm, a));
        Assert.Equal(new ShapeBox(90, 20, 30, 30), BoxAt(vm, b));

        vm.UndoCommand.Execute(null);
        Assert.Equal(new ShapeBox(20, 20, 40, 60), BoxAt(vm, a));

        vm.UndoCommand.Execute(null);
        Assert.Equal(1, DeclaredShapes(vm));
    }

    [AvaloniaFact]
    public void UndoingACollisionBoxGroupMovePutsThemAllBack()
    {
        var vm = OnOnes();
        var (a, b) = TwoBoxes(vm);

        vm.BeginShapesMove();
        vm.UpdateShapesMove(-8, 12);
        vm.EndShapesMove();
        vm.UndoCommand.Execute(null);

        Assert.Equal(new ShapeBox(20, 20, 40, 60), BoxAt(vm, a));
        Assert.Equal(new ShapeBox(80, 10, 30, 30), BoxAt(vm, b));
    }

    [AvaloniaFact]
    public void MovingACollisionBoxLeavesItsSizeAlone()
    {
        var vm = OnOnes();
        var (a, _) = TwoBoxes(vm);

        vm.BeginShapesMove();
        vm.UpdateShapesMove(20, 30);
        vm.EndShapesMove();

        // A move is two numbers, not four. Resizing is the corner drag, and a box
        // that quietly changed size while being positioned would change what the
        // exporter calls a hurtbox.
        var box = BoxAt(vm, a);
        Assert.Equal(40, box.W);
        Assert.Equal(60, box.H);
    }
}

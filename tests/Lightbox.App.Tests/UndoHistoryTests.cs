using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.Tests;

/// <summary>
/// The undo record as the history panel reads it: named steps, an enumerable
/// timeline, and jump-to-state. Lives beside <see cref="DirtyRevisionTests"/>
/// because it guards the same machinery from the other side — that one asks
/// "does the record know what changed", this one asks "can the record be
/// walked and read".
/// </summary>
public sealed class UndoHistoryTests(ITestOutputHelper output)
{
    private static DocumentEditor Editor() => new(DocumentFactory.CreateDoc(64, 64, 12));

    private static void AddStroke(DocumentEditor editor, string? label = null)
    {
        var stroke = new Stroke { Points = [new(1, 1, 0.5), new(30, 30, 0.5)] };
        var frame = editor.Doc.Scene.Layers[^1].Cels[0].Frame!;
        editor.PerformDelta(
            d => d.Scene.Layers[^1].Cels[0].Frame!.Strokes.Add(stroke),
            d => d.Scene.Layers[^1].Cels[0].Frame!.Strokes.Remove(stroke),
            affectedFrameId: frame.Id, label: label);
    }

    private static void RenameScene(DocumentEditor editor, string name) =>
        editor.Perform(d => d.Scene.Name = name);

    // ---- names --------------------------------------------------------------

    [Fact]
    public void AStepIsNamedAfterItsCallerUnlessLabelled()
    {
        var editor = Editor();
        RenameScene(editor, "Shot 12");
        AddStroke(editor, label: "Stroke");

        var history = editor.History;
        output.WriteLine(string.Join("\n", history.Select(h => $"{h.Revision}: {h.Label}")));
        Assert.Equal(2, history.Count);
        // CallerMemberName, humanized — no call site had to say anything.
        Assert.Equal("Rename scene", history[0].Label);
        // The explicit label wins where the method name would read badly.
        Assert.Equal("Stroke", history[1].Label);
    }

    // ---- the timeline -------------------------------------------------------

    [Fact]
    public void UndoneStepsStayInTheHistoryMarkedAsAhead()
    {
        var editor = Editor();
        AddStroke(editor, "first");
        AddStroke(editor, "second");
        AddStroke(editor, "third");
        editor.Undo();
        editor.Undo();

        var history = editor.History;
        Assert.Equal(3, history.Count);
        Assert.Equal(["first", "second", "third"], history.Select(h => h.Label));
        Assert.Equal([false, true, true], history.Select(h => h.IsUndone));
        // Chronological even on the redo side: what redo would replay first
        // comes first.
        Assert.True(history[1].Revision < history[2].Revision);
    }

    // ---- jumping ------------------------------------------------------------

    [Fact]
    public void NavigatingTheHistoryRestoresTheState()
    {
        var editor = Editor();
        RenameScene(editor, "one");
        var target = editor.Revision;
        RenameScene(editor, "two");
        RenameScene(editor, "three");
        Assert.Equal("three", editor.Doc.Scene.Name);

        editor.JumpTo(target);
        Assert.Equal("one", editor.Doc.Scene.Name);
        Assert.Equal(target, editor.Revision);

        // Forward again, across two steps, without having drawn anything new.
        editor.JumpTo(editor.History[^1].Revision);
        Assert.Equal("three", editor.Doc.Scene.Name);
    }

    [Fact]
    public void JumpingToZeroIsTheDocumentAsOpened()
    {
        var editor = Editor();
        var original = editor.Doc.Scene.Name;
        RenameScene(editor, "changed");
        RenameScene(editor, "changed again");

        editor.JumpTo(0);
        Assert.Equal(original, editor.Doc.Scene.Name);
        Assert.Equal(0, editor.Revision);
        Assert.False(editor.HistoryTrimmed);
    }

    [Fact]
    public void JumpReportsOneFrameWhenEveryStepAgreed()
    {
        var editor = Editor();
        var frameId = editor.Doc.Scene.Layers[^1].Cels[0].Frame!.Id;
        AddStroke(editor, "a");
        AddStroke(editor, "b");

        var scope = editor.JumpTo(0);
        Assert.True(scope.Any);
        // Both steps touched the same frame, so the caller invalidates one
        // frame — the reason JumpTo merges scopes instead of dropping them.
        Assert.Equal(frameId, scope.FrameId);

        var mixed = editor.JumpTo(editor.History[^1].Revision);
        Assert.True(mixed.Any);
        Assert.Equal(frameId, mixed.FrameId);

        RenameScene(editor, "structural");
        var wide = editor.JumpTo(0);
        Assert.True(wide.DocumentWide);
    }

    [Fact]
    public void JumpingNowhereTouchesNothing()
    {
        var editor = Editor();
        AddStroke(editor, "only");
        var scope = editor.JumpTo(editor.Revision);
        Assert.False(scope.Any);
    }

    // ---- trimming stays honest ----------------------------------------------

    [Fact]
    public void ATrimmedHistorySaysSoAndJumpStopsAtTheOldestStep()
    {
        var editor = Editor();
        editor.MaxUndo = 3;
        for (var i = 1; i <= 5; i++) AddStroke(editor, $"s{i}");

        Assert.True(editor.HistoryTrimmed);
        var history = editor.History;
        Assert.Equal(3, history.Count);
        Assert.Equal("s3", history[0].Label);

        // The jump can only unwind what the stack still holds: the two trimmed
        // strokes stay applied. This is exactly what holding Ctrl+Z has always
        // done — and it is why the panel offers no "as opened" row once
        // HistoryTrimmed is true: a row for revision zero would promise a
        // state undo can no longer reach.
        editor.JumpTo(0);
        Assert.Equal(2, editor.Doc.Scene.Layers[^1].Cels[0].Frame!.Strokes.Count);
        Assert.All(editor.History, h => Assert.True(h.IsUndone));
        output.WriteLine($"unwound to revision {editor.Revision} with the trimmed strokes still applied");
    }
}

/// <summary>
/// The docker's view model against a real painting view model: rows follow
/// the active document, a jump moves the drawing, and a tab switch re-attaches.
/// </summary>
[Collection("BrushState")]
public sealed class UndoHistoryPanelTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel Vm()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        return vm;
    }

    private static void Paint(MainViewModel vm, double x = 20)
    {
        vm.BeginStroke(x, 20, 0.5);
        vm.MoveStroke(x + 30, 60, 0.5);
        vm.EndStroke();
    }

    [AvaloniaFact]
    public void TheRowsFollowThePaintingAndAJumpMovesIt()
    {
        var vm = Vm();
        Paint(vm, 20);
        Paint(vm, 80);

        var rows = vm.UndoHistory.Rows;
        output.WriteLine(string.Join("\n", rows.Select(r => $"{r.Revision} {r.Label} current={r.IsCurrent}")));
        Assert.Equal(3, rows.Count);
        Assert.Equal("As opened", rows[0].Label);
        Assert.Equal("Stroke", rows[1].Label);
        Assert.True(rows[2].IsCurrent);

        // Jump to before everything, through the view model, the way a
        // double-tap does — the drawing empties and the rows dim ahead.
        vm.UndoHistory.Jump(rows[0]);
        Assert.Equal(0, vm.ActiveTab!.Editor.Revision);
        Assert.All(vm.UndoHistory.Rows.Where(r => r.Revision > 0), r => Assert.True(r.IsAhead));

        // And forward again: both strokes come back without repainting them.
        vm.UndoHistory.Jump(vm.UndoHistory.Rows[^1]);
        Assert.Equal(2, vm.UndoHistory.Rows.Count(r => !r.IsAhead && r.Revision > 0));
    }

    [AvaloniaFact]
    public void SwitchingTabsSwitchesTheHistory()
    {
        var vm = Vm();
        Paint(vm);
        Assert.Equal(2, vm.UndoHistory.Rows.Count);

        vm.NewDocument(new NewDocumentSettings("Second", 320, 240, 12, 72, "#ffffff", true));
        // A fresh document: nothing but the root row.
        var fresh = vm.UndoHistory.Rows;
        Assert.Single(fresh);
        Assert.Equal("As opened", fresh[0].Label);
    }

    /// <summary>
    /// The Edit menu's two entries name the step they would act on, and are
    /// dead at the ends of the stack.
    /// </summary>
    /// <remarks>
    /// The record has carried a label per step since this docker landed; the
    /// menu reads the top of it. Guarded because the failure is quiet — a
    /// header that stopped following the stack would still render, still say
    /// "Undo", and simply describe the wrong step, which is worse than saying
    /// nothing.
    /// </remarks>
    [AvaloniaFact]
    public void TheEditMenuEntriesNameTheStepAndDieAtTheEndsOfTheStack()
    {
        var vm = Vm();
        output.WriteLine($"fresh: {vm.UndoMenuHeader} / {vm.RedoMenuHeader}");
        Assert.False(vm.CanUndo);
        Assert.False(vm.CanRedo);
        // Nothing to name, so neither entry pretends to name something.
        Assert.Equal("_Undo", vm.UndoMenuHeader);
        Assert.Equal("_Redo", vm.RedoMenuHeader);

        Paint(vm);
        output.WriteLine($"after a stroke: {vm.UndoMenuHeader} / {vm.RedoMenuHeader}");
        Assert.True(vm.CanUndo);
        Assert.Equal("_Undo Stroke", vm.UndoMenuHeader);
        Assert.False(vm.CanRedo);

        vm.UndoCommand.Execute(null);
        output.WriteLine($"after undo: {vm.UndoMenuHeader} / {vm.RedoMenuHeader}");
        // The step moves across: nothing left to undo, and it is what redo
        // would now put back.
        Assert.False(vm.CanUndo);
        Assert.True(vm.CanRedo);
        Assert.Equal("_Redo Stroke", vm.RedoMenuHeader);
    }

    /// <summary>
    /// The headers follow a tab switch, which no edit announces.
    /// </summary>
    /// <remarks>
    /// A tab switch swaps the whole editor, and nothing raises <c>Changed</c>
    /// on the way in — so a menu that only listened to the edit funnel would
    /// keep describing the document you just left. The failure is invisible
    /// until somebody presses the entry.
    /// </remarks>
    [AvaloniaFact]
    public void TheHeadersFollowATabSwitch()
    {
        var vm = Vm();
        Paint(vm);
        Assert.Equal("_Undo Stroke", vm.UndoMenuHeader);

        vm.NewDocument(new Lightbox.App.ViewModels.NewDocumentSettings(
            "Second", 400, 300, 12, 72, Lightbox.Core.Documents.Scene.DefaultBackgroundColor, false));

        output.WriteLine($"on the new tab: {vm.UndoMenuHeader}, canUndo={vm.CanUndo}");
        Assert.False(vm.CanUndo);
        Assert.Equal("_Undo", vm.UndoMenuHeader);
    }
}

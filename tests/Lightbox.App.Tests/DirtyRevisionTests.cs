using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.Tests;

/// <summary>
/// <b>B98.</b> Dirtiness is derived from the edit record, not asserted by
/// whichever code path ran.
/// </summary>
/// <remarks>
/// <para>
/// The bug behind B79, B94, B95, B96 and B97. <c>IsDirty</c> was a flag each
/// edit path set for itself, which failed in both directions at once: paths
/// that changed nothing raised it, and no path could lower it when an edit was
/// undone. Four spurious setters turned up in one week, which is the signal
/// that the next one was already being written.
/// </para>
/// <para>
/// These tests are about the <em>rule</em> rather than about any one of those
/// symptoms — each symptom has its own entry. What is guarded here is that the
/// question "does this differ from disk" is answered by the undo record.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class DirtyRevisionTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel Vm() => new(null) { SmoothStrokes = false };

    private static void Paint(MainViewModel vm, double x = 20)
    {
        vm.BeginStroke(x, 20, 0.5);
        vm.MoveStroke(x + 30, 60, 0.5);
        vm.EndStroke();
    }

    /// <summary>Undoing back to the saved state clears the badge.</summary>
    /// <remarks>
    /// <b>B97</b>, and the one a boolean flag could not express at all: the
    /// reporter added a reference, saw the badge correctly, removed it again and
    /// the badge stayed. Nothing was wrong with the clearing — the flag had no
    /// way to say <em>back where it started</em>.
    /// </remarks>
    [AvaloniaFact]
    public void UndoingBackToTheSavedStateClearsTheBadge()
    {
        var vm = Vm();
        var tab = vm.Tabs[0];
        vm.NotifySaved("/somewhere/knight.lightbox.json");
        Assert.False(tab.IsDirty);

        Paint(vm);
        output.WriteLine($"after a stroke: dirty={tab.IsDirty}");
        Assert.True(tab.IsDirty);

        vm.UndoCommand.Execute(null);
        output.WriteLine($"after undo:     dirty={tab.IsDirty}");
        Assert.False(tab.IsDirty);

        // And redo puts it back, rather than the badge sticking off.
        vm.RedoCommand.Execute(null);
        Assert.True(tab.IsDirty);
    }

    /// <summary>
    /// Redoing to a state that was saved reads as saved, not as new work.
    /// </summary>
    /// <remarks>
    /// The undo stack keeps a step's original revision when it moves back from
    /// redo. Handing out a fresh number there would make "undo, then redo" leave
    /// a document that is byte-identical to the file looking unsaved.
    /// </remarks>
    [AvaloniaFact]
    public void RedoingToTheSavedStateIsStillSaved()
    {
        var vm = Vm();
        var tab = vm.Tabs[0];
        Paint(vm);
        vm.NotifySaved("/somewhere/knight.lightbox.json");
        Assert.False(tab.IsDirty);

        vm.UndoCommand.Execute(null);
        Assert.True(tab.IsDirty);
        vm.RedoCommand.Execute(null);
        output.WriteLine($"undo then redo: dirty={tab.IsDirty}");
        Assert.False(tab.IsDirty);
    }

    /// <summary>
    /// Trimming the undo stack must not make a changed document look clean.
    /// </summary>
    /// <remarks>
    /// <b>The trap this design exists to avoid.</b> <see cref="DocumentEditor.MaxUndo"/>
    /// drops the *oldest* step, so a dirty check written as "is the stack deeper
    /// than it was at save" reads clean forever once the stack is full. Silent,
    /// and it loses work. Driven directly against the editor rather than through
    /// the view model, because sixty-five strokes through the paint pipeline is a
    /// slow way to assert an off-by-one.
    /// </remarks>
    [AvaloniaFact]
    public void TrimmingTheUndoStackDoesNotFakeACleanDocument()
    {
        var editor = new DocumentEditor(DocumentFactory.CreateDoc(64, 64, 4)) { MaxUndo = 8 };
        for (var i = 0; i < 8; i++) editor.Perform(d => d.Scene.Fps++);

        var full = editor.Revision;
        // The stack is now at its limit, so every further edit trims one off the
        // bottom and the depth stops moving. The revision must not.
        for (var i = 0; i < 20; i++) editor.Perform(d => d.Scene.Fps++);

        // 8 -> 28 across twenty edits that left the depth pinned at MaxUndo.
        output.WriteLine($"revision {full} -> {editor.Revision} with depth pinned at {editor.MaxUndo}");
        Assert.NotEqual(full, editor.Revision);
        Assert.True(editor.Revision > full);
    }

    /// <summary>A document at the revision it was saved at is not dirty.</summary>
    [AvaloniaFact]
    public void ADocumentAtItsSavedRevisionIsNotDirty()
    {
        var vm = Vm();
        var tab = vm.Tabs[0];
        Paint(vm);
        Paint(vm, 80);
        vm.NotifySaved("/somewhere/knight.lightbox.json");

        Assert.False(tab.IsDirty);
        Assert.False(tab.IsUnsaved);
        Assert.False(tab.HasWorkToLose);
    }

    /// <summary>
    /// Choosing a brush never touches the document's badge.
    /// </summary>
    /// <remarks>
    /// <b>B96</b>, and <b>Q24</b> made structural rather than remembered.
    /// Selecting a preset marked the document unsaved while editing its settings
    /// — the thing Q24 actually forbids — did not. Neither can now, because a
    /// brush pushes no undo step and the badge reads the undo record.
    /// </remarks>
    [AvaloniaFact]
    public void ChoosingABrushDoesNotMarkTheDocument()
    {
        var vm = Vm();
        var tab = vm.Tabs[0];
        Paint(vm);
        vm.NotifySaved("/somewhere/knight.lightbox.json");
        Assert.False(tab.IsDirty);

        foreach (var preset in vm.BrushPresetChoices.Take(3))
        {
            vm.ApplyPreset(preset);
            output.WriteLine($"after choosing “{preset.Name}”: dirty={tab.IsDirty}");
            Assert.False(tab.IsDirty);
        }

        // And the settings themselves, which were already correct — asserted so
        // a later change cannot quietly make the two disagree again.
        vm.BrushSize = vm.BrushSize + 7;
        vm.BrushHardness = 0.42;
        Assert.False(tab.IsDirty);

        // The control: a real edit still raises it.
        Paint(vm, 90);
        Assert.True(tab.IsDirty);
    }

    /// <summary>
    /// A never-saved document says so, and does not claim work it does not have.
    /// </summary>
    /// <remarks>
    /// <b>B99.</b> The reporter found new documents silent — no badge, and
    /// closing them prompted for nothing even though nothing was on disk. Both
    /// halves are asserted here because they pull in opposite directions and a
    /// fix for one alone is how the other broke.
    /// </remarks>
    [AvaloniaFact]
    public void ANeverSavedDocumentBadgesButOnlyPromptsOnceDrawnIn()
    {
        var vm = Vm();
        var tab = vm.Tabs[0];

        Assert.True(tab.IsUnsaved);
        Assert.True(tab.IsDirty);            // nothing is on disk — say so
        Assert.False(tab.HasWorkToLose);     // …but there is nothing to mourn
        Assert.EndsWith("•", tab.DisplayTitle);

        Paint(vm);
        output.WriteLine($"after a stroke: unsaved={tab.IsUnsaved} lose={tab.HasWorkToLose}");
        Assert.True(tab.HasWorkToLose);      // now closing has to ask

        vm.NotifySaved("/somewhere/untitled.lightbox.json");
        Assert.False(tab.IsUnsaved);
        Assert.False(tab.IsDirty);
        Assert.False(tab.HasWorkToLose);
    }

    /// <summary>
    /// A sheet edit raises the badge on the sheet tab as well as its owner.
    /// </summary>
    /// <remarks>
    /// <b>B95's second half.</b> The reporter found the badge on the parent
    /// only. A reference tab is a view onto the owner's document, and its edits
    /// move its own editor rather than the owner's — so the owner has to be told
    /// the view exists, or a stroke drawn in a sheet leaves no mark on the badge
    /// at all.
    /// </remarks>
    [AvaloniaFact]
    public void ASheetEditRaisesTheBadgeOnBothTabs()
    {
        var vm = Vm();
        vm.AddReferenceSheet();
        vm.AddReferenceView(vm.ReferenceSheetsView[0]);
        var owner = vm.Tabs[0];
        var sheet = vm.ActiveTab!;
        Assert.Equal(DocumentTabKind.Reference, sheet.Kind);

        owner.MarkSaved();
        Assert.False(owner.IsDirty);
        Assert.False(sheet.IsDirty);

        Paint(vm);
        output.WriteLine($"after a sheet stroke: owner={owner.IsDirty} sheet={sheet.IsDirty}");
        Assert.True(owner.IsDirty);
        Assert.True(sheet.IsDirty);

        // Saving the owner clears both, because the sheet lives in its document.
        owner.MarkSaved();
        Assert.False(owner.IsDirty);
        Assert.False(sheet.IsDirty);
    }
}

using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.Core.Tests;

/// <summary>
/// The performance lab's view into an edit (Q209): every undoable step is timed
/// by its history label, split where the cost can hide — the whole-document
/// snapshot, the change itself, and everything listening.
/// </summary>
[Collection("EditMeasure")] // the hook is static: nothing else in this assembly may set it at the same time
public sealed class EditMeasureTests
{
    private sealed class Recorded(List<string> into, string entry) : IDisposable
    {
        public void Dispose() => into.Add(entry);
    }

    [Fact]
    public void AnEditIsTimedByItsLabel_InSnapshotApplyAndListeners()
    {
        var editor = new DocumentEditor(new Doc());
        var seen = new List<string>();
        var label = $"Measured {Guid.NewGuid():N}"; // other tests' edits may pass through the hook
        DocumentEditor.Measure = (name, l) => l == label ? new Recorded(seen, name) : null;
        try
        {
            editor.Perform(d => d.Scene.FrameCount += 1, label);
            editor.PerformDelta(d => d.Scene.FrameCount += 1, d => d.Scene.FrameCount -= 1, label: label);
        }
        finally
        {
            DocumentEditor.Measure = null;
        }

        // Disposed innermost first, so each part closes before the whole.
        Assert.Equal(
            ["edit.snapshot", "edit.apply", "edit.changed", "edit", "edit.apply", "edit.changed", "edit"],
            seen);
    }

    [Fact]
    public void WithNothingMeasuringAnEditStillWorks()
    {
        Assert.Null(DocumentEditor.Measure);
        var editor = new DocumentEditor(new Doc());
        var before = editor.Doc.Scene.FrameCount;
        editor.Perform(d => d.Scene.FrameCount += 1, "Add a frame");
        Assert.Equal(before + 1, editor.Doc.Scene.FrameCount);
    }
}

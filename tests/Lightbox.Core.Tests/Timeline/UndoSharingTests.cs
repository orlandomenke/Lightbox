using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Lightbox.Core.Timeline;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests.Timeline;

/// <summary>
/// An undo step stores only the drawings that changed
/// (docs/DESIGN-undo-shares-unchanged.md). A structural edit used to copy every
/// point of every drawing, about 14 MB a step on the owner-shaped document, for
/// a stack of 64.
/// </summary>
/// <remarks>
/// The fidelity half matters more than the memory half: what is shared has to
/// be frozen, because the live document is edited in place. So these do not
/// only measure; they edit in place between undos and redos and compare whole
/// serialized documents, as <see cref="UndoSnapshotFidelityTests"/> does.
/// </remarks>
public class UndoSharingTests(ITestOutputHelper output)
{
    private const int Drawings = 6;

    /// <summary>Six drawings of 4,000 points each: heavy enough that a copy of one shows.</summary>
    private static Doc Heavy()
    {
        var doc = DocumentFactory.CreateDoc(400, 300, Drawings);
        var layer = Drawn(doc);
        while (layer.Cels.Count < Drawings) layer.Cels.Add(new Cel { Frame = new Frame() });
        for (var f = 0; f < Drawings; f++)
        {
            var frame = layer.Cels[f].Frame ??= new Frame();
            for (var s = 0; s < 20; s++)
            {
                var stroke = new Stroke { Brush = new BrushSettings { Size = 8 }, Color = "#202020" };
                for (var p = 0; p < 200; p++) stroke.Points.Add(new StrokePoint(f * 3 + s + p * 0.5, s * 2 + p * 0.25, 0.5));
                frame.Strokes.Add(stroke);
            }
        }
        return doc;
    }

    private static Layer Drawn(Doc doc) => doc.Scene.Layers[^1];

    private static Frame DrawingAt(Doc doc, int i) => Drawn(doc).Cels[i].Frame!;

    private static long Allocated(Action act)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        act();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static string Shape(Doc doc) => DocJson.Serialize(doc);

    [Fact]
    public void AStructuralEditThatChangesNoDrawingStoresNoneOfThem()
    {
        var editor = new DocumentEditor(Heavy());
        var whole = Allocated(() => editor.Doc.Clone());
        editor.Perform(d => d.Scene.Layers[0].Name = "first", "Rename"); // the first step freezes every drawing once

        var rename = Allocated(() => editor.Perform(d => d.Scene.Layers[0].Name = "second", "Rename"));

        output.WriteLine($"a whole copy {whole / 1024} KB; a rename after it {rename / 1024} KB");
        Assert.True(rename < whole / 10, $"a rename stored {rename / 1024} KB of {whole / 1024} KB");
    }

    [Fact]
    public void AnEditThatChangesOneDrawingStoresAboutOne()
    {
        var editor = new DocumentEditor(Heavy());
        var whole = Allocated(() => editor.Doc.Clone());
        editor.Perform(d => d.Scene.Layers[0].Name = "start", "Rename");

        long total = 0;
        for (var i = 0; i < Drawings; i++)
        {
            var at = i;
            total += Allocated(() => editor.Perform(d =>
            {
                var points = DrawingAt(d, at).Strokes[0].Points;
                points[0] = points[0] with { X = points[0].X + 1 };
            }, "Nudge"));
        }

        var perEdit = total / Drawings;
        output.WriteLine($"a whole copy {whole / 1024} KB; an edit to one of {Drawings} drawings {perEdit / 1024} KB");
        Assert.True(perEdit < whole * 2 / Drawings, $"an edit to one drawing stored {perEdit / 1024} KB of {whole / 1024} KB");
    }

    /// <summary>
    /// Stroke commits are deltas that edit a drawing in place between snapshots.
    /// A snapshot after them has to see the drawing as it now is, not as it was
    /// last frozen.
    /// </summary>
    [Fact]
    public void DeltasBetweenSnapshotsAreNeitherLostNorLeaked()
    {
        var editor = new DocumentEditor(Heavy());
        var states = new List<string> { Shape(editor.Doc) };

        editor.Perform(d => d.Scene.Layers[0].Name = "a", "Rename");
        states.Add(Shape(editor.Doc));
        var extra = new Stroke { Color = "#ff0000", Points = [new StrokePoint(5, 5, 1), new StrokePoint(9, 9, 1)] };
        editor.PerformDelta(d => DrawingAt(d, 2).Strokes.Add(extra), d => DrawingAt(d, 2).Strokes.RemoveAll(x => x.Id == extra.Id), // by id, as the app's commit does
            affectedFrameId: DrawingAt(editor.Doc, 2).Id, label: "Stroke");
        states.Add(Shape(editor.Doc));
        editor.Perform(d => d.Scene.Layers[0].Name = "b", "Rename");
        states.Add(Shape(editor.Doc));
        editor.Perform(d => DrawingAt(d, 2).Strokes.RemoveAt(0), "Delete stroke");
        states.Add(Shape(editor.Doc));

        for (var i = states.Count - 2; i >= 0; i--)
        {
            editor.Undo();
            Assert.Equal(states[i], Shape(editor.Doc));
        }
        for (var i = 1; i < states.Count; i++)
        {
            editor.Redo();
            Assert.Equal(states[i], Shape(editor.Doc));
        }
    }

    /// <summary>
    /// After an undo the restored document is live and gets edited in place. If
    /// it shared anything with what the history holds, that edit would reach
    /// into the history and a later undo or redo would show it.
    /// </summary>
    [Fact]
    public void EditingTheRestoredDocumentInPlaceDoesNotReachIntoTheHistory()
    {
        var editor = new DocumentEditor(Heavy());
        var original = Shape(editor.Doc);
        editor.Perform(d => d.Scene.Layers[0].Name = "a", "Rename");
        var renamed = Shape(editor.Doc);

        editor.Undo();
        // In place, as a transform or a stroke commit would, on every drawing.
        for (var i = 0; i < Drawings; i++)
        {
            var points = DrawingAt(editor.Doc, i).Strokes[0].Points;
            for (var p = 0; p < points.Count; p++) points[p] = points[p] with { Y = points[p].Y + 100 };
        }
        DrawingAt(editor.Doc, 0).Strokes.Clear();

        editor.Redo();
        Assert.Equal(renamed, Shape(editor.Doc));
        editor.Undo();
        // The undo lands on the document the redo left: the in-place edit was
        // never a step, so it is gone with the document it was made to. What
        // matters is that the history's own two states never moved.
        editor.Redo();
        Assert.Equal(renamed, Shape(editor.Doc));
        Assert.NotEqual(original, renamed);
    }
}

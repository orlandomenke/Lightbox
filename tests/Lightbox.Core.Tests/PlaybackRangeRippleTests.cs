using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Lightbox.Core.Timeline;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// The playback range in the record (Q280): absent from a file that predates
/// it, and moved by the editor's row edits once per position.
/// </summary>
public class PlaybackRangeRippleTests
{
    [Fact]
    public void AFileFromBeforeTheRangeOpensWithNone()
    {
        // The sensitivity review's W8: a committed file, not a round trip of
        // today's model, is what proves an old document still opens unchanged.
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "grouped-v2.lightbox.json");

        var doc = DocJson.Deserialize(File.ReadAllText(path));

        Assert.Null(doc.Scene.PlaybackStart);
        Assert.Null(doc.Scene.PlaybackEnd);
    }

    /// <summary>Two drawing layers, six frames drawn on each.</summary>
    private static DocumentEditor TwoRows(out string a, out string b)
    {
        var doc = new Doc();
        doc.Scene.Layers.Add(new Layer { Name = "A" });
        doc.Scene.Layers.Add(new Layer { Name = "B" });
        doc.Scene.FrameCount = 6;
        foreach (var layer in doc.Scene.Layers)
        {
            for (var i = 0; i < 6; i++) layer.Cels.Add(new Cel { Frame = new Frame() });
        }
        a = doc.Scene.Layers[0].Id;
        b = doc.Scene.Layers[1].Id;
        return new DocumentEditor(doc);
    }

    [Fact]
    public void RunsOfDifferentLengthsAtOnePositionShiftByTheLongest()
    {
        // The adversary's case: one row inserts one cel at 3, the other two.
        // The scene grew by two there, so the range does — not by three.
        var editor = TwoRows(out var a, out var b);
        editor.Doc.Scene.PlaybackStart = 1;
        editor.Doc.Scene.PlaybackEnd = 5;

        editor.InsertHolds([(a, 3), (b, 3), (b, 4)]);

        Assert.Equal(1, editor.Doc.Scene.PlaybackStart);
        Assert.Equal(7, editor.Doc.Scene.PlaybackEnd);
    }

    [Fact]
    public void UndoOfAnEditThatMovedTheRangeMovesItBack()
    {
        var editor = TwoRows(out var a, out _);
        editor.Doc.Scene.PlaybackStart = 1;
        editor.Doc.Scene.PlaybackEnd = 3;

        editor.ExtendExposure(a, 3);
        Assert.Equal(4, editor.Doc.Scene.PlaybackEnd);
        editor.Undo();
        Assert.Equal(3, editor.Doc.Scene.PlaybackEnd);
        editor.Redo();
        Assert.Equal(4, editor.Doc.Scene.PlaybackEnd);
    }

    [Fact]
    public void UndoKeepsARangeSetSinceTheStep()
    {
        var editor = TwoRows(out var a, out _);
        editor.ExtendExposure(a, 5);

        // Set outside the history, as the view model does.
        editor.Doc.Scene.PlaybackStart = 2;
        editor.Doc.Scene.PlaybackEnd = 4;
        editor.Undo();

        Assert.Equal(2, editor.Doc.Scene.PlaybackStart);
        Assert.Equal(4, editor.Doc.Scene.PlaybackEnd);
    }

    [Fact]
    public void AnEditWithNoRangeLeavesNone()
    {
        var editor = TwoRows(out var a, out _);

        editor.ExtendExposure(a, 2);
        editor.DeleteCelsAcross([(a, 1)]);

        Assert.Null(editor.Doc.Scene.PlaybackStart);
        Assert.Null(editor.Doc.Scene.PlaybackEnd);
    }
}

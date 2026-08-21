using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.Core.Tests.Timeline;

public class ExposureEditingTests
{
    private static (DocumentEditor Editor, Layer A, Layer B) TwoLayerDoc()
    {
        var doc = DocumentFactory.CreateDoc(64, 64, 12);
        var b = new Layer
        {
            Name = "Top",
            Cels = [new Cel { Frame = new Frame() }],
        };
        doc.Scene.Layers.Add(b);
        var editor = new DocumentEditor(doc);
        return (editor, doc.Scene.Layers[0], b);
    }

    [Fact]
    public void ExtendExposure_InsertsAHold_OnThatLayerOnly()
    {
        var (editor, a, b) = TwoLayerDoc();
        editor.SetKeyAt(a.Id, 2, FrameRole.Key); // timeline now 3 frames
        var framesBeforeB = editor.Doc.Scene.Layers[1].Cels.Count;

        editor.ExtendExposure(a.Id, 0);

        a = editor.Doc.Scene.Layers[0];
        b = editor.Doc.Scene.Layers[1];
        Assert.Null(a.Cels[1].Frame);                       // new hold after the drawing
        Assert.NotNull(a.Cels[0].Frame);
        Assert.NotNull(a.Cels[3].Frame);                    // the index-2 key shifted right
        Assert.Equal(4, editor.Doc.Scene.FrameCount);       // timeline grew to fit
        Assert.Equal(framesBeforeB, b.Cels.Count);          // other layer untouched
        Assert.Same(ExposureSheet.ExposedFrame(a, 1), a.Cels[0].Frame); // the hold shows frame 0
    }

    [Fact]
    public void ReduceExposure_RemovesOnlyHolds()
    {
        var (editor, a, _) = TwoLayerDoc();
        editor.SetKeyAt(a.Id, 2, FrameRole.Key);            // cel 1 is a hold between two keys
        editor.ReduceExposure(a.Id, 0);
        a = editor.Doc.Scene.Layers[0];
        Assert.NotNull(a.Cels[1].Frame);                    // the key pulled left, not deleted

        // now cels 0 and 1 are both drawings — reducing must be a no-op
        var count = a.Cels.Count;
        editor.ReduceExposure(a.Id, 0);
        Assert.Equal(count, editor.Doc.Scene.Layers[0].Cels.Count);
    }

    [Fact]
    public void ClearCel_MakesAHold_ThatShowsThePreviousDrawing()
    {
        var (editor, a, _) = TwoLayerDoc();
        editor.SetKeyAt(a.Id, 1, FrameRole.Key);
        editor.ClearCel(a.Id, 1);

        a = editor.Doc.Scene.Layers[0];
        Assert.Null(a.Cels[1].Frame);
        Assert.Same(a.Cels[0].Frame, ExposureSheet.ExposedFrame(a, 1));

        // clearing a hold is a no-op, not an undo step
        var undoable = editor.CanUndo;
        editor.ClearCel(a.Id, 1);
        Assert.Equal(undoable, editor.CanUndo);
    }

    [Fact]
    public void SetFrameAt_ReplacesTheCel_AndExtendsTheTimeline()
    {
        var (editor, a, _) = TwoLayerDoc();
        var pasted = new Frame { Strokes = [new Stroke { Points = [new(1, 1, 1)] }] };

        editor.SetFrameAt(a.Id, 5, pasted);

        var scene = editor.Doc.Scene;
        Assert.Equal(6, scene.FrameCount);
        Assert.Same(pasted, scene.Layers[0].Cels[5].Frame);
        Assert.All(scene.Layers, l => Assert.True(l.Cels.Count >= 6)); // all layers padded
    }

    [Fact]
    public void MoveLayer_ReordersTheStack_AndClampsAtTheEdges()
    {
        var (editor, a, b) = TwoLayerDoc();
        editor.MoveLayer(a.Id, +1);
        Assert.Equal(b.Id, editor.Doc.Scene.Layers[0].Id);
        Assert.Equal(a.Id, editor.Doc.Scene.Layers[1].Id);

        // already on top — no-op
        editor.MoveLayer(a.Id, +1);
        Assert.Equal(a.Id, editor.Doc.Scene.Layers[1].Id);
    }

    [Fact]
    public void CloneFrame_DeepClones_WithAFreshId()
    {
        var src = new Frame
        {
            PngBase64 = "abc",
            Strokes = [new Stroke { Points = [new(3, 4, 1)] }],
        };
        var clone = (Frame)DocumentEditor.CloneFrame(src)!;
        Assert.NotEqual(src.Id, clone.Id);       // ids key the render cache
        Assert.Equal("abc", clone.PngBase64);
        Assert.NotSame(src.Strokes[0], clone.Strokes[0]);
        Assert.Equal(4, clone.Strokes[0].Points[0].Y);
    }

    /// <summary>
    /// A corrective is part of how the drawing renders under the rig, so a
    /// duplicated cel must carry it — and it names strokes by id, so the fresh
    /// stroke ids the clone gets have to be written back into the carried fix.
    /// </summary>
    [Fact]
    public void CloneFrame_CarriesCorrectives_RemappedToTheFreshStrokeIds()
    {
        var stroke = new Stroke { Points = [new(3, 4, 1)] };
        var src = new Frame
        {
            Strokes = [stroke],
            Correctives =
            [
                new Corrective
                {
                    DriverBoneId = "elbow",
                    Stops =
                    [
                        new CorrectiveStop
                        {
                            AngleDeg = 90,
                            Strokes = [new StrokeCorrection { StrokeId = stroke.Id, Offsets = [new(1, 2)] }],
                        },
                    ],
                },
            ],
        };

        var clone = (Frame)DocumentEditor.CloneFrame(src)!;

        var fix = Assert.Single(clone.Correctives!);
        var correction = Assert.Single(fix.Stops[0].Strokes);
        Assert.Equal(clone.Strokes[0].Id, correction.StrokeId);
        Assert.NotEqual(stroke.Id, correction.StrokeId);
        // And the source still names its own stroke — the remap wrote into
        // the copy, not through it.
        Assert.Equal(stroke.Id, src.Correctives![0].Stops[0].Strokes[0].StrokeId);
    }

    [Fact]
    public void LayerBlendMode_SurvivesSerialization()
    {
        var doc = DocumentFactory.CreateDoc(32, 32, 12);
        doc.Scene.Layers[0].BlendMode = LayerBlendMode.Multiply;
        doc.Scene.Layers[0].Opacity = 0.4;
        var restored = Lightbox.Core.Serialization.DocJson.Deserialize(Lightbox.Core.Serialization.DocJson.Serialize(doc));
        Assert.Equal(LayerBlendMode.Multiply, restored.Scene.Layers[0].BlendMode);
        Assert.Equal(0.4, restored.Scene.Layers[0].Opacity, 3);
    }
}

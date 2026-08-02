using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// Q9: whether the brush belongs to the tool or to the drawing.
/// </summary>
/// <remarks>
/// The record half. Answered as a variation on (c) — global or per-document,
/// defaulted by project type and overridable — because the two halves of this
/// application want opposite things. A storyboard artist moving through forty
/// panels wants one pencil; somebody returning to a comic page after a
/// fortnight wants the brush <i>that page</i> is drawn with, and on work where
/// the character of the stroke is part of the style, guessing wrong shows.
/// </remarks>
public class BrushScopeTests
{
    [Fact]
    public void WorkYouComeBackToKeepsTheBrushWithTheDrawing()
    {
        // The types named in the answer, and the reasoning behind the split:
        // these are documents with gaps between sittings.
        Assert.Equal(BrushScope.PerDocument, BrushScopeDefaults.For(ProjectType.Comic));
        Assert.Equal(BrushScope.PerDocument, BrushScopeDefaults.For(ProjectType.GameArt));
        Assert.Equal(BrushScope.PerDocument, BrushScopeDefaults.For(ProjectType.Illustration));
        Assert.Equal(BrushScope.PerDocument, BrushScopeDefaults.For(ProjectType.AssetLibrary));
    }

    [Fact]
    public void WorkYouMoveThroughInOnePassKeepsOneBrushForTheTool()
    {
        // The other half. You are switching documents constantly and want the
        // same pencil in each, which is exactly what per-document would undo.
        Assert.Equal(BrushScope.Global, BrushScopeDefaults.For(ProjectType.Animation));
        Assert.Equal(BrushScope.Global, BrushScopeDefaults.For(ProjectType.Storyboard));
    }

    [Fact]
    public void WithNoProjectItIsWhatTheApplicationAlwaysDid()
    {
        // No project means no type to ask. Somebody who opened the app to draw
        // one picture must not find the brush behaving differently than it
        // did before this existed.
        Assert.Equal(BrushScope.Global, BrushScopeDefaults.For(null));
    }

    [Fact]
    public void ADocumentThatNeverAsksForThisWritesNoBrushKey()
    {
        // The camera's rule. A file made under Global — every file that
        // exists today — must serialize exactly as it does now.
        var doc = DocumentFactory.CreateDoc(120, 80, 12);

        var json = DocJson.Serialize(doc);

        Assert.DoesNotContain("\"brush\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ARememberedBrushSurvivesASaveAndReload()
    {
        // The whole point: the gap between sessions is where the brush gets
        // forgotten, so the answer has to be in the file rather than in the
        // application's memory of what you were doing.
        var doc = DocumentFactory.CreateDoc(120, 80, 12);
        doc.Brush = new BrushSettings
        {
            Size = 37, Hardness = 0.22, Flow = 0.61, Spacing = 0.07,
            Kind = BrushKind.Paint, SmudgeLength = 0.4,
        };

        var back = DocJson.Deserialize(DocJson.Serialize(doc));

        Assert.NotNull(back.Brush);
        Assert.Equal(37, back.Brush!.Size);
        Assert.Equal(0.22, back.Brush.Hardness, 6);
        Assert.Equal(0.61, back.Brush.Flow, 6);
        Assert.Equal(0.07, back.Brush.Spacing, 6);
    }

    [Fact]
    public void TheRememberedBrushIsACopyNotTheLiveOne()
    {
        // It is a bookmark, not the tool's own state. Sharing the instance
        // would make every later tweak of the working brush rewrite what the
        // document says it was painted with, which is the opposite of the
        // question it answers.
        var doc = DocumentFactory.CreateDoc(120, 80, 12);
        var working = new BrushSettings { Size = 10 };
        doc.Brush = working.Clone();

        working.Size = 99;

        Assert.Equal(10, doc.Brush!.Size);
    }

    [Fact]
    public void RememberingABrushChangesNoPixelInTheRecord()
    {
        // Not a breach of invariant 4, and this is the assertion that says so:
        // every stroke still carries its own settings, and what the document
        // remembers reaches nothing that renders.
        var doc = DocumentFactory.CreateDoc(120, 80, 12);
        var frame = (PaintedFrame)doc.Scene.Layers[0].Cels[0].Frame!;
        var stroke = new Stroke
        {
            Tool = ToolKind.Brush,
            Points = [new StrokePoint(10, 10, 1), new StrokePoint(100, 60, 1)],
            Brush = new BrushSettings { Size = 12, Hardness = 1 },
        };
        frame.Strokes.Add(stroke);

        doc.Brush = new BrushSettings { Size = 200, Hardness = 0 };

        Assert.Equal(12, frame.Strokes[0].Brush.Size);
        Assert.Equal(1, frame.Strokes[0].Brush.Hardness);
    }
}

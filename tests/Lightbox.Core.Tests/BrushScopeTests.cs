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
    public void WorkWithAHouseStyleKeepsTheBrushWithTheProject()
    {
        // The types named in the answer. The mark is part of what the work
        // looks like, so every new page should start from it.
        Assert.Equal(BrushScope.PerProject, BrushScopeDefaults.For(ProjectType.Comic));
        Assert.Equal(BrushScope.PerProject, BrushScopeDefaults.For(ProjectType.GameArt));
        Assert.Equal(BrushScope.PerProject, BrushScopeDefaults.For(ProjectType.Illustration));
        Assert.Equal(BrushScope.PerProject, BrushScopeDefaults.For(ProjectType.AssetLibrary));
    }

    [Fact]
    public void WorkYouMoveThroughInOnePassKeepsOneBrushForTheTool()
    {
        // The other half. A storyboard is drawn to be read and thrown away,
        // and an animator working one character's cycles already carries one
        // pencil.
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
    public void AProjectThatNeverAsksForThisWritesNoBrushKey()
    {
        // The camera's rule. A project made under Global — every project that
        // exists today — must serialize exactly as it does now.
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-brush-{Guid.NewGuid():N}.lbproj");
        try
        {
            // Through the real writer, not a bare JsonSerializer — the whole
            // claim is about what lands on disk.
            ProjectIo.Save(ProjectIo.Create("Knight", root));

            var json = File.ReadAllText(Path.Combine(root, "project.json"));

            Assert.DoesNotContain("brush", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ARememberedBrushSurvivesASaveAndReload()
    {
        // The whole point: the gap between sessions is where the brush gets
        // forgotten, so the answer has to be on disk rather than in the
        // application's memory of what you were doing.
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-brush-{Guid.NewGuid():N}.lbproj");
        try
        {
            var project = ProjectIo.Create("Knight", root);
            project.Manifest.Brush = new BrushSettings
            {
                Size = 37, Hardness = 0.22, Flow = 0.61, Spacing = 0.07, Kind = BrushKind.Paint,
            };
            ProjectIo.Save(project);

            var back = ProjectIo.Load(root);

            Assert.NotNull(back.Manifest.Brush);
            Assert.Equal(37, back.Manifest.Brush!.Size);
            Assert.Equal(0.22, back.Manifest.Brush.Hardness, 6);
            Assert.Equal(0.61, back.Manifest.Brush.Flow, 6);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EveryDocumentInTheProjectIsFedTheSameBrush()
    {
        // The reason this is per project and not per document: the answer has
        // to reach the pages that do not exist yet. One store, so animation
        // forty gets what animation one was drawn with.
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-brush-{Guid.NewGuid():N}.lbproj");
        try
        {
            var project = ProjectIo.Create("Knight", root);
            var knight = ProjectIo.AddCharacter(project, "Knight");
            ProjectIo.AddAnimation(project, knight, "Walk", DocumentFactory.CreateDoc(120, 80, 12));
            project.Manifest.Brush = new BrushSettings { Size = 19 };

            // A page made later, after the brush was chosen.
            ProjectIo.AddAnimation(project, knight, "Idle", DocumentFactory.CreateDoc(120, 80, 12));

            Assert.Equal(19, project.Manifest.Brush.Size);
            Assert.Equal(2, knight.Animations.Count);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
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

        var manifest = new ProjectManifest { Brush = new BrushSettings { Size = 200, Hardness = 0 } };
        Assert.NotNull(manifest.Brush);

        Assert.Equal(12, frame.Strokes[0].Brush.Size);
        Assert.Equal(1, frame.Strokes[0].Brush.Hardness);
    }
}

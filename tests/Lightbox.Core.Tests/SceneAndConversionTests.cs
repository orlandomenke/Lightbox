using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Timeline;

namespace Lightbox.Core.Tests;

/// <summary>
/// The two things a project could not do: plan a film, and change its mind
/// about what it is for.
/// </summary>
/// <remarks>
/// Scenes are the second axis of a project — a character groups drawings by
/// <i>who</i>, a scene by <i>when</i> — and conversion is the promise that
/// picking a type at creation is not a decision you are stuck with.
/// </remarks>
public sealed class SceneAndConversionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-scene-{Guid.NewGuid():N}.lbproj");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static Doc Shot(int frames = 12, int fps = 24)
    {
        var doc = DocumentFactory.CreateDoc(80, 60, fps);
        while (doc.Scene.FrameCount < frames) DocumentEditor.AppendFrame(doc.Scene);
        return doc;
    }

    // ---- absence ----------------------------------------------------------------

    [Fact]
    public void AProjectWithNoScenesWritesNoSceneKey()
    {
        // A project making sprite sheets has no use for scenes and, following
        // the camera's rule, carries none.
        var project = ProjectIo.Create("Sprites", _root);
        ProjectIo.AddCharacter(project, "Knight");
        ProjectIo.Save(project);

        var json = File.ReadAllText(Path.Combine(_root, "project.json"));

        Assert.DoesNotContain("scenes", json, StringComparison.OrdinalIgnoreCase);
        Assert.False(ProjectIo.Load(_root).HasScenes);
    }

    [Fact]
    public void DeletingTheLastSceneTakesTheSceneListWithIt()
    {
        var project = ProjectIo.Create("Film", _root);
        var scene = ProjectIo.AddScene(project, "Opening");
        Assert.True(project.HasScenes);

        ProjectIo.RemoveScene(project, scene);

        Assert.Null(project.Manifest.Scenes);
        Assert.False(project.HasScenes);
    }

    // ---- the shape of a film ------------------------------------------------------

    [Fact]
    public void AShotIsADocumentLikeAnyOther()
    {
        // Which is what makes Save write it. Leaving shots out of AllDocuments
        // is how a save quietly skips every drawing in the film.
        var project = ProjectIo.Create("Film", _root);
        var scene = ProjectIo.AddScene(project, "Opening");
        var shot = ProjectIo.AddShot(project, scene, "1a", Shot());

        Assert.Contains(shot, project.AllDocuments);

        ProjectIo.Save(project);

        Assert.True(File.Exists(project.PathOf(shot)));
        Assert.Same(scene, project.SceneOf(shot));
    }

    [Fact]
    public void AFilmSurvivesASaveAndReload()
    {
        var project = ProjectIo.Create("Film", _root);
        var opening = ProjectIo.AddScene(project, "Opening");
        opening.Notes = "Rain on the window.";
        ProjectIo.AddShot(project, opening, "1a", Shot());
        ProjectIo.AddShot(project, opening, "1b", Shot());
        ProjectIo.AddScene(project, "The chase");
        ProjectIo.Save(project);

        var back = ProjectIo.Load(_root);

        Assert.Equal(["Opening", "The chase"], back.Scenes.Select(s => s.Name));
        Assert.Equal(["1a", "1b"], back.Scenes[0].Shots.Select(s => s.Name));
        Assert.Equal("Rain on the window.", back.Scenes[0].Notes);
    }

    [Fact]
    public void TwoScenesWithTheSameNameGetDifferentFolders()
    {
        var project = ProjectIo.Create("Film", _root);
        var first = ProjectIo.AddScene(project, "Chase");
        var second = ProjectIo.AddScene(project, "Chase");

        Assert.NotEqual(first.Slug, second.Slug);

        // And their shots do not land on top of each other.
        var a = ProjectIo.AddShot(project, first, "1a", Shot());
        var b = ProjectIo.AddShot(project, second, "1a", Shot());
        Assert.NotEqual(a.Path, b.Path);
    }

    [Fact]
    public void TwoShotsWithTheSameNameInOneSceneDoNotOverwriteEachOther()
    {
        var project = ProjectIo.Create("Film", _root);
        var scene = ProjectIo.AddScene(project, "Opening");
        var a = ProjectIo.AddShot(project, scene, "1a", Shot());
        var b = ProjectIo.AddShot(project, scene, "1a", Shot());

        Assert.NotEqual(a.Path, b.Path);
    }

    // ---- ordering ------------------------------------------------------------------

    [Fact]
    public void ScenesAndShotsCanBeReordered()
    {
        // The order is the running order — it is the whole point of the list.
        var project = ProjectIo.Create("Film", _root);
        var one = ProjectIo.AddScene(project, "One");
        ProjectIo.AddScene(project, "Two");
        ProjectIo.AddShot(project, one, "1a", Shot());
        ProjectIo.AddShot(project, one, "1b", Shot());

        Assert.True(ProjectIo.MoveScene(project, 1, 0));
        Assert.Equal(["Two", "One"], project.Scenes.Select(s => s.Name));

        Assert.True(ProjectIo.MoveShot(one, 1, 0));
        Assert.Equal(["1b", "1a"], one.Shots.Select(s => s.Name));
    }

    [Fact]
    public void AnImpossibleMoveChangesNothing()
    {
        var project = ProjectIo.Create("Film", _root);
        ProjectIo.AddScene(project, "One");

        Assert.False(ProjectIo.MoveScene(project, 0, 0));
        Assert.False(ProjectIo.MoveScene(project, 0, 7));
        Assert.False(ProjectIo.MoveScene(project, -1, 0));
        Assert.Single(project.Scenes);
    }

    [Fact]
    public void AShotCanMoveToAnotherScene()
    {
        var project = ProjectIo.Create("Film", _root);
        var one = ProjectIo.AddScene(project, "One");
        var two = ProjectIo.AddScene(project, "Two");
        var shot = ProjectIo.AddShot(project, one, "1a", Shot());

        Assert.True(ProjectIo.MoveShotToScene(one, two, shot));

        Assert.Empty(one.Shots);
        Assert.Same(shot, Assert.Single(two.Shots));
        // The file does not move. The index says where a shot belongs; the path
        // says where it lives, and rewriting paths on a reorganise is how a
        // project loses track of its own art.
        Assert.Contains("/one/", shot.Path);
    }

    // ---- running time ---------------------------------------------------------------

    [Fact]
    public void ASceneKnowsHowLongItRuns()
    {
        var project = ProjectIo.Create("Film", _root);
        var scene = ProjectIo.AddScene(project, "Opening");
        ProjectIo.AddShot(project, scene, "1a", Shot(frames: 24, fps: 24));
        ProjectIo.AddShot(project, scene, "1b", Shot(frames: 12, fps: 24));

        var (frames, seconds) = ProjectIo.SceneDuration(scene);

        Assert.Equal(36, frames);
        Assert.Equal(1.5, seconds!.Value, 3);
    }

    [Fact]
    public void AShotOfUnknownLengthMakesTheRunningTimeUnknownRatherThanShort()
    {
        // The number somebody schedules against. Silently omitting the shots it
        // could not measure is worse than admitting it does not know.
        var project = ProjectIo.Create("Film", _root);
        var scene = ProjectIo.AddScene(project, "Opening");
        ProjectIo.AddShot(project, scene, "1a", Shot(frames: 24, fps: 24));
        scene.Shots.Add(new DocumentRef { Name = "1b", Path = "scenes/opening/shots/1b.lightbox.json" });

        var (_, seconds) = ProjectIo.SceneDuration(scene);

        Assert.Null(seconds);
    }

    [Fact]
    public void TheLengthHintIsRefreshedWhenTheDocumentIsWritten()
    {
        // Derived data in an index, correct at the one moment it can be: the
        // save that produced the file.
        var project = ProjectIo.Create("Film", _root);
        var scene = ProjectIo.AddScene(project, "Opening");
        var doc = Shot(frames: 12, fps: 24);
        var shot = ProjectIo.AddShot(project, scene, "1a", doc);

        DocumentEditor.AppendFrame(doc.Scene);
        DocumentEditor.AppendFrame(doc.Scene);
        ProjectIo.Save(project);

        Assert.Equal(14, shot.Frames);
        Assert.Equal(14, ProjectIo.Load(_root).Scenes[0].Shots[0].Frames);
    }

    // ---- deleting a scene -----------------------------------------------------------

    [Fact]
    public void DeletingASceneKeepsItsShots()
    {
        // Reorganising a film must not be the fastest way to delete it.
        var project = ProjectIo.Create("Film", _root);
        var scene = ProjectIo.AddScene(project, "Opening");
        var shot = ProjectIo.AddShot(project, scene, "1a", Shot());
        ProjectIo.Save(project);
        var path = project.PathOf(shot);

        ProjectIo.RemoveScene(project, scene);

        Assert.Contains(shot, project.Manifest.Documents);
        Assert.Contains(shot, project.AllDocuments);
        Assert.True(File.Exists(path));
    }

    // ---- conversion ------------------------------------------------------------------

    [Fact]
    public void ConvertingRecreatesNoArtwork()
    {
        // The guarantee worth making, and the only one a test can hold: not a
        // single document is read, rewritten or recreated.
        var project = ProjectIo.Create("Knight", _root);
        project.Manifest.Type = ProjectType.Illustration;
        var character = ProjectIo.AddCharacter(project, "Knight");
        var walk = ProjectIo.AddAnimation(project, character, "Walk", Shot());
        ProjectIo.Save(project);
        var path = project.PathOf(walk);
        var before = File.ReadAllBytes(path);
        var written = File.GetLastWriteTimeUtc(path);

        ProjectIo.Convert(project, ProjectType.Animation);

        Assert.Equal(ProjectType.Animation, project.Manifest.Type);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void ConvertingAwayFromAnimationKeepsTheCameraAndTheScenes()
    {
        // A conversion that quietly deleted the shot work would be one nobody
        // could risk taking.
        var project = ProjectIo.Create("Film", _root);
        project.Manifest.Type = ProjectType.Animation;
        var scene = ProjectIo.AddScene(project, "Opening");
        var doc = Shot();
        doc.Scene.Camera = new Camera();
        ProjectIo.AddShot(project, scene, "1a", doc);

        var report = ProjectIo.Convert(project, ProjectType.GameArt);

        Assert.Single(project.Scenes);
        Assert.NotNull(project.Loaded.Values.Single().Scene.Camera);
        Assert.Contains(report.Notes, n => n.Contains("kept", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConvertingToNoTypeTakesTheKeyOutOfTheFile()
    {
        var project = ProjectIo.Create("Knight", _root);
        project.Manifest.Type = ProjectType.GameArt;
        ProjectIo.Save(project);
        Assert.Contains("gameArt", File.ReadAllText(Path.Combine(_root, "project.json")));

        ProjectIo.Convert(project, null);
        ProjectIo.Save(project);

        Assert.DoesNotContain("\"type\"", File.ReadAllText(Path.Combine(_root, "project.json")));
        Assert.Null(ProjectIo.Load(_root).Manifest.Type);
    }

    [Fact]
    public void ConvertingReportsWhatTheArtistShouldKnow()
    {
        var project = ProjectIo.Create("Knight", _root);
        project.Manifest.Type = ProjectType.Illustration;
        ProjectIo.AddCharacter(project, "Knight");   // no pivot

        var report = ProjectIo.Convert(project, ProjectType.GameArt);

        Assert.Equal(ProjectType.Illustration, report.From);
        Assert.Equal(ProjectType.GameArt, report.To);
        Assert.Contains(report.Notes, n => n.Contains("sprite sheets"));
        // The pivot is what asset export registers frames on, so its absence is
        // the one thing genuinely worth saying.
        Assert.Contains(report.Notes, n => n.Contains("no pivot"));
    }

    [Fact]
    public void ConvertingToTheTypeItAlreadyIsSaysSoAndDoesNothing()
    {
        var project = ProjectIo.Create("Knight", _root);
        project.Manifest.Type = ProjectType.Animation;

        var report = ProjectIo.Convert(project, ProjectType.Animation);

        Assert.Equal(ProjectType.Animation, project.Manifest.Type);
        Assert.Contains(report.Notes, n => n.Contains("Already"));
        Assert.Single(report.Notes);
    }

    [Fact]
    public void ConversionSurvivesASaveAndReload()
    {
        var project = ProjectIo.Create("Knight", _root);
        project.Manifest.Type = ProjectType.Illustration;
        ProjectIo.Save(project);

        ProjectIo.Convert(project, ProjectType.Animation);
        ProjectIo.Save(project);

        Assert.Equal(ProjectType.Animation, ProjectIo.Load(_root).Manifest.Type);
    }
}

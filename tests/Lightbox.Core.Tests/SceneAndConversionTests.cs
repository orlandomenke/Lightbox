using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Timeline;

namespace Lightbox.Core.Tests;

/// <summary>
/// The two things a project could not do: plan a film, and change its mind
/// about what it is for.
/// </summary>
/// <remarks>
/// Rewritten for B114. A scene is a folder with a running order — there is no
/// <c>ProjectScene</c>, and a shot is an ordinary document filed in it. What
/// these tests guard is unchanged: a film has an authored order, that order is
/// partial, reorganising a film is not the fastest way to delete it, and
/// conversion never touches artwork.
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
    public void AProjectWithNoFoldersWritesNoFolderKey()
    {
        // A project making sprite sheets from loose documents has no use for a
        // tree and, following the camera's rule, carries none.
        var project = ProjectIo.Create("Sprites", _root);
        ProjectIo.AddDocument(project, "Knight", Shot());
        ProjectIo.Save(project);

        var json = File.ReadAllText(Path.Combine(_root, "project.json"));

        Assert.DoesNotContain("\"folders\"", json);
        Assert.Empty(ProjectFolders.All(ProjectIo.Load(_root).Manifest));
    }

    [Fact]
    public void AFolderNobodyArrangedWritesNoOrderKey()
    {
        var project = ProjectIo.Create("Film", _root);
        var scene = ProjectFolders.Add(project.Manifest, "Opening");
        ProjectIo.AddDocument(project, "1a", Shot(), scene);
        ProjectIo.Save(project);

        Assert.DoesNotContain("\"order\"", File.ReadAllText(Path.Combine(_root, "project.json")));
    }

    // ---- the shape of a film ------------------------------------------------------

    [Fact]
    public void AShotIsADocumentLikeAnyOther()
    {
        // Which is what makes Save write it. Leaving shots out of the project's
        // document list is how a save quietly skips every drawing in the film —
        // B114 exactly.
        var project = ProjectIo.Create("Film", _root);
        var scene = ProjectFolders.Add(project.Manifest, "Opening");
        var shot = ProjectIo.AddDocument(project, "1a", Shot(), scene);

        Assert.Contains(shot, project.AllDocuments);
        Assert.Contains(shot, project.Manifest.Documents);

        ProjectIo.Save(project);

        Assert.True(File.Exists(project.PathOf(shot)));
        Assert.Equal(scene.Id, shot.FolderId);
    }

    [Fact]
    public void AFilmSurvivesASaveAndReload()
    {
        var project = ProjectIo.Create("Film", _root);
        var opening = ProjectFolders.Add(project.Manifest, "Opening");
        opening.Notes = "Rain on the window.";
        var a = ProjectIo.AddDocument(project, "1a", Shot(), opening);
        var b = ProjectIo.AddDocument(project, "1b", Shot(), opening);
        opening.Order = [a.Id, b.Id];
        ProjectFolders.Add(project.Manifest, "The chase");
        ProjectIo.Save(project);

        var back = ProjectIo.Load(_root);
        var scenes = ProjectFolders.ChildrenInOrder(back.Manifest, null);

        Assert.Equal(["Opening", "The chase"], scenes.Select(s => s.Name));
        Assert.Equal(
            ["1a", "1b"],
            ProjectFolders.InOrder(back.Manifest, scenes[0]).Select(s => s.Name));
        Assert.Equal("Rain on the window.", scenes[0].Notes);
    }

    [Fact]
    public void TwoScenesWithTheSameNameAreDistinguished()
    {
        var project = ProjectIo.Create("Film", _root);
        var first = ProjectFolders.Add(project.Manifest, "Chase");
        var second = ProjectFolders.Add(project.Manifest, "Chase");

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.Name, second.Name);

        // And their shots do not land on top of each other.
        var a = ProjectIo.AddDocument(project, "1a", Shot(), first);
        var b = ProjectIo.AddDocument(project, "1a", Shot(), second);
        Assert.NotEqual(a.Path, b.Path);
    }

    [Fact]
    public void TwoShotsWithTheSameNameInOneSceneDoNotOverwriteEachOther()
    {
        var project = ProjectIo.Create("Film", _root);
        var scene = ProjectFolders.Add(project.Manifest, "Opening");
        var a = ProjectIo.AddDocument(project, "1a", Shot(), scene);
        var b = ProjectIo.AddDocument(project, "1a", Shot(), scene);

        Assert.NotEqual(a.Path, b.Path);
    }

    // ---- ordering ------------------------------------------------------------------

    [Fact]
    public void ScenesAndShotsCanBeReordered()
    {
        // The order is the running order — it is the whole reason a folder
        // needed one, since membership order explicitly is not display order.
        var project = ProjectIo.Create("Film", _root);
        var film = ProjectFolders.Add(project.Manifest, "Film");
        var one = ProjectFolders.Add(project.Manifest, "One", film);
        ProjectFolders.Add(project.Manifest, "Two", film);
        ProjectIo.AddDocument(project, "1a", Shot(), one);
        ProjectIo.AddDocument(project, "1b", Shot(), one);

        Assert.True(ProjectFolders.MoveFolder(project.Manifest, film, 1, 0));
        Assert.Equal(
            ["Two", "One"],
            ProjectFolders.ChildrenInOrder(project.Manifest, film).Select(f => f.Name));

        Assert.True(ProjectFolders.MoveDocument(project.Manifest, one, 1, 0));
        Assert.Equal(
            ["1b", "1a"],
            ProjectFolders.InOrder(project.Manifest, one).Select(d => d.Name));
    }

    [Fact]
    public void OneOrderArrangesBothTheShotsAndTheSubScenes()
    {
        // Two readers over one list: each takes the ids it owns and skips the
        // rest, so arranging shots cannot scramble sub-scenes.
        var project = ProjectIo.Create("Film", _root);
        var act = ProjectFolders.Add(project.Manifest, "Act 1");
        var interior = ProjectFolders.Add(project.Manifest, "Interior", act);
        ProjectFolders.Add(project.Manifest, "Exterior", act);
        ProjectIo.AddDocument(project, "1a", Shot(), act);
        ProjectIo.AddDocument(project, "1b", Shot(), act);

        act.Order = [interior.Id];                       // pin one sub-scene
        Assert.True(ProjectFolders.MoveDocument(project.Manifest, act, 1, 0));

        Assert.Equal(
            ["1b", "1a"],
            ProjectFolders.InOrder(project.Manifest, act).Select(d => d.Name));
        Assert.Equal(
            ["Interior", "Exterior"],
            ProjectFolders.ChildrenInOrder(project.Manifest, act).Select(f => f.Name));
    }

    [Fact]
    public void AnImpossibleMoveChangesNothing()
    {
        var project = ProjectIo.Create("Film", _root);
        var film = ProjectFolders.Add(project.Manifest, "Film");
        ProjectFolders.Add(project.Manifest, "One", film);

        Assert.False(ProjectFolders.MoveFolder(project.Manifest, film, 0, 0));
        Assert.False(ProjectFolders.MoveFolder(project.Manifest, film, 0, 7));
        Assert.False(ProjectFolders.MoveFolder(project.Manifest, film, -1, 0));
        Assert.Single(ProjectFolders.ChildrenOf(project.Manifest, film));
        // And nothing was materialised, so the folder still writes no order key.
        Assert.Null(film.Order);
    }

    [Fact]
    public void AShotCanMoveToAnotherScene()
    {
        var project = ProjectIo.Create("Film", _root);
        var one = ProjectFolders.Add(project.Manifest, "One");
        var two = ProjectFolders.Add(project.Manifest, "Two");
        var shot = ProjectIo.AddDocument(project, "1a", Shot(), one);
        Assert.Equal("one/1a.lightbox.json", shot.Path);

        Assert.True(ProjectFolders.FileDocument(project.Manifest, shot, two));

        Assert.Empty(ProjectFolders.DocumentsIn(project.Manifest, one));
        Assert.Equal(shot.Id, Assert.Single(ProjectFolders.DocumentsIn(project.Manifest, two)).Id);
        // Filing a document is the one gesture that does move the bytes, and it
        // is the only thing allowed to: `FileDocument` sets `FolderId` and
        // `Path` together. Renaming the folder afterwards moves nothing.
        Assert.Equal("two/1a.lightbox.json", shot.Path);
    }

    // ---- running time ---------------------------------------------------------------

    [Fact]
    public void AFolderKnowsHowLongItRuns()
    {
        var project = ProjectIo.Create("Film", _root);
        var scene = ProjectFolders.Add(project.Manifest, "Opening");
        ProjectIo.AddDocument(project, "1a", Shot(frames: 24, fps: 24), scene);
        ProjectIo.AddDocument(project, "1b", Shot(frames: 12, fps: 24), scene);

        var (frames, seconds) = ProjectIo.FolderDuration(project.Manifest, scene);

        Assert.Equal(36, frames);
        Assert.Equal(1.5, seconds!.Value, 3);
    }

    [Fact]
    public void AShotOfUnknownLengthMakesTheRunningTimeUnknownRatherThanShort()
    {
        // The number somebody schedules against. Silently omitting the shots it
        // could not measure is worse than admitting it does not know.
        var project = ProjectIo.Create("Film", _root);
        var scene = ProjectFolders.Add(project.Manifest, "Opening");
        ProjectIo.AddDocument(project, "1a", Shot(frames: 24, fps: 24), scene);
        project.Manifest.Documents.Add(new DocumentRef
        {
            Name = "1b",
            Path = "unassigned-documents/1b.lightbox.json",
            FolderId = scene.Id,
        });

        var (_, seconds) = ProjectIo.FolderDuration(project.Manifest, scene);

        Assert.Null(seconds);
    }

    [Fact]
    public void TheLengthHintIsRefreshedWhenTheDocumentIsWritten()
    {
        // Derived data in an index, correct at the one moment it can be: the
        // save that produced the file.
        var project = ProjectIo.Create("Film", _root);
        var scene = ProjectFolders.Add(project.Manifest, "Opening");
        var doc = Shot(frames: 12, fps: 24);
        var shot = ProjectIo.AddDocument(project, "1a", doc, scene);

        DocumentEditor.AppendFrame(doc.Scene);
        DocumentEditor.AppendFrame(doc.Scene);
        ProjectIo.Save(project);

        Assert.Equal(14, shot.Frames);
        Assert.Equal(14, ProjectIo.Load(_root).Manifest.Documents[0].Frames);
    }

    // ---- deleting a scene -----------------------------------------------------------

    [Fact]
    public void DeletingASceneKeepsItsShots()
    {
        // Reorganising a film must not be the fastest way to delete it.
        var project = ProjectIo.Create("Film", _root);
        var scene = ProjectFolders.Add(project.Manifest, "Opening");
        var shot = ProjectIo.AddDocument(project, "1a", Shot(), scene);
        ProjectIo.Save(project);
        var path = project.PathOf(shot);

        var orphaned = ProjectFolders.Remove(project.Manifest, scene);

        Assert.Contains(shot, orphaned);
        Assert.Contains(shot, project.Manifest.Documents);
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
        var knight = ProjectFolders.Add(project.Manifest, "Knight");
        var walk = ProjectIo.AddDocument(project, "Walk", Shot(), knight);
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
        var scene = ProjectFolders.Add(project.Manifest, "Opening");
        var doc = Shot();
        doc.Scene.Camera = new Camera();
        ProjectIo.AddDocument(project, "1a", doc, scene);

        var report = ProjectIo.Convert(project, ProjectType.GameArt);

        Assert.Single(ProjectFolders.All(project.Manifest));
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
        var knight = ProjectFolders.Add(project.Manifest, "Knight");   // no pivot
        knight.Taxonomy = new SubjectTaxonomy { Kind = "biped" };

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

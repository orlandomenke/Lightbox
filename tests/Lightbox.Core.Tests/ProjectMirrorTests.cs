using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.Core.Tests;

/// <summary>
/// B188: the project tree and the folder on disk are the same thing.
/// </summary>
/// <remarks>
/// The report was a project whose disk held every layout the tree had ever
/// had at once: reparenting a folder rewrote only the manifest, the next save
/// materialised the new directory beside the old one, and the files stayed
/// wherever the layout of their creation put them. Three fixes, tested here:
/// moves carry the directory, saves bring stray files home, and directories
/// no layout explains — and nothing lives in — are removed.
/// </remarks>
public sealed class ProjectMirrorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-mirror-{Guid.NewGuid():N}.lbproj");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static DocumentRef Add(Project project, string name, ProjectFolder? folder = null) =>
        ProjectIo.AddDocument(project, name, DocumentFactory.CreateDoc(8, 8, 12), folder);

    [Fact]
    public void ReparentingAFolderCarriesItsDirectoryAndContents()
    {
        var project = ProjectIo.Create("Production", _root);
        var characters = ProjectFolders.Add(project.Manifest, "Characters");
        var main = ProjectFolders.Add(project.Manifest, "MainCharacter");
        var run = Add(project, "run", main);
        ProjectSheets.Add(project, "Character 1", main);
        ProjectIo.Save(project);
        Assert.True(Directory.Exists(Path.Combine(_root, "maincharacter")));

        // The drag: MainCharacter goes under Characters.
        Assert.True(ProjectIo.MoveFolder(project, main, characters));

        // The directory moved, with the document and the sheet inside it —
        // no fossil left at the root.
        Assert.False(Directory.Exists(Path.Combine(_root, "maincharacter")));
        Assert.True(File.Exists(Path.Combine(_root, "characters", "maincharacter", "run.lightbox.json")));
        var sheet = project.Manifest.Sheets!.Single();
        Assert.Equal("characters/maincharacter/character-1.sheet.json", sheet.Path);
        Assert.Equal("characters/maincharacter/run.lightbox.json", run.Path);

        // And the mirrored state round-trips.
        ProjectIo.Save(project);
        var reloaded = ProjectIo.Load(_root);
        Assert.Equal(run.Path, reloaded.Manifest.Documents.Single().Path);
    }

    [Fact]
    public void AMoveTheDiskRefusesChangesNothing()
    {
        var project = ProjectIo.Create("Production", _root);
        var characters = ProjectFolders.Add(project.Manifest, "Characters");
        var main = ProjectFolders.Add(project.Manifest, "MainCharacter");
        var run = Add(project, "run", main);
        ProjectIo.Save(project);
        // Occupy the destination, so Directory.Move must refuse rather than
        // overwrite somebody's work.
        Directory.CreateDirectory(Path.Combine(_root, "characters", "maincharacter"));
        File.WriteAllText(Path.Combine(_root, "characters", "maincharacter", "note.txt"), "mine");

        Assert.False(ProjectIo.MoveFolder(project, main, characters));

        Assert.Null(main.ParentId);
        Assert.Equal("maincharacter/run.lightbox.json", run.Path);
        Assert.True(File.Exists(Path.Combine(_root, "maincharacter", "run.lightbox.json")));
    }

    [Fact]
    public void RenamingAFolderCarriesSheetPathsToo()
    {
        var project = ProjectIo.Create("Production", _root);
        var main = ProjectFolders.Add(project.Manifest, "MainCharacter");
        ProjectSheets.Add(project, "Character 1", main);
        ProjectIo.Save(project);

        Assert.Equal(ProjectIo.RenameOutcome.Renamed,
            ProjectIo.RenameFolder(project, main, "Hero"));

        // The sheet's recorded path followed the directory — before B188 it
        // kept pointing at the old name, which reads as lost work.
        var sheet = project.Manifest.Sheets!.Single();
        Assert.Equal("hero/character-1.sheet.json", sheet.Path);
        Assert.True(File.Exists(Path.Combine(_root, "hero", "character-1.sheet.json")));
    }

    [Fact]
    public void SaveBringsAStrayFiledDocumentHome()
    {
        // A project rearranged before B188: the manifest says the document is
        // in Characters/MainCharacter, the file sits at the root layout of an
        // older day. One save reconciles it.
        var project = ProjectIo.Create("Production", _root);
        var characters = ProjectFolders.Add(project.Manifest, "Characters");
        var main = ProjectFolders.Add(project.Manifest, "MainCharacter", characters);
        var run = Add(project, "run", main);
        ProjectIo.Save(project);
        // Simulate the legacy scatter by hand: file at the old place, path
        // recorded to match, folder membership pointing at the new tree.
        var stray = "maincharacter/run.lightbox.json";
        Directory.CreateDirectory(Path.Combine(_root, "maincharacter"));
        File.Move(
            Path.Combine(_root, "characters", "maincharacter", "run.lightbox.json"),
            Path.Combine(_root, "maincharacter", "run.lightbox.json"));
        run.Path = stray;

        ProjectIo.Save(project);

        Assert.Equal("characters/maincharacter/run.lightbox.json", run.Path);
        Assert.True(File.Exists(Path.Combine(_root, "characters", "maincharacter", "run.lightbox.json")));
        // The emptied stray directory went with it.
        Assert.False(Directory.Exists(Path.Combine(_root, "maincharacter")));
    }

    [Fact]
    public void SaveRemovesOnlyEmptyUnexplainedDirectories()
    {
        var project = ProjectIo.Create("Production", _root);
        var empty = ProjectFolders.Add(project.Manifest, "Environments");
        ProjectIo.Save(project);
        // Two strays: one empty, one holding a file that is not Lightbox's —
        // and an empty system directory, which is never this pass's to judge.
        Directory.CreateDirectory(Path.Combine(_root, "fossil", "deeper"));
        Directory.CreateDirectory(Path.Combine(_root, "keepsake"));
        File.WriteAllText(Path.Combine(_root, "keepsake", "notes.txt"), "the artist's");
        Directory.CreateDirectory(Path.Combine(_root, "palettes"));

        ProjectIo.Save(project);

        // The empty fossil went, deepest first; the one with a file in it is
        // not this pass's to judge (B83 reports it instead).
        Assert.False(Directory.Exists(Path.Combine(_root, "fossil")));
        Assert.True(File.Exists(Path.Combine(_root, "keepsake", "notes.txt")));
        // An artist's empty folder is real (B64) and stays.
        Assert.True(Directory.Exists(Path.Combine(_root, ProjectFolders.PathOf(project.Manifest, empty))));
        // System directories are never judged.
        Assert.True(Directory.Exists(Path.Combine(_root, "palettes")));
    }

    [Fact]
    public void AMovedFolderKeepsLegacyLeafNamesIntact()
    {
        // The repath is a prefix substitution, not a re-derivation: a file
        // whose leaf does not match slug(Name) — a dedupe, an old shape —
        // must keep the name the directory move preserved.
        var project = ProjectIo.Create("Production", _root);
        var characters = ProjectFolders.Add(project.Manifest, "Characters");
        var main = ProjectFolders.Add(project.Manifest, "MainCharacter");
        var run = Add(project, "run", main);
        ProjectIo.Save(project);
        // Give the file an odd leaf by hand, as a legacy project would have.
        File.Move(
            Path.Combine(_root, "maincharacter", "run.lightbox.json"),
            Path.Combine(_root, "maincharacter", "run-2.lightbox.json"));
        run.Path = "maincharacter/run-2.lightbox.json";

        Assert.True(ProjectIo.MoveFolder(project, main, characters));

        Assert.Equal("characters/maincharacter/run-2.lightbox.json", run.Path);
        Assert.True(File.Exists(Path.Combine(_root, "characters", "maincharacter", "run-2.lightbox.json")));
    }
}

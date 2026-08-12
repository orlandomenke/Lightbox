using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// Q25, re-answered: inside a project a character sheet is its own file, filed
/// in a folder like a document, visible to that folder's subtree, and refiled
/// through the project window.
/// </summary>
public class ProjectSheetTests(ITestOutputHelper output)
{
    /// <summary>knight/{locomotion,combat} plus environments, one doc in each branch.</summary>
    private static (Project Project, ProjectFolder Knight, ProjectFolder Locomotion,
                    ProjectFolder Environments, DocumentRef Walk, DocumentRef Attack) Production()
    {
        var project = ProjectIo.Create("Production", root: "");
        var manifest = project.Manifest;
        var knight = ProjectFolders.Add(manifest, "knight");
        var locomotion = ProjectFolders.Add(manifest, "locomotion", knight);
        var combat = ProjectFolders.Add(manifest, "combat", knight);
        var environments = ProjectFolders.Add(manifest, "environments");

        var walk = ProjectIo.AddDocument(project, "walk", DocumentFactory.CreateDoc(), locomotion);
        var attack = ProjectIo.AddDocument(project, "attack", DocumentFactory.CreateDoc(), combat);
        return (project, knight, locomotion, environments, walk, attack);
    }

    // ---- the record stays absent until used -----------------------------------

    [Fact]
    public void AProjectWithNoSheetsWritesNoSheetsKey()
    {
        var manifest = new ProjectManifest { Name = "Plain" };
        var json = JsonSerializer.Serialize(manifest, DocJson.Options);
        Assert.DoesNotContain("\"sheets\"", json);
    }

    // ---- creation and the default scope ----------------------------------------

    /// <summary>
    /// The owner's sentence: "every document can create a reference sheet but
    /// it's always assigned to the first top folder by default".
    /// </summary>
    [Fact]
    public void ANewSheetDefaultsToTheTopFolderAboveItsDocument()
    {
        var (project, knight, _, _, walk, attack) = Production();

        var scope = ProjectSheets.DefaultScope(project.Manifest, walk);
        Assert.Same(knight, scope);

        var sheet = ProjectSheets.Add(project, "Knight", scope);
        var entry = Assert.Single(project.Manifest.Sheets!);
        output.WriteLine($"filed at {entry.Path} in folder {entry.FolderId}");
        Assert.Equal(sheet.Id, entry.Id);
        Assert.Equal(knight.Id, entry.FolderId);
        Assert.Equal("knight/knight.sheet.json", entry.Path);

        // "All documents within the folder can access the character sheet" —
        // including a sibling branch under the same top folder.
        Assert.Single(ProjectSheets.VisibleTo(project.Manifest, walk));
        Assert.Single(ProjectSheets.VisibleTo(project.Manifest, attack));
    }

    [Fact]
    public void ALooseDocumentsSheetIsProjectWideAndLandsInTheSheetsDirectory()
    {
        var (project, _, _, _, _, _) = Production();
        var loose = ProjectIo.AddDocument(project, "poster", DocumentFactory.CreateDoc());

        Assert.Null(ProjectSheets.DefaultScope(project.Manifest, loose));
        var sheet = ProjectSheets.Add(project, "Style", folder: null);
        var entry = ProjectSheets.FindRef(project.Manifest, sheet.Id)!;
        Assert.Equal("sheets/style.sheet.json", entry.Path);

        // Project-wide: every document sees it, wherever it is filed.
        foreach (var document in project.AllDocuments)
        {
            Assert.Contains(ProjectSheets.VisibleTo(project.Manifest, document), s => s.Id == sheet.Id);
        }
    }

    [Fact]
    public void ASheetOnOneBranchIsInvisibleToTheOther()
    {
        var (project, _, _, environments, walk, _) = Production();
        var sheet = ProjectSheets.Add(project, "Backgrounds", environments);

        Assert.DoesNotContain(ProjectSheets.VisibleTo(project.Manifest, walk), s => s.Id == sheet.Id);
    }

    [Fact]
    public void TwoSheetsOfTheSameNameInOneFolderDoNotCollideOnDisk()
    {
        var (project, knight, _, _, _, _) = Production();
        ProjectSheets.Add(project, "Knight", knight);
        var second = ProjectSheets.Add(project, "Knight", knight);

        var entry = ProjectSheets.FindRef(project.Manifest, second.Id)!;
        Assert.Equal("knight/knight-2.sheet.json", entry.Path);
    }

    // ---- round trip through disk ------------------------------------------------

    [Fact]
    public void ASheetSurvivesSaveAndReload()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-sheets-{Guid.NewGuid():N}.lbproj");
        try
        {
            var (project, knight, _, _, _, _) = Production();
            project.Root = root;
            var sheet = ProjectSheets.Add(project, "Knight", knight);
            var view = ReferenceView.Create("side", 400, 300);
            ((Frame)view.Layers[0].Cels[0].Frame!).Strokes.Add(new Stroke
            {
                Points = [new(10, 10, 0.5), new(50, 50, 0.5)],
                Label = "back-line",
            });
            sheet.Views.Add(view);

            ProjectIo.Save(project);
            Assert.Empty(project.DirtySheets);
            Assert.True(File.Exists(Path.Combine(root, "knight", "knight.sheet.json")));

            var reloaded = ProjectIo.Load(root);
            var entry = Assert.Single(reloaded.Manifest.Sheets!);
            Assert.Equal("Knight", entry.Name);
            var content = ProjectSheets.Load(reloaded, entry)!;
            var restored = Assert.Single(content.Views);
            Assert.Equal("side", restored.Name);
            Assert.Equal("back-line",
                Assert.Single(((Frame)restored.Layers[0].Cels[0].Frame!).Strokes).Label);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Only what changed is rewritten — the incremental-save rule.</summary>
    [Fact]
    public void AnUntouchedSheetIsNotRewrittenBySave()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-sheets-{Guid.NewGuid():N}.lbproj");
        try
        {
            var (project, knight, _, _, _, _) = Production();
            project.Root = root;
            var sheet = ProjectSheets.Add(project, "Knight", knight);
            ProjectIo.Save(project);

            var path = Path.Combine(root, "knight", "knight.sheet.json");
            var written = File.GetLastWriteTimeUtc(path);
            File.SetLastWriteTimeUtc(path, written.AddHours(-1));

            ProjectIo.Save(project);
            Assert.Equal(written.AddHours(-1), File.GetLastWriteTimeUtc(path));

            project.DirtySheets.Add(sheet.Id);
            ProjectIo.Save(project);
            Assert.NotEqual(written.AddHours(-1), File.GetLastWriteTimeUtc(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // ---- refiling -----------------------------------------------------------------

    /// <summary>"Through the project manager we can re-assign if need be."</summary>
    [Fact]
    public void RefilingMovesTheFileAndTheVisibility()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-sheets-{Guid.NewGuid():N}.lbproj");
        try
        {
            var (project, knight, _, environments, walk, _) = Production();
            project.Root = root;
            var sheet = ProjectSheets.Add(project, "Knight", knight);
            ProjectIo.Save(project);

            var entry = ProjectSheets.FindRef(project.Manifest, sheet.Id)!;
            Assert.True(ProjectSheets.Refile(project, entry, environments));
            output.WriteLine($"now at {entry.Path}");

            // Disk followed the manifest — B106's order, nothing left behind.
            Assert.False(File.Exists(Path.Combine(root, "knight", "knight.sheet.json")));
            Assert.True(File.Exists(Path.Combine(root, "environments", "knight.sheet.json")));

            // And the audience changed with it.
            Assert.DoesNotContain(ProjectSheets.VisibleTo(project.Manifest, walk), s => s.Id == sheet.Id);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RefilingToTheSameFolderIsRefused()
    {
        var (project, knight, _, _, _, _) = Production();
        ProjectSheets.Add(project, "Knight", knight);
        var entry = project.Manifest.Sheets![0];
        Assert.False(ProjectSheets.Refile(project, entry, knight));
    }

    // ---- migration from Q25's first answer ------------------------------------------

    [Fact]
    public void OpeningAnOldDocumentLiftsItsSheetsIntoTheProject()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-sheets-{Guid.NewGuid():N}.lbproj");
        try
        {
            var (project, knight, _, _, walk, _) = Production();
            project.Root = root;
            // Write the project with a sheet inside the document — the old shape.
            project.Loaded[walk.Id].ReferenceSheets.Add(new ReferenceSheet { Name = "Old knight" });
            ProjectIo.Save(project);

            var reloaded = ProjectIo.Load(root);
            var reference = reloaded.FindRef(walk.Id)!;
            var doc = ProjectIo.LoadDocument(reloaded, reference)!;

            // Lifted: registered on the top folder, gone from the document.
            var entry = Assert.Single(reloaded.Manifest.Sheets ?? []);
            Assert.Equal("Old knight", entry.Name);
            Assert.Equal(knight.Id, entry.FolderId);
            Assert.Empty(doc.ReferenceSheets);
            Assert.True(reloaded.DirtySheets.Contains(entry.Id),
                "a promoted sheet must be queued for the next save or it exists nowhere on disk");

            // The save that follows writes both halves; a second open finds the
            // registry already filled and invents nothing.
            ProjectIo.Save(reloaded);
            var again = ProjectIo.Load(root);
            var doc2 = ProjectIo.LoadDocument(again, again.FindRef(walk.Id)!)!;
            Assert.Single(again.Manifest.Sheets ?? []);
            Assert.Empty(doc2.ReferenceSheets);
            Assert.NotNull(ProjectSheets.Load(again, again.Manifest.Sheets![0]));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>B83's promise extended: the sheets directory is accounted for.</summary>
    [Fact]
    public void TheSheetsDirectoryIsNotReportedAsUnaccounted()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-sheets-{Guid.NewGuid():N}.lbproj");
        try
        {
            var (project, _, _, _, _, _) = Production();
            project.Root = root;
            ProjectSheets.Add(project, "Style", folder: null);
            ProjectIo.Save(project);
            Assert.Empty(ProjectIo.UnaccountedFolders(project));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}

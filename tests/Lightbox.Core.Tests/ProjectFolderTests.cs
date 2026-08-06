using System.Text.Json;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// The folder tree: arbitrary names, any depth, and absent until used.
/// </summary>
/// <remarks>
/// The primitive that replaces <c>characters/&lt;slug&gt;/animations/</c>. A
/// project is a production — a feature, an episode, every 2D asset in a game —
/// and none of those decompose into two levels somebody else named.
/// </remarks>
public class ProjectFolderTests
{
    private static ProjectManifest Fresh() => new() { Name = "Production" };

    // ---- absence -------------------------------------------------------------

    /// <summary>
    /// A project that never made a folder writes no folder key.
    /// </summary>
    /// <remarks>
    /// The half of "optional" that is easy to miss, and the one this repository
    /// has been caught by twice — a non-nullable block that serialises at its
    /// default, and a convenience getter that reintroduced the key under a
    /// second name. Serialising and looking is the cheap check.
    /// </remarks>
    [Fact]
    public void AProjectThatNeverMadeAFolderWritesNoFolderKey()
    {
        var json = JsonSerializer.Serialize(Fresh(), DocJson.Options);
        Assert.DoesNotContain("\"folders\"", json);
        Assert.DoesNotContain("\"folderId\"", json);
    }

    [Fact]
    public void AFolderThatWasNeverTaggedWritesNoTagsKey()
    {
        var manifest = Fresh();
        ProjectFolders.Add(manifest, "Backgrounds");
        var json = JsonSerializer.Serialize(manifest, DocJson.Options);

        Assert.Contains("\"folders\"", json);
        Assert.DoesNotContain("\"tags\"", json);
    }

    [Fact]
    public void AFolderTreeSurvivesARoundTrip()
    {
        var manifest = Fresh();
        var art = ProjectFolders.Add(manifest, "Art");
        var backgrounds = ProjectFolders.Add(manifest, "Backgrounds", art);
        backgrounds.Tags = ["environment"];

        var restored = JsonSerializer.Deserialize<ProjectManifest>(
            JsonSerializer.Serialize(manifest, DocJson.Options), DocJson.Options)!;

        var top = Assert.Single(ProjectFolders.ChildrenOf(restored, null));
        Assert.Equal("Art", top.Name);
        var child = Assert.Single(ProjectFolders.ChildrenOf(restored, top));
        Assert.Equal("Backgrounds", child.Name);
        Assert.Equal(["environment"], child.Tags);
    }

    // ---- arbitrary names, any depth -------------------------------------------

    /// <summary>Whatever the artist types, at whatever depth.</summary>
    [Fact]
    public void FoldersTakeAnyNameAndNestToAnyDepth()
    {
        var manifest = Fresh();
        var at = ProjectFolders.Add(manifest, "Episode 2");
        foreach (var name in new[] { "Act 1", "Sc 014 — Rooftop", "backgrounds", "final" })
        {
            at = ProjectFolders.Add(manifest, name, at);
        }

        Assert.Equal(4, ProjectFolders.DepthOf(manifest, at));
        Assert.Equal(
            "episode-2/act-1/sc-014-rooftop/backgrounds/final",
            ProjectFolders.PathOf(manifest, at));
    }

    /// <summary>
    /// The name is the artist's; the path is a slug of it.
    /// </summary>
    /// <remarks>
    /// Worth its own test because the two must not be conflated: a folder called
    /// "Act 2 — Interiors" has to keep the em dash in the docker and lose it on
    /// disk, and a fix that slugged the name itself would pass a path assertion
    /// while quietly renaming what the artist typed.
    /// </remarks>
    [Fact]
    public void TheNameKeepsItsPunctuationAndThePathDoesNot()
    {
        var manifest = Fresh();
        var folder = ProjectFolders.Add(manifest, "Act 2 — Interiors");

        Assert.Equal("Act 2 — Interiors", folder.Name);
        Assert.Equal("act-2-interiors", ProjectFolders.PathOf(manifest, folder));
    }

    // ---- collisions ------------------------------------------------------------

    [Fact]
    public void TwoFoldersOfTheSameNameInOnePlaceAreNumbered()
    {
        var manifest = Fresh();
        var first = ProjectFolders.Add(manifest, "Backgrounds");
        var second = ProjectFolders.Add(manifest, "Backgrounds");

        Assert.Equal("Backgrounds", first.Name);
        Assert.Equal("Backgrounds (2)", second.Name);
    }

    /// <summary>The same name in a different place is not a collision.</summary>
    [Fact]
    public void TheSameNameUnderADifferentParentIsFine()
    {
        var manifest = Fresh();
        var knight = ProjectFolders.Add(manifest, "Knight");
        var goblin = ProjectFolders.Add(manifest, "Goblin");

        Assert.Equal("Walk", ProjectFolders.Add(manifest, "Walk", knight).Name);
        Assert.Equal("Walk", ProjectFolders.Add(manifest, "Walk", goblin).Name);
    }

    /// <summary>
    /// A rename onto an existing name is refused rather than numbered.
    /// </summary>
    /// <remarks>
    /// Deliberately unlike <c>Add</c>. Somebody typing a name over another meant
    /// <em>that</em> name, and being handed "Backgrounds (2)" is help that has to
    /// be undone. Case-insensitive because two rows differing only in case are
    /// one directory on Windows and macOS.
    /// </remarks>
    [Fact]
    public void ARenameThatWouldCollideIsRefused()
    {
        var manifest = Fresh();
        ProjectFolders.Add(manifest, "Backgrounds");
        var props = ProjectFolders.Add(manifest, "Props");

        Assert.False(ProjectFolders.Rename(manifest, props, "backgrounds"));
        Assert.Equal("Props", props.Name);
        Assert.True(ProjectFolders.Rename(manifest, props, "Set dressing"));
        Assert.Equal("Set dressing", props.Name);
    }

    // ---- moving ----------------------------------------------------------------

    [Fact]
    public void AFolderMovesUnderAnotherAndBackToTheRoot()
    {
        var manifest = Fresh();
        var art = ProjectFolders.Add(manifest, "Art");
        var loose = ProjectFolders.Add(manifest, "Rooftop");

        Assert.True(ProjectFolders.Move(manifest, loose, art));
        Assert.Equal("art/rooftop", ProjectFolders.PathOf(manifest, loose));

        Assert.True(ProjectFolders.Move(manifest, loose, null));
        Assert.Equal("rooftop", ProjectFolders.PathOf(manifest, loose));
    }

    /// <summary>
    /// A folder cannot be dragged into itself or into anything beneath it.
    /// </summary>
    /// <remarks>
    /// The refusal that matters. Dropping a folder onto its own child is an
    /// ordinary slip in a tree view, and the result would be a subtree detached
    /// from the root: invisible in every surface, still in the file, and
    /// unreachable without editing JSON by hand.
    /// </remarks>
    [Fact]
    public void AFolderCannotBeMovedInsideItselfOrItsOwnDescendant()
    {
        var manifest = Fresh();
        var art = ProjectFolders.Add(manifest, "Art");
        var backgrounds = ProjectFolders.Add(manifest, "Backgrounds", art);
        var deep = ProjectFolders.Add(manifest, "Final", backgrounds);

        Assert.False(ProjectFolders.Move(manifest, art, art));
        Assert.False(ProjectFolders.Move(manifest, art, backgrounds));
        Assert.False(ProjectFolders.Move(manifest, art, deep));

        // Still reachable from the root, which is the thing being protected.
        Assert.Contains(art, ProjectFolders.ChildrenOf(manifest, null));
        Assert.Equal("art/backgrounds/final", ProjectFolders.PathOf(manifest, deep));
    }

    /// <summary>A cycle in a hand-edited file is walked, not fallen into.</summary>
    /// <remarks>
    /// <c>Move</c> refuses to create one, so the only way to get here is a
    /// <c>project.json</c> written by hand or by an agent. A stack overflow deep
    /// inside a docker rebuild is the worst available way to find out.
    /// </remarks>
    [Fact]
    public void ACycleFromAHandEditedFileDoesNotHang()
    {
        var manifest = Fresh();
        var a = ProjectFolders.Add(manifest, "A");
        var b = ProjectFolders.Add(manifest, "B", a);
        a.ParentId = b.Id;   // what no operation would produce

        Assert.Equal([b], ProjectFolders.Descendants(manifest, a));
        Assert.Equal(2, ProjectFolders.AncestryOf(manifest, a).Count);
        Assert.NotNull(ProjectFolders.PathOf(manifest, a));
    }

    // ---- documents --------------------------------------------------------------

    [Fact]
    public void ADocumentFiledInAFolderTakesThatFoldersPath()
    {
        var manifest = Fresh();
        var art = ProjectFolders.Add(manifest, "Art");
        var backgrounds = ProjectFolders.Add(manifest, "Backgrounds", art);
        var doc = new DocumentRef { Name = "Rooftop" };
        manifest.Documents.Add(doc);

        Assert.True(ProjectFolders.FileDocument(manifest, doc, backgrounds));
        Assert.Equal("art/backgrounds/rooftop.lightbox.json", doc.Path);
        Assert.Equal(backgrounds.Id, doc.FolderId);
        Assert.Equal([doc], ProjectFolders.DocumentsIn(manifest, backgrounds));
    }

    /// <summary>Two documents of the same name in one folder do not collide on disk.</summary>
    [Fact]
    public void TwoDocumentsOfOneNameInOneFolderGetDistinctFiles()
    {
        var manifest = Fresh();
        var art = ProjectFolders.Add(manifest, "Art");
        var first = new DocumentRef { Name = "Rooftop" };
        var second = new DocumentRef { Name = "Rooftop" };
        manifest.Documents.Add(first);
        manifest.Documents.Add(second);

        ProjectFolders.FileDocument(manifest, first, art);
        ProjectFolders.FileDocument(manifest, second, art);

        Assert.Equal("art/rooftop.lightbox.json", first.Path);
        Assert.Equal("art/rooftop-2.lightbox.json", second.Path);
    }

    [Fact]
    public void ADocumentFiledAtTheRootGoesToDocuments()
    {
        var manifest = Fresh();
        var art = ProjectFolders.Add(manifest, "Art");
        var doc = new DocumentRef { Name = "Colour test" };
        manifest.Documents.Add(doc);

        ProjectFolders.FileDocument(manifest, doc, art);
        Assert.True(ProjectFolders.FileDocument(manifest, doc, null));

        Assert.Null(doc.FolderId);
        Assert.Equal("unassigned-documents/colour-test.lightbox.json", doc.Path);
    }

    // ---- removal ------------------------------------------------------------------

    /// <summary>
    /// Removing a folder hands back what was in it rather than deciding.
    /// </summary>
    /// <remarks>
    /// "Remove from the project" and "delete from disk" are two operations with
    /// different consequences (B87), and a model that dropped the documents
    /// would make every caller live with one answer to a question the artist
    /// has not been asked yet.
    /// </remarks>
    [Fact]
    public void RemovingAFolderReturnsEverythingThatWasInIt()
    {
        var manifest = Fresh();
        var art = ProjectFolders.Add(manifest, "Art");
        var backgrounds = ProjectFolders.Add(manifest, "Backgrounds", art);
        var rooftop = new DocumentRef { Name = "Rooftop" };
        var loose = new DocumentRef { Name = "Colour test" };
        manifest.Documents.Add(rooftop);
        manifest.Documents.Add(loose);
        ProjectFolders.FileDocument(manifest, rooftop, backgrounds);

        var orphaned = ProjectFolders.Remove(manifest, art);

        Assert.Equal([rooftop], orphaned);
        Assert.Empty(ProjectFolders.All(manifest));
        // The documents are still in the manifest — nothing decided their fate.
        Assert.Contains(rooftop, manifest.Documents);
        Assert.Contains(loose, manifest.Documents);
    }

    [Fact]
    public void ContentsReportsTheWholeSubtreeBeforeAnythingHappens()
    {
        var manifest = Fresh();
        var art = ProjectFolders.Add(manifest, "Art");
        var backgrounds = ProjectFolders.Add(manifest, "Backgrounds", art);
        ProjectFolders.Add(manifest, "Props", art);
        var doc = new DocumentRef { Name = "Rooftop" };
        manifest.Documents.Add(doc);
        ProjectFolders.FileDocument(manifest, doc, backgrounds);

        var (folders, documents) = ProjectFolders.Contents(manifest, art);

        Assert.Equal(3, folders.Count);   // itself and two children
        Assert.Equal([doc], documents);
        Assert.Equal(3, ProjectFolders.All(manifest).Count);  // reported, not removed
    }

    // ---- migration --------------------------------------------------------------

    /// <summary>
    /// A project written before folders existed opens and keeps its paths.
    /// </summary>
    /// <remarks>
    /// The whole reason <c>FolderId</c> sits beside <c>Path</c> rather than
    /// replacing it. Deriving the path from the folder chain on every read would
    /// have moved every existing document to <c>documents/</c> the first time an
    /// old project was saved.
    /// </remarks>
    [Fact]
    public void AProjectWrittenBeforeFoldersKeepsItsPaths()
    {
        const string old = """
            {
              "name": "Knight",
              "characters": [],
              "documents": [
                { "id": "docref_1", "name": "Walk",
                  "path": "characters/knight/animations/walk.lightbox.json" }
              ],
              "palettes": []
            }
            """;

        var manifest = JsonSerializer.Deserialize<ProjectManifest>(old, DocJson.Options)!;
        var walk = Assert.Single(manifest.Documents);

        Assert.Null(walk.FolderId);
        Assert.Equal("characters/knight/animations/walk.lightbox.json", walk.Path);
        Assert.Empty(ProjectFolders.All(manifest));
        // And it still writes no folder key, so re-saving it changes nothing.
        Assert.DoesNotContain("\"folders\"", JsonSerializer.Serialize(manifest, DocJson.Options));
    }

    /// <summary>
    /// A project written before the folder was renamed keeps its own paths, and
    /// its <c>documents/</c> is still a directory the manifest can explain.
    /// </summary>
    /// <remarks>
    /// <b>B105.</b> The unfiled directory is now <c>unassigned-documents</c>, and
    /// the rename is of the constant rather than of anybody's project: a path is
    /// recorded per document, so an existing <c>.lbproj</c> goes on reading and
    /// writing the folder it already has and nothing moves under an artist who
    /// asked for nothing.
    /// <para>
    /// The <c>SystemFolders</c> half is the one that would have gone unnoticed.
    /// B83 promises that nothing sits at the top of a project the manifest cannot
    /// explain, and dropping the old name would have made every project written
    /// before today report its own <c>documents/</c> as unaccounted for.
    /// </para>
    /// </remarks>
    [Fact]
    public void AProjectWrittenBeforeTheRenameKeepsItsDocumentsFolder()
    {
        const string old = """
            {
              "name": "Knight",
              "characters": [],
              "documents": [
                { "id": "docref_1", "name": "Colour test",
                  "path": "documents/colour-test.lightbox.json" }
              ],
              "palettes": []
            }
            """;

        var manifest = JsonSerializer.Deserialize<ProjectManifest>(old, DocJson.Options)!;
        var test = Assert.Single(manifest.Documents);

        Assert.Equal("documents/colour-test.lightbox.json", test.Path);
        Assert.Contains(ProjectIo.LegacyDocumentsDir, ProjectIo.SystemFolders);
        Assert.Contains(ProjectIo.DocumentsDir, ProjectIo.SystemFolders);
        // The new name is what a fresh path is derived from, though — the old one
        // is read, not written.
        Assert.Equal(
            "unassigned-documents/colour-test.lightbox.json",
            ProjectFolders.PathFor(manifest, test, null));
    }
}

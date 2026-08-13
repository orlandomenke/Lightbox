using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// B133: the reference <em>declaration</em> kind is retired, and sheets filed
/// on folders are the reference mechanism.
/// </summary>
/// <remarks>
/// This file used to test Q30 step 3's declarations — Declare, VisibleTo,
/// OfTarget, three target shapes. Measurement showed the whole system was
/// write-only: every declaration it produced was read by nothing, while
/// <see cref="ProjectSheets.VisibleTo"/> delivered the same promise and was
/// consumed end-to-end. The owner chose retirement over wiring a consumer
/// (2026-08-13), so what is tested now is the retirement itself: nothing
/// produces the kind, old files carrying it are pruned, and the working
/// mechanism still works.
/// </remarks>
public class ReferenceScopeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-refretire-{Guid.NewGuid():N}.lbproj");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void TheRetiredDeclarationsArePrunedWhenAProjectLoads()
    {
        var project = ProjectIo.Create("Production", _root);
        var knight = ProjectFolders.Add(project.Manifest, "Knight");
        ProjectIo.AddDocument(project, "walk", DocumentFactory.CreateDoc(8, 8, 12), knight);
        // What "Use as reference" used to write, at every scope it could:
        // the project, a folder, a document. None of it was ever read.
        ResourceScopes.Declare(project.Manifest, null, ReferenceScopes.Kind, "sheet-1");
        ResourceScopes.Declare(project.Manifest, knight, ReferenceScopes.Kind, "doc-1");
        ResourceScopes.DeclareOn(project.Manifest.Documents[0], ReferenceScopes.Kind, "img-1");
        // A live kind beside them, which must survive the prune untouched.
        ResourceScopes.Declare(project.Manifest, knight, PaletteScopes.Kind, "pal-1");
        ProjectIo.Save(project);

        var reloaded = ProjectIo.Load(_root);

        Assert.Null(reloaded.Manifest.Resources);
        var folder = reloaded.Manifest.Folders!.Single();
        Assert.DoesNotContain(folder.Resources!, r => r.Kind == ReferenceScopes.Kind);
        Assert.Contains(folder.Resources!, r => r.Kind == PaletteScopes.Kind);
        Assert.Null(reloaded.Manifest.Documents.Single().Resources);
    }

    [Fact]
    public void AnOldFileWithATargetKeyStillLoads()
    {
        // ScopedResource carried a `target` word for this kind alone. The
        // property is gone; the key in an old file is ignored, not an error.
        var json = """{"id":"pal-1","kind":"palette","target":"sheet"}""";
        var entry = JsonSerializer.Deserialize<ScopedResource>(json, DocJson.Options);
        Assert.NotNull(entry);
        Assert.Equal("pal-1", entry!.Id);
        // And nothing writes one back.
        Assert.DoesNotContain("target", JsonSerializer.Serialize(entry, DocJson.Options));
    }

    [Fact]
    public void NoSurfaceOffersTheRetiredKind()
    {
        var project = ProjectIo.Create("Production", _root);
        ProjectSheets.Add(project, "Knight sheet", null);

        Assert.DoesNotContain(ReferenceScopes.Kind, ProjectBoard.Kinds);
        Assert.Empty(ProjectBoard.Offers(project, ReferenceScopes.Kind));
    }

    /// <summary>
    /// The mechanism that replaced the declarations: a sheet filed on a folder
    /// reaches every document below it — consumed by the Reference sheets
    /// panel, which is what the declarations never had.
    /// </summary>
    [Fact]
    public void ASheetFiledOnAFolderIsTheReferenceMechanism()
    {
        var project = ProjectIo.Create("Production", _root);
        var knight = ProjectFolders.Add(project.Manifest, "Knight");
        var combat = ProjectFolders.Add(project.Manifest, "Combat", knight);
        var attack = ProjectIo.AddDocument(
            project, "attack", DocumentFactory.CreateDoc(8, 8, 12), combat);
        var elsewhere = ProjectIo.AddDocument(
            project, "background", DocumentFactory.CreateDoc(8, 8, 12));
        ProjectIo.Save(project);
        var sheet = ProjectSheets.Add(project, "Knight sheet", knight);

        Assert.Contains(ProjectSheets.VisibleTo(project.Manifest, attack), s => s.Id == sheet.Id);
        Assert.DoesNotContain(
            ProjectSheets.VisibleTo(project.Manifest, elsewhere), s => s.Id == sheet.Id);
    }
}

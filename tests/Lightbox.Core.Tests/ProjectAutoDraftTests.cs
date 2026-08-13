using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.Core.Tests;

/// <summary>
/// New work enters the pipeline as Draft the moment it first lands on disk.
/// </summary>
/// <remarks>
/// The rule lives in <c>ProjectIo.Save</c> and is deliberately narrow: first
/// write only, and only when nobody has said anything. "Nobody has said" is a
/// state <see cref="DocumentRef.Status"/> exists to keep distinct from Design,
/// so a document already on disk without a status keeps that distinction —
/// backfilling it would invent a pipeline position for every imported file.
/// </remarks>
public sealed class ProjectAutoDraftTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-draft-{Guid.NewGuid():N}.lbproj");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static DocumentRef Add(Project project, string name)
    {
        var doc = DocumentFactory.CreateDoc(32, 32, 12);
        return ProjectIo.AddDocument(project, name, doc);
    }

    [Fact]
    public void ANewDocumentBecomesDraftOnItsFirstSave()
    {
        var project = ProjectIo.Create("Production", _root);
        var reference = Add(project, "walk");
        Assert.Null(reference.Status);   // in the project, not yet in the pipeline

        ProjectIo.Save(project);

        Assert.Equal(AssetStatus.Draft, reference.Status);
        // In the manifest on disk too, not only in memory — the status is
        // production metadata and the manifest is its record.
        var reloaded = ProjectIo.Load(_root);
        Assert.Equal(AssetStatus.Draft, reloaded.Manifest.Documents.Single().Status);
    }

    [Fact]
    public void AStatusAlreadyChosenSurvivesTheSave()
    {
        var project = ProjectIo.Create("Production", _root);
        var reference = Add(project, "walk");
        reference.Status = AssetStatus.Review;

        ProjectIo.Save(project);

        Assert.Equal(AssetStatus.Review, reference.Status);
    }

    [Fact]
    public void ADocumentAlreadyOnDiskWithoutAStatusIsNotBackfilled()
    {
        var project = ProjectIo.Create("Production", _root);
        var reference = Add(project, "walk");
        ProjectIo.Save(project);
        Assert.Equal(AssetStatus.Draft, reference.Status);

        // The artist cleared it: "nobody has said" is a real state and
        // clearing must stick, or the field cannot say it.
        reference.Status = null;
        ProjectIo.Save(project, new HashSet<string> { reference.Id });

        Assert.Null(reference.Status);
    }
}

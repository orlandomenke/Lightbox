using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.Core.Tests;

/// <summary>
/// The manifest's template hint: written at save, absent on ordinary work.
/// </summary>
/// <remarks>
/// The flag lives in the document so an artist can mark work they already did;
/// the hint lives on <see cref="DocumentRef"/> so a row can wear the kind
/// without loading the file. Refreshed in the same pass as the duration hints,
/// and held to the same serialization rule: true or absent, never false.
/// </remarks>
public sealed class TemplateHintTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-tplhint-{Guid.NewGuid():N}.lbproj");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void TheHintFollowsTheFlagAcrossSaves()
    {
        var project = ProjectIo.Create("Production", _root);
        var doc = DocumentFactory.CreateDoc(32, 32, 12);
        var reference = ProjectIo.AddDocument(project, "colour key", doc);
        Templates.SetTemplate(doc, true);

        ProjectIo.Save(project);
        Assert.True(reference.IsTemplate);
        Assert.True(ProjectIo.Load(_root).Manifest.Documents.Single().IsTemplate);

        // Unmarking clears it on the next save — a hint that only ever gains
        // would go on calling something a template after the artist said no.
        Templates.SetTemplate(doc, false);
        ProjectIo.Save(project, new HashSet<string> { reference.Id });
        Assert.Null(reference.IsTemplate);
    }

    [Fact]
    public void AnOrdinaryDocumentWritesNoTemplateKey()
    {
        var project = ProjectIo.Create("Production", _root);
        ProjectIo.AddDocument(project, "walk", DocumentFactory.CreateDoc(32, 32, 12));
        ProjectIo.Save(project);

        var manifest = File.ReadAllText(Path.Combine(_root, "project.json"));
        Assert.DoesNotContain("\"isTemplate\"", manifest);
    }
}

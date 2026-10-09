using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// A document in a project that cannot be read is unavailable, with its reason,
/// the way a missing one is — not an exception through every caller.
/// </summary>
/// <remarks>
/// <c>ProjectIo.LoadDocument</c>'s dozen callers (the project window, the
/// docker, templates, the character library) catch nothing, so one corrupt
/// file took the application down on any route that read it — the template
/// list on every build of it (the sensitivity review of Q235, 2026-10-09; the
/// owner chose to fix it next).
/// </remarks>
public class CorruptProjectDocumentTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lightbox-corrupt").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private (Project Project, DocumentRef Good, DocumentRef Bad) TwoDocuments()
    {
        var project = ProjectIo.Create("Knight", _root);
        var good = ProjectIo.AddDocument(project, "Walk", DocumentFactory.CreateDoc(120, 80, 12));
        var bad = ProjectIo.AddDocument(project, "Idle", DocumentFactory.CreateDoc(120, 80, 12));
        ProjectIo.Save(project);
        project.Loaded.Clear();
        File.WriteAllText(project.PathOf(bad), "this is not a drawing");
        return (project, good, bad);
    }

    [Fact]
    public void ACorruptDocumentIsUnavailableWithItsReason()
    {
        var (project, good, bad) = TwoDocuments();

        Assert.Null(ProjectIo.LoadDocument(project, bad));
        Assert.NotNull(ProjectIo.LoadDocument(project, good));
        Assert.Contains("could not be read", ProjectIo.Unavailable(project, bad));
    }

    [Fact]
    public void AMissingDocumentSaysItIsMissing()
    {
        var (project, _, bad) = TwoDocuments();
        File.Delete(project.PathOf(bad));

        Assert.Null(ProjectIo.LoadDocument(project, bad));
        Assert.Contains("missing", ProjectIo.Unavailable(project, bad));
    }

    /// <summary>The template list reads every document; one bad file must not break it.</summary>
    [Fact]
    public void TheTemplateListSurvivesACorruptDocument()
    {
        var (project, _, _) = TwoDocuments();
        var templates = Templates.InProject(project);
        Assert.NotNull(templates);
    }
}

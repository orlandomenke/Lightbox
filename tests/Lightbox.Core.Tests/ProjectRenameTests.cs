using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.Core.Tests;

/// <summary>
/// Renaming the project renames the manifest and the folder on disk together —
/// the owner's call (2026-08-13), superseding the B62-era refusal.
/// </summary>
public sealed class ProjectRenameTests : IDisposable
{
    private readonly string _parent = Path.Combine(
        Path.GetTempPath(), $"lightbox-projrename-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_parent)) Directory.Delete(_parent, recursive: true);
    }

    private Project Make(string leaf)
    {
        var project = ProjectIo.Create("Production", Path.Combine(_parent, leaf));
        ProjectIo.AddDocument(project, "walk", DocumentFactory.CreateDoc(8, 8, 12));
        ProjectIo.Save(project);
        return project;
    }

    [Fact]
    public void TheFolderFollowsTheNameAndKeepsItsSuffix()
    {
        var project = Make("Production.lbproj");

        Assert.Equal(ProjectIo.RenameOutcome.Renamed, ProjectIo.RenameProject(project, "Sequel"));

        Assert.Equal("Sequel", project.Manifest.Name);
        Assert.Equal(Path.Combine(_parent, "Sequel.lbproj"), project.Root);
        Assert.True(Directory.Exists(project.Root));
        Assert.False(Directory.Exists(Path.Combine(_parent, "Production.lbproj")));

        // Everything inside still resolves — the paths are relative, so the
        // move is invisible to them — and the renamed project round-trips.
        ProjectIo.Save(project);
        var reloaded = ProjectIo.Load(project.Root);
        Assert.Equal("Sequel", reloaded.Manifest.Name);
        Assert.Single(reloaded.Manifest.Documents);
    }

    [Fact]
    public void ATypedSuffixBelongsToTheFolderNotTheName()
    {
        var project = Make("Production.lbproj");

        Assert.Equal(ProjectIo.RenameOutcome.Renamed,
            ProjectIo.RenameProject(project, "Sequel.lbproj"));

        Assert.Equal("Sequel", project.Manifest.Name);
        Assert.EndsWith("Sequel.lbproj", project.Root);
    }

    [Fact]
    public void AProjectWithoutTheSuffixDoesNotGainOne()
    {
        var project = Make("production");

        Assert.Equal(ProjectIo.RenameOutcome.Renamed, ProjectIo.RenameProject(project, "Sequel"));

        Assert.Equal(Path.Combine(_parent, "Sequel"), project.Root);
    }

    [Fact]
    public void ACollisionRefusesWhole()
    {
        var project = Make("Production.lbproj");
        Directory.CreateDirectory(Path.Combine(_parent, "Sequel.lbproj"));

        Assert.Equal(ProjectIo.RenameOutcome.NameTaken, ProjectIo.RenameProject(project, "Sequel"));

        // Manifest put back, folder untouched — no half-rename.
        Assert.Equal("Production", project.Manifest.Name);
        Assert.True(Directory.Exists(Path.Combine(_parent, "Production.lbproj")));
    }
}

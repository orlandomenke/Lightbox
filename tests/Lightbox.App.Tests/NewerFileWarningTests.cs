using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// Opening a file a newer build wrote says so (Q234): saving it here may drop
/// what this build cannot read, and the artist should know before, not after.
/// </summary>
[Collection("BrushState")]
public sealed class NewerFileWarningTests : BrushStateIsolated
{
    [AvaloniaFact]
    public void ADocumentFromANewerBuildWarnsWhenItOpens()
    {
        var vm = VmLayers.PaperVm();
        var newer = new Doc { Version = Doc.CurrentVersion + 1 };
        newer.Scene.Layers.Add(new Layer { Name = "Ink" });

        vm.OpenDocumentTab(newer, Path.Combine(Path.GetTempPath(), "From later.lightbox.json"));

        Assert.Contains("newer", vm.AiStatus);
        Assert.Contains("From later", vm.AiStatus);
    }

    [AvaloniaFact]
    public void ADocumentFromThisBuildSaysNothing()
    {
        var vm = VmLayers.PaperVm();
        vm.AiStatus = "";
        var doc = new Doc();
        doc.Scene.Layers.Add(new Layer { Name = "Ink" });

        vm.OpenDocumentTab(doc, Path.Combine(Path.GetTempPath(), "Ours.lightbox.json"));

        Assert.DoesNotContain("newer", vm.AiStatus ?? "");
    }

    [AvaloniaFact]
    public void AProjectFromANewerBuildWarnsWhenItOpens()
    {
        var root = Directory.CreateTempSubdirectory("lightbox-newer-project").FullName;
        try
        {
            var project = ProjectIo.Create("Later", root);
            project.Manifest.Version = ProjectManifest.CurrentVersion + 1;
            ProjectIo.Save(project);
            var vm = VmLayers.PaperVm();

            vm.OpenProject(root);

            Assert.Contains("newer", vm.AiStatus);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Through a project, the route most drawings take: the review found that
    /// only directly opened files warned, so a project's animation from a newer
    /// build opened silently and a save dropped what it could not read.
    /// </summary>
    [AvaloniaFact]
    public void AProjectDocumentFromANewerBuildWarnsWhenItOpens()
    {
        var root = Directory.CreateTempSubdirectory("lightbox-newer-doc").FullName;
        try
        {
            var project = ProjectIo.Create("Current", root);
            ProjectIo.Save(project);
            var vm = VmLayers.PaperVm();
            vm.OpenProject(root);
            var newer = new Doc { Version = Doc.CurrentVersion + 1 };
            newer.Scene.Layers.Add(new Layer { Name = "Ink" });

            vm.OpenProjectDocument(new DocumentRef { Id = "later", Name = "Walk", Path = "walk.lightbox.json" }, newer);

            Assert.Contains("newer", vm.AiStatus);
            Assert.Contains("Walk", vm.AiStatus);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

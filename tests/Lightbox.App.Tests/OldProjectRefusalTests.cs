using Avalonia.Headless.XUnit;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// A project from an earlier alpha is refused with its sentence, not with the
/// window going down.
/// </summary>
/// <remarks>
/// <c>ProjectIo.Load</c> refuses an older manifest with a
/// <see cref="NotSupportedException"/> carrying a sentence written for the
/// artist (Q36) — and <c>OpenProject</c> caught only IO and JSON errors, so
/// the sentence escaped from a click handler instead of reaching the status
/// strip (found 2026-10-09 while adding the newer-file warning, Q234).
/// </remarks>
[Collection("BrushState")]
public sealed class OldProjectRefusalTests : BrushStateIsolated
{
    [AvaloniaFact]
    public void AProjectFromAnEarlierAlphaSaysWhyInsteadOfThrowing()
    {
        var root = Directory.CreateTempSubdirectory("lightbox-old-project").FullName;
        try
        {
            var project = ProjectIo.Create("Old", root);
            project.Manifest.Version = ProjectManifest.CurrentVersion - 1;
            ProjectIo.Save(project);
            var vm = VmLayers.PaperVm();

            vm.OpenProject(root);

            Assert.Contains("earlier alpha", vm.AiStatus);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

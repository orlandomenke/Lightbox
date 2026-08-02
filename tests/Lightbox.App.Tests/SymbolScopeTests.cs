using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Raster;

namespace Lightbox.App.Tests;

/// <summary>
/// Symbols reaching the engine — the app half of Pillar 3's step two.
/// </summary>
/// <remarks>
/// There is one funnel that points the registries at whatever should resolve
/// right now, and symbols go through it beside the palettes and gradients
/// rather than beside them in a second place. The rendering itself is proved
/// in <c>SymbolRenderTests</c> over in Raster; this is about scope.
/// </remarks>
public class SymbolScopeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-sym-{Guid.NewGuid():N}.lbproj");

    public void Dispose()
    {
        SymbolRegistry.Clear();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static Symbol Sword() => new() { Name = "Sword" };

    [AvaloniaFact]
    public void WithNoProjectOpenNothingResolves()
    {
        // Absence, again. A single painting has no symbols and no way to make
        // one, and the registry saying so is the correct answer rather than a
        // degraded one.
        SymbolRegistry.Register(Sword());

        _ = new MainViewModel(null);

        Assert.Empty(SymbolRegistry.Unresolved);
        Assert.Null(SymbolRegistry.Resolve("sym_anything"));
    }

    [AvaloniaFact]
    public void AProjectsSymbolsResolveOnceTheProjectIsOpen()
    {
        var vm = new MainViewModel(null);
        var project = ProjectIo.Create("Knight", _root);
        var sword = Sword();
        project.Symbols[sword.Id] = sword;

        vm.ProjectDocker.Project = project;
        vm.RefreshProjectResources();

        Assert.Same(sword, SymbolRegistry.Resolve(sword.Id));
    }

    [AvaloniaFact]
    public void ClosingAProjectStopsItsSymbolsResolving()
    {
        // The reason the funnel resets rather than registers: the next document
        // must not paint with the last project's assets.
        var vm = new MainViewModel(null);
        var project = ProjectIo.Create("Knight", _root);
        var sword = Sword();
        project.Symbols[sword.Id] = sword;
        vm.ProjectDocker.Project = project;
        vm.RefreshProjectResources();

        vm.ProjectDocker.Project = null;
        vm.RefreshProjectResources();

        Assert.Null(SymbolRegistry.Resolve(sword.Id));
    }

    [AvaloniaFact]
    public void DeletingASymbolStopsItResolving()
    {
        var vm = new MainViewModel(null);
        var project = ProjectIo.Create("Knight", _root);
        var sword = Sword();
        project.Symbols[sword.Id] = sword;
        vm.ProjectDocker.Project = project;
        vm.RefreshProjectResources();

        project.Symbols.Remove(sword.Id);
        vm.RefreshProjectResources();

        Assert.Null(SymbolRegistry.Resolve(sword.Id));
    }
}

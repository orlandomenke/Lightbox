using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// Q30: declaring a scope from the project docker, and the two recolour defects
/// that scoping made worth fixing (B102, B103).
/// </summary>
[Collection("BrushState")]
public sealed class ScopeDeclarationTests(ITestOutputHelper output) : BrushStateIsolated, IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-scope-{Guid.NewGuid():N}.lbproj");

    private readonly List<MainViewModel> _built = [];

    public new void Dispose()
    {
        foreach (var vm in _built) vm.ProjectDocker.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        base.Dispose();
    }

    private MainViewModel Vm()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        _built.Add(vm);
        vm.NewProject(_root, "Production");
        return vm;
    }

    /// <summary>Sharing a palette onto a folder is what scopes the project.</summary>
    /// <remarks>
    /// The moment an old project becomes a new one: before the first
    /// declaration every palette is offered to every document, after it the
    /// resolver is taken at its word. Reversible by taking the last one back,
    /// because a project with no declarations reads as unscoped.
    /// </remarks>
    [AvaloniaFact]
    public void SharingAPaletteOntoAFolderScopesTheProject()
    {
        var vm = Vm();
        var docker = vm.ProjectDocker;
        var manifest = docker.Project!.Manifest;
        Assert.False(PaletteScopes.AnyDeclared(manifest));

        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Knight");
        var palette = Assert.Single(docker.ShareablePalettes);
        output.WriteLine($"sharing “{palette.Name}” with {docker.ShareScopeLabel}");

        docker.SharePalette(palette.Id);

        Assert.True(PaletteScopes.AnyDeclared(manifest));
        var declared = Assert.Single(docker.DeclarationsOnSelected);
        Assert.Equal(palette.Id, declared.Id);
        Assert.Equal(ResourceReach.Subtree, declared.ReachOrDefault);

        // And taking it back leaves the project as it was, rather than scoped
        // to nothing — which would hide every swatch from every document.
        docker.UnshareDeclaration(declared);
        Assert.False(PaletteScopes.AnyDeclared(manifest));
        Assert.Empty(docker.DeclarationsOnSelected);
    }

    /// <summary>Sharing the same palette twice does not declare it twice.</summary>
    [AvaloniaFact]
    public void SharingTheSamePaletteTwiceIsOneDeclaration()
    {
        var vm = Vm();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Knight");
        var palette = docker.ShareablePalettes[0];

        docker.SharePalette(palette.Id);
        docker.SharePalette(palette.Id);
        Assert.Single(docker.DeclarationsOnSelected);
    }

    /// <summary>Promoting a declaration lets the whole project see it.</summary>
    [AvaloniaFact]
    public void PromotingADeclarationReachesTheWholeProject()
    {
        var vm = Vm();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Library");
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Library");
        docker.SharePalette(docker.ShareablePalettes[0].Id);

        var declared = Assert.Single(docker.DeclarationsOnSelected);
        Assert.Equal(ResourceReach.Subtree, declared.ReachOrDefault);
        docker.PromoteDeclaration(declared);
        Assert.Equal(ResourceReach.Project, declared.ReachOrDefault);
    }

    /// <summary>
    /// <b>B103.</b> Undoing a recolour of a <em>project</em> palette restores it.
    /// </summary>
    /// <remarks>
    /// Deliberately a project palette rather than a document one. The document
    /// case passed while this was broken, because the undo step walked
    /// <c>doc.Palettes</c> — a test that used one would have gone on passing and
    /// said nothing.
    /// </remarks>
    [AvaloniaFact]
    public void UndoingARecolourOfAProjectPaletteRestoresIt()
    {
        var vm = Vm();
        var palette = Assert.Single(vm.ProjectDocker.Project!.Palettes);
        var swatch = palette.Swatches[0];
        var before = swatch.Color;

        // Through the docker's own row, which is how the colour wheel drives it:
        // the swatch object is mutated in place and the edit is closed off as
        // one undo step. That in-place mutation is exactly what made the bug
        // invisible until somebody pressed undo.
        var row = Assert.Single(vm.PaletteDocker.Swatches, r => r.Id == swatch.Id);
        row.Color = "#123456";
        vm.CommitSwatchEdit();
        Assert.Equal("#123456", swatch.Color);

        vm.UndoCommand.Execute(null);
        output.WriteLine($"{before} -> #123456 -> {swatch.Color}");
        Assert.Equal(before, swatch.Color);
    }
}

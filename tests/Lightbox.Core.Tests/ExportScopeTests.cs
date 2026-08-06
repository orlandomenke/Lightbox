using Lightbox.Core.Projects;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// Export scoping: which preset applies, and where one artifact ends.
/// </summary>
public class ExportScopeTests(ITestOutputHelper output)
{
    private static (ProjectManifest M, ProjectFolder Knight, ProjectFolder Locomotion,
                    DocumentRef Walk) Production()
    {
        var m = new ProjectManifest { Name = "Production" };
        var characters = ProjectFolders.Add(m, "characters");
        var knight = ProjectFolders.Add(m, "knight", characters);
        var locomotion = ProjectFolders.Add(m, "locomotion", knight);
        var walk = new DocumentRef { Name = "walk", Path = "d/walk.json", FolderId = locomotion.Id };
        m.Documents.Add(walk);
        return (m, knight, locomotion, walk);
    }

    /// <summary>A project that declares nothing keeps the user's presets.</summary>
    [Fact]
    public void AProjectThatDeclaresNoPresetIsNotScoped()
    {
        var (m, _, _, walk) = Production();
        Assert.False(ExportScopes.AnyDeclared(m));
        Assert.Null(ExportScopes.PresetFor(m, walk));
    }

    /// <summary>The nearest declaration wins, because a document exports one way.</summary>
    [Fact]
    public void TheNearestPresetWins()
    {
        var (m, knight, locomotion, walk) = Production();
        ExportScopes.SetPreset(m, knight, "sheet-knight");
        Assert.Equal("sheet-knight", ExportScopes.PresetFor(m, walk));

        ExportScopes.SetPreset(m, locomotion, "sheet-locomotion");
        output.WriteLine($"walk exports with {ExportScopes.PresetFor(m, walk)}");
        Assert.Equal("sheet-locomotion", ExportScopes.PresetFor(m, walk));
    }

    /// <summary>Setting a scope's preset replaces rather than accumulates.</summary>
    [Fact]
    public void AScopeExportsOneWay()
    {
        var (m, knight, _, _) = Production();
        ExportScopes.SetPreset(m, knight, "a");
        ExportScopes.SetPreset(m, knight, "b");
        Assert.Single(knight.Resources!.Where(r => r.Kind == ExportScopes.Kind));
        Assert.Equal("b", knight.Resources!.Single(r => r.Kind == ExportScopes.Kind).Id);
    }

    /// <summary>
    /// The declaring folder is the artifact boundary, so a status change knows
    /// what it invalidated.
    /// </summary>
    /// <remarks>
    /// The part that makes this more than a settings lookup. Without a boundary,
    /// "one animation reached Ready" has nothing it can name as needing rebuild.
    /// </remarks>
    [Fact]
    public void TheDeclaringFolderIsTheArtifactBoundary()
    {
        var (m, knight, locomotion, walk) = Production();
        Assert.Null(ExportScopes.BoundaryFor(m, walk));

        ExportScopes.SetPreset(m, knight, "sheet-knight");
        Assert.Same(knight, ExportScopes.BoundaryFor(m, walk));

        // Declaring nearer moves the boundary in — locomotion becomes its own sheet.
        ExportScopes.SetPreset(m, locomotion, "sheet-locomotion");
        output.WriteLine($"boundary is now {ExportScopes.BoundaryFor(m, walk)!.Name}");
        Assert.Same(locomotion, ExportScopes.BoundaryFor(m, walk));
    }

    /// <summary>A preset with no filter admits everything, including no status.</summary>
    /// <remarks>
    /// Null rather than every value listed, so a preset that does not filter
    /// writes no key and cannot fall out of step with the enum.
    /// </remarks>
    [Fact]
    public void APresetWithNoFilterAdmitsEverything()
    {
        var preset = new ExportPreset { Name = "Sheet" };
        Assert.False(preset.Filters);
        Assert.True(preset.Admits(null));
        Assert.True(preset.Admits(AssetStatus.Draft));
        Assert.True(preset.Admits(AssetStatus.Ready));
    }

    /// <summary>A filtered preset keeps work in progress out of the deliverable.</summary>
    [Fact]
    public void AFilteredPresetAdmitsOnlyTheStatusesItLists()
    {
        var preset = new ExportPreset { Name = "Shipped", IncludeStatuses = [AssetStatus.Ready] };
        Assert.True(preset.Filters);
        Assert.True(preset.Admits(AssetStatus.Ready));
        Assert.False(preset.Admits(AssetStatus.Draft));
        Assert.False(preset.Admits(AssetStatus.Reopened));
        // An unset status is not Ready, so it stays out rather than defaulting in.
        Assert.False(preset.Admits(null));
    }

    /// <summary>A document's version moves when it is saved, and staleness is a comparison.</summary>
    /// <remarks>
    /// Symbol.Version against SeenVersion, applied to a document. The artifact
    /// records what it was built from; any difference means stale.
    /// </remarks>
    [Fact]
    public void StalenessIsTwoIntegersDiffering()
    {
        var (_, _, _, walk) = Production();
        var builtFrom = walk.Version;
        Assert.Equal(builtFrom, walk.Version);

        walk.Version++; // the artist saved it again
        output.WriteLine($"artifact built from v{builtFrom}, document now v{walk.Version}");
        Assert.NotEqual(builtFrom, walk.Version);
    }

    /// <summary>Grouping defaults to today's behaviour.</summary>
    [Fact]
    public void GroupingDefaultsToOneArtifactPerDocument()
    {
        Assert.Equal(ExportGrouping.PerDocument, new ExportPreset().Grouping);
        Assert.Null(new ExportPreset().RebuildOn);
        Assert.Null(new ExportPreset().IncludeStatuses);
    }
}

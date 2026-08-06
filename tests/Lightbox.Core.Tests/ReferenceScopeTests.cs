using System.Text.Json;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// Q30 step 3: a reference is a declaration at a scope naming something to draw
/// against, and what it points at can be three different shapes.
/// </summary>
/// <remarks>
/// The character sheet's redesign turned out to be mostly this. The
/// <c>ReferenceSheet</c> record was already generic — a named set of views with
/// static layer stacks, nothing about characters in it. What was not generic was
/// where a sheet could live, and what a reference could point at.
/// </remarks>
public class ReferenceScopeTests(ITestOutputHelper output)
{
    private static (ProjectManifest Manifest, ProjectFolder Knight, ProjectFolder Environments,
                    DocumentRef Attack) Production()
    {
        var manifest = new ProjectManifest { Name = "Production" };
        var characters = ProjectFolders.Add(manifest, "characters");
        var knight = ProjectFolders.Add(manifest, "knight", characters);
        var combat = ProjectFolders.Add(manifest, "combat", knight);
        var environments = ProjectFolders.Add(manifest, "environments");

        var attack = new DocumentRef
        {
            Name = "attack", Path = "documents/attack.lightbox.json", FolderId = combat.Id,
        };
        manifest.Documents.Add(attack);
        return (manifest, knight, environments, attack);
    }

    /// <summary>
    /// Workflow 1: a sheet on the knight folder serves every knight drawing.
    /// </summary>
    /// <remarks>
    /// The reporter's words were *"a character sheet at the knight folder is
    /// valuable as all knight files have direct access to it"*. Unbuildable
    /// before, because a sheet lived inside one document and could not be filed
    /// anywhere.
    /// </remarks>
    [Fact]
    public void ASheetOnTheKnightFolderServesEveryDrawingUnderIt()
    {
        var (manifest, knight, _, attack) = Production();
        ReferenceScopes.Declare(manifest, knight, "sheet-knight", ReferenceTargets.Sheet);

        var found = Assert.Single(ReferenceScopes.VisibleTo(manifest, attack)!);
        output.WriteLine($"attack sees {found.Id} ({found.Target})");
        Assert.Equal("sheet-knight", found.Id);
        Assert.Equal(ReferenceTargets.Sheet, found.Target);
    }

    /// <summary>
    /// Workflow 3: one environment document, reachable from another branch.
    /// </summary>
    /// <remarks>
    /// *"Both the environment assets and the characters could benefit, so
    /// project-wide distribution proves valuable."* The document lives in
    /// <c>environments/</c> and belongs there — filing it at the root to make it
    /// visible would make the tree lie about what it is.
    /// </remarks>
    [Fact]
    public void APublishedEnvironmentDocumentReachesACharactersDrawing()
    {
        var (manifest, _, environments, attack) = Production();
        var overview = ReferenceScopes.Declare(
            manifest, environments, "env-overview", ReferenceTargets.Document);

        Assert.Empty(ReferenceScopes.VisibleTo(manifest, attack) ?? []);

        ResourceScopes.Promote(overview);
        var found = Assert.Single(ReferenceScopes.VisibleTo(manifest, attack)!);
        output.WriteLine($"after promoting: {found.Id} ({found.Target}) reaches the knight");
        Assert.Equal(ReferenceTargets.Document, found.Target);
    }

    /// <summary>All three shapes coexist, and can be asked for separately.</summary>
    /// <remarks>
    /// Workflow 1 wants a multi-view sheet and workflow 3 wants one large
    /// document, and those are different shapes rather than one being a bigger
    /// version of the other. A panel that shows sheets should not have to filter
    /// documents out by hand.
    /// </remarks>
    [Fact]
    public void ReferencesOfDifferentTargetsAreResolvedSeparately()
    {
        var (manifest, knight, _, attack) = Production();
        ReferenceScopes.Declare(manifest, knight, "sheet-knight", ReferenceTargets.Sheet);
        ReferenceScopes.Declare(manifest, knight, "photo-armour", ReferenceTargets.Image);
        ReferenceScopes.Declare(manifest, null, "style-guide", ReferenceTargets.Document);

        Assert.Equal(3, ReferenceScopes.VisibleTo(manifest, attack)!.Count);
        Assert.Equal("sheet-knight",
            Assert.Single(ReferenceScopes.OfTarget(manifest, attack, ReferenceTargets.Sheet)).Id);
        Assert.Equal("photo-armour",
            Assert.Single(ReferenceScopes.OfTarget(manifest, attack, ReferenceTargets.Image)).Id);
        Assert.Equal("style-guide",
            Assert.Single(ReferenceScopes.OfTarget(manifest, attack, ReferenceTargets.Document)).Id);
    }

    /// <summary>References accumulate down the tree, nearest first.</summary>
    [Fact]
    public void ReferencesAccumulateFromEveryScopeAbove()
    {
        var (manifest, knight, _, attack) = Production();
        var combat = ProjectFolders.ById(manifest, attack.FolderId)!;

        ReferenceScopes.Declare(manifest, null, "style-guide", ReferenceTargets.Document);
        ReferenceScopes.Declare(manifest, knight, "sheet-knight", ReferenceTargets.Sheet);
        ReferenceScopes.Declare(manifest, combat, "weapon-poses", ReferenceTargets.Sheet);

        var ids = ReferenceScopes.VisibleTo(manifest, attack)!.Select(r => r.Id).ToList();
        output.WriteLine($"resolved: {string.Join(" > ", ids)}");
        Assert.Equal(["weapon-poses", "sheet-knight", "style-guide"], ids);
    }

    /// <summary>
    /// A project that declares no references is not filtered — it keeps reading
    /// a character's own list, exactly as before.
    /// </summary>
    [Fact]
    public void AProjectThatDeclaresNoReferencesIsNotFiltered()
    {
        var (manifest, _, _, attack) = Production();
        Assert.False(ReferenceScopes.AnyDeclared(manifest));
        Assert.Null(ReferenceScopes.VisibleTo(manifest, attack));
    }

    /// <summary>
    /// A target is written only where a kind needs one.
    /// </summary>
    /// <remarks>
    /// A palette id is a palette and needs no second word; a reference does. The
    /// half of "optional" only a serialize-and-look catches.
    /// </remarks>
    [Fact]
    public void OnlyAReferenceWritesATarget()
    {
        var (manifest, knight, _, _) = Production();
        ResourceScopes.Declare(manifest, knight, PaletteScopes.Kind, "pal-knight");
        var json = JsonSerializer.Serialize(manifest, DocJson.Options);
        Assert.DoesNotContain("\"target\"", json);

        ReferenceScopes.Declare(manifest, knight, "sheet-knight", ReferenceTargets.Sheet);
        json = JsonSerializer.Serialize(manifest, DocJson.Options);
        output.WriteLine(json.Contains("\"target\"") ? "target written for the reference" : "absent");
        Assert.Contains("\"target\"", json);

        var back = JsonSerializer.Deserialize<ProjectManifest>(json, DocJson.Options)!;
        var sheet = ProjectFolders.All(back)
            .SelectMany(f => f.Resources ?? [])
            .Single(r => r.Id == "sheet-knight");
        Assert.Equal(ReferenceTargets.Sheet, sheet.Target);
    }

    /// <summary>Palettes and references do not see each other.</summary>
    [Fact]
    public void ReferencesAndPalettesResolveIndependently()
    {
        var (manifest, knight, _, attack) = Production();
        ResourceScopes.Declare(manifest, knight, PaletteScopes.Kind, "pal-knight");
        ReferenceScopes.Declare(manifest, knight, "sheet-knight", ReferenceTargets.Sheet);

        Assert.Equal(["pal-knight"], PaletteScopes.VisibleTo(manifest, attack));
        Assert.Equal("sheet-knight", Assert.Single(ReferenceScopes.VisibleTo(manifest, attack)!).Id);
    }
}

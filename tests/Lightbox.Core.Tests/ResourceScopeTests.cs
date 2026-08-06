using System.Text.Json;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// Q30's resolution rule: resources hang on scopes and accumulate down the tree,
/// with the nearest declaration winning ties.
/// </summary>
/// <remarks>
/// Driven by the four workflows in <c>docs/DESIGN-scoped-resources.md</c>. The
/// first two are the same knight at two tree depths, which is what pins
/// walk-up-until-found rather than naming a level; the second two need a reach
/// a cascade cannot express.
/// </remarks>
public class ResourceScopeTests(ITestOutputHelper output)
{
    private static (ProjectManifest Manifest, ProjectFolder Knight, DocumentRef Doc) Knight(int depth)
    {
        var manifest = new ProjectManifest { Name = "Production" };
        var characters = ProjectFolders.Add(manifest, "characters");
        var knight = ProjectFolders.Add(manifest, "knight", characters);

        var at = knight;
        for (var i = 0; i < depth; i++) at = ProjectFolders.Add(manifest, $"level {i}", at);

        var doc = new DocumentRef { Name = "walk", Path = "documents/walk.lightbox.json", FolderId = at.Id };
        manifest.Documents.Add(doc);
        return (manifest, knight, doc);
    }

    /// <summary>Workflows 1 and 2: the same declaration, at two tree depths.</summary>
    /// <remarks>
    /// The whole reason both workflows were supplied. The artist's gesture is
    /// identical — *the palette belongs to the knight* — so resolution has to be
    /// identical whether the animations sit directly under the knight or two
    /// folders down. Anything that named a level would pass one of these and
    /// fail the other.
    /// </remarks>
    [Theory]
    [InlineData(0)] // knight/walk.lightbox      — workflow 2
    [InlineData(1)] // knight/locomotion/walk    — workflow 1
    [InlineData(4)] // and a tree nobody planned for
    public void APaletteOnTheKnightReachesItsAnimationsAtAnyDepth(int depth)
    {
        var (manifest, knight, doc) = Knight(depth);
        ResourceScopes.Declare(manifest, knight, "palette", "pal-knight");

        var found = ResourceScopes.Nearest(manifest, doc, "palette");
        output.WriteLine($"depth {depth}: resolved {found?.Id ?? "nothing"}");
        Assert.Equal("pal-knight", found?.Id);
    }

    /// <summary>Splitting a folder in two must not break resolution.</summary>
    /// <remarks>
    /// The property that makes the tree safe to rearrange, and the one the
    /// rejected *explicit per document* option would have lost: an artist who
    /// reorganises has not re-authored anything.
    /// </remarks>
    [Fact]
    public void AddingALevelBetweenTheScopeAndTheDocumentChangesNothing()
    {
        var (manifest, knight, doc) = Knight(1);
        ResourceScopes.Declare(manifest, knight, "palette", "pal-knight");
        Assert.Equal("pal-knight", ResourceScopes.Nearest(manifest, doc, "palette")?.Id);

        // The artist splits locomotion into ground and air.
        var deeper = ProjectFolders.Add(manifest, "ground", ProjectFolders.ById(manifest, doc.FolderId));
        doc.FolderId = deeper.Id;

        Assert.Equal("pal-knight", ResourceScopes.Nearest(manifest, doc, "palette")?.Id);
    }

    /// <summary>Nearest wins, and everything above it is still visible.</summary>
    [Fact]
    public void DeclarationsAccumulateAndTheNearestWinsATie()
    {
        var (manifest, knight, doc) = Knight(1);
        var shot = ProjectFolders.ById(manifest, doc.FolderId)!;

        ResourceScopes.Declare(manifest, null, "palette", "studio");
        ResourceScopes.Declare(manifest, knight, "palette", "knight");
        ResourceScopes.Declare(manifest, shot, "palette", "shot-extras");

        var all = ResourceScopes.Resolve(manifest, doc, "palette").Select(r => r.Id).ToList();
        output.WriteLine($"resolved: {string.Join(" > ", all)}");

        // All three are in force — the studio palette is not shadowed, it is
        // simply further away.
        Assert.Equal(["shot-extras", "knight", "studio"], all);
    }

    /// <summary>The same id declared twice resolves to the nearer one.</summary>
    [Fact]
    public void TheSameIdDeclaredTwiceResolvesToTheNearer()
    {
        var (manifest, knight, doc) = Knight(1);
        ResourceScopes.Declare(manifest, null, "palette", "shared");
        var near = ResourceScopes.Declare(manifest, knight, "palette", "shared");

        var all = ResourceScopes.Resolve(manifest, doc, "palette");
        Assert.Same(near, Assert.Single(all));
    }

    /// <summary>Workflows 3 and 4: a resource that lives here and reaches everywhere.</summary>
    /// <remarks>
    /// The environment reference filed under <c>environments/</c> that a knight
    /// animation needs, and the sword in the asset library that characters,
    /// environments and props all place. Neither folder is an ancestor of what
    /// wants it, so a cascade cannot reach — which is why reach exists.
    /// </remarks>
    [Fact]
    public void APublishedResourceReachesADocumentInAnotherBranch()
    {
        var (manifest, _, doc) = Knight(1);
        var environments = ProjectFolders.Add(manifest, "environments");

        var unpublished = ResourceScopes.Declare(manifest, environments, "reference", "env-overview");
        Assert.Empty(ResourceScopes.Resolve(manifest, doc, "reference"));

        ResourceScopes.Promote(unpublished);
        var found = Assert.Single(ResourceScopes.Resolve(manifest, doc, "reference"));
        output.WriteLine($"after promoting: {found.Id} reaches the knight");
        Assert.Equal("env-overview", found.Id);
    }

    /// <summary>A nearer subtree declaration beats a published one.</summary>
    /// <remarks>
    /// Locality is the tie-break, so a knight's own red overrides the studio's
    /// red without anything having to be unpublished.
    /// </remarks>
    [Fact]
    public void LocalityBeatsPublication()
    {
        var (manifest, knight, doc) = Knight(1);
        var library = ProjectFolders.Add(manifest, "library");
        ResourceScopes.Declare(manifest, library, "palette", "red", ResourceReach.Project);
        var local = ResourceScopes.Declare(manifest, knight, "palette", "red");

        Assert.Same(local, ResourceScopes.Nearest(manifest, doc, "palette"));
    }

    /// <summary>Kinds do not see each other.</summary>
    [Fact]
    public void ResolvingOneKindIgnoresTheOthers()
    {
        var (manifest, knight, doc) = Knight(0);
        ResourceScopes.Declare(manifest, knight, "palette", "pal");
        ResourceScopes.Declare(manifest, knight, "reference", "sheet");

        Assert.Equal("pal", ResourceScopes.Nearest(manifest, doc, "palette")?.Id);
        Assert.Equal("sheet", ResourceScopes.Nearest(manifest, doc, "reference")?.Id);
        Assert.Null(ResourceScopes.Nearest(manifest, doc, "gradient"));
    }

    /// <summary>A document filed nowhere still sees the project's own scope.</summary>
    [Fact]
    public void ADocumentAtTheProjectRootStillResolves()
    {
        var manifest = new ProjectManifest { Name = "Production" };
        var doc = new DocumentRef { Name = "test", Path = "documents/test.lightbox.json" };
        manifest.Documents.Add(doc);
        ResourceScopes.Declare(manifest, null, "palette", "studio");

        Assert.Equal("studio", ResourceScopes.Nearest(manifest, doc, "palette")?.Id);
    }

    /// <summary>A project that declares nothing writes no keys at all.</summary>
    /// <remarks>
    /// The half of "optional" that is easy to miss, and the one that is only
    /// caught by serializing and looking. A folder tree that has never heard of
    /// scoped resources must round-trip byte-for-byte as it did before.
    /// </remarks>
    [Fact]
    public void AProjectThatDeclaresNothingWritesNoResourceKeys()
    {
        var (manifest, _, _) = Knight(1);
        var json = JsonSerializer.Serialize(manifest, DocJson.Options);
        output.WriteLine(json.Length > 400 ? json[..400] : json);

        Assert.DoesNotContain("\"resources\"", json);
        Assert.DoesNotContain("\"reach\"", json);
    }

    /// <summary>An ordinary declaration writes no reach key; a promoted one does.</summary>
    [Fact]
    public void OnlyAPromotedResourceWritesItsReach()
    {
        var (manifest, knight, _) = Knight(0);
        ResourceScopes.Declare(manifest, knight, "palette", "pal");
        Assert.DoesNotContain("\"reach\"", JsonSerializer.Serialize(manifest, DocJson.Options));

        ResourceScopes.Declare(manifest, knight, "reference", "env", ResourceReach.Project);
        var json = JsonSerializer.Serialize(manifest, DocJson.Options);
        output.WriteLine(json.Contains("\"reach\"") ? "reach written once promoted" : "reach absent");
        Assert.Contains("\"reach\"", json);

        // And it survives the trip, rather than reading back as the default.
        var back = JsonSerializer.Deserialize<ProjectManifest>(json, DocJson.Options)!;
        var published = ProjectFolders.All(back)
            .SelectMany(f => f.Resources ?? [])
            .Single(r => r.Id == "env");
        Assert.Equal(ResourceReach.Project, published.ReachOrDefault);
    }


    // ---- brush tips (Q30's loose end) ---------------------------------------

    /// <summary>
    /// A project that declares no tip scope offers every tip, as it always has.
    /// </summary>
    [Fact]
    public void WithNothingDeclaredEveryTipIsStillOffered()
    {
        var manifest = new ProjectManifest { Name = "Production" };
        var folder = ProjectFolders.Add(manifest, "Knight", null);
        var walk = new DocumentRef { Name = "Walk", FolderId = folder.Id };
        manifest.Documents.Add(walk);

        Assert.False(TipScopes.AnyDeclared(manifest));
        Assert.Null(TipScopes.VisibleTo(manifest, walk));
        Assert.True(TipScopes.CanUse(manifest, walk, "tip-anything"));
    }

    /// <summary>Declaring one narrows the picker to that subtree.</summary>
    [Fact]
    public void ATipDeclaredOnAFolderIsOfferedThereAndNotElsewhere()
    {
        var manifest = new ProjectManifest { Name = "Production" };
        var inkers = ProjectFolders.Add(manifest, "Inking", null);
        var painters = ProjectFolders.Add(manifest, "Paint", null);
        var line = new DocumentRef { Name = "Clean-up", FolderId = inkers.Id };
        var wash = new DocumentRef { Name = "Colour", FolderId = painters.Id };
        manifest.Documents.Add(line);
        manifest.Documents.Add(wash);

        ResourceScopes.Declare(manifest, inkers, TipScopes.Kind, "tip-nib");

        Assert.True(TipScopes.CanUse(manifest, line, "tip-nib"));
        Assert.False(TipScopes.CanUse(manifest, wash, "tip-nib"));
        Assert.Empty(TipScopes.VisibleTo(manifest, wash)!);
    }
}

using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// The model half of the project window: tags, people, and the counts its
/// footer reads.
/// </summary>
/// <remarks>
/// None of it reaches a pixel — tags, status and assignment are production
/// metadata about a drawing rather than part of it. That is what makes Q44's
/// "no undo" honest, and it is the first thing these tests establish.
/// </remarks>
public sealed class ProjectBoardTests
{
    private static (ProjectManifest M, ProjectFolder Knight, DocumentRef Walk, DocumentRef Idle) World()
    {
        var m = new ProjectManifest { Name = "Production" };
        var knight = ProjectFolders.Add(m, "Knight");
        var walk = new DocumentRef { Name = "walk", Path = "knight/walk.json", FolderId = knight.Id };
        var idle = new DocumentRef { Name = "idle", Path = "knight/idle.json", FolderId = knight.Id };
        m.Documents.AddRange([walk, idle]);
        return (m, knight, walk, idle);
    }

    // ---- absence ----------------------------------------------------------------

    [Fact]
    public void ADocumentNobodyTaggedOrAssignedWritesNeitherKey()
    {
        // Optional means absent, and the project window adds three fields at
        // once — so the check is on all three rather than on the one that
        // happened to be remembered.
        var (m, _, _, _) = World();

        var json = JsonSerializer.Serialize(m, DocJson.Options);

        Assert.DoesNotContain("\"tags\"", json);
        Assert.DoesNotContain("\"assigneeId\"", json);
        Assert.DoesNotContain("\"resources\"", json);
        Assert.DoesNotContain("\"people\"", json);
    }

    [Fact]
    public void TaggingAndUntaggingLeavesNothingBehind()
    {
        // Absent unless used, in both directions — the rule the folder glyph
        // follows, and the one an empty list quietly breaks.
        var (m, _, walk, _) = World();

        Assert.True(ProjectBoard.Tag(walk, "rough"));
        Assert.True(ProjectBoard.Untag(walk, "rough"));

        Assert.Null(walk.Tags);
        Assert.DoesNotContain("\"tags\"", JsonSerializer.Serialize(m, DocJson.Options));
    }

    // ---- tags --------------------------------------------------------------------

    [Fact]
    public void TheVocabularyIsEveryTagInUse()
    {
        // Derived rather than declared: typing a new tag adds it by using it,
        // and folders and documents share one vocabulary so "rough" means the
        // same thing wherever it is put.
        var (m, knight, walk, _) = World();
        ProjectBoard.Tag(knight, "hero");
        ProjectBoard.Tag(walk, "rough");
        ProjectBoard.Tag(walk, "locomotion");

        Assert.Equal(["hero", "locomotion", "rough"], ProjectBoard.TagsInUse(m));
    }

    [Fact]
    public void TheSameTagInTwoCasesIsOneTag()
    {
        // Two spellings of one word is the spreadsheet problem this window
        // exists to remove.
        var (m, knight, walk, _) = World();
        ProjectBoard.Tag(knight, "Rough");
        ProjectBoard.Tag(walk, "rough");

        Assert.Single(ProjectBoard.TagsInUse(m));
        Assert.False(ProjectBoard.Tag(walk, "ROUGH"));
        Assert.Single(walk.Tags!);
    }

    [Fact]
    public void ATagOnAFolderReachesEveryDocumentUnderIt()
    {
        // Q31's point: tagging `characters/` once is how "every character
        // animation" is expressible without listing them.
        var (m, knight, walk, _) = World();
        var locomotion = ProjectFolders.Add(m, "Locomotion", knight);
        var run = new DocumentRef { Name = "run", Path = "r.json", FolderId = locomotion.Id };
        m.Documents.Add(run);

        ProjectBoard.Tag(knight, "hero");
        ProjectBoard.Tag(run, "fast");

        Assert.Contains("hero", ProjectBoard.TagsReaching(m, walk));
        Assert.Contains("hero", ProjectBoard.TagsReaching(m, run));
        // The document's own come first — the order every other resolution uses.
        Assert.Equal("fast", ProjectBoard.TagsReaching(m, run)[0]);
        // And the folder's tag is not written onto the document.
        Assert.Null(walk.Tags);
    }

    [Fact]
    public void ALooseDocumentSeesOnlyItsOwnTags()
    {
        var (m, _, _, _) = World();
        var background = new DocumentRef { Name = "background", Path = "b.json" };
        m.Documents.Add(background);
        ProjectBoard.Tag(background, "paint");

        Assert.Equal(["paint"], ProjectBoard.TagsReaching(m, background));
    }

    // ---- people ------------------------------------------------------------------

    [Fact]
    public void AddingTheSamePersonTwiceIsOnePerson()
    {
        // The whole reason Q43 chose a registry over a typed name.
        var (m, _, _, _) = World();

        var first = ProjectBoard.AddPerson(m, "Ana");
        var again = ProjectBoard.AddPerson(m, "ana");

        Assert.Same(first, again);
        Assert.Single(ProjectBoard.People(m));
    }

    [Fact]
    public void RenamingAPersonFixesEveryRowAtOnce()
    {
        // The other half of that reason: documents point at an id, so the name
        // is stored once and a correction reaches everything naming it.
        var (m, _, walk, idle) = World();
        var ana = ProjectBoard.AddPerson(m, "Ana");
        ProjectBoard.Assign(walk, ana);
        ProjectBoard.Assign(idle, ana);

        ana.Name = "Ana Ruiz";

        Assert.All(
            new[] { walk, idle },
            d => Assert.Equal("Ana Ruiz", ProjectBoard.PersonById(m, d.AssigneeId)!.Name));
    }

    [Fact]
    public void RemovingSomebodySaysHowManyDocumentsNamedThem()
    {
        // Q35's pattern pointed at people: the gesture names what it costs
        // rather than asking a bare "are you sure".
        var (m, _, walk, idle) = World();
        var ana = ProjectBoard.AddPerson(m, "Ana");
        ProjectBoard.Assign(walk, ana);
        ProjectBoard.Assign(idle, ana);

        Assert.Equal(2, ProjectBoard.AssignedCount(m, ana));

        var unassigned = ProjectBoard.RemovePerson(m, ana);

        Assert.Equal(2, unassigned);
        // Unassigned rather than left dangling: a row saying somebody is working
        // on it who is not is worse than a row saying nobody is.
        Assert.Null(walk.AssigneeId);
        Assert.Null(idle.AssigneeId);
        Assert.Null(m.People);
    }

    [Fact]
    public void APersonCarriesNoRoleAndNoRights()
    {
        // Q45, asserted rather than left to a doc comment. The manifest is plain
        // JSON on disk, so a permission here is one a text editor defeats — and
        // a permission that cannot be enforced is a UI that lies.
        var names = typeof(Person).GetProperties().Select(p => p.Name).ToList();

        Assert.Equal(["Id", "Name", "Color"], names);
    }

    // ---- the footer's counts -------------------------------------------------------

    [Fact]
    public void TheSummaryCountsWhatIsTrueRatherThanTheWholeEnum()
    {
        var (m, _, walk, idle) = World();
        walk.Status = AssetStatus.Ready;
        idle.Status = AssetStatus.Ready;
        var rough = new DocumentRef { Name = "rough", Path = "r.json", Status = AssetStatus.Reopened };
        var untouched = new DocumentRef { Name = "untouched", Path = "u.json" };
        m.Documents.AddRange([rough, untouched]);
        ProjectBoard.Assign(walk, ProjectBoard.AddPerson(m, "Ana"));

        var summary = ProjectBoard.Summarise(m);

        Assert.Equal(4, summary.Documents);
        Assert.Equal(2, summary.ByStatus[AssetStatus.Ready]);
        Assert.Equal(1, summary.ByStatus[AssetStatus.Reopened]);
        // A status nobody used is absent, so the footer lists what is true.
        Assert.DoesNotContain(AssetStatus.Draft, summary.ByStatus.Keys);
        Assert.Equal(1, summary.Unset);
        Assert.Equal(3, summary.Unassigned);
    }

    [Fact]
    public void NobodyHasSaidIsItsOwnColumnRatherThanDesign()
    {
        // The distinction DocumentRef.Status exists to keep: a project imported
        // from loose files has no statuses, and calling them all Design would be
        // a guess.
        var (m, _, walk, idle) = World();
        walk.Status = AssetStatus.Design;

        Assert.Equal(walk.Id, Assert.Single(ProjectBoard.At(m, AssetStatus.Design)).Id);
        Assert.Equal(idle.Id, Assert.Single(ProjectBoard.At(m, null)).Id);
    }

    // ---- the four-tier chain (the Assets tab's whole reason) -------------------------

    [Fact]
    public void TheChainIsFourDeepAndNearestWins()
    {
        // ResourceScopes always described this chain and only had the middle
        // two: "this one drawing paints from that palette" had no target, and an
        // artist's own library was reachable by one kind and no mechanism.
        var (m, knight, walk, _) = World();
        var user = new List<ScopedResource> { new() { Id = "mine", Kind = PaletteScopes.Kind } };
        ResourceScopes.Declare(m, null, PaletteScopes.Kind, "studio");
        ResourceScopes.Declare(m, knight, PaletteScopes.Kind, "knight");
        ResourceScopes.DeclareOn(walk, PaletteScopes.Kind, "just-this-one");

        var chain = ResourceScopes.Resolve(m, walk, PaletteScopes.Kind, user);

        Assert.Equal(["just-this-one", "knight", "studio", "mine"], chain.Select(r => r.Id));
        Assert.Equal("just-this-one", ResourceScopes.Nearest(m, walk, PaletteScopes.Kind, user)!.Id);
    }

    [Fact]
    public void AProjectOverridesTheArtistsOwnDefault()
    {
        // The precedence falls out of the order rather than being decided, which
        // is the sentence `DESIGN-project-scoping.md` wanted and could not have.
        var (m, _, walk, _) = World();
        var user = new List<ScopedResource> { new() { Id = "mine", Kind = PaletteScopes.Kind } };
        ResourceScopes.Declare(m, null, PaletteScopes.Kind, "studio");

        Assert.Equal("studio", ResourceScopes.Nearest(m, walk, PaletteScopes.Kind, user)!.Id);
    }

    [Fact]
    public void WithNoUserLibraryTheChainIsExactlyWhatItAlwaysWas()
    {
        // The new tiers must cost nothing to every caller that has neither.
        var (m, knight, walk, _) = World();
        ResourceScopes.Declare(m, knight, PaletteScopes.Kind, "knight");

        Assert.Equal(
            ResourceScopes.Resolve(m, walk, PaletteScopes.Kind).Select(r => r.Id),
            ResourceScopes.Resolve(m, walk, PaletteScopes.Kind, user: null).Select(r => r.Id));
    }

    [Fact]
    public void ADocumentDeclarationWritesNoReachKey()
    {
        // Reach is meaningless on a document — there is nothing below it to
        // subtree into — so it is left null rather than defaulted.
        var (m, _, walk, _) = World();
        ResourceScopes.DeclareOn(walk, PaletteScopes.Kind, "just-this-one");

        var json = JsonSerializer.Serialize(m, DocJson.Options);

        Assert.Contains("just-this-one", json);
        Assert.DoesNotContain("\"reach\"", json);
    }

    // ---- it is metadata, which is what makes "no undo" honest -------------------------

    [Fact]
    public void NothingHereNeedsTheDocumentOpen()
    {
        // Q44's premise. Status, tags and assignment are set on the manifest
        // entry, so a bulk edit over forty rows reads no artwork file at all —
        // which is also why it cannot alter what re-renders.
        var (m, _, walk, _) = World();

        walk.Status = AssetStatus.Ready;
        ProjectBoard.Tag(walk, "rough");
        ProjectBoard.Assign(walk, ProjectBoard.AddPerson(m, "Ana"));

        // A Project with nothing loaded — no Doc was ever read.
        var project = new Project(m, "/nowhere");
        Assert.Empty(project.Loaded);
    }
}

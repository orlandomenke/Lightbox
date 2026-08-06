using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// Guide sets: the record step 4 needed and did not have, and the scoping it
/// unblocks.
/// </summary>
/// <remarks>
/// The roadmap's <c>[?] Character height guide</c> is exactly this — a guide set
/// declared on a character's folder — and there was never another thing it could
/// have been.
/// </remarks>
public class GuideScopeTests(ITestOutputHelper output)
{
    private static (ProjectManifest Manifest, ProjectFolder Knight, DocumentRef Walk) Production()
    {
        var manifest = new ProjectManifest { Name = "Production" };
        var knight = ProjectFolders.Add(manifest, "knight");
        var locomotion = ProjectFolders.Add(manifest, "locomotion", knight);
        var walk = new DocumentRef
        {
            Name = "walk", Path = "documents/walk.lightbox.json", FolderId = locomotion.Id,
        };
        manifest.Documents.Add(walk);
        return (manifest, knight, walk);
    }

    private static GuideSet HeightGuide(ProjectManifest manifest)
    {
        var set = new GuideSet { Name = "Knight height" };
        set.Guides.Add(new Guide { Kind = GuideKind.Line, Y = 900 });
        set.Guides.Add(new Guide { Kind = GuideKind.Line, Y = 120 });
        (manifest.GuideSets ??= []).Add(set);
        return set;
    }

    /// <summary>A height guide on the knight reaches every drawing under it.</summary>
    [Fact]
    public void AGuideSetOnAFolderReachesEveryDocumentUnderIt()
    {
        var (manifest, knight, walk) = Production();
        Assert.Null(GuideScopes.VisibleTo(manifest, walk));

        var set = HeightGuide(manifest);
        ResourceScopes.Declare(manifest, knight, GuideScopes.Kind, set.Id);

        var visible = GuideScopes.VisibleTo(manifest, walk);
        output.WriteLine($"walk sees [{string.Join(",", visible!)}] — “{set.Name}”");
        Assert.Equal([set.Id], visible);
    }

    /// <summary>One character's height guide is not another's.</summary>
    [Fact]
    public void AGuideSetOnOneCharacterIsNotVisibleToAnother()
    {
        var (manifest, knight, walk) = Production();
        var goblin = ProjectFolders.Add(manifest, "goblin");
        var lurk = new DocumentRef { Name = "lurk", Path = "documents/lurk.json", FolderId = goblin.Id };
        manifest.Documents.Add(lurk);

        var set = HeightGuide(manifest);
        ResourceScopes.Declare(manifest, knight, GuideScopes.Kind, set.Id);

        Assert.Equal([set.Id], GuideScopes.VisibleTo(manifest, walk));
        Assert.Empty(GuideScopes.VisibleTo(manifest, lurk)!);
    }

    /// <summary>A studio guide set published from anywhere reaches everything.</summary>
    [Fact]
    public void APublishedGuideSetReachesEveryDocument()
    {
        var (manifest, _, walk) = Production();
        var shared = ProjectFolders.Add(manifest, "shared");
        var set = HeightGuide(manifest);
        var declared = ResourceScopes.Declare(manifest, shared, GuideScopes.Kind, set.Id);

        Assert.Empty(GuideScopes.VisibleTo(manifest, walk)!);
        ResourceScopes.Promote(declared);
        Assert.Equal([set.Id], GuideScopes.VisibleTo(manifest, walk));
    }

    /// <summary>A project with no guide sets writes no key.</summary>
    /// <remarks>
    /// The half of "optional" that only shows up in the JSON. A project that has
    /// never shared a guide must serialize exactly as it did before this record
    /// existed.
    /// </remarks>
    [Fact]
    public void AProjectWithNoGuideSetsWritesNoKey()
    {
        var (manifest, _, _) = Production();
        var json = JsonSerializer.Serialize(manifest, DocJson.Options);
        Assert.DoesNotContain("\"guideSets\"", json);

        HeightGuide(manifest);
        json = JsonSerializer.Serialize(manifest, DocJson.Options);
        output.WriteLine(json.Contains("\"guideSets\"") ? "written once a set exists" : "absent");
        Assert.Contains("\"guideSets\"", json);

        var back = JsonSerializer.Deserialize<ProjectManifest>(json, DocJson.Options)!;
        var set = Assert.Single(back.GuideSets!);
        Assert.Equal("Knight height", set.Name);
        Assert.Equal(2, set.Guides.Count);
    }
}

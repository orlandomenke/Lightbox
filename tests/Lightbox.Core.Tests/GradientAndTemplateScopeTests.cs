using Lightbox.Core.Projects;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// Q30 step 4: gradients and default templates join the resolver by naming a
/// kind rather than by growing machinery of their own.
/// </summary>
/// <remarks>
/// Which is the test of whether step 1 was the right shape. Two of step 4's four
/// could not join, and for the same reason both times — guides and export
/// configuration have no project-level record with an id to point at.
/// </remarks>
public class GradientAndTemplateScopeTests(ITestOutputHelper output)
{
    private static (ProjectManifest Manifest, ProjectFolder Knight, ProjectFolder Locomotion,
                    DocumentRef Walk) Production()
    {
        var manifest = new ProjectManifest { Name = "Production" };
        var knight = ProjectFolders.Add(manifest, "knight");
        var locomotion = ProjectFolders.Add(manifest, "locomotion", knight);
        var walk = new DocumentRef
        {
            Name = "walk", Path = "documents/walk.lightbox.json", FolderId = locomotion.Id,
        };
        manifest.Documents.Add(walk);
        return (manifest, knight, locomotion, walk);
    }

    /// <summary>Gradients scope exactly as palettes do.</summary>
    [Fact]
    public void AGradientOnAFolderReachesEveryDocumentUnderIt()
    {
        var (manifest, knight, _, walk) = Production();
        Assert.Null(GradientScopes.VisibleTo(manifest, walk));

        ResourceScopes.Declare(manifest, knight, GradientScopes.Kind, "grad-shield");
        var visible = GradientScopes.VisibleTo(manifest, walk);
        output.WriteLine($"walk sees [{string.Join(",", visible!)}]");
        Assert.Equal(["grad-shield"], visible);
    }

    /// <summary>A gradient on one character is not offered to another.</summary>
    [Fact]
    public void AGradientOnOneCharacterIsNotVisibleToAnother()
    {
        var (manifest, knight, _, walk) = Production();
        var goblin = ProjectFolders.Add(manifest, "goblin");
        var lurk = new DocumentRef { Name = "lurk", Path = "documents/lurk.json", FolderId = goblin.Id };
        manifest.Documents.Add(lurk);

        ResourceScopes.Declare(manifest, knight, GradientScopes.Kind, "grad-shield");
        Assert.Equal(["grad-shield"], GradientScopes.VisibleTo(manifest, walk));
        Assert.Empty(GradientScopes.VisibleTo(manifest, lurk)!);
    }

    /// <summary>
    /// Workflow 1's locomotion folder: new documents there start from its
    /// template.
    /// </summary>
    /// <remarks>
    /// Asked for a *scope* rather than for a document, because the document a
    /// template is about does not exist yet — that is the whole point of a
    /// default.
    /// </remarks>
    [Fact]
    public void ANewDocumentInAScopeStartsFromThatScopesTemplate()
    {
        var (manifest, knight, locomotion, _) = Production();
        Assert.Null(TemplateScopes.DefaultFor(manifest, locomotion));

        TemplateScopes.SetDefault(manifest, locomotion, "tpl-walk");
        Assert.Equal("tpl-walk", TemplateScopes.DefaultFor(manifest, locomotion));

        // Inherited from above where a folder sets none of its own.
        var combat = ProjectFolders.Add(manifest, "combat", knight);
        TemplateScopes.SetDefault(manifest, knight, "tpl-knight");
        Assert.Equal("tpl-knight", TemplateScopes.DefaultFor(manifest, combat));
        // …and the nearer one still wins where there is one.
        Assert.Equal("tpl-walk", TemplateScopes.DefaultFor(manifest, locomotion));
    }

    /// <summary>Setting a scope's template replaces rather than accumulates.</summary>
    /// <remarks>
    /// A scope has one default. Two declarations of the same kind on one folder
    /// would make which-one-wins depend on insertion order, which reads as
    /// random the first time it surprises somebody.
    /// </remarks>
    [Fact]
    public void SettingATemplateTwiceLeavesOneDeclaration()
    {
        var (manifest, _, locomotion, _) = Production();
        TemplateScopes.SetDefault(manifest, locomotion, "tpl-a");
        TemplateScopes.SetDefault(manifest, locomotion, "tpl-b");

        var declared = Assert.Single(locomotion.Resources!.Where(r => r.Kind == TemplateScopes.Kind));
        output.WriteLine($"after two sets: {declared.Id}");
        Assert.Equal("tpl-b", declared.Id);
        Assert.Equal("tpl-b", TemplateScopes.DefaultFor(manifest, locomotion));
    }

    /// <summary>Every kind resolves without seeing the others.</summary>
    [Fact]
    public void GradientsPalettesAndTemplatesDoNotCollide()
    {
        var (manifest, knight, _, walk) = Production();
        ResourceScopes.Declare(manifest, knight, PaletteScopes.Kind, "pal");
        ResourceScopes.Declare(manifest, knight, GradientScopes.Kind, "grad");
        TemplateScopes.SetDefault(manifest, knight, "tpl");

        Assert.Equal(["pal"], PaletteScopes.VisibleTo(manifest, walk));
        Assert.Equal(["grad"], GradientScopes.VisibleTo(manifest, walk));
        Assert.Equal("tpl", TemplateScopes.DefaultFor(manifest, knight));
    }
}

using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// Q30 step 2: which palettes a document paints from, and the migration that
/// keeps every project written before Q30 behaving exactly as it did.
/// </summary>
public class PaletteScopeTests(ITestOutputHelper output)
{
    private static (ProjectManifest Manifest, ProjectFolder Knight, ProjectFolder Goblin,
                    DocumentRef KnightDoc, DocumentRef GoblinDoc) TwoCharacters()
    {
        var manifest = new ProjectManifest { Name = "Production" };
        var characters = ProjectFolders.Add(manifest, "characters");
        var knight = ProjectFolders.Add(manifest, "knight", characters);
        var goblin = ProjectFolders.Add(manifest, "goblin", characters);

        var kd = new DocumentRef { Name = "walk", Path = "documents/knight-walk.json", FolderId = knight.Id };
        var gd = new DocumentRef { Name = "walk", Path = "documents/goblin-walk.json", FolderId = goblin.Id };
        manifest.Documents.Add(kd);
        manifest.Documents.Add(gd);
        return (manifest, knight, goblin, kd, gd);
    }

    /// <summary>
    /// A project that declares no scopes is not filtered — the old behaviour,
    /// unchanged.
    /// </summary>
    /// <remarks>
    /// Q30's migration answer was <em>new projects only</em>, and this is where a
    /// reader can tell the two shapes apart. Null means *do not filter*, which is
    /// a different claim from an empty list and the reason the return type is
    /// nullable rather than a set.
    /// </remarks>
    [Fact]
    public void AProjectThatDeclaresNoScopesIsNotFiltered()
    {
        var (manifest, _, _, knightDoc, _) = TwoCharacters();
        Assert.False(PaletteScopes.AnyDeclared(manifest));
        Assert.Null(PaletteScopes.VisibleTo(manifest, knightDoc));
    }

    /// <summary>
    /// Once scopes exist, a character's palette is not in another's picker.
    /// </summary>
    /// <remarks>
    /// The behaviour change, and the reason step 2 is not just plumbing. Every
    /// project palette used to be registered for every document, so
    /// <c>TwoAnimationsUnderOneCharacterPaintFromOnePalette</c> passed because
    /// nothing was scoped at all — which reads as working right up until a
    /// project has a second character.
    /// </remarks>
    [Fact]
    public void APaletteOnOneCharacterIsNotVisibleToAnother()
    {
        var (manifest, knight, goblin, knightDoc, goblinDoc) = TwoCharacters();
        ResourceScopes.Declare(manifest, knight, PaletteScopes.Kind, "pal-knight");
        ResourceScopes.Declare(manifest, goblin, PaletteScopes.Kind, "pal-goblin");

        var forKnight = PaletteScopes.VisibleTo(manifest, knightDoc);
        var forGoblin = PaletteScopes.VisibleTo(manifest, goblinDoc);
        output.WriteLine($"knight sees [{string.Join(",", forKnight!)}], goblin sees [{string.Join(",", forGoblin!)}]");

        Assert.Equal(["pal-knight"], forKnight);
        Assert.Equal(["pal-goblin"], forGoblin);
    }

    /// <summary>
    /// Pillar 1's promise, with a folder where the character used to be.
    /// </summary>
    /// <remarks>
    /// <c>TwoAnimationsUnderOneCharacterPaintFromOnePalette</c> restated against
    /// the new mechanism: this is the behaviour scoping has to be a superset of,
    /// not a replacement for.
    /// </remarks>
    [Fact]
    public void TwoAnimationsUnderOneFolderPaintFromOnePalette()
    {
        var (manifest, knight, _, walk, _) = TwoCharacters();
        var run = new DocumentRef { Name = "run", Path = "documents/knight-run.json", FolderId = knight.Id };
        manifest.Documents.Add(run);
        ResourceScopes.Declare(manifest, knight, PaletteScopes.Kind, "pal-knight");

        Assert.Equal(PaletteScopes.VisibleTo(manifest, walk), PaletteScopes.VisibleTo(manifest, run));
        Assert.Equal(["pal-knight"], PaletteScopes.VisibleTo(manifest, walk));
    }

    /// <summary>A studio palette on the project reaches both characters.</summary>
    [Fact]
    public void AProjectPaletteReachesEveryDocument()
    {
        var (manifest, knight, _, knightDoc, goblinDoc) = TwoCharacters();
        ResourceScopes.Declare(manifest, null, PaletteScopes.Kind, "studio");
        ResourceScopes.Declare(manifest, knight, PaletteScopes.Kind, "pal-knight");

        Assert.Equal(["pal-knight", "studio"], PaletteScopes.VisibleTo(manifest, knightDoc));
        Assert.Equal(["studio"], PaletteScopes.VisibleTo(manifest, goblinDoc));
    }

    /// <summary>A loose document opened beside a project is not filtered.</summary>
    /// <remarks>
    /// It has no place in the tree to resolve from, and hiding every swatch from
    /// it would be a worse answer than offering too many.
    /// </remarks>
    [Fact]
    public void ADocumentTheProjectDoesNotKnowIsNotFiltered()
    {
        var (manifest, knight, _, _, _) = TwoCharacters();
        ResourceScopes.Declare(manifest, knight, PaletteScopes.Kind, "pal-knight");
        Assert.Null(PaletteScopes.VisibleTo(manifest, null));
    }

    /// <summary>
    /// A stroke records which palette its swatch came from, and writes nothing
    /// when it was painted with a literal colour.
    /// </summary>
    /// <remarks>
    /// The guard scoping makes necessary. Swatch ids are generated, so
    /// independently authored palettes will not collide — but a palette
    /// *duplicated* to tweak it shares every id with its original, and
    /// duplicating one is the obvious thing to do.
    /// </remarks>
    [Fact]
    public void AStrokePaintedFromAPaletteRecordsWhichOne()
    {
        var literal = new Stroke { Color = "#ff0000" };
        var json = JsonSerializer.Serialize(literal, DocJson.Options);
        output.WriteLine(json);
        Assert.DoesNotContain("\"paletteId\"", json);
        Assert.DoesNotContain("\"swatchId\"", json);

        var fromPalette = new Stroke { Color = "#ff0000", SwatchId = "sw-1", PaletteId = "pal-knight" };
        var back = JsonSerializer.Deserialize<Stroke>(
            JsonSerializer.Serialize(fromPalette, DocJson.Options), DocJson.Options)!;
        Assert.Equal("pal-knight", back.PaletteId);
        Assert.Equal("sw-1", back.SwatchId);

        // And a copy keeps the pair, or duplicating a drawing would lose the
        // link the way B-era copies lost SwatchId.
        Assert.Equal("pal-knight", fromPalette.Clone().PaletteId);
    }
}

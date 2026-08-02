using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// Variants and the library. Both rest on one property: art references
/// swatches by id, so a second palette carrying the SAME ids repaints the same
/// drawings. Every test here that matters is really testing that.
/// </summary>
public sealed class CharacterVariantTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-var-{Guid.NewGuid():N}.lbproj");
    private readonly string _library = Path.Combine(
        Path.GetTempPath(), $"lightbox-lib-{Guid.NewGuid():N}.lbproj");

    public void Dispose()
    {
        foreach (var dir in new[] { _root, _library })
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    private static Doc Drawing(string? swatchId = null)
    {
        var doc = DocumentFactory.CreateDoc(100, 100, 12);
        ((PaintedFrame)doc.Scene.Layers[0].Cels[0].Frame!).Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            SwatchId = swatchId,
            Points = [new StrokePoint(10, 10, 1), new StrokePoint(80, 80, 1)],
            Brush = new BrushSettings { Size = 10, Opacity = 1 },
        });
        return doc;
    }

    /// <summary>A character with a palette, one swatch and one animation.</summary>
    private Project Knight(out Character knight, out Swatch swatch, out DocumentRef walk, string? root = null)
    {
        var project = ProjectIo.Create("Knight", root ?? _root);
        swatch = new Swatch { Color = "#8090a0", Name = "Armour" };
        var palette = new Palette { Name = "Knight", Swatches = [swatch] };
        project.Palettes.Add(palette);
        knight = ProjectIo.AddCharacter(project, "Knight");
        knight.PaletteId = palette.Id;
        walk = ProjectIo.AddAnimation(project, knight, "Walk", Drawing(swatch.Id));
        return project;
    }

    // ---- variants -----------------------------------------------------------

    [Fact]
    public void ACharacterNobodyVariedCarriesNoVariantKeys()
    {
        // The same discipline the camera and the project type follow: optional
        // means absent. A character that was never varied must not start
        // writing variant structure because the feature exists.
        var project = Knight(out _, out _, out _);
        ProjectIo.Save(project);

        var json = File.ReadAllText(Path.Combine(_root, "characters", "knight", "character.json"));
        Assert.Contains("\"variants\": []", json);
        Assert.Empty(ProjectIo.Load(_root).Characters.First().Variants);
    }

    [Fact]
    public void AVariantCopiesThePaletteKeepingEverySwatchId()
    {
        // The whole trick. Fresh ids would make the variant paint nothing,
        // because the art references swatches by id.
        var project = Knight(out var knight, out var swatch, out _);

        var winter = ProjectIo.AddVariant(project, knight, "Winter");

        var palette = project.Palettes.Single(p => p.Id == winter.PaletteId);
        Assert.Equal(swatch.Id, Assert.Single(palette.Swatches).Id);
        Assert.NotEqual(knight.PaletteId, winter.PaletteId);
    }

    [Fact]
    public void RecolouringAVariantLeavesTheBaseCharacterAlone()
    {
        var project = Knight(out var knight, out var swatch, out _);
        var winter = ProjectIo.AddVariant(project, knight, "Winter");

        project.Palettes.Single(p => p.Id == winter.PaletteId).Swatches[0].Color = "#e8f0ff";

        Assert.Equal("#8090a0", swatch.Color);
        Assert.Equal("#8090a0", project.PaletteFor(knight)!.Swatches[0].Color);
    }

    [Fact]
    public void SelectingAVariantSwitchesWhichPaletteTheCharacterPaintsWith()
    {
        var project = Knight(out var knight, out _, out _);
        var winter = ProjectIo.AddVariant(project, knight, "Winter");
        project.Palettes.Single(p => p.Id == winter.PaletteId).Swatches[0].Color = "#e8f0ff";

        Assert.Equal("#8090a0", project.PaletteFor(knight)!.Swatches[0].Color);

        project.ActiveVariant[knight.Id] = winter.Id;
        Assert.Equal("#e8f0ff", project.PaletteFor(knight)!.Swatches[0].Color);
    }

    [Fact]
    public void AVariantInheritsEveryAnimationItDoesNotOverride()
    {
        // "Inherits animations" means exactly this: a walk cycle drawn once is
        // the walk cycle of every variant.
        var project = Knight(out var knight, out _, out var walk);
        ProjectIo.AddAnimation(project, knight, "Idle", Drawing());
        var winter = ProjectIo.AddVariant(project, knight, "Winter");

        var played = knight.AnimationsFor(winter).ToList();
        Assert.Equal(2, played.Count);
        Assert.Equal(walk.Id, played[0].Id);
    }

    [Fact]
    public void AnOverriddenAnimationReplacesOnlyItself()
    {
        var project = Knight(out var knight, out _, out var walk);
        var idle = ProjectIo.AddAnimation(project, knight, "Idle", Drawing());
        var winter = ProjectIo.AddVariant(project, knight, "Winter");

        var replaced = ProjectIo.OverrideAnimation(project, knight, winter, walk, Drawing());

        var played = knight.AnimationsFor(winter).ToList();
        Assert.Equal(replaced.Id, played[0].Id);   // overridden
        Assert.Equal(idle.Id, played[1].Id);       // still shared
        Assert.Equal(walk.Id, knight.Animations[0].Id); // the base is untouched
    }

    [Fact]
    public void AVariantsOwnArtIsSavedAndReloaded()
    {
        // The override's document must be in AllDocuments or it never reaches
        // disk and the variant silently loses its art.
        var project = Knight(out var knight, out _, out var walk);
        var winter = ProjectIo.AddVariant(project, knight, "Winter");
        var replaced = ProjectIo.OverrideAnimation(project, knight, winter, walk, Drawing());
        ProjectIo.Save(project);

        Assert.True(File.Exists(project.PathOf(replaced)));

        var reloaded = ProjectIo.Load(_root);
        var variant = Assert.Single(reloaded.Characters.First().Variants);
        var over = Assert.Single(variant.AnimationOverrides);
        Assert.NotNull(ProjectIo.LoadDocument(reloaded, over.Value));
    }

    [Fact]
    public void VariantsRoundTripWithTheirPalettes()
    {
        var project = Knight(out var knight, out _, out _);
        var winter = ProjectIo.AddVariant(project, knight, "Winter");
        project.Palettes.Single(p => p.Id == winter.PaletteId).Swatches[0].Color = "#e8f0ff";
        ProjectIo.Save(project);

        var reloaded = ProjectIo.Load(_root);
        var character = reloaded.Characters.First();
        var restored = Assert.Single(character.Variants);
        Assert.Equal("Winter", restored.Name);

        reloaded.ActiveVariant[character.Id] = restored.Id;
        Assert.Equal("#e8f0ff", reloaded.PaletteFor(character)!.Swatches[0].Color);
    }

    // ---- library ------------------------------------------------------------

    [Fact]
    public void OnlyAssetLibraryProjectsOfferTheirCharacters()
    {
        // Which is what makes the project type mean something rather than being
        // a label on an enum.
        var plain = Knight(out _, out _, out _, _library);
        ProjectIo.Save(plain);
        Assert.Empty(CharacterLibrary.Scan([_library]));

        plain.Manifest.Type = ProjectType.AssetLibrary;
        ProjectIo.Save(plain);
        var entry = Assert.Single(CharacterLibrary.Scan([_library]));
        Assert.Equal("Knight", entry.Name);
        Assert.Equal(1, entry.AnimationCount);
    }

    [Fact]
    public void ScanningIgnoresFoldersThatAreNotProjects()
    {
        // A library directory is somewhere people also keep other things.
        Directory.CreateDirectory(Path.Combine(_library, "notes"));
        Assert.Empty(CharacterLibrary.Scan([_library]));
        Assert.Empty(CharacterLibrary.Scan(["/nowhere/at/all"]));
    }

    [Fact]
    public void ImportingACharacterBringsItsAnimationsAndPalette()
    {
        var source = Knight(out _, out var swatch, out _, _library);
        source.Manifest.Type = ProjectType.AssetLibrary;
        ProjectIo.Save(source);

        var target = ProjectIo.Create("Game", _root);
        var imported = CharacterLibrary.Import(CharacterLibrary.Scan([_library]).Single(), target);

        Assert.Equal("Knight", imported.Name);
        Assert.Single(imported.Animations);
        // The palette came with it, and kept the swatch ids the art references.
        var palette = target.Palettes.Single(p => p.Id == imported.PaletteId);
        Assert.Equal(swatch.Id, Assert.Single(palette.Swatches).Id);
    }

    [Fact]
    public void AnImportedCharacterStillPaintsFromItsPalette()
    {
        // The failure this guards is the loud one: renumbering swatches on
        // import gives you a character whose drawings resolve to nothing.
        var source = Knight(out _, out var swatch, out _, _library);
        source.Manifest.Type = ProjectType.AssetLibrary;
        ProjectIo.Save(source);

        var target = ProjectIo.Create("Game", _root);
        var imported = CharacterLibrary.Import(CharacterLibrary.Scan([_library]).Single(), target);

        var doc = ProjectIo.LoadDocument(target, imported.Animations[0])!;
        var stroke = ((PaintedFrame)doc.Scene.Layers[0].Cels[0].Frame!).Strokes[0];
        Assert.Equal(swatch.Id, stroke.SwatchId);
        Assert.Contains(target.Palettes.SelectMany(p => p.Swatches), s => s.Id == stroke.SwatchId);
    }

    [Fact]
    public void ImportingCopiesRatherThanLinks()
    {
        // Copying is honest about what this does. A linked character that edits
        // in place is Pillar 3's job and needs a dependency graph; a link that
        // silently breaks later would be worse than no link.
        var source = Knight(out _, out var swatch, out _, _library);
        source.Manifest.Type = ProjectType.AssetLibrary;
        ProjectIo.Save(source);

        var target = ProjectIo.Create("Game", _root);
        var imported = CharacterLibrary.Import(CharacterLibrary.Scan([_library]).Single(), target);

        swatch.Color = "#ff0000";
        Assert.Equal("#8090a0", target.Palettes.Single(p => p.Id == imported.PaletteId).Swatches[0].Color);
    }

    [Fact]
    public void ImportingCarriesVariantsAndRebasesTheirOverrides()
    {
        var source = Knight(out var knight, out _, out var walk, _library);
        source.Manifest.Type = ProjectType.AssetLibrary;
        var winter = ProjectIo.AddVariant(source, knight, "Winter");
        source.Palettes.Single(p => p.Id == winter.PaletteId).Swatches[0].Color = "#e8f0ff";
        ProjectIo.OverrideAnimation(source, knight, winter, walk, Drawing());
        ProjectIo.Save(source);

        var target = ProjectIo.Create("Game", _root);
        var imported = CharacterLibrary.Import(CharacterLibrary.Scan([_library]).Single(), target);

        var variant = Assert.Single(imported.Variants);
        Assert.Equal("Winter", variant.Name);
        // Rebased onto the COPY's animation ids, not the source's, or the
        // override would point at an animation this project does not have.
        var over = Assert.Single(variant.AnimationOverrides);
        Assert.Equal(imported.Animations[0].Id, over.Key);

        target.ActiveVariant[imported.Id] = variant.Id;
        Assert.Equal("#e8f0ff", target.PaletteFor(imported)!.Swatches[0].Color);
    }

    [Fact]
    public void ImportingTwiceGivesTwoCharactersWithDistinctFolders()
    {
        var source = Knight(out _, out _, out _, _library);
        source.Manifest.Type = ProjectType.AssetLibrary;
        ProjectIo.Save(source);

        var target = ProjectIo.Create("Game", _root);
        var entry = CharacterLibrary.Scan([_library]).Single();
        var a = CharacterLibrary.Import(entry, target);
        var b = CharacterLibrary.Import(entry, target);

        Assert.NotEqual(a.Id, b.Id);
        Assert.NotEqual(a.Slug, b.Slug);

        ProjectIo.Save(target);
        Assert.True(Directory.Exists(Path.Combine(_root, "characters", a.Slug)));
        Assert.True(Directory.Exists(Path.Combine(_root, "characters", b.Slug)));
    }
}

using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// Variants and the library. Both rest on one property: art references
/// swatches by id, so a second palette carrying the SAME ids repaints the same
/// drawings. Every test here that matters is really testing that.
/// </summary>
/// <remarks>
/// Rewritten for B114. A character is a folder with a reading, so "the knight"
/// is a <see cref="ProjectFolder"/> and its animations are ordinary documents
/// filed in it — which is the whole point: they now resolve palettes and appear
/// in export plans, and the tests below say so rather than assuming it.
/// </remarks>
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

    /// <summary>A subject folder with a palette, one swatch and one animation.</summary>
    private Project Knight(
        out ProjectFolder knight, out Swatch swatch, out DocumentRef walk, string? root = null)
    {
        var project = ProjectIo.Create("Knight", root ?? _root);
        swatch = new Swatch { Color = "#8090a0", Name = "Armour" };
        var palette = new Palette { Name = "Knight", Swatches = [swatch] };
        project.Palettes.Add(palette);

        knight = ProjectFolders.Add(project.Manifest, "Knight");
        knight.Taxonomy = new SubjectTaxonomy
        {
            Kind = "biped",
            Parts = [new SubjectPart { Name = "torso" }],
        };
        ResourceScopes.Declare(project.Manifest, knight, PaletteScopes.Kind, palette.Id);
        walk = ProjectIo.AddDocument(project, "Walk", Drawing(swatch.Id), knight);
        return project;
    }

    // ---- the bug itself (B114) ----------------------------------------------

    [Fact]
    public void CharacterDocumentsAreInTheProject()
    {
        // The defect in one line: `AddAnimation` filed a reference under
        // `character.Animations` and nowhere else, so the project's own document
        // list did not contain most of an animation project.
        var project = Knight(out var knight, out _, out var walk);

        Assert.Contains(walk, project.Manifest.Documents);
        Assert.Equal(knight.Id, walk.FolderId);
        Assert.Equal(walk.Id, Assert.Single(ProjectFolders.DocumentsIn(project.Manifest, knight)).Id);
    }

    [Fact]
    public void AFolderPaletteReachesACharactersAnimation()
    {
        // `ResourceScopes.Resolve` keys off `document.FolderId`, which a
        // character's animation never had — so no folder's palette, reference,
        // guide set or export preset ever reached a character's work.
        var project = Knight(out _, out _, out var walk);

        var visible = PaletteScopes.VisibleTo(project.Manifest, walk);
        Assert.NotNull(visible);
        Assert.Single(visible);
        Assert.Equal(project.Palettes[0].Id, visible[0]);
    }

    // ---- variants -----------------------------------------------------------

    [Fact]
    public void AFolderNobodyVariedCarriesNoVariantKeys()
    {
        // Optional means absent, the same discipline the camera and the project
        // type follow. A subject that was never varied must not start writing
        // variant structure because the feature exists.
        var project = Knight(out _, out _, out _);
        ProjectIo.Save(project);

        var json = File.ReadAllText(Path.Combine(_root, "project.json"));
        Assert.DoesNotContain("\"variants\"", json);
        Assert.All(ProjectIo.Load(_root).WithReading, f => Assert.Null(f.Variants));
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
        Assert.NotEqual(project.Palettes[0].Id, winter.PaletteId);
    }

    [Fact]
    public void RecolouringAVariantLeavesTheBaseSubjectAlone()
    {
        var project = Knight(out var knight, out var swatch, out _);
        var winter = ProjectIo.AddVariant(project, knight, "Winter");

        project.Palettes.Single(p => p.Id == winter.PaletteId).Swatches[0].Color = "#e8f0ff";

        Assert.Equal("#8090a0", swatch.Color);
        Assert.Equal("#8090a0", project.PaletteFor(knight)!.Swatches[0].Color);
    }

    [Fact]
    public void SelectingAVariantSwitchesWhichPaletteTheSubjectPaintsWith()
    {
        var project = Knight(out var knight, out _, out _);
        var winter = ProjectIo.AddVariant(project, knight, "Winter");
        project.Palettes.Single(p => p.Id == winter.PaletteId).Swatches[0].Color = "#e8f0ff";

        Assert.Equal("#8090a0", project.PaletteFor(knight)!.Swatches[0].Color);

        project.ActiveVariant[knight.Id] = winter.Id;
        Assert.Equal("#e8f0ff", project.PaletteFor(knight)!.Swatches[0].Color);
    }

    [Fact]
    public void AVariantInheritsEveryDocumentItDoesNotOverride()
    {
        // "Inherits animations" means exactly this: a walk cycle drawn once is
        // the walk cycle of every variant.
        var project = Knight(out var knight, out _, out var walk);
        ProjectIo.AddDocument(project, "Idle", Drawing(), knight);
        var winter = ProjectIo.AddVariant(project, knight, "Winter");

        var played = ProjectFolders.DocumentsFor(project.Manifest, knight, winter);
        Assert.Equal(2, played.Count);
        // Unordered, so by name: Idle then Walk.
        Assert.Equal(["Idle", "Walk"], played.Select(d => d.Name));
        Assert.Contains(played, d => d.Id == walk.Id);
    }

    [Fact]
    public void AnOverriddenDocumentReplacesOnlyItself()
    {
        var project = Knight(out var knight, out _, out var walk);
        var idle = ProjectIo.AddDocument(project, "Idle", Drawing(), knight);
        var winter = ProjectIo.AddVariant(project, knight, "Winter");

        var replaced = ProjectIo.OverrideDocument(project, knight, winter, walk, Drawing());

        var played = ProjectFolders.DocumentsFor(project.Manifest, knight, winter);
        Assert.Equal(idle.Id, played[0].Id);        // still shared
        Assert.Equal(replaced.Id, played[1].Id);    // overridden, in Walk's place

        // The base is untouched, and the override is not a second animation.
        var basis = ProjectFolders.DocumentsIn(project.Manifest, knight);
        Assert.Equal(2, basis.Count);
        Assert.Contains(basis, d => d.Id == walk.Id);
        Assert.DoesNotContain(basis, d => d.Id == replaced.Id);
    }

    [Fact]
    public void AVariantsOwnArtIsAnOrdinaryDocumentInTheProject()
    {
        // Under the old model an override lived in the variant and nowhere else,
        // which is the third container B114 is about. It has to be in the one
        // list or it resolves no palette, exports nowhere and never reaches disk.
        var project = Knight(out var knight, out _, out var walk);
        var winter = ProjectIo.AddVariant(project, knight, "Winter");
        var replaced = ProjectIo.OverrideDocument(project, knight, winter, walk, Drawing());
        ProjectIo.Save(project);

        Assert.Contains(replaced, project.Manifest.Documents);
        Assert.True(File.Exists(project.PathOf(replaced)));

        var reloaded = ProjectIo.Load(_root);
        var variant = Assert.Single(reloaded.WithReading.Single().Variants!);
        var over = Assert.Single(variant.Overrides);
        var reference = reloaded.FindRef(over.Value);
        Assert.NotNull(reference);
        Assert.NotNull(ProjectIo.LoadDocument(reloaded, reference));
    }

    [Fact]
    public void VariantsRoundTripWithTheirPalettes()
    {
        var project = Knight(out var knight, out _, out _);
        var winter = ProjectIo.AddVariant(project, knight, "Winter");
        project.Palettes.Single(p => p.Id == winter.PaletteId).Swatches[0].Color = "#e8f0ff";
        ProjectIo.Save(project);

        var reloaded = ProjectIo.Load(_root);
        var subject = reloaded.WithReading.Single();
        var restored = Assert.Single(subject.Variants!);
        Assert.Equal("Winter", restored.Name);

        reloaded.ActiveVariant[subject.Id] = restored.Id;
        Assert.Equal("#e8f0ff", reloaded.PaletteFor(subject)!.Swatches[0].Color);
    }

    // ---- ordering (the one thing folders could not previously do) ------------

    [Fact]
    public void OrderIsPartialAndTheRestSortByName()
    {
        var project = Knight(out var knight, out _, out var walk);
        var run = ProjectIo.AddDocument(project, "Run", Drawing(), knight);
        ProjectIo.AddDocument(project, "Idle", Drawing(), knight);

        // Pin two; leave "Idle" unsorted.
        knight.Order = [run.Id, walk.Id];

        Assert.Equal(
            ["Run", "Walk", "Idle"],
            ProjectFolders.InOrder(project.Manifest, knight).Select(d => d.Name));
    }

    [Fact]
    public void AnOrderIdWhoseDocumentIsGoneIsSkipped()
    {
        // An ordering is a preference, not a claim about what exists.
        var project = Knight(out var knight, out _, out var walk);
        knight.Order = ["docref-that-never-existed", walk.Id];

        Assert.Equal(walk.Id, Assert.Single(ProjectFolders.InOrder(project.Manifest, knight)).Id);
    }

    // ---- library ------------------------------------------------------------

    [Fact]
    public void OnlyAssetLibraryProjectsOfferTheirSubjects()
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
        Assert.Equal(1, entry.DocumentCount);
    }

    [Fact]
    public void AFolderWithNoReadingIsOfferedToo()
    {
        // Q40. Offering only folders something had read would make "character" a
        // designation again, this time one deciding what can be shared — and a
        // prop set or a shared environment is exactly what a library is for.
        var project = ProjectIo.Create("Bits", _library, ProjectType.AssetLibrary);
        ProjectFolders.Add(project.Manifest, "Scratch");
        ProjectIo.Save(project);

        var entry = Assert.Single(CharacterLibrary.Scan([_library]));
        Assert.Equal("Scratch", entry.Name);
        Assert.False(entry.Folder.HasReading);
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
    public void ImportingASubjectBringsItsDocumentsAndPalette()
    {
        var source = Knight(out _, out var swatch, out _, _library);
        source.Manifest.Type = ProjectType.AssetLibrary;
        ProjectIo.Save(source);

        var target = ProjectIo.Create("Game", _root);
        var imported = CharacterLibrary.Import(CharacterLibrary.Scan([_library]).Single(), target);

        Assert.Equal("Knight", imported.Name);
        Assert.Single(ProjectFolders.DocumentsIn(target.Manifest, imported));
        // The reading came too — it describes the character, not the project.
        Assert.Equal("biped", imported.Taxonomy!.Kind);
        // The palette came with it, and kept the swatch ids the art references.
        var palette = target.PaletteFor(imported)!;
        Assert.Equal(swatch.Id, Assert.Single(palette.Swatches).Id);
    }

    [Fact]
    public void AnImportedSubjectStillPaintsFromItsPalette()
    {
        // The failure this guards is the loud one: renumbering swatches on
        // import gives you a character whose drawings resolve to nothing.
        var source = Knight(out _, out var swatch, out _, _library);
        source.Manifest.Type = ProjectType.AssetLibrary;
        ProjectIo.Save(source);

        var target = ProjectIo.Create("Game", _root);
        var imported = CharacterLibrary.Import(CharacterLibrary.Scan([_library]).Single(), target);

        var walk = ProjectFolders.DocumentsIn(target.Manifest, imported)[0];
        var doc = ProjectIo.LoadDocument(target, walk)!;
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
        Assert.Equal("#8090a0", target.PaletteFor(imported)!.Swatches[0].Color);
    }

    [Fact]
    public void ImportingCarriesVariantsAndRebasesTheirOverrides()
    {
        var source = Knight(out var knight, out _, out var walk, _library);
        source.Manifest.Type = ProjectType.AssetLibrary;
        var winter = ProjectIo.AddVariant(source, knight, "Winter");
        source.Palettes.Single(p => p.Id == winter.PaletteId).Swatches[0].Color = "#e8f0ff";
        ProjectIo.OverrideDocument(source, knight, winter, walk, Drawing());
        ProjectIo.Save(source);

        var target = ProjectIo.Create("Game", _root);
        var imported = CharacterLibrary.Import(CharacterLibrary.Scan([_library]).Single(), target);

        var variant = Assert.Single(imported.Variants!);
        Assert.Equal("Winter", variant.Name);
        // Rebased onto the COPY's document ids, not the source's, or the
        // override would point at a document this project does not have.
        var over = Assert.Single(variant.Overrides);
        Assert.Equal(ProjectFolders.DocumentsIn(target.Manifest, imported)[0].Id, over.Key);
        Assert.NotNull(target.FindRef(over.Value));

        target.ActiveVariant[imported.Id] = variant.Id;
        Assert.Equal("#e8f0ff", target.PaletteFor(imported)!.Swatches[0].Color);
    }

    [Fact]
    public void ImportingTwiceGivesTwoDistinctFolders()
    {
        var source = Knight(out _, out _, out _, _library);
        source.Manifest.Type = ProjectType.AssetLibrary;
        ProjectIo.Save(source);

        var target = ProjectIo.Create("Game", _root);
        var entry = CharacterLibrary.Scan([_library]).Single();
        var a = CharacterLibrary.Import(entry, target);
        var b = CharacterLibrary.Import(entry, target);

        Assert.NotEqual(a.Id, b.Id);
        // Numbered rather than refused — the same rule ProjectFolders.Add uses.
        Assert.NotEqual(a.Name, b.Name);
        Assert.Equal(2, target.WithReading.Count());
    }

    // ---- ending subjecthood says what it costs (Q35) -------------------------

    [Fact]
    public void ClearingAReadingNamesEverythingItWouldDiscard()
    {
        var project = Knight(out var knight, out _, out _);
        knight.Pivot = new Pivot();
        ProjectIo.AddVariant(project, knight, "Winter");
        ProjectIo.AddVariant(project, knight, "Damaged");

        var lost = knight.WhatClearingTheReadingDiscards();
        Assert.Equal(3, lost.Count);
        Assert.Contains("1 part", lost[0]);
        Assert.Equal("its pivot", lost[1]);
        Assert.Equal("2 variants", lost[2]);
    }

    [Fact]
    public void AHandCorrectedReadingIsNamedAsSuch()
    {
        // Losing a reading an artist corrected is the failure `Reviewed` exists
        // to prevent, arriving from the other direction.
        var project = Knight(out var knight, out _, out _);
        knight.Taxonomy!.Reviewed = true;

        Assert.Equal(
            "the reading you corrected by hand",
            Assert.Single(knight.WhatClearingTheReadingDiscards()));
    }

    [Fact]
    public void AnOrdinaryFolderLosesNothing()
    {
        var project = ProjectIo.Create("Game", _root);
        var folder = ProjectFolders.Add(project.Manifest, "Backgrounds");
        Assert.Empty(folder.WhatClearingTheReadingDiscards());
        Assert.False(folder.HasReading);
    }
}

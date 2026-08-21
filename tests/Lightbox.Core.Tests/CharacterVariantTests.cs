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
        ((Frame)doc.Scene.Layers[0].Cels[0].Frame!).Strokes.Add(new Stroke
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
    public void TheStandInMapAnswersForTheBasePalettesId()
    {
        // Strokes name the palette they were painted from, and the registry
        // never answers a named palette from another that shares the swatch id
        // (Q30). So the variant's copy repaints nothing by existing — the
        // renderer has to be told which id it stands in for, and this is the
        // one place that says so.
        var project = Knight(out var knight, out _, out var walk);
        var winter = ProjectIo.AddVariant(project, knight, "Winter");

        Assert.Empty(project.PaletteStandInsFor(walk));

        project.ActiveVariant[knight.Id] = winter.Id;
        var standIn = Assert.Single(project.PaletteStandInsFor(walk));
        Assert.Equal(project.Palettes[0].Id, standIn.Key);
        Assert.Equal(winter.PaletteId, standIn.Value.Id);
    }

    [Fact]
    public void AVariantWithNoPaletteOfItsOwnStandsInForNothing()
    {
        // PaletteId null means "whatever the subject paints with" — the folder
        // shares no palette, so there is nothing to substitute and nothing to
        // hide from the flat lookup either.
        var project = ProjectIo.Create("Plain", _root);
        var scratch = ProjectFolders.Add(project.Manifest, "Scratch");
        var bare = ProjectIo.AddVariant(project, scratch, "Alt");
        var doc = ProjectIo.AddDocument(project, "Loop", Drawing(), scratch);

        Assert.Null(bare.PaletteId);
        project.ActiveVariant[scratch.Id] = bare.Id;
        Assert.Empty(project.PaletteStandInsFor(doc));
        Assert.Empty(project.VariantPaletteIds());
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
        var imported = CharacterLibrary.Import(CharacterLibrary.Scan([_library]).Single(), target).Folder;

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
        var imported = CharacterLibrary.Import(CharacterLibrary.Scan([_library]).Single(), target).Folder;

        var walk = ProjectFolders.DocumentsIn(target.Manifest, imported)[0];
        var doc = ProjectIo.LoadDocument(target, walk)!;
        var stroke = ((Frame)doc.Scene.Layers[0].Cels[0].Frame!).Strokes[0];
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
        var imported = CharacterLibrary.Import(CharacterLibrary.Scan([_library]).Single(), target).Folder;

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
        var imported = CharacterLibrary.Import(CharacterLibrary.Scan([_library]).Single(), target).Folder;

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
    public void AnImportSurvivesSavingAndReopeningTheProject()
    {
        // End to end, through the disk: everything Import assembles in memory
        // — the documents, the palette declaration, the variant and its
        // override, the reading — must come back from a cold load, or the
        // library works exactly until the artist quits. Nothing below this
        // line holds a reference to the import; only the folder proves it.
        var source = Knight(out var knight, out var swatch, out var walk, _library);
        source.Manifest.Type = ProjectType.AssetLibrary;
        var winter = ProjectIo.AddVariant(source, knight, "Winter");
        source.Palettes.Single(p => p.Id == winter.PaletteId).Swatches[0].Color = "#e8f0ff";
        ProjectIo.OverrideDocument(source, knight, winter, walk, Drawing());
        ProjectIo.Save(source);

        var target = ProjectIo.Create("Game", _root);
        CharacterLibrary.Import(CharacterLibrary.Scan([_library]).Single(), target);
        ProjectIo.Save(target);

        var reloaded = ProjectIo.Load(_root);
        var imported = reloaded.WithReading.Single();
        Assert.Equal("biped", imported.Taxonomy!.Kind);

        // The animation loads from this project's own file…
        var animations = ProjectFolders.DocumentsIn(reloaded.Manifest, imported);
        var doc = ProjectIo.LoadDocument(reloaded, Assert.Single(animations));
        Assert.NotNull(doc);
        // …and still paints from the palette that travelled with it.
        var painted = ((Frame)doc!.Scene.Layers[0].Cels[0].Frame!).Strokes[0].SwatchId;
        Assert.Equal(swatch.Id, painted);
        Assert.Equal(
            "#8090a0",
            reloaded.PaletteFor(imported)!.Swatches.Single(s => s.Id == painted).Color);

        // The variant came back attached, recolour and override included.
        var restored = Assert.Single(imported.Variants!);
        reloaded.ActiveVariant[imported.Id] = restored.Id;
        Assert.Equal("#e8f0ff", reloaded.PaletteFor(imported)!.Swatches[0].Color);
        var over = Assert.Single(restored.Overrides);
        Assert.NotNull(ProjectIo.LoadDocument(reloaded, reloaded.FindRef(over.Value)!));
    }

    [Fact]
    public void ImportingTwiceMergesIntoOneFolder()
    {
        // Q138 declined numbered-beside import — "nothing would ever update".
        // The second import finds the first by provenance and replaces the
        // unedited copy instead of standing a Knight 2 next to the Knight.
        var source = Knight(out _, out _, out _, _library);
        source.Manifest.Type = ProjectType.AssetLibrary;
        ProjectIo.Save(source);

        var target = ProjectIo.Create("Game", _root);
        var entry = CharacterLibrary.Scan([_library]).Single();
        var a = CharacterLibrary.Import(entry, target);
        var b = CharacterLibrary.Import(entry, target);

        Assert.Equal(a.Folder.Id, b.Folder.Id);
        Assert.Single(target.WithReading);
        Assert.Single(ProjectFolders.DocumentsIn(target.Manifest, b.Folder));
        Assert.Equal(["Walk"], b.Replaced);
        Assert.Empty(b.Added);
        Assert.Empty(b.KeptEdited);
    }

    // ---- provenance: what a re-import may and may not touch (Q138) ------------

    [Fact]
    public void AnImportIsStampedWithItsOriginAndLocalWorkIsNot()
    {
        var source = Knight(out _, out _, out _, _library);
        source.Manifest.Type = ProjectType.AssetLibrary;
        ProjectIo.Save(source);

        var target = ProjectIo.Create("Game", _root);
        var entry = CharacterLibrary.Scan([_library]).Single();
        var imported = CharacterLibrary.Import(entry, target).Folder;
        var local = ProjectIo.AddDocument(target, "Taunt", Drawing(), imported);

        Assert.Equal(entry.Source.Manifest.Id, imported.Origin!.LibraryId);
        Assert.Equal(entry.Folder.Id, imported.Origin.SourceId);
        var copy = ProjectFolders.DocumentsIn(target.Manifest, imported)
            .Single(d => d.Name == "Walk");
        Assert.NotNull(copy.Origin);
        Assert.NotNull(copy.Origin!.Hash);
        Assert.Null(local.Origin);
    }

    [Fact]
    public void ADocumentNobodyImportedCarriesNoOriginKeys()
    {
        // Optional means absent: a project that never used the library must
        // not start writing origin keys because the feature exists.
        var project = Knight(out _, out _, out _);
        ProjectIo.Save(project);
        Assert.DoesNotContain("\"origin\"", File.ReadAllText(Path.Combine(_root, "project.json")));
    }

    [Fact]
    public void ReImportReplacesByProvenanceAndNeverTouchesLocalWork()
    {
        var source = Knight(out var knight, out var swatch, out var walk, _library);
        source.Manifest.Type = ProjectType.AssetLibrary;
        ProjectIo.Save(source);

        var target = ProjectIo.Create("Game", _root);
        var imported = CharacterLibrary.Import(CharacterLibrary.Scan([_library]).Single(), target).Folder;
        var local = ProjectIo.AddDocument(target, "Taunt", Drawing(), imported);
        ProjectIo.Save(target);

        // The library moves on: the walk gains a stroke, and a run appears.
        source.Loaded[walk.Id].Scene.Layers[0].Cels[0].Frame!.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            SwatchId = swatch.Id,
            Points = [new StrokePoint(20, 20, 1), new StrokePoint(30, 30, 1)],
            Brush = new BrushSettings { Size = 4, Opacity = 1 },
        });
        ProjectIo.AddDocument(source, "Run", Drawing(swatch.Id), knight);
        ProjectIo.Save(source);

        // Through the disk on both sides: the re-import must recognise its own
        // copies from their stamps alone, hashes included, after a cold load.
        var reloaded = ProjectIo.Load(_root);
        var result = CharacterLibrary.Import(CharacterLibrary.Scan([_library]).Single(), reloaded);

        Assert.Equal(["Run"], result.Added);
        Assert.Equal(["Walk"], result.Replaced);
        Assert.Empty(result.KeptEdited);
        // The unedited walk took the library's newer content, on its own ref…
        var docs = ProjectFolders.DocumentsIn(reloaded.Manifest, result.Folder);
        Assert.Equal(3, docs.Count);
        var replaced = docs.Single(d => d.Name == "Walk");
        Assert.Equal(2, ProjectIo.LoadDocument(reloaded, replaced)!
            .Scene.Layers[0].Cels[0].Frame!.Strokes.Count);
        // …and the artist's own animation was not looked at, let alone touched.
        var kept = docs.Single(d => d.Name == "Taunt");
        Assert.Equal(local.Id, kept.Id);
        Assert.Null(kept.Origin);
    }

    [Fact]
    public void AnEditedCopyIsKeptAndNamedBeforeItIsReplaced()
    {
        // The one destructive act in the merge warns first, Q35-style: the
        // preflight names the edited copy, the default import keeps it, and
        // only an explicit replaceEdited overwrites the artist's changes.
        var source = Knight(out _, out var swatch, out _, _library);
        source.Manifest.Type = ProjectType.AssetLibrary;
        ProjectIo.Save(source);

        var target = ProjectIo.Create("Game", _root);
        var entry = CharacterLibrary.Scan([_library]).Single();
        var imported = CharacterLibrary.Import(entry, target).Folder;
        var copy = ProjectFolders.DocumentsIn(target.Manifest, imported).Single();
        ProjectIo.LoadDocument(target, copy)!.Scene.Layers[0].Cels[0].Frame!.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#ff0000",
            Points = [new StrokePoint(5, 5, 1), new StrokePoint(6, 6, 1)],
            Brush = new BrushSettings { Size = 2, Opacity = 1 },
        });

        Assert.Equal(["Walk"], CharacterLibrary.WhatReimportWouldReplace(entry, target));

        var kept = CharacterLibrary.Import(entry, target);
        Assert.Equal(["Walk"], kept.KeptEdited);
        Assert.Equal(2, ProjectIo.LoadDocument(target, copy)!
            .Scene.Layers[0].Cels[0].Frame!.Strokes.Count);

        var replaced = CharacterLibrary.Import(entry, target, replaceEdited: true);
        Assert.Equal(["Walk"], replaced.Replaced);
        Assert.Single(ProjectIo.LoadDocument(target, copy)!
            .Scene.Layers[0].Cels[0].Frame!.Strokes);
        // And having taken the library's copy, the slate is clean again.
        Assert.Empty(CharacterLibrary.WhatReimportWouldReplace(entry, target));
    }

    [Fact]
    public void ANameClashWithoutProvenanceStillMergesByFolder()
    {
        // The artist already has a folder called Knight that was never
        // imported. Nothing matches, so nothing is replaced — the library's
        // documents are added beside the local ones, in that folder.
        var source = Knight(out _, out _, out _, _library);
        source.Manifest.Type = ProjectType.AssetLibrary;
        ProjectIo.Save(source);

        var target = ProjectIo.Create("Game", _root);
        var mine = ProjectFolders.Add(target.Manifest, "Knight");
        var local = ProjectIo.AddDocument(target, "Walk", Drawing(), mine);

        var result = CharacterLibrary.Import(CharacterLibrary.Scan([_library]).Single(), target);

        Assert.Equal(mine.Id, result.Folder.Id);
        Assert.Equal(["Walk"], result.Added);
        Assert.Empty(result.Replaced);
        var docs = ProjectFolders.DocumentsIn(target.Manifest, mine);
        Assert.Equal(2, docs.Count);
        Assert.Null(docs.Single(d => d.Id == local.Id).Origin);
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

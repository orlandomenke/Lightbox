using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// The project container: a folder of plain JSON, loaded lazily, saved
/// incrementally, and able to hand a standalone copy of any document back out.
/// </summary>
public sealed class ProjectTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-proj-{Guid.NewGuid():N}.lbproj");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static Doc Drawing(string color = "#3070b0")
    {
        var doc = DocumentFactory.CreateDoc(120, 80, 12);
        // These tests are about the project's palettes. A document's own
        // starting black-and-white is noise here, and leaving it in would make
        // every "which palette did this end up with" assertion read around it.
        doc.Palettes.Clear();
        ((PaintedFrame)doc.Scene.Layers[0].Cels[0].Frame!).Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = color,
            Points = [new StrokePoint(10, 10, 1), new StrokePoint(100, 60, 1)],
            Brush = new BrushSettings { Size = 12, Hardness = 1, Opacity = 1, AntiAlias = false },
        });
        return doc;
    }

    private Project TwoAnimations(out DocumentRef walk, out DocumentRef idle)
    {
        var project = ProjectIo.Create("Knight", _root);
        var knight = ProjectFolders.Add(project.Manifest, "Knight");
        walk = ProjectIo.AddDocument(project, "Walk", Drawing(), knight);
        idle = ProjectIo.AddDocument(project, "Idle", Drawing("#b03040"), knight);
        return project;
    }

    [Fact]
    public void AProjectRoundTripsThroughTheFolder()
    {
        var project = TwoAnimations(out var walk, out _);
        ProjectIo.Save(project);

        var reloaded = ProjectIo.Load(_root);
        Assert.Equal("Knight", reloaded.Name);
        var knight = Assert.Single(ProjectFolders.All(reloaded.Manifest));
        Assert.Equal("Knight", knight.Name);
        var animations = ProjectFolders.DocumentsIn(reloaded.Manifest, knight);
        Assert.Equal(2, animations.Count);
        Assert.Contains(animations, a => a.Id == walk.Id);
    }

    [Fact]
    public void AStatusRoundTripsAndAnUnsetOneWritesNoKey()
    {
        // On the manifest rather than in the document, so this is what proves marking
        // something Ready survives a reload without the artwork file being involved.
        var project = TwoAnimations(out var walk, out var idle);
        walk.Status = AssetStatus.Ready;
        ProjectIo.Save(project);

        var manifest = File.ReadAllText(Path.Combine(_root, "project.json"));
        // As a name rather than a number, so a hand-edit and a diff both read. Camel
        // case, because that is what DocJson.Options applies to every enum in the
        // project — consistency with the rest of the file beats matching the C# spelling.
        Assert.Contains("\"status\": \"ready\"", manifest);

        var reloaded = ProjectIo.Load(_root);
        var animations = reloaded.Manifest.Documents;
        Assert.Equal(AssetStatus.Ready, animations.Single(a => a.Id == walk.Id).Status);
        // And the one nobody set stays unset — "nobody has said" is not "Design".
        Assert.Null(animations.Single(a => a.Id == idle.Id).Status);
    }

    [Fact]
    public void MarkingSomethingReadyDoesNotTouchTheArtwork()
    {
        // The whole reason status lives on the manifest: it must not dirty the drawing
        // and must not need it open.
        var project = TwoAnimations(out var walk, out _);
        ProjectIo.Save(project);
        var file = Path.Combine(_root, "knight", "walk.lightbox.json");
        var before = File.ReadAllBytes(file);

        walk.Status = AssetStatus.Ready;
        ProjectIo.Save(project);

        Assert.Equal(before, File.ReadAllBytes(file));
    }

    [Fact]
    public void TheLayoutIsTheOneDocumented()
    {
        var project = TwoAnimations(out _, out _);
        ProjectIo.Save(project);

        // One index file and one directory per folder, since B114. There is no
        // `characters/` and no second `character.json` — a character is a folder
        // like any other, so its work is filed the way every folder's is.
        Assert.True(File.Exists(Path.Combine(_root, "project.json")));
        Assert.False(Directory.Exists(Path.Combine(_root, "characters")));
        Assert.True(File.Exists(Path.Combine(_root, "knight", "walk.lightbox.json")));
    }

    [Fact]
    public void AnAnimationOnDiskIsAnOrdinaryDocument()
    {
        // The migration path depends on this: a loose .lightbox.json IS an
        // animation, so the two formats must not drift apart.
        var project = TwoAnimations(out var walk, out _);
        ProjectIo.Save(project);

        var loose = DocJson.Load(project.PathOf(walk));
        Assert.Single(((PaintedFrame)loose.Scene.Layers[0].Cels[0].Frame!).Strokes);
    }

    [Fact]
    public void AProjectWithNoTypeWritesNoTypeKey()
    {
        // Same discipline as Scene.Camera and Scene.Pivot: an illustration
        // project must not start carrying game-art keys because the feature
        // exists.
        var project = ProjectIo.Create("Plain", _root);
        ProjectIo.Save(project);

        var json = File.ReadAllText(Path.Combine(_root, "project.json"));
        Assert.DoesNotContain("\"type\"", json);
        Assert.Null(ProjectIo.Load(_root).Manifest.Type);
    }

    [Fact]
    public void ADeclaredTypeSurvives()
    {
        var project = ProjectIo.Create("Sprites", _root, ProjectType.GameArt);
        ProjectIo.Save(project);
        Assert.Equal(ProjectType.GameArt, ProjectIo.Load(_root).Manifest.Type);
    }

    [Fact]
    public void LoadingAProjectDoesNotReadItsDocuments()
    {
        // The entire reason a project is a folder: forty animations must not
        // cost forty reads to open.
        var project = TwoAnimations(out _, out _);
        ProjectIo.Save(project);

        var reloaded = ProjectIo.Load(_root);
        Assert.Empty(reloaded.Loaded);

        var first = reloaded.Manifest.Documents[0];
        Assert.NotNull(ProjectIo.LoadDocument(reloaded, first));
        Assert.Single(reloaded.Loaded);
    }

    [Fact]
    public void SavingRewritesOnlyTheDirtyDocument()
    {
        var project = TwoAnimations(out var walk, out var idle);
        ProjectIo.Save(project);

        var idlePath = project.PathOf(idle);
        var before = File.GetLastWriteTimeUtc(idlePath);
        // Coarse filesystem timestamps would make an immediate rewrite look
        // unchanged, so move the untouched file's stamp into the past instead
        // of sleeping.
        File.SetLastWriteTimeUtc(idlePath, before.AddMinutes(-5));
        var marker = File.GetLastWriteTimeUtc(idlePath);

        ProjectIo.Save(project, new HashSet<string> { walk.Id });

        Assert.Equal(marker, File.GetLastWriteTimeUtc(idlePath));
    }

    [Fact]
    public void SavingWithNoDirtySetWritesEveryLoadedDocument()
    {
        var project = TwoAnimations(out _, out var idle);
        ProjectIo.Save(project);
        var idlePath = project.PathOf(idle);
        File.SetLastWriteTimeUtc(idlePath, DateTime.UtcNow.AddMinutes(-5));
        var stale = File.GetLastWriteTimeUtc(idlePath);

        ProjectIo.Save(project); // Save As: everything

        Assert.NotEqual(stale, File.GetLastWriteTimeUtc(idlePath));
    }

    [Fact]
    public void AnInterruptedWriteLeavesThePreviousFileIntact()
    {
        // WriteAtomic writes a .tmp and moves it. A crash before the move is
        // simulated by the .tmp simply existing and never being moved — the
        // real file must be untouched and still parse.
        var project = TwoAnimations(out var walk, out _);
        ProjectIo.Save(project);
        var path = project.PathOf(walk);
        var original = File.ReadAllText(path);

        File.WriteAllText(path + ".tmp", "{ this is half a document");

        Assert.Equal(original, File.ReadAllText(path));
        Assert.NotNull(DocJson.Deserialize(File.ReadAllText(path)));
    }

    [Fact]
    public void SharedPalettesLiveOnTheProjectAndRoundTrip()
    {
        var project = ProjectIo.Create("Knight", _root);
        project.Palettes.Add(new Palette
        {
            Name = "Knight",
            Swatches = [new Swatch { Color = "#ff0000", Name = "Cape" }],
        });
        ProjectIo.Save(project);

        var reloaded = ProjectIo.Load(_root);
        var palette = Assert.Single(reloaded.Palettes);
        Assert.Equal("#ff0000", Assert.Single(palette.Swatches).Color);
    }

    [Fact]
    public void ASavedProjectKeepsItsSwatchIds()
    {
        // B10. Palettes were stored as .gpl, which carries names and RGB and
        // cannot carry ids — so every Stroke.SwatchId pointed at nothing after
        // a reload and the live-palette feature silently died on first reopen.
        var swatch = new Swatch { Color = "#8090a0", Name = "Armour" };
        var palette = new Palette { Name = "Knight", Swatches = [swatch] };
        var project = ProjectIo.Create("Knight", _root);
        project.Palettes.Add(palette);
        var knight = ProjectFolders.Add(project.Manifest, "Knight");
        ResourceScopes.Declare(project.Manifest, knight, PaletteScopes.Kind, palette.Id);

        var doc = Drawing();
        ((PaintedFrame)doc.Scene.Layers[0].Cels[0].Frame!).Strokes[0].SwatchId = swatch.Id;
        ProjectIo.AddDocument(project, "Walk", doc, knight);
        ProjectIo.Save(project);

        var reloaded = ProjectIo.Load(_root);
        var back = Assert.Single(reloaded.Palettes);
        Assert.Equal(palette.Id, back.Id);
        Assert.Equal(swatch.Id, Assert.Single(back.Swatches).Id);
        // And the links still resolve, which is the thing that actually broke.
        var folder = Assert.Single(ProjectFolders.All(reloaded.Manifest));
        Assert.Equal(back.Id, reloaded.PaletteFor(folder)!.Id);
    }

    [Fact]
    public void FoldersAreUniqueEvenWhenNamesCollide()
    {
        var project = ProjectIo.Create("P", _root);
        var a = ProjectFolders.Add(project.Manifest, "Knight");
        var b = ProjectFolders.Add(project.Manifest, "Knight");

        Assert.NotEqual(a.Id, b.Id);
        // Numbered rather than refused: two folders called "Knight" in one place
        // is a slip, and the artist wants the second one more than an error.
        Assert.NotEqual(a.Name, b.Name);
    }

    [Theory]
    [InlineData("Knight", "knight")]
    [InlineData("Sir Reginald III", "sir-reginald-iii")]
    [InlineData("  ", "untitled")]
    [InlineData("///", "untitled")]
    [InlineData("a//b", "a-b")]
    public void SlugsAreAlwaysUsableAsAFolderName(string name, string expected) =>
        Assert.Equal(expected, ProjectIo.Slug(name));

    [Fact]
    public void MigratingALooseDocumentGivesAOneDocumentProject()
    {
        var path = Path.Combine(Path.GetTempPath(), $"walk-{Guid.NewGuid():N}.lightbox.json");
        var doc = Drawing();
        doc.Palettes.Add(new Palette { Swatches = [new Swatch { Color = "#123456" }] });
        DocJson.Save(doc, path);
        try
        {
            var project = ProjectIo.Migrate(DocJson.Load(path), path);

            // No folder is invented — B83/B84. One document at the root is what
            // was opened, so it is what the project holds.
            Assert.Single(project.Manifest.Documents);
            Assert.Empty(ProjectFolders.All(project.Manifest));
            // The document's palettes become the project's — the point of
            // migrating is that they are now shared.
            Assert.Equal("#123456", Assert.Single(Assert.Single(project.Palettes).Swatches).Color);
            // And nothing was written: the container is offered, not imposed.
            Assert.False(Directory.Exists(project.Root));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FlattenInlinesTheSwatchesTheDocumentActuallyUses()
    {
        var used = new Swatch { Color = "#20c040" };
        var unused = new Swatch { Color = "#c02040" };
        var project = ProjectIo.Create("Knight", _root);
        project.Palettes.Add(new Palette { Swatches = [used, unused] });
        var knight = ProjectFolders.Add(project.Manifest, "Knight");

        var doc = Drawing();
        ((PaintedFrame)doc.Scene.Layers[0].Cels[0].Frame!).Strokes[0].SwatchId = used.Id;
        ProjectIo.AddDocument(project, "Walk", doc, knight);

        var flat = ProjectIo.Flatten(doc, project);

        var swatches = Assert.Single(flat.Palettes).Swatches;
        // Only what it references: an exported walk cycle must not arrive
        // carrying every other character's colours.
        Assert.Equal(used.Id, Assert.Single(swatches).Id);
        Assert.Equal("#20c040", swatches[0].Color);
    }

    [Fact]
    public void FlattenInlinesReferencedGradients()
    {
        var gradient = new Gradient { Name = "Sky" };
        var project = ProjectIo.Create("P", _root);
        project.Gradients[gradient.Id] = gradient;
        var character = ProjectFolders.Add(project.Manifest, "C");

        var doc = Drawing();
        ((PaintedFrame)doc.Scene.Layers[0].Cels[0].Frame!).Strokes.Add(new Stroke
        {
            Tool = ToolKind.Gradient,
            GradientId = gradient.Id,
            Points = [new StrokePoint(0, 0, 1), new StrokePoint(100, 0, 1)],
            Brush = new BrushSettings { Opacity = 1 },
        });
        ProjectIo.AddDocument(project, "A", doc, character);

        var flat = ProjectIo.Flatten(doc, project);
        Assert.Equal("Sky", Assert.Single(flat.Gradients).Value.Name);
    }

    [Fact]
    public void FlattenDoesNotMutateTheOpenDocument()
    {
        var swatch = new Swatch { Color = "#20c040" };
        var project = ProjectIo.Create("P", _root);
        project.Palettes.Add(new Palette { Swatches = [swatch] });

        var doc = Drawing();
        ((PaintedFrame)doc.Scene.Layers[0].Cels[0].Frame!).Strokes[0].SwatchId = swatch.Id;

        ProjectIo.Flatten(doc, project);

        // The artist still has this document open; exporting must not quietly
        // move the project's palette into it.
        Assert.Empty(doc.Palettes);
    }

    [Fact]
    public void AnEmptyProjectSavesAndLoadsWithNothingInIt()
    {
        ProjectIo.Save(ProjectIo.Create("Empty", _root));
        var reloaded = ProjectIo.Load(_root);
        Assert.Empty(ProjectFolders.All(reloaded.Manifest));
        Assert.Empty(reloaded.Manifest.Documents);
    }

    [Fact]
    public void LoadingSomethingThatIsNotAProjectFails()
    {
        Directory.CreateDirectory(_root);
        Assert.Throws<FileNotFoundException>(() => ProjectIo.Load(_root));
    }

    // ---- the palette hierarchy, project-wide ----------------------------------

    [Fact]
    public void ThePaletteHierarchySurvivesAProjectSaveAndReload()
    {
        // A project is the scope with enough palettes in it to need filing, so
        // this is where the hierarchy has to hold rather than in a lone file.
        var project = ProjectIo.Create("Knight", _root);
        var characters = new PaletteFolder { Name = "Characters" };
        var knight = new PaletteFolder { Name = "Knight", ParentId = characters.Id };
        project.PaletteFolders.AddRange([characters, knight]);
        project.Palettes.Add(new Palette { Name = "Armour", FolderId = knight.Id });
        ProjectIo.Save(project);

        var back = ProjectIo.Load(_root);

        Assert.Equal(2, back.PaletteFolders.Count);
        Assert.Equal(characters.Id, back.PaletteFolders.Single(f => f.Name == "Knight").ParentId);
        Assert.Equal(knight.Id, back.Palettes.Single().FolderId);
    }

    [Fact]
    public void AProjectWithNoFoldersWritesNoFolderFile()
    {
        // Optional means absent. A project that never files anything must be
        // byte-identical to one written before folders existed.
        var project = ProjectIo.Create("Knight", _root);
        project.Palettes.Add(new Palette { Name = "Armour" });
        ProjectIo.Save(project);

        Assert.False(File.Exists(Path.Combine(_root, "palettes", "folders.json")));
    }

    [Fact]
    public void DeletingTheLastFolderReachesTheDisk()
    {
        // Otherwise reopening the project brings the filing system back.
        var project = ProjectIo.Create("Knight", _root);
        project.PaletteFolders.Add(new PaletteFolder { Name = "Characters" });
        project.Palettes.Add(new Palette { Name = "Armour" });
        ProjectIo.Save(project);
        Assert.True(File.Exists(Path.Combine(_root, "palettes", "folders.json")));

        project.PaletteFolders.Clear();
        ProjectIo.Save(project);

        Assert.False(File.Exists(Path.Combine(_root, "palettes", "folders.json")));
        Assert.Empty(ProjectIo.Load(_root).PaletteFolders);
    }

    [Fact]
    public void APaletteFiledUnderAMissingFolderStillShowsUpOnLoad()
    {
        var project = ProjectIo.Create("Knight", _root);
        project.Palettes.Add(new Palette { Name = "Armour", FolderId = "pf_gone" });
        ProjectIo.Save(project);

        var back = ProjectIo.Load(_root);

        Assert.Null(back.Palettes.Single().FolderId);
    }
}

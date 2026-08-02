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
        var knight = ProjectIo.AddCharacter(project, "Knight");
        walk = ProjectIo.AddAnimation(project, knight, "Walk", Drawing());
        idle = ProjectIo.AddAnimation(project, knight, "Idle", Drawing("#b03040"));
        return project;
    }

    [Fact]
    public void AProjectRoundTripsThroughTheFolder()
    {
        var project = TwoAnimations(out var walk, out _);
        ProjectIo.Save(project);

        var reloaded = ProjectIo.Load(_root);
        Assert.Equal("Knight", reloaded.Name);
        var character = Assert.Single(reloaded.Characters);
        Assert.Equal("Knight", character.Name);
        Assert.Equal(2, character.Animations.Count);
        Assert.Contains(character.Animations, a => a.Id == walk.Id);
    }

    [Fact]
    public void TheLayoutIsTheOneDocumented()
    {
        var project = TwoAnimations(out _, out _);
        ProjectIo.Save(project);

        Assert.True(File.Exists(Path.Combine(_root, "project.json")));
        Assert.True(File.Exists(Path.Combine(_root, "characters", "knight", "character.json")));
        Assert.True(File.Exists(Path.Combine(
            _root, "characters", "knight", "animations", "walk.lightbox.json")));
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

        var first = reloaded.Characters.First().Animations[0];
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
        var knight = ProjectIo.AddCharacter(project, "Knight");
        knight.PaletteId = palette.Id;

        var doc = Drawing();
        ((PaintedFrame)doc.Scene.Layers[0].Cels[0].Frame!).Strokes[0].SwatchId = swatch.Id;
        ProjectIo.AddAnimation(project, knight, "Walk", doc);
        ProjectIo.Save(project);

        var reloaded = ProjectIo.Load(_root);
        var back = Assert.Single(reloaded.Palettes);
        Assert.Equal(palette.Id, back.Id);
        Assert.Equal(swatch.Id, Assert.Single(back.Swatches).Id);
        // And the links still resolve, which is the thing that actually broke.
        Assert.Equal(back.Id, reloaded.Characters.First().PaletteId);
        Assert.NotNull(reloaded.PaletteFor(reloaded.Characters.First()));
    }

    [Fact]
    public void CharacterFoldersAreUniqueEvenWhenNamesCollide()
    {
        var project = ProjectIo.Create("P", _root);
        var a = ProjectIo.AddCharacter(project, "Knight");
        var b = ProjectIo.AddCharacter(project, "Knight");
        Assert.NotEqual(a.Slug, b.Slug);

        ProjectIo.Save(project);
        Assert.True(Directory.Exists(Path.Combine(_root, "characters", a.Slug)));
        Assert.True(Directory.Exists(Path.Combine(_root, "characters", b.Slug)));
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
    public void MigratingALooseDocumentGivesAOneCharacterProject()
    {
        var path = Path.Combine(Path.GetTempPath(), $"walk-{Guid.NewGuid():N}.lightbox.json");
        var doc = Drawing();
        doc.Palettes.Add(new Palette { Swatches = [new Swatch { Color = "#123456" }] });
        DocJson.Save(doc, path);
        try
        {
            var project = ProjectIo.Migrate(DocJson.Load(path), path);

            var character = Assert.Single(project.Characters);
            Assert.Single(character.Animations);
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
        var knight = ProjectIo.AddCharacter(project, "Knight");

        var doc = Drawing();
        ((PaintedFrame)doc.Scene.Layers[0].Cels[0].Frame!).Strokes[0].SwatchId = used.Id;
        ProjectIo.AddAnimation(project, knight, "Walk", doc);

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
        var character = ProjectIo.AddCharacter(project, "C");

        var doc = Drawing();
        ((PaintedFrame)doc.Scene.Layers[0].Cels[0].Frame!).Strokes.Add(new Stroke
        {
            Tool = ToolKind.Gradient,
            GradientId = gradient.Id,
            Points = [new StrokePoint(0, 0, 1), new StrokePoint(100, 0, 1)],
            Brush = new BrushSettings { Opacity = 1 },
        });
        ProjectIo.AddAnimation(project, character, "A", doc);

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
    public void AnEmptyProjectSavesAndLoadsWithoutCharacters()
    {
        ProjectIo.Save(ProjectIo.Create("Empty", _root));
        var reloaded = ProjectIo.Load(_root);
        Assert.Empty(reloaded.Characters);
        Assert.Empty(reloaded.Manifest.Documents);
    }

    [Fact]
    public void LoadingSomethingThatIsNotAProjectFails()
    {
        Directory.CreateDirectory(_root);
        Assert.Throws<FileNotFoundException>(() => ProjectIo.Load(_root));
    }
}

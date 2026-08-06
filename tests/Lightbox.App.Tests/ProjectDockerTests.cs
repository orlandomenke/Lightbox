using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The project container as the app sees it: absent until asked for, a tree
/// when present, and one palette shared by everything under a character.
/// </summary>
[Collection("BrushState")]
public sealed class ProjectDockerTests(ITestOutputHelper output) : BrushStateIsolated, IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-app-proj-{Guid.NewGuid():N}.lbproj");

    /// <summary>
    /// Every view model this class built, so its directory watch can be handed
    /// back rather than left to a finalizer.
    /// </summary>
    /// <remarks>
    /// <b>B61.</b> Each open project arms one <c>FileSystemWatcher</c>, which on
    /// Linux is one inotify instance, and the default limit is <b>128 per
    /// user</b> — this class alone opens thirty-one. Relying on the GC to stay
    /// under that would work until it did not, and the failure mode is the worst
    /// available: <c>ProjectWatcher.Watch</c> swallows the resulting
    /// <c>IOException</c> on purpose, so a network share degrades to a manual
    /// refresh instead of refusing to open the project. Exhaust the limit and
    /// tests would go on passing while watching nothing.
    /// </remarks>
    private readonly List<MainViewModel> _built = [];

    public new void Dispose()
    {
        foreach (var vm in _built) vm.ProjectDocker.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        base.Dispose();
    }

    private MainViewModel Vm()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        _built.Add(vm);
        return vm;
    }

    /// <summary>
    /// Add the character these tests were written around, and write it.
    /// </summary>
    /// <remarks>
    /// <b>B83/B84.</b> <c>NewProject</c> used to invent a character from the
    /// project's own name, which is the bug — it put the artist's first drawing
    /// at <c>characters/knight/animations/</c> and created two folders nobody
    /// asked for. These tests are about characters rather than about that
    /// invention, so they ask for one explicitly, with the name they always
    /// assumed so their slugs and paths are unchanged.
    ///
    /// Saved, not merely added: <c>NewProject</c> writes on the way out, so a
    /// character added afterwards would exist in the manifest and not on disk —
    /// and the docker would correctly report it missing.
    /// </remarks>
    private void WithKnight(MainViewModel vm)
    {
        var project = vm.ProjectDocker.Project!;
        var knight = ProjectIo.AddCharacter(project, "Knight");
        // And the adopted document goes under it. That is the arrangement these
        // tests were written around; the only change is that they now ask for it
        // instead of NewProject inventing it.
        foreach (var adopted in project.Manifest.Documents.ToList())
        {
            ProjectIo.MoveDocument(project, adopted, knight);
        }
        // Written, not merely recorded: NewProject saves on the way out, so
        // anything added afterwards exists in the manifest and not on disk — and
        // the docker would rightly report it missing.
        vm.SaveProject(everything: true);
        vm.ProjectDocker.Refresh();
    }

    // ---- absence ------------------------------------------------------------

    [AvaloniaFact]
    public void TheAppOpensWithNoProject()
    {
        // Document-first. Optional means absent, not disabled — the same rule
        // the camera follows. Someone who opened the app to draw one picture
        // must never be shown a character tree.
        var vm = Vm();
        Assert.False(vm.HasProject);
        Assert.Null(vm.ProjectDocker.Project);
        Assert.Empty(vm.ProjectDocker.Rows);
        Assert.Null(vm.ActiveTab!.Source);
    }

    [AvaloniaFact]
    public void WithNoProjectADocumentSavesAndLoadsExactlyAsBefore()
    {
        var vm = Vm();
        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(80, 80, 1);
        vm.EndStroke();

        var json = vm.SerializeDocument();
        // No project keys leak into a document that never joined one.
        Assert.DoesNotContain("\"characters\"", json);
        Assert.Equal(json, vm.ExportStandaloneDocument());
    }

    // ---- creation -----------------------------------------------------------

    [AvaloniaFact]
    public void NewProjectAdoptsTheDocumentAlreadyOpen()
    {
        // Adopting rather than starting empty: the artist has been drawing, and
        // the container should form around that work.
        var vm = Vm();
        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(80, 80, 1);
        vm.EndStroke();

        vm.NewProject(_root, "Knight");

        Assert.True(vm.HasProject);
        // B83/B84. Adopted as a project document, not as an animation of a
        // character invented from the project's own name — that invention is
        // what put the first drawing at `characters/knight/animations/` and
        // created two folders nobody asked for.
        Assert.Empty(vm.ProjectDocker.Project!.Characters);
        var adopted = Assert.Single(vm.ProjectDocker.Project!.Manifest.Documents);
        Assert.Equal(adopted.Id, vm.ActiveTab!.Source?.Id);

        // And it landed on disk with the work in it — the half of this test that
        // was always the point, and is unchanged.
        var saved = Lightbox.Core.Serialization.DocJson.Load(vm.ProjectDocker.Project!.PathOf(adopted));
        Assert.Single(((PaintedFrame)saved.Scene.Layers[^1].Cels[0].Frame!).Strokes);
    }

    [AvaloniaFact]
    public void TheDockerListsCharactersWithTheirAnimationsUnderThem()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        vm.ProjectDocker.AddAnimationCommand.Execute(null);

        // B62 put the project itself at the top, so the shape is now
        // project, character, adopted animation, new one.
        var rows = vm.ProjectDocker.Rows;
        Assert.Equal(4, rows.Count);
        Assert.True(rows[0].IsRoot);
        Assert.True(rows[1].IsCharacter);
        Assert.False(rows[2].IsCharacter);
        Assert.False(rows[3].IsCharacter);
    }

    [AvaloniaFact]
    public void AddingAnAnimationOpensItAsATabBoundToItsSlot()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        WithKnight(vm);
        var before = vm.Tabs.Count;

        vm.ProjectDocker.AddAnimationCommand.Execute(null);

        Assert.Equal(before + 1, vm.Tabs.Count);
        var reference = vm.ProjectDocker.Rows[^1].Animation!;
        Assert.Equal(reference.Id, vm.ActiveTab!.Source?.Id);
    }

    [AvaloniaFact]
    public void OpeningAnAnimationTwiceFocusesTheTabRatherThanDuplicatingIt()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        vm.ProjectDocker.AddAnimationCommand.Execute(null);
        var count = vm.Tabs.Count;

        vm.ProjectDocker.Selected = vm.ProjectDocker.Rows[1]; // the adopted one
        vm.ProjectDocker.OpenSelected();
        Assert.Equal(count, vm.Tabs.Count);

        vm.ProjectDocker.Selected = vm.ProjectDocker.Rows[2];
        vm.ProjectDocker.OpenSelected();
        Assert.Equal(count, vm.Tabs.Count);
    }

    [AvaloniaFact]
    public void FileNewStillMakesAStandaloneDocumentWithAProjectOpen()
    {
        // The most common action in the app must not change meaning based on
        // which row happens to be selected.
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        var animations = vm.ProjectDocker.Project!.AllDocuments.Count();

        vm.NewDocument(new NewDocumentSettings("Loose", 128, 128, 12, 72, "#ffffff", false));

        Assert.Null(vm.ActiveTab!.Source);
        Assert.Equal(animations, vm.ProjectDocker.Project!.AllDocuments.Count());
    }

    // ---- sharing ------------------------------------------------------------

    [AvaloniaFact]
    public void TwoAnimationsUnderOneCharacterPaintFromOnePalette()
    {
        // The promise Pillar 1 is named for.
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        var swatch = new Swatch { Color = "#20c040" };
        vm.ProjectDocker.Project!.Palettes.Add(new Palette { Name = "Knight", Swatches = [swatch] });
        vm.RefreshProjectResources();

        var first = StrokeUsing(vm, swatch.Id);
        vm.ProjectDocker.AddAnimationCommand.Execute(null);
        var second = StrokeUsing(vm, swatch.Id);

        Assert.Equal(new SKColor(0x20, 0xc0, 0x40), BrushEngine.StrokeColor(first));
        Assert.Equal(new SKColor(0x20, 0xc0, 0x40), BrushEngine.StrokeColor(second));

        swatch.Color = "#c02040";
        Assert.Equal(new SKColor(0xc0, 0x20, 0x40), BrushEngine.StrokeColor(first));
        Assert.Equal(new SKColor(0xc0, 0x20, 0x40), BrushEngine.StrokeColor(second));
    }

    private static Stroke StrokeUsing(MainViewModel vm, string swatchId)
    {
        var stroke = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            SwatchId = swatchId,
            Points = [new StrokePoint(10, 10, 1), new StrokePoint(60, 60, 1)],
            Brush = new BrushSettings { Size = 12, Opacity = 1 },
        };
        ((PaintedFrame)vm.PaintLayer().Cels[0].Frame!).Strokes.Add(stroke);
        return stroke;
    }

    // ---- saving -------------------------------------------------------------

    [AvaloniaFact]
    public void SaveWritesTheProjectWithoutAPicker()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        Assert.True(vm.CanSaveInPlace);

        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(80, 80, 1);
        vm.EndStroke();
        vm.Save();

        var animation = vm.ProjectDocker.Project!.AllDocuments.First();
        var saved = Lightbox.Core.Serialization.DocJson.Load(vm.ProjectDocker.Project!.PathOf(animation));
        Assert.Single(((PaintedFrame)saved.Scene.Layers[^1].Cels[0].Frame!).Strokes);
    }

    [AvaloniaFact]
    public void WithoutAProjectOrAPathThereIsNothingToSaveInPlace()
    {
        // Which is what makes Ctrl+S fall through to Save as… instead of
        // appearing to work and writing nothing.
        Assert.False(Vm().CanSaveInPlace);
    }

    [AvaloniaFact]
    public void AProjectReopensWithItsCharactersAndAnimations()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        WithKnight(vm);
        vm.ProjectDocker.AddAnimationCommand.Execute(null);
        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(80, 80, 1);
        vm.EndStroke();
        vm.Save();

        var reopened = Vm();
        reopened.OpenProject(_root);

        Assert.True(reopened.HasProject);
        var character = Assert.Single(reopened.ProjectDocker.Project!.Characters);
        Assert.Equal(2, character.Animations.Count);
        // And it opened one, so the project is not an empty shell.
        Assert.Contains(reopened.Tabs, t => t.Source is not null);
    }

    [AvaloniaFact]
    public void RemovingAnAnimationLeavesItsFileOnDisk()
    {
        // Removing a row from an index is cheap to undo by hand; deleting an
        // artist's drawing because they clicked the wrong row is not.
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        var animation = vm.ProjectDocker.Project!.AllDocuments.First();
        var path = vm.ProjectDocker.Project!.PathOf(animation);
        Assert.True(File.Exists(path));

        // The animation, named as such. "Not a character" used to mean it and
        // stopped meaning it when B62 added the project row, which is also not a
        // character — and selects first.
        vm.ProjectDocker.Selected = vm.ProjectDocker.Rows.First(r => r.Animation is not null);
        vm.ProjectDocker.RemoveSelectedCommand.Execute(null);

        Assert.Empty(vm.ProjectDocker.Project!.AllDocuments);
        Assert.True(File.Exists(path));
    }

    // ---- making things inside the project -----------------------------------

    [AvaloniaFact]
    public void TheNewMenuOffersOneEntryPerPlaceWorkCanLand()
    {
        // Each lands somewhere specific. Creating work inside a project should
        // not be "make it, then file it".
        //
        // The order is the two axes then the loose case: a character and its
        // animations, a scene and its shots, and last the document that
        // belongs to neither.
        var vm = Vm();
        vm.NewProject(_root, "Knight");

        // B86 put Folder first: it is the artist's own structure, and a
        // production is organised by it rather than by the two fixed axes.
        // B63 then grouped them — containers, then the drawings that go in
        // them — because six words in a row read as an undifferentiated pile.
        Assert.Equal(
            ["Folder", "Character", "Scene", "Animation", "Shot", "Document"],
            vm.ProjectDocker.NewItemKinds.Select(k => k.Label));
    }

    [AvaloniaFact]
    public void ADocumentCreatedFromTheDockerBelongsToTheProjectNotACharacter()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        WithKnight(vm);

        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewLooseDocument);

        var loose = Assert.Single(vm.ProjectDocker.Project!.Manifest.Documents);
        Assert.DoesNotContain(
            vm.ProjectDocker.Project!.Characters.SelectMany(c => c.Animations),
            a => a.Id == loose.Id);
        Assert.StartsWith("documents/", loose.Path);
        // And it opened, bound to its slot — the same as adding an animation.
        Assert.Equal(loose.Id, vm.ActiveTab!.Source?.Id);
    }

    [AvaloniaFact]
    public void ALooseDocumentGetsItsOwnRowWithNoCharacterAboveIt()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewLooseDocument);

        var row = vm.ProjectDocker.Rows[^1];
        Assert.True(row.IsLoose);
        Assert.Null(row.Character);
        Assert.Equal(0, row.Indent);
    }

    // ---- re-filing ------------------------------------------------------------

    [AvaloniaFact]
    public void MovingADocumentToAnotherCharacterRepathsItAndKeepsItsId()
    {
        // The id has to survive: a tab already showing the document stays
        // bound to it, so rearranging the tree does not orphan the window you
        // are drawing in.
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        WithKnight(vm);
        vm.ProjectDocker.AddCharacterCommand.Execute(null);
        var project = vm.ProjectDocker.Project!;
        var from = project.Characters.First();
        var to = project.Characters.Last();
        Assert.NotSame(from, to);

        var row = vm.ProjectDocker.Rows.First(r => r.Animation is not null && r.Character == from);
        var id = row.Animation!.Id;

        Assert.True(vm.ProjectDocker.Move(row, to));

        Assert.Empty(from.Animations);
        var moved = Assert.Single(to.Animations);
        Assert.Equal(id, moved.Id);
        Assert.Contains(to.Slug, moved.Path);
    }

    [AvaloniaFact]
    public void MovingADocumentToTheProjectTakesItOutOfEveryCharacter()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        WithKnight(vm);
        var project = vm.ProjectDocker.Project!;
        var row = vm.ProjectDocker.Rows.First(r => r.Animation is not null);

        Assert.True(vm.ProjectDocker.Move(row, null));

        Assert.Empty(project.Characters.SelectMany(c => c.Animations));
        Assert.Single(project.Manifest.Documents);
    }

    [AvaloniaFact]
    public void MovingADocumentWhereItAlreadyIsDoesNothing()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        var row = vm.ProjectDocker.Rows.First(r => r.Animation is not null);

        Assert.False(vm.ProjectDocker.Move(row, row.Character));
    }

    [AvaloniaFact]
    public void AMovedDocumentSurvivesASaveAndReopen()
    {
        // The end-to-end claim: the manifest, the file and the reload agree.
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        WithKnight(vm);
        vm.ProjectDocker.AddCharacterCommand.Execute(null);
        var to = vm.ProjectDocker.Project!.Characters.Last();
        var row = vm.ProjectDocker.Rows.First(r => r.Animation is not null);
        vm.ProjectDocker.Move(row, to);
        vm.Save();

        var reopened = Vm();
        reopened.OpenProject(_root);

        var characters = reopened.ProjectDocker.Project!.Characters.ToList();
        Assert.Empty(characters[0].Animations);
        Assert.Single(characters[1].Animations);
    }

    [AvaloniaFact]
    public void RenamingARowWritesThrough()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        WithKnight(vm);
        var row = Assert.Single(vm.ProjectDocker.Rows, r => r.IsCharacter);

        Assert.True(vm.ProjectDocker.Rename(row, "Sir Reginald"));

        Assert.Equal("Sir Reginald", vm.ProjectDocker.Project!.Characters.First().Name);
        Assert.Equal("Sir Reginald", row.Name);
    }

    // ---- reaching the files ---------------------------------------------------

    [AvaloniaFact]
    public void EveryRowKnowsWhereItIsOnDisk()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        WithKnight(vm);
        var docker = vm.ProjectDocker;
        var character = docker.Rows.First(r => r.IsCharacter);
        var animation = docker.Rows.First(r => r.Animation is not null);

        Assert.Equal(_root, docker.RootPath);
        // A character is a folder; an animation is a file inside it.
        Assert.Equal(Path.Combine(_root, "characters", "knight"), docker.PathOf(character));
        Assert.StartsWith(Path.Combine(_root, "characters", "knight"), docker.PathOf(animation));
        Assert.EndsWith(".lightbox.json", docker.PathOf(animation));
        // Nothing selected is the project itself, which is what the folder
        // button in the header opens.
        Assert.Equal(_root, docker.PathOf(null));
    }

    [AvaloniaFact]
    public void WithNoProjectThereIsNoPathToShow()
    {
        var docker = Vm().ProjectDocker;

        Assert.Null(docker.RootPath);
        Assert.Null(docker.PathOf(null));
        Assert.Null(docker.SelectedPath);
    }

    [AvaloniaFact]
    public void CopyPathGivesTheSelectedRowsFile()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        vm.ProjectDocker.Selected = vm.ProjectDocker.Rows.First(r => r.Animation is not null);

        vm.ProjectDocker.CopySelectedPathCommand.Execute(null);

        Assert.Equal(vm.ProjectDocker.SelectedPath, vm.ProjectDocker.CopiedPath);
        Assert.EndsWith(".lightbox.json", vm.ProjectDocker.CopiedPath);
    }

    [AvaloniaFact]
    public void OpeningExternallySaysSoWhenTheFileIsNotWrittenYet()
    {
        // An animation can exist in the manifest before it exists on disk — a
        // duplicate, until the next save. Handing that path to the desktop
        // would do nothing at all and look like the menu item was broken.
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        vm.Save();
        vm.ProjectDocker.Selected = vm.ProjectDocker.Rows.First(r => r.Animation is not null);
        vm.ProjectDocker.DuplicateSelectedCommand.Execute(null);
        Assert.False(File.Exists(vm.ProjectDocker.SelectedPath));

        vm.ProjectDocker.OpenSelectedExternallyCommand.Execute(null);

        Assert.Contains("Save the project first", vm.ProjectDocker.Status);
    }

    [AvaloniaFact]
    public void DuplicatingAnAnimationCopiesItsArtIntoTheSameCharacter()
    {
        // A cycle you want to vary — a walk into a limp — starts as a copy of
        // the walk, and the alternative was exporting and re-importing it.
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        WithKnight(vm);
        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(80, 80, 1);
        vm.EndStroke();
        vm.Save();
        var source = vm.ProjectDocker.Rows.First(r => r.Animation is not null);
        vm.ProjectDocker.Selected = source;

        vm.ProjectDocker.DuplicateSelectedCommand.Execute(null);

        var character = vm.ProjectDocker.Project!.Characters.First();
        Assert.Equal(2, character.Animations.Count);
        var copy = character.Animations[1];
        Assert.Equal($"{source.Animation!.Name} copy", copy.Name);
        Assert.NotEqual(source.Animation.Path, copy.Path);
        // The art came with it, and it is a copy rather than the same object.
        var original = vm.ProjectDocker.Project.Loaded[source.Animation.Id];
        var duplicate = vm.ProjectDocker.Project.Loaded[copy.Id];
        Assert.NotSame(original, duplicate);
        Assert.Equal(
            original.Scene.Layers.Sum(l => l.Cels.Count),
            duplicate.Scene.Layers.Sum(l => l.Cels.Count));
        Assert.Contains(
            duplicate.Scene.Layers.SelectMany(l => l.Cels),
            c => c.Frame is PaintedFrame { Strokes.Count: > 0 });
    }

    [AvaloniaFact]
    public void DuplicatingWritesTheCopyOnTheNextSave()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        WithKnight(vm);
        vm.Save();
        vm.ProjectDocker.Selected = vm.ProjectDocker.Rows.First(r => r.Animation is not null);
        vm.ProjectDocker.DuplicateSelectedCommand.Execute(null);

        vm.Save();

        var copy = vm.ProjectDocker.Project!.Characters.First().Animations[1];
        Assert.True(File.Exists(Path.Combine(
            _root, copy.Path.Replace('/', Path.DirectorySeparatorChar))));
    }

    // ---- B61: the docker against what is actually on disk --------------------

    /// <summary>
    /// The reported defect: a document deleted from disk goes on being listed.
    /// </summary>
    /// <remarks>
    /// The reporter's own follow-up is what this asserts rather than what it
    /// first looked like — after a restart the files on disk were correct, so
    /// nothing was wrong with what got written. The docker was describing the
    /// manifest and calling it the disk.
    /// </remarks>
    [AvaloniaFact]
    public void DeletingAFolderOnDiskRemovesItFromTheDocker()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        vm.ProjectDocker.AddAnimationCommand.Execute(null);
        vm.SaveProject();

        var row = vm.ProjectDocker.Rows.First(r => r.Animation is not null);
        var path = vm.ProjectDocker.PathOf(row);
        Assert.NotNull(path);
        Assert.True(File.Exists(path), "the animation was never written, so this tests nothing");
        Assert.False(row.Missing);

        File.Delete(path!);
        vm.ProjectDocker.Refresh();

        var after = vm.ProjectDocker.Rows.First(r => r.Animation is not null);
        Assert.True(
            after.Missing,
            "the docker still presents a document whose file has been deleted as though it were there");
        Assert.True(vm.ProjectDocker.HasMissing);
        Assert.Equal(1, vm.ProjectDocker.MissingCount);
    }

    /// <summary>
    /// The other half, and the one that makes the flag worth having: a refresh
    /// must not need the application to be restarted.
    /// </summary>
    [AvaloniaFact]
    public void TheDockerRefreshesWithoutBeingReopened()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        vm.ProjectDocker.AddAnimationCommand.Execute(null);
        vm.SaveProject();

        var row = vm.ProjectDocker.Rows.First(r => r.Animation is not null);
        var path = vm.ProjectDocker.PathOf(row)!;
        File.Delete(path);
        vm.ProjectDocker.Refresh();
        Assert.True(vm.ProjectDocker.HasMissing);

        // And it recovers: putting the file back clears the flag on the next
        // refresh, so this reports the world rather than latching on first sight.
        File.WriteAllText(path, "{}");
        vm.ProjectDocker.Refresh();
        Assert.False(vm.ProjectDocker.HasMissing);
        Assert.Equal(0, vm.ProjectDocker.MissingCount);
    }

    /// <summary>
    /// An unsaved project has nothing on disk, and marking every row missing
    /// there would be true and useless.
    /// </summary>
    [AvaloniaFact]
    public void AnUnsavedProjectDoesNotReportEveryRowAsMissing()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        WithKnight(vm);
        vm.ProjectDocker.AddAnimationCommand.Execute(null);

        // No SaveProject: nothing has been written anywhere yet.
        vm.ProjectDocker.Refresh();

        Assert.False(
            vm.ProjectDocker.HasMissing,
            "an unsaved project reported its rows as missing from disk, which is true of all of them "
            + "and tells the artist nothing they can act on");
    }

    // ---- B61: and something has to call Refresh ------------------------------

    /// <summary>
    /// The watch follows the project, and null — the ordinary state — watches
    /// nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three tests above drive <c>Refresh()</c> by hand, which is why the
    /// <c>Missing</c> flag landed while the reported behaviour did not change:
    /// <b>nothing in the running application ever called it.</b> A grep for
    /// <c>.Refresh()</c> across <c>src/</c> found the symbol browser and the
    /// shortcut editor and no project docker at all.
    /// </para>
    /// <para>
    /// Armed on <c>Adopt</c> because that is the one funnel every project arrives
    /// through — <c>NewProject</c> and <c>OpenProject</c> both go through it — so
    /// a future third way in cannot forget. And released when the project goes,
    /// because a document-first application that never opens a project must not
    /// hold an OS handle for a folder it does not have.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void TheWatchFollowsTheProjectAndNotTheApplication()
    {
        var vm = Vm();
        Assert.False(vm.ProjectDocker.Watcher.IsWatching, "a watch was armed before any project existed");
        Assert.Null(vm.ProjectDocker.Watcher.Root);

        vm.NewProject(_root, "Knight");
        Assert.True(
            vm.ProjectDocker.Watcher.IsWatching,
            "no directory watch was armed, so nothing will ever call Refresh and B61 is unchanged");
        Assert.Equal(_root, vm.ProjectDocker.Watcher.Root);

        vm.ProjectDocker.Adopt(null);
        Assert.False(vm.ProjectDocker.Watcher.IsWatching, "closing the project left the folder watched");
        Assert.Null(vm.ProjectDocker.Watcher.Root);
    }

    /// <summary>
    /// A burst of disk events costs one re-read, not one each.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The half of B61 that decides whether the fix is an improvement or a stall.
    /// A checkout, an unzip or the project's own save fires one event per file
    /// and arrives as hundreds within a few milliseconds; a docker that re-reads
    /// per event turns someone else's `git switch` into a freeze.
    /// </para>
    /// <para>
    /// Counted rather than timed, and driven through <c>Notify</c>/<c>Flush</c>
    /// rather than through the filesystem, so this measures the coalescing and
    /// not the machine or the OS. <b>A count and not a flag</b>: "it refreshed"
    /// is also true when it refreshed two hundred times, which is the defect.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void ABurstOfDiskEventsCostsOneRefresh()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        var watcher = vm.ProjectDocker.Watcher;

        var before = watcher.Refreshes;
        for (var i = 0; i < 200; i++) watcher.Notify();
        Assert.True(watcher.Pending, "200 events left nothing waiting, so none of them registered");
        Assert.Equal(before, watcher.Refreshes);   // nothing re-read *during* the burst

        watcher.Flush();

        // Exactly one, and the "not vacuous" half: zero would also satisfy "at
        // most one", and zero is a watcher wired to nothing.
        Assert.Equal(before + 1, watcher.Refreshes);
        Assert.False(watcher.Pending);

        // And a flush with nothing waiting is free, so an idle project does not
        // re-read on a timer.
        watcher.Flush();
        Assert.Equal(before + 1, watcher.Refreshes);
    }

    /// <summary>
    /// End to end: a file deleted behind the application's back reaches the row,
    /// with nothing calling <c>Refresh</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two tests above are exact and neither touches the operating system, so
    /// between them they would pass on a build where <c>FileSystemWatcher</c> was
    /// constructed and never subscribed to. This is the one that says an inotify
    /// event actually arrives and reaches the row.
    /// </para>
    /// <para>
    /// <b>Asserted on the outcome rather than on the event, deliberately.</b>
    /// Counting events would need this test to tell the delete's from the save's,
    /// which arrive on their own schedule; <c>HasMissing</c> cannot be true before
    /// the delete however many times the docker re-reads, so the outcome is
    /// unambiguous where a count would need care. The half before the delete is
    /// what makes that airtight: pumping and flushing while the file exists must
    /// leave the flag alone.
    /// </para>
    /// <para>
    /// <b>What is not asserted here: the debounce timer firing.</b> This flushes
    /// explicitly, because whether a <c>DispatcherTimer</c> ticks under a headless
    /// pump is a fact about Avalonia's test harness rather than about B61, and a
    /// test that depends on it would fail for reasons nobody could read. The logic
    /// that timer drives is <see cref="ABurstOfDiskEventsCostsOneRefresh"/>; the
    /// wiring between them is one line and is checked by hand
    /// (<c>MANUAL_TESTING.md</c>).
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void ADeletionOnDiskReachesTheRowWithoutARefreshCall()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        vm.ProjectDocker.AddAnimationCommand.Execute(null);
        vm.SaveProject();

        var watcher = vm.ProjectDocker.Watcher;
        var row = vm.ProjectDocker.Rows.First(r => r.Animation is not null);
        var path = vm.ProjectDocker.PathOf(row)!;
        Assert.True(File.Exists(path), "the animation was never written, so this tests nothing");

        // While the file is there, no amount of watching and re-reading may raise
        // the flag. This is what stops the assertion below being satisfied by a
        // stray refresh rather than by the deletion.
        Drain(watcher, until: () => vm.ProjectDocker.HasMissing, TimeSpan.FromMilliseconds(400));
        Assert.False(
            vm.ProjectDocker.HasMissing,
            "a row whose file is present was reported missing, so the assertion below would prove nothing");

        File.Delete(path);

        var noticed = Drain(watcher, until: () => vm.ProjectDocker.HasMissing, TimeSpan.FromSeconds(5));
        Assert.True(
            noticed,
            "five seconds after a file was deleted from the project folder the docker still presented it "
            + "as though it were there — no filesystem event reached the watcher, which is B61 exactly");
        Assert.Equal(1, vm.ProjectDocker.MissingCount);
    }

    /// <summary>
    /// A re-read keeps the rows it already had, and only those that still stand
    /// for the same thing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Found by breaking an unrelated test, which is why it is written down
    /// here.</b> Arming the watch made a save trigger a re-read, the re-read
    /// rebuilt every row from scratch, and
    /// <c>WorkspaceTests.TheProjectRowMenuActuallyDoesSomethingWhenClicked</c>
    /// started failing: it held a row, clicked Status ▸ Ready through the real
    /// menu, and the click landed on a row that had already been discarded. On
    /// screen that is invisible — the list looks right — and the interaction is
    /// silently addressing an object nobody can see any more.
    /// </para>
    /// <para>
    /// It also falsified the reasoning written into <c>ProjectWatcher</c>, which
    /// had argued that self-inflicted events were harmless because "a re-read is
    /// idempotent". The cost was right and the side effect was missed, which is
    /// the shape this repository keeps paying for. A re-read is idempotent
    /// <em>now</em>.
    /// </para>
    /// <para>
    /// The second half is the one a naive fix gets wrong: keyed reuse alone would
    /// keep the row when a document is re-filed under another character, because
    /// <c>Move</c> deliberately keeps the document's id. That row is a different
    /// row — differently indented, no longer loose — so identity has to be the
    /// underlying objects rather than the key.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void ARefreshKeepsTheRowsThatStillStandForTheSameThing()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        vm.ProjectDocker.AddAnimationCommand.Execute(null);
        vm.SaveProject();

        var row = vm.ProjectDocker.Rows.First(r => r.Animation is not null);
        vm.ProjectDocker.Selected = row;
        // Interaction state lives on the row, so a replaced row silently loses it.
        row.IsRenaming = true;

        vm.ProjectDocker.Refresh();

        Assert.Same(row, vm.ProjectDocker.Rows.First(r => r.Animation is not null));
        Assert.Same(row, vm.ProjectDocker.Selected);
        Assert.True(
            row.IsRenaming,
            "a re-read discarded the row a rename was in progress on, so the edit box was editing "
            + "an object no longer in the list");

        // And the negative: re-filed under nothing, the id is unchanged and the row
        // is not the same row. Found by id rather than by position — project-level
        // documents are listed after the characters, so the row order changes too.
        var id = row.Animation!.Id;
        var moved = vm.ProjectDocker.Move(row, null);
        Assert.True(moved, "the document was not re-filed, so the check below proves nothing");
        var after = vm.ProjectDocker.Rows.Single(r => r.Animation?.Id == id);
        Assert.NotSame(row, after);
        Assert.True(after.IsLoose, "a document moved to the project should read as a project-level row");
    }

    /// <summary>
    /// The manual re-read exists, says what it found, and is rebindable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not a nicety — the watch's failure path depends on it.</b>
    /// <c>ProjectWatcher.Watch</c> swallows an <c>IOException</c> so a project on a
    /// network share or a platform without inotify still opens instead of
    /// refusing to. That is the right trade only if there is another way to get a
    /// current view; without one, the swallow turns B61 into a bug with no
    /// workaround.
    /// </para>
    /// <para>
    /// The registry half is <c>CLAUDE.md</c>'s rule about landing the places a
    /// feature shows up, and it is the one with a history here: a command wired
    /// straight to a gesture works perfectly and is invisible to the whole
    /// configuration system, which is how <c>Ctrl+Shift+S</c> came to have no
    /// binding at all.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void AManualReReadIsReachableAndReportsWhatItFound()
    {
        var map = new Lightbox.App.Services.ShortcutMap();
        var refresh = map.Definitions.FirstOrDefault(d => d.Id == "project.refresh");
        Assert.NotNull(refresh);
        Assert.Equal(Avalonia.Input.Key.F5, refresh!.Default!.Key);

        var vm = Vm();

        // With no project it must be inert rather than throwing: the shortcut is
        // global, and F5 in a document-first session with no project is ordinary.
        vm.ProjectDocker.RefreshFromDiskCommand.Execute(null);
        Assert.Equal(string.Empty, vm.ProjectDocker.Status);

        vm.NewProject(_root, "Knight");
        vm.ProjectDocker.AddAnimationCommand.Execute(null);
        vm.SaveProject();

        vm.ProjectDocker.RefreshFromDiskCommand.Execute(null);
        Assert.Contains("everything is where the project says", vm.ProjectDocker.Status);

        var path = vm.ProjectDocker.PathOf(vm.ProjectDocker.Rows.First(r => r.Animation is not null))!;
        File.Delete(path);
        vm.ProjectDocker.RefreshFromDiskCommand.Execute(null);

        // Says the number, because "refreshed" with nothing else to show is
        // indistinguishable from a button wired to nothing.
        Assert.Contains("1 item is not on disk", vm.ProjectDocker.Status);
    }

    /// <summary>
    /// Pump the dispatcher and flush the watcher until <paramref name="until"/>
    /// holds or the deadline passes. Returns whether it held.
    /// </summary>
    /// <remarks>
    /// The watcher raises on a thread-pool thread and marshals to the UI thread,
    /// which in a headless test is this thread — so its post does not run until
    /// something pumps it. This is what pumping looks like. The deadline guards
    /// against a hang; it is not a latency measurement, and nothing asserts how
    /// long the loop took.
    /// </remarks>
    private static bool Drain(
        Lightbox.App.Services.ProjectWatcher watcher, Func<bool> until, TimeSpan deadline)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            watcher.Flush();
            if (until()) return true;
            if (clock.Elapsed >= deadline) return false;
            Thread.Sleep(10);
        }
    }

    // ---- B65: the name is asked for before anything is written ---------------

    /// <summary>
    /// The reported defect: everything arrived numbered because nothing asked.
    /// </summary>
    [AvaloniaFact]
    public void CreatingAnItemAsksForItsNameFirst()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        var docker = vm.ProjectDocker;

        docker.AddItemNamed(ProjectViewModel.NewCharacterItem, "Rusty knight");
        docker.AddItemNamed(ProjectViewModel.NewSceneItem, "The duel");

        Assert.Contains(docker.Rows, r => r.IsCharacter && r.Name == "Rusty knight");
        Assert.Contains(docker.Rows, r => r.IsScene && r.Name == "The duel");
        // ...and nothing arrived numbered, which is the symptom that was reported.
        Assert.DoesNotContain(docker.Rows, r => r.Name == "Character 2");
    }

    /// <summary>
    /// The suggestion has to be what Enter would have produced anyway, or the
    /// box is lying about the default.
    /// </summary>
    [AvaloniaFact]
    public void TheSuggestedNameMatchesTheNumberedFallback()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        var docker = vm.ProjectDocker;

        var suggested = docker.SuggestedNameFor(ProjectViewModel.NewSceneItem);
        docker.AddItemNamed(ProjectViewModel.NewSceneItem, null);   // take the default

        Assert.Contains(docker.Rows, r => r.IsScene && r.Name == suggested);
    }

    /// <summary>
    /// A blank answer is not a name. It falls back rather than creating an item
    /// called "   ", because by this point the artist has asked for one.
    /// </summary>
    [AvaloniaFact]
    public void ABlankNameFallsBackRatherThanCreatingAnUnnamedItem()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        var docker = vm.ProjectDocker;

        docker.AddItemNamed(ProjectViewModel.NewCharacterItem, "   ");

        Assert.DoesNotContain(docker.Rows, r => string.IsNullOrWhiteSpace(r.Name));
        Assert.Contains(docker.Rows, r => r.IsCharacter && r.Name.StartsWith("Character "));
    }

    /// <summary>
    /// The old command still means what it did, so nothing that already created
    /// an item changed behaviour when the name was threaded through.
    /// </summary>
    [AvaloniaFact]
    public void TheUnnamedCommandStillCreatesTheNumberedDefault()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        var docker = vm.ProjectDocker;

        docker.AddItemCommand.Execute(ProjectViewModel.NewSceneItem);

        Assert.Contains(docker.Rows, r => r.IsScene && r.Name.StartsWith("Scene "));
    }

    // ---- B62: where the project is, and what the reveal button acts on -------

    /// <summary>
    /// The project itself is the first row, named after the folder on disk.
    /// </summary>
    /// <remarks>
    /// <b>B62's second half</b>, and the half that makes the first half safe. The
    /// tree listed everything <em>in</em> a project and never the project, so
    /// there was nothing to select and no way to see where the work actually
    /// lives. Without this row, moving the toolbar button onto the selection
    /// would have deleted a capability rather than moved it.
    /// </remarks>
    [AvaloniaFact]
    public void TheProjectRootIsVisibleInTheDocker()
    {
        var vm = Vm();
        // Absent with no project, like every other piece of project UI. Optional
        // means absent, not disabled — asserted before the project exists so the
        // row cannot quietly become the thing that breaks document-first.
        Assert.Empty(vm.ProjectDocker.Rows);

        vm.NewProject(_root, "Knight");
        var docker = vm.ProjectDocker;

        var root = docker.Rows[0];
        Assert.True(root.IsRoot);
        // The folder on disk, not the manifest name: the docker's title already
        // says "Knight", and repeating it here would answer a question nobody
        // asked while leaving the reported one — where is it? — unanswered.
        Assert.Equal(Path.GetFileName(_root), root.Name);
        Assert.EndsWith(".lbproj", root.Name);
        Assert.Equal(_root, docker.PathOf(root));

        // It is a place, not a thing in the project. Every one of these would
        // put it in a code path written for characters or documents.
        Assert.False(root.IsCharacter);
        Assert.False(root.IsFolder);
        Assert.False(root.IsScene);
        Assert.False(root.IsLoose);
        Assert.Null(root.Animation);
        Assert.Equal(0, root.Indent);
        Assert.Equal("", root.Twisty);
        // And never "not on disk" — PathOf answers the project root for several
        // row kinds, so MarkMissing has to exclude it or the project reports
        // itself missing.
        Assert.False(root.Missing);

        // Survives a rebuild as the same object (B61's rule: the row instance is
        // what an open menu and an in-flight rename are holding).
        docker.Refresh();
        Assert.Same(root, docker.Rows[0]);
    }

    /// <summary>
    /// The project row refuses everything that would remove, delete or rename it
    /// — and says so.
    /// </summary>
    /// <remarks>
    /// The control on the row above. Adding a selectable row to a panel whose
    /// verbs all assume "a character, a scene or a document" is the cheap way to
    /// turn a P3 into a P1: <c>DeleteSelectedPermanently</c> on the project row
    /// would mean deleting the project folder, which is every drawing in it.
    /// Silence is not enough either — a － that does nothing reads as broken.
    /// </remarks>
    [AvaloniaFact]
    public void TheProjectRowCannotBeRemovedRenamedOrDeleted()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        var docker = vm.ProjectDocker;
        var root = docker.Rows[0];
        var before = docker.Rows.Count;
        docker.Selected = root;

        docker.RemoveSelectedCommand.Execute(null);
        Assert.Contains(docker.Rows, r => r.IsRoot);
        Assert.Equal(before, docker.Rows.Count);
        Assert.Contains("cannot be removed", docker.Status);

        docker.DeleteSelectedPermanently();
        Assert.True(Directory.Exists(_root));
        Assert.Contains("cannot be deleted", docker.Status);
        // No confirmation is offered for it, so the refusal above is the only
        // thing standing between a click and the project folder.
        Assert.False(docker.DeleteNeedsConfirmation);

        Assert.False(docker.Rename(root, "Something else"));
        Assert.Equal(Path.GetFileName(_root), docker.Rows[0].Name);
        Assert.True(Directory.Exists(_root));
    }

    /// <summary>
    /// Reveal acts on what is selected, and the toolbar button is wired to that.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B62's first half.</b> The toolbar button opened the project folder
    /// whatever was selected, while "reveal <em>this</em>" — the useful one —
    /// was buried in the right-click menu.
    /// </para>
    /// <para>
    /// <b>The binding is asserted from the XAML on purpose.</b> The view model
    /// already had <c>RevealSelectedCommand</c> before the fix and it already
    /// followed the selection, so a test that only exercised the view model
    /// would have passed on the broken build — the defect was entirely in which
    /// command the button was bound to. Reading the file is the only thing here
    /// that fails before the fix and passes after it.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void ShowInFileManagerOpensTheSelectedItem()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        WithKnight(vm);
        var docker = vm.ProjectDocker;

        var animation = Assert.Single(docker.Rows, r => r.Animation is not null);
        docker.Selected = animation;
        var selected = docker.SelectedPath;
        output.WriteLine($"selected → {selected}");
        Assert.NotNull(selected);
        Assert.EndsWith(".lightbox.json", selected);
        Assert.NotEqual(_root, selected);

        // The project row is how the button's old behaviour stays reachable.
        docker.Selected = docker.Rows[0];
        Assert.Equal(_root, docker.SelectedPath);
        // And so is selecting nothing, which is what an untouched panel is.
        docker.Selected = null;
        Assert.Equal(_root, docker.SelectedPath);

        // Windows named explicitly rather than Current: the branch that selects
        // a file inside its folder is the one worth pinning, and Linux has no
        // reveal-and-select to pin. Pure function, so the desktop is an argument.
        var command = Services.FileReveal.RevealCommand(
            Services.Desktop.Windows, selected!, isDirectory: false);
        Assert.Equal([$"/select,{selected}"], command.Args);

        // The button. RevealRootCommand is gone rather than merely unbound —
        // PathOf already answers the root for a null selection and for the
        // project row, so a second command would be a second way to do one thing
        // and a button that does not follow the tree.
        var xaml = MainWindowXaml();
        Assert.Contains("ProjectDocker.RevealSelectedCommand", xaml);
        Assert.DoesNotContain("RevealRootCommand", xaml);
    }

    /// <summary>
    /// A rebuild keeps the folder that was selected, rather than jumping to the
    /// first row that looks like it.
    /// </summary>
    /// <remarks>
    /// <b>Found by B62 and older than it.</b> <c>ProjectRow.Key</c> covered
    /// animations, scenes and characters but not folders, so every folder row
    /// keyed as null — and <c>Rebuild</c> restores the selection with
    /// <c>FirstOrDefault(r =&gt; r.Key == keep)</c>, which matches the first
    /// null-keyed row. Two folders were enough to see it; adding a project row
    /// above them would have made it look like a regression this fix caused.
    /// A rebuild happens on every save and on every directory event, so this is
    /// not a rare path.
    /// </remarks>
    [AvaloniaFact]
    public void SelectingAFolderSurvivesARebuild()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        var docker = vm.ProjectDocker;

        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Backgrounds");
        docker.Selected = null;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Props");
        var props = Assert.Single(docker.Rows, r => r is { IsFolder: true, Name: "Props" });
        docker.Selected = props;

        docker.Refresh();

        output.WriteLine("selected after rebuild: " + docker.Selected?.Name);
        Assert.Same(props, docker.Selected);
    }

    private static string MainWindowXaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(
            Path.Combine(dir!.FullName, "src", "Lightbox.App", "Views", "MainWindow.axaml"));
    }
}

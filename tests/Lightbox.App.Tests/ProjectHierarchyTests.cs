using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// <b>B85, B86.</b> The docker as a tree the artist built.
/// </summary>
/// <remarks>
/// Two reports, one shape. B85: a document created inside a folder ignored the
/// folder and went to a top-level <c>documents/</c>. B86: there was no
/// hierarchy to create it in — a flat list with no subfolders, no dragging and
/// nothing to collapse.
/// </remarks>
[Collection("BrushState")]
public sealed class ProjectHierarchyTests(ITestOutputHelper output) : BrushStateIsolated, IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-tree-{Guid.NewGuid():N}.lbproj");

    private readonly List<MainViewModel> _built = [];

    public new void Dispose()
    {
        foreach (var vm in _built) vm.ProjectDocker.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        base.Dispose();
    }

    private MainViewModel Open()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        _built.Add(vm);
        vm.NewProject(_root, "Production");
        return vm;
    }

    private static ProjectRow RowFor(ProjectViewModel docker, ProjectFolder folder) =>
        docker.Rows.First(r => r.IsFolder && ReferenceEquals(r.Folder, folder));

    // ---- absence ---------------------------------------------------------------

    /// <summary>A project with no folders looks exactly as it did.</summary>
    /// <remarks>
    /// The control on all of this. Folders are a new row kind emitted before
    /// characters, so the guard that matters is that a project which never made
    /// one is unchanged — no empty heading, no stray indent.
    /// </remarks>
    [AvaloniaFact]
    public void AProjectWithNoFoldersShowsNoFolderRows()
    {
        var vm = Open();
        Assert.DoesNotContain(vm.ProjectDocker.Rows, r => r.IsFolder);
        Assert.All(vm.ProjectDocker.Rows, r => Assert.Equal(0d, r.Indent));
    }

    // ---- B86: subfolders, depth, collapse ---------------------------------------

    [AvaloniaFact]
    public void SubfoldersCanBeCreatedWithinFolders()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;

        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Art");
        var art = Assert.Single(ProjectFolders.All(docker.Project!.Manifest));
        // Creating selects what was made, so the next one nests without a click.
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Backgrounds");
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Rooftops");

        var manifest = docker.Project!.Manifest;
        var deepest = ProjectFolders.All(manifest).First(f => f.Name == "Rooftops");
        Assert.Equal("art/backgrounds/rooftops", ProjectFolders.PathOf(manifest, deepest));
        Assert.Equal(2, ProjectFolders.DepthOf(manifest, deepest));

        // ...and the tree renders with increasing indent.
        var indents = docker.Rows.Where(r => r.IsFolder).Select(r => r.Indent).ToList();
        output.WriteLine("indents: " + string.Join(", ", indents));
        Assert.Equal([0d, 14d, 28d], indents);
        Assert.Equal("Art", RowFor(docker, art).Name);
    }

    [AvaloniaFact]
    public void FoldersCanBeCollapsedAndExpanded()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Art");
        var art = ProjectFolders.All(docker.Project!.Manifest).First();
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Backgrounds");
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Rooftop");

        var expanded = docker.Rows.Count;
        Assert.Contains(docker.Rows, r => r.Name == "Rooftop");

        docker.ToggleCollapsed(RowFor(docker, art));

        Assert.True(docker.IsCollapsed(art));
        Assert.True(RowFor(docker, art).IsCollapsed);
        Assert.Equal("▸", RowFor(docker, art).Twisty);
        Assert.DoesNotContain(docker.Rows, r => r.Name == "Backgrounds");
        Assert.DoesNotContain(docker.Rows, r => r.Name == "Rooftop");

        docker.ToggleCollapsed(RowFor(docker, art));
        Assert.Equal(expanded, docker.Rows.Count);
    }

    /// <summary>
    /// Collapse survives a re-read, which happens on every save.
    /// </summary>
    /// <remarks>
    /// B61's lesson from the other side: the directory watch means a re-read is
    /// routine, and collapse kept on the row would spring open every time the
    /// disk moved. It lives in the view model, keyed by folder id.
    /// </remarks>
    [AvaloniaFact]
    public void CollapseSurvivesARefresh()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Art");
        var art = ProjectFolders.All(docker.Project!.Manifest).First();
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Backgrounds");

        docker.ToggleCollapsed(RowFor(docker, art));
        docker.Refresh();

        Assert.True(docker.IsCollapsed(art));
        Assert.True(RowFor(docker, art).IsCollapsed);
        Assert.DoesNotContain(docker.Rows, r => r.Name == "Backgrounds");
    }

    [AvaloniaFact]
    public void FoldersCanBeDraggedWithinProject()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Art");
        var art = ProjectFolders.All(docker.Project!.Manifest).First();
        docker.Selected = null;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Rooftops");
        var rooftops = ProjectFolders.All(docker.Project!.Manifest).First(f => f.Name == "Rooftops");

        Assert.True(docker.MoveInto(RowFor(docker, rooftops), art));
        Assert.Equal("art/rooftops", ProjectFolders.PathOf(docker.Project!.Manifest, rooftops));

        // And back out.
        Assert.True(docker.MoveInto(RowFor(docker, rooftops), null));
        Assert.Equal("rooftops", ProjectFolders.PathOf(docker.Project!.Manifest, rooftops));
    }

    /// <summary>A folder cannot be dropped on its own child.</summary>
    /// <remarks>
    /// The slip a tree view invites. Refused rather than reported: the drop
    /// simply does not happen, and the tree is unchanged — the alternative is a
    /// subtree with no path back to the root, which no surface can show.
    /// </remarks>
    [AvaloniaFact]
    public void AFolderCannotBeDroppedOnItsOwnDescendant()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Art");
        var art = ProjectFolders.All(docker.Project!.Manifest).First();
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Backgrounds");
        var backgrounds = ProjectFolders.All(docker.Project!.Manifest)
            .First(f => f.Name == "Backgrounds");

        Assert.False(docker.MoveInto(RowFor(docker, art), backgrounds));
        Assert.Equal("art", ProjectFolders.PathOf(docker.Project!.Manifest, art));
        Assert.Contains(docker.Rows, r => r.IsFolder && r.Name == "Art" && r.Indent == 0);
    }

    [AvaloniaFact]
    public void DocumentsCanBeDraggedWithinProject()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Art");
        var art = ProjectFolders.All(docker.Project!.Manifest).First();
        docker.Selected = null;
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Colour test");
        var doc = docker.Project!.Manifest.Documents.First(d => d.Name == "Colour test");
        var row = docker.Rows.First(r => r.Animation?.Id == doc.Id);

        Assert.True(docker.MoveInto(row, art));

        Assert.Equal(art.Id, doc.FolderId);
        Assert.Equal("art/colour-test.lightbox.json", doc.Path);
        // The tab that was showing it is still bound to it — the id did not move.
        Assert.Contains(vm.Tabs, t => t.Source?.Id == doc.Id);
    }

    // ---- B85: created where you are ---------------------------------------------

    /// <summary>
    /// A document made with a folder selected lands in that folder.
    /// </summary>
    /// <remarks>
    /// The reported defect, stated as the artist met it: creating inside a
    /// subfolder "ignores the location and places the document in a top-level
    /// Documents folder instead".
    /// </remarks>
    [AvaloniaFact]
    public void DocumentsCreatedInFoldersAppearInCorrectFolder()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Art");
        var art = ProjectFolders.All(docker.Project!.Manifest).First();
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Backgrounds");
        var backgrounds = ProjectFolders.All(docker.Project!.Manifest)
            .First(f => f.Name == "Backgrounds");

        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Rooftop");

        var doc = docker.Project!.Manifest.Documents.First(d => d.Name == "Rooftop");
        output.WriteLine($"path: {doc.Path}");
        Assert.Equal(backgrounds.Id, doc.FolderId);
        Assert.Equal("art/backgrounds/rooftop.lightbox.json", doc.Path);
        Assert.DoesNotContain(docker.Rows, r => r.IsLoose && r.Name == "Rooftop");
    }

    /// <summary>Selecting a document means "beside this one", not "at the top".</summary>
    [AvaloniaFact]
    public void ADocumentMadeBesideAnotherJoinsItsFolder()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Art");
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Rooftop");
        var first = docker.Project!.Manifest.Documents.First(d => d.Name == "Rooftop");

        // Creating selects what it made, so the next one is made beside it.
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Alleyway");

        var second = docker.Project!.Manifest.Documents.First(d => d.Name == "Alleyway");
        Assert.Equal(first.FolderId, second.FolderId);
        Assert.Equal("art/alleyway.lightbox.json", second.Path);
    }

    /// <summary>With nothing selected a document still goes to the project root.</summary>
    /// <remarks>
    /// The control on the fix. "Put it where I am" must not become "put it in
    /// whatever folder was touched last" — with no selection there is no folder,
    /// and the old behaviour is the right one.
    /// </remarks>
    [AvaloniaFact]
    public void WithNothingSelectedADocumentStillGoesToTheProjectRoot()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Art");
        docker.Selected = null;

        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Colour test");

        var doc = docker.Project!.Manifest.Documents.First(d => d.Name == "Colour test");
        Assert.Null(doc.FolderId);
        Assert.Equal("unassigned-documents/colour-test.lightbox.json", doc.Path);
    }

    // ---- the tree reaches disk ----------------------------------------------------

    /// <summary>What the tree says is where the file goes.</summary>
    /// <remarks>
    /// The end-to-end check, and the one the others cannot make: every
    /// assertion above reads the manifest, and a manifest that is right while
    /// the save writes elsewhere is exactly the class of bug B85 was.
    /// </remarks>
    [AvaloniaFact]
    public void FolderStructureReflectsFileSystemHierarchy()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Episode 2");
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Act 1");
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Sc 014");
        vm.SaveProject(everything: true);

        var expected = Path.Combine(_root, "episode-2", "act-1", "sc-014.lightbox.json");
        output.WriteLine(expected);
        Assert.True(File.Exists(expected), $"not written: {expected}");

        // And it comes back in the same place.
        var reopened = new MainViewModel(null);
        _built.Add(reopened);
        reopened.OpenProject(_root);
        var manifest = reopened.ProjectDocker.Project!.Manifest;
        var doc = manifest.Documents.First(d => d.Name == "Sc 014");
        var folder = ProjectFolders.ById(manifest, doc.FolderId);
        Assert.NotNull(folder);
        Assert.Equal("Act 1", folder!.Name);
        Assert.Equal("episode-2/act-1", ProjectFolders.PathOf(manifest, folder));
    }

    // ---- B104: the depth reaches the screen ---------------------------------------

    /// <summary>
    /// The row template binds the indent the row computes.
    /// </summary>
    /// <remarks>
    /// <b>B104, and the whole of it.</b> <c>ProjectRow.Indent</c> has been
    /// correct since B86 and nothing bound to it, so every assertion about depth
    /// in this file passed while the panel drew a flat list — a folder three
    /// deep sat flush left beside the project row. That is the shape of bug a
    /// view-model test cannot see, so the binding is asserted in the one place
    /// the failure lives: the XAML.
    /// <para>
    /// Paired with a depth assertion rather than left on its own, because a test
    /// that only greps the markup passes on a build where <c>Indent</c> returns
    /// zero for everything.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void TheTreeIndentReachesTheRowTemplate()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Art");
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Backgrounds");
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Rooftop");

        var art = docker.Rows.First(r => r is { IsFolder: true, Name: "Art" });
        var backgrounds = docker.Rows.First(r => r is { IsFolder: true, Name: "Backgrounds" });
        var rooftop = docker.Rows.First(r => r.Animation?.Name == "Rooftop");
        output.WriteLine($"art {art.Indent}, backgrounds {backgrounds.Indent}, doc {rooftop.Indent}");

        // Each level in, and the document one further in than the folder holding it.
        Assert.Equal(0d, art.Indent);
        Assert.Equal(14d, backgrounds.Indent);
        Assert.Equal(28d, rooftop.Indent);

        var xaml = MainWindowXaml();
        Assert.Contains("Width=\"{Binding Indent}\"", xaml);
    }

    /// <summary>Where the project docker's row template is.</summary>
    private static string MainWindowXaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(
            Path.Combine(dir!.FullName, "src", "Lightbox.App", "Views", "MainWindow.axaml"));
    }

    // ---- B106: a move is a move, not a copy ---------------------------------------

    /// <summary>
    /// Dragging a saved document into a folder leaves one file, not two.
    /// </summary>
    /// <remarks>
    /// <b>B106 as it was reported:</b> "both the root document in the folder
    /// Documents and a copy of that file in the moved folder exist
    /// simultaneously". The manifest was repathed and the file was not, so the
    /// next save wrote the document at its new path and left the old one
    /// sitting there — two files, one drawing, and nothing saying which the app
    /// would open.
    /// <para>
    /// Saved twice on purpose. The bug needs a file to exist <em>before</em> the
    /// move and another save <em>after</em> it; a document that was never
    /// written has nothing to leave behind, which is why this went unnoticed —
    /// every test in this file moved documents that had never reached disk.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void MovingADocumentIntoAFolderMovesItsFileRatherThanCopyingIt()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");
        var knight = ProjectFolders.All(docker.Project!.Manifest).First();
        docker.Selected = null;
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Walk");
        vm.SaveProject(everything: true);

        var before = Path.Combine(_root, "unassigned-documents", "walk.lightbox.json");
        Assert.True(File.Exists(before), $"not written: {before}");

        var doc = docker.Project!.Manifest.Documents.First(d => d.Name == "Walk");
        Assert.True(docker.MoveInto(docker.Rows.First(r => r.Animation?.Id == doc.Id), knight));
        vm.SaveProject(everything: true);

        var after = Path.Combine(_root, "knight", "walk.lightbox.json");
        foreach (var file in Directory.EnumerateFiles(_root, "*.lightbox.json", SearchOption.AllDirectories))
        {
            output.WriteLine(Path.GetRelativePath(_root, file));
        }
        Assert.True(File.Exists(after), $"not moved to: {after}");
        Assert.False(File.Exists(before), "the original is still there — the move copied it");
        Assert.Equal("knight/walk.lightbox.json", doc.Path);
    }

    /// <summary>
    /// And out again, to the project root.
    /// </summary>
    /// <remarks>
    /// The direction the report did not mention and the one that would fail on
    /// half a fix: the destination is null here, so the path comes from
    /// <c>ProjectIo.DocumentsDir</c> rather than from a folder chain.
    /// </remarks>
    [AvaloniaFact]
    public void MovingADocumentBackToTheRootMovesItsFileToo()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Walk");
        vm.SaveProject(everything: true);

        var inFolder = Path.Combine(_root, "knight", "walk.lightbox.json");
        Assert.True(File.Exists(inFolder), $"not written: {inFolder}");

        var doc = docker.Project!.Manifest.Documents.First(d => d.Name == "Walk");
        Assert.True(docker.MoveInto(docker.Rows.First(r => r.Animation?.Id == doc.Id), null));
        vm.SaveProject(everything: true);

        Assert.True(File.Exists(Path.Combine(_root, "unassigned-documents", "walk.lightbox.json")));
        Assert.False(File.Exists(inFolder), "the original is still there — the move copied it");
    }

    /// <summary>
    /// A move that cannot reach disk changes neither the disk nor the panel.
    /// </summary>
    /// <remarks>
    /// The guard on the ordering. The disk is moved first and the manifest only
    /// if that worked, because a manifest pointing at a path the file never
    /// reached is worse than either half: the panel would show the document in
    /// its new home and the drawing would not be there. A file already sitting
    /// at the destination is the reachable way to provoke it —
    /// <c>MoveInProject</c> refuses to write over somebody's work.
    /// </remarks>
    [AvaloniaFact]
    public void AMoveThatCannotReachDiskLeavesTheTreeAlone()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");
        var knight = ProjectFolders.All(docker.Project!.Manifest).First();
        docker.Selected = null;
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Walk");
        vm.SaveProject(everything: true);

        // Something the manifest does not know about, in the way.
        var blocked = Path.Combine(_root, "knight", "walk.lightbox.json");
        Directory.CreateDirectory(Path.GetDirectoryName(blocked)!);
        File.WriteAllText(blocked, "{}");

        var doc = docker.Project!.Manifest.Documents.First(d => d.Name == "Walk");
        var row = docker.Rows.First(r => r.Animation?.Id == doc.Id);

        Assert.False(docker.MoveInto(row, knight));
        output.WriteLine(docker.Status);

        Assert.Null(doc.FolderId);
        Assert.Equal("unassigned-documents/walk.lightbox.json", doc.Path);
        Assert.True(File.Exists(Path.Combine(_root, "unassigned-documents", "walk.lightbox.json")));
        Assert.Equal("{}", File.ReadAllText(blocked));
        // And it says so, because a drag that silently does nothing reads as a
        // drag that missed.
        Assert.Contains("Could not move", docker.Status);
    }

    // ---- B107: the name comes from where it is going ------------------------------

    /// <summary>
    /// A document made inside "Knight" is offered "Knight - " to finish.
    /// </summary>
    /// <remarks>
    /// <b>B107.</b> The prompt offered <c>Document 3</c> — a number nobody keeps,
    /// so every drawing was renamed once by hand. The folder is what the drawing
    /// is of, so it is the start of the name.
    /// </remarks>
    [AvaloniaFact]
    public void ADocumentMadeInAFolderIsNamedAfterIt()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");

        var suggested = docker.SuggestedNameFor(ProjectViewModel.NewLooseDocument);
        output.WriteLine($"suggested: “{suggested}”");
        Assert.Equal("Knight - ", suggested);

        // Overwritable, and that is the point of a suggestion: what the artist
        // types is what is made, prefix or no prefix.
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Knight - Walk");
        // Named rather than first: NewProject adopts the drawing that was open,
        // so the project already holds one document before this one.
        var doc = docker.Project!.Manifest.Documents.First(d => d.Name == "Knight - Walk");
        Assert.Equal("Knight - Walk", doc.Name);
        Assert.Equal("knight/knight-walk.lightbox.json", doc.Path);
    }

    /// <summary>
    /// Accepting the offered stem unchanged makes "Knight", not "Knight -".
    /// </summary>
    /// <remarks>
    /// The suggestion has to equal what Enter produces, which is the rule
    /// <c>SuggestedNameFor</c> has carried since B65 — and a stem is the first
    /// suggestion that is not already a finished name. Trimming the dangling
    /// separator is what keeps the two in step.
    /// </remarks>
    [AvaloniaFact]
    public void AcceptingTheOfferedStemDropsTheDanglingSeparator()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");

        docker.AddItemNamed(
            ProjectViewModel.NewLooseDocument,
            docker.SuggestedNameFor(ProjectViewModel.NewLooseDocument));

        var doc = docker.Project!.Manifest.Documents.First(d => d.Name == "Knight");
        output.WriteLine($"name: “{doc.Name}”, path: {doc.Path}");
        Assert.Equal("Knight", doc.Name);
        Assert.Equal("knight/knight.lightbox.json", doc.Path);
    }

    /// <summary>
    /// With no folder to name it after, the numbered suggestion is still there.
    /// </summary>
    /// <remarks>
    /// The control. "Derive it from the folder" must not become "derive it from
    /// whatever was touched last" — with nothing selected there is no scope, and
    /// the old suggestion is the right one.
    /// </remarks>
    [AvaloniaFact]
    public void WithNoFolderSelectedTheNumberedSuggestionStands()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");
        docker.Selected = null;

        // Two, not one: the project adopted the drawing that was open when it
        // was created, and the number counts what is already there.
        Assert.Equal("Document 2", docker.SuggestedNameFor(ProjectViewModel.NewLooseDocument));
    }

    /// <summary>
    /// A folder is offered "Folder", not "Animation 0".
    /// </summary>
    /// <remarks>
    /// Found while fixing B107 and part of the same defect: <c>NewFolderItem</c>
    /// had no branch in <c>SuggestedNameFor</c> and fell through to the animation
    /// line, so ＋ New ▸ Folder prefilled the box with <c>Animation 0</c> while
    /// Enter on an empty box made <c>Folder</c> — the exact drift that method's
    /// own remarks warn about, in the one place nothing asserted it.
    /// <para>
    /// Subfolders are deliberately <em>not</em> prefixed: they are structural,
    /// and prefixing would compound into "Knight - Locomotion - Knight - Walk"
    /// one level down.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void AFolderIsNotOfferedAnAnimationName()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");

        var suggested = docker.SuggestedNameFor(ProjectViewModel.NewFolderItem);
        output.WriteLine($"suggested: “{suggested}”");
        Assert.Equal("Folder", suggested);
        // And it is what Enter on an untouched box produces.
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, null);
        Assert.Contains(ProjectFolders.All(docker.Project!.Manifest), f => f.Name == "Folder");
    }
}

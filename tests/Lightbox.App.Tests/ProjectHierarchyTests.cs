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
        Assert.Equal("documents/colour-test.lightbox.json", doc.Path);
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
}

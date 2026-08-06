using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// <b>B87.</b> Two operations, and the difference between them is the point.
/// </summary>
/// <remarks>
/// The context menu only offered "remove from project", which keeps the file —
/// so deleting a drawing meant leaving Lightbox for a file manager. The other
/// half is a separate entry rather than a modifier on the first, because two
/// operations with different consequences should not be one gesture told apart
/// by a held key.
/// </remarks>
[Collection("BrushState")]
public sealed class ProjectDeleteTests(ITestOutputHelper output) : BrushStateIsolated, IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-del-{Guid.NewGuid():N}.lbproj");

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

    private static ProjectRow FolderRow(ProjectViewModel docker, string name) =>
        docker.Rows.First(r => r.IsFolder && r.Name == name);

    private static ProjectRow DocRow(ProjectViewModel docker, string name) =>
        docker.Rows.First(r => r.Animation is not null && r.Name == name);

    // ---- deleting a document ----------------------------------------------------

    [AvaloniaFact]
    public void DeletedFilesCanBePermanentlyRemovedFromDisk()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.Selected = null;
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Colour test");
        vm.SaveProject(everything: true);

        var onDisk = Path.Combine(_root, "unassigned-documents", "colour-test.lightbox.json");
        Assert.True(File.Exists(onDisk), "the test's own premise: it was written");

        docker.Selected = DocRow(docker, "Colour test");
        docker.DeleteSelectedPermanently();

        Assert.False(File.Exists(onDisk));
        Assert.DoesNotContain(docker.Project!.Manifest.Documents, d => d.Name == "Colour test");
        Assert.DoesNotContain(docker.Rows, r => r.Name == "Colour test");
    }

    /// <summary>
    /// Remove from project is the other operation, and it keeps the file.
    /// </summary>
    /// <remarks>
    /// The control that gives the test above its meaning. Both remove the row;
    /// only one touches disk, and a fix that made them the same thing would
    /// pass every assertion about rows.
    /// </remarks>
    [AvaloniaFact]
    public void RemoveFromProjectLeavesTheFileWhereItIs()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.Selected = null;
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Colour test");
        vm.SaveProject(everything: true);
        var onDisk = Path.Combine(_root, "unassigned-documents", "colour-test.lightbox.json");

        docker.Selected = DocRow(docker, "Colour test");
        docker.RemoveSelectedCommand.Execute(null);

        Assert.True(File.Exists(onDisk), "remove from project must not delete the drawing");
        Assert.DoesNotContain(docker.Rows, r => r.Name == "Colour test");
        Assert.Contains("still on disk", docker.Status);
    }

    /// <summary>
    /// A removed document stays removed across a save and a reopen.
    /// </summary>
    /// <remarks>
    /// The reporter's "should be marked so they are not unwantedly reloaded".
    /// It holds today because the manifest <em>is</em> the index and nothing
    /// scans the folder — so this test is not describing new machinery, it is
    /// pinning a property that a later directory scan would silently break.
    /// The folder tree makes such a scan tempting, which is why it is written
    /// down now rather than after somebody adds one.
    /// </remarks>
    [AvaloniaFact]
    public void MissingFilesAreTrackedAndNotReloadedOnNextOpen()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.Selected = null;
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Colour test");
        vm.SaveProject(everything: true);

        docker.Selected = DocRow(docker, "Colour test");
        docker.RemoveSelectedCommand.Execute(null);
        vm.SaveProject(everything: true);

        var reopened = new MainViewModel(null);
        _built.Add(reopened);
        reopened.OpenProject(_root);

        // The file is still there, and the project does not claim it back.
        Assert.True(File.Exists(Path.Combine(_root, "unassigned-documents", "colour-test.lightbox.json")));
        Assert.DoesNotContain(
            reopened.ProjectDocker.Project!.Manifest.Documents, d => d.Name == "Colour test");
        Assert.DoesNotContain(reopened.ProjectDocker.Rows, r => r.Name == "Colour test");
    }

    // ---- deleting a folder -------------------------------------------------------

    /// <summary>An empty folder goes without being asked about.</summary>
    [AvaloniaFact]
    public void EmptyFoldersAreDeletedWithoutPrompt()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Scrap");
        vm.SaveProject(everything: true);

        docker.Selected = FolderRow(docker, "Scrap");
        Assert.False(docker.DeleteNeedsConfirmation);
        output.WriteLine(docker.DeleteWarning);

        docker.DeleteSelectedPermanently();
        Assert.Empty(ProjectFolders.All(docker.Project!.Manifest));
        Assert.False(Directory.Exists(Path.Combine(_root, "scrap")));
    }

    /// <summary>A folder with anything in it asks, and says how much.</summary>
    [AvaloniaFact]
    public void DeletedFoldersWithFilesPromptForConfirmation()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Art");
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Backgrounds");
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Rooftop");
        vm.SaveProject(everything: true);

        docker.Selected = FolderRow(docker, "Art");
        Assert.True(docker.DeleteNeedsConfirmation);

        var warning = docker.DeleteWarning;
        output.WriteLine(warning);
        // It names the size of what is about to go, because "are you sure?"
        // tells somebody nothing they can weigh.
        Assert.Contains("Art", warning);
        Assert.Contains("1 folder", warning);
        Assert.Contains("1 document", warning);

        // A child folder with nothing in it still does not ask.
        docker.Selected = FolderRow(docker, "Backgrounds");
        Assert.True(docker.DeleteNeedsConfirmation);   // it holds Rooftop
        Assert.Contains("1 document", docker.DeleteWarning);
    }

    [AvaloniaFact]
    public void DeletingAFolderTakesItsSubtreeAndFilesWithIt()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Art");
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Backgrounds");
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Rooftop");
        vm.SaveProject(everything: true);
        Assert.True(File.Exists(Path.Combine(_root, "art", "backgrounds", "rooftop.lightbox.json")));

        docker.Selected = FolderRow(docker, "Art");
        docker.DeleteSelectedPermanently();

        Assert.False(Directory.Exists(Path.Combine(_root, "art")));
        Assert.Empty(ProjectFolders.All(docker.Project!.Manifest));
        // Named rather than counted: NewProject adopts the drawing that was
        // already open, so the project is never empty and a count assertion
        // would be about that rather than about the delete.
        Assert.DoesNotContain(docker.Project!.Manifest.Documents, d => d.Name == "Rooftop");
        Assert.DoesNotContain(docker.Rows, r => r.Name is "Art" or "Backgrounds" or "Rooftop");
    }

    /// <summary>
    /// Removing a folder is not deleting it, and the work inside survives.
    /// </summary>
    /// <remarks>
    /// The pair to the test above. A drawing that vanished from every row
    /// because its folder was removed is a drawing that is gone as far as
    /// anyone can tell, so it comes back to the project root.
    /// </remarks>
    [AvaloniaFact]
    public void RemovingAFolderKeepsItsDocumentsInTheProject()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Art");
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Rooftop");
        vm.SaveProject(everything: true);

        docker.Selected = FolderRow(docker, "Art");
        docker.RemoveSelectedCommand.Execute(null);

        Assert.Empty(ProjectFolders.All(docker.Project!.Manifest));
        var kept = docker.Project!.Manifest.Documents.Single(d => d.Name == "Rooftop");
        Assert.Null(kept.FolderId);
        Assert.Equal("unassigned-documents/rooftop.lightbox.json", kept.Path);
        Assert.Contains(docker.Rows, r => r.IsLoose && r.Name == "Rooftop");
        // And the old file is untouched, because remove never deletes.
        Assert.True(File.Exists(Path.Combine(_root, "art", "rooftop.lightbox.json")));
    }

    // ---- the guard ------------------------------------------------------------------

    /// <summary>
    /// A path that climbs out of the project deletes nothing.
    /// </summary>
    /// <remarks>
    /// Not hypothetical. Every path here comes from the manifest, which is
    /// plain JSON a person or an agent can edit, so <c>../../Documents</c> is
    /// one careless entry away — and the operation on the other side of it
    /// deletes a directory tree.
    /// </remarks>
    [AvaloniaFact]
    public void ADeleteCannotEscapeTheProjectFolder()
    {
        var vm = Open();
        var project = vm.ProjectDocker.Project!;
        var outside = Path.Combine(Path.GetDirectoryName(_root)!, $"bystander-{Guid.NewGuid():N}.txt");
        File.WriteAllText(outside, "not yours");
        try
        {
            Assert.False(ProjectIo.DeleteInProject(project, $"../{Path.GetFileName(outside)}"));
            Assert.False(ProjectIo.DeleteInProject(project, "../.."));
            Assert.False(ProjectIo.DeleteInProject(project, ""));
            Assert.False(ProjectIo.DeleteInProject(project, "."));

            Assert.True(File.Exists(outside));
            Assert.True(Directory.Exists(_root));
        }
        finally
        {
            if (File.Exists(outside)) File.Delete(outside);
        }
    }

    /// <summary>A sibling whose name merely starts the same is not inside.</summary>
    /// <remarks>
    /// The prefix trap: <c>Production.lbproj-old</c> starts with
    /// <c>Production.lbproj</c>, so a containment check written as a plain
    /// <c>StartsWith</c> would happily delete it.
    /// </remarks>
    [AvaloniaFact]
    public void ASiblingWithAMatchingPrefixIsNotInsideTheProject()
    {
        var vm = Open();
        var project = vm.ProjectDocker.Project!;
        var sibling = _root + "-old";
        Directory.CreateDirectory(sibling);
        try
        {
            Assert.False(ProjectIo.DeleteInProject(project, $"../{Path.GetFileName(sibling)}"));
            Assert.True(Directory.Exists(sibling));
        }
        finally
        {
            if (Directory.Exists(sibling)) Directory.Delete(sibling, recursive: true);
        }
    }
}

using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// <b>B76.</b> What a project row says about whether its file is actually there.
/// </summary>
/// <remarks>
/// <para>
/// The entry is titled "a new document is written to disk the moment it is
/// created", and a probe found that half already closed — B65 and B85 did it on
/// the way past, and `NothingIsWrittenToDiskIfTheNameIsCancelled` is the same
/// property arrived at from the other side. What was left is the opposite of the
/// title: nothing on the row said the document was <em>unwritten</em>, so it
/// looked exactly like a saved one.
/// </para>
/// <para>
/// <b>The two states must not be conflated, and that is the whole entry.</b>
/// <c>Missing</c> means *this was on disk and is gone*; pending means *this has
/// not been saved yet*. One is alarming and one is ordinary, and a row that said
/// the same thing for both would either cry wolf on every new document or bury a
/// deleted file among them.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class UnsavedDocumentTests(ITestOutputHelper output) : BrushStateIsolated, IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-unsaved-{Guid.NewGuid():N}.lbproj");

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

    private ProjectRow Row(ProjectViewModel docker, string name) =>
        Assert.Single(docker.Rows, r => r.Name == name);

    /// <summary>A document created in the docker is not on disk until it is saved.</summary>
    /// <remarks>
    /// The half of B76 that was already true when the entry was re-read. Kept as
    /// a guard rather than dropped: it is the property the rest of this file
    /// depends on, and nothing was pinning it from this direction — the existing
    /// coverage asserts what happens when a name prompt is *cancelled*, which is
    /// a different claim that happens to imply this one.
    /// </remarks>
    [AvaloniaFact]
    public void ANewDocumentIsNotOnDiskUntilItIsSaved()
    {
        var vm = Vm();
        vm.NewProject(_root, "Production");
        var docker = vm.ProjectDocker;

        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Colour test");
        var made = Assert.Single(docker.Project!.Manifest.Documents, d => d.Name == "Colour test");
        var path = Path.Combine(_root, made.Path.Replace('/', Path.DirectorySeparatorChar));
        output.WriteLine($"manifest path {made.Path}, on disk {File.Exists(path)}");

        // In the project, not yet on disk. The manifest is the record; the file
        // is what a save produces.
        Assert.False(File.Exists(path));
        Assert.Contains(docker.Rows, r => r.Name == "Colour test");

        vm.SaveProject();
        Assert.True(File.Exists(path));
    }

    /// <summary>The row says it is not saved yet, and stops saying it once it is.</summary>
    [AvaloniaFact]
    public void AnUnsavedDocumentIsShownAsPendingInTheDocker()
    {
        var vm = Vm();
        vm.NewProject(_root, "Production");
        var docker = vm.ProjectDocker;

        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Backgrounds");
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Colour test");

        var document = Row(docker, "Colour test");
        var folder = Row(docker, "Backgrounds");
        output.WriteLine($"document pending={document.Pending} missing={document.Missing}");
        output.WriteLine($"folder   pending={folder.Pending} missing={folder.Missing}");

        // Pending, and emphatically **not** missing — that is the distinction the
        // whole entry turns on.
        Assert.True(document.Pending);
        Assert.False(document.Missing);
        Assert.False(document.IsOnDisk);
        Assert.Equal("not saved yet", document.PendingHint);
        Assert.Equal("", document.MissingHint);

        // A folder made in the same breath reads the same way. It is in the
        // manifest and materialises on save (B83), so "not on disk" would be a
        // lie that reads as a fault.
        Assert.True(folder.Pending);
        Assert.False(folder.Missing);

        // Nothing that is genuinely there claims either.
        Assert.DoesNotContain(docker.Rows, r => r.IsRoot && (r.Pending || r.Missing));

        vm.SaveProject();

        Assert.False(Row(docker, "Colour test").Pending);
        Assert.True(Row(docker, "Colour test").IsOnDisk);
        Assert.False(Row(docker, "Backgrounds").Pending);
        Assert.Equal("", Row(docker, "Colour test").PendingHint);
    }

    /// <summary>
    /// A file deleted behind the app's back is <b>missing</b>, never pending.
    /// </summary>
    /// <remarks>
    /// The control on the test above, and the one that would catch a fix that
    /// simply stopped reporting anything. Written as the same row in two states
    /// — saved, then deleted from under it — because the interesting claim is
    /// that the <em>same</em> document reads differently depending on why its
    /// file is absent.
    /// </remarks>
    [AvaloniaFact]
    public void ADeletedFileIsMissingRatherThanPending()
    {
        var vm = Vm();
        vm.NewProject(_root, "Production");
        var docker = vm.ProjectDocker;

        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Colour test");
        vm.SaveProject();
        Assert.True(Row(docker, "Colour test").IsOnDisk);

        var made = Assert.Single(docker.Project!.Manifest.Documents, d => d.Name == "Colour test");
        File.Delete(Path.Combine(_root, made.Path.Replace('/', Path.DirectorySeparatorChar)));
        docker.Refresh();

        var row = Row(docker, "Colour test");
        output.WriteLine($"after deleting the file: pending={row.Pending} missing={row.Missing}");
        Assert.True(row.Missing);
        Assert.False(row.Pending);
        Assert.False(row.IsOnDisk);
        Assert.Equal("not on disk", row.MissingHint);
    }

    /// <summary>Discarding an unsaved document takes its row away.</summary>
    /// <remarks>
    /// The reporter's last expectation, and it needs no new machinery: removing
    /// from the project already drops the row, and for a document that was never
    /// written there is no file left behind to argue about. Guarded because that
    /// second half is only true while creation stays deferred — if a document
    /// ever went back to being written on creation, this would start leaving
    /// orphans on disk and nothing else would notice.
    /// </remarks>
    [AvaloniaFact]
    public void DiscardingAnUnsavedDocumentRemovesItFromTheDocker()
    {
        var vm = Vm();
        vm.NewProject(_root, "Production");
        var docker = vm.ProjectDocker;

        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Colour test");
        var made = Assert.Single(docker.Project!.Manifest.Documents, d => d.Name == "Colour test");
        var path = Path.Combine(_root, made.Path.Replace('/', Path.DirectorySeparatorChar));

        docker.Selected = Row(docker, "Colour test");
        docker.RemoveSelectedCommand.Execute(null);

        Assert.DoesNotContain(docker.Rows, r => r.Name == "Colour test");
        Assert.DoesNotContain(docker.Project!.Manifest.Documents, d => d.Name == "Colour test");
        // And nothing is orphaned, because nothing was ever written.
        Assert.False(File.Exists(path));

        vm.SaveProject();
        Assert.False(File.Exists(path));
    }
}

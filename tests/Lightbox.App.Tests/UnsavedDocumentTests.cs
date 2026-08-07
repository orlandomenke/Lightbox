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
        foreach (var directory in _outside.Where(Directory.Exists))
        {
            Directory.Delete(directory, recursive: true);
        }
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

        docker.AddItemNamed(ProjectViewModel.NewDocumentItem, "Colour test");
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
        docker.AddItemNamed(ProjectViewModel.NewDocumentItem, "Colour test");

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

        docker.AddItemNamed(ProjectViewModel.NewDocumentItem, "Colour test");
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

        docker.AddItemNamed(ProjectViewModel.NewDocumentItem, "Colour test");
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

    // ---- B99: File ▸ New inside a project ------------------------------------------

    /// <summary>A blank document for a New that only differs by name.</summary>
    private static NewDocumentSettings NewOf(string name) =>
        new(name, 400, 300, 12, 72, "#ffffff", false);

    /// <summary>
    /// File ▸ New with a project open gives the document a row, marked unsaved.
    /// </summary>
    /// <remarks>
    /// <b>B99's remaining half.</b> The document used to be in limbo: a tab, and
    /// nothing else — no manifest entry, no row, <c>Source</c> null. That last one
    /// is why a project save skipped it, because <c>SaveProject</c> writes only
    /// tabs that have a <c>Source</c>, so the one document with nothing on disk
    /// was also the one nothing would write.
    /// <para>
    /// Filed where the artist is, which is the same rule ＋ New ▸ Document
    /// follows. File ▸ New still means "a new document" rather than "an animation
    /// under the selected character" — what changed is that the project knows
    /// about it.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void ANewDocumentAppearsInTheProjectDocker()
    {
        var vm = Vm();
        vm.NewProject(_root, "Production");
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");
        var knight = ProjectFolders.All(docker.Project!.Manifest).First();

        vm.NewDocument(NewOf("Rooftop"));

        var row = Row(docker, "Rooftop");
        output.WriteLine($"pending={row.Pending}, missing={row.Missing}, onDisk={row.IsOnDisk}");
        // Unsaved rather than missing: it has never been written, which is
        // ordinary, and saying "not on disk" would cry wolf (B76).
        Assert.True(row.Pending);
        Assert.False(row.Missing);
        Assert.Equal("not saved yet", row.PendingHint);

        // Filed where the artist was, and bound to the tab that is showing it.
        var made = Assert.Single(docker.Project!.Manifest.Documents, d => d.Name == "Rooftop");
        Assert.Equal(knight.Id, made.FolderId);
        Assert.Equal("knight/rooftop.lightbox.json", made.Path);
        Assert.Equal(made.Id, vm.ActiveTab!.Source?.Id);
    }

    /// <summary>
    /// And a project save writes it, which is the half nothing could reach before.
    /// </summary>
    /// <remarks>
    /// The end-to-end claim, and the one the row assertions cannot make: a row
    /// that is right while the save skips the file is exactly the shape B99 was.
    /// </remarks>
    [AvaloniaFact]
    public void AProjectSaveWritesADocumentMadeWithFileNew()
    {
        var vm = Vm();
        vm.NewProject(_root, "Production");
        var docker = vm.ProjectDocker;

        vm.NewDocument(NewOf("Rooftop"));
        vm.SaveProject();

        var made = Assert.Single(docker.Project!.Manifest.Documents, d => d.Name == "Rooftop");
        var path = Path.Combine(_root, made.Path.Replace('/', Path.DirectorySeparatorChar));
        output.WriteLine(path);
        Assert.True(File.Exists(path), $"a project save did not write it: {path}");
        // And the row stops saying it is unsaved, because it is not.
        Assert.False(Row(docker, "Rooftop").Pending);
    }

    /// <summary>
    /// Closing a document that was never saved takes its row with it, at once.
    /// </summary>
    /// <remarks>
    /// <b>The owner's rule for B99, 2026-08-06.</b> The alternative is a row
    /// pointing at a file that does not exist and never will — which the next
    /// refresh would report as <em>not on disk</em>, the badge that means
    /// something went wrong, raised by nothing going wrong.
    /// </remarks>
    [AvaloniaFact]
    public void ClosingANeverSavedDocumentTakesItsRowWithIt()
    {
        var vm = Vm();
        vm.NewProject(_root, "Production");
        var docker = vm.ProjectDocker;

        vm.NewDocument(NewOf("Rooftop"));
        var tab = Assert.Single(vm.Tabs, t => t.Title == "Rooftop");
        Assert.Contains(docker.Rows, r => r.Name == "Rooftop");

        vm.CloseTab(tab);

        output.WriteLine(docker.Status);
        Assert.DoesNotContain(docker.Rows, r => r.Name == "Rooftop");
        Assert.DoesNotContain(docker.Project!.Manifest.Documents, d => d.Name == "Rooftop");
        // And a save afterwards writes nothing for it.
        vm.SaveProject();
        Assert.False(Directory.EnumerateFiles(_root, "rooftop.lightbox.json", SearchOption.AllDirectories).Any());
    }

    /// <summary>
    /// Closing a document that <em>was</em> saved keeps its row.
    /// </summary>
    /// <remarks>
    /// The control, and the assertion that matters most: "never written" has to
    /// be narrower than "this tab closed", or closing a tab would quietly take a
    /// finished drawing out of the project while leaving the file on disk — a row
    /// gone and the work stranded, which is worse than the bug being fixed.
    /// </remarks>
    [AvaloniaFact]
    public void ClosingASavedDocumentKeepsItsRow()
    {
        var vm = Vm();
        vm.NewProject(_root, "Production");
        var docker = vm.ProjectDocker;

        vm.NewDocument(NewOf("Rooftop"));
        vm.SaveProject();
        var tab = Assert.Single(vm.Tabs, t => t.Title == "Rooftop");

        vm.CloseTab(tab);

        Assert.Contains(docker.Rows, r => r.Name == "Rooftop");
        var kept = Assert.Single(docker.Project!.Manifest.Documents, d => d.Name == "Rooftop");
        Assert.True(File.Exists(Path.Combine(_root, kept.Path.Replace('/', Path.DirectorySeparatorChar))));
        // Nor is it flagged, because its file is where the project says.
        Assert.False(Row(docker, "Rooftop").Missing);
    }

    /// <summary>
    /// A document whose file was deleted from outside keeps its row on close.
    /// </summary>
    /// <remarks>
    /// The other edge of the same line, and B61's rule: a vanished file is
    /// reported, never removed on the artist's behalf. "Never written" is two
    /// conditions — no save has covered it <em>and</em> there is no file — so a
    /// saved document whose file has gone stays in the project, flagged, and
    /// taking it out remains a decision somebody makes.
    /// </remarks>
    [AvaloniaFact]
    public void ClosingADocumentWhoseFileVanishedKeepsItsRow()
    {
        var vm = Vm();
        vm.NewProject(_root, "Production");
        var docker = vm.ProjectDocker;

        vm.NewDocument(NewOf("Rooftop"));
        vm.SaveProject();
        var made = Assert.Single(docker.Project!.Manifest.Documents, d => d.Name == "Rooftop");
        File.Delete(Path.Combine(_root, made.Path.Replace('/', Path.DirectorySeparatorChar)));

        vm.CloseTab(Assert.Single(vm.Tabs, t => t.Title == "Rooftop"));

        Assert.Contains(docker.Rows, r => r.Name == "Rooftop");
        docker.Refresh();
        Assert.True(Row(docker, "Rooftop").Missing);
    }

    /// <summary>
    /// With no project open, File ▸ New is what it always was.
    /// </summary>
    /// <remarks>
    /// The control on the whole entry. The application is document-first — it
    /// opens on an untitled document with no project and no project UI — and
    /// making a drawing must never be what brings a project into existence.
    /// </remarks>
    [AvaloniaFact]
    public void ANewDocumentWithNoProjectOpenChangesNothing()
    {
        var vm = Vm();
        Assert.False(vm.HasProject);

        vm.NewDocument(NewOf("Rooftop"));

        Assert.False(vm.HasProject);
        Assert.Null(vm.ActiveTab!.Source);
        Assert.Empty(vm.ProjectDocker.Rows);
        // And closing it does not go looking for a project either.
        vm.CloseTab(vm.ActiveTab!);
        Assert.False(vm.HasProject);
    }

    // ---- B99: saved somewhere else ---------------------------------------------------

    /// <summary>Somewhere outside the project, on the same disk.</summary>
    private string Elsewhere(string name)
    {
        var directory = Path.Combine(Path.GetDirectoryName(_root)!, $"elsewhere-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        _outside.Add(directory);
        return Path.Combine(directory, name);
    }

    private readonly List<string> _outside = [];

    /// <summary>
    /// Saving a project document outside the project takes it out of the project.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The counterpart B99 obliged and did not have, and the reporter asked
    /// for it before it existed:</b> "if I create a new document and place it
    /// outside of the project, it does not show in the project". It did. The row
    /// stayed, pointing at a file that had never been written there and still
    /// saying <em>not saved yet</em>, and a project save then wrote a <b>second
    /// copy</b> inside the project — B106's one-drawing-two-files from a
    /// different direction.
    /// </para>
    /// <para>
    /// Adoption and release are a pair. Anything that takes a document into the
    /// project on creation owes it a way out when the artist chooses a home
    /// elsewhere.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void SavingADocumentOutsideTheProjectTakesItOutOfTheProject()
    {
        var vm = Vm();
        vm.NewProject(_root, "Production");
        var docker = vm.ProjectDocker;

        vm.NewDocument(NewOf("Rooftop"));
        var made = Assert.Single(docker.Project!.Manifest.Documents, d => d.Name == "Rooftop");
        var wouldHaveBeen = Path.Combine(_root, made.Path.Replace('/', Path.DirectorySeparatorChar));

        // Save As, to a folder beside the project rather than in it.
        var outside = Elsewhere("rooftop.lightbox.json");
        Lightbox.Core.Serialization.DocJson.Save(vm.ActiveTab!.Doc, outside);
        vm.NotifySaved(outside);

        output.WriteLine(docker.Status);
        Assert.DoesNotContain(docker.Rows, r => r.Name == "Rooftop");
        Assert.DoesNotContain(docker.Project!.Manifest.Documents, d => d.Name == "Rooftop");
        Assert.Null(vm.ActiveTab!.Source);

        // And no second copy appears inside the project, now or on the next save.
        vm.SaveProject();
        Assert.False(File.Exists(wouldHaveBeen), $"a duplicate was written: {wouldHaveBeen}");
        Assert.True(File.Exists(outside), "the artist's own file must still be there");
    }

    /// <summary>
    /// Saved <em>into</em> the project, the record follows the file.
    /// </summary>
    /// <remarks>
    /// The other direction, and it is not the same fix: inside the project the
    /// document stays, and what has to move is the manifest. Without this the
    /// reference kept pointing at the path it was created with, so a project save
    /// wrote a second copy inside the project instead of outside it — the same
    /// duplicate, one directory over.
    /// <para>
    /// The name follows too, because a panel that says <i>Rooftop</i> while the
    /// file says <c>rooftop-v2</c> is B64's complaint exactly.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void SavingADocumentIntoTheProjectRepathsItsRow()
    {
        var vm = Vm();
        vm.NewProject(_root, "Production");
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");
        var knight = ProjectFolders.All(docker.Project!.Manifest).First();
        docker.Selected = null;

        vm.NewDocument(NewOf("Rooftop"));
        var made = Assert.Single(docker.Project!.Manifest.Documents, d => d.Name == "Rooftop");
        var createdAt = Path.Combine(_root, made.Path.Replace('/', Path.DirectorySeparatorChar));

        // Save As into the knight folder, under a different name.
        var into = Path.Combine(_root, "knight", "rooftop-v2.lightbox.json");
        Directory.CreateDirectory(Path.GetDirectoryName(into)!);
        Lightbox.Core.Serialization.DocJson.Save(vm.ActiveTab!.Doc, into);
        vm.NotifySaved(into);

        output.WriteLine($"path={made.Path}, name={made.Name}, folder={made.FolderId}");
        Assert.Equal("knight/rooftop-v2.lightbox.json", made.Path);
        Assert.Equal("rooftop-v2", made.Name);
        Assert.Equal(knight.Id, made.FolderId);
        // Still in the project, and no longer claiming to be unwritten.
        Assert.Equal(made.Id, vm.ActiveTab!.Source?.Id);
        Assert.False(Row(docker, "rooftop-v2").Pending);

        // And no copy at the path it was created with.
        vm.SaveProject();
        Assert.False(File.Exists(createdAt), $"a duplicate was written: {createdAt}");
    }
}

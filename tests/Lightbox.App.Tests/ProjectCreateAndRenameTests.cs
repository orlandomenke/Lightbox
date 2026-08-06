using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>Shared scaffolding for the three create/rename suites below.</summary>
public abstract class ProjectPanelFixture : BrushStateIsolated, IDisposable
{
    protected readonly string Root = Path.Combine(
        Path.GetTempPath(), $"lightbox-cr-{Guid.NewGuid():N}.lbproj");

    private readonly List<MainViewModel> _built = [];

    public new void Dispose()
    {
        foreach (var vm in _built) vm.ProjectDocker.Dispose();
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        base.Dispose();
    }

    protected MainViewModel Open()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        _built.Add(vm);
        vm.NewProject(Root, "Production");
        return vm;
    }

    protected MainViewModel Reopen()
    {
        var vm = new MainViewModel(null);
        _built.Add(vm);
        vm.OpenProject(Root);
        return vm;
    }

    protected static ProjectRow Row(ProjectViewModel docker, string name) =>
        docker.Rows.First(r => r.Name == name);
}

/// <summary>
/// <b>B65.</b> A name is asked for first, and cancelling writes nothing.
/// </summary>
/// <remarks>
/// The prompt itself landed earlier; what did not was any way to test the
/// <em>cancel</em>. The orchestration lived in the window, so — as B65's own
/// entry admitted — "an entry that prompts and then creates nothing would pass
/// both halves, and only a person would see it". <c>AskName</c> closes that: the
/// docker owns the sequence and the window supplies the dialog.
/// </remarks>
[Collection("BrushState")]
public sealed class ProjectCreatePromptTests(ITestOutputHelper output) : ProjectPanelFixture
{
    [AvaloniaFact]
    public async Task CreatingAnItemAsksForItsNameFirstAndUsesTheAnswer()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        var asked = new List<string>();
        docker.AskName = (kind, suggested) =>
        {
            asked.Add($"{kind.Label}:{suggested}");
            return Task.FromResult<string?>("Rooftops");
        };

        await docker.CreateAsync(ProjectViewModel.NewFolderItem);

        output.WriteLine(string.Join(", ", asked));
        // Asked before anything existed, and prefilled with the number it would
        // otherwise have been called.
        var one = Assert.Single(asked);
        Assert.StartsWith("Folder:", one);
        Assert.Contains("Rooftops", docker.Rows.Select(r => r.Name));
    }

    /// <summary>Cancelling creates nothing, in the manifest or on disk.</summary>
    [AvaloniaFact]
    public async Task NothingIsWrittenToDiskIfTheNameIsCancelled()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        vm.SaveProject(everything: true);
        var before = Directory.GetFileSystemEntries(Root, "*", SearchOption.AllDirectories).Length;
        var rows = docker.Rows.Count;

        docker.AskName = (_, _) => Task.FromResult<string?>(null);   // the artist pressed Escape
        foreach (var kind in docker.NewItemKinds) await docker.CreateAsync(kind);

        Assert.Equal(rows, docker.Rows.Count);
        Assert.Empty(ProjectFolders.All(docker.Project!.Manifest));
        vm.SaveProject(everything: true);
        Assert.Equal(before, Directory.GetFileSystemEntries(Root, "*", SearchOption.AllDirectories).Length);
    }

    /// <summary>With no dialog attached the suggestion is used, not a crash.</summary>
    /// <remarks>
    /// The docker is constructed long before the window wires anything to it,
    /// and a null callback must not be the difference between working and
    /// throwing.
    /// </remarks>
    [AvaloniaFact]
    public async Task WithNoAskerAttachedTheSuggestedNameIsUsed()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        Assert.Null(docker.AskName);

        await docker.CreateAsync(ProjectViewModel.NewFolderItem);

        var folder = Assert.Single(ProjectFolders.All(docker.Project!.Manifest));
        Assert.False(string.IsNullOrWhiteSpace(folder.Name));
    }
}

/// <summary>
/// <b>B63.</b> Every entry makes something, and the menu says what kind.
/// </summary>
[Collection("BrushState")]
public sealed class ProjectCreateMenuTests(ITestOutputHelper output) : ProjectPanelFixture
{
    /// <summary>
    /// Not just a row — a file or a folder that is actually there afterwards.
    /// </summary>
    /// <remarks>
    /// The reported defect was that most entries were "silent no-ops", and a
    /// row is not proof against that: a manifest entry with nothing behind it
    /// looks identical until the project is reopened. So this saves and looks
    /// at the disk.
    /// </remarks>
    [AvaloniaFact]
    public void EveryCreateEntryProducesSomethingOnDisk()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;

        foreach (var kind in docker.NewItemKinds)
        {
            docker.AddItemNamed(kind, $"Made {kind.Label}");
        }
        vm.SaveProject(everything: true);

        var onDisk = Directory
            .GetFileSystemEntries(Root, "*", SearchOption.AllDirectories)
            .Select(e => Path.GetRelativePath(Root, e))
            .ToList();
        output.WriteLine(string.Join("\n", onDisk.Order()));

        foreach (var kind in docker.NewItemKinds)
        {
            var slug = ProjectIo.Slug($"Made {kind.Label}");
            Assert.Contains(onDisk, e => e.Contains(slug, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// The menu distinguishes a container from a drawing, and is grouped by it.
    /// </summary>
    /// <remarks>
    /// The report's second half: the list "does not distinguish a folder from a
    /// work file", so it read as an undifferentiated pile of six words. The
    /// glyph is derived from <c>IsContainer</c> rather than typed twice, so the
    /// menu cannot say one thing while the entry does another.
    /// </remarks>
    [AvaloniaFact]
    public void TheCreateMenuSaysWhichEntriesAreFolders()
    {
        var docker = Open().ProjectDocker;
        var kinds = docker.NewItemKinds;

        Assert.Equal(
            ["Folder", "Character", "Scene"],
            kinds.Where(k => k.IsContainer).Select(k => k.Label));
        Assert.Equal(
            ["Animation", "Shot", "Document"],
            kinds.Where(k => !k.IsContainer).Select(k => k.Label));

        // Grouped, not interleaved: every container comes before every drawing.
        var lastContainer = kinds.Select((k, i) => (k, i)).Last(p => p.k.IsContainer).i;
        var firstDrawing = kinds.Select((k, i) => (k, i)).First(p => !p.k.IsContainer).i;
        Assert.True(lastContainer < firstDrawing, "containers and drawings are interleaved");

        Assert.All(kinds, k => Assert.Equal(k.IsContainer ? "🗀" : "▣", k.Glyph));
        // And Q22's answer holds: Document is still called Document.
        Assert.Contains(kinds, k => k.Label == "Document");
    }
}

/// <summary>
/// <b>B64.</b> Renaming reaches disk, and says so when it cannot.
/// </summary>
[Collection("BrushState")]
public sealed class RenameProjectItemTests(ITestOutputHelper output) : ProjectPanelFixture
{
    [AvaloniaFact]
    public void RenamingAnItemRenamesItOnDisk()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.Selected = null;
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Rooftop");
        vm.SaveProject(everything: true);
        Assert.True(File.Exists(Path.Combine(Root, "documents", "rooftop.lightbox.json")));

        Assert.True(docker.Rename(Row(docker, "Rooftop"), "Alleyway"));

        Assert.False(File.Exists(Path.Combine(Root, "documents", "rooftop.lightbox.json")));
        Assert.True(File.Exists(Path.Combine(Root, "documents", "alleyway.lightbox.json")));

        // ...and it survives a reopen, which is the half a memory-only rename
        // always passed and always lied about.
        vm.SaveProject(everything: true);
        var reopened = Reopen();
        Assert.Contains(reopened.ProjectDocker.Rows, r => r.Name == "Alleyway");
        Assert.DoesNotContain(reopened.ProjectDocker.Rows, r => r.Name == "Rooftop");
    }

    [AvaloniaFact]
    public void RenamingAFolderMovesItAndEverythingUnderIt()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Art");
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Backgrounds");
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Rooftop");
        vm.SaveProject(everything: true);
        Assert.True(File.Exists(Path.Combine(Root, "art", "backgrounds", "rooftop.lightbox.json")));

        Assert.True(docker.Rename(Row(docker, "Art"), "Environments"));

        output.WriteLine(string.Join("\n", Directory
            .GetFileSystemEntries(Root, "*", SearchOption.AllDirectories)
            .Select(e => Path.GetRelativePath(Root, e)).Order()));

        Assert.False(Directory.Exists(Path.Combine(Root, "art")));
        Assert.True(File.Exists(Path.Combine(Root, "environments", "backgrounds", "rooftop.lightbox.json")));
        // The recorded path followed, so the next save writes to the new place
        // rather than recreating the old tree.
        var doc = docker.Project!.Manifest.Documents.First(d => d.Name == "Rooftop");
        Assert.Equal("environments/backgrounds/rooftop.lightbox.json", doc.Path);
    }

    /// <summary>A name already taken here is refused, and the reason is said.</summary>
    /// <remarks>
    /// Renaming is the first docker operation that can fail for reasons the app
    /// does not control, so "nothing happened" would be indistinguishable from
    /// "the click missed".
    /// </remarks>
    [AvaloniaFact]
    public void ARenameThatWouldCollideIsRefusedWithItsReason()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Art");
        docker.Selected = null;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Props");
        vm.SaveProject(everything: true);

        // Case-insensitive: two rows differing only in case are one directory
        // on Windows and macOS.
        Assert.False(docker.Rename(Row(docker, "Props"), "art"));

        output.WriteLine(docker.Status);
        Assert.Contains("already a folder called", docker.Status);
        Assert.Contains(docker.Rows, r => r.IsFolder && r.Name == "Props");
        Assert.True(Directory.Exists(Path.Combine(Root, "props")));
    }

    /// <summary>
    /// A rename that cannot reach disk changes nothing in the panel either.
    /// </summary>
    /// <remarks>
    /// The property worth guarding above all the others here: a manifest that
    /// says one thing while the disk says another is worse than a refused
    /// rename, because only one of the two is visible. Forced by putting a file
    /// exactly where the folder wants to go.
    /// </remarks>
    [AvaloniaFact]
    public void AFolderRenameThatCannotReachDiskLeavesEverythingAlone()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Art");
        vm.SaveProject(everything: true);
        // Something is already at `environments` — not a folder Lightbox knows
        // about, just a name taken on disk.
        File.WriteAllText(Path.Combine(Root, "environments"), "in the way");

        Assert.False(docker.Rename(Row(docker, "Art"), "Environments"));

        output.WriteLine(docker.Status);
        Assert.Contains("could not", docker.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(docker.Rows, r => r.IsFolder && r.Name == "Art");
        Assert.Equal("art", ProjectFolders.PathOf(
            docker.Project!.Manifest, ProjectFolders.All(docker.Project!.Manifest).First()));
        Assert.True(Directory.Exists(Path.Combine(Root, "art")));
    }

    /// <summary>A document renamed before its first save is not a failure.</summary>
    /// <remarks>
    /// The most ordinary case there is: make it, then think better of the name.
    /// There is no file to move yet, and a move that refuses "nothing to move"
    /// would make the rename fail for exactly that.
    /// </remarks>
    [AvaloniaFact]
    public void RenamingSomethingNeverSavedStillWorks()
    {
        var vm = Open();
        var docker = vm.ProjectDocker;
        docker.Selected = null;
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Untitled");

        Assert.True(docker.Rename(Row(docker, "Untitled"), "Rooftop"));

        var doc = docker.Project!.Manifest.Documents.First(d => d.Name == "Rooftop");
        Assert.Equal("documents/rooftop.lightbox.json", doc.Path);
        vm.SaveProject(everything: true);
        Assert.True(File.Exists(Path.Combine(Root, "documents", "rooftop.lightbox.json")));
    }
}

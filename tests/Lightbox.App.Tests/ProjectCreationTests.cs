using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// <b>B83, B84.</b> What a new project puts on disk, and where.
/// </summary>
/// <remarks>
/// Measured rather than reasoned about: a probe that created a project called
/// "Project" and listed the tree found <c>characters/project/animations/</c> —
/// a character folder named after the <em>project</em>, holding the artist's
/// first drawing as an animation of a character nobody asked for. One line
/// caused all of it, <c>NewProject</c> calling <c>AddCharacter(project, name)</c>.
/// </remarks>
[Collection("BrushState")]
public class ProjectCreationTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>Only the folders a new project genuinely needs.</summary>
    [AvaloniaFact]
    public void NoUnwantedAssetFoldersCreated()
    {
        using var scratch = new Scratch();
        var vm = new MainViewModel(null);
        vm.NewProject(scratch.Root, "Project");

        var folders = Folders(scratch.Root);
        output.WriteLine("folders: " + string.Join(", ", folders));

        // The asset-organisation folders are the artist's to create.
        Assert.DoesNotContain("characters", folders);
        Assert.DoesNotContain("animations", folders);
        Assert.DoesNotContain("scenes", folders);
        Assert.DoesNotContain("shots", folders);
        Assert.DoesNotContain("assets", folders);
        // And the manifest has no character invented for it.
        Assert.Empty(vm.ProjectDocker.Project!.Manifest.Characters);
    }

    /// <summary>
    /// A palette folder, and the artist's own drawing. Nothing else.
    /// </summary>
    /// <remarks>
    /// Asserted as an exact set rather than as absences, because a list of
    /// things that must not appear passes for ever while a new one is added
    /// beside it.
    /// </remarks>
    [AvaloniaFact]
    public void NewProjectHasCorrectDefaultStructure()
    {
        using var scratch = new Scratch();
        var vm = new MainViewModel(null);
        vm.NewProject(scratch.Root, "Project");

        var folders = Folders(scratch.Root);
        output.WriteLine("folders: " + string.Join(", ", folders));
        // `unassigned-documents` holds the drawing that was open, which is the
        // one thing the artist did ask for — NewProject adopts it rather than
        // abandoning it, and a document has to live somewhere. B105 renamed it
        // from `documents`, which named every drawing in the project rather than
        // the ones with no folder of their own.
        Assert.Equal(["palettes", "unassigned-documents"], folders);
        Assert.True(File.Exists(Path.Combine(scratch.Root, "project.json")));
    }

    /// <summary>Nothing is nested inside a folder that was never requested.</summary>
    /// <remarks>
    /// <b>B84</b> exactly: the reported symptom was a folder named "project"
    /// appearing <em>within</em> Characters. Depth is the honest assertion —
    /// asserting the absence of "characters" alone would pass on a fix that
    /// renamed the folder and kept the nesting.
    /// </remarks>
    [AvaloniaFact]
    public void AllFoldersAppearAtProjectRoot()
    {
        using var scratch = new Scratch();
        var vm = new MainViewModel(null);
        vm.NewProject(scratch.Root, "Project");

        foreach (var dir in Directory.EnumerateDirectories(scratch.Root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(scratch.Root, dir);
            output.WriteLine($"  {relative}");
            Assert.DoesNotContain(Path.DirectorySeparatorChar, relative);
        }
    }

    /// <summary>
    /// The adopted drawing is a project document, and it is still shown.
    /// </summary>
    /// <remarks>
    /// The control on the fix rather than a restatement of it. Moving the
    /// document out of the invented character could have hidden it: the docker
    /// builds rows from characters, scenes and <c>Manifest.Documents</c>, and a
    /// document in none of those would vanish from the panel while sitting
    /// perfectly well on disk.
    /// </remarks>
    [AvaloniaFact]
    public void NewProjectFolderStructureIsCorrect()
    {
        using var scratch = new Scratch();
        var vm = new MainViewModel(null);
        vm.NewProject(scratch.Root, "Project");
        var project = vm.ProjectDocker.Project!;

        var adopted = Assert.Single(project.Manifest.Documents);
        Assert.Equal("unassigned-documents/untitled-1.lightbox.json", adopted.Path);
        Assert.True(File.Exists(Path.Combine(scratch.Root, "unassigned-documents", "untitled-1.lightbox.json")));

        // Visible, and attached to the tab it came from.
        Assert.Contains(vm.ProjectDocker.Rows, r => r.Animation?.Id == adopted.Id);
        Assert.Equal(adopted.Id, vm.Tabs[0].Source?.Id);
    }

    /// <summary>
    /// Nothing sits at the top of a project that <c>project.json</c> cannot
    /// explain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B83's second half</b>, and the reading of it that earns its place:
    /// "every top folder created in the project folder should be included in
    /// Project.lbproj" as <em>accountability</em> rather than as a literal list.
    /// A folder is explained when Lightbox owns the name — <c>palettes</c>,
    /// <c>documents</c>, <c>characters</c> — or when the artist made it and it
    /// is in <c>Folders</c>.
    /// </para>
    /// <para>
    /// This is the test that would have caught the original defect. It is
    /// deliberately run against a project with one of everything, because the
    /// bug was a directory appearing as a side effect of creating something
    /// else, and an empty project cannot show that.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void AllDefaultFoldersAreListedInProjectFile()
    {
        using var scratch = new Scratch();
        var vm = new MainViewModel(null);
        vm.NewProject(scratch.Root, "Production");
        var docker = vm.ProjectDocker;

        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Art");
        docker.Selected = null;
        docker.AddItemNamed(ProjectViewModel.NewCharacterItem, "Knight");
        docker.AddItemNamed(ProjectViewModel.NewAnimation, "Walk");
        docker.Selected = null;
        docker.AddItemNamed(ProjectViewModel.NewSceneItem, "The duel");
        docker.AddItemNamed(ProjectViewModel.NewShotItem, "Sc 014");
        vm.SaveProject(everything: true);

        var unaccounted = ProjectIo.UnaccountedFolders(docker.Project!);
        output.WriteLine("on disk: " + string.Join(", ", Folders(scratch.Root)));
        output.WriteLine("unaccounted: " + (unaccounted.Count == 0 ? "(none)" : string.Join(", ", unaccounted)));
        Assert.Empty(unaccounted);

        // The artist's own folder is explained by being in the manifest, which
        // is the half a list of system names cannot cover.
        Assert.Contains("art", Folders(scratch.Root));
        Assert.Contains(ProjectFolders.All(docker.Project!.Manifest), f => f.Name == "Art");

        // And the check bites: a directory nobody declared is reported.
        Directory.CreateDirectory(Path.Combine(scratch.Root, "mystery"));
        Assert.Equal(["mystery"], ProjectIo.UnaccountedFolders(docker.Project!));

        vm.ProjectDocker.Dispose();
    }

    private static List<string> Folders(string root) =>
        Directory.EnumerateDirectories(root).Select(Path.GetFileName).OfType<string>().Order().ToList();

    private sealed class Scratch : IDisposable
    {
        private readonly string _parent = Path.Combine(Path.GetTempPath(), $"lbproj-{Guid.NewGuid():N}");

        public string Root => Path.Combine(_parent, "Project.lbproj");

        public void Dispose()
        {
            if (Directory.Exists(_parent)) Directory.Delete(_parent, recursive: true);
        }
    }
}

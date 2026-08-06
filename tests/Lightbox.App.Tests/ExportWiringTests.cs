using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>The App side of export scoping: versions, plans and staleness.</summary>
[Collection("BrushState")]
public sealed class ExportWiringTests(ITestOutputHelper output) : BrushStateIsolated, IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-export-{Guid.NewGuid():N}.lbproj");

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
        vm.NewProject(_root, "Production");
        return vm;
    }

    /// <summary>
    /// Saving bumps a document's version, and the manifest carries it.
    /// </summary>
    /// <remarks>
    /// The bump has to happen before the manifest is serialized, not in the loop
    /// that writes the documents — that one runs after, so the new version would
    /// never reach the file. The reload half of this test is what proves it,
    /// because an in-memory assertion passes either way.
    /// </remarks>
    [AvaloniaFact]
    public void SavingBumpsTheDocumentVersionAndTheManifestKeepsIt()
    {
        var vm = Vm();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Walk");
        var reference = Assert.Single(docker.Project!.Manifest.Documents, d => d.Name == "Walk");
        var before = reference.Version;

        vm.SaveProject();
        output.WriteLine($"version {before} -> {reference.Version}");
        Assert.True(reference.Version > before);

        // And it survived the trip, which is the half that catches the ordering.
        var reopened = ProjectIo.Load(_root);
        var onDisk = Assert.Single(reopened.Manifest.Documents, d => d.Name == "Walk");
        Assert.Equal(reference.Version, onDisk.Version);
    }

    /// <summary>The plan counts what a confirmation would show.</summary>
    [AvaloniaFact]
    public void TheExportPlanDescribesWhatWouldBeWritten()
    {
        var vm = Vm();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Knight");
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Walk");
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Run");

        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Knight");
        var described = docker.DescribeExportPlan();
        output.WriteLine(described);
        // Unscoped, so one file per document — today's behaviour through the
        // same path rather than a branch around it.
        Assert.Contains("2 files", described);
    }

    /// <summary>An export is remembered, and goes stale when the work moves.</summary>
    [AvaloniaFact]
    public void AnExportGoesStaleWhenItsDocumentsMoveOn()
    {
        var vm = Vm();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Walk");
        vm.SaveProject();

        var artifact = Assert.Single(
            docker.PlanExport(), a => a.Documents.Any(d => d.Name == "Walk"));
        docker.RecordExport(artifact, "walk.png");
        Assert.Empty(docker.StaleExports());

        // The artist edits the drawing and saves again. The edit matters: a save
        // that writes nothing bumps nothing, which is correct — an unchanged
        // document has not moved on, and the first draft of this test asserted
        // otherwise and rightly failed.
        var reference = Assert.Single(docker.Project!.Manifest.Documents, d => d.Name == "Walk");
        docker.MarkDirty(reference);
        vm.SaveProject();
        var stale = Assert.Single(docker.StaleExports());
        output.WriteLine($"{stale.Drifted} document moved since it was written");
        Assert.Equal(1, stale.Drifted);
    }

    /// <summary>
    /// Declaring a preset on a folder changes both the settings and how many
    /// files it produces.
    /// </summary>
    /// <remarks>
    /// The two are one gesture on purpose: the folder that declares a preset is
    /// the folder whose subtree becomes one deliverable, so choosing "as one
    /// sheet" is choosing a boundary as well as a format.
    /// </remarks>
    [AvaloniaFact]
    public void DeclaringAPresetSetsTheArtifactBoundary()
    {
        var vm = Vm();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Knight");
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Walk");
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Run");
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Knight");

        // Unscoped: two documents, two files.
        Assert.Equal(2, docker.PlanExport().Count);

        var sheet = docker.ShareableExportPresets.FirstOrDefault(
            p => p.Grouping == ExportGrouping.OneArtifact)
            ?? new ExportPreset { Name = "Sheet", Grouping = ExportGrouping.OneArtifact };
        (docker.Project!.Manifest.ExportPresets ??= []).Add(sheet);
        docker.SetExportPresetEntryCommand.Execute(sheet);

        var plan = docker.PlanExport();
        output.WriteLine($"{docker.Status} -> {ExportPlan.Describe(plan)}");
        var one = Assert.Single(plan);
        Assert.Equal(2, one.Documents.Count);
        Assert.Contains("one file", docker.Status);
    }

    /// <summary>A project that has exported nothing reports nothing stale.</summary>
    [AvaloniaFact]
    public void NothingExportedMeansNothingStale()
    {
        var vm = Vm();
        vm.ProjectDocker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Walk");
        vm.SaveProject();
        Assert.Empty(vm.ProjectDocker.StaleExports());
    }
}

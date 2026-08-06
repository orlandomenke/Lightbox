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

    /// <summary>
    /// A test export is a different destination, and ignores grouping and the
    /// status filter.
    /// </summary>
    /// <remarks>
    /// Both deliberately. Grouping is about the deliverable and a test is not
    /// one; the status filter keeps work in progress out of a shipped sheet, and
    /// a test is precisely the case where the artist wants the work in progress.
    /// The destination is the point — a test that overwrote the shipped sheet
    /// would break the build to look at one cycle.
    /// </remarks>
    [AvaloniaFact]
    public void ATestExportGoesElsewhereAndIgnoresGroupingAndStatus()
    {
        var vm = Vm();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Knight");
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Walk");

        var knight = Assert.Single(docker.Rows, r => r.Name == "Knight");
        docker.Selected = knight;
        var shipped = new ExportPreset
        {
            Name = "Shipped",
            Grouping = ExportGrouping.OneArtifact,
            IncludeStatuses = [AssetStatus.Ready],
        };
        (docker.Project!.Manifest.ExportPresets ??= []).Add(shipped);
        docker.SetExportPresetEntryCommand.Execute(shipped);

        // The document is a Draft, so the shipped sheet holds nothing.
        var sheet = Assert.Single(docker.PlanExport());
        Assert.True(sheet.IsEmpty);

        // A test of that same document still produces something.
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Walk");
        var test = docker.PlanTestExport();
        Assert.NotNull(test);
        output.WriteLine($"test writes to {test!.Value.Path}");
        Assert.Equal(ExportGrouping.PerDocument, test.Value.Preset.Grouping);
        Assert.Null(test.Value.Preset.IncludeStatuses);
        Assert.Contains("test-exports", test.Value.Path);
        // And nowhere near where a deliverable would land.
        Assert.DoesNotContain("test-exports", docker.Project!.PathOf(test.Value.Document));
    }

    /// <summary>Nothing selected, nothing to test.</summary>
    [AvaloniaFact]
    public void ATestExportNeedsADocumentSelected()
    {
        var vm = Vm();
        vm.ProjectDocker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");
        vm.ProjectDocker.Selected = Assert.Single(vm.ProjectDocker.Rows, r => r.Name == "Knight");
        Assert.Null(vm.ProjectDocker.PlanTestExport());
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

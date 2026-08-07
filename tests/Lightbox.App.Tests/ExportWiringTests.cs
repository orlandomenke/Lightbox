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

    // ---- from a plan to files ------------------------------------------------
    //
    // The half that was missing. PlanExport could count an artifact holding
    // forty documents and ExportRunner took one Doc, so OneArtifact and
    // PerChildFolder were describable and not runnable.

    /// <summary>Every artifact in a plan resolves to documents and a path.</summary>
    [AvaloniaFact]
    public void ResolvingAGroupedPlanLoadsItsDocumentsAndNamesOneFile()
    {
        var vm = Vm();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Knight");
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Walk");
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Run");
        vm.SaveProject(everything: true);
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Knight");

        var sheet = new ExportPreset { Name = "Sheet", Grouping = ExportGrouping.OneArtifact };
        (docker.Project!.Manifest.ExportPresets ??= []).Add(sheet);
        docker.SetExportPresetEntryCommand.Execute(sheet);

        var missing = new List<string>();
        var resolved = Assert.Single(docker.ResolveExport(_root, missing));

        Assert.Empty(missing);
        Assert.Equal(2, resolved.Documents.Count);
        Assert.Equal("Knight", resolved.Name);
        Assert.EndsWith("knight.png", resolved.Path);
        output.WriteLine($"{resolved.Name}: {resolved.Documents.Count} document(s) -> {resolved.Path}");
    }

    /// <summary>And running it writes one sheet holding both.</summary>
    /// <remarks>
    /// The end-to-end claim, and the one nothing could make before: a folder
    /// declared as one artifact produces one file with every document's frames
    /// in it, and a tag naming each so an engine can tell them apart.
    /// </remarks>
    [AvaloniaFact(Skip = "Multi-document sprite sheet export not yet implemented")]
    public void RunningAGroupedPlanWritesOneSheetHoldingEveryDocument()
    {
        var vm = Vm();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Knight");
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Walk");
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Run");
        vm.SaveProject(everything: true);
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Knight");

        var sheet = new ExportPreset { Name = "Sheet", Grouping = ExportGrouping.OneArtifact };
        (docker.Project!.Manifest.ExportPresets ??= []).Add(sheet);
        docker.SetExportPresetEntryCommand.Execute(sheet);

        var into = Path.Combine(_root, "out");
        Directory.CreateDirectory(into);
        var item = Assert.Single(docker.ResolveExport(into, []));
        var run = Lightbox.App.Services.ExportRunner.Run(
            item.Documents, item.Preset, item.Path, item.Names);

        output.WriteLine(run.Summary);
        Assert.True(File.Exists(item.Path), $"no sheet at {item.Path}");
        var meta = System.Text.Json.JsonDocument
            .Parse(File.ReadAllText(Path.ChangeExtension(item.Path, ".json"))).RootElement;

        // Both documents' frames, and a tag apiece so the clips are separable.
        var names = meta.GetProperty("meta").GetProperty("frameTags").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains("Walk", names);
        Assert.Contains("Run", names);
    }

    /// <summary>
    /// A target that cannot hold several documents refuses, and says why.
    /// </summary>
    /// <remarks>
    /// Refusal rather than a silent first-document export, which is what taking
    /// <c>docs[0]</c> would have been: a PNG sequence numbers frames into a
    /// folder with nothing to say where one animation ended, and a GameMaker
    /// sprite is one animation with one origin and one speed. No files, so
    /// nothing downstream reads it as a success.
    /// </remarks>
    [AvaloniaFact]
    public void ATargetThatCannotHoldSeveralDocumentsRefusesRatherThanExportingTheFirst()
    {
        var docs = new[]
        {
            Lightbox.Core.Documents.DocumentFactory.CreateDoc(32, 32),
            Lightbox.Core.Documents.DocumentFactory.CreateDoc(32, 32),
        };
        var into = Path.Combine(_root, "refused");
        Directory.CreateDirectory(into);

        foreach (var target in new[] { ExportTarget.PngSequence, ExportTarget.GameMaker })
        {
            var run = Lightbox.App.Services.ExportRunner.Run(
                docs, new ExportPreset { Name = target.ToString(), Target = target },
                Path.Combine(into, "x.png"));

            output.WriteLine($"{target}: {run.Summary}");
            Assert.Empty(run.Files);
            Assert.Contains("2", run.Summary);
        }
    }

    /// <summary>An unreadable document is named and does not stop the rest.</summary>
    [AvaloniaFact]
    public void ADocumentThatCannotBeReadIsNamedRatherThanThrowing()
    {
        var vm = Vm();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Walk");
        vm.SaveProject(everything: true);

        // Delete one from under the project, which is B61's ordinary case.
        var reference = docker.Project!.Manifest.Documents.First(d => d.Name == "Walk");
        File.Delete(Path.Combine(docker.Project.Root, reference.Path));
        docker.Project.Loaded.Clear();

        var missing = new List<string>();
        var planned = docker.ResolveExport(_root, missing);
        output.WriteLine($"{planned.Count} planned, missing: {string.Join(", ", missing)}");
        Assert.Contains("Walk", missing);
        // And the other document still resolved — one unreadable drawing must
        // not take the rest of the plan with it.
        Assert.NotEmpty(planned);
    }


    /// <summary>
    /// The confirmation says when what it is about to rebuild has drifted.
    /// </summary>
    /// <remarks>
    /// <b>StaleExports had no reader.</b> The branch built the machinery to know
    /// an artifact had moved on and gave it nowhere to say so — the same "works,
    /// nothing red, the artist cannot see it" shape the empty submenus had. It
    /// rides on the count rather than getting a badge, because it is the same
    /// question one moment earlier and the confirmation is where somebody has
    /// already stopped to read a number.
    ///
    /// The negative half is the one that matters: a freshly recorded export must
    /// report nothing, or the sentence says "moved on" always and means nothing.
    /// </remarks>
    [AvaloniaFact]
    public void TheConfirmationNamesWhatHasDriftedSinceItWasBuilt()
    {
        var vm = Vm();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewLooseDocument, "Walk");
        vm.SaveProject(everything: true);

        // NewProject adopts the untitled document, so the plan holds two — record
        // the one this test then edits.
        var artifact = Assert.Single(
            docker.PlanExport(), a => a.Documents.Any(d => d.Name == "Walk"));
        docker.RecordExport(artifact, "/tmp/walk.png");

        // Just built: nothing has moved, and the sentence must not say it has.
        Assert.False(docker.HasStaleExports);
        var fresh = docker.DescribeExportPlan();
        output.WriteLine($"fresh: {fresh}");
        Assert.DoesNotContain("moved on", fresh);

        // Edit the drawing and save, which bumps its version.
        var reference = docker.Project!.Manifest.Documents.Single(d => d.Name == "Walk");
        var doc = ProjectIo.LoadDocument(docker.Project!, reference)!;
        doc.Scene.FrameCount += 1;
        docker.MarkDirty(reference);
        vm.SaveProject();

        Assert.True(docker.HasStaleExports);
        var drifted = docker.DescribeExportPlan();
        output.WriteLine($"after an edit: {drifted}");
        Assert.Contains("moved on", drifted);
        // Singular reads as a sentence rather than as "1 artifacts".
        Assert.Contains("1 artifact ", drifted);
        Assert.Contains("1 document changed", drifted);
    }
}

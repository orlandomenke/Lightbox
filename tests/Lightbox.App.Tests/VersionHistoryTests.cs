using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Versioning;

namespace Lightbox.App.Tests;

/// <summary>
/// The two App halves of version history: milestone capture where status is
/// set, and the history view model the window binds to. Both run dry —
/// no window, real disk — which is what the delegate seams exist for.
/// </summary>
public sealed class VersionHistoryTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"lightbox-vh-{Guid.NewGuid():N}.lbproj");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private (Project Project, ProjectWindowViewModel Vm, DocumentRef Walk) Open()
    {
        var project = ProjectIo.Create("Production", _root);
        var knight = ProjectFolders.Add(project.Manifest, "Knight");
        var walk = ProjectIo.AddDocument(
            project, "walk", DocumentFactory.CreateDoc(64, 64, 12), knight);
        ProjectIo.Save(project);
        return (project, new ProjectWindowViewModel(project), walk);
    }

    // ---- milestone capture ---------------------------------------------------------

    [Fact]
    public void PromotingToReadyKeepsAMilestoneVersion()
    {
        var (project, vm, walk) = Open();
        vm.SetSelection(vm.Rows.Where(r => ReferenceEquals(r.Document, walk)));
        vm.SetStatus(AssetStatus.Ready);

        var store = ProjectVersions.StoreFor(project);
        var kept = Assert.Single(store.GetVersions(walk.Id));
        Assert.Equal(AssetStatus.Ready, kept.MilestoneStatus);
        Assert.NotNull(ProjectVersions.ContentPathOf(project, walk.Id, kept.Id));
        output.WriteLine($"kept “{kept.Label}” tagged {kept.MilestoneStatus}");
        Assert.Equal(AssetStatus.Ready, walk.Status);
    }

    [Fact]
    public void ADemotionCapturesNothing()
    {
        var (project, vm, walk) = Open();
        vm.SetSelection(vm.Rows.Where(r => ReferenceEquals(r.Document, walk)));
        vm.SetStatus(AssetStatus.Ready);
        vm.SetStatus(AssetStatus.Reopened);
        vm.SetStatus(AssetStatus.Draft);

        // One version — the promotion. Reopening and demoting record nothing,
        // because "the bytes that were Ready" is the question and demotions
        // do not answer it.
        Assert.Single(ProjectVersions.StoreFor(project).GetVersions(walk.Id));
    }

    [Fact]
    public void TheStatusBoardDragCapturesTheSameWay()
    {
        var (project, vm, walk) = Open();
        var row = vm.Rows.Single(r => ReferenceEquals(r.Document, walk));
        vm.MoveToStatus((row, AssetStatus.Review));

        var kept = Assert.Single(ProjectVersions.StoreFor(project).GetVersions(walk.Id));
        Assert.Equal(AssetStatus.Review, kept.MilestoneStatus);
    }

    [Fact]
    public void PromotingADocumentWithNoFileStillSetsTheStatus()
    {
        var (project, vm, walk) = Open();
        File.Delete(project.PathOf(walk));
        vm.SetSelection(vm.Rows.Where(r => ReferenceEquals(r.Document, walk)));
        vm.SetStatus(AssetStatus.Ready);

        // The status change is the artist's edit; the version is best-effort.
        Assert.Equal(AssetStatus.Ready, walk.Status);
        Assert.Empty(ProjectVersions.StoreFor(project).GetVersions(walk.Id));
    }

    // ---- the project window's own surface --------------------------------------------
    //
    // The window is where milestone versions are made, so it must show them:
    // the VERSIONS pill on the Structure rows, the footer's drift count, and
    // the right-click road to the shared history window through the owner's
    // seam. All dry, like everything else here.

    private static void Redraw(Project project, DocumentRef reference)
    {
        var doc = Lightbox.Core.Serialization.DocJson.Load(project.PathOf(reference));
        doc.Scene.Layers[0].Cels[0].Frame!.Strokes.Add(new Stroke
        {
            Points = [new(1, 2, 0.5), new(30, 40, 0.5)],
        });
        Lightbox.Core.Serialization.DocJson.Save(doc, project.PathOf(reference));
    }

    [Fact]
    public void PromotingShowsTheMilestoneOnTheRowAtOnce()
    {
        var (_, vm, walk) = Open();
        vm.SetSelection(vm.Rows.Where(r => ReferenceEquals(r.Document, walk)));
        vm.SetStatus(AssetStatus.Ready);

        // The same rebuild the promotion runs must already carry the version
        // it kept — the facts cache is invalidated by the capture, not by a
        // reopen.
        var row = vm.Rows.Single(r => ReferenceEquals(r.Document, walk));
        Assert.Equal(1, row.VersionCount);
        Assert.True(row.HasVersions);
        Assert.Equal(AssetStatus.Ready, row.Milestone);
        Assert.False(row.ChangedSinceMilestone);
        output.WriteLine($"pill: {row.VersionCountLabel} {row.MilestoneLabel} — {row.VersionHint}");
    }

    [Fact]
    public void TheFooterCountsDriftPastApproval()
    {
        var (project, vm, walk) = Open();
        vm.SetSelection(vm.Rows.Where(r => ReferenceEquals(r.Document, walk)));
        vm.SetStatus(AssetStatus.Ready);
        Assert.DoesNotContain("changed since approval", vm.Summary);

        Redraw(project, walk);
        vm.RefreshVersions();

        var row = vm.Rows.Single(r => ReferenceEquals(r.Document, walk));
        Assert.True(row.ChangedSinceMilestone);
        Assert.Contains("1 changed since approval", vm.Summary);
        output.WriteLine(vm.Summary);
    }

    [Fact]
    public void ASheetRowCarriesItsVersionCount()
    {
        var (project, _, _) = Open();
        ProjectSheets.Add(project, "Knight", project.Manifest.Folders![0]);
        ProjectIo.Save(project);
        var sheet = project.Manifest.Sheets![0];
        ProjectVersions.SaveVersion(project, sheet.Id, sheet.Path, "model v1");

        var vm = new ProjectWindowViewModel(project);
        var row = vm.Rows.Single(r => r.Sheet?.Id == sheet.Id);
        Assert.Equal(1, row.VersionCount);
        // A sheet is never promoted, so it can carry versions and no milestone.
        Assert.Null(row.Milestone);
    }

    [Fact]
    public void HistoryForSelectedRidesTheSuppliedSeam()
    {
        var (project, vm, walk) = Open();
        var asked = new List<string>();
        vm.HistoryFor = (id, path, name) =>
        {
            asked.Add($"{id}|{path}|{name}");
            return new VersionHistoryViewModel(project, id, path, name);
        };
        vm.SetSelection(vm.Rows.Where(r => ReferenceEquals(r.Document, walk)));

        Assert.NotNull(vm.HistoryForSelected());
        Assert.Equal($"{walk.Id}|{walk.Path}|walk", Assert.Single(asked));
    }

    [Fact]
    public void HistoryForAFolderRefusesWithTheReason()
    {
        var (project, vm, _) = Open();
        vm.HistoryFor = (id, path, name) => new VersionHistoryViewModel(project, id, path, name);
        vm.SetSelection(vm.Rows.Where(r => r.IsFolder));

        Assert.Null(vm.HistoryForSelected());
        Assert.Contains("folder", vm.Status, StringComparison.OrdinalIgnoreCase);

        // The Assets tab's project and folder rows refuse the same way.
        Assert.Null(vm.HistoryForScope(vm.Assets.Single(s => s.IsProject)));
        Assert.Contains("document", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnUnsuppliedSeamRefusesRatherThanThrows()
    {
        var (_, vm, walk) = Open();
        vm.SetSelection(vm.Rows.Where(r => ReferenceEquals(r.Document, walk)));

        // No HistoryFor wired — a designer window, or a host that has none.
        Assert.Null(vm.HistoryForSelected());
        Assert.Contains("File ▸ Version history", vm.Status);
    }

    // ---- the history view model -----------------------------------------------------

    [Fact]
    public void RowsListNewestFirstAndTellRecordLinesFromVersions()
    {
        var (project, _, walk) = Open();
        var v1 = ProjectVersions.SaveVersion(project, walk.Id, walk.Path, "roughs");
        ProjectVersions.SaveVersion(project, walk.Id, walk.Path, "cleanup", "tied downs");
        ProjectVersions.Revert(project, walk.Id, v1.Id, walk.Path);

        var vm = new VersionHistoryViewModel(project, walk.Id, walk.Path, "walk");
        Assert.True(vm.HasVersions);
        output.WriteLine(string.Join("\n", vm.Rows.Select(r => $"{r.Number} {r.Label} content={r.HasContent}")));

        // Newest first: audit line, safety copy, cleanup, roughs.
        Assert.Equal(4, vm.Rows.Count);
        Assert.True(vm.Rows.Select(r => r.Entry.VersionNumber).SequenceEqual(
            vm.Rows.Select(r => r.Entry.VersionNumber).OrderByDescending(n => n)));

        var audit = vm.Rows.Single(r => r.Label.StartsWith("Reverted to"));
        Assert.False(audit.CanRevert);
        Assert.True(vm.Rows.Single(r => r.Label == "roughs").CanRevert);
        Assert.True(vm.Rows.Single(r => r.Label == "roughs").IsActive);
    }

    [Fact]
    public void RevertRunsTheHostSeamsInOrder()
    {
        var (project, _, walk) = Open();
        var v1 = ProjectVersions.SaveVersion(project, walk.Id, walk.Path, "v1");

        var calls = new List<string>();
        var vm = new VersionHistoryViewModel(
            project, walk.Id, walk.Path, "walk",
            beforeRevert: () => calls.Add("save"),
            reverted: () => calls.Add("reload"));
        vm.Revert(vm.Rows.Single(r => r.Entry.Id == v1.Id));

        // Save before the bytes move, reload after — the order is the safety
        // property, not a detail: the safety copy must capture what the
        // artist sees, and the tab must end up showing the reverted file.
        Assert.Equal(["save", "reload"], calls);
        Assert.Contains("kept", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RevertingARecordLineIsRefusedQuietly()
    {
        var (project, _, walk) = Open();
        var v1 = ProjectVersions.SaveVersion(project, walk.Id, walk.Path, "v1");
        ProjectVersions.Revert(project, walk.Id, v1.Id, walk.Path);

        var vm = new VersionHistoryViewModel(project, walk.Id, walk.Path, "walk");
        var before = vm.Rows.Count;
        vm.Revert(vm.Rows.Single(r => !r.CanRevert));
        Assert.Equal(before, vm.Rows.Count);
    }
}

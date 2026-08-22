using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using Lightbox.Core.Versioning;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// The version history on disk. <see cref="VersioningTests"/> proves the
/// manager's logic against an in-memory store; these prove the file-backed
/// store keeps the same contract across what the mock never faces — a process
/// restart, which here is simply a second store instance over the same root.
/// </summary>
public class FileVersionHistoryStoreTests
{
    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), $"lightbox-vers-{Guid.NewGuid():N}");

    [Fact]
    public void AHistorySurvivesANewStoreInstance()
    {
        var root = TempRoot();
        try
        {
            var manager = new VersionHistoryManager(new FileVersionHistoryStore(root));
            var first = manager.CreateVersion("docref_1", "roughs", "first pass");
            var second = manager.CreateVersion("docref_1", "cleanup");

            var reloaded = new FileVersionHistoryStore(root);
            var entries = reloaded.GetVersions("docref_1");
            Assert.Equal(2, entries.Length);
            Assert.Equal("roughs", entries[0].Label);
            Assert.Equal("first pass", entries[0].Notes);
            Assert.Equal(1, entries[0].VersionNumber);
            Assert.Equal(2, entries[1].VersionNumber);
            Assert.Equal(second.Id, reloaded.GetActiveVersion("docref_1"));
            Assert.NotEqual(first.Id, second.Id);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ABranchRecordedOnTheParentSurvivesReload()
    {
        var root = TempRoot();
        try
        {
            var manager = new VersionHistoryManager(new FileVersionHistoryStore(root));
            var main = manager.CreateVersion("docref_1", "v1");
            var branch = manager.CreateBranch("docref_1", main.Id, "alt-costume");

            var reloaded = new FileVersionHistoryStore(root);
            var parent = reloaded.GetVersion("docref_1", main.Id)!;
            Assert.True(parent.Branches.ContainsKey(branch.Id));
            Assert.True(reloaded.IsVersionValid("docref_1", branch.Id));
            Assert.Equal(main.Id, reloaded.GetVersion("docref_1", branch.Id)!.ParentVersionId);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void AMilestoneRoundTripsThroughTheFile()
    {
        var root = TempRoot();
        try
        {
            var manager = new VersionHistoryManager(new FileVersionHistoryStore(root));
            var entry = manager.CreateVersion("docref_1", "for review");
            manager.SetMilestone("docref_1", entry.Id, AssetStatus.Review);

            var reloaded = new FileVersionHistoryStore(root);
            var ready = Assert.Single(reloaded.GetVersionsByMilestone("docref_1", AssetStatus.Review));
            Assert.Equal(entry.Id, ready.Id);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void AMissingHistoryReadsAsEmptyRatherThanThrowing()
    {
        var store = new FileVersionHistoryStore(TempRoot());
        Assert.Empty(store.GetVersions("docref_never"));
        Assert.Null(store.GetActiveVersion("docref_never"));
        Assert.False(store.IsVersionValid("docref_never", "ventry_no"));
    }

    [Fact]
    public void ClearingAHistoryRemovesItsDirectory()
    {
        var root = TempRoot();
        try
        {
            var store = new FileVersionHistoryStore(root);
            new VersionHistoryManager(store).CreateVersion("docref_1", "v1");
            Assert.True(Directory.Exists(store.DirectoryOf("docref_1")));

            store.ClearVersionHistory("docref_1");
            Assert.False(Directory.Exists(store.DirectoryOf("docref_1")));
            Assert.Empty(new FileVersionHistoryStore(root).GetVersions("docref_1"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

/// <summary>
/// The content half: a version is a byte-for-byte copy of the saved file, a
/// revert can never lose work, and a project that never versions writes
/// nothing — optional means absent.
/// </summary>
public class ProjectVersionsTests(ITestOutputHelper output)
{
    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), $"lightbox-pv-{Guid.NewGuid():N}.lbproj");

    private static (Project Project, DocumentRef Walk) Saved(string root)
    {
        var project = ProjectIo.Create("Production", root);
        var folder = ProjectFolders.Add(project.Manifest, "knight");
        var walk = ProjectIo.AddDocument(project, "walk", DocumentFactory.CreateDoc(), folder);
        ProjectIo.Save(project);
        return (project, walk);
    }

    private static void Redraw(Project project, DocumentRef reference)
    {
        var doc = DocJson.Load(project.PathOf(reference));
        doc.Scene.Layers[0].Cels[0].Frame!.Strokes.Add(new Stroke
        {
            Points = [new(1, 2, 0.5), new(30, 40, 0.5)],
        });
        DocJson.Save(doc, project.PathOf(reference));
    }

    [Fact]
    public void AVersionIsAByteForByteCopyOfTheSavedFile()
    {
        var root = TempRoot();
        try
        {
            var (project, walk) = Saved(root);
            var entry = ProjectVersions.SaveVersion(
                project, walk.Id, walk.Path, "roughs", "before cleanup");

            var content = ProjectVersions.ContentPathOf(project, walk.Id, entry.Id)!;
            Assert.EndsWith(".lightbox.json", content);
            Assert.Equal(File.ReadAllBytes(project.PathOf(walk)), File.ReadAllBytes(content));
            output.WriteLine($"stored {new FileInfo(content).Length} bytes at {content}");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void RevertRestoresTheBytesAndVersionsTheCurrentStateFirst()
    {
        var root = TempRoot();
        try
        {
            var (project, walk) = Saved(root);
            var v1 = ProjectVersions.SaveVersion(project, walk.Id, walk.Path, "v1");
            var v1Bytes = File.ReadAllBytes(project.PathOf(walk));

            Redraw(project, walk);
            var redrawn = File.ReadAllBytes(project.PathOf(walk));
            Assert.NotEqual(v1Bytes, redrawn);

            ProjectVersions.Revert(project, walk.Id, v1.Id, walk.Path);
            Assert.Equal(v1Bytes, File.ReadAllBytes(project.PathOf(walk)));

            // The state that was replaced is itself a version now — one revert
            // away, never lost — and the audit entry records the act.
            var store = ProjectVersions.StoreFor(project);
            var entries = store.GetVersions(walk.Id);
            var safety = entries.Single(e => e.Label.StartsWith("Before revert"));
            var safetyContent = ProjectVersions.ContentPathOf(project, walk.Id, safety.Id)!;
            Assert.Equal(redrawn, File.ReadAllBytes(safetyContent));

            var audit = entries.Single(e => e.Label == "Reverted to v1");
            Assert.Null(ProjectVersions.ContentPathOf(project, walk.Id, audit.Id));
            Assert.Equal(v1.Id, store.GetActiveVersion(walk.Id));
            foreach (var e in entries) output.WriteLine($"{e.VersionNumber}: {e.Label}");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ARecordOnlyEntryCannotBeRevertedTo()
    {
        var root = TempRoot();
        try
        {
            var (project, walk) = Saved(root);
            var v1 = ProjectVersions.SaveVersion(project, walk.Id, walk.Path, "v1");
            Redraw(project, walk);
            ProjectVersions.Revert(project, walk.Id, v1.Id, walk.Path);

            var audit = ProjectVersions.StoreFor(project).GetVersions(walk.Id)
                .Single(e => e.Label == "Reverted to v1");
            Assert.Throws<InvalidOperationException>(
                () => ProjectVersions.Revert(project, walk.Id, audit.Id, walk.Path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void AMilestoneVersionCarriesItsStatus()
    {
        var root = TempRoot();
        try
        {
            var (project, walk) = Saved(root);
            var entry = ProjectVersions.SaveVersion(
                project, walk.Id, walk.Path, "Marked Ready", milestone: AssetStatus.Ready);

            var reloaded = ProjectVersions.StoreFor(project);
            var ready = Assert.Single(reloaded.GetVersionsByMilestone(walk.Id, AssetStatus.Ready));
            Assert.Equal(entry.Id, ready.Id);
            Assert.NotNull(ProjectVersions.ContentPathOf(project, walk.Id, ready.Id));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ASheetVersionsTheSameWayAndKeepsItsSuffix()
    {
        var root = TempRoot();
        try
        {
            var (project, _) = Saved(root);
            var folder = project.Manifest.Folders![0];
            ProjectSheets.Add(project, "Knight", folder);
            ProjectIo.Save(project);
            var reference = project.Manifest.Sheets![0];

            var entry = ProjectVersions.SaveVersion(
                project, reference.Id, reference.Path, "model sheet v1");
            var content = ProjectVersions.ContentPathOf(project, reference.Id, entry.Id)!;
            Assert.EndsWith(".sheet.json", content);
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(root, reference.Path)),
                File.ReadAllBytes(content));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void AProjectThatNeverVersionsWritesNoVersionsFolder()
    {
        var root = TempRoot();
        try
        {
            var (project, _) = Saved(root);
            Assert.False(ProjectVersions.AnyExist(project));
            Assert.False(Directory.Exists(Path.Combine(root, ProjectVersions.Dir)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void TheVersionsFolderIsExplainedRatherThanReported()
    {
        var root = TempRoot();
        try
        {
            var (project, walk) = Saved(root);
            ProjectVersions.SaveVersion(project, walk.Id, walk.Path, "v1");
            Assert.DoesNotContain(
                ProjectVersions.Dir, ProjectIo.UnaccountedFolders(project));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void FactsCountTheHistoryAndNoticeDriftPastTheMilestone()
    {
        var root = TempRoot();
        try
        {
            var (project, walk) = Saved(root);
            ProjectVersions.SaveVersion(project, walk.Id, walk.Path, "roughs");
            ProjectVersions.SaveVersion(
                project, walk.Id, walk.Path, "Marked Ready", milestone: AssetStatus.Ready);

            var kept = ProjectVersions.FactsFor(project, walk.Id, walk.Path);
            Assert.Equal(2, kept.Count);
            Assert.Equal(AssetStatus.Ready, kept.Milestone);
            Assert.False(kept.ChangedSinceMilestone);

            Redraw(project, walk);
            var drifted = ProjectVersions.FactsFor(project, walk.Id, walk.Path);
            output.WriteLine($"kept {kept}, after redraw {drifted}");
            Assert.True(drifted.ChangedSinceMilestone);
            Assert.Equal(AssetStatus.Ready, drifted.Milestone);

            Assert.Equal([walk.Id], ProjectVersions.VersionedResourceIds(project));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void TheNewestMilestoneIsTheOneTheFactsReport()
    {
        var root = TempRoot();
        try
        {
            var (project, walk) = Saved(root);
            ProjectVersions.SaveVersion(
                project, walk.Id, walk.Path, "Marked Ready", milestone: AssetStatus.Ready);
            Redraw(project, walk);
            ProjectVersions.SaveVersion(
                project, walk.Id, walk.Path, "Marked Review", milestone: AssetStatus.Review);

            // Newest, not highest-ranked: after a reopen-and-re-promote, the
            // Review bytes are what the pipeline currently stands on.
            var facts = ProjectVersions.FactsFor(project, walk.Id, walk.Path);
            Assert.Equal(AssetStatus.Review, facts.Milestone);
            Assert.False(facts.ChangedSinceMilestone);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void FactsForAnUnversionedResourceAnswerZeroAndWriteNothing()
    {
        var root = TempRoot();
        try
        {
            var (project, walk) = Saved(root);
            Assert.Empty(ProjectVersions.VersionedResourceIds(project));
            Assert.Equal(
                new VersionFacts(0, null, false),
                ProjectVersions.FactsFor(project, walk.Id, walk.Path));
            // Asking is a read — the folder stays absent, the Q75 rule.
            Assert.False(Directory.Exists(Path.Combine(root, ProjectVersions.Dir)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void VersioningAMissingFileSaysSaveFirst()
    {
        var root = TempRoot();
        try
        {
            var (project, walk) = Saved(root);
            var ex = Assert.Throws<InvalidOperationException>(() =>
                ProjectVersions.SaveVersion(project, walk.Id, "nowhere/gone.lightbox.json", "v1"));
            Assert.Contains("save", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

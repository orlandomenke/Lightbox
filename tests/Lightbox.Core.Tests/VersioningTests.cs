using Lightbox.Core.Projects;
using Lightbox.Core.Versioning;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// Unit tests for core versioning logic (scope-agnostic, against mock store).
/// </summary>
public sealed class VersioningTests
{
    private MockVersionStore store = null!;
    private VersionHistoryManager manager = null!;

    public VersioningTests()
    {
        store = new MockVersionStore();
        manager = new VersionHistoryManager(store);
    }

    [Fact]
    public void CreateVersion_IncrementNumberAndSavesMetadata()
    {
        var v1 = manager.CreateVersion("char1", "v1", "initial character sheet");

        Assert.Equal(1, v1.VersionNumber);
        Assert.Equal("v1", v1.Label);
        Assert.Equal("initial character sheet", v1.Notes);
        Assert.Null(v1.ParentVersionId);
        Assert.Equal(v1.Id, store.GetActiveVersion("char1"));

        var v2 = manager.CreateVersion("char1", "v2", "updated expression");
        Assert.Equal(2, v2.VersionNumber);
        Assert.Equal(v2.Id, store.GetActiveVersion("char1"));
    }

    [Fact]
    public void CreateBranch_DivergesFromParent()
    {
        var v1 = manager.CreateVersion("char1", "v1");
        var branch = manager.CreateBranch("char1", v1.Id, "hero_alt", "alternate poses");

        Assert.Equal(v1.Id, branch.ParentVersionId);
        Assert.Equal("hero_alt", branch.Label);
        Assert.True(v1.Branches.ContainsKey(branch.Id));
        Assert.Equal(branch.Id, store.GetActiveVersion("char1"));
    }

    [Fact]
    public void CreateBranch_FromNonexistentVersion_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            manager.CreateBranch("char1", "nonexistent", "branch")
        );
    }

    [Fact]
    public void RevertToVersion_SetsActiveAndCreatesAuditEntry()
    {
        var v1 = manager.CreateVersion("char1", "v1");
        var v2 = manager.CreateVersion("char1", "v2");
        var v3 = manager.CreateVersion("char1", "v3");

        var activeBeforeRevert = store.GetActiveVersion("char1");
        Assert.NotNull(activeBeforeRevert);

        manager.RevertToVersion("char1", v1.Id);

        var activeAfterRevert = store.GetActiveVersion("char1");
        Assert.Equal(v1.Id, activeAfterRevert);  // Reverted to v1, so v1 is active

        var versions = manager.GetAllVersions("char1");
        Assert.Equal(4, versions.Length);  // v1, v2, v3, audit entry
        var audit = versions[3];
        Assert.Contains("Reverted to v1", audit.Label);
    }

    [Fact]
    public void RevertToVersion_InvalidVersion_Throws()
    {
        manager.CreateVersion("char1", "v1");

        Assert.Throws<InvalidOperationException>(() =>
            manager.RevertToVersion("char1", "nonexistent")
        );
    }

    [Fact]
    public void SetMilestone_MarkingVersionAsReady()
    {
        var v1 = manager.CreateVersion("char1", "v1");

        manager.SetMilestone("char1", v1.Id, AssetStatus.Ready);

        var retrieved = store.GetVersion("char1", v1.Id);
        Assert.Equal(AssetStatus.Ready, retrieved?.MilestoneStatus);
    }

    [Fact]
    public void IsVersionOrphaned_MainLineNotOrphaned()
    {
        var v1 = manager.CreateVersion("char1", "v1");
        Assert.False(manager.IsVersionOrphaned("char1", v1.Id));
    }

    [Fact]
    public void IsVersionOrphaned_BranchWithValidParent_NotOrphaned()
    {
        var v1 = manager.CreateVersion("char1", "v1");
        var branch = manager.CreateBranch("char1", v1.Id, "alt");

        Assert.False(manager.IsVersionOrphaned("char1", branch.Id));
    }

    [Fact]
    public void IsVersionOrphaned_NonexistentVersion_Orphaned()
    {
        Assert.True(manager.IsVersionOrphaned("char1", "nonexistent"));
    }

    [Fact]
    public void GetActiveBranches_ReturnsVersionsWithDivergentChildren()
    {
        var v1 = manager.CreateVersion("char1", "v1");
        var v2 = manager.CreateVersion("char1", "v2");
        var branch1 = manager.CreateBranch("char1", v1.Id, "alt1");
        var branch2 = manager.CreateBranch("char1", v1.Id, "alt2");

        var activeBranches = manager.GetActiveBranches("char1");

        Assert.Contains(v1, activeBranches);
        Assert.Single(activeBranches);  // Only v1 has branches
    }

    [Fact]
    public void ValidateVersionHistory_ConsistentTree_NoErrors()
    {
        var v1 = manager.CreateVersion("char1", "v1");
        var v2 = manager.CreateVersion("char1", "v2");
        var branch = manager.CreateBranch("char1", v1.Id, "alt");

        var errors = manager.ValidateVersionHistory("char1");

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateVersionHistory_OrphanedParent_ReportsError()
    {
        var v1 = manager.CreateVersion("char1", "v1");
        var branch = manager.CreateBranch("char1", v1.Id, "alt");

        // Simulate orphaning by manually modifying store
        var versions = store.GetVersions("char1");
        versions[0].ParentVersionId = "nonexistent";
        store.SaveVersion("char1", versions[0]);

        var errors = manager.ValidateVersionHistory("char1");

        Assert.NotEmpty(errors);
        Assert.Contains("non-existent parent", errors[0]);
    }

    [Fact]
    public void CleanupOrphanedVersions_RemovesInvalidBranches()
    {
        var v1 = manager.CreateVersion("char1", "v1");
        var branch = manager.CreateBranch("char1", v1.Id, "alt");

        // Manually create an orphaned entry
        var versions = store.GetVersions("char1");
        versions[0].Branches["orphaned"] = new VersionEntry { Id = "orphaned", Label = "orphan" };
        store.SaveVersion("char1", versions[0]);

        int removed = manager.CleanupOrphanedVersions("char1");

        Assert.Equal(1, removed);
        var v1After = store.GetVersion("char1", v1.Id);
        Assert.False(v1After?.Branches.ContainsKey("orphaned") ?? false);
    }

    [Fact]
    public void GetMilestones_FiltersVersionsByStatus()
    {
        var v1 = manager.CreateVersion("char1", "v1");
        var v2 = manager.CreateVersion("char1", "v2");
        var v3 = manager.CreateVersion("char1", "v3");

        manager.SetMilestone("char1", v1.Id, AssetStatus.Ready);
        manager.SetMilestone("char1", v3.Id, AssetStatus.Review);

        var readyVersions = manager.GetMilestones("char1", AssetStatus.Ready);
        var reviewVersions = manager.GetMilestones("char1", AssetStatus.Review);

        Assert.Single(readyVersions);
        Assert.Equal(v1.Id, readyVersions[0].Id);
        Assert.Single(reviewVersions);
        Assert.Equal(v3.Id, reviewVersions[0].Id);
    }

    [Fact]
    public void VersionEntry_GetAllDescendants_RecursesTree()
    {
        var root = new VersionEntry { Id = "root", Label = "root" };
        var branch1 = root.CreateBranch("b1");
        var branch2 = root.CreateBranch("b2");
        var subbranch = branch1.CreateBranch("b1.1");

        var descendants = root.GetAllDescendants();

        Assert.Equal(3, descendants.Count);
        Assert.Contains(descendants, d => d.Label == "b1");
        Assert.Contains(descendants, d => d.Label == "b2");
        Assert.Contains(descendants, d => d.Label == "b1.1");
    }

    [Fact]
    public void ClearVersionHistory_RemovesAllVersionsForResource()
    {
        manager.CreateVersion("char1", "v1");
        manager.CreateVersion("char1", "v2");

        store.ClearVersionHistory("char1");
        var versions = store.GetVersions("char1");

        Assert.Empty(versions);
    }
}

/// <summary>
/// Mock implementation of IVersionHistoryStore for testing (in-memory, scope-agnostic).
/// </summary>
internal sealed class MockVersionStore : IVersionHistoryStore
{
    private readonly Dictionary<string, List<VersionEntry>> histories = [];
    private readonly Dictionary<string, string?> activeVersions = [];

    public VersionEntry[] GetVersions(string resourceId)
    {
        if (!histories.TryGetValue(resourceId, out var versions))
        {
            return [];
        }
        return [..versions];
    }

    public VersionEntry? GetVersion(string resourceId, string versionId)
    {
        if (!histories.TryGetValue(resourceId, out var versions))
            return null;

        return versions.FirstOrDefault(v => v.Id == versionId);
    }

    public void SaveVersion(string resourceId, VersionEntry entry)
    {
        if (!histories.TryGetValue(resourceId, out var versions))
        {
            versions = [];
            histories[resourceId] = versions;
        }

        var existing = versions.FirstOrDefault(v => v.Id == entry.Id);
        if (existing is not null)
        {
            versions.Remove(existing);
        }

        versions.Add(entry);
    }

    public void SetActiveVersion(string resourceId, string? versionId)
    {
        activeVersions[resourceId] = versionId;
    }

    public string? GetActiveVersion(string resourceId)
    {
        return activeVersions.TryGetValue(resourceId, out var versionId) ? versionId : null;
    }

    public void RevertToVersion(string resourceId, string versionId)
    {
        if (!IsVersionValid(resourceId, versionId))
            throw new InvalidOperationException($"Invalid version {versionId}");

        SetActiveVersion(resourceId, versionId);
    }

    public bool IsVersionValid(string resourceId, string versionId)
    {
        var version = GetVersion(resourceId, versionId);
        if (version is null) return false;

        if (version.ParentVersionId is null) return true;

        return GetVersion(resourceId, version.ParentVersionId) is not null;
    }

    public void ClearVersionHistory(string resourceId)
    {
        histories.Remove(resourceId);
        activeVersions.Remove(resourceId);
    }

    public VersionEntry[] GetVersionsByMilestone(string resourceId, AssetStatus status)
    {
        if (!histories.TryGetValue(resourceId, out var versions))
            return [];

        return versions
            .Where(v => v.MilestoneStatus == status)
            .ToArray();
    }
}

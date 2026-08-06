namespace Lightbox.Core.Versioning;

using Lightbox.Core.Projects;

/// <summary>
/// Core versioning logic: revert, undo, branching, orphan detection, hardening.
/// Scope-agnostic; delegates storage to IVersionHistoryStore implementation.
/// </summary>
public sealed class VersionHistoryManager
{
    private readonly IVersionHistoryStore store;

    public VersionHistoryManager(IVersionHistoryStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Create a new version of a resource. Called on save.
    /// </summary>
    public VersionEntry CreateVersion(string resourceId, string label, string? notes = null)
    {
        var existing = store.GetVersions(resourceId);
        var versionNumber = existing.Length + 1;

        var entry = new VersionEntry
        {
            VersionNumber = versionNumber,
            Label = label,
            Notes = notes,
            CreatedAt = DateTime.UtcNow,
        };

        store.SaveVersion(resourceId, entry);
        store.SetActiveVersion(resourceId, entry.Id);
        return entry;
    }

    /// <summary>
    /// Create a branch from a specific version.
    /// The new branch gets its own version history line, independent of the parent.
    /// </summary>
    public VersionEntry CreateBranch(string resourceId, string fromVersionId, string branchLabel, string? notes = null)
    {
        var fromVersion = store.GetVersion(resourceId, fromVersionId)
            ?? throw new InvalidOperationException($"Cannot branch from non-existent version {fromVersionId}");

        var branch = fromVersion.CreateBranch(branchLabel, notes);
        store.SaveVersion(resourceId, branch);
        store.SetActiveVersion(resourceId, branch.Id);
        return branch;
    }

    /// <summary>
    /// Revert a resource to a previous version.
    /// Validates the version exists and is not orphaned.
    /// Creates an audit entry (new version with "Reverted from X" note).
    /// The revert target becomes active; the audit entry is just recorded.
    /// </summary>
    public void RevertToVersion(string resourceId, string versionId)
    {
        if (!store.IsVersionValid(resourceId, versionId))
            throw new InvalidOperationException($"Cannot revert to invalid or orphaned version {versionId}");

        var targetVersion = store.GetVersion(resourceId, versionId)
            ?? throw new InvalidOperationException($"Version {versionId} not found");

        // Create audit entry (before setting active, so we can reference what we reverted from)
        var currentVersion = store.GetVersions(resourceId).LastOrDefault();
        var auditLabel = $"Reverted to {targetVersion.Label}";
        var auditNotes = currentVersion is not null ? $"Reverted from {currentVersion.Label}" : null;

        var existing = store.GetVersions(resourceId);
        var auditEntry = new VersionEntry
        {
            VersionNumber = existing.Length + 1,
            Label = auditLabel,
            Notes = auditNotes,
            CreatedAt = DateTime.UtcNow,
        };

        store.SaveVersion(resourceId, auditEntry);

        // Set active to the revert target (not the audit entry)
        store.SetActiveVersion(resourceId, versionId);
    }

    /// <summary>
    /// Mark a version with a milestone status (Ready, Review, InDevelopment, etc.).
    /// Used for checkpoints and export filtering.
    /// </summary>
    public void SetMilestone(string resourceId, string versionId, AssetStatus status)
    {
        var version = store.GetVersion(resourceId, versionId)
            ?? throw new InvalidOperationException($"Version {versionId} not found");

        version.MilestoneStatus = status;
        store.SaveVersion(resourceId, version);
    }

    /// <summary>
    /// Get all versions in order (main line + branches).
    /// </summary>
    public VersionEntry[] GetAllVersions(string resourceId)
    {
        return store.GetVersions(resourceId);
    }

    /// <summary>
    /// Get the currently active version (null = working copy).
    /// </summary>
    public string? GetActiveVersionId(string resourceId)
    {
        return store.GetActiveVersion(resourceId);
    }

    /// <summary>
    /// Check if a version is orphaned (unreachable in the tree).
    /// A version is orphaned if its parent no longer exists.
    /// </summary>
    public bool IsVersionOrphaned(string resourceId, string versionId)
    {
        var version = store.GetVersion(resourceId, versionId);
        if (version is null) return true;

        if (version.ParentVersionId is null) return false;  // Main line versions are never orphaned

        var parent = store.GetVersion(resourceId, version.ParentVersionId);
        return parent is null;  // Orphaned if parent doesn't exist
    }

    /// <summary>
    /// Get all active branches (versions that have diverged and are still being developed).
    /// Used for UI: "how many character variants exist for hero?".
    /// </summary>
    public VersionEntry[] GetActiveBranches(string resourceId)
    {
        var versions = store.GetVersions(resourceId);
        return versions.Where(v => v.Branches.Count > 0).ToArray();
    }

    /// <summary>
    /// Get all versions marked with a specific milestone.
    /// </summary>
    public VersionEntry[] GetMilestones(string resourceId, AssetStatus status)
    {
        return store.GetVersionsByMilestone(resourceId, status);
    }

    /// <summary>
    /// Validate the entire version history tree for orphans and inconsistencies.
    /// Returns list of errors; empty if valid.
    /// </summary>
    public List<string> ValidateVersionHistory(string resourceId)
    {
        var errors = new List<string>();
        var versions = store.GetVersions(resourceId);
        var versionMap = versions.ToDictionary(v => v.Id);

        foreach (var version in versions)
        {
            // Check parent exists
            if (version.ParentVersionId is not null && !versionMap.ContainsKey(version.ParentVersionId))
                errors.Add($"Version {version.Label} references non-existent parent {version.ParentVersionId}");

            // Check branches point to valid versions
            var descendants = version.GetAllDescendants();
            foreach (var desc in descendants)
            {
                if (!versionMap.ContainsKey(desc.Id))
                    errors.Add($"Branch {desc.Label} is unreachable from main line");
            }
        }

        return errors;
    }

    /// <summary>
    /// Clean up version history: remove orphaned branches and consolidate.
    /// Returns count of orphans removed.
    /// </summary>
    public int CleanupOrphanedVersions(string resourceId)
    {
        var versions = store.GetVersions(resourceId);
        var versionMap = versions.ToDictionary(v => v.Id);
        int removed = 0;

        foreach (var version in versions)
        {
            var branchesToRemove = new List<string>();
            foreach (var (branchId, branch) in version.Branches)
            {
                if (!versionMap.ContainsKey(branchId))
                {
                    branchesToRemove.Add(branchId);
                    removed++;
                }
            }

            foreach (var branchId in branchesToRemove)
            {
                version.Branches.Remove(branchId);
            }
        }

        if (removed > 0)
        {
            foreach (var version in versions)
            {
                store.SaveVersion(resourceId, version);
            }
        }

        return removed;
    }
}

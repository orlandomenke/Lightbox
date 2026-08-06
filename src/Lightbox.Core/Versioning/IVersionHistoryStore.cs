namespace Lightbox.Core.Versioning;

using Lightbox.Core.Projects;

/// <summary>
/// Scope-agnostic abstraction for version history storage.
/// Implementations can store in ProjectManifest, Folder objects, a central vault, or elsewhere.
/// Versioning logic depends only on this interface, allowing storage to evolve with scope redesign.
/// </summary>
public interface IVersionHistoryStore
{
    /// <summary>
    /// Get all versions for a resource (by ID), in creation order.
    /// Returns empty array if resource has no version history.
    /// </summary>
    VersionEntry[] GetVersions(string resourceId);

    /// <summary>
    /// Get a specific version by ID.
    /// Returns null if version does not exist.
    /// </summary>
    VersionEntry? GetVersion(string resourceId, string versionId);

    /// <summary>
    /// Save a new version entry. Called after resource save or branch creation.
    /// Increments VersionNumber relative to existing versions.
    /// </summary>
    void SaveVersion(string resourceId, VersionEntry entry);

    /// <summary>
    /// Set the "active version" for a resource (which version is currently active).
    /// Pass null to mark as "working copy" (unsaved/current).
    /// </summary>
    void SetActiveVersion(string resourceId, string? versionId);

    /// <summary>
    /// Get the currently active version ID for a resource.
    /// Returns null if no version is pinned (working copy).
    /// </summary>
    string? GetActiveVersion(string resourceId);

    /// <summary>
    /// Revert resource to a specific version by ID.
    /// Validates that version exists and is not orphaned before reverting.
    /// Creates an audit entry (new version with "Reverted from X" note).
    /// Throws InvalidOperationException if version is invalid.
    /// </summary>
    void RevertToVersion(string resourceId, string versionId);

    /// <summary>
    /// Check if a version exists and is valid (not orphaned).
    /// A version is orphaned if it references a non-existent parent or is unreachable in the tree.
    /// </summary>
    bool IsVersionValid(string resourceId, string versionId);

    /// <summary>
    /// Delete all version history for a resource (cleanup when resource is deleted).
    /// </summary>
    void ClearVersionHistory(string resourceId);

    /// <summary>
    /// Get all versions that are marked with a specific milestone status.
    /// Used for UI queries: "show me all Ready versions" or "list Review milestones".
    /// </summary>
    VersionEntry[] GetVersionsByMilestone(string resourceId, AssetStatus status);
}

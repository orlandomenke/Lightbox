namespace Lightbox.Core.Versioning;

using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

/// <summary>
/// Immutable version metadata for a resource (palette, character, brush tip, symbol).
/// Stored independently from the resource content itself.
/// </summary>
public sealed class VersionEntry
{
    /// <summary>Unique version entry ID.</summary>
    public string Id { get; set; } = Ids.NewId("ventry");

    /// <summary>Monotonically increasing version number (1, 2, 3, ...).</summary>
    public int VersionNumber { get; set; }

    /// <summary>When this version was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Human-readable label: "v1", "hero_final", "review_pass_1".
    /// Does not have to be unique within a resource's version history.
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>Optional notes: animator comments, frame ranges, review feedback.</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// For branching: ID of the version this was branched from (null = main line or initial version).
    /// </summary>
    public string? ParentVersionId { get; set; }

    /// <summary>
    /// Lightweight branch tree: branched-from-this-version → VersionEntry.
    /// Allows querying "what branches exist from v1?" without walking full history.
    /// </summary>
    public Dictionary<string, VersionEntry> Branches { get; set; } = [];

    /// <summary>
    /// Milestone status: Design, Draft, InDevelopment, Review, Ready, Reopened.
    /// Null means unmarked. Used for filtering exports and tracking checkpoints.
    /// </summary>
    public AssetStatus? MilestoneStatus { get; set; }

    /// <summary>
    /// Create a branch from this version. Returns the new branch entry.
    /// </summary>
    public VersionEntry CreateBranch(string label, string? notes = null)
    {
        var branch = new VersionEntry
        {
            VersionNumber = 0,  // Branches start at 0; main line increments
            Label = label,
            Notes = notes,
            ParentVersionId = Id,
        };
        Branches[branch.Id] = branch;
        return branch;
    }

    /// <summary>
    /// Recursively collect all versions in this version's branch tree (depth-first).
    /// Used for validation (e.g., checking if version exists anywhere in the tree).
    /// </summary>
    public List<VersionEntry> GetAllDescendants()
    {
        var result = new List<VersionEntry>();
        foreach (var branch in Branches.Values)
        {
            result.Add(branch);
            result.AddRange(branch.GetAllDescendants());
        }
        return result;
    }
}

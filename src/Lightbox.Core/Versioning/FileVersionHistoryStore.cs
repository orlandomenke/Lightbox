namespace Lightbox.Core.Versioning;

using System.Text.Json;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;

/// <summary>
/// The version history on disk: one directory per resource under the
/// project's <c>versions/</c> root, holding a <c>history.json</c> beside the
/// content files <see cref="ProjectVersions"/> copies in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every read serves the cached instance, and that is a contract rather
/// than an optimisation.</b> <see cref="VersionHistoryManager"/> was written
/// against an in-memory store, so it mutates what <see cref="GetVersion"/>
/// returns — <c>CreateBranch</c> adds to the parent's
/// <see cref="VersionEntry.Branches"/> and then saves only the child. A store
/// that deserialized fresh copies per call would silently drop those edits;
/// this one loads a resource's history once and rewrites the whole file on
/// every mutation, so reference semantics hold for as long as the store
/// instance lives.
/// </para>
/// <para>
/// The flat entry list is the truth and <see cref="VersionEntry.Branches"/>
/// serializes redundantly inside it. On reload the nested copy and the flat
/// copy are distinct instances — every consumer in the manager compares by
/// id, which is why that is survivable. Nothing should hand out a nested
/// instance as if it were the flat one.
/// </para>
/// <para>
/// A history file that is missing reads as an empty history rather than an
/// error — the same rule the raster path applies to a missing baseline
/// (B137): degraded is a state, broken is a bug.
/// </para>
/// </remarks>
public sealed class FileVersionHistoryStore : IVersionHistoryStore
{
    private const string HistoryFile = "history.json";

    private readonly string _root;
    private readonly Dictionary<string, ResourceHistory> _cache = [];

    /// <param name="root">
    /// The versions root itself (<c>&lt;project&gt;/versions</c>), not the
    /// project root — the store neither knows nor cares what it sits under.
    /// </param>
    public FileVersionHistoryStore(string root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
    }

    /// <summary>Where a resource's history and content files live.</summary>
    public string DirectoryOf(string resourceId) => Path.Combine(_root, resourceId);

    public VersionEntry[] GetVersions(string resourceId) =>
        [.. HistoryOf(resourceId).Entries];

    public VersionEntry? GetVersion(string resourceId, string versionId) =>
        HistoryOf(resourceId).Entries.FirstOrDefault(v => v.Id == versionId);

    public void SaveVersion(string resourceId, VersionEntry entry)
    {
        var history = HistoryOf(resourceId);
        var existing = history.Entries.FirstOrDefault(v => v.Id == entry.Id);
        if (existing is not null) history.Entries.Remove(existing);
        history.Entries.Add(entry);
        Write(resourceId, history);
    }

    public void SetActiveVersion(string resourceId, string? versionId)
    {
        var history = HistoryOf(resourceId);
        history.ActiveVersionId = versionId;
        Write(resourceId, history);
    }

    public string? GetActiveVersion(string resourceId) =>
        HistoryOf(resourceId).ActiveVersionId;

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
        _cache.Remove(resourceId);
        var dir = DirectoryOf(resourceId);
        // The whole directory, content files included: history without content
        // is a record, but content without history is unreachable bytes.
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    public VersionEntry[] GetVersionsByMilestone(string resourceId, AssetStatus status) =>
        HistoryOf(resourceId).Entries.Where(v => v.MilestoneStatus == status).ToArray();

    // ---- the file ----------------------------------------------------------

    private sealed class ResourceHistory
    {
        public string? ActiveVersionId { get; set; }

        public List<VersionEntry> Entries { get; set; } = [];
    }

    private ResourceHistory HistoryOf(string resourceId)
    {
        if (_cache.TryGetValue(resourceId, out var cached)) return cached;

        var path = Path.Combine(DirectoryOf(resourceId), HistoryFile);
        ResourceHistory history;
        if (File.Exists(path))
        {
            history = JsonSerializer.Deserialize<ResourceHistory>(
                File.ReadAllText(path), DocJson.Options) ?? new ResourceHistory();
        }
        else
        {
            history = new ResourceHistory();
        }

        _cache[resourceId] = history;
        return history;
    }

    private void Write(string resourceId, ResourceHistory history) =>
        DocJson.WriteAtomic(
            Path.Combine(DirectoryOf(resourceId), HistoryFile),
            JsonSerializer.Serialize(history, DocJson.Options));
}

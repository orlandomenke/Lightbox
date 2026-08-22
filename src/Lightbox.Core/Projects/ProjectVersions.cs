namespace Lightbox.Core.Projects;

using Lightbox.Core.Versioning;

/// <summary>
/// What a surface listing many resources wants to know about one resource's
/// history without opening it: how many versions are kept, the latest
/// milestone among them, and whether the file has changed since that
/// milestone was kept.
/// </summary>
/// <param name="Milestone">
/// The status of the newest milestone-tagged version, or null when no
/// promotion has ever kept one. The newest rather than the highest-ranked:
/// a Ready followed by a Review re-promotion means the Review bytes are the
/// ones the pipeline is currently standing on.
/// </param>
/// <param name="ChangedSinceMilestone">
/// True when the file on disk no longer matches the milestone's kept bytes —
/// the fact a studio schedules against, because "Ready" on a row whose file
/// has moved on is a claim about the history, not the file. False when there
/// is no milestone, or its content is gone and no comparison can be honest.
/// </param>
public sealed record VersionFacts(int Count, AssetStatus? Milestone, bool ChangedSinceMilestone);

/// <summary>
/// Authored version history for the files a project owns — documents and
/// character sheets. The metadata half is <see cref="FileVersionHistoryStore"/>;
/// this is the content half: each version keeps a byte-for-byte copy of the
/// file as it was saved, under <c>versions/&lt;resourceId&gt;/</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A version is a copy of the saved file, not a serialization of the open
/// document.</b> That keeps the mechanism uniform across everything with a
/// file — a gzipped document and a plain-JSON sheet version identically — and
/// it means the caller's obligation is exactly one thing: save first. What is
/// on disk is what is versioned.
/// </para>
/// <para>
/// <b>Two kinds of entry share the history, and only one has bytes.</b> An
/// authored or milestone version carries a content file; the audit entry
/// <see cref="VersionHistoryManager.RevertToVersion"/> writes ("Reverted to
/// …") is a record of the act and carries none. <see cref="ContentPathOf"/>
/// answering null is how a reader tells them apart, and a record-only entry
/// can never be reverted to — there is nothing to restore.
/// </para>
/// <para>
/// <b>Revert can never lose work</b>: it versions the current state first
/// (the safety copy), so the state being replaced is itself one revert away.
/// </para>
/// </remarks>
public static class ProjectVersions
{
    /// <summary>
    /// The one directory versions live under, beside the manifest. In
    /// <see cref="ProjectIo.SystemFolders"/> so B83 does not report it as a
    /// stranger; keyed by resource id so a B188 re-filing moves nothing here.
    /// </summary>
    public const string Dir = "versions";

    public static string RootOf(Project project) => Path.Combine(project.Root, Dir);

    /// <summary>
    /// A store over this project's versions. Fresh per operation on purpose:
    /// the store caches per instance, and a long-lived one would go stale if a
    /// second window wrote the same history.
    /// </summary>
    public static FileVersionHistoryStore StoreFor(Project project) => new(RootOf(project));

    /// <summary>Whether anything under this project has ever been versioned.</summary>
    public static bool AnyExist(Project project) => Directory.Exists(RootOf(project));

    /// <summary>
    /// Version the file at <paramref name="relativePath"/> as it stands on
    /// disk. The caller saves first; an unsaved buffer is not a version of
    /// anything.
    /// </summary>
    /// <param name="resourceId">
    /// The manifest's stable id (<see cref="DocumentRef.Id"/> /
    /// <see cref="SheetRef.Id"/>) — stable across renames and re-filing,
    /// which paths are not.
    /// </param>
    /// <param name="milestone">
    /// Set when this version marks a status promotion (Review, Ready), so the
    /// history can answer "which bytes were the Ready ones".
    /// </param>
    public static VersionEntry SaveVersion(
        Project project, string resourceId, string relativePath,
        string label, string? notes = null, AssetStatus? milestone = null)
    {
        var source = Path.Combine(project.Root, relativePath);
        if (!File.Exists(source))
            throw new InvalidOperationException(
                $"Nothing at {relativePath} to version — save the file first.");

        var store = StoreFor(project);
        var manager = new VersionHistoryManager(store);

        // Content lands under a temporary name before the entry exists, so a
        // full disk fails here — with the history untouched — rather than
        // after the metadata says there is a version.
        var directory = store.DirectoryOf(resourceId);
        Directory.CreateDirectory(directory);
        var staged = Path.Combine(directory, Documents.Ids.NewId("staging") + ".tmp");
        File.Copy(source, staged);

        try
        {
            var entry = manager.CreateVersion(resourceId, label, notes);
            if (milestone is { } status) manager.SetMilestone(resourceId, entry.Id, status);
            File.Move(staged, Path.Combine(directory, entry.Id + SuffixOf(source)), overwrite: true);
            return entry;
        }
        catch
        {
            File.Delete(staged);
            throw;
        }
    }

    /// <summary>
    /// The stored bytes for a version, or null for a record-only entry (an
    /// audit line, or content lost outside the app). Null is a state the UI
    /// shows, not an error.
    /// </summary>
    public static string? ContentPathOf(Project project, string resourceId, string versionId)
    {
        var directory = StoreFor(project).DirectoryOf(resourceId);
        if (!Directory.Exists(directory)) return null;
        return Directory.GetFiles(directory, versionId + ".*").FirstOrDefault();
    }

    /// <summary>
    /// Put a version's bytes back as the file at <paramref name="relativePath"/>.
    /// The current state is versioned first, so this is always undoable by a
    /// second revert; the framework's audit entry records the act in the
    /// history itself.
    /// </summary>
    /// <returns>The entry reverted to.</returns>
    public static VersionEntry Revert(
        Project project, string resourceId, string versionId, string relativePath)
    {
        var content = ContentPathOf(project, resourceId, versionId)
            ?? throw new InvalidOperationException(
                "That entry records an action rather than a state — there are no bytes to restore.");

        var target = StoreFor(project).GetVersion(resourceId, versionId)
            ?? throw new InvalidOperationException($"Version {versionId} not found");

        // The safety copy goes in BEFORE the store that performs the revert is
        // opened. Each store instance caches the history it loaded; opening
        // the reverting store first left it blind to the safety entry, and its
        // write-back erased it — the failure mode its own remarks warn about.
        var current = Path.Combine(project.Root, relativePath);
        if (File.Exists(current))
            SaveVersion(project, resourceId, relativePath, $"Before revert to {target.Label}");

        new VersionHistoryManager(StoreFor(project)).RevertToVersion(resourceId, versionId);

        // Atomically, the way every project write goes: the half-copied file a
        // crash leaves behind must never be the document.
        var temp = current + ".tmp";
        File.Copy(content, temp, overwrite: true);
        File.Move(temp, current, overwrite: true);
        return target;
    }

    /// <summary>
    /// Every resource id with any history under this project, in one
    /// directory listing. A surface building a row per resource asks this
    /// once rather than probing per row — a project that never versions
    /// answers empty from a single <c>Exists</c> check.
    /// </summary>
    public static IReadOnlyList<string> VersionedResourceIds(Project project)
    {
        var root = RootOf(project);
        if (!Directory.Exists(root)) return [];
        return [.. Directory.GetDirectories(root).Select(Path.GetFileName).OfType<string>()];
    }

    /// <summary>
    /// The listing-level facts for one resource: version count, latest
    /// milestone, and whether the file has drifted from that milestone's
    /// bytes. Reads the resource's history and at most one kept copy.
    /// </summary>
    public static VersionFacts FactsFor(Project project, string resourceId, string relativePath)
    {
        var entries = StoreFor(project).GetVersions(resourceId);
        var milestone = entries
            .Where(e => e.MilestoneStatus is not null)
            .OrderByDescending(e => e.VersionNumber)
            .FirstOrDefault();

        var changed = false;
        if (milestone is not null
            && ContentPathOf(project, resourceId, milestone.Id) is { } kept)
        {
            var current = Path.Combine(project.Root, relativePath);
            changed = File.Exists(current) && !SameBytes(current, kept);
        }
        return new VersionFacts(entries.Length, milestone?.MilestoneStatus, changed);
    }

    /// <summary>
    /// Byte equality, length first. The kept copy was made with
    /// <see cref="File.Copy(string, string)"/> from the file itself, so an
    /// untouched file matches exactly; a re-save that rewrote the bytes reads
    /// as changed, which is the honest answer about a file that was written.
    /// </summary>
    private static bool SameBytes(string a, string b)
    {
        if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }

    /// <summary>
    /// Forget a resource's history and content, for when the resource itself
    /// is deleted. Deliberate data loss, so it is its own verb rather than a
    /// side effect anything reaches by accident.
    /// </summary>
    public static void ClearHistory(Project project, string resourceId) =>
        StoreFor(project).ClearVersionHistory(resourceId);

    /// <summary>
    /// The full extension chain — <c>.lightbox.json</c>, <c>.sheet.json</c> —
    /// not <see cref="Path.GetExtension"/>'s last segment, so a restored file
    /// keeps the shape its readers sniff for.
    /// </summary>
    private static string SuffixOf(string path)
    {
        var name = Path.GetFileName(path);
        var dot = name.IndexOf('.');
        return dot < 0 ? "" : name[dot..];
    }
}

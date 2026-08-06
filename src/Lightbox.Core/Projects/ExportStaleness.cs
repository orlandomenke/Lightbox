namespace Lightbox.Core.Projects;

/// <summary>What an artifact was built from, so it can tell whether it still matches.</summary>
/// <remarks>
/// <b>Pillar 3's S7 pattern, second application.</b> <c>SymbolPlacement</c>
/// records the <c>Symbol.Version</c> it was placed against; this records the
/// <see cref="DocumentRef.Version"/> of every document that went into a
/// deliverable. Staleness is the comparison, not a stored flag — a flag would
/// need clearing correctly in every path, and a comparison cannot go wrong.
/// </remarks>
public sealed class ExportRecord
{
    /// <summary>The scope this was built for, or null for a loose document.</summary>
    public string? ScopeId { get; set; }

    public string PresetId { get; set; } = "";

    /// <summary>Document id → the version the artifact was built from.</summary>
    public Dictionary<string, int> SeenVersions { get; set; } = [];

    /// <summary>Where it was written, for a report to point at.</summary>
    public string? Path { get; set; }
}

/// <summary>Whether a deliverable still matches the work behind it.</summary>
public static class ExportStaleness
{
    /// <summary>
    /// The documents that have moved since this artifact was written — saved,
    /// added to the scope, or removed from it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three kinds of drift and all three matter. A document that was
    /// <b>edited</b> makes the artifact out of date; one <b>added</b> to the
    /// scope is missing from it; one <b>removed</b> is in it and should not be.
    /// Reporting only the first is the version of this that looks right and
    /// quietly ships a sheet with a deleted animation still in it.
    /// </para>
    /// <para>
    /// This is also the honest answer to the Reopened case. A document that
    /// drops out of the status filter shows up here as removed, so the artifact
    /// reads stale and <b>keeps what it had</b> — rather than being regenerated
    /// with a hole in it while an engine is loading it.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Drifted(ExportRecord record, ExportArtifact now)
    {
        var current = now.Documents.ToDictionary(d => d.Id, d => d.Version, StringComparer.Ordinal);
        var moved = new List<string>();

        foreach (var (id, seen) in record.SeenVersions)
        {
            if (!current.TryGetValue(id, out var version)) moved.Add(id);      // removed
            else if (version != seen) moved.Add(id);                            // edited
        }
        moved.AddRange(current.Keys.Where(id => !record.SeenVersions.ContainsKey(id))); // added
        return moved;
    }

    /// <summary>Whether anything moved at all.</summary>
    public static bool IsStale(ExportRecord record, ExportArtifact now) =>
        Drifted(record, now).Count > 0;

    /// <summary>Record what an artifact was built from, at the moment it is written.</summary>
    public static ExportRecord RecordOf(ExportArtifact artifact, string? path = null) => new()
    {
        ScopeId = artifact.Scope?.Id,
        PresetId = artifact.PresetId,
        Path = path,
        SeenVersions = artifact.Documents.ToDictionary(d => d.Id, d => d.Version, StringComparer.Ordinal),
    };
}

/// <summary>Which artifacts a status change asks to be rebuilt.</summary>
/// <remarks>
/// <para>
/// Q30 step 4. The trigger is per document — one animation reaches
/// <c>Ready</c> — and the effect is per artifact, because the deliverable
/// holding it is what actually has to change. That mapping is exactly what
/// <see cref="ExportScopes.BoundaryFor"/> exists to answer, and it is why
/// grouping is worth having rather than being a packaging detail.
/// </para>
/// <para>
/// Deciding is separate from doing, the same way <c>AutoExport.Decide</c>
/// already separates them: this names what should be rebuilt and writes nothing.
/// </para>
/// </remarks>
public static class ExportTriggers
{
    /// <summary>
    /// The artifacts that <paramref name="document"/> reaching
    /// <paramref name="status"/> asks to be rebuilt.
    /// </summary>
    /// <remarks>
    /// Empty when no preset above the document names this status, which is the
    /// ordinary case — a trigger is opt-in, and a project that declares none
    /// exports only when an artist says so.
    /// </remarks>
    public static IReadOnlyList<ExportArtifact> Fired(
        ProjectManifest manifest,
        DocumentRef document,
        AssetStatus status,
        Func<string, ExportPreset?> preset)
    {
        if (ExportScopes.PresetFor(manifest, document) is not { } id) return [];
        if (preset(id) is not { RebuildOn: { } wanted } || wanted != status) return [];

        var boundary = ExportScopes.BoundaryFor(manifest, document);
        // The whole artifact, not the one document — a sheet is rebuilt entire,
        // and the sibling frames have not changed but they are still in it.
        return ExportPlan.For(manifest, boundary, preset);
    }
}

namespace Lightbox.Core.Projects;

/// <summary>One deliverable: which documents, under which preset.</summary>
/// <param name="Scope">
/// The folder whose declaration made this an artifact, or null for the project.
/// </param>
/// <param name="PresetId">The preset that governs it.</param>
/// <param name="Documents">
/// What goes in, in tree order, already filtered by the preset's statuses.
/// </param>
/// <param name="Excluded">
/// What was in the scope and did not qualify, so the report can say so.
/// </param>
public sealed record ExportArtifact(
    ProjectFolder? Scope,
    string PresetId,
    IReadOnlyList<DocumentRef> Documents,
    IReadOnlyList<DocumentRef> Excluded)
{
    /// <summary>Nothing qualified, so running this would write an empty file.</summary>
    /// <remarks>
    /// Worth naming rather than filtering out silently. A scope where everything
    /// is still Draft is a legitimate state and the artist should be told it
    /// produced nothing, not left wondering why the sheet is missing.
    /// </remarks>
    public bool IsEmpty => Documents.Count == 0;
}

/// <summary>
/// Working out what an export would produce, before anything is written.
/// </summary>
/// <remarks>
/// <para>
/// Q30 step 3. <c>ExportRunner</c> already turns <em>a preset and a path</em>
/// into files and is deliberately thin; what did not exist was the step before
/// it — <em>given this folder, what artifacts are there and what goes in each</em>.
/// That is selection and filtering, with no rendering and no file system in it,
/// which is why it lives here and can be asserted without writing a byte.
/// </para>
/// <para>
/// <b>Separating the plan from the run is what makes a confirmation possible.</b>
/// "3 folders, 47 documents, 4 sheets" is a plan being read aloud, and the
/// decision to put the count in the confirm rather than a yes/no needs something
/// to count before the export starts.
/// </para>
/// </remarks>
public static class ExportPlan
{
    /// <summary>
    /// Every artifact produced by exporting <paramref name="selection"/> —
    /// null meaning the whole project.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Recursive, because that is what "export the knight" means.</b> The
    /// safeguard is the count this returns rather than a checkbox, which is the
    /// same argument B87's delete confirmation already makes.
    /// </para>
    /// <para>
    /// A project that declares no export scope produces one artifact per
    /// document, which is today's behaviour reached by the same code path rather
    /// than by a branch around it.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ExportArtifact> For(
        ProjectManifest manifest,
        ProjectFolder? selection,
        Func<string, ExportPreset?> preset)
    {
        var inScope = DocumentsUnder(manifest, selection).ToList();
        var artifacts = new List<ExportArtifact>();

        // Group by the boundary that governs each document, so one pass answers
        // both "which artifact" and "what is in it".
        foreach (var group in inScope.GroupBy(d => ExportScopes.BoundaryFor(manifest, d)))
        {
            var id = ExportScopes.PresetFor(manifest, group.First());
            if (id is null) continue;
            var found = preset(id);

            foreach (var artifact in Split(group.Key, id, found, group.ToList()))
            {
                artifacts.Add(artifact);
            }
        }

        // Documents the project does not scope at all still export one by one —
        // the migration path, and the only behaviour an old project has.
        foreach (var loose in inScope.Where(d => ExportScopes.PresetFor(manifest, d) is null))
        {
            artifacts.Add(new ExportArtifact(null, "", [loose], []));
        }

        return artifacts;
    }

    /// <summary>Apply the preset's grouping and status filter to one boundary.</summary>
    private static IEnumerable<ExportArtifact> Split(
        ProjectFolder? scope, string presetId, ExportPreset? preset, List<DocumentRef> documents)
    {
        var grouping = preset?.Grouping ?? ExportGrouping.PerDocument;
        var admits = (DocumentRef d) => preset?.Admits(d.Status) ?? true;

        switch (grouping)
        {
            case ExportGrouping.OneArtifact:
                yield return Build(scope, presetId, documents, admits);
                break;

            case ExportGrouping.PerChildFolder:
                // Shared settings, separate deliverables — one declaration on
                // characters/ giving every character its own sheet.
                foreach (var byChild in documents.GroupBy(d => d.FolderId))
                {
                    yield return Build(null, presetId, byChild.ToList(), admits);
                }
                break;

            default:
                foreach (var one in documents.Where(d => admits(d)))
                {
                    yield return new ExportArtifact(scope, presetId, [one], []);
                }
                break;
        }
    }

    private static ExportArtifact Build(
        ProjectFolder? scope, string presetId,
        List<DocumentRef> documents, Func<DocumentRef, bool> admits) =>
        new(scope, presetId,
            documents.Where(admits).ToList(),
            documents.Where(d => !admits(d)).ToList());

    /// <summary>Every document at or under a folder, or the whole project for null.</summary>
    private static IEnumerable<DocumentRef> DocumentsUnder(
        ProjectManifest manifest, ProjectFolder? selection)
    {
        if (selection is null) return manifest.Documents;
        var ids = ProjectFolders.Descendants(manifest, selection)
            .Select(f => f.Id)
            .Append(selection.Id)
            .ToHashSet(StringComparer.Ordinal);
        return manifest.Documents.Where(d => d.FolderId is { } f && ids.Contains(f));
    }

    /// <summary>What the confirmation says, so nobody has to count it twice.</summary>
    public static string Describe(IReadOnlyList<ExportArtifact> artifacts)
    {
        var documents = artifacts.Sum(a => a.Documents.Count);
        var excluded = artifacts.Sum(a => a.Excluded.Count);
        var text = $"{Count(artifacts.Count, "file")} from {Count(documents, "document")}";
        // Named, not hidden: a scope where most things are still Draft is
        // legitimate, and an artist who is not told will read the small number
        // as a bug.
        if (excluded > 0) text += $", {excluded} held back by status";
        var empty = artifacts.Count(a => a.IsEmpty);
        if (empty > 0) text += $", {empty} of them empty";
        return text;
    }

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";
}

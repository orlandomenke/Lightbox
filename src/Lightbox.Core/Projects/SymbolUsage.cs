using Lightbox.Core.Documents;

namespace Lightbox.Core.Projects;

/// <summary>One document that places a symbol, and how many times.</summary>
/// <param name="Document">The animation, shot or standalone document.</param>
/// <param name="Owner">The character it belongs to, or null for a loose document.</param>
/// <param name="Placements">How many placements of the symbol it holds, across every frame and layer.</param>
public sealed record SymbolUse(DocumentRef Document, string? Owner, int Placements);

/// <summary>Everywhere in a project one symbol is placed.</summary>
public sealed record SymbolUsage(string SymbolId, IReadOnlyList<SymbolUse> Documents)
{
    public int Total => Documents.Sum(d => d.Placements);

    /// <summary>Nothing in the project places this. Safe to delete.</summary>
    public bool IsUnused => Documents.Count == 0;
}

/// <summary>
/// Which documents place which symbols — the answer to "what breaks if I
/// change this?", and the one question the project's own file layout is
/// arranged to avoid asking.
/// </summary>
/// <remarks>
/// <para>
/// A project is a folder precisely so that opening a character with forty
/// animations reads one manifest instead of forty documents. This walks all
/// forty. That is not a flaw in it — it is the reason it must stay an
/// <b>explicit action</b>, asked for by name, and must never be called on
/// load, on save, or to refresh a panel.
/// </para>
/// <para>
/// What it makes possible is a delete that knows what it is deleting. Removing
/// a symbol leaves its placements alone by design — they stop drawing and are
/// reported — because the alternative is a delete that quietly edits forty
/// animations. But "leaves them alone" is only defensible if the artist is
/// told how many there are first, and until now nothing could count them.
/// </para>
/// <para>
/// Scope: symbol → document. Symbol → symbol edges do not exist, because
/// symbols cannot contain other symbols — the loader refuses the nesting.
/// When that changes this is where the second kind of edge belongs.
/// </para>
/// </remarks>
public static class SymbolGraph
{
    /// <summary>
    /// Count placements of every symbol across every document in the project.
    /// </summary>
    /// <remarks>
    /// Documents already loaded are read from the project's cache; the rest are
    /// read from disk and join it, so a second call is free and the documents
    /// stay available to whatever the artist does next.
    /// </remarks>
    /// <param name="load">
    /// How to fetch a document that is not loaded yet. Injected rather than
    /// called directly so this stays testable without a folder on disk, and so
    /// a caller can decline to touch the filesystem by passing one that only
    /// answers from the cache.
    /// </param>
    public static IReadOnlyDictionary<string, SymbolUsage> Usage(
        Project project, Func<Project, DocumentRef, Doc?> load)
    {
        var counts = new Dictionary<string, Dictionary<string, int>>();
        var refs = new Dictionary<string, DocumentRef>();

        foreach (var reference in project.AllDocuments)
        {
            refs[reference.Id] = reference;
            if (load(project, reference) is not { } doc) continue;

            foreach (var (symbolId, n) in PlacementsIn(doc))
            {
                var perDoc = counts.TryGetValue(symbolId, out var existing)
                    ? existing
                    : counts[symbolId] = [];
                perDoc[reference.Id] = perDoc.GetValueOrDefault(reference.Id) + n;
            }
        }

        var result = new Dictionary<string, SymbolUsage>();
        // Every symbol in the project gets an entry, including the ones nothing
        // places. "Used nowhere" is the answer that makes a delete safe, and it
        // is not the same answer as "not in this dictionary".
        foreach (var symbolId in project.Symbols.Keys)
        {
            result[symbolId] = Build(symbolId, counts, refs, project);
        }
        // And any symbol placed but no longer in the project — a placement left
        // behind by a delete. Reporting those is how they get found.
        foreach (var symbolId in counts.Keys)
        {
            if (!result.ContainsKey(symbolId)) result[symbolId] = Build(symbolId, counts, refs, project);
        }
        return result;
    }

    /// <summary>Usage of every symbol, reading documents from disk as needed.</summary>
    public static IReadOnlyDictionary<string, SymbolUsage> Usage(Project project) =>
        Usage(project, ProjectIo.LoadDocument);

    /// <summary>Where one symbol is placed.</summary>
    public static SymbolUsage Of(Project project, string symbolId) =>
        Usage(project).TryGetValue(symbolId, out var usage)
            ? usage
            : new SymbolUsage(symbolId, []);

    private static SymbolUsage Build(
        string symbolId,
        Dictionary<string, Dictionary<string, int>> counts,
        Dictionary<string, DocumentRef> refs,
        Project project)
    {
        if (!counts.TryGetValue(symbolId, out var perDoc)) return new SymbolUsage(symbolId, []);
        var uses = perDoc
            .Where(kv => refs.ContainsKey(kv.Key))
            // The folder is the owner now (B114). It used to be the character,
            // which meant a symbol placed in a loose document reported no owner
            // at all — the same blind spot that made the bug worth filing.
            .Select(kv => new SymbolUse(
                refs[kv.Key],
                ProjectFolders.ById(project.Manifest, refs[kv.Key].FolderId)?.Name,
                kv.Value))
            .OrderByDescending(u => u.Placements)
            .ThenBy(u => u.Document.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new SymbolUsage(symbolId, uses);
    }

    /// <summary>symbolId → placement count, over every frame of every layer.</summary>
    private static Dictionary<string, int> PlacementsIn(Doc doc)
    {
        var counts = new Dictionary<string, int>();
        // Reference sheets carry frames of their own and an artist can place a
        // symbol on one, so they are walked alongside the scene — the same
        // pairing Flatten uses, and for the same reason.
        var frames = doc.Scene.Layers
            .SelectMany(l => l.Cels)
            .Select(c => c.Frame)
            .Concat(doc.ReferenceSheets
                .SelectMany(s => s.Views)
                .SelectMany(v => v.Layers)
                .SelectMany(l => l.Cels)
                .Select(c => c.Frame))
            .OfType<PaintedFrame>();

        var seen = new HashSet<string>();
        foreach (var frame in frames)
        {
            // A held cel is one drawing exposed at several indices and appears
            // in the list once per exposure. Counting it each time would report
            // a symbol placed once on a four-frame hold as placed four times.
            if (!seen.Add(frame.Id)) continue;
            if (frame.Placements is not { Count: > 0 } placements) continue;
            foreach (var placement in placements)
            {
                counts[placement.SymbolId] = counts.GetValueOrDefault(placement.SymbolId) + 1;
            }
        }
        return counts;
    }
}

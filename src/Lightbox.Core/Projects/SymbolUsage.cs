using Lightbox.Core.Documents;

namespace Lightbox.Core.Projects;

/// <summary>One document that places a symbol, and how many times.</summary>
/// <param name="Document">The animation, shot or standalone document.</param>
/// <param name="Owner">The character it belongs to, or null for a loose document.</param>
/// <param name="Placements">How many placements of the symbol it holds, across every frame and layer.</param>
public sealed record SymbolUse(DocumentRef Document, string? Owner, int Placements);

/// <summary>One symbol that holds another, and how many times (Q289).</summary>
public sealed record SymbolHolder(string SymbolId, string Name, int Placements);

/// <summary>Everywhere in a project one symbol is placed.</summary>
public sealed record SymbolUsage(string SymbolId, IReadOnlyList<SymbolUse> Documents)
{
    /// <summary>The symbols that hold this one directly. Empty for most.</summary>
    public IReadOnlyList<SymbolHolder> HeldBy { get; init; } = [];

    public int Total => Documents.Sum(d => d.Placements) + HeldBy.Sum(h => h.Placements);

    /// <summary>Nothing in the project places or holds this. Safe to delete.</summary>
    public bool IsUnused => Documents.Count == 0 && HeldBy.Count == 0;
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
/// Two kinds of edge: symbol → document, and symbol → the symbols that hold
/// it (Q289). The second is direct only. The knight holds the hand, the hand
/// holds the sword, and the sword reports the hand, which is the one to open
/// to change it. It costs nothing to collect, since the project's symbols are
/// already in memory.
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

        var holders = HoldersIn(project);

        var result = new Dictionary<string, SymbolUsage>();
        // Every symbol in the project gets an entry, including the ones nothing
        // places. "Used nowhere" is the answer that makes a delete safe, and it
        // is not the same answer as "not in this dictionary".
        foreach (var symbolId in project.Symbols.Keys)
        {
            result[symbolId] = Build(symbolId, counts, refs, project, holders);
        }
        // And any symbol placed but no longer in the project — a placement left
        // behind by a delete. Reporting those is how they get found.
        foreach (var symbolId in counts.Keys)
        {
            if (!result.ContainsKey(symbolId)) result[symbolId] = Build(symbolId, counts, refs, project, holders);
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
        Project project,
        Dictionary<string, List<SymbolHolder>> holders)
    {
        var heldBy = holders.TryGetValue(symbolId, out var list) ? list : [];
        if (!counts.TryGetValue(symbolId, out var perDoc)) return new SymbolUsage(symbolId, []) { HeldBy = heldBy };
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
        return new SymbolUsage(symbolId, uses) { HeldBy = heldBy };
    }

    /// <summary>placed symbol id → the project symbols holding it directly.</summary>
    private static Dictionary<string, List<SymbolHolder>> HoldersIn(Project project)
    {
        var holders = new Dictionary<string, List<SymbolHolder>>();
        foreach (var holder in project.Symbols.Values)
        {
            var counts = new Dictionary<string, int>();
            var seen = new HashSet<string>();
            // Masks too: a mask is rendered through the same pass and can place.
            var drawings = holder.Layers
                .SelectMany(l => l.Cels.Select(c => c.Frame).Append(l.Mask?.Frame))
                .OfType<Frame>();
            foreach (var frame in drawings)
            {
                // A held cel names the same drawing again; count it once.
                if (!seen.Add(frame.Id)) continue;
                if (frame.Placements is not { Count: > 0 } placements) continue;
                foreach (var placement in placements)
                {
                    counts[placement.SymbolId] = counts.GetValueOrDefault(placement.SymbolId) + 1;
                }
            }
            foreach (var (placed, n) in counts)
            {
                if (!holders.TryGetValue(placed, out var list)) holders[placed] = list = [];
                list.Add(new SymbolHolder(holder.Id, holder.Name, n));
            }
        }
        foreach (var list in holders.Values)
        {
            list.Sort((a, b) => b.Placements != a.Placements
                ? b.Placements.CompareTo(a.Placements)
                : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        }
        return holders;
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
            .OfType<Frame>();

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

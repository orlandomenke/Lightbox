using System.Collections.Concurrent;
using Lightbox.Core.Documents;

namespace Lightbox.Raster;

/// <summary>
/// Symbols available to render, keyed by id — the fifth entry in a table that
/// already had four rows in it.
/// </summary>
/// <remarks>
/// <para>
/// The same arrangement as <see cref="PaletteRegistry"/> and
/// <see cref="ClipRegionRegistry"/>, and for the same reason: a placement
/// records <em>which symbol</em> rather than a copy of it, so editing the
/// symbol changes every placement of it. That is Pillar 3's one promise, and
/// it only works if resolution happens at render time.
/// </para>
/// <para>
/// Overwrites rather than TryAdd, like the palette and unlike the clip
/// registry. A clip region is content-hashed and immutable; a symbol is meant
/// to be edited, and the whole feature is that the new version wins.
/// </para>
/// </remarks>
public static class SymbolRegistry
{
    private static readonly ConcurrentDictionary<string, Symbol> Symbols = new();

    /// <summary>
    /// Ids that were asked for and were not here.
    /// </summary>
    /// <remarks>
    /// Reported rather than substituted. A missing asset that renders as a
    /// pink box gets exported by somebody in a hurry, so the render draws
    /// nothing at all and the fact lands here instead — where a panel can say
    /// so out loud without ever reaching a pixel.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, byte> Missing = new();

    public static void Register(IReadOnlyDictionary<string, Symbol> symbols)
    {
        foreach (var (id, symbol) in symbols) Register(id, symbol);
    }

    public static void Register(string id, Symbol symbol)
    {
        if (id.Length == 0) return;
        Symbols[id] = symbol;
        // It resolves now, so the complaint about it is stale.
        Missing.TryRemove(id, out _);
        // Q173: a symbol captured from a marquee carries the clip regions its
        // own strokes reference, because clip regions otherwise come from the
        // *document* and a symbol is placed into documents it was not made in.
        // Registered here rather than at render time so there is one moment a
        // symbol becomes resolvable and everything it needs arrives together.
        if (symbol.ClipRegions is { Count: > 0 } regions)
        {
            ClipRegionRegistry.Register(regions);
        }
    }

    /// <summary>Register under the symbol's own id.</summary>
    public static void Register(Symbol symbol) => Register(symbol.Id, symbol);

    /// <summary>
    /// Point the registry at exactly one project's symbols, dropping anything
    /// else — <see cref="Register(IReadOnlyDictionary{string, Symbol})"/> only
    /// ever adds, so a deleted symbol would keep resolving and a closed
    /// project would keep colouring the next one's art.
    /// </summary>
    public static void Reset(IReadOnlyDictionary<string, Symbol> symbols)
    {
        Clear();
        Register(symbols);
    }

    public static Symbol? Resolve(string id) => Symbols.GetValueOrDefault(id);

    /// <summary>
    /// Whether the symbol <paramref name="fromId"/> is <paramref name="toId"/>,
    /// or places it at any depth (B487).
    /// </summary>
    /// <remarks>
    /// Asked before a placement is made inside a symbol, where a yes means the
    /// placement would close a loop. A walk with a visited set rather than a
    /// recursion, so a loop that already exists ends the walk instead of the
    /// process.
    /// </remarks>
    public static bool Reaches(string fromId, string toId)
    {
        if (fromId == toId) return true;
        var seen = new HashSet<string> { fromId };
        var pending = new Stack<string>();
        pending.Push(fromId);
        while (pending.TryPop(out var id))
        {
            if (Resolve(id) is not { } symbol) continue;
            foreach (var frame in DrawingsOf(symbol))
            {
                if (frame.Placements is not { } placements) continue;
                for (var p = 0; p < placements.Count; p++)
                {
                    var placed = placements[p].SymbolId;
                    if (placed == toId) return true;
                    if (seen.Add(placed)) pending.Push(placed);
                }
            }
        }
        return false;
    }

    /// <summary>
    /// How deep symbols may nest and still draw (Q289): a symbol holding one
    /// that holds one, sixteen times over.
    /// </summary>
    /// <remarks>
    /// Generous for an artist, whose deepest real case is a prop in a hand on
    /// a character in a crowd, and small for a stack. With loops refused
    /// (B487) every nesting is finite, but a file can hold a chain thousands
    /// long, and each level costs the render several frames of recursion.
    /// </remarks>
    public const int MaxNesting = 16;

    /// <summary>
    /// The longest chain of placements under a symbol: 0 for one that places
    /// nothing. Counting stops at <see cref="MaxNesting"/> + 1, which means
    /// too deep to draw.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A fact about the symbol, not about the render in progress.</b>
    /// Counting as the render descends would cut a symbol differently
    /// depending on where it was first drawn from, and the cache would keep
    /// whichever came first.
    /// </para>
    /// <para>
    /// Level by level rather than recursively, so a chain far past the limit
    /// costs the limit's worth of levels and no stack. A symbol that holds
    /// itself adds nothing, because it draws nothing.
    /// </para>
    /// </remarks>
    public static int NestingDepth(Symbol symbol)
    {
        var level = new List<Symbol> { symbol };
        for (var depth = 0; depth <= MaxNesting; depth++)
        {
            var seen = new HashSet<string>();
            var next = new List<Symbol>();
            foreach (var holder in level)
            {
                foreach (var frame in DrawingsOf(holder))
                {
                    if (frame.Placements is not { } placements) continue;
                    for (var p = 0; p < placements.Count; p++)
                    {
                        var placed = placements[p].SymbolId;
                        if (!seen.Add(placed)) continue;
                        if (Resolve(placed) is { } inner && !HoldsItself(inner)) next.Add(inner);
                    }
                }
            }
            if (next.Count == 0) return depth;
            level = next;
        }
        return MaxNesting + 1;
    }

    /// <summary>Whether a symbol nests too deep to draw (Q289).</summary>
    public static bool NestsTooDeep(Symbol symbol) => NestingDepth(symbol) > MaxNesting;

    /// <summary>
    /// Whether a symbol places itself, directly or through others: one that
    /// can never finish rendering (B487).
    /// </summary>
    /// <remarks>
    /// <b>Every drawing, not the one being drawn</b>: hidden layers, masks and
    /// cels past the end all count. A loop on any of them blanks the whole
    /// symbol, which is wider than a render strictly needs. That is on purpose.
    /// It agrees with what placing refuses, and an answer that changed with the
    /// frame would make a symbol flicker in and out along the timeline.
    /// </remarks>
    public static bool HoldsItself(Symbol symbol)
    {
        foreach (var frame in DrawingsOf(symbol))
        {
            if (frame.Placements is not { } placements) continue;
            for (var p = 0; p < placements.Count; p++)
            {
                if (Reaches(placements[p].SymbolId, symbol.Id)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Every drawing a symbol renders from, masks included: a mask is rendered
    /// through the same rasterizer and stamps its placements too.
    /// </summary>
    /// <remarks>
    /// By index rather than by enumerator. A render off the UI thread walks
    /// symbols other than the one it draws, and an enumerator over a list the
    /// artist is editing throws rather than reading a moment late.
    /// </remarks>
    private static IEnumerable<Frame> DrawingsOf(Symbol symbol)
    {
        var layers = symbol.Layers;
        for (var l = 0; l < layers.Count; l++)
        {
            var layer = layers[l];
            var cels = layer.Cels;
            for (var c = 0; c < cels.Count; c++)
            {
                if (cels[c].Frame is { } frame) yield return frame;
            }
            if (layer.Mask is { } mask) yield return mask.Frame;
        }
    }

    /// <summary>Ids a render asked for and could not find, in no particular order.</summary>
    public static IReadOnlyCollection<string> Unresolved => Missing.Keys.ToList();

    internal static void NoteUnresolved(string id)
    {
        if (id.Length > 0) Missing.TryAdd(id, 0);
    }

    /// <summary>Forget the complaints without forgetting the symbols.</summary>
    public static void ForgetUnresolved() => Missing.Clear();

    /// <summary>
    /// Drop everything — a new document must not see the last one's symbols.
    /// </summary>
    /// <remarks>
    /// Takes the render cache with it. The cache is keyed by symbol version so
    /// an edit invalidates itself, but a <em>different project</em> can reuse
    /// an id, and a stale bitmap under a reused id is the one case the version
    /// cannot catch.
    /// </remarks>
    public static void Clear()
    {
        Symbols.Clear();
        Missing.Clear();
        SymbolRasterizer.ClearCache();
    }
}

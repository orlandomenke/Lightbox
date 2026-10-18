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
    /// Worked out with the loop check in one pass (<see cref="Draws"/>), with
    /// no recursion, so a chain far past the limit costs a walk of its length
    /// and no stack. A symbol that holds itself adds nothing, because it draws
    /// nothing.
    /// </para>
    /// </remarks>
    public static int NestingDepth(Symbol symbol) => Math.Min(Shape(symbol).Depth, MaxNesting + 1);

    /// <summary>Whether a symbol nests too deep to draw (Q289).</summary>
    public static bool NestsTooDeep(Symbol symbol) => NestingDepth(symbol) > MaxNesting;

    /// <summary>
    /// Every registered symbol that holds <paramref name="symbolId"/>, at any
    /// depth: the ones whose picture changes when it does (Q289).
    /// </summary>
    /// <remarks>
    /// Walked upward from a reverse map built here, iteratively. Asked once per
    /// edit, never per render, so building the map each time costs one pass
    /// over the registered placements.
    /// </remarks>
    public static HashSet<string> HoldersOf(string symbolId)
    {
        var holders = ReverseMap();
        var found = new HashSet<string>();
        var pending = new Stack<string>();
        pending.Push(symbolId);
        while (pending.TryPop(out var id))
        {
            if (!holders.TryGetValue(id, out var direct)) continue;
            foreach (var holder in direct)
            {
                if (found.Add(holder)) pending.Push(holder);
            }
        }
        found.Remove(symbolId);
        return found;
    }

    /// <summary>
    /// How far down <paramref name="symbolId"/> sits inside the symbols that
    /// hold it: 0 when nothing holds it. Counting stops at
    /// <see cref="MaxNesting"/> + 1 (Q289).
    /// </summary>
    /// <remarks>
    /// The other half of what a drop has to ask. Placing a symbol of depth d
    /// into a host this deep makes the top of the stack this + 1 + d deep, and
    /// the top is what stops drawing.
    /// </remarks>
    public static int HeldDepth(string symbolId)
    {
        var holders = ReverseMap();
        var level = new HashSet<string> { symbolId };
        for (var depth = 0; depth <= MaxNesting; depth++)
        {
            var next = new HashSet<string>();
            foreach (var id in level)
            {
                if (!holders.TryGetValue(id, out var direct)) continue;
                foreach (var holder in direct)
                {
                    // A holder in a loop draws nothing, so it holds nothing up.
                    if (Resolve(holder) is { } symbol && !HoldsItself(symbol)) next.Add(holder);
                }
            }
            if (next.Count == 0) return depth;
            level = next;
        }
        return MaxNesting + 1;
    }

    /// <summary>placed symbol id → the registered symbols placing it directly.</summary>
    private static Dictionary<string, HashSet<string>> ReverseMap()
    {
        var map = new Dictionary<string, HashSet<string>>();
        foreach (var (id, symbol) in Symbols)
        {
            foreach (var frame in DrawingsOf(symbol))
            {
                if (frame.Placements is not { } placements) continue;
                for (var p = 0; p < placements.Count; p++)
                {
                    var placed = placements[p].SymbolId;
                    if (!map.TryGetValue(placed, out var set)) map[placed] = set = [];
                    set.Add(id);
                }
            }
        }
        return map;
    }

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
    public static bool HoldsItself(Symbol symbol) => Shape(symbol).Loops;

    /// <summary>
    /// Whether a symbol draws at all: it holds no loop of itself and nests no
    /// deeper than <see cref="MaxNesting"/>. One walk for both (Q289).
    /// </summary>
    public static bool Draws(Symbol symbol)
    {
        var (loops, depth) = Shape(symbol);
        return !loops && depth <= MaxNesting;
    }

    /// <summary>
    /// Both structural answers about a symbol in one pass: whether it lies on a
    /// loop of placements, and the longest chain of drawable placements under it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Tarjan's strongly connected components, iteratively</b>, over what
    /// the symbol reaches. Linear in the symbols and placements reached, with
    /// no recursion, so a chain loaded from a file thousands deep costs a walk
    /// of its length and no stack. The first version asked each question with
    /// its own walk and re-walked the reach for every symbol it met, which
    /// leak-hunter put at about 90,000 visits per stamp on such a file.
    /// </para>
    /// <para>
    /// Components come out children first, so each one's depth is known
    /// before any component that places it. A component that is a loop draws
    /// nothing, so it adds no depth to whatever places it, which is the rule
    /// the render follows.
    /// </para>
    /// <para>
    /// Not cached. The answer depends on every symbol reached, whose drawings
    /// are edited in place, and no version covers all of them. A cache keyed
    /// on anything less would serve a stale answer, which is how B487 began.
    /// </para>
    /// </remarks>
    private static (bool Loops, int Depth) Shape(Symbol symbol)
    {
        var index = new Dictionary<string, int>();
        var low = new Dictionary<string, int>();
        var onStack = new HashSet<string>();
        var stack = new Stack<string>();
        var children = new Dictionary<string, List<string>>();
        var depth = new Dictionary<string, int>();
        var loops = new HashSet<string>();
        var next = 0;

        List<string> ChildrenOf(string id)
        {
            if (children.TryGetValue(id, out var known)) return known;
            var list = new List<string>();
            var at = id == symbol.Id ? symbol : Resolve(id);
            if (at is not null)
            {
                var seen = new HashSet<string>();
                foreach (var frame in DrawingsOf(at))
                {
                    if (frame.Placements is not { } placements) continue;
                    for (var p = 0; p < placements.Count; p++)
                    {
                        var placed = placements[p].SymbolId;
                        if ((placed == symbol.Id || Resolve(placed) is not null) && seen.Add(placed)) list.Add(placed);
                    }
                }
            }
            return children[id] = list;
        }

        // Each frame of the walk: a symbol, and how many of its children are done.
        var walk = new Stack<(string Id, int Child)>();
        void Visit(string id)
        {
            index[id] = low[id] = next++;
            stack.Push(id);
            onStack.Add(id);
            walk.Push((id, 0));
        }

        Visit(symbol.Id);
        while (walk.TryPop(out var top))
        {
            var (id, child) = top;
            var kids = ChildrenOf(id);
            if (child < kids.Count)
            {
                walk.Push((id, child + 1));
                var kid = kids[child];
                if (!index.ContainsKey(kid)) Visit(kid);
                else if (onStack.Contains(kid)) low[id] = Math.Min(low[id], index[kid]);
                continue;
            }

            // All children done: fold this one's low link into its parent's.
            if (walk.TryPeek(out var parent)) low[parent.Id] = Math.Min(low[parent.Id], low[id]);
            if (low[id] != index[id]) continue;

            // The root of a component: pop it, and settle its depth.
            var members = new List<string>();
            string member;
            do
            {
                member = stack.Pop();
                onStack.Remove(member);
                members.Add(member);
            }
            while (member != id);

            var isLoop = members.Count > 1 || ChildrenOf(id).Contains(id);
            foreach (var m in members)
            {
                if (isLoop)
                {
                    loops.Add(m);
                    depth[m] = 0;
                    continue;
                }
                var deepest = 0;
                foreach (var kid in ChildrenOf(m))
                {
                    if (!loops.Contains(kid) && depth.TryGetValue(kid, out var d)) deepest = Math.Max(deepest, d + 1);
                }
                depth[m] = deepest;
            }
        }

        return (loops.Contains(symbol.Id), depth.GetValueOrDefault(symbol.Id));
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

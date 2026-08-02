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

using System.Collections.Concurrent;
using Lightbox.Core.Documents;

namespace Lightbox.Raster;

/// <summary>
/// Selections that strokes were painted under, keyed by their document id
/// (content-hashed, globally unique). Documents register their
/// <c>ClipRegions</c> on load/change; the brush engine resolves by id when
/// re-rendering a clipped stroke.
/// </summary>
public static class ClipRegionRegistry
{
    private static readonly ConcurrentDictionary<string, ClipRegion> Regions = new();

    public static void Register(IReadOnlyDictionary<string, ClipRegion> regions)
    {
        foreach (var (id, region) in regions)
        {
            Regions.TryAdd(id, region);
        }
    }

    public static void Register(string id, ClipRegion region) => Regions.TryAdd(id, region);

    public static ClipRegion? Resolve(string id) => Regions.GetValueOrDefault(id);
}
